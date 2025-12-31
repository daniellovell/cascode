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
        double RloadOhms
    );

    /// <summary>
    /// Converts an ACIR circuit and bench config to a TestbenchContext.
    /// </summary>
    /// <param name="circuit">ACIR circuit.</param>
    /// <param name="bench">Bench configuration.</param>
    /// <param name="backend">Backend type.</param>
    /// <param name="outputDir">Output directory for generated files.</param>
    /// <param name="workspaceRoot">Optional workspace root for template discovery.</param>
    /// <param name="includeResolution">Optional include resolution for PDK model decks.</param>
    /// <returns>TestbenchContext ready for TestbenchGenerator.</returns>
    public static TestbenchContext ToTestbenchContext(
        Circuit circuit,
        BenchConfig bench,
        BenchBackendType backend,
        string outputDir,
        string? workspaceRoot = null,
        BenchIncludeResolution? includeResolution = null
    )
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
        var passbandFreqHz = DerivePassbandMeasurementFrequency(circuit, acStartHz, acStopHz);
        var sweepDict = BuildSweepDictionary(circuit);

        // Determine if bench is differential based on name
        var isDifferential = bench.Name.StartsWith("FD", StringComparison.OrdinalIgnoreCase);
        var loadElements = GenerateLoadElements(circuit, isDifferential);
        var supplyElements = GenerateSupplyElements(circuit);

        var designFile = $"{circuit.Name}.sp";
        var includesWithSection =
            includeResolution
                ?.WithSection?.Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            ?? new List<string>();

        var includesWithoutSection =
            includeResolution
                ?.WithoutSection?.Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            ?? new List<string>();

        if (!includesWithoutSection.Contains(designFile, StringComparer.OrdinalIgnoreCase))
        {
            includesWithoutSection.Add(designFile);
        }

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
            ["passband_freq_hz"] = passbandFreqHz,
            ["stb_start_hz"] = acStartHz,
            ["stb_stop_hz"] = acStopHz,
            ["load_elements"] = loadElements,
            ["supply_elements"] = supplyElements,
            ["includes_with_section"] = includesWithSection,
            ["includes_without_section"] = includesWithoutSection,
            ["section"] = includeResolution?.Section,
        };

        // Add sweep conditions - templates access them as sweep.ConditionName
        foreach (var kvp in sweepDict)
        {
            args[$"sweep.{kvp.Key}"] = kvp.Value;
        }

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
            TemperatureC = 27.0,
        };

        return new TestbenchContext
        {
            Spec = spec,
            WorkspaceRoot = workspaceRoot ?? Directory.GetCurrentDirectory(),
            PdkRoot = string.Empty,
            DeckPaths = Array.Empty<string>(),
            Args = args,
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
                result.Add(
                    new Dictionary<string, object>
                    {
                        ["net"] = supply.Net,
                        ["value"] = supply.Value,
                    }
                );
            }
        }

        if (circuit.Harness?.Biases != null)
        {
            foreach (var bias in circuit.Harness.Biases)
            {
                result.Add(
                    new Dictionary<string, object> { ["net"] = bias.Net, ["value"] = bias.Value }
                );
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the list of harness loads (capacitive and resistive loads) for template rendering.
    /// </summary>
    internal static List<object> BuildHarnessLoads(Circuit circuit)
    {
        var result = new List<object>();

        if (circuit.Harness?.Loads == null)
            return result;

        foreach (var load in circuit.Harness.Loads)
        {
            if (load.Elements.Count == 0)
                continue;

            var data = new Dictionary<string, object> { ["net"] = load.Net };

            var cs = load.Elements.Where(e => e.Type == "C").Select(e => e.Value).ToList();
            var rs = load.Elements.Where(e => e.Type == "R").Select(e => e.Value).ToList();

            if (cs.Count > 0)
                data["cs"] = cs;
            if (rs.Count > 0)
                data["rs"] = rs;

            // Compute halved values for FD templates (split load across differential outputs)
            if (cs.Count > 0)
            {
                var csHalf = new List<string>();
                foreach (var c in cs)
                {
                    if (TryParseValue(c, out var cValue))
                    {
                        csHalf.Add(FormatSIValue(cValue / 2.0));
                    }
                    else
                    {
                        throw new ArgumentException(
                            $"Unable to parse capacitance load value '{c}' for net '{load.Net}' in circuit '{circuit.Name}'. "
                                + "Value must be a valid number with optional SI prefix (e.g., '1p', '10pF', '500f').",
                            paramName: null
                        );
                    }
                }
                data["cs_half"] = csHalf;
            }

            if (rs.Count > 0)
            {
                var rsHalf = new List<string>();
                foreach (var r in rs)
                {
                    if (TryParseValue(r, out var rValue))
                    {
                        rsHalf.Add(FormatSIValue(rValue / 2.0));
                    }
                    else
                    {
                        throw new ArgumentException(
                            $"Unable to parse resistance load value '{r}' for net '{load.Net}' in circuit '{circuit.Name}'. "
                                + "Value must be a valid number with optional SI prefix (e.g., '1K', '10KOhm', '500').",
                            paramName: null
                        );
                    }
                }
                data["rs_half"] = rsHalf;
            }

            if (data.Count > 1) // net plus at least one component
            {
                result.Add(data);
            }
        }

        return result;
    }

    /// <summary>
    /// Generates pre-formatted SPICE netlist strings for load elements.
    /// Handles both single-ended and differential configurations.
    /// </summary>
    internal static string GenerateLoadElements(Circuit circuit, bool differential)
    {
        var lines = new List<string>();

        if (circuit.Harness?.Loads == null)
            return string.Empty;

        foreach (var load in circuit.Harness.Loads)
        {
            if (load.Elements.Count == 0)
                continue;

            var cs = load.Elements.Where(e => e.Type == "C").Select(e => e.Value).ToList();
            var rs = load.Elements.Where(e => e.Type == "R").Select(e => e.Value).ToList();

            // Check if load is already split (ends with _P or _N)
            var isAlreadySplit =
                load.Net.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
                || load.Net.EndsWith("_N", StringComparison.OrdinalIgnoreCase);

            if (differential && !isAlreadySplit)
            {
                // Split load across differential outputs
                for (int i = 0; i < cs.Count; i++)
                {
                    if (TryParseValue(cs[i], out var cValue))
                    {
                        var halfValue = FormatSIValue(cValue / 2.0);
                        var suffix = cs.Count > 1 ? $"_{i}" : "";
                        lines.Add($"C{load.Net}_P_load{suffix} {load.Net}_P 0 {halfValue}");
                        lines.Add($"C{load.Net}_N_load{suffix} {load.Net}_N 0 {halfValue}");
                    }
                    else
                    {
                        throw new ArgumentException(
                            $"Unable to parse capacitance load value '{cs[i]}' for net '{load.Net}' in circuit '{circuit.Name}'. "
                                + "Value must be a valid number with optional SI prefix (e.g., '1p', '10pF', '500f').",
                            paramName: null
                        );
                    }
                }

                for (int i = 0; i < rs.Count; i++)
                {
                    if (TryParseValue(rs[i], out var rValue))
                    {
                        var halfValue = FormatSIValue(rValue / 2.0);
                        var suffix = rs.Count > 1 ? $"_{i}" : "";
                        lines.Add($"R{load.Net}_P_load{suffix} {load.Net}_P 0 {halfValue}");
                        lines.Add($"R{load.Net}_N_load{suffix} {load.Net}_N 0 {halfValue}");
                    }
                    else
                    {
                        throw new ArgumentException(
                            $"Unable to parse resistance load value '{rs[i]}' for net '{load.Net}' in circuit '{circuit.Name}'. "
                                + "Value must be a valid number with optional SI prefix (e.g., '1K', '10KOhm', '500').",
                            paramName: null
                        );
                    }
                }
            }
            else
            {
                // Single-ended or already-split differential
                for (int i = 0; i < cs.Count; i++)
                {
                    var suffix = cs.Count > 1 ? $"_{i}" : "";
                    lines.Add($"C{load.Net}_load{suffix} {load.Net} 0 {cs[i]}");
                }

                for (int i = 0; i < rs.Count; i++)
                {
                    var suffix = rs.Count > 1 ? $"_{i}" : "";
                    lines.Add($"R{load.Net}_load{suffix} {load.Net} 0 {rs[i]}");
                }
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Generates pre-formatted SPICE netlist strings for supply and bias elements.
    /// </summary>
    internal static string GenerateSupplyElements(Circuit circuit)
    {
        var lines = new List<string>();

        if (circuit.Harness?.Supplies != null)
        {
            foreach (var supply in circuit.Harness.Supplies)
            {
                lines.Add($"V{supply.Net} {supply.Net} 0 DC {supply.Value}");
            }
        }

        if (circuit.Harness?.Biases != null)
        {
            foreach (var bias in circuit.Harness.Biases)
            {
                lines.Add($"V{bias.Net} {bias.Net} 0 DC {bias.Value}");
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Builds a dictionary of sweep conditions for template rendering.
    /// Keys are condition names (e.g., "InputDCBias"), values are dictionaries with start/stop/step.
    /// </summary>
    internal static Dictionary<string, object?> BuildSweepDictionary(Circuit circuit)
    {
        var result = new Dictionary<string, object?>();

        if (circuit.Harness?.Sweeps == null)
            return result;

        foreach (var sweep in circuit.Harness.Sweeps)
        {
            if (sweep.IsAuto)
            {
                // At EL level, Auto should have been resolved, but handle gracefully
                continue;
            }

            if (!TryParseValue(sweep.Start, out var startVal))
            {
                throw new ArgumentException(
                    $"Unable to parse sweep start value '{sweep.Start}' for sweep '{sweep.Name}'. "
                        + "Value must be a valid number with optional SI prefix (e.g., '0.3V', '1.5V').",
                    paramName: null
                );
            }

            if (!TryParseValue(sweep.Stop, out var stopVal))
            {
                throw new ArgumentException(
                    $"Unable to parse sweep stop value '{sweep.Stop}' for sweep '{sweep.Name}'. "
                        + "Value must be a valid number with optional SI prefix (e.g., '0.3V', '1.5V').",
                    paramName: null
                );
            }

            var sweepData = new Dictionary<string, object>
            {
                ["start"] = startVal,
                ["stop"] = stopVal,
            };

            if (sweep.Step != null)
            {
                if (TryParseValue(sweep.Step, out var stepVal))
                {
                    sweepData["step"] = stepVal;
                }
            }
            else
            {
                // Auto-step: compute as (stop - start) / 20, clamped between 10mV and 100mV
                var range =
                    sweepData["stop"] is double stop && sweepData["start"] is double start
                        ? Math.Abs(stop - start)
                        : 0.0;
                var autoStep = Math.Max(0.01, Math.Min(0.1, range / 20.0));
                sweepData["step"] = autoStep;
            }

            result[sweep.Name] = sweepData;
        }

        return result;
    }

    /// <summary>
    /// Determines the output node name from circuit ports.
    /// Returns the first port named "OUT" (case-insensitive), or the first port, or "OUT" as fallback.
    /// </summary>
    internal static string DetermineOutNode(Circuit circuit)
    {
        return circuit
                .Ports.FirstOrDefault(p => p.Name.Equals("OUT", StringComparison.OrdinalIgnoreCase))
                ?.Name
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
                return modelName.Equals("nmos", StringComparison.OrdinalIgnoreCase)
                    || modelName.Equals("pmos", StringComparison.OrdinalIgnoreCase);
            })
            ?? false;
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
        var rloadOhms = DeriveLoadResistance(circuit);

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
            $"Unable to parse supply value '{firstSupply.Value}' in harness for circuit '{circuit.Name}'. "
                + "Value must be a valid number (e.g. '1.8', '1.8V') to automatically determine common-mode voltage."
        );
    }

    private static double DeriveLoadCapacitance(Circuit circuit)
    {
        const double defaultLoadC = 1e-12;

        if (circuit.Harness?.Loads == null || circuit.Harness.Loads.Count == 0)
            return defaultLoadC;

        var firstLoad = circuit.Harness.Loads[0];
        var firstC = firstLoad.Elements.FirstOrDefault(e => e.Type == "C");
        if (firstC != null && TryParseValue(firstC.Value, out var parsedC))
            return parsedC;

        return defaultLoadC;
    }

    private static double DeriveLoadResistance(Circuit circuit)
    {
        const double defaultLoadR = 1e9; // 1 GOhm default

        if (circuit.Harness?.Loads == null || circuit.Harness.Loads.Count == 0)
            return defaultLoadR;

        var firstLoad = circuit.Harness.Loads[0];
        var firstR = firstLoad.Elements.FirstOrDefault(e => e.Type == "R");
        if (firstR != null && TryParseValue(firstR.Value, out var parsedR))
            return parsedR;

        return defaultLoadR;
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
    /// <param name="includeResolution">Optional include resolution for PDK model decks.</param>
    /// <returns>TestbenchFiles with path to generated netlist.</returns>
    public static TestbenchFiles GenerateTestbench(
        Circuit circuit,
        BenchConfig bench,
        BenchBackendType backend,
        string outputDir,
        string? workspaceRoot = null,
        BenchIncludeResolution? includeResolution = null
    )
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(bench);

        var harness = new ACIRTemplateHarness();
        var context = ToTestbenchContext(
            circuit,
            bench,
            backend,
            outputDir,
            workspaceRoot,
            includeResolution
        );
        var plan = harness.BuildPlan(context);

        // Find template
        var templatePath = plan.Data.TryGetValue("template_path", out var tp)
            ? tp?.ToString()
            : null;
        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            throw new InvalidOperationException($"Template not found: {templatePath}");
        }

        // Load template
        var templateText = File.ReadAllText(templatePath);
        if (string.IsNullOrWhiteSpace(templateText))
        {
            throw new InvalidOperationException(
                $"Template file is empty: {templatePath}. "
                    + $"Bench '{bench.Name}' requires a valid template with content."
            );
        }

        // Get template model from plan
        var templateModel = plan.Data.TryGetValue("template_model", out var tm) ? tm : null;
        if (templateModel == null)
        {
            throw new InvalidOperationException("Template model not found in plan");
        }

        // Render template
        var netlistText = TemplateRenderer.Render(templateText, templateModel);
        if (string.IsNullOrWhiteSpace(netlistText))
        {
            throw new InvalidOperationException(
                $"Template rendering produced empty output for bench '{bench.Name}'. "
                    + $"Template: {templatePath}"
            );
        }

        // Write files
        Directory.CreateDirectory(outputDir);
        var netlistPath = Path.Combine(outputDir, plan.NetlistName);
        File.WriteAllText(netlistPath, netlistText);

        var specPath = Path.Combine(outputDir, "spec.json");
        File.WriteAllText(
            specPath,
            System.Text.Json.JsonSerializer.Serialize(
                context.Spec,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
            )
        );

        return new TestbenchFiles
        {
            RootDir = outputDir,
            NetlistPath = netlistPath,
            SpecPath = specPath,
            ResultsCsv = context.Spec.ResultsCsv,
            RunnerPath = string.Empty,
        };
    }

    /// <summary>
    /// Derives AC sweep start/stop frequencies from circuit constraints.
    /// For DC-coupled amps (no HP constraint): starts at 1Hz.
    /// For AC-coupled amps (with HP constraint): starts 3 decades below HP corner.
    /// </summary>
    private static (double acStartHz, double acStopHz) DeriveAcSweepFromConstraints(Circuit circuit)
    {
        const double DefaultStartHz = 1.0;
        const double DefaultStopHz = 10e9;

        if (circuit.Constraints?.Numeric == null || circuit.Constraints.Numeric.Count == 0)
        {
            return (DefaultStartHz, DefaultStopHz);
        }

        // Check for highpass constraint (AC-coupled amplifier)
        var hpConstraint = circuit.Constraints.Numeric.FirstOrDefault(c =>
            c.Metric.Equals("HighpassBandwidth", StringComparison.OrdinalIgnoreCase)
        );

        double acStartHz;
        if (hpConstraint != null && TryParseConstraintValue(hpConstraint.Value, out var hpHz))
        {
            // AC-coupled: start 3 decades below HP corner to see rolloff
            acStartHz = Math.Max(0.001, hpHz / 1000.0);
        }
        else
        {
            // DC-coupled: always start at 1Hz to measure DC gain
            acStartHz = DefaultStartHz;
        }

        // Determine stop frequency from GBW or bandwidth constraints
        var gbwConstraint = circuit.Constraints.Numeric.FirstOrDefault(c =>
            c.Metric.Equals("GainBandwidth", StringComparison.OrdinalIgnoreCase)
            || c.Metric.Equals("GBW", StringComparison.OrdinalIgnoreCase)
            || c.Metric.Equals("UGF", StringComparison.OrdinalIgnoreCase)
            || c.Metric.Equals("UnityGainFrequency", StringComparison.OrdinalIgnoreCase)
        );

        if (gbwConstraint != null && TryParseConstraintValue(gbwConstraint.Value, out var gbwHz))
        {
            // Stop at 10x the constrained GBW value, at least 1GHz
            var acStopHz = Math.Max(gbwHz * 10.0, 1e9);
            return (acStartHz, acStopHz);
        }

        // Look for bandwidth-related constraints
        var bwConstraint = circuit.Constraints.Numeric.FirstOrDefault(c =>
            c.Metric.Equals("Bandwidth", StringComparison.OrdinalIgnoreCase)
            || c.Metric.Equals("3dBBandwidth", StringComparison.OrdinalIgnoreCase)
            || c.Metric.Equals("LowpassBandwidth", StringComparison.OrdinalIgnoreCase)
        );

        if (bwConstraint != null && TryParseConstraintValue(bwConstraint.Value, out var bwHz))
        {
            var acStopHz = Math.Max(bwHz * 100.0, 1e9);
            return (acStartHz, acStopHz);
        }

        return (acStartHz, DefaultStopHz);
    }

    /// <summary>
    /// Derives the optimal passband measurement frequency from circuit constraints.
    /// Uses HP/LP corner constraints or infers from GBW and gain constraints.
    /// </summary>
    private static double DerivePassbandMeasurementFrequency(
        Circuit circuit,
        double acStartHz,
        double acStopHz
    )
    {
        const double DcCoupledHpCorner = 1.0; // DC-coupled amps: passband starts at ~1Hz
        var constraints = circuit.Constraints?.Numeric ?? new List<NumericConstraint>();

        // 1. Determine HP corner (low-frequency bound of passband)
        // For DC-coupled amps (no HP constraint), use 1Hz as the effective HP corner
        var hpCorner = GetConstraintHz(constraints, "HighpassBandwidth") ?? DcCoupledHpCorner;

        // 2. Determine LP corner (high-frequency bound of passband)
        var lpCorner = GetConstraintHz(constraints, "LowpassBandwidth");

        if (lpCorner == null)
        {
            // Infer from GBW and PassbandGain
            var gbwHz = GetConstraintHz(
                constraints,
                "GainBandwidth",
                "GBW",
                "UGF",
                "UnityGainFrequency"
            );
            var gainDb = GetConstraintValue(constraints, "PassbandGain", "Gain");

            if (gbwHz != null && gainDb != null)
            {
                // f_3dB = GBW / 10^(gain_dB/20)
                var gainLinear = Math.Pow(10, gainDb.Value / 20.0);
                lpCorner = gbwHz.Value / gainLinear;
            }
            else if (gbwHz != null)
            {
                // Assume typical 40dB gain (100x linear)
                lpCorner = gbwHz.Value / 100.0;
            }
            else
            {
                lpCorner = acStopHz;
            }
        }

        // 3. Passband measurement = geometric mean of corners
        var passbandFreq = Math.Sqrt(hpCorner * lpCorner.Value);

        // 4. Clamp to sweep range
        return Math.Max(acStartHz, Math.Min(acStopHz, passbandFreq));
    }

    /// <summary>
    /// Gets constraint value in Hz by searching for metric names (case-insensitive).
    /// </summary>
    private static double? GetConstraintHz(
        IEnumerable<NumericConstraint> constraints,
        params string[] metricNames
    )
    {
        foreach (var name in metricNames)
        {
            var constraint = constraints.FirstOrDefault(c =>
                c.Metric.Equals(name, StringComparison.OrdinalIgnoreCase)
            );

            if (constraint != null && TryParseConstraintValue(constraint.Value, out var value))
            {
                return value;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets constraint value (unitless) by searching for metric names.
    /// </summary>
    private static double? GetConstraintValue(
        IEnumerable<NumericConstraint> constraints,
        params string[] metricNames
    )
    {
        foreach (var name in metricNames)
        {
            var constraint = constraints.FirstOrDefault(c =>
                c.Metric.Equals(name, StringComparison.OrdinalIgnoreCase)
            );

            if (constraint != null && TryParseConstraintValue(constraint.Value, out var value))
            {
                return value;
            }
        }
        return null;
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
    /// Formats a numeric value using an appropriate SI prefix for compact SPICE representation.
    /// </summary>
    /// <param name="value">The numeric value to format.</param>
    /// <returns>A compact SPICE-compatible string (e.g., "1.5p", "10M", "500f").</returns>
    internal static string FormatSIValue(double value)
    {
        if (value == 0)
            return "0";

        var absValue = Math.Abs(value);
        var sign = value < 0 ? "-" : "";

        // Select SI prefix based on magnitude
        var (divisor, suffix) = absValue switch
        {
            >= 1e12 => (1e12, "T"),
            >= 1e9 => (1e9, "G"),
            >= 1e6 => (1e6, "M"),
            >= 1e3 => (1e3, "K"),
            >= 1 => (1.0, ""),
            >= 1e-3 => (1e-3, "m"),
            >= 1e-6 => (1e-6, "u"),
            >= 1e-9 => (1e-9, "n"),
            >= 1e-12 => (1e-12, "p"),
            _ => (1e-15, "f"),
        };

        var scaled = absValue / divisor;

        // Format with appropriate precision to avoid floating-point artifacts
        // Use up to 6 significant figures, removing trailing zeros
        var formatted = scaled.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);

        return $"{sign}{formatted}{suffix}";
    }

    /// <summary>
    /// Core SI value parser with configurable behavior.
    /// </summary>
    /// <param name="valueStr">Input string to parse.</param>
    /// <param name="result">Parsed numeric result.</param>
    /// <param name="stripUnits">Whether to strip unit suffixes (V, F, ohm, Hz, etc.).</param>
    /// <param name="allowSubUnity">Whether to recognize sub-unity prefixes (m, u, n, p, f).</param>
    internal static bool TryParseSIValue(
        string valueStr,
        out double result,
        bool stripUnits,
        bool allowSubUnity
    )
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(valueStr))
            return false;

        var cleanedValue = valueStr.Trim();

        if (stripUnits)
        {
            // Check longer suffixes first to avoid partial matches (e.g., "Hz" before "H")
            foreach (var suffix in new[] { "Ohm", "ohm", "Hz", "V", "A", "F", "H", "W", "s", "S" })
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
                _ => 1.0,
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

        if (
            double.TryParse(
                cleanedValue,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            result = parsed * multiplier;
            return true;
        }

        return false;
    }
}
