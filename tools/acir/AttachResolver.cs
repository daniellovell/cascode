using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Parser;

namespace Cascode.ACIR;

/// <summary>
/// Resolves attach statements to determine net connectivity using union-find.
/// </summary>
public sealed class AttachResolver
{
    private readonly ACIRDocument _document;
    private readonly Dictionary<string, TraitDefinition> _traitsByName;
    private readonly List<Diagnostic> _diagnostics = new();

    /// <summary>
    /// Initializes a new AttachResolver for the given document.
    /// </summary>
    public AttachResolver(ACIRDocument document)
    {
        _document = document;
        _traitsByName = document.Traits.ToDictionary(t => t.Name);
    }

    /// <summary>
    /// Resolves all attach statements in the document, returning the resolved
    /// net connectivity map and any diagnostics.
    /// </summary>
    public AttachResolutionResult Resolve()
    {
        var result = new AttachResolutionResult();

        foreach (var circuit in _document.Circuits)
        {
            if (circuit.Fill is null)
                continue;

            var circuitResult = ResolveCircuit(circuit);
            result.CircuitResults[circuit.Name] = circuitResult;
            result.Diagnostics.AddRange(circuitResult.Diagnostics);
        }

        return result;
    }

    /// <summary>
    /// Resolves attach statements for a single circuit.
    /// </summary>
    private CircuitResolutionResult ResolveCircuit(Circuit circuit)
    {
        var result = new CircuitResolutionResult();
        var unionFind = new UnionFind<string>();
        var netDomains = new Dictionary<string, string>();

        // Initialize atoms from declared nets
        foreach (var net in circuit.Fill!.Nets)
        {
            unionFind.MakeSet(net.Id);
            netDomains[net.Id] = net.Domain;
        }

        // Initialize atoms from supplies and grounds
        foreach (var supply in circuit.Supplies)
        {
            unionFind.MakeSet(supply);
            netDomains[supply] = "power";
        }
        foreach (var ground in circuit.Grounds)
        {
            unionFind.MakeSet(ground);
            netDomains[ground] = "ground";
        }

        // Initialize atoms from port names
        foreach (var port in circuit.Ports)
        {
            unionFind.MakeSet(port.Name);
            netDomains[port.Name] = port.Type;
        }

        // Process device bindings
        foreach (var device in circuit.Fill.Devices)
        {
            foreach (var binding in device.Bindings)
            {
                // Terminal binding format: terminal -> net
                var netName = binding.Value;
                if (!unionFind.Contains(netName))
                {
                    unionFind.MakeSet(netName);
                    netDomains[netName] = "analog"; // Default domain
                }
            }
        }

        // Process instance bindings
        foreach (var inst in circuit.Fill.Instances)
        {
            foreach (var binding in inst.Bindings)
            {
                var netName = binding.Value;
                if (!unionFind.Contains(netName))
                {
                    unionFind.MakeSet(netName);
                    netDomains[netName] = "analog";
                }
            }
        }

        // Process explicit connect statements
        foreach (var conn in circuit.Fill.Connections)
        {
            var fromNet = conn.From;
            var toNet = conn.To;

            if (!unionFind.Contains(fromNet))
            {
                unionFind.MakeSet(fromNet);
                netDomains[fromNet] = "analog";
            }
            if (!unionFind.Contains(toNet))
            {
                unionFind.MakeSet(toNet);
                netDomains[toNet] = "analog";
            }

            // Check domain compatibility before union
            var fromDomain = netDomains.GetValueOrDefault(fromNet, "analog");
            var toDomain = netDomains.GetValueOrDefault(toNet, "analog");
            if (!AreDomainsCompatible(fromDomain, toDomain))
            {
                result.Diagnostics.Add(
                    new Diagnostic(
                        $"ACIR0024: Incompatible domain merge '{fromNet}' ({fromDomain}) -> '{toNet}' ({toDomain})",
                        DiagnosticSeverity.Error,
                        circuit.Name,
                        1,
                        1
                    )
                );
            }

            unionFind.Union(fromNet, toNet);
        }

        // Process attach statements
        foreach (var attach in circuit.Fill.Attaches)
        {
            var attachResult = ProcessAttach(
                attach,
                circuit,
                unionFind,
                netDomains,
                result.Diagnostics
            );
            if (attachResult is not null)
            {
                result.AttachBindings[attach] = attachResult;
            }
        }

        // Build final net equivalence classes
        var equivalenceClasses = new Dictionary<string, List<string>>();
        foreach (var net in unionFind.GetAllElements())
        {
            var rep = unionFind.Find(net);
            if (!equivalenceClasses.ContainsKey(rep))
            {
                equivalenceClasses[rep] = new List<string>();
            }
            equivalenceClasses[rep].Add(net);
        }

        // Select representative for each class
        foreach (var kvp in equivalenceClasses)
        {
            var rep = SelectRepresentative(kvp.Value, circuit);
            result.NetEquivalences[rep] = kvp.Value;
            foreach (var net in kvp.Value)
            {
                result.NetToRepresentative[net] = rep;
            }
        }

        return result;
    }

