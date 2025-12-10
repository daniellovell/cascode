using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cascode.Bench;

namespace Cascode.ACIR;

/// <summary>
/// Emits SPICE netlists from ACIR EL documents.
/// </summary>
/// <remarks>
/// The emitter generates ngspice-compatible SPICE netlists from EL-level ACIR circuits.
/// It produces:
/// - Design subcircuit files (.sp) with device instantiations
/// - Testbench files that instantiate the design with harness elements
/// 
/// Device terminal ordering follows SPICE conventions:
/// - MOSFETs: D G S B (drain, gate, source, bulk)
/// - Two-terminal devices (R, C, L): P N (positive, negative)
/// - Diodes: A K (anode, cathode)
/// 
/// When devices use generic model names (nmos, pmos) without PDK-specific models,
/// the emitter includes Level-1 MOSFET model definitions for ngspice simulation.
/// </remarks>
public static class SpiceEmitter
{
    private static readonly HashSet<string> GenericMosfetModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "nmos", "pmos"
    };

    /// <summary>
    /// Emits a SPICE subcircuit definition for an EL-level circuit.
    /// </summary>
    /// <param name="circuit">The circuit to emit (must be EL level).</param>
    /// <param name="writer">Text writer for output.</param>
    /// <exception cref="InvalidOperationException">Thrown if circuit is not EL level.</exception>
    /// <remarks>
    /// Output format:
    /// <code>
    /// * CircuitName - Generated from ACIR EL
    /// .subckt CircuitName port1 port2 ... supply1 ... ground1 ...
    /// * Internal nets: net1, net2, ...
    /// M... (device instances)
    /// .ends CircuitName
    /// </code>
    /// Port ordering: declared ports, then supplies, then grounds.
    /// </remarks>
    public static void EmitDesign(Circuit circuit, TextWriter writer)
    {
        if (circuit.Level != ACIRLevel.EL)
        {
            throw new InvalidOperationException(
                $"SpiceEmitter requires EL-level circuit, but '{circuit.Name}' is {circuit.Level}.");
        }

        // Header comment
        writer.WriteLine($"* {circuit.Name} - Generated from ACIR EL");
        writer.WriteLine();

        // Build port list: ports first, then supplies, then grounds
        var portList = new List<string>();
        foreach (var port in circuit.Ports)
        {
            portList.Add(port.Name);
        }
        foreach (var supply in circuit.Supplies)
        {
            portList.Add(supply);
        }
        foreach (var ground in circuit.Grounds)
        {
            portList.Add(ground);
        }

        writer.WriteLine($".subckt {circuit.Name} {string.Join(" ", portList)}");
        writer.WriteLine();

        // Internal nets comment
        if (circuit.Fill?.Nets.Count > 0)
        {
            var netNames = circuit.Fill.Nets
                .OrderBy(n => n.Id, StringComparer.Ordinal)
                .Select(n => n.Id);
            writer.WriteLine($"* Internal nets: {string.Join(", ", netNames)}");
            writer.WriteLine();
        }

        // Emit devices
        if (circuit.Fill?.Devices.Count > 0)
        {
            foreach (var device in circuit.Fill.Devices.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                EmitDevice(device, writer);
            }
        }

        writer.WriteLine();
        writer.WriteLine($".ends {circuit.Name}");
    }

    /// <summary>
    /// Emits a SPICE testbench for a given bench configuration.
    /// </summary>
    /// <param name="circuit">The circuit containing the bench.</param>
    /// <param name="bench">The bench configuration.</param>
    /// <param name="designPath">Path to the design .sp file to include.</param>
    /// <param name="writer">Text writer for output.</param>
    /// <param name="backend">Backend type for testbench generation.</param>
    /// <exception cref="InvalidOperationException">Thrown if circuit is not EL level.</exception>
    /// <remarks>
    /// This method is deprecated. Use Emit() with backend parameter instead.
    /// The testbench is now generated using templates via TestbenchGenerator.
    /// </remarks>
    [Obsolete("Use Emit() method with backend parameter instead")]
    public static void EmitTestbench(Circuit circuit, BenchConfig bench, string designPath, TextWriter writer, BenchBackendType backend = BenchBackendType.Ngspice)
    {
        if (circuit.Level != ACIRLevel.EL)
        {
            throw new InvalidOperationException(
                $"SpiceEmitter requires EL-level circuit, but '{circuit.Name}' is {circuit.Level}.");
        }

        var title = $"{circuit.Name}_{bench.Name}";

        // Header
        writer.WriteLine($"* {title} - Generated from ACIR EL");
        writer.WriteLine($".title {title}");
        writer.WriteLine();

        // Emit generic model definitions if circuit uses generic devices
        var genericModels = GetRequiredGenericModels(circuit);
        if (genericModels.Count > 0)
        {
            EmitGenericModels(genericModels, writer);
            writer.WriteLine();
        }

        // Include design
        writer.WriteLine($".include \"{designPath}\"");
        writer.WriteLine();

        // Harness section
        writer.WriteLine("* Harness");
        if (circuit.Harness is not null)
        {
            EmitHarness(circuit, writer);
        }
        writer.WriteLine();

        // DUT instantiation
        writer.WriteLine("* DUT");
        EmitDutInstantiation(circuit, writer);
        writer.WriteLine();

        // Analysis commands based on bench type
        EmitAnalysis(bench, writer);

        writer.WriteLine(".end");
    }

    /// <summary>
    /// Emits all outputs for an ACIR document: design netlist and testbenches.
    /// </summary>
    /// <param name="doc">The ACIR document.</param>
    /// <param name="outputDir">Output directory for generated files.</param>
    /// <param name="backend">Backend type for testbench generation (default: ngspice).</param>
    /// <param name="workspaceRoot">Optional workspace root for template discovery.</param>
    /// <returns>Result containing paths to generated files.</returns>
    /// <remarks>
    /// Processes all EL-level circuits in the document:
    /// - Generates {CircuitName}.sp for each circuit
    /// - Generates {CircuitName}_{BenchName}.sp for each bench using templates
    /// Output directory is created if it doesn't exist.
    /// Non-EL circuits are silently skipped.
    /// </remarks>
    public static SpiceEmitResult Emit(ACIRDocument doc, string outputDir, BenchBackendType backend = BenchBackendType.Ngspice, string? workspaceRoot = null)
    {
        var result = new SpiceEmitResult();
        Directory.CreateDirectory(outputDir);

        foreach (var circuit in doc.Circuits)
        {
            if (circuit.Level != ACIRLevel.EL)
            {
                continue;
            }

            // Emit design netlist
            var designPath = Path.Combine(outputDir, $"{circuit.Name}.sp");
            using (var writer = File.CreateText(designPath))
            {
                EmitDesign(circuit, writer);
            }
            result.DesignPaths.Add(designPath);

            // Emit testbenches using template-based generation
            if (circuit.Benches?.Benches.Count > 0)
            {
                foreach (var bench in circuit.Benches.Benches)
                {
                    var files = ACIRBenchAdapter.GenerateTestbench(circuit, bench, backend, outputDir, workspaceRoot);
                    result.TestbenchPaths.Add(files.NetlistPath);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Emits a SPICE element line for a device declaration.
    /// </summary>
    /// <param name="device">Device to emit.</param>
    /// <param name="writer">Text writer for output.</param>
    /// <exception cref="InvalidOperationException">Thrown if device type is unknown or required terminals are missing.</exception>
    private static void EmitDevice(DeviceDeclaration device, TextWriter writer)
    {
        var spiceType = device.DeviceType.ToLowerInvariant() switch
        {
            "nmos" or "pmos" => "M",
            "resistor" => "R",
            "capacitor" => "C",
            "inductor" => "L",
            "diode" => "D",
            _ => throw new InvalidOperationException($"Unknown device type: {device.DeviceType}")
        };

        var sb = new StringBuilder();
        sb.Append(spiceType);
        sb.Append(device.Id);
        sb.Append(' ');

        // Terminal ordering and parameters depend on device type
        if (spiceType == "M")
        {
            EmitMosfetTerminalsAndParams(device, sb);
        }
        else if (spiceType is "R" or "C" or "L")
        {
            // Two-terminal: P N
            sb.Append(GetBinding(device, "P"));
            sb.Append(' ');
            sb.Append(GetBinding(device, "N"));
            sb.Append(' ');

            // Value parameter
            var valueKey = spiceType switch
            {
                "R" => "R",
                "C" => "C",
                "L" => "L",
                _ => throw new InvalidOperationException()
            };
            if (device.Params.TryGetValue(valueKey, out var value))
            {
                sb.Append(value);
            }
        }
        else if (spiceType == "D")
        {
            // Diode: A K
            sb.Append(GetBinding(device, "A"));
            sb.Append(' ');
            sb.Append(GetBinding(device, "K"));
            sb.Append(' ');
            sb.Append(device.PdkDevice ?? "D");
        }

        writer.WriteLine(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Emits MOSFET terminal connections and parameters.
    /// </summary>
    /// <param name="device">MOSFET device declaration.</param>
    /// <param name="sb">StringBuilder to append to.</param>
    private static void EmitMosfetTerminalsAndParams(DeviceDeclaration device, StringBuilder sb)
    {
        // MOSFET terminal ordering: D G S B
        sb.Append(GetBinding(device, "D"));
        sb.Append(' ');
        sb.Append(GetBinding(device, "G"));
        sb.Append(' ');
        sb.Append(GetBinding(device, "S"));
        sb.Append(' ');
        sb.Append(GetBinding(device, "B"));
        sb.Append(' ');

        // Model name (PDK device or generic)
        sb.Append(device.PdkDevice ?? device.DeviceType);
        sb.Append(' ');

        // Parameters: W, L, m
        if (device.Params.TryGetValue("W", out var w))
        {
            sb.Append($"W={w} ");
        }
        if (device.Params.TryGetValue("L", out var l))
        {
            sb.Append($"L={l} ");
        }
        if (device.Params.TryGetValue("M", out var m))
        {
            sb.Append($"m={m}");
        }
    }

    /// <summary>
    /// Gets the net name bound to a device terminal.
    /// </summary>
    /// <param name="device">Device declaration.</param>
    /// <param name="terminal">Terminal name (D, G, S, B for MOSFETs; P, N for passives).</param>
    /// <returns>Net name bound to the terminal.</returns>
    /// <exception cref="InvalidOperationException">Thrown if terminal is not bound.</exception>
    private static string GetBinding(DeviceDeclaration device, string terminal)
    {
        if (device.Bindings.TryGetValue(terminal, out var net))
        {
            return net;
        }
        throw new InvalidOperationException(
            $"Device '{device.Id}' missing required terminal '{terminal}'.");
    }

    /// <summary>
    /// Emits harness elements (voltage sources, loads, source impedances).
    /// </summary>
    /// <param name="circuit">Circuit containing harness block.</param>
    /// <param name="writer">Text writer for output.</param>
    private static void EmitHarness(Circuit circuit, TextWriter writer)
    {
        var harness = circuit.Harness!;

        // Supply voltage sources
        foreach (var supply in harness.Supplies)
        {
            writer.WriteLine($"V{supply.Net} {supply.Net} 0 DC {supply.Value}");
        }

        // Bias voltage sources (DC only, no AC)
        foreach (var bias in harness.Biases)
        {
            writer.WriteLine($"V{bias.Net} {bias.Net} 0 DC {bias.Value}");
        }

        // Input sources - simplified: DC bias with AC stimulus
        foreach (var source in harness.Sources)
        {
            // Default to mid-supply bias with AC stimulus
            writer.WriteLine($"V{source.Net} {source.Net} 0 DC 0.9 AC 1");
            if (source.Z is not null)
            {
                writer.WriteLine($"R{source.Net}_Z {source.Net}_int {source.Net} {source.Z}");
            }
        }

        // Load elements
        foreach (var load in harness.Loads)
        {
            if (load.C is not null)
            {
                writer.WriteLine($"C{load.Net}_load {load.Net} 0 {load.C}");
            }
        }
    }

    /// <summary>
    /// Emits the DUT instantiation line (X-element).
    /// </summary>
    /// <param name="circuit">Circuit to instantiate.</param>
    /// <param name="writer">Text writer for output.</param>
    private static void EmitDutInstantiation(Circuit circuit, TextWriter writer)
    {
        // Build port list for subcircuit instantiation
        var portList = new List<string>();
        foreach (var port in circuit.Ports)
        {
            portList.Add(port.Name);
        }
        foreach (var supply in circuit.Supplies)
        {
            portList.Add(supply);
        }
        foreach (var ground in circuit.Grounds)
        {
            portList.Add(ground);
        }

        writer.WriteLine($"XDUT {string.Join(" ", portList)} {circuit.Name}");
    }

    /// <summary>
    /// Emits analysis commands based on bench type.
    /// </summary>
    /// <param name="bench">Bench configuration.</param>
    /// <param name="writer">Text writer for output.</param>
    /// <remarks>
    /// Bench type is inferred from the bench name:
    /// - "AC" → AC sweep (op + ac dec)
    /// - "STEP" or "TRAN" → Transient analysis (op + tran)
    /// - Default → DC operating point only
    /// </remarks>
    private static void EmitAnalysis(BenchConfig bench, TextWriter writer)
    {
        writer.WriteLine(".control");

        // Determine analysis type from bench name
        var benchName = bench.Name.ToUpperInvariant();
        if (benchName.Contains("AC"))
        {
            writer.WriteLine("op");
            writer.WriteLine("ac dec 100 1 10G");
        }
        else if (benchName.Contains("STEP") || benchName.Contains("TRAN"))
        {
            writer.WriteLine("op");
            writer.WriteLine("tran 1n 100n");
        }
        else if (benchName.Contains("DC"))
        {
            writer.WriteLine("op");
        }
        else
        {
            // Default to DC operating point
            writer.WriteLine("op");
        }

        writer.WriteLine("quit");
        writer.WriteLine(".endc");
    }

    /// <summary>
    /// Determines which generic MOSFET models are needed based on device declarations.
    /// </summary>
    /// <param name="circuit">The circuit to analyze.</param>
    /// <returns>Set of generic model names that need definitions.</returns>
    private static HashSet<string> GetRequiredGenericModels(Circuit circuit)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (circuit.Fill?.Devices is null)
        {
            return required;
        }

        foreach (var device in circuit.Fill.Devices)
        {
            var modelName = device.PdkDevice ?? device.DeviceType;
            if (GenericMosfetModels.Contains(modelName))
            {
                required.Add(modelName.ToLowerInvariant());
            }
        }

        return required;
    }

    /// <summary>
    /// Emits Level-1 MOSFET model definitions for ngspice simulation.
    /// </summary>
    /// <param name="models">Set of model names to emit (nmos, pmos).</param>
    /// <param name="writer">Text writer for output.</param>
    private static void EmitGenericModels(HashSet<string> models, TextWriter writer)
    {
        writer.WriteLine("* Generic MOSFET models for simulation");
        foreach (var model in models.OrderBy(m => m, StringComparer.Ordinal))
        {
            var modelLine = model switch
            {
                "nmos" => ".model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04",
                "pmos" => ".model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05",
                _ => null
            };
            if (modelLine is not null)
            {
                writer.WriteLine(modelLine);
            }
        }
    }
}

/// <summary>
/// Result of SPICE emission containing paths to generated files.
/// </summary>
public sealed class SpiceEmitResult
{
    /// <summary>
    /// Paths to generated design netlist files.
    /// </summary>
    public List<string> DesignPaths { get; } = new();

    /// <summary>
    /// Paths to generated testbench files.
    /// </summary>
    public List<string> TestbenchPaths { get; } = new();
}
