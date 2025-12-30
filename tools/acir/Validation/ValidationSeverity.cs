namespace Cascode.ACIR.Validation;

/// <summary>
/// Severity level for validation diagnostics.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Informational message that does not affect validation result.
    /// </summary>
    Info,

    /// <summary>
    /// Warning that indicates a potential issue but does not block emission.
    /// </summary>
    Warning,

    /// <summary>
    /// Error that blocks emission or indicates an invalid circuit.
    /// </summary>
    Error,
}