    /// <summary>
    /// Processes a single attach statement, creating net connections.
    /// </summary>
    private Dictionary<string, string>? ProcessAttach(
        AttachStatement attach,
        Circuit circuit,
        UnionFind<string> unionFind,
        Dictionary<string, string> netDomains,
        List<Diagnostic> diagnostics
    )
    {
        // Parse Via clause: "SourceTrait::TargetTrait"
        var viaParts = attach.Via.Split("::");
        if (viaParts.Length != 2)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0022: Malformed via clause '{attach.Via}'",
                    DiagnosticSeverity.Error,
                    circuit.Name,
                    1,
                    1
                )
            );
            return null;
        }

        var sourceTraitName = viaParts[0];
        var targetTraitName = viaParts[1];

        // Look up source trait
        if (!_traitsByName.TryGetValue(sourceTraitName, out var sourceTrait))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0021: Undefined trait '{sourceTraitName}' in attach via clause",
                    DiagnosticSeverity.Error,
                    circuit.Name,
                    1,
                    1
                )
            );
            return null;
        }

        // Find connector from source trait to target trait
        var connector = sourceTrait.Connectors.FirstOrDefault(c =>
            c.TargetTrait == targetTraitName
        );
        if (connector is null)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0023: No connector from '{sourceTraitName}' to '{targetTraitName}'",
                    DiagnosticSeverity.Error,
                    circuit.Name,
                    1,
                    1
                )
            );
            return null;
        }

        // Look up target trait for domain validation
        _traitsByName.TryGetValue(targetTraitName, out var targetTrait);

        // Validate override ports exist in connector
        if (attach.Overrides is not null)
        {
            var connectorSourcePorts = connector.Mappings.Select(m => m.SourcePort).ToHashSet();
            foreach (var ov in attach.Overrides)
            {
                if (!connectorSourcePorts.Contains(ov.SourcePort))
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"Override source port '{ov.SourcePort}' not found in connector {attach.Via}",
                            DiagnosticSeverity.Warning,
                            circuit.Name,
                            1,
                            1
                        )
                    );
                }
            }
        }

        // Apply connector mappings
        var bindings = new Dictionary<string, string>();
        var hasError = false;

        foreach (var mapping in connector.Mappings)
        {
            // Override with attach.Overrides if present
            var sourcePort = mapping.SourcePort;
            var targetPort = mapping.TargetPort;

            if (attach.Overrides is not null)
            {
                var overrideMapping = attach.Overrides.FirstOrDefault(o =>
                    o.SourcePort == sourcePort
                );
                if (overrideMapping is not null)
                {
                    targetPort = overrideMapping.TargetPort;
                }
            }

            // Domain validation: check source and target port domains match
            var sourcePortDomain =
                sourceTrait.Ports.FirstOrDefault(p => p.Name == sourcePort)?.Type ?? "analog";
            var targetPortDomain =
                targetTrait?.Ports.FirstOrDefault(p => p.Name == targetPort)?.Type ?? "analog";

            if (!AreDomainsCompatible(sourcePortDomain, targetPortDomain))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"ACIR0024: Domain mismatch in attach: {sourcePort} ({sourcePortDomain}) vs {targetPort} ({targetPortDomain})",
                        DiagnosticSeverity.Error,
                        circuit.Name,
                        1,
                        1
                    )
                );
                hasError = true;
            }

            // Generate net name for this connection
            var netName = attach.Anchor is not null
                ? $"{attach.Anchor}_{sourcePort}"
                : $"{attach.SourceInstance}_{attach.TargetInstance}_{sourcePort}";

            if (!unionFind.Contains(netName))
            {
                unionFind.MakeSet(netName);
                netDomains[netName] = sourcePortDomain;
            }

            bindings[sourcePort] = netName;
        }

        return hasError ? null : bindings;
    }

    /// <summary>
    /// Selects the representative net from an equivalence class.
    /// Priority: supply/ground > port > declared net > auto-generated.
    /// </summary>
    private string SelectRepresentative(List<string> nets, Circuit circuit)
    {
        // Check for supply/ground
        foreach (var net in nets)
        {
            if (circuit.Supplies.Contains(net) || circuit.Grounds.Contains(net))
            {
                return net;
            }
        }

        // Check for port
        foreach (var net in nets)
        {
            if (circuit.Ports.Any(p => p.Name == net))
            {
                return net;
            }
        }

        // Check for declared net
        if (circuit.Fill is not null)
        {
            foreach (var net in nets)
            {
                if (circuit.Fill.Nets.Any(n => n.Id == net))
                {
                    return net;
                }
            }
        }

        // Default to first net
        return nets.First();
    }

    /// <summary>
    /// Checks if two domains are compatible for merging.
    /// Per spec §3.13.4: "All endpoints in an equivalence class must have
    /// identical domains (exact matching, no supertype inference)."
    /// </summary>
    private static bool AreDomainsCompatible(string domain1, string domain2)
    {
        // Strict exact matching per spec
        return domain1 == domain2;
    }
}

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
    /// Maps attach statements to the generated port bindings.
    /// </summary>
    public Dictionary<AttachStatement, Dictionary<string, string>> AttachBindings { get; } = new();

    /// <summary>
    /// Diagnostics for this circuit's resolution.
    /// </summary>
    public List<Diagnostic> Diagnostics { get; } = new();
}

