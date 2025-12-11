using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cascode.ACIR.Validation;

/// <summary>
/// Result of validating an ACIR circuit, containing all errors and warnings.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// All validation diagnostics (errors, warnings, info).
    /// </summary>
    public List<ValidationError> Errors { get; } = new();

    /// <summary>
    /// True if validation passed (no errors, warnings are allowed).
    /// </summary>
    public bool IsValid => !Errors.Any(e => e.Severity == ValidationSeverity.Error);

    /// <summary>
    /// True if there are any errors.
    /// </summary>
    public bool HasErrors => Errors.Any(e => e.Severity == ValidationSeverity.Error);

    /// <summary>
    /// True if there are any warnings.
    /// </summary>
    public bool HasWarnings => Errors.Any(e => e.Severity == ValidationSeverity.Warning);

    /// <summary>
    /// Count of errors only.
    /// </summary>
    public int ErrorCount => Errors.Count(e => e.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Count of warnings only.
    /// </summary>
    public int WarningCount => Errors.Count(e => e.Severity == ValidationSeverity.Warning);

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
        Errors.Add(new ValidationError
        {
            Code = code,
            Severity = ValidationSeverity.Error,
            Message = message,
            Location = location,
            Suggestion = suggestion
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
        Errors.Add(new ValidationError
        {
            Code = code,
            Severity = ValidationSeverity.Warning,
            Message = message,
            Location = location,
            Suggestion = suggestion
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
        Errors.Add(new ValidationError
        {
            Code = code,
            Severity = ValidationSeverity.Info,
            Message = message,
            Location = location
        });
    }

    /// <summary>
    /// Merges another validation result into this one.
    /// </summary>
    /// <param name="other">Result to merge.</param>
    public void Merge(ValidationResult other)
    {
        Errors.AddRange(other.Errors);
    }

    /// <summary>
    /// Gets only the errors (excludes warnings and info).
    /// </summary>
    public IEnumerable<ValidationError> GetErrors()
        => Errors.Where(e => e.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Gets only the warnings.
    /// </summary>
    public IEnumerable<ValidationError> GetWarnings()
        => Errors.Where(e => e.Severity == ValidationSeverity.Warning);

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
