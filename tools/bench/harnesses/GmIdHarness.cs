using System;
using System.Collections.Generic;
using System.IO;

namespace Cascode.Bench;

public sealed class GmIdHarness : ITestbenchHarness
{
    public string Id => "gm_id.v1";
    public string Description => "DC sweep VGS; export V(dut) and I(VDD). Post-process for gm/Id.";
    public IReadOnlyList<BenchBackendType> SupportedBackends { get; } =
        new[] { BenchBackendType.Ngspice, BenchBackendType.Spectre };
    public IReadOnlyList<HarnessParam> Params { get; } =
        new[]
        {
            new HarnessParam("vds", "number", "Drain bias (V)", 0.9),
            new HarnessParam("vsb", "number", "Body bias (V)", 0.0),
            new HarnessParam("start", "number", "VGS start (V)", 0.0),
            new HarnessParam("stop", "number", "VGS stop (V)", 1.2),
            new HarnessParam("step", "number", "VGS step (V)", 0.01),
            new HarnessParam("w_m", "number", "Width (m)", 1e-6),
            new HarnessParam("l_m", "number", "Length (m)", 0.18e-6),
            new HarnessParam("mult", "integer", "Multiplier", 1),
            new HarnessParam("nf", "integer", "Fingers", 1),
            new HarnessParam(
                "drain_bias_mode",
                "string",
                "Drain bias mode: fixed or scaled",
                "fixed"
            ),
            new HarnessParam("drain_alpha", "number", "When scaled, Vd = alpha * VGS", 1.0),
        };

    public TestbenchPlan BuildPlan(TestbenchContext ctx)
    {
        var spec = ctx.Spec;
        var artifacts = new Dictionary<string, string> { ["results"] = spec.ResultsCsv };

        var data = new Dictionary<string, object>();
        if (spec.Backend == BenchBackendType.Spectre)
        {
            var templateFile = "gm_id_v1.scs.tpl";
            data["template_path"] = FindTemplatePath(templateFile);
            data["template_name"] = templateFile;
            data["params"] = BuildTemplateParams(spec);
        }

        return new TestbenchPlan
        {
            HarnessId = Id,
            Backend = spec.Backend,
            NetlistName = spec.Name + (spec.Backend == BenchBackendType.Ngspice ? ".cir" : ".scs"),
            Artifacts = artifacts,
            Notes = Description,
            Data = data,
        };
    }

    private static string FindTemplatePath(string templateFile)
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "templates", templateFile);
        if (File.Exists(candidate))
            return candidate;
        return templateFile; // allow TryRenderTemplate to fall back to embedded resource lookup
    }

    private static IDictionary<string, object> BuildTemplateParams(TestbenchSpec spec)
    {
        // Provide simple defaults for geometry-related params; Spectre will fill in more detailed
        // parasitics via the model when available.
        double W = spec.W_M;
        double L = spec.L_M;
        int nf = Math.Max(1, spec.Nfingers);
        int mult = Math.Max(1, spec.Mult);
        double area = W * L * nf;
        double peri = 2.0 * (W + L) * nf;

        return new Dictionary<string, object>
        {
            ["vds"] = spec.Vds,
            ["vsb"] = spec.Vsb,
            ["start"] = spec.Vgs.Start,
            ["stop"] = spec.Vgs.Stop,
            ["step"] = spec.Vgs.Step,
            ["w_m"] = W,
            ["l_m"] = L,
            ["mult"] = mult,
            ["nf"] = nf,
            ["inst_name"] = spec.IsSubckt ? "X1" : "M1",
            ["drain_bias_mode"] = "fixed",
            ["drain_alpha"] = 1.0,
            ["as"] = area * 0.5,
            ["ad"] = area * 0.5,
            ["ps"] = peri * 0.5,
            ["pd"] = peri * 0.5,
            ["nrd"] = 0.0,
            ["nrs"] = 0.0,
            ["sa"] = 0.0,
            ["sb"] = 0.0,
            ["sd"] = 0.0,
            ["sca"] = 0.0,
            ["scb"] = 0.0,
            ["scc"] = 0.0,
        };
    }
}