/// <summary>
/// Union-find data structure for net connectivity.
/// </summary>
internal sealed class UnionFind<T>
    where T : notnull
{
    private readonly Dictionary<T, T> _parent = new();
    private readonly Dictionary<T, int> _rank = new();

    public void MakeSet(T item)
    {
        if (_parent.ContainsKey(item))
            return;
        _parent[item] = item;
        _rank[item] = 0;
    }

    public bool Contains(T item) => _parent.ContainsKey(item);

    public T Find(T item)
    {
        if (!_parent.TryGetValue(item, out var parent))
            throw new ArgumentException($"Item '{item}' not in union-find");

        if (!EqualityComparer<T>.Default.Equals(parent, item))
        {
            _parent[item] = Find(parent); // Path compression
        }
        return _parent[item];
    }

    public void Union(T a, T b)
    {
        var rootA = Find(a);
        var rootB = Find(b);

        if (EqualityComparer<T>.Default.Equals(rootA, rootB))
            return;

        // Union by rank
        if (_rank[rootA] < _rank[rootB])
        {
            _parent[rootA] = rootB;
        }
        else if (_rank[rootA] > _rank[rootB])
        {
            _parent[rootB] = rootA;
        }
        else
        {
            _parent[rootB] = rootA;
            _rank[rootA]++;
        }
    }

    public IEnumerable<T> GetAllElements() => _parent.Keys;
}
