using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cascode.Bench.Yaml;

public static class YamlHarnessDiscovery
{
    public static IEnumerable<ITestbenchHarness> Discover(IEnumerable<string> roots)
    {
        var list = new List<ITestbenchHarness>();
        var debug = Environment.GetEnvironmentVariable("CASCODE_DEBUG") == "1";
        foreach (var root in roots)
        {
            var baseDir = SafeGetFullPath(root);
            if (baseDir is null || !Directory.Exists(baseDir)) continue;
            if (debug) Console.WriteLine($"[debug] scan dir: {baseDir}");

            foreach (var path in Directory.EnumerateFiles(baseDir, "harness.yaml", SearchOption.AllDirectories))
            {
                try
                {
                    var text = File.ReadAllText(path);
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(UnderscoredNamingConvention.Instance)
                        .IgnoreUnmatchedProperties()
                        .Build();
                    var manifest = deserializer.Deserialize<HarnessYaml>(text);
                    if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id)) { if (debug) Console.WriteLine($"[debug] skip (no id): {path}"); continue; }
                    var harness = new YamlTemplateHarness(Path.GetDirectoryName(path)!, manifest);
                    if (debug) Console.WriteLine($"[debug] found harness: {manifest.Id} at {path}");
                    list.Add(harness);
                }
                catch (Exception ex)
                {
                    if (debug) Console.WriteLine($"[debug] invalid harness at {path}: {ex.Message}");
                }
            }
        }
        return list;
    }

    private static string? SafeGetFullPath(string path)
    {
        try { return Path.GetFullPath(path); } catch { return null; }
    }
}
