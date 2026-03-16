using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Cascode.Language.Json;

/// <summary>
/// Converts between CascodeDocument and CascodeJsonDocument representations.
/// Only EL-level circuits are supported.
/// </summary>
public static class CascodeJsonConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Converts an CascodeDocument to JSON string representation.
    /// </summary>
    /// <param name="document">The Cascode document to convert.</param>
    /// <param name="circuitName">Optional circuit name. If null, uses first EL circuit.</param>
    /// <returns>JSON string representation of the circuit.</returns>
    /// <exception cref="ArgumentException">If no EL circuit is found.</exception>
    public static string ToJson(CascodeDocument document, string? circuitName = null)
    {
        var jsonDoc = ToJsonDocument(document, circuitName);
        return JsonSerializer.Serialize(jsonDoc, JsonOptions);
    }

    /// <summary>
    /// Converts an CascodeDocument to CascodeJsonDocument.
    /// </summary>
    /// <param name="document">The Cascode document to convert.</param>
    /// <param name="circuitName">Optional circuit name. If null, uses first EL circuit.</param>
    /// <returns>The JSON document representation.</returns>
    /// <exception cref="ArgumentException">If no EL circuit is found.</exception>
    public static CascodeJsonDocument ToJsonDocument(
        CascodeDocument document,
        string? circuitName = null
    )
    {
        var circuit = FindElCircuit(document, circuitName);
        if (circuit == null)
        {
            throw new ArgumentException(
                circuitName != null
                    ? $"EL-level circuit '{circuitName}' not found."
                    : "No EL-level circuit found in document."
            );
        }

        return ConvertCircuit(document, circuit);
    }

    /// <summary>
    /// Parses JSON string into an CascodeDocument with structured error handling.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <param name="filePath">Optional file path for diagnostic messages.</param>
    /// <returns>Read result containing the document and any diagnostics.</returns>
    public static CascodeReadResult FromJson(string json, string filePath = "<json>")
    {
        var diagnostics = new List<Diagnostic>();

        CascodeJsonDocument? jsonDoc;
        try
        {
            jsonDoc = JsonSerializer.Deserialize<CascodeJsonDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0009: JSON parse error: {ex.Message}",
                    DiagnosticSeverity.Error,
                    filePath,
                    1,
                    1
                )
            );
            return new CascodeReadResult { Document = null, Diagnostics = diagnostics };
        }

        if (jsonDoc == null)
        {
            diagnostics.Add(
                new Diagnostic(
                    "CAS0009: Failed to parse JSON document (null result)",
                    DiagnosticSeverity.Error,
                    filePath,
                    1,
                    1
                )
            );
            return new CascodeReadResult { Document = null, Diagnostics = diagnostics };
        }

        return FromJsonDocument(jsonDoc, filePath, diagnostics);
    }

    /// <summary>
    /// Converts CascodeJsonDocument back to CascodeDocument with structured error handling.
    /// </summary>
    /// <param name="jsonDoc">The JSON document to convert.</param>
    /// <param name="filePath">Optional file path for diagnostic messages.</param>
    /// <param name="diagnostics">Optional diagnostics list to append to.</param>
    /// <returns>Read result containing the document and any diagnostics.</returns>
    public static CascodeReadResult FromJsonDocument(
        CascodeJsonDocument jsonDoc,
        string filePath = "<json>",
        List<Diagnostic>? diagnostics = null
    )
    {
        diagnostics ??= new List<Diagnostic>();

        // Parse version with error handling
        int major,
            minor;
        try
        {
            (major, minor) = ParseVersion(jsonDoc.Version);
        }
        catch (FormatException ex)
        {
            diagnostics.Add(
                new Diagnostic($"CAS0002: {ex.Message}", DiagnosticSeverity.Error, filePath, 1, 1)
            );
            return new CascodeReadResult { Document = null, Diagnostics = diagnostics };
        }

        // Validate major version
        if (major != CascodeVersion.Major)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0007: Cascode major version {major} not supported. Expected major version {CascodeVersion.Major}.",
                    DiagnosticSeverity.Error,
                    filePath,
                    1,
                    1
                )
            );
            return new CascodeReadResult { Document = null, Diagnostics = diagnostics };
        }

        // Parse level with error handling - NO FALLBACK
        if (!Enum.TryParse<CascodeLevel>(jsonDoc.Circuit.Level, ignoreCase: true, out var level))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0008: Invalid level '{jsonDoc.Circuit.Level}' - expected HL, ML, or EL",
                    DiagnosticSeverity.Error,
                    filePath,
                    1,
                    1
                )
            );
            return new CascodeReadResult { Document = null, Diagnostics = diagnostics };
        }

        var circuit = new Circuit
        {
            Name = jsonDoc.Circuit.Name,
            Traits = jsonDoc.Circuit.Interfaces?.ToList(),
            Level = level,
            Inline = jsonDoc.Circuit.Inline,
            Parameters = BuildCircuitParameters(jsonDoc.Circuit.Parameters),
            Sizes = BuildCircuitSizes(jsonDoc.Circuit.Sizes),
            Supplies = jsonDoc.Supplies.ToList(),
            Grounds = jsonDoc.Grounds.ToList(),
            Ports =
                BuildPortDeclarations(
                    jsonDoc.Ports,
                    filePath,
                    diagnostics,
                    $"circuit '{jsonDoc.Circuit.Name}'"
                ) ?? [],
            Fill = BuildFillBlock(jsonDoc),
            Constraints = BuildConstraintsBlock(jsonDoc.Constraints),
            Harness = BuildHarnessBlock(jsonDoc.Harness),
        };

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CascodeReadResult { Document = null, Diagnostics = diagnostics };
        }

        var doc = new CascodeDocument
        {
            VersionMajor = major,
            VersionMinor = minor,
            Traits = BuildTraits(jsonDoc.Interfaces, filePath, diagnostics) ?? [],
            Primitives = BuildPrimitives(jsonDoc.Primitives),
            BenchDefinitions = BuildBenchDefinitions(jsonDoc.BenchDefinitions),
            Circuits = [circuit],
        };

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new CascodeReadResult { Document = null, Diagnostics = diagnostics };
        }

        return new CascodeReadResult { Document = doc, Diagnostics = diagnostics };
    }

    private static Circuit? FindElCircuit(CascodeDocument document, string? circuitName)
    {
        var elCircuits = document.Circuits.Where(c => c.Level == CascodeLevel.EL).ToList();
        if (elCircuits.Count == 0)
            return null;

        if (circuitName != null)
            return elCircuits.FirstOrDefault(c => c.Name == circuitName);

        return elCircuits[0];
    }

    private static CascodeJsonDocument ConvertCircuit(CascodeDocument document, Circuit circuit)
    {
        return new CascodeJsonDocument
        {
            Version = $"{document.VersionMajor}.{document.VersionMinor}",
            Interfaces = ConvertTraits(document.Traits),
            Primitives = ConvertPrimitives(document.Primitives),
            Circuit = new CascodeJsonCircuitInfo
            {
                Name = circuit.Name,
                Interfaces = circuit.Traits?.Count > 0 ? circuit.Traits : null,
                Level = circuit.Level.ToString(),
                Inline = circuit.Inline,
                Parameters = ConvertCircuitParameters(circuit.Parameters),
                Sizes = ConvertCircuitSizes(circuit.Sizes),
            },
            Supplies = circuit.Supplies,
            Grounds = circuit.Grounds,
            Ports = circuit
                .Ports.Select(p => new CascodeJsonPort
                {
                    Name = p.Name,
                    Direction = p.Direction.ToCascodeString(),
                    Kind = p.Type,
                })
                .ToList(),
            FillSizes = ConvertFillSizes(circuit.Fill),
            Nets = ConvertNets(circuit.Fill),
            Components = ConvertDevices(circuit.Fill),
            Instances = ConvertInstances(circuit.Fill),
            Attaches = ConvertAttaches(circuit.Fill),
            Constraints = ConvertConstraints(circuit.Constraints),
            Harness = ConvertHarness(circuit.Harness),
            BenchDefinitions = ConvertBenchDefinitions(document.BenchDefinitions),
        };
    }

    private static IReadOnlyList<CascodeJsonBenchDefinition>? ConvertBenchDefinitions(
        IReadOnlyList<BenchDefinition> benches
    )
    {
        if (benches.Count == 0)
            return null;

        return benches
            .Select(b => new CascodeJsonBenchDefinition
            {
                Name = b.Name,
                Interface = string.Empty,
                Builtin = null,
                Config = null,
                Outputs = null,
            })
            .ToList();
    }

    private static List<CascodeJsonNet> ConvertNets(FillBlock? fill)
    {
        if (fill == null)
            return [];

        return fill.Nets.Select(n => new CascodeJsonNet { Name = n.Id, Kind = n.Domain }).ToList();
    }

    private static List<CascodeJsonSizeDeclaration>? ConvertFillSizes(FillBlock? fill)
    {
        if (fill?.Sizes.Count is null or 0)
        {
            return null;
        }

        return fill
            .Sizes.OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new CascodeJsonSizeDeclaration
            {
                Name = s.Name,
                Default = s.Default is null
                    ? null
                    : new Dictionary<string, string>(s.Default.Entries),
            })
            .ToList();
    }

    private static List<CascodeJsonComponent> ConvertDevices(FillBlock? fill)
    {
        if (fill == null)
            return [];

        return fill
            .Devices.Select(d => new CascodeJsonComponent
            {
                Kind = d.DeviceType,
                Name = d.Id,
                Primitive = d.Primitive,
                Connections = d.Bindings,
                SizeName = d.SizeName,
                Size = d.Size?.Entries,
            })
            .ToList();
    }

    private static IReadOnlyList<CascodeJsonPrimitive>? ConvertPrimitives(
        IReadOnlyList<PrimitiveDefinition> primitives
    )
    {
        if (primitives.Count == 0)
        {
            return null;
        }

        return primitives
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => new CascodeJsonPrimitive
            {
                Name = p.Name,
                Kind = p.Kind,
                Device = p.Device,
                SizeParam = p.SizeParameter,
                Params = new Dictionary<string, string>(p.Params),
            })
            .ToList();
    }

    private static List<CascodeJsonTrait>? ConvertTraits(List<TraitDefinition> interfaces)
    {
        if (interfaces.Count == 0)
            return null;

        return interfaces
            .Select(t => new CascodeJsonTrait
            {
                Name = t.Name,
                Ports = t
                    .Ports.Select(p => new CascodeJsonPort
                    {
                        Name = p.Name,
                        Direction = p.Direction.ToCascodeString(),
                        Kind = p.Type,
                    })
                    .ToList(),
                Connectors =
                    t.Connectors.Count > 0
                        ? t
                            .Connectors.Select(c => new CascodeJsonConnector
                            {
                                TargetTrait = c.TargetTrait,
                                Mappings = c
                                    .Mappings.Select(m => new CascodeJsonMapping
                                    {
                                        Source = m.SourcePort,
                                        Target = m.TargetPort,
                                    })
                                    .ToList(),
                            })
                            .ToList()
                        : null,
            })
            .ToList();
    }

    private static List<CascodeJsonCircuitParameter>? ConvertCircuitParameters(
        List<CircuitParameter> parameters
    )
    {
        if (parameters.Count == 0)
            return null;

        return parameters
            .Select(p => new CascodeJsonCircuitParameter
            {
                Name = p.Name,
                Type = p.Type,
                Default = p.Default switch
                {
                    { Symbolic: not null } => p.Default.Symbolic,
                    { Numeric: not null } => p.Default.Numeric,
                    { Literal: not null } => p.Default.Literal,
                    _ => null,
                },
            })
            .ToList();
    }

    private static List<CascodeJsonSizeDeclaration>? ConvertCircuitSizes(
        List<SizeDeclaration> sizes
    )
    {
        if (sizes.Count == 0)
        {
            return null;
        }

        return sizes
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new CascodeJsonSizeDeclaration
            {
                Name = s.Name,
                Default = s.Default is null
                    ? null
                    : new Dictionary<string, string>(s.Default.Entries),
            })
            .ToList();
    }

    private static List<CascodeJsonInstance>? ConvertInstances(FillBlock? fill)
    {
        if (fill?.Instances.Count is null or 0)
            return null;

        return fill
            .Instances.Select(i => new CascodeJsonInstance
            {
                Id = i.Id,
                DeclaredType =
                    string.IsNullOrWhiteSpace(i.DeclaredType) || i.DeclaredType == i.Type
                        ? null
                        : i.DeclaredType,
                Type = i.Type,
                Bindings = i.Bindings,
                Params =
                    i.Params.Count > 0
                        ? i.Params.ToDictionary(
                            p => p.Key,
                            p =>
                                p.Value switch
                                {
                                    { Symbolic: not null } => p.Value.Symbolic,
                                    { Numeric: not null } => p.Value.Numeric,
                                    { Literal: not null } => p.Value.Literal!,
                                    _ => "",
                                }
                        )
                        : null,
                Sizes =
                    i.Sizes.Count > 0
                        ? i.Sizes.ToDictionary(
                            s => s.Key,
                            s => (IReadOnlyDictionary<string, string>)s.Value.Entries
                        )
                        : null,
            })
            .ToList();
    }

    private static List<SizeDeclaration> BuildCircuitSizes(
        IReadOnlyList<CascodeJsonSizeDeclaration>? sizes
    )
    {
        if (sizes is null || sizes.Count == 0)
        {
            return [];
        }

        return sizes
            .Select(s => new SizeDeclaration
            {
                Name = s.Name,
                Default = s.Default is null
                    ? null
                    : new SizePack { Entries = new Dictionary<string, string>(s.Default) },
            })
            .ToList();
    }

    private static List<CascodeJsonAttach>? ConvertAttaches(FillBlock? fill)
    {
        if (fill?.Attaches.Count is null or 0)
            return null;

        return fill
            .Attaches.Select(a => new CascodeJsonAttach
            {
                SourceInstance = a.SourceInstance,
                TargetInstances = a.TargetInstances,
                Via = a.Via,
                Anchor = a.Anchor,
                Overrides =
                    a.Overrides?.Count > 0
                        ? a
                            .Overrides.Select(o => new CascodeJsonMapping
                            {
                                Source = o.SourcePort,
                                Target = o.TargetPort,
                            })
                            .ToList()
                        : null,
            })
            .ToList();
    }

    private static CascodeJsonConstraints? ConvertConstraints(ConstraintsBlock? constraints)
    {
        if (constraints == null)
            return null;

        var hasContent =
            constraints.Bench.Count > 0
            || constraints.Spec.Count > 0
            || constraints.Physical.Count > 0;

        if (!hasContent)
            return null;

        return new CascodeJsonConstraints
        {
            Bench = constraints
                .Bench.Select(c => new CascodeJsonMetricConstraint
                {
                    Id = c.Id,
                    Bench = c.Bench,
                    Metric = c.Metric,
                    Node = c.Node?.ToString(),
                    Op = c.Op,
                    Value = ParseConstraintValue(c.Value),
                    Unit = c.Unit,
                })
                .ToList(),
            Spec = constraints
                .Spec.Select(c => new CascodeJsonMetricConstraint
                {
                    Id = c.Id,
                    Bench = c.Bench,
                    Metric = c.Metric,
                    Node = c.Node?.ToString(),
                    Op = c.Op,
                    Value = ParseConstraintValue(c.Value),
                    Unit = c.Unit,
                })
                .ToList(),
            Physical = constraints
                .Physical.Select(c => new CascodeJsonPhysicalConstraint
                {
                    Id = c.Id,
                    Metric = c.Param,
                    Op = c.Op,
                    Value = ParseConstraintValue(c.Value),
                    Unit = c.Unit,
                    Scope = c.Scope,
                })
                .ToList(),
        };
    }

    private static CascodeJsonHarness? ConvertHarness(HarnessBlock? harness)
    {
        if (harness == null)
            return null;

        var hasContent =
            harness.Supplies.Count > 0
            || harness.Biases.Count > 0
            || harness.Loads.Count > 0
            || harness.Sweeps.Count > 0;

        if (!hasContent)
            return null;

        return new CascodeJsonHarness
        {
            Supply =
                harness.Supplies.Count > 0
                    ? new CascodeJsonHarnessSupply
                    {
                        Net = harness.Supplies[0].Net,
                        Voltage = ParseHarnessValue(harness.Supplies[0].Value),
                    }
                    : null,
            Biases = harness
                .Biases.Select(b => new CascodeJsonHarnessBias
                {
                    Net = b.Net,
                    Voltage = ParseHarnessValue(b.Value),
                })
                .ToList(),
            Loads = harness.Loads.Select(ConvertLoad).ToList(),
            Sweeps = harness
                .Sweeps.Select(s => new CascodeJsonHarnessSweep
                {
                    Name = s.Name,
                    Start = ParseHarnessValue(s.Start),
                    Stop = ParseHarnessValue(s.Stop),
                    Step = s.Step != null ? ParseHarnessValue(s.Step) : null,
                    IsAuto = s.IsAuto,
                })
                .ToList(),
        };
    }

    private static CascodeJsonHarnessLoad ConvertLoad(LoadValue load)
    {
        var capacitances = new List<double>();
        var resistances = new List<double>();

        foreach (var element in load.Elements)
        {
            if (element.Type == "C")
                capacitances.Add(ParseHarnessValue(element.Value));
            else if (element.Type == "R")
                resistances.Add(ParseHarnessValue(element.Value));
        }

        return new CascodeJsonHarnessLoad
        {
            Net = load.Net,
            Capacitances = capacitances,
            Resistances = resistances,
        };
    }

    private static double ParseConstraintValue(string value)
    {
        if (SiValue.TryParse(value, out var result, stripUnits: false, allowSubUnity: true))
            return result;
        return 0;
    }

    private static double ParseHarnessValue(string value)
    {
        if (SiValue.TryParse(value, out var result, stripUnits: true, allowSubUnity: true))
            return result;
        return 0;
    }

    private static (int major, int minor) ParseVersion(string version)
    {
        var parts = version.Split('.');

        if (parts.Length < 2)
        {
            throw new FormatException(
                $"Invalid Cascode version format: '{version}'. Expected format: 'MAJOR.MINOR' (e.g., '1.0')."
            );
        }

        if (!int.TryParse(parts[0], out var major))
        {
            throw new FormatException(
                $"Invalid Cascode version format: '{version}'. Major version '{parts[0]}' is not a valid integer. Expected format: 'MAJOR.MINOR' (e.g., '1.0')."
            );
        }

        if (!int.TryParse(parts[1], out var minor))
        {
            throw new FormatException(
                $"Invalid Cascode version format: '{version}'. Minor version '{parts[1]}' is not a valid integer. Expected format: 'MAJOR.MINOR' (e.g., '1.0')."
            );
        }

        return (major, minor);
    }

    private static FillBlock BuildFillBlock(CascodeJsonDocument jsonDoc)
    {
        return new FillBlock
        {
            Sizes = BuildCircuitSizes(jsonDoc.FillSizes),
            Nets = jsonDoc
                .Nets.Select(n => new NetDeclaration { Id = n.Name, Domain = n.Kind })
                .ToList(),
            Devices = jsonDoc
                .Components.Select(c => new DeviceDeclaration
                {
                    DeviceType = c.Kind,
                    Id = c.Name,
                    Primitive = c.Primitive,
                    SizeName = c.SizeName,
                    Size = c.Size is null
                        ? null
                        : new SizePack { Entries = new Dictionary<string, string>(c.Size) },
                    Bindings = new Dictionary<string, string>(c.Connections),
                })
                .ToList(),
            Instances =
                jsonDoc
                    .Instances?.Select(i => new InstanceDeclaration
                    {
                        Id = i.Id,
                        Type = i.Type,
                        DeclaredType = string.IsNullOrWhiteSpace(i.DeclaredType)
                            ? i.Type
                            : i.DeclaredType,
                        Bindings = new Dictionary<string, string>(i.Bindings),
                        Params =
                            i.Params?.ToDictionary(p => p.Key, p => ParamValueParser.Parse(p.Value))
                            ?? new Dictionary<string, ParamValue>(),
                        Sizes =
                            i.Sizes?.ToDictionary(
                                s => s.Key,
                                s => new SizePack
                                {
                                    Entries = new Dictionary<string, string>(s.Value),
                                }
                            ) ?? new Dictionary<string, SizePack>(),
                    })
                    .ToList()
                ?? [],
            Attaches =
                jsonDoc
                    .Attaches?.Select(a => new AttachStatement
                    {
                        SourceInstance = a.SourceInstance,
                        TargetInstances = a.TargetInstances.ToList(),
                        Via = a.Via,
                        Anchor = a.Anchor,
                        Overrides = a
                            .Overrides?.Select(o => new ConnectorMapping
                            {
                                SourcePort = o.Source,
                                TargetPort = o.Target,
                            })
                            .ToList(),
                    })
                    .ToList()
                ?? [],
        };
    }

    private static List<TraitDefinition>? BuildTraits(
        IReadOnlyList<CascodeJsonTrait>? interfaces,
        string filePath,
        List<Diagnostic> diagnostics
    )
    {
        if (interfaces is null or { Count: 0 })
            return [];

        return interfaces
            .Select(t => new TraitDefinition
            {
                Name = t.Name,
                Ports =
                    BuildPortDeclarations(t.Ports, filePath, diagnostics, $"interface '{t.Name}'")
                    ?? [],
                Connectors =
                    t.Connectors?.Select(c => new TraitConnector
                        {
                            TargetTrait = c.TargetTrait,
                            Mappings = c
                                .Mappings.Select(m => new ConnectorMapping
                                {
                                    SourcePort = m.Source,
                                    TargetPort = m.Target,
                                })
                                .ToList(),
                        })
                        .ToList()
                    ?? [],
            })
            .ToList();
    }

    private static List<PrimitiveDefinition> BuildPrimitives(
        IReadOnlyList<CascodeJsonPrimitive>? primitives
    )
    {
        if (primitives is null || primitives.Count == 0)
        {
            return [];
        }

        return primitives
            .Select(p => new PrimitiveDefinition
            {
                Name = p.Name,
                Kind = p.Kind,
                Device = p.Device,
                SizeParameter = p.SizeParam,
                Params = new Dictionary<string, string>(p.Params),
            })
            .ToList();
    }

    private static List<PortDeclaration>? BuildPortDeclarations(
        IReadOnlyList<CascodeJsonPort> ports,
        string filePath,
        List<Diagnostic> diagnostics,
        string owner
    )
    {
        var result = new List<PortDeclaration>(ports.Count);

        foreach (var port in ports)
        {
            if (!PortDirectionExtensions.TryParse(port.Direction, out var direction))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS0017: Invalid port direction '{port.Direction ?? "<missing>"}' for {owner} port '{port.Name}' - expected input, output, or io",
                        DiagnosticSeverity.Error,
                        filePath,
                        1,
                        1
                    )
                );
                return null;
            }

            result.Add(
                new PortDeclaration
                {
                    Direction = direction,
                    Name = port.Name,
                    Type = port.Kind,
                }
            );
        }

        return result;
    }

    private static List<BenchDefinition> BuildBenchDefinitions(
        IReadOnlyList<CascodeJsonBenchDefinition>? benches
    )
    {
        if (benches is null or { Count: 0 })
            return [];

        return benches
            .Select(b => new BenchDefinition
            {
                Name = b.Name,
                Terminals = new List<BenchTerminal>(),
                Fill = null,
                Functions = new List<FunctionDefinition>(),
                Analyses = new List<AnalysisDeclaration>(),
                Measurements = new List<MeasurementDefinition>(),
            })
            .ToList();
    }

    private static List<CircuitParameter> BuildCircuitParameters(
        IReadOnlyList<CascodeJsonCircuitParameter>? parameters
    )
    {
        if (parameters is null or { Count: 0 })
            return [];

        return parameters
            .Select(p => new CircuitParameter
            {
                Name = p.Name,
                Type = p.Type,
                Default = p.Default is not null ? ParamValueParser.Parse(p.Default) : null,
            })
            .ToList();
    }

    private static ConstraintsBlock? BuildConstraintsBlock(CascodeJsonConstraints? constraints)
    {
        if (constraints == null)
            return null;

        return new ConstraintsBlock
        {
            Bench = constraints
                .Bench.Select(c => new MetricConstraint
                {
                    Id = c.Id,
                    Bench = c.Bench,
                    Metric = c.Metric,
                    Node = ParseNodeRef(c.Node),
                    Op = c.Op,
                    Value = FormatSIValue(c.Value, c.Unit),
                    Unit = c.Unit,
                })
                .ToList(),
            Spec = constraints
                .Spec.Select(c => new MetricConstraint
                {
                    Id = c.Id,
                    Bench = c.Bench,
                    Metric = c.Metric,
                    Node = ParseNodeRef(c.Node),
                    Op = c.Op,
                    Value = FormatSIValue(c.Value, c.Unit),
                    Unit = c.Unit,
                })
                .ToList(),
            Physical = constraints
                .Physical.Select(c => new PhysicalConstraint
                {
                    Id = c.Id,
                    Param = c.Metric,
                    Op = c.Op,
                    Value = FormatSIValue(c.Value, c.Unit),
                    Unit = c.Unit,
                    Scope = c.Scope,
                })
                .ToList(),
        };
    }

    private static NodeRef? ParseNodeRef(string? node)
    {
        if (string.IsNullOrWhiteSpace(node))
            return null;

        var parts = node.Split(new[] { "::" }, 2, System.StringSplitOptions.None);
        if (parts.Length == 2)
        {
            return new NodeRef { Scope = parts[0], Path = parts[1] };
        }

        return new NodeRef { Scope = "net", Path = node };
    }

    private static HarnessBlock? BuildHarnessBlock(CascodeJsonHarness? harness)
    {
        if (harness == null)
            return null;

        var supplies = new List<SupplyValue>();
        if (harness.Supply != null)
        {
            supplies.Add(
                new SupplyValue
                {
                    Net = harness.Supply.Net,
                    Value = FormatVoltage(harness.Supply.Voltage),
                }
            );
        }

        return new HarnessBlock
        {
            Supplies = supplies,
            Biases = harness
                .Biases.Select(b => new BiasValue { Net = b.Net, Value = FormatVoltage(b.Voltage) })
                .ToList(),
            Loads = harness.Loads.Select(BuildLoadValue).ToList(),
            Sweeps = harness
                .Sweeps.Select(s => new SweepCondition
                {
                    Name = s.Name,
                    Start = FormatVoltage(s.Start),
                    Stop = FormatVoltage(s.Stop),
                    Step = s.Step.HasValue ? FormatVoltage(s.Step.Value) : null,
                    IsAuto = s.IsAuto,
                })
                .ToList(),
        };
    }

    private static LoadValue BuildLoadValue(CascodeJsonHarnessLoad load)
    {
        var elements = new List<LoadElement>();
        foreach (var capacitance in load.Capacitances)
        {
            if (capacitance != 0)
                elements.Add(new LoadElement("C", FormatCapacitance(capacitance)));
        }
        foreach (var resistance in load.Resistances)
        {
            if (resistance != 0)
                elements.Add(new LoadElement("R", FormatResistance(resistance)));
        }

        return new LoadValue { Net = load.Net, Elements = elements };
    }

    private static string FormatSIValue(double value, string unit)
    {
        var formatted = SiValue.Format(value);
        return formatted;
    }

    private static string FormatVoltage(double value)
    {
        return $"{SiValue.Format(value)}V";
    }

    private static string FormatCapacitance(double value)
    {
        return $"{SiValue.Format(value)}F";
    }

    private static string FormatResistance(double value)
    {
        return $"{SiValue.Format(value)}Ohm";
    }
}
