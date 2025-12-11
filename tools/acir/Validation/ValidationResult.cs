using System.Collections.Generic;
using System.Linq;

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
}
