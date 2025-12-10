using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Bench;

namespace Cascode.ACIR;

/// <summary>
/// Harness for ACIR-based testbenches that uses template discovery.
/// </summary>
public sealed class ACIRTemplateHarness : ITestbenchHarness
{
    public string Id => "acir_template";
    public string Description => "ACIR template-based testbench harness";
    public IReadOnlyList<BenchBackendType> SupportedBackends => new[] { BenchBackendType.Ngspice, BenchBackendType.Spectre };
    public IReadOnlyList<HarnessParam> Params => Array.Empty<HarnessParam>();

    public TestbenchPlan BuildPlan(TestbenchContext ctx)
    {
        var benchName = ctx.Spec.Name;
        var backend = ctx.Spec.Backend;
        var workspaceRoot = ctx.WorkspaceRoot;

        // Find template using discovery
        var templatePath = TemplateDiscovery.FindTemplate(benchName, backend, ctx.Args.TryGetValue("start_dir", out var sd) ? sd?.ToString() : null, workspaceRoot);
        if (templatePath == null)
        {
            throw new InvalidOperationException($"Template not found for bench '{benchName}' with backend '{backend}'. Searched upward from current directory and lib/std/amp/benches/.");
        }

        // Build template model from context args
        var templateModel = BuildTemplateModel(ctx);

        // Netlist name should include circuit name: {circuit}_{bench}.{ext}
        var circuitName = ctx.Args.TryGetValue("circuit_name", out var cn) ? cn?.ToString() ?? "" : "";
        var netlistName = backend == BenchBackendType.Spectre
            ? $"{circuitName}_{benchName}.scs"
            : $"{circuitName}_{benchName}.sp";

        return new TestbenchPlan
        {
            HarnessId = Id,
            Backend = backend,
            NetlistName = netlistName,
            Artifacts = new Dictionary<string, string>
            {
                ["results"] = ctx.Spec.ResultsCsv
            },
            Notes = Description,
            Data = new Dictionary<string, object>
            {
                ["template_path"] = templatePath,
                ["template_model"] = templateModel
            }
        };
    }

    private static object BuildTemplateModel(TestbenchContext ctx)
    {
        // Extract circuit info from args
        var circuitName = ctx.Args.TryGetValue("circuit_name", out var cn) ? cn?.ToString() ?? "" : "";
        var designFile = ctx.Args.TryGetValue("design_file", out var df) ? df?.ToString() ?? "" : "";
        var portList = ctx.Args.TryGetValue("port_list", out var pl) ? pl?.ToString() ?? "" : "";
        var outNode = ctx.Args.TryGetValue("out_node", out var on) ? on?.ToString() ?? "" : "";
        var genericModels = ctx.Args.TryGetValue("generic_models", out var gm) && gm is bool gmb && gmb;
        var vcm = ctx.Args.TryGetValue("vcm", out var vcmObj) ? Convert.ToDouble(vcmObj) : 0.9;
        var biasV = ctx.Args.TryGetValue("bias_v", out var bvObj) ? Convert.ToDouble(bvObj) : 0.9;

        var harness = ExtractHarnessData(ctx);
        var env = ExtractEnvironmentDefaults(ctx);
        var (acMag, acStartHz, acStopHz) = ExtractAcSweepParams(ctx);

        var spec = new { temperature_c = ctx.Spec.TemperatureC };

        var includesWithSection = ctx.Args.TryGetValue("includes_with_section", out var iws)
            && iws is IEnumerable<string> iwsL ? iwsL.ToList() : new List<string>();
        var includesWithoutSection = ctx.Args.TryGetValue("includes_without_section", out var iwos)
            && iwos is IEnumerable<string> iwosL ? iwosL.ToList() : new List<string>();

        return new
        {
            circuit_name = circuitName,
            bench_name = ctx.Spec.Name,
            design_file = designFile,
            port_list = portList,
            out_node = outNode,
            generic_models = genericModels,
            vcm = vcm,
            bias_v = biasV,
            harness = harness,
            env = env,
            spec = spec,
            ac_mag = acMag,
            ac_start_hz = acStartHz,
            ac_stop_hz = acStopHz,
            includes_with_section = includesWithSection,
            includes_without_section = includesWithoutSection
        };
    }

    private static object ExtractHarnessData(TestbenchContext ctx)
    {
        var supplies = new List<object>();
        if (ctx.Args.TryGetValue("harness_supplies", out var hs) && hs is IEnumerable<object> suppliesList)
        {
            foreach (var item in suppliesList)
            {
                if (item is Dictionary<string, object> supplyDict)
                {
                    supplies.Add(new
                    {
                        net = supplyDict.TryGetValue("net", out var net) ? net?.ToString() ?? "" : "",
                        value = supplyDict.TryGetValue("value", out var val) ? val?.ToString() ?? "" : ""
                    });
                }
            }
        }

        var loads = new List<object>();
        if (ctx.Args.TryGetValue("harness_loads", out var hl) && hl is IEnumerable<object> loadsList)
        {
            foreach (var item in loadsList)
            {
                if (item is Dictionary<string, object> loadDict)
                {
                    loads.Add(new
                    {
                        net = loadDict.TryGetValue("net", out var net) ? net?.ToString() ?? "" : "",
                        c = loadDict.TryGetValue("c", out var c) ? c?.ToString() ?? "" : ""
                    });
                }
            }
        }

        return new { supplies = supplies, loads = loads };
    }

    private static object ExtractEnvironmentDefaults(TestbenchContext ctx)
    {
        var sourceOhms = ctx.Args.TryGetValue("source_ohms", out var so) ? Convert.ToDouble(so) : 50.0;
        var cloadF = ctx.Args.TryGetValue("cload_f", out var cl) ? Convert.ToDouble(cl) : 1e-12;
        var rloadOhms = ctx.Args.TryGetValue("rload_ohms", out var rl) ? Convert.ToDouble(rl) : 0.0;

        return new
        {
            source_ohms = sourceOhms,
            cload_f = cloadF,
            rload_ohms = rloadOhms
        };
    }

    private static (double AcMag, double AcStartHz, double AcStopHz) ExtractAcSweepParams(TestbenchContext ctx)
    {
        var acMag = ctx.Args.TryGetValue("ac_mag", out var am) ? Convert.ToDouble(am) : 1.0;
        var acStartHz = ctx.Args.TryGetValue("ac_start_hz", out var ash) ? Convert.ToDouble(ash) : 1.0;
        var acStopHz = ctx.Args.TryGetValue("ac_stop_hz", out var astop) ? Convert.ToDouble(astop) : 10e9;

        return (acMag, acStartHz, acStopHz);
    }
}

