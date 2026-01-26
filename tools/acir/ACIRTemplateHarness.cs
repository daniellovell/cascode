using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Bench;

namespace Cascode.ACIR;

/// <summary>
/// Harness for ACIR-based testbenches that uses embedded templates.
/// </summary>
public sealed class ACIRTemplateHarness : ITestbenchHarness
{
    public string Id => "acir_template";
    public string Description => "ACIR template-based testbench harness";
    public IReadOnlyList<BenchBackendType> SupportedBackends =>
        new[] { BenchBackendType.Ngspice, BenchBackendType.Spectre };
    public IReadOnlyList<HarnessParam> Params => Array.Empty<HarnessParam>();

    public TestbenchPlan BuildPlan(TestbenchContext ctx)
    {
        var benchName = ctx.Spec.Name;
        var backend = ctx.Spec.Backend;
        var templateName = ctx.Args.TryGetValue("template_name", out var tn)
            ? tn?.ToString()
            : benchName;
        if (string.IsNullOrWhiteSpace(templateName))
        {
            throw new InvalidOperationException("Bench template name is required.");
        }

        if (!BenchTemplateLibrary.TryGetTemplate(templateName, backend, out var templateText))
        {
            var available = string.Join(", ", BenchTemplateLibrary.GetBenchNames());
            throw new InvalidOperationException(
                $"Builtin template not found for bench '{templateName}' with backend '{backend}'. Available: {available}."
            );
        }

        // Build template model from context args
        var templateModel = BuildTemplateModel(ctx);

        // Netlist name should include circuit name: {circuit}_{bench}.{ext}
        var circuitName = ctx.Args.TryGetValue("circuit_name", out var cn)
            ? cn?.ToString() ?? ""
            : "";
        var netlistName =
            backend == BenchBackendType.Spectre
                ? $"{circuitName}_{benchName}.scs"
                : $"{circuitName}_{benchName}.sp";

        return new TestbenchPlan
        {
            HarnessId = Id,
            Backend = backend,
            NetlistName = netlistName,
            Artifacts = new Dictionary<string, string> { ["results"] = ctx.Spec.ResultsCsv },
            Notes = Description,
            Data = new Dictionary<string, object>
            {
                ["template_text"] = templateText,
                ["template_model"] = templateModel,
            },
        };
    }

    private static object BuildTemplateModel(TestbenchContext ctx)
    {
        // Extract circuit info from args
        var circuitName = ctx.Args.TryGetValue("circuit_name", out var cn)
            ? cn?.ToString() ?? ""
            : "";
        var designFile = ctx.Args.TryGetValue("design_file", out var df)
            ? df?.ToString() ?? ""
            : "";
        var portList = ctx.Args.TryGetValue("port_list", out var pl) ? pl?.ToString() ?? "" : "";
        var outNode = ctx.Args.TryGetValue("out_node", out var on) ? on?.ToString() ?? "" : "";
        var genericModels =
            ctx.Args.TryGetValue("generic_models", out var gm) && gm is bool gmb && gmb;
        var vcm = ctx.Args.TryGetValue("vcm", out var vcmObj) ? Convert.ToDouble(vcmObj) : 0.9;
        var biasV = ctx.Args.TryGetValue("bias_v", out var bvObj) ? Convert.ToDouble(bvObj) : 0.9;

        var harness = ExtractHarnessData(ctx);
        var env = ExtractEnvironmentDefaults(ctx);
        var (acMag, acStartHz, acStopHz) = ExtractAcSweepParams(ctx);
        var passbandFreqHz = ctx.Args.TryGetValue("passband_freq_hz", out var pbf)
            ? Convert.ToDouble(pbf)
            : acStartHz;
        var stbStartHz = ctx.Args.TryGetValue("stb_start_hz", out var stbS)
            ? Convert.ToDouble(stbS)
            : acStartHz;
        var stbStopHz = ctx.Args.TryGetValue("stb_stop_hz", out var stbE)
            ? Convert.ToDouble(stbE)
            : acStopHz;
        var loadElements = ctx.Args.TryGetValue("load_elements", out var le)
            ? le?.ToString() ?? ""
            : "";
        var supplyElements = ctx.Args.TryGetValue("supply_elements", out var se)
            ? se?.ToString() ?? ""
            : "";
        var sweep = ExtractSweepData(ctx);

        var spec = new { temperature_c = ctx.Spec.TemperatureC };

