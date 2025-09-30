namespace Cascode.Bench;

public interface ITestbenchHarness
{
    string Id { get; }
    string Description { get; }
    IReadOnlyList<BenchBackendType> SupportedBackends { get; }

    // Describe parameters accepted by the harness to enable prompting/validation.
    IReadOnlyList<HarnessParam> Params { get; }

    TestbenchPlan BuildPlan(TestbenchContext ctx);
}

public sealed record HarnessParam(
    string Name,
    string Type,
    string Description,
    object? DefaultValue = null,
    bool Required = false,
    IReadOnlyList<object>? Choices = null
);

