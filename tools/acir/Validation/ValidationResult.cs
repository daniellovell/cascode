using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cascode.ACIR.Validation;

/// <summary>
/// Result of validating an ACIR circuit, containing validation diagnostics.
/// </summary>
public sealed class ValidationResult
{
    private readonly List<ValidationError> _diagnostics = new();

    /// <summary>
    /// All validation diagnostics (errors, warnings, info).
    /// </summary>
    public IReadOnlyList<ValidationError> Diagnostics { get; }

    public ValidationResult()
    {
        Diagnostics = _diagnostics.AsReadOnly();
    }

    /// <summary>
    /// True if validation passed (no errors, warnings are allowed).
    /// </summary>
    public bool IsValid => !_diagnostics.Any(e => e.Severity == ValidationSeverity.Error);

    /// <summary>
    /// True if there are any errors.
    /// </summary>
    public bool HasErrors => _diagnostics.Any(e => e.Severity == ValidationSeverity.Error);

    /// <summary>
    /// True if there are any warnings.
    /// </summary>
    public bool HasWarnings => _diagnostics.Any(e => e.Severity == ValidationSeverity.Warning);

    /// <summary>
    /// Count of errors only.
    /// </summary>
    public int ErrorCount => _diagnostics.Count(e => e.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Count of warnings only.
    /// </summary>
    public int WarningCount => _diagnostics.Count(e => e.Severity == ValidationSeverity.Warning);

    /// <summary>
    /// Creates a successful validation result with no errors.
    /// </summary>
    public static ValidationResult Success() => new();

    /// <summary>
    /// Adds an error to the result.
    /// </summary>
    /// <param name="code">Error code (e.g., "EMIT-001").</param>
    /// <param name="message">Error message.</param>
    /// <param name="location">Optional location string.</param>
    /// <param name="suggestion">Optional suggestion for fixing.</param>
    public void AddError(string code, string message, string? location = null, string? suggestion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code, nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

        _diagnostics.Add(new ValidationError
        {
            Code = code,
            Severity = ValidationSeverity.Error,
            Message = message,
            Location = NormalizeOptional(location),
            Suggestion = NormalizeOptional(suggestion)
        });
    }

    /// <summary>
    /// Adds a warning to the result.
    /// </summary>
    /// <param name="code">Warning code (e.g., "ERC-005").</param>
    /// <param name="message">Warning message.</param>
    /// <param name="location">Optional location string.</param>
    /// <param name="suggestion">Optional suggestion for fixing.</param>
    public void AddWarning(string code, string message, string? location = null, string? suggestion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code, nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

        _diagnostics.Add(new ValidationError
        {
            Code = code,
            Severity = ValidationSeverity.Warning,
            Message = message,
            Location = NormalizeOptional(location),
            Suggestion = NormalizeOptional(suggestion)
        });
    }

    /// <summary>
    /// Adds an info message to the result.
    /// </summary>
    /// <param name="code">Info code.</param>
    /// <param name="message">Info message.</param>
    /// <param name="location">Optional location string.</param>
    public void AddInfo(string code, string message, string? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code, nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

        _diagnostics.Add(new ValidationError
        {
            Code = code,
            Severity = ValidationSeverity.Info,
            Message = message,
            Location = NormalizeOptional(location)
        });
    }

    /// <summary>
    /// Merges another validation result into this one.
    /// </summary>
    /// <param name="other">Result to merge.</param>
    public void Merge(ValidationResult other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _diagnostics.AddRange(other._diagnostics);
    }

    /// <summary>
    /// Gets only the errors (excludes warnings and info).
    /// </summary>
    public IEnumerable<ValidationError> GetErrors()
        => _diagnostics.Where(e => e.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Gets only the warnings.
    /// </summary>
    public IEnumerable<ValidationError> GetWarnings()
        => _diagnostics.Where(e => e.Severity == ValidationSeverity.Warning);

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Converts the validation result to a JSON string for machine-readable output.
    /// </summary>
    /// <param name="exitCode">The exit code that will be returned by the command.</param>
    /// <returns>JSON string representation of the validation result.</returns>
    public string ToJson(int exitCode)
    {
        var output = new ValidationJsonOutput
        {
            Success = IsValid,
            ExitCode = exitCode,
            Errors = GetErrors().Select(e => new ValidationErrorJson
            {
                Code = e.Code,
                Severity = "error",
                Message = e.Message,
                Location = e.Location,
                Suggestion = e.Suggestion
            }).ToList(),
            Warnings = GetWarnings().Select(e => new ValidationErrorJson
            {
                Code = e.Code,
                Severity = "warning",
                Message = e.Message,
                Location = e.Location,
                Suggestion = e.Suggestion
            }).ToList(),
            Summary = new ValidationSummaryJson
            {
                ErrorCount = ErrorCount,
                WarningCount = WarningCount
            }
        };

        return JsonSerializer.Serialize(output, ValidationJsonOutput.SerializerOptions);
    }
}

/// <summary>
/// JSON output model for validation results.
/// </summary>
internal sealed class ValidationJsonOutput
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; }

    [JsonPropertyName("errors")]
    public List<ValidationErrorJson> Errors { get; init; } = new();

    [JsonPropertyName("warnings")]
    public List<ValidationErrorJson> Warnings { get; init; } = new();

    [JsonPropertyName("summary")]
    public ValidationSummaryJson Summary { get; init; } = new();
}

/// <summary>
/// JSON model for a single validation error or warning.
/// </summary>
internal sealed class ValidationErrorJson
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("location")]
    public string? Location { get; init; }

    [JsonPropertyName("suggestion")]
    public string? Suggestion { get; init; }
}

/// <summary>
/// JSON model for validation summary counts.
/// </summary>
internal sealed class ValidationSummaryJson
{
    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; init; }

    [JsonPropertyName("warningCount")]
    public int WarningCount { get; init; }
}
