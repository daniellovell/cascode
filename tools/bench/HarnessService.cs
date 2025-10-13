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

    /// <summary>
    /// Discovers testbench harness definitions by searching a set of well-known harness directories.
    /// </summary>
    /// <param name="workspaceRoot">Optional workspace root to search; when provided the method includes
    /// workspaceRoot/lib/harnesses and workspaceRoot/examples/harnesses. If null or whitespace this root is ignored.</param>
    /// <returns>An enumerable of harnesses found in the discovered directories.</returns>
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
        var cascodeHome = Environment.GetEnvironmentVariable("CASCODE_HOME");
        if (string.IsNullOrWhiteSpace(cascodeHome))
        {
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(user)) cascodeHome = Path.Combine(user, ".cascode");
        }
        if (!string.IsNullOrWhiteSpace(cascodeHome)) roots.Add(Path.Combine(cascodeHome!, "harnesses"));

        return YamlHarnessDiscovery.Discover(roots);
    }
}