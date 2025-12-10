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

        // Build harness data structures
        var harnessSupplies = new List<Dictionary<string, object>>();
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

        var harnessLoads = new List<Dictionary<string, object>>();
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

        // Build args dictionary
        var args = new Dictionary<string, object?>
        {
            ["harness"] = "acir_template", // Use ACIR template harness
            ["circuit_name"] = circuit.Name,
            ["design_file"] = $"{circuit.Name}.sp",
            ["port_list"] = string.Join(" ", portList),
            ["out_node"] = outNode,
            ["generic_models"] = genericModels,
            ["vcm"] = vcm,
            ["bias_v"] = biasV,
            ["harness_supplies"] = harnessSupplies,
            ["harness_loads"] = harnessLoads
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
}

