using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Bench;

namespace Cascode.ACIR;

/// <summary>
/// Adapter that converts ACIR circuits and bench configs to TestbenchContext.
/// </summary>
public static class ACIRBenchAdapter
{
    /// <summary>
    /// Holds derived voltage and impedance parameters from harness.
    /// </summary>
    internal readonly record struct HarnessParameters(
        double Vcm,
        double BiasV,
        double LoadC,
        double SourceOhms,
        double RloadOhms);

    /// <summary>
    /// Converts an ACIR circuit and bench config to a TestbenchContext.
    /// </summary>
    /// <param name="circuit">ACIR circuit.</param>
    /// <param name="bench">Bench configuration.</param>
    /// <param name="backend">Backend type.</param>
    /// <param name="outputDir">Output directory for generated files.</param>
    /// <param name="workspaceRoot">Optional workspace root for template discovery.</param>
    /// <returns>TestbenchContext ready for TestbenchGenerator.</returns>
    public static TestbenchContext ToTestbenchContext(
        Circuit circuit,
        BenchConfig bench,
        BenchBackendType backend,
        string outputDir,
        string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(bench);

        var harnessSupplies = BuildHarnessSuppliesAndBiases(circuit);
        var harnessLoads = BuildHarnessLoads(circuit);
        var outNode = DetermineOutNode(circuit);
        var portList = BuildPortList(circuit);
        var genericModels = UsesGenericModels(circuit);
        var harnessParams = DeriveVoltageAndImpedance(circuit);
        var (acStartHz, acStopHz) = DeriveAcSweepFromConstraints(circuit);

        var designFile = $"{circuit.Name}.sp";
        var args = new Dictionary<string, object?>
        {
            ["harness"] = "acir_template",
            ["circuit_name"] = circuit.Name,
            ["design_file"] = designFile,
            ["port_list"] = string.Join(" ", portList),
            ["out_node"] = outNode,
            ["generic_models"] = genericModels,
            ["vcm"] = harnessParams.Vcm,
            ["bias_v"] = harnessParams.BiasV,
            ["harness_supplies"] = harnessSupplies,
            ["harness_loads"] = harnessLoads,
            ["source_ohms"] = harnessParams.SourceOhms,
            ["cload_f"] = harnessParams.LoadC,
            ["rload_ohms"] = harnessParams.RloadOhms,
            ["ac_mag"] = 1.0,
            ["ac_start_hz"] = acStartHz,
            ["ac_stop_hz"] = acStopHz,
            ["includes_with_section"] = new List<string>(),
            ["includes_without_section"] = new List<string> { designFile }
        };

        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            args["start_dir"] = outputDir;
        }

        var spec = new TestbenchSpec
        {
            Backend = backend,
            Name = bench.Name,
            JobDir = outputDir,
            ResultsCsv = $"{circuit.Name}_{bench.Name}_results.json",
            TemperatureC = 27.0
        };

