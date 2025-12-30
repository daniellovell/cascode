using System.Reflection;

namespace Cascode.Bench;

public sealed class HarnessRegistry
{
    private readonly Dictionary<string, ITestbenchHarness> _harnesses = new(
        StringComparer.OrdinalIgnoreCase
    );

    public HarnessRegistry()
    {
        // Register built-ins
        Register(new GmIdHarness());
    }

    public void Register(ITestbenchHarness harness)
    {
        _harnesses[harness.Id] = harness;
    }

    public bool TryGet(string id, out ITestbenchHarness harness) =>
        _harnesses.TryGetValue(id, out harness!);

    public IReadOnlyCollection<ITestbenchHarness> All => _harnesses.Values;
}
