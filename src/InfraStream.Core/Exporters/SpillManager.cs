using System.Globalization;
using System.Text;

namespace InfraStream.Core.Exporters;

/// <summary>
/// Manages spill files on disk for the Kafka exporter.
/// </summary>
/// <remarks>
/// <para>
/// When the in-memory buffer is full, the exporter appends serialized
/// messages to a spill file (JSONL format — one JSON object per line).
/// Files are rotated when they exceed <c>maxFileSizeBytes</c>.
/// </para>
/// <para>
/// Files are named <c>spill-yyyyMMdd-HHmmss-{seq}.jsonl</c> so that
/// lexicographic sorting matches write order.
/// </para>
/// <para>
/// Writing uses <see cref="FileStream.Write(ReadOnlySpan{byte})"/> directly
/// to avoid UTF-16 conversions and per-item allocations. Reading uses a
/// <see cref="StreamReader"/> and allocates one string per line — this is
/// acceptable because reading happens on the recovery path, not the hot path.
/// </para>
/// </remarks>
internal sealed class SpillManager : IDisposable
{
    private const string FilePrefix = "spill-";
    private const string FileSuffix = ".jsonl";
    private const int MinFreeSpaceBytes = 10 * 1024 * 1024; // 10 MB
    private const int WriteBufferSize = 64 * 1024;

    private readonly string _directory;
    private readonly long _maxFileSizeBytes;
    private readonly object _sync = new();

    private FileStream? _currentStream;
    private string? _currentFilePath;
    private long _currentFileSize;
    private int _sequence;
    private bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="SpillManager"/>.
    /// </summary>
    /// <param name="directory">Directory for spill files (created if missing).</param>
    /// <param name="maxFileSizeBytes">Maximum size of a single spill file.</param>
    public SpillManager(string directory, long maxFileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory must not be empty.", nameof(directory));
        if (maxFileSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFileSizeBytes));

        _directory = directory;
        _maxFileSizeBytes = maxFileSizeBytes;

        Directory.CreateDirectory(_directory);
        _sequence = CountExistingFiles();
    }

    /// <summary>True if at least one spill file exists on disk.</summary>
    public bool HasData
    {
        get
        {
            lock (_sync)
            {
                return EnumerateSpillFiles().Any();
            }
        }
    }

    /// <summary>Number of spill files currently on disk.</summary>
    public long FileCount
    {
        get
        {
            lock (_sync)
            {
                return EnumerateSpillFiles().LongCount();
            }
        }
    }

    /// <summary>Total size of all spill files in bytes.</summary>
    public long TotalBytes
    {
        get
        {
            lock (_sync)
            {
                long total = 0;
                foreach (var file in EnumerateSpillFiles())
                {
                    try { total += new FileInfo(file).Length; }
                    catch (IOException) { /* file disappeared */ }
                }
                return total;
            }
        }
    }

    /// <summary>True if the disk has at least 10 MB of free space.</summary>
    public bool HasFreeSpace
    {
        get
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(_directory));
                if (string.IsNullOrEmpty(root))
                    return true;

                var drive = new DriveInfo(root);
                return drive.AvailableFreeSpace >= MinFreeSpaceBytes;
            }
            catch
            {
                // If we cannot determine free space, assume ok and let the write fail.
                return true;
            }
        }
    }

    /// <summary>
    /// Appends a serialized JSON message to the current spill file,
    /// rotating the file first if the next write would exceed the limit.
    /// </summary>
    /// <param name="json">The serialized JSON message (UTF-8).</param>
    public void Append(ReadOnlySpan<byte> json)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            // Account for the trailing newline.
            long upcomingSize = _currentFileSize + json.Length + 1;
            if (_currentStream is null || upcomingSize > _maxFileSizeBytes)
            {
                Rotate();
            }

            // Zero-alloc write — no UTF-16 conversion.
            _currentStream!.Write(json);
            _currentStream.WriteByte((byte)'\n');
            _currentStream.Flush();

            _currentFileSize += json.Length + 1;
        }
    }

    /// <summary>
    /// Returns the path of the oldest spill file, or <see langword="null"/>
    /// if there are no files on disk.
    /// </summary>
    public string? PeekOldestFile()
    {
        lock (_sync)
        {
            return EnumerateSpillFiles().FirstOrDefault();
        }
    }

    /// <summary>
    /// Reads a spill file line by line. Each yielded item is one JSON message.
    /// </summary>
    /// <remarks>
    /// This method allocates one string per line — acceptable because it runs
    /// on the recovery path, not on the hot path.
    /// </remarks>
    public static IEnumerable<ReadOnlyMemory<byte>> ReadFile(string path)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: WriteBufferSize);

        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;

            yield return Encoding.UTF8.GetBytes(line);
        }
    }

    /// <summary>
    /// Deletes a spill file after its messages have been produced.
    /// </summary>
    public void DeleteFile(string path)
    {
        lock (_sync)
        {
            try { File.Delete(path); }
            catch (IOException) { /* best effort */ }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;

            _currentStream?.Dispose();
            _currentStream = null;
            _currentFilePath = null;
            _currentFileSize = 0;
        }
    }

    // ---- private ----

    private void Rotate()
    {
        _currentStream?.Dispose();
        _currentStream = null;

        _sequence++;
        string fileName = string.Format(
            CultureInfo.InvariantCulture,
            "{0}{1:yyyyMMdd-HHmmss}-{2:D4}{3}",
            FilePrefix,
            DateTime.UtcNow,
            _sequence,
            FileSuffix);

        _currentFilePath = Path.Combine(_directory, fileName);
        _currentStream = new FileStream(
            _currentFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: WriteBufferSize);

        _currentFileSize = 0;
    }

    private IEnumerable<string> EnumerateSpillFiles()
    {
        if (!Directory.Exists(_directory))
            yield break;

        var files = Directory.GetFiles(_directory, FilePrefix + "*" + FileSuffix);
        Array.Sort(files, StringComparer.Ordinal); // lexicographic == write order
        foreach (var file in files)
            yield return file;
    }

    private int CountExistingFiles()
    {
        return EnumerateSpillFiles().Count();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SpillManager));
    }
}
