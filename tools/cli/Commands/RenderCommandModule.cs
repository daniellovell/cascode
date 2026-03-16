using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Layout;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cascode.Cli.Commands;

/// <summary>
/// Command module for rendering SVG schematics from Cascode EL circuits.
/// </summary>
internal sealed class RenderCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly CliOutputProvider _output;

    public RenderCommandModule(ShellState state, CliOutputProvider output)
    {
        _state = state;
        _output = output;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand(
                "render",
                "Render SVG schematic from Cascode EL circuit",
                RenderCommand,
                helpCategory: CommandHelpCategory.Design
            )
        );
    }

    private CommandResult RenderCommand(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0)
        {
            ShowUsage(output);
            return CommandResult.Success;
        }

        var inputPath = args[0];
        var options = ParseOptions(args);

        if (!File.Exists(inputPath))
        {
            if (options.JsonOutput)
            {
                OutputJson(output, false, 2, null, error: $"Input file '{inputPath}' not found.");
            }
            else
            {
                output.Error($"Input file '{inputPath}' not found.");
            }
            return new CommandResult(2, false);
        }

        inputPath = Path.GetFullPath(inputPath);

        var inputDir = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
        var loadLogger = _state.LoggerFactory?.CreateLogger("CascodeLinker") ?? NullLogger.Instance;
        var linkArtifactsDir = Path.Combine(inputDir, "build", "render");
        if (
            !CascodeLoadLinkService.TryLoadAndLinkIfNeeded(
                inputPath,
                _state.WorkspaceRoot,
                linkArtifactsDir,
                loadLogger,
                out var loaded,
                out var diagnostics
            )
        )
        {
            if (options.JsonOutput)
            {
                var errors = diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)
                    .ToList();
                OutputJson(output, false, 2, null, error: string.Join("; ", errors));
            }
            else
            {
                foreach (var diag in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    output.Error($"{diag.FilePath}:{diag.Line}: {diag.Message}");
                }
            }
            return new CommandResult(2, false);
        }

        var doc = loaded.Document;
        var attachResolution = new AttachResolver(doc).Resolve();

        var circuitsToRender = doc
            .Circuits.Where(c => !c.Inline && c.Level is CascodeLevel.EL or CascodeLevel.ML)
            .ToList();
        if (circuitsToRender.Count == 0)
        {
            var msg =
                "No non-inline EL or ML circuit found. Rendering requires Cascode circuits at EL or ML level.";
            if (options.JsonOutput)
            {
                OutputJson(output, false, 2, null, error: msg);
            }
            else
            {
                output.Error(msg);
            }
            return new CommandResult(2, false);
        }

        try
        {
            // Get style
            var style = StyleSheet.GetByName(options.StyleName ?? "default");

            var renderOptions = new RenderOptions
            {
                ShowNetLabels = options.ShowNets,
                ShowDeviceLabels = !options.NoLabels,
                ShowParamLabels = !options.NoParams,
                Title = options.Title,
                ExplicitWidth = options.Width,
                ExplicitHeight = options.Height,
            };

            var outputPaths = RenderCircuits(
                circuitsToRender,
                doc,
                attachResolution,
                style,
                renderOptions,
                options,
                inputDir
            );

            if (options.JsonOutput)
            {
                OutputJson(
                    output,
                    true,
                    0,
                    outputPaths.Count == 1 ? outputPaths[0] : null,
                    outputPaths.Count > 1 ? outputPaths : null
                );
            }
            else
            {
                foreach (var path in outputPaths)
                {
                    output.Success($"Rendered: {path}");
                }
            }

            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            if (options.JsonOutput)
            {
                OutputJson(output, false, 1, null, error: $"Render failed: {ex.Message}");
            }
            else
            {
                output.Error($"Render failed: {ex.Message}");
            }
            return CommandResult.Failure;
        }
    }

    private static void ShowUsage(ICliOutput output)
    {
        output.WriteLine("Usage: render <cascode_file> [options]");
        output.WriteLine("");
        output.WriteLine("Renders SVG output from Cascode EL/ML circuits.");
        output.WriteLine("");
        output.WriteLine("Options:");
        output.WriteLine(
            "  -o, --output <path>   Output file path (single circuit) or output directory (multi-circuit)"
        );
        output.WriteLine(
            "  --style <name>        Style preset: default, dark, minimal, publication"
        );
        output.WriteLine("  --width <pixels>      Explicit width");
        output.WriteLine("  --height <pixels>     Explicit height");
        output.WriteLine("  --show-nets           Show internal net labels");
        output.WriteLine("  --no-labels           Hide device labels");
        output.WriteLine("  --no-params           Hide parameter labels");
        output.WriteLine("  --title <text>        Add title to schematic");
        output.WriteLine("  --json                Output result as JSON");
    }

    private static List<string> RenderCircuits(
        IReadOnlyList<Circuit> circuits,
        CascodeDocument document,
        AttachResolutionResult attachResolution,
        StyleSheet style,
        RenderOptions renderOptions,
        RenderCommandOptions commandOptions,
        string inputDir
    )
    {
        var outputPaths = new List<string>();

        var outputRoot = ResolveOutputRoot(circuits, commandOptions, inputDir);
        Directory.CreateDirectory(outputRoot);
        var isSingleSvgOutput = circuits.Count == 1 && IsSvgFilePath(commandOptions.OutputPath);

        foreach (var circuit in circuits.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            var outputPath = isSingleSvgOutput
                ? Path.GetFullPath(commandOptions.OutputPath!)
                : Path.Combine(outputRoot, $"{circuit.Name}.svg");

            if (circuit.Level == CascodeLevel.ML)
            {
                var blockSvg = new BlockDiagramRenderer().Render(circuit, style, renderOptions);
                File.WriteAllText(outputPath, blockSvg);
                outputPaths.Add(outputPath);
                continue;
            }

            var resolution = attachResolution.CircuitResults.GetValueOrDefault(circuit.Name);
            var flattened = CircuitFlattener.Flatten(circuit, document, resolution);
            var graph = CircuitGraph.Build(flattened);
            CoarseGridResult placement;
            RoutingResult routing;

            if (circuit.Render?.Mode == RenderLayoutMode.Manual)
            {
                var exact = ExactSchematicResolver.Resolve(
                    flattened.RootCircuit,
                    graph,
                    circuit.Render
                );
                placement = exact.Placement;
                routing = exact.Routing;
            }
            else
            {
                var topology = TopologyAnalyzer.Analyze(graph);
                placement = CoarseGridPlacer.Place(topology, graph);
                routing = MazeRouter.Route(placement, graph);
            }

            var schematicSvg = new SvgRenderer().Render(
                placement,
                routing,
                graph,
                style,
                renderOptions
            );
            File.WriteAllText(outputPath, schematicSvg);
            outputPaths.Add(outputPath);
        }

        return outputPaths;
    }

    private static string ResolveOutputRoot(
        IReadOnlyList<Circuit> circuits,
        RenderCommandOptions options,
        string inputDir
    )
    {
        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            return Path.Combine(inputDir, "build");
        }

        var outputPath = Path.GetFullPath(options.OutputPath);
        if (IsSvgFilePath(outputPath))
        {
            if (circuits.Count > 1)
            {
                throw new InvalidOperationException(
                    "Multiple circuits selected for rendering. Use --output <dir> to choose an output directory."
                );
            }

            return Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        }

        return outputPath;
    }

    private static bool IsSvgFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase);
    }

    private static RenderCommandOptions ParseOptions(string[] args)
    {
        var options = new RenderCommandOptions();

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if ((arg == "-o" || arg == "--output") && i + 1 < args.Length)
            {
                options.OutputPath = args[++i];
            }
            else if (arg == "--style" && i + 1 < args.Length)
            {
                options.StyleName = args[++i];
            }
            else if (arg == "--width" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var width))
                {
                    options.Width = width;
                }
            }
            else if (arg == "--height" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var height))
                {
                    options.Height = height;
                }
            }
            else if (arg == "--show-nets")
            {
                options.ShowNets = true;
            }
            else if (arg == "--no-labels")
            {
                options.NoLabels = true;
            }
            else if (arg == "--no-params")
            {
                options.NoParams = true;
            }
            else if (arg == "--title" && i + 1 < args.Length)
            {
                options.Title = args[++i];
            }
            else if (arg == "--json")
            {
                options.JsonOutput = true;
            }
        }

        return options;
    }

    private static void OutputJson(
        ICliOutput cliOutput,
        bool success,
        int exitCode,
        string? outputPath,
        IReadOnlyList<string>? outputPaths = null,
        string? error = null
    )
    {
        var json = new RenderJsonOutput
        {
            Success = success,
            ExitCode = exitCode,
            OutputPath = outputPath,
            OutputPaths = outputPaths,
            Error = error,
        };

        cliOutput.WriteLine(JsonSerializer.Serialize(json, RenderJsonOutput.SerializerOptions));
    }

    private sealed class RenderCommandOptions
    {
        public string? OutputPath { get; set; }
        public string? StyleName { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public bool ShowNets { get; set; }
        public bool NoLabels { get; set; }
        public bool NoParams { get; set; }
        public string? Title { get; set; }
        public bool JsonOutput { get; set; }
    }
}

internal sealed class RenderJsonOutput
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; }

    [JsonPropertyName("outputPath")]
    public string? OutputPath { get; init; }

    [JsonPropertyName("outputPaths")]
    public IReadOnlyList<string>? OutputPaths { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