        return new TestbenchContext
        {
            Spec = spec,
            WorkspaceRoot = workspaceRoot ?? Directory.GetCurrentDirectory(),
            PdkRoot = string.Empty,
            DeckPaths = Array.Empty<string>(),
            Args = args
        };
    }

    /// <summary>
    /// Builds the combined list of harness supplies and biases for template rendering.
    /// </summary>
    internal static List<object> BuildHarnessSuppliesAndBiases(Circuit circuit)
    {
        var result = new List<object>();

        if (circuit.Harness?.Supplies != null)
        {
            foreach (var supply in circuit.Harness.Supplies)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["net"] = supply.Net,
                    ["value"] = supply.Value
                });
            }
        }

        if (circuit.Harness?.Biases != null)
        {
            foreach (var bias in circuit.Harness.Biases)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["net"] = bias.Net,
                    ["value"] = bias.Value
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the list of harness loads (capacitive loads) for template rendering.
    /// </summary>
    internal static List<object> BuildHarnessLoads(Circuit circuit)
    {
        var result = new List<object>();

        if (circuit.Harness?.Loads == null)
            return result;

        foreach (var load in circuit.Harness.Loads)
        {
            if (load.C != null)
            {
                result.Add(new Dictionary<string, object>
                {
                    ["net"] = load.Net,
                    ["c"] = load.C
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Determines the output node name from circuit ports.
    /// Returns the first port named "OUT" (case-insensitive), or the first port, or "OUT" as fallback.
    /// </summary>
    internal static string DetermineOutNode(Circuit circuit)
    {
        return circuit.Ports
            .FirstOrDefault(p => p.Name.Equals("OUT", StringComparison.OrdinalIgnoreCase))?.Name
            ?? circuit.Ports.FirstOrDefault()?.Name
            ?? "OUT";
    }

    /// <summary>
    /// Builds the port list for DUT instantiation by combining ports, supplies, and grounds.
    /// </summary>
    internal static List<string> BuildPortList(Circuit circuit)
    {
        var result = new List<string>();

        foreach (var port in circuit.Ports)
            result.Add(port.Name);

        foreach (var supply in circuit.Supplies)
            result.Add(supply);

        foreach (var ground in circuit.Grounds)
            result.Add(ground);

        return result;
    }

    /// <summary>
    /// Checks if the circuit uses generic device models (nmos/pmos without PDK binding).
    /// </summary>
    internal static bool UsesGenericModels(Circuit circuit)
    {
        return circuit.Fill?.Devices?.Any(d =>
        {
            var modelName = d.PdkDevice ?? d.DeviceType;
            return modelName.Equals("nmos", StringComparison.OrdinalIgnoreCase) ||
                   modelName.Equals("pmos", StringComparison.OrdinalIgnoreCase);
        }) ?? false;
    }

    /// <summary>
    /// Derives voltage and impedance parameters from harness configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when supply value cannot be parsed to determine common-mode voltage.
    /// </exception>
    internal static HarnessParameters DeriveVoltageAndImpedance(Circuit circuit)
    {
        var vcm = DeriveCommonModeVoltage(circuit);
        var biasV = vcm;
        var loadC = DeriveLoadCapacitance(circuit);
        var sourceOhms = DeriveSourceImpedance(circuit);
        const double rloadOhms = 1e9;

        return new HarnessParameters(vcm, biasV, loadC, sourceOhms, rloadOhms);
    }

    private static double DeriveCommonModeVoltage(Circuit circuit)
    {
        const double defaultVcm = 0.9;

        if (circuit.Harness?.Supplies == null || circuit.Harness.Supplies.Count == 0)
            return defaultVcm;

        var firstSupply = circuit.Harness.Supplies[0];
        if (TryParseValue(firstSupply.Value, out var supplyVal))
            return supplyVal / 2.0;

        throw new InvalidOperationException(
            $"Unable to parse supply value '{firstSupply.Value}' in harness for circuit '{circuit.Name}'. " +
            "Value must be a valid number (e.g. '1.8', '1.8V') to automatically determine common-mode voltage.");
    }

    private static double DeriveLoadCapacitance(Circuit circuit)
    {
        const double defaultLoadC = 1e-12;

        if (circuit.Harness?.Loads == null || circuit.Harness.Loads.Count == 0)
            return defaultLoadC;

        var firstLoad = circuit.Harness.Loads[0];
        if (firstLoad.C != null && TryParseValue(firstLoad.C, out var parsedC))
            return parsedC;

        return defaultLoadC;
    }

    private static double DeriveSourceImpedance(Circuit circuit)
    {
        const double defaultSourceOhms = 50.0;

        if (circuit.Harness?.Sources == null || circuit.Harness.Sources.Count == 0)
            return defaultSourceOhms;

        var firstSource = circuit.Harness.Sources[0];
        if (firstSource.Z != null && TryParseValue(firstSource.Z, out var parsedZ))
            return parsedZ;

        return defaultSourceOhms;
    }

    /// <summary>
    /// Generates a testbench file directly using template discovery and rendering.
    /// </summary>
    /// <param name="circuit">ACIR circuit.</param>
    /// <param name="bench">Bench configuration.</param>
    /// <param name="backend">Backend type.</param>
    /// <param name="outputDir">Output directory for generated files.</param>
    /// <param name="workspaceRoot">Optional workspace root for template discovery.</param>
    /// <returns>TestbenchFiles with path to generated netlist.</returns>
    public static TestbenchFiles GenerateTestbench(
        Circuit circuit,
        BenchConfig bench,
        BenchBackendType backend,
        string outputDir,
        string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(bench);

        var harness = new ACIRTemplateHarness();
        var context = ToTestbenchContext(circuit, bench, backend, outputDir, workspaceRoot);
        var plan = harness.BuildPlan(context);

        // Find template
        var templatePath = plan.Data.TryGetValue("template_path", out var tp) ? tp?.ToString() : null;
        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            throw new InvalidOperationException($"Template not found: {templatePath}");
        }

        // Load template
        var templateText = File.ReadAllText(templatePath);

        // Get template model from plan
        var templateModel = plan.Data.TryGetValue("template_model", out var tm) ? tm : null;
        if (templateModel == null)
        {
            throw new InvalidOperationException("Template model not found in plan");
        }

        // Render template
        var netlistText = TemplateRenderer.Render(templateText, templateModel);

        // Write files
        Directory.CreateDirectory(outputDir);
        var netlistPath = Path.Combine(outputDir, plan.NetlistName);
        File.WriteAllText(netlistPath, netlistText);

        var specPath = Path.Combine(outputDir, "spec.json");
        File.WriteAllText(specPath, System.Text.Json.JsonSerializer.Serialize(context.Spec, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        return new TestbenchFiles
        {
            RootDir = outputDir,
            NetlistPath = netlistPath,
            SpecPath = specPath,
            ResultsCsv = context.Spec.ResultsCsv,
            RunnerPath = string.Empty
        };
    }

    /// <summary>
    /// Derives AC sweep start/stop frequencies from circuit constraints.
    /// Uses GainBandwidth constraint if present, otherwise defaults to 1Hz-10GHz.
    /// </summary>
    private static (double acStartHz, double acStopHz) DeriveAcSweepFromConstraints(Circuit circuit)
    {
        const double DefaultStartHz = 1.0;
        const double DefaultStopHz = 10e9;

        if (circuit.Constraints?.Numeric == null || circuit.Constraints.Numeric.Count == 0)
        {
            return (DefaultStartHz, DefaultStopHz);
        }

        // Look for GainBandwidth or similar frequency-related constraints
        var gbwConstraint = circuit.Constraints.Numeric.FirstOrDefault(c =>
            c.Metric.Equals("GainBandwidth", StringComparison.OrdinalIgnoreCase) ||
            c.Metric.Equals("GBW", StringComparison.OrdinalIgnoreCase) ||
            c.Metric.Equals("UGF", StringComparison.OrdinalIgnoreCase) ||
            c.Metric.Equals("UnityGainFrequency", StringComparison.OrdinalIgnoreCase));

        if (gbwConstraint != null && TryParseConstraintValue(gbwConstraint.Value, out var gbwHz))
        {
            // Sweep should cover well beyond the expected GBW
            // Start at 1Hz (or 1/1000 of GBW if GBW is very high)
            // Stop at 10x the constrained GBW value
            var acStartHz = Math.Max(1.0, gbwHz / 1000.0);
            var acStopHz = Math.Max(gbwHz * 10.0, 1e9); // At least 1GHz

            return (acStartHz, acStopHz);
        }

        // Look for bandwidth-related constraints
        var bwConstraint = circuit.Constraints.Numeric.FirstOrDefault(c =>
            c.Metric.Equals("Bandwidth", StringComparison.OrdinalIgnoreCase) ||
            c.Metric.Equals("3dBBandwidth", StringComparison.OrdinalIgnoreCase));

        if (bwConstraint != null && TryParseConstraintValue(bwConstraint.Value, out var bwHz))
        {
            var acStartHz = Math.Max(1.0, bwHz / 100.0);
            var acStopHz = Math.Max(bwHz * 100.0, 1e9);

            return (acStartHz, acStopHz);
        }

        return (DefaultStartHz, DefaultStopHz);
    }

    /// <summary>
    /// Parses a constraint value string (e.g., "100M") into a double.
    /// Only supports positive SI prefixes (T, G, M, K) since frequency constraints don't use sub-unity values.
    /// </summary>
    private static bool TryParseConstraintValue(string valueStr, out double result) =>
        TryParseSIValue(valueStr, out result, stripUnits: false, allowSubUnity: false);

    /// <summary>
    /// Tries to parse a value string with SI unit suffix into a double.
    /// Supports full SI prefixes (k, M, G, T, m, u, n, p, f) and strips common unit suffixes.
    /// </summary>
    private static bool TryParseValue(string valueStr, out double result) =>
        TryParseSIValue(valueStr, out result, stripUnits: true, allowSubUnity: true);

    /// <summary>
    /// Core SI value parser with configurable behavior.
    /// </summary>
    /// <param name="valueStr">Input string to parse.</param>
    /// <param name="result">Parsed numeric result.</param>
    /// <param name="stripUnits">Whether to strip unit suffixes (V, F, ohm, Hz, etc.).</param>
    /// <param name="allowSubUnity">Whether to recognize sub-unity prefixes (m, u, n, p, f).</param>
    private static bool TryParseSIValue(string valueStr, out double result, bool stripUnits, bool allowSubUnity)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(valueStr))
            return false;

        var cleanedValue = valueStr.Trim();

        if (stripUnits)
        {
            foreach (var suffix in new[] { "V", "F", "ohm", "Ohm", "Hz", "W", "A", "s", "S" })
            {
                if (cleanedValue.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    cleanedValue = cleanedValue[..^suffix.Length].Trim();
                    break;
                }
            }
        }

        if (cleanedValue.Length == 0)
            return false;

        var multiplier = 1.0;
        if (char.IsLetter(cleanedValue[^1]))
        {
            var lastChar = cleanedValue[^1];
            var upperChar = char.ToUpperInvariant(lastChar);

            multiplier = upperChar switch
            {
                'T' => 1e12,
                'G' => 1e9,
                'M' => 1e6,
                'K' => 1e3,
                'U' when allowSubUnity => 1e-6,
                'N' when allowSubUnity => 1e-9,
                'P' when allowSubUnity => 1e-12,
                'F' when allowSubUnity => 1e-15,
                _ => 1.0
            };

            // Handle lowercase 'm' for milli separately (uppercase 'M' is mega)
            if (allowSubUnity && lastChar == 'm')
            {
                multiplier = 1e-3;
            }

            if (multiplier != 1.0)
            {
                cleanedValue = cleanedValue[..^1];
            }
        }

        if (double.TryParse(cleanedValue, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed * multiplier;
            return true;
        }

        return false;
    }
}

