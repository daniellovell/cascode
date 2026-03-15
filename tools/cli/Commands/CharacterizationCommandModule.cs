using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Commands;

internal sealed class CharacterizationCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly CliOutputProvider _output;

    public CharacterizationCommandModule(ShellState state, CliOutputProvider output)
    {
        _state = state;
        _output = output;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand(
                "char",
                "Characterization commands",
                ShowCharUsage,
                helpCategory: CommandHelpCategory.Characterization
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "char gen",
                "Generate characterization testbench",
                CharacterizationGenerateCommand,
                helpCategory: CommandHelpCategory.Characterization
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "char read",
                "Read characterization results",
                CharacterizationReadCommand,
                helpCategory: CommandHelpCategory.Characterization
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "char export",
                "Export derived metrics (e.g., gm/Id)",
                CharacterizationExportCommand,
                helpCategory: CommandHelpCategory.Characterization
            )
        );
    }

    private CommandResult ShowCharUsage(string[] args)
    {
        _output.Get().WriteLine("Usage: char <subcommand>");
        return CommandResult.Success;
    }

    private CommandResult CharacterizationGenerateCommand(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0)
        {
            output.WriteLine("Usage: char gen <model> [--out <dir>] [--param <k=v>]");
            return CommandResult.Success;
        }
        return CharacterizationGenerate(args);
    }

    private CommandResult CharacterizationReadCommand(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0)
        {
            output.WriteLine("Usage: char read <job-dir> [--head <n>]");
            return CommandResult.Success;
        }
        return CharacterizationRead(args);
    }

    private CommandResult CharacterizationExportCommand(string[] args)
    {
        var output = _output.Get();
        if (args.Length == 0)
        {
            output.WriteLine("Usage: char export <job-dir> [--out <file.csv>] [--metrics <list>]");
            return CommandResult.Success;
        }
        var jobDir = PathUtils.NormalizePath(args[0]);
        string? outOverride = null;
        HashSet<string>? metricFilter = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                outOverride = args[++i];
            else if (
                args[i].Equals("--metrics", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
                metricFilter = args[++i]
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        var ok = CharExportService.ExportDerived(
            jobDir,
            metricFilter,
            out var outFile,
            out var msg
        );
        if (ok && !string.IsNullOrWhiteSpace(outOverride))
        {
            try
            {
                File.Copy(outFile, outOverride, overwrite: true);
                outFile = outOverride;
            }
            catch (Exception ex)
            {
                output.Error($"Failed to copy to '{outOverride}': {ex.Message}");
            }
        }
        output.WriteLine(msg);
        return ok ? CommandResult.Success : CommandResult.Failure;
    }

    private CommandResult CharacterizationRead(string[] args)
    {
        var output = _output.Get();
        var jobDir = PathUtils.NormalizePath(args[0]);
        var head = 20;
        for (var i = 1; i < args.Length; i++)
            if (args[i].Equals("--head", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                int.TryParse(args[++i], out head);
        var csv = Path.Combine(jobDir, "results.csv");
        if (!File.Exists(csv))
        {
            output.Error($"Results file not found: {csv}");
            return CommandResult.Failure;
        }
        try
        {
            using var reader = new StreamReader(csv);
            for (var i = 0; i < head && !reader.EndOfStream; i++)
                output.WriteLine(reader.ReadLine() ?? string.Empty);
            if (!reader.EndOfStream)
                output.WriteLine("…");
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            output.Error($"Failed to read: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult CharacterizationGenerate(string[] args)
    {
        var output = _output.Get();
        var modelQuery = args[0];
        string? outDir = null;
        var userParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                outDir = args[++i];
            }
            else if (a.Equals("--param", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var kv = args[++i];
                var eq = kv.IndexOf('=');
                if (eq > 0)
                {
                    var key = kv[..eq].Trim();
                    var value = kv[(eq + 1)..].Trim();
                    if (key.Length > 0)
                        userParams[key] = value;
                }
            }
            else
            {
                output.Error($"Unknown option: {a}");
                return CommandResult.Failure;
            }
        }

        var jobRoot =
            outDir
            ?? Path.Combine(
                _state.WorkspaceRoot,
                "build",
                "char",
                Sanitize(modelQuery),
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")
            );
        Directory.CreateDirectory(jobRoot);

        double TryParseDouble(string key, double fallback) =>
            userParams.TryGetValue(key, out var s) && double.TryParse(s, out var v) ? v : fallback;
        int TryParseInt(string key, int fallback) =>
            userParams.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : fallback;

        var w_m = TryParseDouble("w", TryParseDouble("w_m", 1e-6));
        var l_m = TryParseDouble("l", TryParseDouble("l_m", 0.18e-6));
        var vsbV = TryParseDouble("vsb", 0.0);
        var vdsV = TryParseDouble("vds", 0.9);
        var startV = TryParseDouble("start", 0.0);
        var stopV = TryParseDouble("stop", 1.2);
        var stepV = TryParseDouble("step", 0.01);
        var mult = TryParseInt("mult", 1);
        var nf = TryParseInt("nf", 1);

        ILoggerFactory? localFactory = null;
        try
        {
            var loggerFactory =
                _state.LoggerFactory
                ?? (
                    localFactory = LoggerFactory.Create(builder =>
                    {
                        builder.SetMinimumLevel(LogLevel.Warning);
                        builder.AddSimpleConsole(o =>
                        {
                            o.SingleLine = true;
                        });
                    })
                );

            var result = CharGenService.GenerateAndRun(
                Directory.GetCurrentDirectory(),
                _state.PdkRoot ?? _state.WorkspaceRoot,
                new CharGenService.CharGenArgs(
                    ModelQuery: modelQuery,
                    OutputDir: jobRoot,
                    Corner: "tt",
                    Backend: "ngspice",
                    DeviceName: null,
                    WidthM: w_m,
                    LengthM: l_m,
                    Mult: mult,
                    Nf: nf,
                    VdsV: vdsV,
                    VsbV: vsbV,
                    VgsStartV: startV,
                    VgsStopV: stopV,
                    VgsStepV: stepV
                ),
                loggerFactory,
                output
            );

            if (!result.Succeeded)
            {
                output.Error(result.Message);
                return CommandResult.Failure;
            }

            output.WriteLine(result.Message);
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            output.Error($"char gen failed: {ex.Message}");
            return CommandResult.Failure;
        }
        finally
        {
            localFactory?.Dispose();
        }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
