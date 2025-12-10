using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.ACIR;

namespace Cascode.Bench;

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
        var benchName = ctx.Spec.Name;
        var designFile = ctx.Args.TryGetValue("design_file", out var df) ? df?.ToString() ?? "" : "";
        var portList = ctx.Args.TryGetValue("port_list", out var pl) ? pl?.ToString() ?? "" : "";
        var outNode = ctx.Args.TryGetValue("out_node", out var on) ? on?.ToString() ?? "" : "";
        var genericModels = ctx.Args.TryGetValue("generic_models", out var gm) && gm is bool gmb && gmb;
        var vcm = ctx.Args.TryGetValue("vcm", out var vcmObj) ? Convert.ToDouble(vcmObj) : 0.9;
        var biasV = ctx.Args.TryGetValue("bias_v", out var bvObj) ? Convert.ToDouble(bvObj) : 0.9;

        // Extract harness data
        var supplies = new List<object>();
        if (ctx.Args.TryGetValue("harness_supplies", out var hs) && hs is List<object> suppliesList)
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
        if (ctx.Args.TryGetValue("harness_loads", out var hl) && hl is List<object> loadsList)
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

        var harness = new
        {
            supplies = supplies,
            loads = loads
        };

        return new
        {
            circuit_name = circuitName,
            bench_name = benchName,
            design_file = designFile,
            port_list = portList,
            out_node = outNode,
            generic_models = genericModels,
            vcm = vcm,
            bias_v = biasV,
            harness = harness
        };
    }
}

