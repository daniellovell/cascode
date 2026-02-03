using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.Cli.Output;
using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;
using Cascode.Render.Routing;
using Cascode.Render.Svg;

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
                RenderCommand
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
                OutputJson(output, false, 2, null, $"Input file '{inputPath}' not found.");
            }
            else
            {
                output.Error($"Input file '{inputPath}' not found.");
            }
            return new CommandResult(2, false);
        }

        inputPath = Path.GetFullPath(inputPath);

        // Parse Cascode document
        CascodeReadResult readResult;
        using (var reader = File.OpenText(inputPath))
        {
            readResult = CascodeReader.TryRead(reader, inputPath);
        }

        if (!readResult.Success)
        {
            if (options.JsonOutput)
            {
                var errors = readResult
                    .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)
                    .ToList();
                OutputJson(output, false, 2, null, string.Join("; ", errors));
            }
            else
            {
                foreach (
                    var diag in readResult.Diagnostics.Where(d =>
                        d.Severity == DiagnosticSeverity.Error
                    )
                )
                {
                    output.Error($"{diag.FilePath}:{diag.Line}: {diag.Message}");
                }
            }
            return new CommandResult(2, false);
        }

        var doc = readResult.Document!;

        // Find EL-level circuit
        var elCircuit = doc.Circuits.FirstOrDefault(c => c.Level == CascodeLevel.EL);
        if (elCircuit == null)
        {
            var msg = "No EL-level circuit found. Schematic rendering requires EL-level Cascode.";
            if (options.JsonOutput)
            {
                OutputJson(output, false, 2, null, msg);
            }
            else
            {
                output.Error(msg);
            }
            return new CommandResult(2, false);
        }

        try
        {
            // Build circuit graph
            var graph = CircuitGraph.Build(elCircuit);

            // Analyze topology: vertical chains, symmetry, stages
            var topology = TopologyAnalyzer.Analyze(graph);

            // Coarse grid placement using SAT solver
            var placement = CoarseGridPlacer.Place(topology, graph);

            // Wire routing using maze router
            var routing = MazeRouter.Route(placement, graph);

            // Get style
            var style = StyleSheet.GetByName(options.StyleName ?? "default");

            // Render SVG
            var renderer = new SvgRenderer();
            var renderOptions = new RenderOptions
            {
                ShowNetLabels = options.ShowNets,
                ShowDeviceLabels = !options.NoLabels,
                ShowParamLabels = !options.NoParams,
                Title = options.Title,
                ExplicitWidth = options.Width,
                ExplicitHeight = options.Height,
            };

            var svg = renderer.Render(placement, routing, graph, style, renderOptions);

            // Determine output path
            var outputPath = options.OutputPath ?? Path.ChangeExtension(inputPath, ".svg");

            // Write output
            File.WriteAllText(outputPath, svg);

            if (options.JsonOutput)
            {
                OutputJson(output, true, 0, outputPath);
            }
            else
            {
                output.Success($"Rendered schematic: {outputPath}");
                output.WriteLine($"Circuit: {elCircuit.Name}");
                output.WriteLine($"Devices: {graph.Devices.Count}");
            }

            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            if (options.JsonOutput)
            {
                OutputJson(output, false, 1, null, $"Render failed: {ex.Message}");
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
        output.WriteLine("Renders an SVG schematic from an Cascode EL-level circuit.");
        output.WriteLine("");
        output.WriteLine("Options:");
        output.WriteLine("  -o, --output <path>   Output file path (default: <input>.svg)");
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
        string? error = null
    )
    {
        var json = new RenderJsonOutput
        {
            Success = success,
            ExitCode = exitCode,
            OutputPath = outputPath,
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

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
