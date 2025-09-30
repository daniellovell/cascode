namespace Cascode.Bench;

public sealed class GmIdHarness : ITestbenchHarness
{
    public string Id => "gm_id.v1";
    public string Description => "DC sweep VGS; export V(dut) and I(VDD). Post-process for gm/Id.";
    public IReadOnlyList<BenchBackendType> SupportedBackends { get; } = new[] { BenchBackendType.Ngspice, BenchBackendType.Spectre };
    public IReadOnlyList<HarnessParam> Params { get; } = new[]
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
    };

    public TestbenchPlan BuildPlan(TestbenchContext ctx)
    {
        var spec = ctx.Spec;
        var artifacts = new Dictionary<string, string>
        {
            ["results"] = spec.ResultsCsv,
        };
        return new TestbenchPlan
        {
            HarnessId = Id,
            Backend = spec.Backend,
            NetlistName = spec.Name + (spec.Backend == BenchBackendType.Ngspice ? ".cir" : ".scs"),
            Artifacts = artifacts,
            Notes = Description,
        };
    }
}

