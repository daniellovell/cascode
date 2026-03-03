using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cascode.Language.Validation;

namespace Cascode.Language;

/// <summary>
/// Writes Cascode documents to text format following canonical writer rules.
/// </summary>
public static partial class CascodeWriter
{
    /// <summary>
    /// Writes an Cascode document to a text writer.
    /// </summary>
    public static void Write(CascodeDocument document, TextWriter writer)
    {
        // Version declaration
        writer.WriteLine($"VERSION {CascodeVersion.Current}");
        writer.WriteLine();

        // Include directives (source docs and include-pruned linked outputs).
        foreach (var inc in document.Includes.OrderBy(i => i.Name, StringComparer.Ordinal))
        {
            writer.WriteLine($"include {inc.Name}");
        }
        if (document.Includes.Count > 0)
        {
            writer.WriteLine();
        }

        // Bundle type definitions
        foreach (
            var bundleType in document.BundleTypes.OrderBy(b => b.Name, StringComparer.Ordinal)
        )
        {
            WriteBundleType(bundleType, writer);
            writer.WriteLine();
        }

        // Interface definitions
        foreach (var interfaceDef in document.Traits.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            WriteTrait(interfaceDef, writer);
            writer.WriteLine();
        }

        // Bench definitions
        foreach (
            var bench in document.BenchDefinitions.OrderBy(b => b.Name, StringComparer.Ordinal)
        )
        {
            WriteBenchDefinition(bench, writer);
            writer.WriteLine();
        }

        // File-level helper functions
        foreach (var fn in document.Functions.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            WriteFunctionDefinition(fn, "function", writer);
            writer.WriteLine();
        }

        // Primitive definitions
        foreach (var primitive in document.Primitives.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            WritePrimitiveDefinition(primitive, writer);
            writer.WriteLine();
        }

        // Circuits
        foreach (var circuit in document.Circuits)
        {
            WriteCircuit(circuit, writer);
            writer.WriteLine();
        }
    }

    private static void WriteBundleType(BundleType bundleType, TextWriter writer)
    {
        writer.WriteLine($"bundle {bundleType.Name} {{");
        foreach (var field in bundleType.Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            writer.WriteLine($"  {field.Key} : {field.Value}");
        }
        writer.WriteLine("}");
    }

    private static void WriteTrait(TraitDefinition interfaceDef, TextWriter writer)
    {
        writer.WriteLine($"interface {interfaceDef.Name} {{");

        // Ports
        foreach (var port in interfaceDef.Ports.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            writer.WriteLine($"  {port.Direction.ToCascodeString()} {port.Name} : {port.Type}");
        }

        // Connectors
        if (interfaceDef.Connectors.Count > 0)
        {
            writer.WriteLine("  connectors {");
            foreach (
                var connector in interfaceDef.Connectors.OrderBy(
                    c => c.TargetTrait,
                    StringComparer.Ordinal
                )
            )
            {
                writer.WriteLine($"    to {connector.TargetTrait} {{");
                foreach (var mapping in connector.Mappings)
                {
                    writer.WriteLine($"      {mapping.SourcePort}--{mapping.TargetPort}");
                }
                writer.WriteLine("    }");
            }
            writer.WriteLine("  }");
        }

        if (interfaceDef.BenchBindings.Count > 0)
        {
            WriteBenchesSection(
                interfaceDef.BenchBindings,
                Array.Empty<BenchBindingExtension>(),
                writer
            );
        }

        writer.WriteLine("}");
    }

