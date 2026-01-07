using System.Collections.Generic;
using System.Linq;
using Cascode.Parser;

namespace Cascode.ACIR;

/// <summary>
/// Result of attach resolution for the entire document.
/// </summary>
public sealed class AttachResolutionResult
{
    /// <summary>
    /// Resolution results per circuit.
    /// </summary>
    public Dictionary<string, CircuitResolutionResult> CircuitResults { get; } = new();

    /// <summary>
    /// All diagnostics from resolution.
    /// </summary>
    public List<Diagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Whether resolution completed without errors.
    /// </summary>
    public bool Success => !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// Result of attach resolution for a single circuit.
/// </summary>
public sealed class CircuitResolutionResult
{
    /// <summary>
    /// Maps each net to its representative in the equivalence class.
    /// </summary>
    public Dictionary<string, string> NetToRepresentative { get; } = new();

    /// <summary>
    /// Maps representative nets to all nets in their equivalence class.
    /// </summary>
    public Dictionary<string, List<string>> NetEquivalences { get; } = new();

    /// <summary>
    /// Maps terminal endpoints to their resolved net names.
    /// </summary>
    public Dictionary<string, string> TerminalToNet { get; } = new();

    /// <summary>
    /// Maps attach statements to the generated port bindings.
    /// </summary>
    public Dictionary<AttachStatement, Dictionary<string, string>> AttachBindings { get; } = new();

    /// <summary>
    /// Diagnostics for this circuit's resolution.
    /// </summary>
    public List<Diagnostic> Diagnostics { get; } = new();
}
