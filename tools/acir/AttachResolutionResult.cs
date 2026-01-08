using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Cascode.Parser;

namespace Cascode.ACIR;

/// <summary>
/// Result of attach resolution for the entire document.
/// </summary>
public sealed class AttachResolutionResult
{
    internal readonly Dictionary<string, CircuitResolutionResult> _circuitResults = new();
    internal readonly List<Diagnostic> _diagnostics = new();

    /// <summary>
    /// Resolution results per circuit.
    /// </summary>
    public IReadOnlyDictionary<string, CircuitResolutionResult> CircuitResults { get; }

    /// <summary>
    /// All diagnostics from resolution.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Whether resolution completed without errors.
    /// </summary>
    public bool Success => !_diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public AttachResolutionResult()
    {
        CircuitResults = new ReadOnlyDictionary<string, CircuitResolutionResult>(_circuitResults);
        Diagnostics = _diagnostics.AsReadOnly();
    }
}

/// <summary>
/// Result of attach resolution for a single circuit.
/// </summary>
public sealed class CircuitResolutionResult
{
    internal readonly Dictionary<string, string> _netToRepresentative = new();
    internal readonly Dictionary<string, List<string>> _netEquivalences = new();
    internal readonly Dictionary<string, string> _terminalToNet = new();
    internal readonly Dictionary<AttachStatement, Dictionary<string, string>> _attachBindings =
        new();
    internal readonly List<Diagnostic> _diagnostics = new();

    /// <summary>
    /// Maps each net to its representative in the equivalence class.
    /// </summary>
    public IReadOnlyDictionary<string, string> NetToRepresentative { get; }

    /// <summary>
    /// Maps representative nets to all nets in their equivalence class.
    /// </summary>
    public IReadOnlyDictionary<string, List<string>> NetEquivalences { get; }

    /// <summary>
    /// Maps terminal endpoints to their resolved net names.
    /// </summary>
    public IReadOnlyDictionary<string, string> TerminalToNet { get; }

    /// <summary>
    /// Maps attach statements to the generated port bindings.
    /// </summary>
    public IReadOnlyDictionary<AttachStatement, Dictionary<string, string>> AttachBindings { get; }

    /// <summary>
    /// Diagnostics for this circuit's resolution.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public CircuitResolutionResult()
    {
        NetToRepresentative = new ReadOnlyDictionary<string, string>(_netToRepresentative);
        NetEquivalences = new ReadOnlyDictionary<string, List<string>>(_netEquivalences);
        TerminalToNet = new ReadOnlyDictionary<string, string>(_terminalToNet);
        AttachBindings = new ReadOnlyDictionary<AttachStatement, Dictionary<string, string>>(
            _attachBindings
        );
        Diagnostics = _diagnostics.AsReadOnly();
    }
}