    private static void WriteBenchDefinition(BenchDefinition bench, TextWriter writer)
    {
        var paramSig =
            bench.Parameters.Count == 0
                ? ""
                : "("
                    + string.Join(
                        ", ",
                        bench.Parameters.Select(p =>
                            p.Default is null
                                ? $"{FormatBenchValueType(p.Type)} {p.Name}"
                                : $"{FormatBenchValueType(p.Type)} {p.Name} = {FormatMeasurementExpr(p.Default)}"
                        )
                    )
                    + ")";
        var abstractPrefix = bench.IsAbstract ? "abstract " : string.Empty;
        var extendsClause = string.IsNullOrWhiteSpace(bench.BaseBench)
            ? string.Empty
            : $" extends {bench.BaseBench}";
        writer.WriteLine($"{abstractPrefix}bench {bench.Name}{paramSig}{extendsClause} {{");

        foreach (var t in bench.Terminals.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var role = t.Role == BenchTerminalRole.Stim ? "stim" : "resp";
            if (t.IsAbstract)
            {
                var abstractTypeSuffix = t.Type is null ? string.Empty : $" : {t.Type}";
                writer.WriteLine($"  abstract {role} {t.Name}{abstractTypeSuffix}");
            }
            else if (t.Type is null)
            {
                writer.WriteLine($"  {role} {t.Name}");
            }
            else
            {
                writer.WriteLine($"  {role} {t.Name} : {t.Type}");
            }
        }

        if (bench.Fill is not null)
        {
            writer.WriteLine("  fill {");
            WriteFillBlock(bench.Fill, writer);
            writer.WriteLine("  }");
        }

        foreach (var fn in bench.Functions.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            WriteFunctionDefinition(fn, "function", writer, indent: "  ");
        }

        if (bench.Analyses.Count > 0)
        {
            var analysisPrefix = bench.OverrideAnalysis ? "override " : string.Empty;
            writer.WriteLine($"  {analysisPrefix}analysis {{");
            foreach (var analysis in bench.Analyses.OrderBy(a => a.Name, StringComparer.Ordinal))
            {
                WriteAnalysisDeclaration(analysis, writer, indent: "    ");
            }
            writer.WriteLine("  }");
        }

        if (bench.Measurements.Count > 0)
        {
            writer.WriteLine("  measurements {");
            foreach (
                var measurement in bench.Measurements.OrderBy(m => m.Name, StringComparer.Ordinal)
            )
            {
                WriteMeasurementDefinition(measurement, writer, indent: "    ");
            }
            writer.WriteLine("  }");
        }

        writer.WriteLine("}");
    }