        var includesWithSection =
            ctx.Args.TryGetValue("includes_with_section", out var iws)
            && iws is IEnumerable<string> iwsL
                ? iwsL.ToList()
                : new List<string>();
        var includesWithoutSection =
            ctx.Args.TryGetValue("includes_without_section", out var iwos)
            && iwos is IEnumerable<string> iwosL
                ? iwosL.ToList()
                : new List<string>();
        var section = ctx.Args.TryGetValue("section", out var sec) ? sec?.ToString() : null;
        var benchConfig = ctx.Args.TryGetValue("bench_config", out var bc)
            ? bc
            : new Dictionary<string, string>();

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
            passband_freq_hz = passbandFreqHz,
            stb_start_hz = stbStartHz,
            stb_stop_hz = stbStopHz,
            load_elements = loadElements,
            supply_elements = supplyElements,
            sweep = sweep,
            bench_config = benchConfig,
            includes_with_section = includesWithSection,
            includes_without_section = includesWithoutSection,
            section = section,
        };
    }

    private static object ExtractHarnessData(TestbenchContext ctx)
    {
        var supplies = new List<object>();
        if (
            ctx.Args.TryGetValue("harness_supplies", out var hs)
            && hs is IEnumerable<object> suppliesList
        )
        {
            foreach (var item in suppliesList)
            {
                if (item is Dictionary<string, object> supplyDict)
                {
                    supplies.Add(
                        new
                        {
                            net = supplyDict.TryGetValue("net", out var net)
                                ? net?.ToString() ?? ""
                                : "",
                            value = supplyDict.TryGetValue("value", out var val)
                                ? val?.ToString() ?? ""
                                : "",
                        }
                    );
                }
            }
        }

        var loads = new List<object>();
        if (
            ctx.Args.TryGetValue("harness_loads", out var hl) && hl is IEnumerable<object> loadsList
        )
        {
            foreach (var item in loadsList)
            {
                if (item is Dictionary<string, object> loadDict)
                {
                    var cs =
                        loadDict.TryGetValue("cs", out var csObj) && csObj is List<string> csList
                            ? csList
                            : new List<string>();
                    var rs =
                        loadDict.TryGetValue("rs", out var rsObj) && rsObj is List<string> rsList
                            ? rsList
                            : new List<string>();

                    loads.Add(
                        new
                        {
                            net = loadDict.TryGetValue("net", out var net)
                                ? net?.ToString() ?? ""
                                : "",
                            cs = cs,
                            rs = rs,
                        }
                    );
                }
            }
        }

        return new { supplies = supplies, loads = loads };
    }

    private static object ExtractEnvironmentDefaults(TestbenchContext ctx)
    {
        var sourceOhms = ctx.Args.TryGetValue("source_ohms", out var so)
            ? Convert.ToDouble(so)
            : 50.0;
        var cloadF = ctx.Args.TryGetValue("cload_f", out var cl) ? Convert.ToDouble(cl) : 1e-12;
        var rloadOhms = ctx.Args.TryGetValue("rload_ohms", out var rl) ? Convert.ToDouble(rl) : 0.0;

        return new
        {
            source_ohms = sourceOhms,
            cload_f = cloadF,
            rload_ohms = rloadOhms,
        };
    }

    private static (double AcMag, double AcStartHz, double AcStopHz) ExtractAcSweepParams(
        TestbenchContext ctx
    )
    {
        var acMag = ctx.Args.TryGetValue("ac_mag", out var am) ? Convert.ToDouble(am) : 1.0;
        var acStartHz = ctx.Args.TryGetValue("ac_start_hz", out var ash)
            ? Convert.ToDouble(ash)
            : 1.0;
        var acStopHz = ctx.Args.TryGetValue("ac_stop_hz", out var astop)
            ? Convert.ToDouble(astop)
            : 10e9;

        return (acMag, acStartHz, acStopHz);
    }

    private static object ExtractSweepData(TestbenchContext ctx)
    {
        var sweepDict = new Dictionary<string, object?>();

        // Extract all sweep.* keys from args
        foreach (var kvp in ctx.Args)
        {
            if (kvp.Key.StartsWith("sweep.", StringComparison.Ordinal))
            {
                var conditionName = kvp.Key.Substring(6); // Remove "sweep." prefix
                if (kvp.Value is Dictionary<string, object> sweepData)
                {
                    sweepDict[conditionName] = new
                    {
                        Start = sweepData.TryGetValue("start", out var s)
                            ? Convert.ToDouble(s)
                            : 0.0,
                        Stop = sweepData.TryGetValue("stop", out var st)
                            ? Convert.ToDouble(st)
                            : 0.0,
                        Step = sweepData.TryGetValue("step", out var step)
                            ? Convert.ToDouble(step)
                            : (double?)null,
                    };
                }
            }
        }

        // Convert dictionary to ScriptObject for proper Scriban member access
        var scriptObj = new Scriban.Runtime.ScriptObject();
        foreach (var kvp in sweepDict)
        {
            scriptObj[kvp.Key] = kvp.Value;
        }
        return scriptObj;
    }
}
