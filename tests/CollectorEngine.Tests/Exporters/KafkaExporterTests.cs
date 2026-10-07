using Confluent.Kafka;
using InfraStream.Core.Configuration;
using InfraStream.Core.Exporters;
using InfraStream.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CollectorEngine.Tests.Exporters;

public sealed class KafkaExporterTests : IDisposable
{
    private readonly string _spillDir;
    private readonly Mock<IProducer<byte[], byte[]>> _producer;
    private readonly KafkaExporterOptions _options;

    public KafkaExporterTests()
    {
        _spillDir = Path.Combine(Path.GetTempPath(), "infrastream-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_spillDir);

        _producer = new Mock<IProducer<byte[], byte[]>>(MockBehavior.Loose);

        _producer
            .Setup(p => p.Produce(
                It.IsAny<string>(),
                It.IsAny<Message<byte[], byte[]>>(),
                It.IsAny<Action<DeliveryReport<byte[], byte[]>>>()))
            .Verifiable();

        _producer
            .Setup(p => p.Flush(It.IsAny<TimeSpan>()))
            .Returns(0);

        _options = new KafkaExporterOptions
        {
            BootstrapServers = "test:9092",
            Topic = "telemetry-test",
            DeadLetterTopic = "telemetry-test-dlq",
            InMemoryBufferSize = 10,
            DiskSpillPath = _spillDir,
            MaxSpillFileSizeBytes = 1024 * 1024,
            FlushIntervalMs = 50,
            KafkaFlushTimeoutMs = 1000,
            BlockTimeoutMs = 500,
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_spillDir, recursive: true); }
        catch { /* best effort */ }
    }

    // ---- Constructor ----

    [Fact]
    public void Constructor_Throws_When_Options_Null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new KafkaExporter(null!, NullLogger<KafkaExporter>.Instance));
    }

    [Fact]
    public void Constructor_Throws_When_Logger_Null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new KafkaExporter(_options, null!));
    }

    [Fact]
    public void Constructor_Throws_When_BootstrapServers_Empty()
    {
        var bad = _options with { BootstrapServers = "" };
        Assert.Throws<ArgumentException>(() =>
            new KafkaExporter(bad, _producer.Object, NullLogger<KafkaExporter>.Instance));
    }

    [Fact]
    public void Constructor_Throws_When_Topic_Empty()
    {
        var bad = _options with { Topic = "" };
        Assert.Throws<ArgumentException>(() =>
            new KafkaExporter(bad, _producer.Object, NullLogger<KafkaExporter>.Instance));
    }

    [Fact]
    public void Constructor_Succeeds_With_Valid_Options()
    {
        using var exporter = new KafkaExporter(
            _options,
            _producer.Object,
            NullLogger<KafkaExporter>.Instance);

        Assert.NotNull(exporter);
        Assert.Equal("Kafka", exporter.Name);
    }

    // ---- ExportItem ----

    [Fact]
    public void ExportItem_Queues_Into_Buffer()
    {
        using var exporter = new KafkaExporter(
            _options,
            _producer.Object,
            NullLogger<KafkaExporter>.Instance);

        var item = CreateItem("hello");

        exporter.ExportItem(in item);

        Assert.Equal(1, exporter.Metrics.QueuedTotal);
        Assert.True(exporter.Metrics.BufferSize >= 0);
    }

    [Fact]
    public void ExportItem_Overflow_Spills_To_Disk()
    {
        // Buffer size 1 — second item overflows.
        var options = _options with { InMemoryBufferSize = 1 };

        using var exporter = new KafkaExporter(
            options,
            _producer.Object,
            NullLogger<KafkaExporter>.Instance);

        var item1 = CreateItem("one");
        var item2 = CreateItem("two");

        exporter.ExportItem(in item1);
        exporter.ExportItem(in item2);

        // One in buffer, one spilled.
        Assert.Equal(1, exporter.Metrics.QueuedTotal);
        Assert.Equal(1, exporter.Metrics.SpilledTotal);
        Assert.Equal(1, exporter.Metrics.BufferOverflowsTotal);
    }

    // ---- Flush ----

    [Fact]
    public void Flush_Calls_Producer_Flush()
    {
        using var exporter = new KafkaExporter(
            _options,
            _producer.Object,
            NullLogger<KafkaExporter>.Instance);

        exporter.Flush();

        _producer.Verify(
            p => p.Flush(It.IsAny<TimeSpan>()),
            Times.AtLeastOnce());
    }

    // ---- Dispose ----

    [Fact]
    public void Dispose_Is_Idempotent()
    {
        var exporter = new KafkaExporter(
            _options,
            _producer.Object,
            NullLogger<KafkaExporter>.Instance);

        exporter.Dispose();
        exporter.Dispose(); // should not throw

        _producer.Verify(p => p.Dispose(), Times.Once());
    }

    // ---- helpers ----

    private static TelemetryItem CreateItem(string message)
    {
        var messageBytes = System.Text.Encoding.UTF8.GetBytes(message);
        var levelBytes = System.Text.Encoding.UTF8.GetBytes("INFO");
        var componentBytes = System.Text.Encoding.UTF8.GetBytes("test");

        return new TelemetryItem(
            DateTime.UtcNow,
            levelBytes,
            messageBytes,
            componentBytes,
            ReadOnlyMemory<LogAttribute>.Empty,
            ReadOnlyMemory<byte>.Empty);
    }
}