    private static void WritePrimitiveDefinition(PrimitiveDefinition primitive, TextWriter writer)
    {
        var signature = string.IsNullOrWhiteSpace(primitive.SizeParameter)
            ? string.Empty
            : $"(size {primitive.SizeParameter})";
        writer.WriteLine($"primitive {primitive.Kind} {primitive.Name}{signature} {{");
        writer.WriteLine($"  device \"{primitive.Device}\"");
        writer.WriteLine("  params {");
        foreach (var entry in primitive.Params.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            writer.WriteLine($"    {entry.Key} = {entry.Value}");
        }
        writer.WriteLine("  }");
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes a Circuit to the provided TextWriter using the canonical Cascode textual
    /// representation.
    /// </summary>
    /// <remarks>
    /// Emits the circuit header (name, size parameters, parameters, implemented traits) and then
    /// writes the circuit body sections in canonical order, including level/inline/library, supplies,
    /// grounds, ports, slot, fill, constraints, harness, env, any pruned render block, bench
    /// bindings/extensions, synth entries, and provenance.
    /// </remarks>
    /// <param name="circuit">The Circuit model to serialize.</param>
    /// <param name="writer">The TextWriter to which the circuit text will be written.</param>
    private static void WriteCircuit(Circuit circuit, TextWriter writer)
    {
        // Circuit header
        var header = $"circuit {circuit.Name}";
        var signatureParts = new List<string>();
        foreach (var size in circuit.Sizes.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            var part = $"size {size.Name}";
            if (size.Default is not null)
            {
                part += $" = {FormatSizeExpr(size.Default)}";
            }
            signatureParts.Add(part);
        }
        foreach (var param in circuit.Parameters.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var defaultPart = param.Default is not null
                ? $" = {FormatParamValue(param.Default)}"
                : "";
            signatureParts.Add($"{param.Type} {param.Name}{defaultPart}");
        }
        if (signatureParts.Count > 0)
        {
            header += $"({string.Join(", ", signatureParts)})";
        }
        if (circuit.Traits is { Count: > 0 })
        {
            header += $" implements {string.Join(", ", circuit.Traits)}";
        }
        writer.WriteLine($"{header} {{");

        // Level
        writer.WriteLine($"  level {circuit.Level}");

        // Inline
        if (circuit.Inline)
        {
            writer.WriteLine("  inline");
        }

        // Package
        if (!string.IsNullOrEmpty(circuit.Package))
        {
            writer.WriteLine($"  library {circuit.Package}");
        }

        // Supplies
        foreach (var supply in circuit.Supplies.OrderBy(s => s, StringComparer.Ordinal))
        {
            writer.WriteLine($"  supply {supply}");
        }

        // Grounds
        foreach (var ground in circuit.Grounds.OrderBy(g => g, StringComparer.Ordinal))
        {
            writer.WriteLine($"  ground {ground}");
        }

        // Ports
        foreach (var port in circuit.Ports.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            writer.WriteLine($"  {port.Direction.ToCascodeString()} {port.Name} : {port.Type}");
        }

        // Slot (HL level)
        if (circuit.Slot is { } slot)
        {
            bool hasContent =
                slot.Nets.Count > 0 || slot.Instances.Count > 0 || slot.Connections.Count > 0;
            if (hasContent)
            {
                writer.WriteLine("  slot {");
                WriteSlotBlock(slot, writer);
                writer.WriteLine("  }");
            }
            else
            {
                writer.WriteLine("  slot");
            }
        }

        // Fill block (ML and EL levels)
        if (circuit.Fill is not null)
        {
            writer.WriteLine("  fill {");
            WriteFillBlock(circuit.Fill, writer);
            writer.WriteLine("  }");
        }

        // Constraints
        if (circuit.Constraints is not null)
        {
            WriteConstraints(circuit.Constraints, writer);
        }

        // Harness
        if (circuit.Harness is not null)
        {
            WriteHarness(circuit.Harness, writer);
        }

        if (circuit.Env is not null && circuit.Env.Entries.Count > 0)
        {
            writer.WriteLine("  env {");
            foreach (var entry in circuit.Env.Entries.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                writer.WriteLine($"    {entry.Key} = {entry.Value}");
            }
            writer.WriteLine("  }");
        }

        var prunedRender = RenderBlockValidator.Prune(circuit);
        if (prunedRender is not null && prunedRender.Entities.Count > 0)
        {
            WriteRenderBlock(prunedRender, writer);
        }

        if (circuit.BenchBindings.Count > 0 || circuit.BenchBindingExtensions.Count > 0)
        {
            WriteBenchesSection(circuit.BenchBindings, circuit.BenchBindingExtensions, writer);
        }

        if (circuit.Synth is not null && circuit.Synth.Entries.Count > 0)
        {
            writer.WriteLine("  synth {");
            foreach (var entry in circuit.Synth.Entries.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                writer.WriteLine($"    {entry.Key} = {entry.Value}");
            }
            writer.WriteLine("  }");
        }

        // Provenance
        if (circuit.Provenance is not null)
        {
            WriteProvenance(circuit.Provenance, writer);
        }

        writer.WriteLine("}");
    }

    private static void WriteSlotBlock(SlotBlock slot, TextWriter writer)
    {
        foreach (var net in slot.Nets.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            writer.WriteLine($"    net {net.Id} : {net.Domain}");
        }

        foreach (var inst in slot.Instances.OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            WriteInstance(inst, writer, indent: "    ");
        }

        foreach (var conn in slot.Connections.OrderBy(c => c.From, StringComparer.Ordinal))
        {
            writer.WriteLine($"    {conn.From}--{conn.To}");
        }
    }

    private static void WriteFillBlock(FillBlock fill, TextWriter writer)
    {
        // Nets first
        foreach (var net in fill.Nets.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            writer.WriteLine($"    net {net.Id} : {net.Domain}");
        }

        // Local sizes
        foreach (var size in fill.Sizes.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            if (size.Default is null)
            {
                continue;
            }

            writer.WriteLine($"    size {size.Name} = {FormatSizeExpr(size.Default)}");
        }

        // Instances (ML)
        foreach (var inst in fill.Instances.OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            WriteInstance(inst, writer);
        }

        // Devices (EL)
        foreach (var device in fill.Devices.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            WriteDevice(device, writer);
        }

        // Attach statements (EL level)
        foreach (var attach in fill.Attaches.OrderBy(a => a.SourceInstance, StringComparer.Ordinal))
        {
            WriteAttach(attach, writer);
        }

        // Explicit connections
        foreach (var conn in fill.Connections.OrderBy(c => c.From, StringComparer.Ordinal))
        {
            writer.WriteLine($"    {conn.From}--{conn.To}");
        }
    }

    private static void WriteAttach(AttachStatement attach, TextWriter writer)
    {
        var viaParts = attach.Via.Split("::");
        var header = new StringBuilder();
        header.Append($"    attach {attach.SourceInstance}");
        foreach (var target in attach.TargetInstances)
        {
            header.Append($" to {target}");
        }
        header.Append($" via {viaParts[0]}::{viaParts[1]}");
        if (!string.IsNullOrEmpty(attach.Anchor))
        {
            header.Append($" as {attach.Anchor}");
        }

        if (attach.Overrides is { Count: > 0 })
        {
            writer.WriteLine($"{header} {{");
            foreach (
                var mapping in attach.Overrides.OrderBy(m => m.SourcePort, StringComparer.Ordinal)
            )
            {
                writer.WriteLine($"      .{mapping.SourcePort}--{mapping.TargetPort}");
            }
            writer.WriteLine("    }");
        }
        else
        {
            writer.WriteLine(header.ToString());
        }
    }

    private static void WriteInstance(InstanceDeclaration inst, TextWriter writer)
    {
        WriteInstance(inst, writer, indent: "    ");
    }

    private static void WriteInstance(InstanceDeclaration inst, TextWriter writer, string indent)
    {
        var args = new List<string>();
        foreach (var size in inst.Sizes.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            args.Add($"{size.Key}={FormatSizeExpr(size.Value)}");
        }
        foreach (var param in inst.Params.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            args.Add($"{param.Key}={FormatParamValue(param.Value)}");
        }

        var argList = args.Count > 0 ? $"({string.Join(", ", args)})" : string.Empty;
        var declaredType = string.IsNullOrWhiteSpace(inst.DeclaredType)
            ? inst.Type
            : inst.DeclaredType;
        writer.WriteLine($"{indent}{declaredType} {inst.Id} = new {inst.Type}{argList} {{");
        var bindIndent = indent + "  ";
        foreach (var binding in inst.Bindings.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            writer.WriteLine($"{bindIndent}.{binding.Key}--{binding.Value}");
        }
        writer.WriteLine($"{indent}}}");
    }

    private static void WriteDevice(DeviceDeclaration device, TextWriter writer)
    {
        var sizeArg =
            device.SizeName ?? (device.Size is not null ? FormatSizeExpr(device.Size) : "");
        writer.WriteLine(
            $"    {device.DeviceType} {device.Id} = new {device.Primitive}({sizeArg}) {{"
        );
        foreach (var binding in device.Bindings.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            writer.WriteLine($"      .{binding.Key}--{binding.Value}");
        }
        writer.WriteLine("    }");
    }

    private static void WriteConstraints(ConstraintsBlock constraints, TextWriter writer)
    {
        writer.WriteLine("  constraints {");
        if (constraints.Numeric.Count > 0)
        {
            writer.WriteLine("    numeric {");
            foreach (var c in constraints.Numeric)
            {
                var node = c.Node is not null ? $" at {c.Node}" : "";
                var benchArgs =
                    c.BenchArgs.Count == 0
                        ? ""
                        : "("
                            + string.Join(
                                ", ",
                                c.BenchArgs.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                                    .Select(a => $"{a.Name}={a.Value}")
                            )
                            + ")";
                var metricArgs =
                    c.MetricArgs.Count == 0
                        ? ""
                        : "("
                            + string.Join(
                                ", ",
                                c.MetricArgs.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                                    .Select(a => $"{a.Name}={a.Value}")
                            )
                            + ")";
                writer.WriteLine(
                    $"      {c.Id} = {c.BenchBase}{benchArgs}::{c.Metric}{metricArgs}{node} {c.Op} {c.Value}{c.Unit}"
                );
            }
            writer.WriteLine("    }");
        }
        if (constraints.Tech.Count > 0)
        {
            writer.WriteLine("    tech {");
            foreach (var c in constraints.Tech)
            {
                writer.WriteLine($"      {c.Id} : {c.Param} {c.Op} {c.Value}{c.Unit} on {c.Scope}");
            }
            writer.WriteLine("    }");
        }
        if (constraints.Graph.Count > 0)
        {
            writer.WriteLine("    graph {");
            foreach (var c in constraints.Graph)
            {
                writer.WriteLine($"      {c.Id} : {c.Rule} ..."); // Simplified for now
            }
            writer.WriteLine("    }");
        }
        writer.WriteLine("  }");
    }

    private static void WriteHarness(HarnessBlock harness, TextWriter writer)
    {
        writer.WriteLine("  harness {");
        foreach (var ground in harness.Grounds.OrderBy(g => g.Net, StringComparer.Ordinal))
        {
            writer.WriteLine($"    ground {ground.Net} = {ground.Value}");
        }
        foreach (var supply in harness.Supplies.OrderBy(s => s.Net, StringComparer.Ordinal))
        {
            writer.WriteLine($"    supply {supply.Net} = {supply.Value}");
        }
        foreach (var bias in harness.Biases.OrderBy(b => b.Net, StringComparer.Ordinal))
        {
            writer.WriteLine($"    bias {bias.Net} = {bias.Value}");
        }
        foreach (var source in harness.Sources.OrderBy(s => s.Net, StringComparer.Ordinal))
        {
            var z = source.Z is not null ? $" Z={source.Z}" : "";
            writer.WriteLine($"    source {source.Net}{z}");
        }
        foreach (var load in harness.Loads.OrderBy(l => l.Net, StringComparer.Ordinal))
        {
            if (load.Elements.Count == 0)
            {
                continue;
            }
            else if (load.Elements.Count == 1)
            {
                var element = load.Elements[0];
                writer.WriteLine($"    load {load.Net} {element.Type}={element.Value}");
            }
            else
            {
                var elementStrings = load.Elements.Select(e => $"{e.Type}={e.Value}");
                var joined = string.Join(" || ", elementStrings);
                writer.WriteLine($"    load {load.Net} ({joined})");
            }
        }
        foreach (var sweep in harness.Sweeps.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            if (sweep.IsAuto)
            {
                writer.WriteLine($"    sweep {sweep.Name} [Auto]");
            }
            else if (!string.IsNullOrWhiteSpace(sweep.Step))
            {
                writer.WriteLine(
                    $"    sweep {sweep.Name} [{sweep.Start}:{sweep.Step}:{sweep.Stop}]"
                );
            }
            else
            {
                writer.WriteLine($"    sweep {sweep.Name} [{sweep.Start}:{sweep.Stop}]");
            }
        }
        if (harness.Icmr is not null)
        {
            writer.WriteLine($"    icmr min={harness.Icmr.Min} max={harness.Icmr.Max}");
        }
        if (harness.Pvt.Count > 0)
        {
            writer.WriteLine($"    pvt {string.Join(", ", harness.Pvt)}");
        }
        writer.WriteLine("  }");
    }

    private static void WriteProvenance(ProvenanceBlock provenance, TextWriter writer)
    {
        writer.WriteLine("  provenance {");
        if (provenance.Sources.Count > 0)
        {
            foreach (var source in provenance.Sources)
            {
                var span =
                    source.FromLine.HasValue && source.ToLine.HasValue
                        ? $" [{source.FromLine}:{source.ToLine}]"
                        : "";
                writer.WriteLine($"    source \"{source.File}\"{span}");
            }
        }
        if (provenance.Transforms.Count > 0)
        {
            foreach (var transform in provenance.Transforms)
            {
                writer.WriteLine($"    transform \"{transform}\"");
            }
        }
        if (provenance.Aliases.Count > 0)
        {
            foreach (var alias in provenance.Aliases.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                writer.WriteLine($"    alias {alias.Key} = {alias.Value}");
            }
        }
        writer.WriteLine("  }");
    }

    private static void WriteBenchesSection(
        IReadOnlyList<BenchBinding> bindings,
        IReadOnlyList<BenchBindingExtension> extensions,
        TextWriter writer
    )
    {
        writer.WriteLine("  benches {");

        foreach (var binding in bindings.OrderBy(b => b.BindingName, StringComparer.Ordinal))
        {
            writer.WriteLine($"    bind {binding.BenchName} as {binding.BindingName} {{");
            var bindingExports = binding
                .Statements.OfType<BenchBindingMeasurementExport>()
                .ToList();
            foreach (var stmt in binding.Statements)
            {
                switch (stmt)
                {
                    case BenchTerminalMapping mapping:
                        writer.WriteLine(
                            $"      bench.{mapping.BenchTerminal}--dut.{mapping.DutPinRef}"
                        );
                        break;

                    case BenchDutConnection conn:
                        writer.WriteLine($"      dut.{conn.DutPinRef}--{conn.PinRef}");
                        break;

                    case BenchBindingInstance inst:
                        WriteInstance(inst.Instance, writer, indent: "      ");
                        break;
                }
            }

            if (bindingExports.Count > 0)
            {
                writer.WriteLine("      measurements {");
                foreach (var export in bindingExports.OrderBy(e => e.Name, StringComparer.Ordinal))
                {
                    var paramText = string.Join(
                        ", ",
                        export.Parameters.Select(p => $"{FormatBenchValueType(p.Type)} {p.Name}")
                    );
                    var sig =
                        export.Parameters.Count == 0 ? export.Name : $"{export.Name}({paramText})";

                    writer.WriteLine(
                        $"        measurement {sig} : {export.Unit} = {FormatMeasurementExpr(export.Target)}"
                    );
                }
                writer.WriteLine("      }");
            }

            writer.WriteLine("    }");
        }

        foreach (var ext in extensions.OrderBy(e => e.BindingName, StringComparer.Ordinal))
        {
            writer.WriteLine($"    extend {ext.BindingName} {{");
            var extExports = ext.Statements.OfType<BenchBindingMeasurementExport>().ToList();
            foreach (var stmt in ext.Statements)
            {
                switch (stmt)
                {
                    case BenchTerminalMapping mapping:
                        writer.WriteLine(
                            $"      bench.{mapping.BenchTerminal}--dut.{mapping.DutPinRef}"
                        );
                        break;

                    case BenchDutConnection conn:
                        writer.WriteLine($"      dut.{conn.DutPinRef}--{conn.PinRef}");
                        break;

                    case BenchBindingInstance inst:
                        WriteInstance(inst.Instance, writer, indent: "      ");
                        break;
                }
            }

            if (extExports.Count > 0)
            {
                writer.WriteLine("      measurements {");
                foreach (var export in extExports.OrderBy(e => e.Name, StringComparer.Ordinal))
                {
                    var paramText = string.Join(
                        ", ",
                        export.Parameters.Select(p => $"{FormatBenchValueType(p.Type)} {p.Name}")
                    );
                    var sig =
                        export.Parameters.Count == 0 ? export.Name : $"{export.Name}({paramText})";

                    writer.WriteLine(
                        $"        measurement {sig} : {export.Unit} = {FormatMeasurementExpr(export.Target)}"
                    );
                }
                writer.WriteLine("      }");
            }

            writer.WriteLine("    }");
        }

        writer.WriteLine("  }");
    }

    private static void WriteAnalysisDeclaration(
        AnalysisDeclaration analysis,
        TextWriter writer,
        string indent
    )
    {
        var args = analysis
            .Parameters.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={FormatMeasurementExpr(p.Value)}")
            .ToList();

        writer.WriteLine(
            $"{indent}{FormatBenchValueType(analysis.Type)} {analysis.Name} = new {FormatBenchValueType(analysis.Type)}({string.Join(", ", args)})"
        );
    }

    private static void WriteMeasurementDefinition(
        MeasurementDefinition measurement,
        TextWriter writer,
        string indent
    )
    {
        var paramText = string.Join(
            ", ",
            measurement.Parameters.Select(p => $"{FormatBenchValueType(p.Type)} {p.Name}")
        );
        var sig =
            measurement.Parameters.Count == 0
                ? measurement.Name
                : $"{measurement.Name}({paramText})";

        var overridePrefix = measurement.IsOverride ? "override " : string.Empty;
        writer.WriteLine($"{indent}{overridePrefix}measurement {sig} : {measurement.Unit} {{");
        foreach (var stmt in measurement.Body)
        {
            WriteBenchStatement(stmt, writer, indent: indent + "  ");
        }
        writer.WriteLine($"{indent}}}");
    }

    private static void WriteFunctionDefinition(
        FunctionDefinition fn,
        string keyword,
        TextWriter writer,
        string indent = ""
    )
    {
        var paramText = string.Join(
            ", ",
            fn.Parameters.Select(p => $"{FormatBenchValueType(p.Type)} {p.Name}")
        );

        writer.WriteLine(
            $"{indent}{keyword} {fn.Name}({paramText}) : {FormatBenchValueType(fn.ReturnType)} {{"
        );

        foreach (var stmt in fn.Body)
        {
            WriteBenchStatement(stmt, writer, indent: indent + "  ");
        }

        writer.WriteLine($"{indent}}}");
    }

    private static void WriteBenchStatement(BenchStatement stmt, TextWriter writer, string indent)
    {
        switch (stmt)
        {
            case BenchVarDecl v:
                writer.WriteLine(
                    $"{indent}{FormatBenchValueType(v.Type)} {v.Name} = {FormatMeasurementExpr(v.Expr)}"
                );
                break;

            case BenchIf i:
                writer.WriteLine($"{indent}if {FormatBoolExpr(i.Condition)} {{");
                foreach (var s in i.ThenBody)
                {
                    WriteBenchStatement(s, writer, indent: indent + "  ");
                }
                writer.WriteLine($"{indent}}}");
                if (i.ElseBody is not null)
                {
                    writer.WriteLine($"{indent}else {{");
                    foreach (var s in i.ElseBody)
                    {
                        WriteBenchStatement(s, writer, indent: indent + "  ");
                    }
                    writer.WriteLine($"{indent}}}");
                }
                break;

            case BenchReturn r:
                writer.WriteLine($"{indent}return {FormatMeasurementExpr(r.Expr)}");
                break;

            default:
                throw new InvalidOperationException(
                    $"Unhandled bench statement: {stmt.GetType().Name}"
                );
        }
    }

    private static string FormatBenchValueType(BenchValueType type) =>
        type switch
        {
            BenchValueType.Bool => "bool",
            // BenchValueType doesn't currently preserve stim/resp role; emit a valid terminal type token.
            BenchValueType.Terminal => "stim",
            BenchValueType.ACAnalysis => "ACAnalysis",
            BenchValueType.DCAnalysis => "DCAnalysis",
            BenchValueType.TranAnalysis => "TranAnalysis",
            BenchValueType.NoiseAnalysis => "NoiseAnalysis",
            BenchValueType.STBAnalysis => "STBAnalysis",
            _ => type.ToString(),
        };

    private static string FormatBoolExpr(BoolExpr expr)
    {
        return expr switch
        {
            BoolExists e => FormatScopedAccess(e.Ref),
            BoolTruthy t => FormatMeasurementExpr(t.Expr),
            BoolCompare c =>
                $"{FormatMeasurementExpr(c.Left)} {FormatComparisonOp(c.Op)} {FormatMeasurementExpr(c.Right)}",
            _ => throw new InvalidOperationException($"Unhandled bool expr: {expr.GetType().Name}"),
        };
    }

    private static string FormatComparisonOp(ComparisonOp op) =>
        op switch
        {
            ComparisonOp.Gte => ">=",
            ComparisonOp.Lte => "<=",
            ComparisonOp.Gt => ">",
            ComparisonOp.Lt => "<",
            ComparisonOp.Eq => "==",
            _ => throw new InvalidOperationException($"Unhandled comparison op: {op}"),
        };

    private static string FormatMeasurementExpr(MeasurementExpr expr)
    {
        return expr switch
        {
            MeasurementNumber n => n.Raw,
            MeasurementQuantity q => q.Raw,
            MeasurementPath p => p.Path,
            MeasurementScopedAccess s => FormatScopedAccess(s.Ref),
            MeasurementDutAccess d => $"dut.{d.PinRef}",
            MeasurementUnary u => $"{u.Op}{FormatMeasurementExpr(u.Operand)}",
            MeasurementBinary b =>
                $"({FormatMeasurementExpr(b.Left)} {b.Op} {FormatMeasurementExpr(b.Right)})",
            MeasurementCall c =>
                $"{c.Name}({string.Join(", ", c.Args.Select(FormatMeasurementArg))})",
            MeasurementMethodCall m =>
                $"{FormatMeasurementExpr(m.Receiver)}.{m.Method}({string.Join(", ", m.Args.Select(FormatMeasurementArg))})",
            MeasurementConditional c =>
                $"(if {FormatBoolExpr(c.Condition)} {{ {FormatMeasurementExpr(c.ThenExpr)} }} else {{ {FormatMeasurementExpr(c.ElseExpr)} }})",
            MeasurementBenchMeasurementRef r => FormatBenchMeasurementRef(r),
            _ => throw new InvalidOperationException(
                $"Unhandled measurement expr: {expr.GetType().Name}"
            ),
        };
    }

    private static string FormatBenchMeasurementRef(MeasurementBenchMeasurementRef r)
    {
        if (r.Args.Count == 0)
        {
            return $"{r.BindingAlias}::{r.MeasurementName}";
        }

        var args = r.Args.Select(FormatBenchMeasurementRefArg);
        return $"{r.BindingAlias}::{r.MeasurementName}({string.Join(", ", args)})";
    }

    private static string FormatBenchMeasurementRefArg(BenchMeasurementRefArg arg)
    {
        var value = FormatMeasurementExpr(arg.Expr);
        return arg.Name is null ? value : $"{arg.Name}={value}";
    }

    private static string FormatMeasurementArg(MeasurementCallArg arg)
    {
        var value = FormatMeasurementExpr(arg.Value);
        return arg.Name is null ? value : $"{arg.Name}={value}";
    }

    private static string FormatScopedAccess(ScopedValueRef r)
    {
        var scope = r.Scope switch
        {
            MeasurementScope.Env => "env",
            MeasurementScope.Constraints => "constraints",
            MeasurementScope.Harness => "harness",
            _ => throw new InvalidOperationException($"Unhandled scope: {r.Scope}"),
        };

        return $"{scope}.{r.Name}";
    }

    private static string FormatSizeExpr(SizePack pack)
    {
        var entries = pack.Entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        var parts = entries.Select(e => $"{e.Key}={e.Value}");
        return $"size({string.Join(", ", parts)})";
    }

    private static string FormatParamValue(ParamValue value)
    {
        if (value.Symbolic is not null)
        {
            return value.Symbolic;
        }
        if (value.Numeric is not null)
        {
            return value.Numeric;
        }
        if (value.Literal is not null)
        {
            return value.Literal;
        }
        return "";
    }
}
