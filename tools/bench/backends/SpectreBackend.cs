using System.Linq;
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

        var withSection = ctx.IncludePathsWithSection.Count > 0
            ? ctx.IncludePathsWithSection
            : ctx.DeckPaths;

        foreach (var inc in withSection.Distinct(StringComparer.OrdinalIgnoreCase))
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

        foreach (var inc in ctx.IncludePathsWithoutSection)
        {
            if (!withSection.Contains(inc, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"include \"{inc}\"");
            }
        }

        sb.AppendLine("// sources");
        sb.AppendLine($"VSS (s 0) vsource dc=0");
        sb.AppendLine($"VBS (b s) vsource dc={s.Vsb:F6}");
        sb.AppendLine($"VGS (g 0) vsource dc={s.Vgs.Start:F6}");
        sb.AppendLine($"VDD (d 0) vsource dc={s.Vds:F6}");

        sb.AppendLine("// DUT");
        if (s.IsSubckt)
        {
            // Instantiate a subckt wrapper
            sb.AppendLine($"X1 (d g s b) {s.ModelName} l={s.L_M} w={s.W_M} m={Math.Max(1, s.Mult)} nf={Math.Max(1, s.Nfingers)}");
        }
        else
        {
            // Instantiate a raw device model
            sb.AppendLine($"M1 (d g s b) {s.ModelName} w={s.W_M} l={s.L_M} m={Math.Max(1, s.Mult)}");
        }

        sb.AppendLine($"dcOp dc write=\"spectre.ic\" maxiters=150 maxsteps=5");
        sb.AppendLine($"dc sweep param=VGS start={s.Vgs.Start} stop={s.Vgs.Stop} step={s.Vgs.Step}");
        sb.AppendLine("saveOptions options save=allpub");
        sb.AppendLine("save g");
        sb.AppendLine("save s");
        sb.AppendLine("save d");
        sb.AppendLine("save M1:d");
        sb.AppendLine("save M1:oppoint");
        return sb.ToString();
    }
}
