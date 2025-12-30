namespace Cascode.Bench;

public interface ISpiceBackend
{
    BenchBackendType Kind { get; }
    string FileExtension { get; }

    // Render the netlist text for the given context and harness-generated plan.
    string RenderNetlist(TestbenchContext ctx, TestbenchPlan plan);
}
