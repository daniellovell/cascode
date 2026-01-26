using System.Reflection;

namespace Cascode.Bench;

/// <summary>
/// Provides access to embedded builtin bench templates.
/// </summary>
public static class BenchTemplateLibrary
{
    private const string ResourcePrefix = "Cascode.Bench.Benches.";

    private static readonly Lazy<Dictionary<string, string>> _templates = new(LoadTemplates);

    public static bool TryGetTemplate(
        string benchName,
        BenchBackendType backend,
        out string templateText
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchName);

        var fileName = BuildTemplateFileName(benchName, backend);
        return _templates.Value.TryGetValue(fileName, out templateText!);
    }

    public static IReadOnlyList<string> GetBenchNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileName in _templates.Value.Keys)
        {
            var baseName = ExtractBenchName(fileName);
            if (!string.IsNullOrWhiteSpace(baseName))
            {
                names.Add(baseName);
            }
        }

        return names.ToArray();
    }

    private static string BuildTemplateFileName(string benchName, BenchBackendType backend)
    {
        var suffix = backend == BenchBackendType.Spectre ? "spectre" : "ngspice";
        return $"{benchName}.{suffix}.tpl";
    }

    private static string ExtractBenchName(string fileName)
    {
        const string ngspiceSuffix = ".ngspice.tpl";
        const string spectreSuffix = ".spectre.tpl";

        if (fileName.EndsWith(ngspiceSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return fileName[..^ngspiceSuffix.Length];
        }

        if (fileName.EndsWith(spectreSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return fileName[..^spectreSuffix.Length];
        }

        return string.Empty;
    }

    private static Dictionary<string, string> LoadTemplates()
    {
        var templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(BenchTemplateLibrary).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        foreach (var resourceName in resourceNames)
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var fileName = resourceName[ResourcePrefix.Length..];
            if (!fileName.EndsWith(".tpl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            templates[fileName] = reader.ReadToEnd();
        }

        return templates;
    }
}
