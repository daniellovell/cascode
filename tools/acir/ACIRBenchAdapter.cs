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

        // Build harness data structures (using List<object> for compatibility with ACIRTemplateHarness)
        var harnessSupplies = new List<object>();
        if (circuit.Harness?.Supplies != null)
        {
            foreach (var supply in circuit.Harness.Supplies)
            {
                harnessSupplies.Add(new Dictionary<string, object>
                {
                    ["net"] = supply.Net,
                    ["value"] = supply.Value
                });
            }
        }

        // Also include biases as supplies
        if (circuit.Harness?.Biases != null)
        {
            foreach (var bias in circuit.Harness.Biases)
            {
                harnessSupplies.Add(new Dictionary<string, object>
                {
                    ["net"] = bias.Net,
                    ["value"] = bias.Value
                });
            }
        }

        var harnessLoads = new List<object>();
        if (circuit.Harness?.Loads != null)
        {
            foreach (var load in circuit.Harness.Loads)
            {
                if (load.C != null)
                {
                    harnessLoads.Add(new Dictionary<string, object>
                    {
                        ["net"] = load.Net,
                        ["c"] = load.C
                    });
                }
            }
        }

        // Determine output node (first OUT port, or first port if no OUT)
        var outNode = circuit.Ports.FirstOrDefault(p => p.Name.Equals("OUT", StringComparison.OrdinalIgnoreCase))?.Name
            ?? circuit.Ports.FirstOrDefault()?.Name
            ?? "OUT";

        // Build port list for DUT instantiation
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

        // Check if circuit uses generic models
        var genericModels = circuit.Fill?.Devices?.Any(d =>
        {
            var modelName = d.PdkDevice ?? d.DeviceType;
            return modelName.Equals("nmos", StringComparison.OrdinalIgnoreCase) ||
                   modelName.Equals("pmos", StringComparison.OrdinalIgnoreCase);
        }) ?? false;

        // Determine common-mode voltage (mid-supply default)
        var vcm = 0.9; // Default, can be overridden
        if (circuit.Harness?.Supplies.Count > 0)
        {
            // Try to parse first supply value and use half
            var firstSupply = circuit.Harness.Supplies[0];
            if (double.TryParse(firstSupply.Value.Replace("V", ""), out var supplyVal))
            {
                vcm = supplyVal / 2.0;
            }
        }

        var biasV = vcm; // Same for single-ended

        // Extract load capacitance from harness (e.g., "load OUT C=1p F")
        var loadC = 1e-12; // Default 1pF
        if (circuit.Harness?.Loads?.Count > 0)
        {
            var firstLoad = circuit.Harness.Loads[0];
            if (firstLoad.C != null && TryParseValue(firstLoad.C, out var parsedC))
            {
                loadC = parsedC;
            }
        }

        // Extract source impedance from harness (e.g., "source IN Z=50 ohm")
        var sourceOhms = 50.0; // Default 50 ohms
        if (circuit.Harness?.Sources?.Count > 0)
        {
            var firstSource = circuit.Harness.Sources[0];
            if (firstSource.Z != null && TryParseValue(firstSource.Z, out var parsedZ))
            {
                sourceOhms = parsedZ;
            }
        }

        // Resistive load: use 1G ohm (essentially open) if not specified
        var rloadOhms = 1e9;

        // Derive AC sweep parameters from constraints
        var (acStartHz, acStopHz) = DeriveAcSweepFromConstraints(circuit);

        // Design file is always .sp (from SpiceEmitter), regardless of simulator backend
        var designFile = $"{circuit.Name}.sp";

        // Build args dictionary
        var args = new Dictionary<string, object?>
        {
            ["harness"] = "acir_template", // Use ACIR template harness
            ["circuit_name"] = circuit.Name,
            ["design_file"] = designFile,
            ["port_list"] = string.Join(" ", portList),
            ["out_node"] = outNode,
            ["generic_models"] = genericModels,
            ["vcm"] = vcm,
            ["bias_v"] = biasV,
            ["harness_supplies"] = harnessSupplies,
            ["harness_loads"] = harnessLoads,
            // Spectre-specific env parameters derived from ACIR
            ["source_ohms"] = sourceOhms,
            ["cload_f"] = loadC,
            ["rload_ohms"] = rloadOhms,
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
    /// </summary>
    private static bool TryParseConstraintValue(string valueStr, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(valueStr))
            return false;

        valueStr = valueStr.Trim();
        var multiplier = 1.0;

        if (valueStr.Length > 0 && char.IsLetter(valueStr[^1]))
        {
            var lastChar = char.ToUpperInvariant(valueStr[^1]);
            multiplier = lastChar switch
            {
                'T' => 1e12,
                'G' => 1e9,
                'M' => 1e6,
                'K' => 1e3,
                _ => 1.0
            };
            if (multiplier != 1.0)
            {
                valueStr = valueStr[..^1];
            }
        }

        if (double.TryParse(valueStr, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed * multiplier;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to parse a value string with SI unit suffix into a double.
    /// Supports k, M, G, m, u, n, p, f suffixes.
    /// </summary>
    private static bool TryParseValue(string valueStr, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(valueStr))
            return false;

        valueStr = valueStr.Trim();

        // Remove common unit suffixes (V, F, ohm, Hz, etc.)
        var cleanedValue = valueStr;
        foreach (var suffix in new[] { "V", "F", "ohm", "Ohm", "Hz", "W", "A", "s", "S" })
        {
            if (cleanedValue.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                cleanedValue = cleanedValue[..^suffix.Length].Trim();
                break;
            }
        }

        if (cleanedValue.Length == 0)
            return false;

        var multiplier = 1.0;
        var lastChar = cleanedValue[^1];
        if (char.IsLetter(lastChar))
        {
            multiplier = char.ToUpperInvariant(lastChar) switch
            {
                'T' => 1e12,
                'G' => 1e9,
                'M' => 1e6,
                'K' => 1e3,
                'U' => 1e-6,
                'N' => 1e-9,
                'P' => 1e-12,
                'F' => 1e-15,
                _ => 1.0
            };
            if (multiplier != 1.0)
            {
                cleanedValue = cleanedValue[..^1];
            }
        }

        // Handle lowercase 'm' for milli (after uppercase check)
        if (cleanedValue.Length > 0 && cleanedValue[^1] == 'm')
        {
            multiplier = 1e-3;
            cleanedValue = cleanedValue[..^1];
        }

        if (double.TryParse(cleanedValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed * multiplier;
            return true;
        }

        return false;
    }
}

