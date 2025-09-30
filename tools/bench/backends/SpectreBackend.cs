using System.Text;

namespace Cascode.Bench;

public sealed class SpectreBackend : ISpiceBackend
{
    public BenchBackendType Kind => BenchBackendType.Spectre;
    public string FileExtension => ".scs";

    public string RenderNetlist(TestbenchContext ctx, TestbenchPlan plan)
    {
        var s = ctx.Spec;
        var sb = new StringBuilder();
        sb.AppendLine($"// cascode auto-generated: {plan.HarnessId}");
        sb.AppendLine($"simulator lang=spectre");
        sb.AppendLine($"global 0");
        sb.AppendLine($"temp = {s.TemperatureC:F3}");

        foreach (var inc in ctx.DeckPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(ctx.Section))
            {
                sb.AppendLine($"include \"{inc}\" section={ctx.Section}");
            }
            else
            {
                sb.AppendLine($"include \"{inc}\"");
            }
        }

        sb.AppendLine("// sources");
        sb.AppendLine($"VDD (d 0) vsource dc={s.Vds:F6}");
        sb.AppendLine($"VGS (g 0) vsource dc={s.Vgs.Start:F6}");
        sb.AppendLine($"VBS (b s) vsource dc={s.Vsb:F6}");

        sb.AppendLine("// DUT");
        sb.AppendLine($"M1 (d g s b) {s.ModelName} w={s.W_M} l={s.L_M} m={Math.Max(1,s.Mult)}");

        sb.AppendLine($"dcOp dc write=\"spectre.ic\" maxiters=150 maxsteps=5");
        sb.AppendLine($"dc sweep param=VGS.dc start={s.Vgs.Start} stop={s.Vgs.Stop} step={s.Vgs.Step}");
        sb.AppendLine("saveOptions options save=allpub");
        sb.AppendLine("save v(g) v(d) I(VDD) gm(M1) gds(M1)");
        sb.AppendLine("printfile(\"results.csv\", v(g) v(d) I(VDD) gm(M1) gds(M1))");
        return sb.ToString();
    }
}

