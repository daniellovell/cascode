using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Cascode.ACIR;

/// <summary>
/// Writes ACIR documents to text format following canonical writer rules.
/// </summary>
public static class ACIRWriter
{
    /// <summary>
    /// Writes an ACIR document to a text writer.
    /// </summary>
    public static void Write(ACIRDocument document, TextWriter writer)
    {
        // Version declaration
        writer.WriteLine($"ACIR {ACIRVersion.Current}");
        writer.WriteLine();

        // Bundle type definitions
        foreach (
            var bundleType in document.BundleTypes.OrderBy(b => b.Name, StringComparer.Ordinal)
        )
        {
            WriteBundleType(bundleType, writer);
            writer.WriteLine();
        }

        // Trait definitions
        foreach (var trait in document.Traits.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            WriteTrait(trait, writer);
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
        writer.WriteLine($"bundle {bundleType.Name}:");
        foreach (var field in bundleType.Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            writer.WriteLine($"  {field.Key} : {field.Value}");
        }
    }

    private static void WriteTrait(TraitDefinition trait, TextWriter writer)
    {
        writer.WriteLine($"trait {trait.Name}:");

        // Ports
        foreach (var port in trait.Ports.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            writer.WriteLine($"  port {port.Name} : {port.Type}");
        }

        // Connectors
        if (trait.Connectors.Count > 0)
        {
            writer.WriteLine("  connectors:");
            foreach (
                var connector in trait.Connectors.OrderBy(
                    c => c.TargetTrait,
                    StringComparer.Ordinal
                )
            )
            {
                writer.WriteLine($"    to {connector.TargetTrait}:");
                foreach (var mapping in connector.Mappings)
                {
                    writer.WriteLine($"      {mapping.SourcePort} -> {mapping.TargetPort}");
                }
            }
        }
    }

    private static void WriteCircuit(Circuit circuit, TextWriter writer)
    {
        // Circuit header
        var header = $"circuit {circuit.Name}";
        if (circuit.Traits is { Count: > 0 })
        {
            header += $" : {string.Join(", ", circuit.Traits)}";
        }
        writer.WriteLine(header);

        // Level
        writer.WriteLine($"  level {circuit.Level}");

        // Inline
        if (circuit.Inline)
        {
            writer.WriteLine("  inline");
        }

        // Parameters
        foreach (var param in circuit.Parameters.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var defaultPart = param.Default is not null
                ? $" = {FormatParamValue(param.Default)}"
                : "";
            writer.WriteLine($"  param {param.Name} : {param.Type}{defaultPart}");
        }

        // Sizes
        foreach (var size in circuit.Sizes.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            if (size.Default is null)
            {
                writer.WriteLine($"  size {size.Name}");
                continue;
            }

            writer.WriteLine($"  size {size.Name} = {FormatSizePack(size.Default)}");
        }

        // Package
        if (!string.IsNullOrEmpty(circuit.Package))
        {
            writer.WriteLine($"  package {circuit.Package}");
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
            writer.WriteLine($"  port {port.Name} : {port.Type}");
        }

        // Slots (HL level)
        if (circuit.Slots.Count > 0)
        {
            foreach (var slot in circuit.Slots.OrderBy(s => s.Id, StringComparer.Ordinal))
            {
                WriteSlot(slot, writer);
            }
        }

        // Fill block (ML and EL levels)
        if (circuit.Fill is not null)
        {
            writer.WriteLine("  fill:");
            WriteFillBlock(circuit.Fill, writer);
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

        // Benches
        if (circuit.Benches is not null)
        {
            WriteBenches(circuit.Benches, writer);
        }

        // Provenance
        if (circuit.Provenance is not null)
        {
            WriteProvenance(circuit.Provenance, writer);
        }
    }

    private static void WriteSlot(SlotDeclaration slot, TextWriter writer)
    {
        var header = $"  slot {slot.Id}";
        if (slot.Bindings.Count > 0)
        {
            var bindings = string.Join(
                ", ",
                slot.Bindings.OrderBy(b => b.Key, StringComparer.Ordinal)
                    .Select(b => $"{b.Key}->{b.Value}")
            );
            header += $" ({bindings})";
        }
        header += " : ";
        if (slot.Traits.Count == 1)
        {
            header += slot.Traits[0];
        }
        else if (slot.Traits.Count > 1)
        {
            header += $"[{string.Join(", ", slot.Traits)}]";
        }
        writer.WriteLine(header);

        foreach (var param in slot.Params.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            writer.WriteLine($"    param {param.Key} = {FormatParamValue(param.Value)}");
        }
    }

    private static void WriteFillBlock(FillBlock fill, TextWriter writer)
    {
        // Nets first
        foreach (var net in fill.Nets.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            writer.WriteLine($"    net {net.Id} : {net.Domain}");
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

        // Connections
        foreach (var conn in fill.Connections.OrderBy(c => c.From, StringComparer.Ordinal))
        {
            writer.WriteLine($"    connect {conn.From} -> {conn.To}");
        }

        // Attach statements (EL level)
        foreach (var attach in fill.Attaches.OrderBy(a => a.SourceInstance, StringComparer.Ordinal))
        {
            WriteAttach(attach, writer);
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
                writer.WriteLine($"      {mapping.SourcePort} -> {mapping.TargetPort}");
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
        var header = $"    inst {inst.Id}";
        if (inst.Bindings.Count > 0 && inst.Bindings.Count <= 4)
        {
            // Use inline syntax for 4 or fewer simple connections
            var bindings = string.Join(
                ", ",
                inst.Bindings.OrderBy(b => b.Key, StringComparer.Ordinal)
                    .Select(b => $"{b.Key}->{b.Value}")
            );
            header += $" ({bindings})";
        }
        header += $" : {inst.Type}";
        writer.WriteLine(header);

        // If bindings weren't inline, write them indented
        if (inst.Bindings.Count > 4)
        {
            foreach (var binding in inst.Bindings.OrderBy(b => b.Key, StringComparer.Ordinal))
            {
                writer.WriteLine($"      {binding.Key} -> {binding.Value}");
            }
        }

        // Parameters
        foreach (var param in inst.Params.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            writer.WriteLine($"      param {param.Key} = {FormatParamValue(param.Value)}");
        }

        // Sizes
        foreach (var size in inst.Sizes.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            writer.WriteLine($"      size {size.Key} = {FormatSizePack(size.Value)}");
        }

        // Instance-level connects
        foreach (var conn in inst.Connects.OrderBy(c => c.From, StringComparer.Ordinal))
        {
            writer.WriteLine($"      connect {conn.From} -> {conn.To}");
        }
    }

    private static string FormatSizePack(SizePack pack)
    {
        var entries = pack.Entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        var parts = entries.Select(e => $"{e.Key}={e.Value}");
        return $"({string.Join(", ", parts)})";
    }

    private static void WriteDevice(DeviceDeclaration device, TextWriter writer)
    {
        var header = $"    {device.DeviceType} {device.Id}";
        if (device.Bindings.Count > 0 && device.Bindings.Count <= 4)
        {
            var bindings = string.Join(
                ", ",
                device
                    .Bindings.OrderBy(b => b.Key, StringComparer.Ordinal)
                    .Select(b => $"{b.Key}->{b.Value}")
            );
            header += $" ({bindings})";
        }
        header += " : ";
        var paramParts = device
            .Params.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}");
        header += string.Join(" ", paramParts);
        if (!string.IsNullOrEmpty(device.PdkDevice))
        {
            header += $" {device.PdkDevice}";
        }
        writer.WriteLine(header);

        // If bindings weren't inline, write them indented
        if (device.Bindings.Count > 4)
        {
            foreach (var binding in device.Bindings.OrderBy(b => b.Key, StringComparer.Ordinal))
            {
                writer.WriteLine($"      {binding.Key} -> {binding.Value}");
            }
        }
    }

    private static void WriteConstraints(ConstraintsBlock constraints, TextWriter writer)
    {
        writer.WriteLine("  constraints:");
        if (constraints.Numeric.Count > 0)
        {
            writer.WriteLine("    numeric:");
            foreach (var c in constraints.Numeric.OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                var scope = c.Node is not null ? $" @ {c.Node}" : "";
                writer.WriteLine($"      {c.Id} : {c.Metric}{scope} {c.Op} {c.Value}{c.Unit}");
            }
        }
        if (constraints.Tech.Count > 0)
        {
            writer.WriteLine("    tech:");
            foreach (var c in constraints.Tech.OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                writer.WriteLine($"      {c.Id} : {c.Param} {c.Op} {c.Value}{c.Unit} on {c.Scope}");
            }
        }
        if (constraints.Graph.Count > 0)
        {
            writer.WriteLine("    graph:");
            foreach (var c in constraints.Graph.OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                writer.WriteLine($"      {c.Id} : {c.Rule} ..."); // Simplified for now
            }
        }
        if (constraints.Measure.Count > 0)
        {
            writer.WriteLine("    measure:");
            foreach (var m in constraints.Measure.OrderBy(m => m.Id, StringComparer.Ordinal))
            {
                var node = m.Node is not null ? $" @ {m.Node}" : "";
                writer.WriteLine($"      {m.Id} : {m.Bench} {m.Metric}{node}");
            }
        }
    }

    private static void WriteHarness(HarnessBlock harness, TextWriter writer)
    {
        writer.WriteLine("  harness:");
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
        if (harness.Icmr is not null)
        {
            writer.WriteLine($"    icmr min={harness.Icmr.Min} max={harness.Icmr.Max}");
        }
        if (harness.Pvt.Count > 0)
        {
            writer.WriteLine($"    pvt {string.Join(", ", harness.Pvt)}");
        }
    }

    private static void WriteBenches(BenchesBlock benches, TextWriter writer)
    {
        writer.WriteLine("  benches:");
        foreach (var bench in benches.Benches.OrderBy(b => b.Name, StringComparer.Ordinal))
        {
            if (bench.Config.Count == 0)
            {
                writer.WriteLine($"    {bench.Name}");
            }
            else
            {
                writer.WriteLine($"    {bench.Name}:");
                foreach (var kvp in bench.Config.OrderBy(c => c.Key, StringComparer.Ordinal))
                {
                    writer.WriteLine($"      {kvp.Key} = {kvp.Value}");
                }
            }
        }
    }

    private static void WriteProvenance(ProvenanceBlock provenance, TextWriter writer)
    {
        writer.WriteLine("  provenance:");
        if (provenance.Sources.Count > 0)
        {
            writer.WriteLine("    sources:");
            foreach (var source in provenance.Sources)
            {
                var span =
                    source.FromLine.HasValue && source.ToLine.HasValue
                        ? $" [{source.FromLine}:{source.ToLine}]"
                        : "";
                writer.WriteLine($"      {source.File}{span}");
            }
        }
        if (provenance.Transforms.Count > 0)
        {
            writer.WriteLine("    transforms:");
            foreach (var transform in provenance.Transforms)
            {
                writer.WriteLine($"      {transform}");
            }
        }
        if (provenance.Aliases.Count > 0)
        {
            writer.WriteLine("    aliases:");
            foreach (var alias in provenance.Aliases.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                writer.WriteLine($"      {alias.Key} = {alias.Value}");
            }
        }
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
