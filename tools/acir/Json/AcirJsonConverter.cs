using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cascode.Parser;

namespace Cascode.ACIR.Json;

/// <summary>
/// Converts between ACIRDocument and AcirJsonDocument representations.
/// Only EL-level circuits are supported.
/// </summary>
public static class AcirJsonConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Converts an ACIRDocument to JSON string representation.
    /// </summary>
    /// <param name="document">The ACIR document to convert.</param>
    /// <param name="circuitName">Optional circuit name. If null, uses first EL circuit.</param>
    /// <returns>JSON string representation of the circuit.</returns>
    /// <exception cref="ArgumentException">If no EL circuit is found.</exception>
    public static string ToJson(ACIRDocument document, string? circuitName = null)
    {
        var jsonDoc = ToJsonDocument(document, circuitName);
        return JsonSerializer.Serialize(jsonDoc, JsonOptions);
    }

    /// <summary>
    /// Converts an ACIRDocument to AcirJsonDocument.
    /// </summary>
    /// <param name="document">The ACIR document to convert.</param>
    /// <param name="circuitName">Optional circuit name. If null, uses first EL circuit.</param>
    /// <returns>The JSON document representation.</returns>
    /// <exception cref="ArgumentException">If no EL circuit is found.</exception>
    public static AcirJsonDocument ToJsonDocument(ACIRDocument document, string? circuitName = null)
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
    /// Parses JSON string into an ACIRDocument with structured error handling.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <param name="filePath">Optional file path for diagnostic messages.</param>
    /// <returns>Read result containing the document and any diagnostics.</returns>
    public static ACIRReadResult FromJson(string json, string filePath = "<json>")
    {
        var diagnostics = new List<Diagnostic>();

        AcirJsonDocument? jsonDoc;
        try
        {
            jsonDoc = JsonSerializer.Deserialize<AcirJsonDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0009: JSON parse error: {ex.Message}",
                    DiagnosticSeverity.Error,
                    filePath,
                    1,
                    1
                )
            );
            return new ACIRReadResult { Document = null, Diagnostics = diagnostics };
        }

        if (jsonDoc == null)
        {
            diagnostics.Add(
                new Diagnostic(
                    "ACIR0009: Failed to parse JSON document (null result)",
                    DiagnosticSeverity.Error,
                    filePath,
                    1,
                    1
                )
            );
            return new ACIRReadResult { Document = null, Diagnostics = diagnostics };
        }

        return FromJsonDocument(jsonDoc, filePath, diagnostics);
    }

    /// <summary>
    /// Converts AcirJsonDocument back to ACIRDocument with structured error handling.
    /// </summary>
    /// <param name="jsonDoc">The JSON document to convert.</param>
    /// <param name="filePath">Optional file path for diagnostic messages.</param>
    /// <param name="diagnostics">Optional diagnostics list to append to.</param>
    /// <returns>Read result containing the document and any diagnostics.</returns>
    public static ACIRReadResult FromJsonDocument(
        AcirJsonDocument jsonDoc,
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
            (major, minor) = ParseVersion(jsonDoc.AcirVersion);
        }
        catch (FormatException ex)
        {
            diagnostics.Add(
                new Diagnostic($"ACIR0002: {ex.Message}", DiagnosticSeverity.Error, filePath, 1, 1)
            );
            return new ACIRReadResult { Document = null, Diagnostics = diagnostics };
        }

        // Validate major version
        if (major != ACIRVersion.Major)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0007: ACIR major version {major} not supported. Expected major version {ACIRVersion.Major}.",
                    DiagnosticSeverity.Error,
                    filePath,
                    1,
                    1
                )
            );
            return new ACIRReadResult { Document = null, Diagnostics = diagnostics };
        }

        // Parse level with error handling - NO FALLBACK
        if (!Enum.TryParse<ACIRLevel>(jsonDoc.Circuit.Level, ignoreCase: true, out var level))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0008: Invalid level '{jsonDoc.Circuit.Level}' - expected HL, ML, or EL",
                    DiagnosticSeverity.Error,
                    filePath,
                    1,
                    1
                )
            );
            return new ACIRReadResult { Document = null, Diagnostics = diagnostics };
        }

        var circuit = new Circuit
        {
            Name = jsonDoc.Circuit.Name,
            Traits = jsonDoc.Circuit.Traits?.ToList(),
            Level = level,
            Inline = jsonDoc.Circuit.Inline,
            Parameters = BuildCircuitParameters(jsonDoc.Circuit.Parameters),
            Supplies = jsonDoc.Supplies.ToList(),
            Grounds = jsonDoc.Grounds.ToList(),
            Ports = jsonDoc
                .Ports.Select(p => new PortDeclaration { Name = p.Name, Type = p.Kind })
                .ToList(),
            Fill = BuildFillBlock(jsonDoc),
            Constraints = BuildConstraintsBlock(jsonDoc.Constraints),
            Harness = BuildHarnessBlock(jsonDoc.Harness),
            Benches =
                jsonDoc.Benches.Count > 0
                    ? new BenchesBlock
                    {
                        Benches = jsonDoc
                            .Benches.Select(b => new BenchConfig { Name = b })
                            .ToList(),
                    }
                    : null,
        };

        var doc = new ACIRDocument
        {
            VersionMajor = major,
            VersionMinor = minor,
            Traits = BuildTraits(jsonDoc.Traits),
            Circuits = [circuit],
        };

        return new ACIRReadResult { Document = doc, Diagnostics = diagnostics };
    }

    private static Circuit? FindElCircuit(ACIRDocument document, string? circuitName)
    {
        var elCircuits = document.Circuits.Where(c => c.Level == ACIRLevel.EL).ToList();
        if (elCircuits.Count == 0)
            return null;

        if (circuitName != null)
            return elCircuits.FirstOrDefault(c => c.Name == circuitName);

        return elCircuits[0];
    }

    private static AcirJsonDocument ConvertCircuit(ACIRDocument document, Circuit circuit)
    {
        return new AcirJsonDocument
        {
            AcirVersion = $"{document.VersionMajor}.{document.VersionMinor}",
            Traits = ConvertTraits(document.Traits),
            Circuit = new AcirJsonCircuitInfo
            {
                Name = circuit.Name,
                Traits = circuit.Traits?.Count > 0 ? circuit.Traits : null,
                Level = circuit.Level.ToString(),
                Inline = circuit.Inline,
                Parameters = ConvertCircuitParameters(circuit.Parameters),
            },
            Supplies = circuit.Supplies,
            Grounds = circuit.Grounds,
            Ports = circuit
                .Ports.Select(p => new AcirJsonPort { Name = p.Name, Kind = p.Type })
                .ToList(),
            Nets = ConvertNets(circuit.Fill),
            Components = ConvertDevices(circuit.Fill),
            Instances = ConvertInstances(circuit.Fill),
            Attaches = ConvertAttaches(circuit.Fill),
            Constraints = ConvertConstraints(circuit.Constraints),
            Harness = ConvertHarness(circuit.Harness),
            Benches = circuit.Benches?.Benches.Select(b => b.Name).ToList() ?? [],
        };
    }

    private static List<AcirJsonNet> ConvertNets(FillBlock? fill)
    {
        if (fill == null)
            return [];

        return fill.Nets.Select(n => new AcirJsonNet { Name = n.Id, Kind = n.Domain }).ToList();
    }

    private static List<AcirJsonComponent> ConvertDevices(FillBlock? fill)
    {
        if (fill == null)
            return [];

        return fill
            .Devices.Select(d => new AcirJsonComponent
            {
                Kind = d.DeviceType,
                Name = d.Id,
                Connections = d.Bindings,
                Params = d.Params,
                Process = d.PdkDevice,
            })
            .ToList();
    }

    private static List<AcirJsonTrait>? ConvertTraits(List<TraitDefinition> traits)
    {
        if (traits.Count == 0)
            return null;

        return traits
            .Select(t => new AcirJsonTrait
            {
                Name = t.Name,
                Ports = t
                    .Ports.Select(p => new AcirJsonPort { Name = p.Name, Kind = p.Type })
                    .ToList(),
                Connectors =
                    t.Connectors.Count > 0
                        ? t
                            .Connectors.Select(c => new AcirJsonConnector
                            {
                                TargetTrait = c.TargetTrait,
                                Mappings = c
                                    .Mappings.Select(m => new AcirJsonMapping
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

    private static List<AcirJsonCircuitParameter>? ConvertCircuitParameters(
        List<CircuitParameter> parameters
    )
    {
        if (parameters.Count == 0)
            return null;

        return parameters
            .Select(p => new AcirJsonCircuitParameter
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

    private static List<AcirJsonInstance>? ConvertInstances(FillBlock? fill)
    {
        if (fill?.Instances.Count is null or 0)
            return null;

        return fill
            .Instances.Select(i => new AcirJsonInstance
            {
                Id = i.Id,
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
            })
            .ToList();
    }

    private static List<AcirJsonAttach>? ConvertAttaches(FillBlock? fill)
    {
        if (fill?.Attaches.Count is null or 0)
            return null;

        return fill
            .Attaches.Select(a => new AcirJsonAttach
            {
                SourceInstance = a.SourceInstance,
                TargetInstance = a.TargetInstance,
                Via = a.Via,
                Anchor = a.Anchor,
                Overrides =
                    a.Overrides?.Count > 0
                        ? a
                            .Overrides.Select(o => new AcirJsonMapping
                            {
                                Source = o.SourcePort,
                                Target = o.TargetPort,
                            })
                            .ToList()
                        : null,
            })
            .ToList();
    }

    private static AcirJsonConstraints? ConvertConstraints(ConstraintsBlock? constraints)
    {
        if (constraints == null)
            return null;

        var hasContent =
            constraints.Numeric.Count > 0
            || constraints.Tech.Count > 0
            || constraints.Measure.Count > 0;

        if (!hasContent)
            return null;

        return new AcirJsonConstraints
        {
            Numeric = constraints
                .Numeric.Select(c => new AcirJsonNumericConstraint
                {
                    Id = c.Id,
                    Metric = c.Metric,
                    Node = c.Node,
                    Op = c.Op,
                    Value = ParseConstraintValue(c.Value),
                    Unit = c.Unit,
                })
                .ToList(),
            Tech = constraints
                .Tech.Select(c => new AcirJsonTechConstraint
                {
                    Id = c.Id,
                    Metric = c.Param,
                    Op = c.Op,
                    Value = ParseConstraintValue(c.Value),
                    Unit = c.Unit,
                    Scope = c.Scope,
                })
                .ToList(),
            Measure = constraints
                .Measure.Select(m => new AcirJsonMeasure
                {
                    Id = m.Id,
                    Bench = m.Bench,
                    Metric = m.Metric,
                    Node = m.Node,
                })
                .ToList(),
        };
    }

    private static AcirJsonHarness? ConvertHarness(HarnessBlock? harness)
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

        return new AcirJsonHarness
        {
            Supply =
                harness.Supplies.Count > 0
                    ? new AcirJsonHarnessSupply
                    {
                        Net = harness.Supplies[0].Net,
                        Voltage = ParseHarnessValue(harness.Supplies[0].Value),
                    }
                    : null,
            Biases = harness
                .Biases.Select(b => new AcirJsonHarnessBias
                {
                    Net = b.Net,
                    Voltage = ParseHarnessValue(b.Value),
                })
                .ToList(),
            Loads = harness.Loads.Select(ConvertLoad).ToList(),
            Sweeps = harness
                .Sweeps.Select(s => new AcirJsonHarnessSweep
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

    private static AcirJsonHarnessLoad ConvertLoad(LoadValue load)
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

        return new AcirJsonHarnessLoad
        {
            Net = load.Net,
            Capacitances = capacitances,
            Resistances = resistances,
        };
    }

    private static double ParseConstraintValue(string value)
    {
        if (
            ACIRBenchAdapter.TryParseSIValue(
                value,
                out var result,
                stripUnits: false,
                allowSubUnity: true
            )
        )
            return result;
        return 0;
    }

    private static double ParseHarnessValue(string value)
    {
        if (
            ACIRBenchAdapter.TryParseSIValue(
                value,
                out var result,
                stripUnits: true,
                allowSubUnity: true
            )
        )
            return result;
        return 0;
    }

    private static (int major, int minor) ParseVersion(string version)
    {
        var parts = version.Split('.');

        if (parts.Length < 2)
        {
            throw new FormatException(
                $"Invalid ACIR version format: '{version}'. Expected format: 'MAJOR.MINOR' (e.g., '1.0')."
            );
        }

        if (!int.TryParse(parts[0], out var major))
        {
            throw new FormatException(
                $"Invalid ACIR version format: '{version}'. Major version '{parts[0]}' is not a valid integer. Expected format: 'MAJOR.MINOR' (e.g., '1.0')."
            );
        }

        if (!int.TryParse(parts[1], out var minor))
        {
            throw new FormatException(
                $"Invalid ACIR version format: '{version}'. Minor version '{parts[1]}' is not a valid integer. Expected format: 'MAJOR.MINOR' (e.g., '1.0')."
            );
        }

        return (major, minor);
    }

    private static FillBlock BuildFillBlock(AcirJsonDocument jsonDoc)
    {
        return new FillBlock
        {
            Nets = jsonDoc
                .Nets.Select(n => new NetDeclaration { Id = n.Name, Domain = n.Kind })
                .ToList(),
            Devices = jsonDoc
                .Components.Select(c => new DeviceDeclaration
                {
                    DeviceType = c.Kind,
                    Id = c.Name,
                    Bindings = new Dictionary<string, string>(c.Connections),
                    Params = new Dictionary<string, string>(c.Params),
                    PdkDevice = c.Process,
                })
                .ToList(),
            Instances =
                jsonDoc
                    .Instances?.Select(i => new InstanceDeclaration
                    {
                        Id = i.Id,
                        Type = i.Type,
                        Bindings = new Dictionary<string, string>(i.Bindings),
                        Params =
                            i.Params?.ToDictionary(p => p.Key, p => ParseParamValue(p.Value))
                            ?? new Dictionary<string, ParamValue>(),
                    })
                    .ToList()
                ?? [],
            Attaches =
                jsonDoc
                    .Attaches?.Select(a => new AttachStatement
                    {
                        SourceInstance = a.SourceInstance,
                        TargetInstance = a.TargetInstance,
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

    private static List<TraitDefinition> BuildTraits(IReadOnlyList<AcirJsonTrait>? traits)
    {
        if (traits is null or { Count: 0 })
            return [];

        return traits
            .Select(t => new TraitDefinition
            {
                Name = t.Name,
                Ports = t
                    .Ports.Select(p => new PortDeclaration { Name = p.Name, Type = p.Kind })
                    .ToList(),
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

    private static List<CircuitParameter> BuildCircuitParameters(
        IReadOnlyList<AcirJsonCircuitParameter>? parameters
    )
    {
        if (parameters is null or { Count: 0 })
            return [];

        return parameters
            .Select(p => new CircuitParameter
            {
                Name = p.Name,
                Type = p.Type,
                Default = p.Default is not null ? ParseParamValue(p.Default) : null,
            })
            .ToList();
    }

    private static ParamValue ParseParamValue(string value)
    {
        if (value.StartsWith('$'))
            return new ParamValue { Symbolic = value };

        if (double.TryParse(value, out _) || value.Any(char.IsDigit))
            return new ParamValue { Numeric = value };

        return new ParamValue { Literal = value };
    }

    private static ConstraintsBlock? BuildConstraintsBlock(AcirJsonConstraints? constraints)
    {
        if (constraints == null)
            return null;

        return new ConstraintsBlock
        {
            Numeric = constraints
                .Numeric.Select(c => new NumericConstraint
                {
                    Id = c.Id,
                    Metric = c.Metric,
                    Node = c.Node,
                    Op = c.Op,
                    Value = FormatSIValue(c.Value, c.Unit),
                    Unit = c.Unit,
                })
                .ToList(),
            Tech = constraints
                .Tech.Select(c => new TechConstraint
                {
                    Id = c.Id,
                    Param = c.Metric,
                    Op = c.Op,
                    Value = FormatSIValue(c.Value, c.Unit),
                    Unit = c.Unit,
                    Scope = c.Scope,
                })
                .ToList(),
            Measure = constraints
                .Measure.Select(m => new MeasureIntent
                {
                    Id = m.Id,
                    Bench = m.Bench,
                    Metric = m.Metric,
                    Node = m.Node,
                })
                .ToList(),
        };
    }

    private static HarnessBlock? BuildHarnessBlock(AcirJsonHarness? harness)
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

    private static LoadValue BuildLoadValue(AcirJsonHarnessLoad load)
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
        var formatted = ACIRBenchAdapter.FormatSIValue(value);
        return formatted;
    }

    private static string FormatVoltage(double value)
    {
        return $"{ACIRBenchAdapter.FormatSIValue(value)}V";
    }

    private static string FormatCapacitance(double value)
    {
        return $"{ACIRBenchAdapter.FormatSIValue(value)}F";
    }

    private static string FormatResistance(double value)
    {
        return $"{ACIRBenchAdapter.FormatSIValue(value)}Ohm";
    }
}
