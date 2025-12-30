using Scriban;

namespace Cascode.Bench.Yaml;

public sealed class YamlTemplateHarness : ITestbenchHarness
{
    private readonly string _baseDir;
    private readonly HarnessYaml _manifest;

    public YamlTemplateHarness(string baseDir, HarnessYaml manifest)
    {
        _baseDir = Path.GetFullPath(baseDir);
        _manifest = manifest;
        Id = _manifest.Id;
        Description = _manifest.Description ?? string.Empty;
        SupportedBackends = _manifest
            .Backends.Select(ToBackend)
            .Where(x => x is not null)
            .Cast<BenchBackendType>()
            .Distinct()
            .ToArray();

        Params = _manifest
            .Params.Select(kv => new HarnessParam(
                kv.Key,
                kv.Value.Type,
                kv.Value.Description ?? string.Empty,
                kv.Value.Default,
                kv.Value.Required,
                kv.Value.Choices?.ToArray() ?? Array.Empty<object>()
            ))
            .ToArray();
    }

    public string Id { get; }
    public string Description { get; }
    public IReadOnlyList<BenchBackendType> SupportedBackends { get; }
    public IReadOnlyList<HarnessParam> Params { get; }

    public TestbenchPlan BuildPlan(TestbenchContext ctx)
    {
        var backend = ctx.Spec.Backend;
        EnsureBackendSupported(backend);

        // Resolve params (defaults from manifest overridden by ctx.Args)
        var resolved = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, def) in _manifest.Params)
        {
            if (ctx.Args.TryGetValue(name, out var val))
            {
                resolved[name] = Coerce(val, def.Type);
            }
            else
            {
                resolved[name] = def.Default;
            }
        }

        // Select template path for backend
        var templateRel =
            backend == BenchBackendType.Spectre
                ? _manifest.Templates.Spectre
                : _manifest.Templates.Ngspice;
        if (string.IsNullOrWhiteSpace(templateRel))
        {
            throw new InvalidOperationException(
                $"Harness '{Id}' lacks a template for backend {backend}."
            );
        }
        var templatePath = Path.Combine(_baseDir, templateRel!);

        var artifacts = new Dictionary<string, string> { ["results"] = ctx.Spec.ResultsCsv };

        return new TestbenchPlan
        {
            HarnessId = Id,
            Backend = backend,
            NetlistName = MakeNetlistName(ctx.Spec.Name, backend),
            Artifacts = artifacts,
            Notes = Description,
            Data = new Dictionary<string, object>
            {
                ["template_path"] = templatePath,
                ["params"] = resolved,
            },
        };
    }

    private static object? Coerce(object? value, string type)
    {
        if (value is null)
            return null;
        try
        {
            switch (type.ToLowerInvariant())
            {
                case "number":
                    return Convert.ToDouble(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                case "integer":
                    return Convert.ToInt32(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                case "enum":
                case "string":
                default:
                    return value.ToString();
            }
        }
        catch
        {
            return value;
        }
    }

    private static BenchBackendType? ToBackend(string s)
    {
        return s?.Trim().ToLowerInvariant() switch
        {
            "spectre" => BenchBackendType.Spectre,
            "ngspice" => BenchBackendType.Ngspice,
            _ => null,
        };
    }

    private static void EnsureBackendSupported(BenchBackendType backend)
    {
        // Nothing to enforce here; handled by template presence
    }

    private static string MakeNetlistName(string baseName, BenchBackendType backend)
    {
        var ext = backend == BenchBackendType.Spectre ? ".scs" : ".cir";
        return baseName + ext;
    }
}
