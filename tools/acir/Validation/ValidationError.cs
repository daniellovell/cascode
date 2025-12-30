namespace Cascode.ACIR.Validation;

/// <summary>
/// Represents a single validation error or warning with location and suggestion.
/// </summary>
public sealed class ValidationError
{
    /// <summary>
    /// Error code identifying the rule violation (e.g., "EMIT-001", "ERC-002").
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Severity of this diagnostic.
    /// </summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Human-readable description of the error.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Location of the error (e.g., "device M_in", "net tnode").
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Actionable suggestion for fixing the error.
    /// </summary>
    public string? Suggestion { get; init; }

    /// <summary>
    /// Formats the error for display.
    /// </summary>
    public override string ToString()
    {
        var prefix = Severity switch
        {
            ValidationSeverity.Error => "Error",
            ValidationSeverity.Warning => "Warning",
            ValidationSeverity.Info => "Info",
            _ => "Unknown",
        };

        var result = $"{prefix}: [{Code}] {Message}";

        if (!string.IsNullOrEmpty(Location))
        {
            result += $"\n  Location: {Location}";
        }

        if (!string.IsNullOrEmpty(Suggestion))
        {
            result += $"\n  Suggestion: {Suggestion}";
        }

        return result;
    }
}
