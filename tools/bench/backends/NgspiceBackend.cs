using System.Text;

namespace Cascode.Bench;

public sealed class NgspiceBackend : ISpiceBackend
{
    public BenchBackendType Kind => BenchBackendType.Ngspice;
    public string FileExtension => ".cir";

    public string RenderNetlist(TestbenchContext ctx, TestbenchPlan plan)
    {
        var s = ctx.Spec;
        var sb = new StringBuilder();
        sb.AppendLine($"* cascode auto-generated: {plan.HarnessId}");
        sb.AppendLine($".title {s.Name}");
        sb.AppendLine($".option numdgt=7");
        sb.AppendLine($".temp {s.TemperatureC:F3}");

        foreach (var inc in ctx.DeckPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(ctx.Section))
            {
                sb.AppendLine($".lib \"{inc}\" {ctx.Section}");
            }
            else
            {
                sb.AppendLine($".include \"{inc}\"");
            }
        }

        // Simple gm/Id style DC sweep harness for MOS devices
        // Node naming: d g s b
        sb.AppendLine("* sources");
        sb.AppendLine($"VDD d 0 {s.Vds:F6}");
        sb.AppendLine($"VGS g 0 {s.Vgs.Start:F6}");
        if (Math.Abs(s.Vsb) > 1e-15)
        {
            sb.AppendLine($"VBS b s {s.Vsb:F6}");
        }
        else
        {
            sb.AppendLine("VBS b s 0");
        }

        sb.AppendLine("* device under test");
        if (s.IsSubckt)
        {
            sb.AppendLine($"X1 d g s b {s.ModelName} l={s.L_M} w={s.W_M} m={Math.Max(1, s.Mult)} nf={Math.Max(1, s.Nfingers)}");
        }
        else
        {
            // Support nmos/pmos models by name; parameters kept minimal for portability
            // Users can extend via harness args later.
            sb.AppendLine($"M1 d g s b {s.ModelName} W={s.W_M} L={s.L_M} m={Math.Max(1, s.Mult)}");
        }

        sb.AppendLine(".control");
        sb.AppendLine("set filetype=ascii");
        sb.AppendLine($"dc VGS {s.Vgs.Start} {s.Vgs.Stop} {s.Vgs.Step}");
        // Write a minimal CSV with vectors available across PDKs
        sb.AppendLine($"wrdata {s.ResultsCsv} v(g) v(d) i(VDD)");
        sb.AppendLine("quit");
        sb.AppendLine(".endc");
        sb.AppendLine(".end");
        return sb.ToString();
    }
}
