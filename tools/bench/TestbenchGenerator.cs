using System.Text.Json;

namespace Cascode.Bench;

public sealed class TestbenchGenerator
{
    private readonly HarnessRegistry _registry;
    private readonly IReadOnlyDictionary<BenchBackendType, ISpiceBackend> _backends;

    public TestbenchGenerator(HarnessRegistry? registry = null)
    {
        _registry = registry ?? new HarnessRegistry();
        _backends = new Dictionary<BenchBackendType, ISpiceBackend>
        {
            [BenchBackendType.Ngspice] = new NgspiceBackend(),
            [BenchBackendType.Spectre] = new SpectreBackend(),
        };
    }

    public TestbenchFiles Generate(TestbenchContext ctx)
    {
        if (!_registry.TryGet(ctx.Spec.Name, out var harness))
        {
            // Allow passing explicit harness id via Spec.Name OR via Args["harness"]
            if (!ctx.Args.TryGetValue("harness", out var idObj) || idObj is not string id || !_registry.TryGet(id, out harness))
            {
                throw new InvalidOperationException("Unknown harness: specify spec.name or args.harness to a registered id.");
            }
        }

        var plan = harness.BuildPlan(ctx);
        if (!_backends.TryGetValue(plan.Backend, out var backend))
        {
            throw new InvalidOperationException($"Unsupported backend: {plan.Backend}");
        }

        var root = EnsureJobDir(ctx.Spec.JobDir);
        var netlistPath = Path.Combine(root, plan.NetlistName);
        var specPath = Path.Combine(root, "spec.json");
        var resultsCsv = Path.Combine(root, ctx.Spec.ResultsCsv);

        var netlistText = TryRenderTemplate(ctx, plan) ?? backend.RenderNetlist(ctx, plan);
        File.WriteAllText(netlistPath, netlistText);
        File.WriteAllText(specPath, JsonSerializer.Serialize(ctx.Spec, new JsonSerializerOptions { WriteIndented = true }));

        return new TestbenchFiles
        {
            RootDir = root,
            NetlistPath = netlistPath,
            SpecPath = specPath,
            ResultsCsv = resultsCsv,
            RunnerPath = string.Empty,
        };
    }

    private static string? TryRenderTemplate(TestbenchContext ctx, TestbenchPlan plan)
    {
        if (!plan.Data.TryGetValue("template_path", out var pathObj))
        {
            return null;
        }

        var path = pathObj?.ToString();
        if (string.IsNullOrWhiteSpace(path) && plan.Data.TryGetValue("template_name", out var nameObj))
        {
            path = nameObj?.ToString();
        }

        string? templateText = null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            templateText = File.ReadAllText(path);
        }
        else if (!string.IsNullOrWhiteSpace(path))
        {
            // Attempt to load from embedded resources (bench assembly)
            var asm = typeof(TestbenchGenerator).Assembly;
            var resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(resName))
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream is not null)
                {
                    using var reader = new StreamReader(stream);
                    templateText = reader.ReadToEnd();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(templateText))
        {
            return null;
        }

        var tpl = templateText!;

        // Use template_model from plan if available (ACIR templates), otherwise build default model
        object model;
        if (plan.Data.TryGetValue("template_model", out var templateModel))
        {
            model = templateModel;
        }
        else
        {
            // Build template model for legacy harnesses
            model = new
            {
                spec = new
                {
                    temperature_c = ctx.Spec.TemperatureC,
                    model_name = ctx.Spec.ModelName,
                    results_csv = ctx.Spec.ResultsCsv,
                    is_subckt = ctx.Spec.IsSubckt
                },
                includes = ctx.DeckPaths,
                includes_with_section = ctx.IncludePathsWithSection,
                includes_without_section = ctx.IncludePathsWithoutSection,
                section = ctx.Section,
                @params = plan.Data.TryGetValue("params", out var p) ? p : new Dictionary<string, object?>()
            };
        }

        return TemplateRenderer.Render(tpl, model);
    }

    private static string EnsureJobDir(string path)
    {
        var full = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? "." : path);
        Directory.CreateDirectory(full);
        return full;
    }
}
