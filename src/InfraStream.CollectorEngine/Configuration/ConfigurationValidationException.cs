namespace InfraStream.CollectorEngine.Configuration;

/// <summary>
/// Thrown when a configuration section is invalid and the engine cannot
/// be constructed. Carries a list of human-readable error messages.
/// </summary>
/// <remarks>
/// Intended to be caught in the application entry point and printed to
/// <see cref="Console.Error"/> followed by a non-zero exit code, so that
/// the host fails fast with a clear message instead of an unhandled
/// exception stack trace.
/// </remarks>
public sealed class ConfigurationValidationException : Exception
{
    /// <summary>Validation error messages, one per line.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Initializes a new instance with the given validation errors.
    /// </summary>
    /// <param name="section">Configuration section that failed validation.</param>
    /// <param name="errors">Human-readable error messages.</param>
    public ConfigurationValidationException(string section, IReadOnlyList<string> errors)
        : base(BuildMessage(section, errors))
    {
        Errors = errors;
    }

    private static string BuildMessage(string section, IReadOnlyList<string> errors)
    {
        var lines = new List<string>(errors.Count + 1)
        {
            $"Configuration section '{section}' is invalid:"
        };

        foreach (var error in errors)
            lines.Add("  - " + error);

        return string.Join(Environment.NewLine, lines);
    }
}
