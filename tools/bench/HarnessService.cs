using Cascode.Bench.Yaml;

namespace Cascode.Bench;

public static class HarnessService
{
    public static HarnessRegistry CreateDefault(string workspaceRoot)
    {
        var reg = new HarnessRegistry();
        foreach (var h in Discover(workspaceRoot))
        {
            reg.Register(h);
        }
        return reg;
    }

    public static IEnumerable<ITestbenchHarness> Discover(string workspaceRoot)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            roots.Add(Path.Combine(workspaceRoot, "lib", "harnesses"));
            roots.Add(Path.Combine(workspaceRoot, "examples", "harnesses"));
        }
        // Also search the current working directory for repo-local examples, useful when --workspace points to a PDK tree
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            if (!string.IsNullOrWhiteSpace(cwd))
            {
                roots.Add(Path.Combine(cwd, "lib", "harnesses"));
                roots.Add(Path.Combine(cwd, "examples", "harnesses"));
            }
        }
        catch { }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            roots.Add(Path.Combine(home, ".cascode", "harnesses"));
        }

        return YamlHarnessDiscovery.Discover(roots);
    }
}
