using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Spectre.Console;
using Cascode.Cli.Services;
using Cascode.Workspace;

namespace Cascode.Cli.Commands;

internal sealed class CharacterizationCommandModule : ICommandModule
{
    private readonly ShellState _state;
    private readonly WorkspaceScanStorage _storage;

    public CharacterizationCommandModule(ShellState state, WorkspaceScanStorage storage)
    {
        _state = state;
        _storage = storage;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(new DelegateCliCommand("char", "Characterization commands", ShowCharUsage));
        registry.Register(new DelegateCliCommand("char gen", "Generate characterization testbench", CharacterizationGenerateCommand));
        registry.Register(new DelegateCliCommand("char read", "Read characterization results", CharacterizationReadCommand));
        registry.Register(new DelegateCliCommand("char export", "Export derived metrics (e.g., gm/Id)", CharacterizationExportCommand));
    }

    private CommandResult ShowCharUsage(string[] args)
    {
        _state.AddMessage("Usage: char <subcommand>");
        return CommandResult.Success;
    }

    private CommandResult CharacterizationGenerateCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: char gen <model> [--harness <id>] [--backend spectre|ngspice] [--out <dir>] [--corner <name>]");
            return CommandResult.Success;
        }
        return CharacterizationGenerate(args);
    }

    private CommandResult CharacterizationReadCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: char read <job-dir> [--head <n>]");
            return CommandResult.Success;
        }
        return CharacterizationRead(args);
    }

    private CommandResult CharacterizationExportCommand(string[] args)
    {
        if (args.Length == 0)
        {
            _state.AddMessage("Usage: char export <job-dir> [--out <file.csv>] [--metrics <list>]");
            return CommandResult.Success;
        }
        var jobDir = PathUtils.NormalizePath(args[0]);
        string? outOverride = null;
        HashSet<string>? metricFilter = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) outOverride = args[++i];
            else if (args[i].Equals("--metrics", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) metricFilter = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        var ok = CharExportService.ExportDerived(jobDir, metricFilter, out var outFile, out var msg);
        if (ok && !string.IsNullOrWhiteSpace(outOverride))
        {
            try { File.Copy(outFile, outOverride, overwrite: true); outFile = outOverride; }
            catch (Exception ex) { _state.AddMessage($"Failed to copy to '{outOverride}': {ex.Message}"); }
        }
        _state.AddMessage(msg);
        return ok ? CommandResult.Success : CommandResult.Failure;
    }

    private CommandResult CharacterizationRead(string[] args)
    {
        var jobDir = PathUtils.NormalizePath(args[0]);
        var head = 20;
        for (var i = 1; i < args.Length; i++) if (args[i].Equals("--head", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) int.TryParse(args[++i], out head);
        var csv = Path.Combine(jobDir, "results.csv");
        if (!File.Exists(csv)) { _state.AddMessage($"Results file not found: {csv}"); return CommandResult.Failure; }
        try
        {
            using var reader = new StreamReader(csv);
            for (var i = 0; i < head && !reader.EndOfStream; i++) _state.AddMessage(reader.ReadLine() ?? string.Empty);
            if (!reader.EndOfStream) _state.AddMessage("…");
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Failed to read: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private CommandResult CharacterizationGenerate(string[] args)
    {
        var modelName = args[0];
        var backend = "ngspice";
        string? outDir = null;
        string? corner = null;
        string harness = "gm_id.v1";
        var userParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a.Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) backend = args[++i];
            else if (a.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) outDir = args[++i];
            else if (a.Equals("--corner", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) corner = args[++i];
            else if (a.Equals("--harness", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) harness = args[++i];
            else if (a.Equals("--param", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var kv = args[++i];
                var eq = kv.IndexOf('=');
                if (eq > 0)
                {
                    var key = kv[..eq].Trim();
                    var value = kv[(eq + 1)..].Trim();
                    if (key.Length > 0) userParams[key] = value;
                }
            }
        }

        var scan = EnsureScan();
        if (scan is null) return CommandResult.Failure;
        var model = scan.Models.FirstOrDefault(m => string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase)) ?? scan.Models.FirstOrDefault(m => m.Name.Contains(modelName, StringComparison.OrdinalIgnoreCase));
        if (model is null) { _state.AddMessage("Model not found."); return CommandResult.Failure; }

        var jobRoot = outDir ?? Path.Combine(_state.WorkspaceRoot, "build", "char", Sanitize(model.Name), DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(jobRoot);

        double TryParseDouble(string key, double fallback) => userParams.TryGetValue(key, out var s) && double.TryParse(s, out var v) ? v : fallback;
        int TryParseInt(string key, int fallback) => userParams.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : fallback;

        var w_m = TryParseDouble("w", TryParseDouble("w_m", 1e-6));
        var l_m = TryParseDouble("l", TryParseDouble("l_m", 0.18e-6));
        var vsbVal = TryParseDouble("vsb", 0.0);
        var vdsVal = TryParseDouble("vds", 0.9);
        var start = TryParseDouble("start", 0.0);
        var stop = TryParseDouble("stop", 1.2);
        var step = TryParseDouble("step", 0.01);
        var multVal = TryParseInt("mult", 1);
        var nfVal = TryParseInt("nf", 1);

        static string? TryNormalizeInclude(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return PathUtils.NormalizePath(path); }
            catch { return File.Exists(path) ? Path.GetFullPath(path) : null; }
        }

        var rawDecks = model.Decks ?? Array.Empty<string>();
        var decksWithSection = rawDecks.Select(TryNormalizeInclude).Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p!)).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var sourceIncludesAll = (model.SourceFiles ?? Array.Empty<string>()).Select(TryNormalizeInclude).Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p!)).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        List<string> extraIncludes = new();
        if (!string.IsNullOrWhiteSpace(corner))
        {
            var key = corner!.Trim();
            sourceIncludesAll = sourceIncludesAll.Where(p => Path.GetFileName(p)!.IndexOf($"_{key}", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
        if (decksWithSection.Count == 0) extraIncludes = sourceIncludesAll; else extraIncludes.Clear();

        var resolvedIncludes = new List<string>(decksWithSection.Count + extraIncludes.Count);
        resolvedIncludes.AddRange(decksWithSection);
        resolvedIncludes.AddRange(extraIncludes);

        static string ResolveModelNameForNetlist(Cascode.Workspace.SpectreModel m)
        {
            var name = m.Name;
            if (string.IsNullOrWhiteSpace(name)) return name;
            var modelMarker = name.IndexOf("__model", StringComparison.OrdinalIgnoreCase);
            if (modelMarker < 0) return name;
            var basePart = name.Substring(0, modelMarker);
            var lastSeparator = basePart.LastIndexOf("__", StringComparison.Ordinal);
            if (lastSeparator >= 0 && lastSeparator + 2 < basePart.Length) basePart = basePart[(lastSeparator + 2)..];
            return basePart.Replace('.', '_');
        }
        var netlistModelName = ResolveModelNameForNetlist(model);
        if (resolvedIncludes.Count == 0) _state.AddMessage($"[warn] No include decks located for model '{model.Name}'. Spectre run may fail.");

        var spec = new Cascode.Bench.TestbenchSpec
        {
            Backend = backend.Equals("spectre", StringComparison.OrdinalIgnoreCase) ? Cascode.Bench.BenchBackendType.Spectre : Cascode.Bench.BenchBackendType.Ngspice,
            Name = harness,
            ModelName = netlistModelName,
            IsSubckt = string.Equals(model.ModelType, "subckt", StringComparison.OrdinalIgnoreCase),
            Corner = corner,
            TemperatureC = 27,
            SupplyV = 0,
            W_M = w_m,
            L_M = l_m,
            Mult = multVal,
            Nfingers = nfVal,
            Vgs = new Cascode.Bench.SweepSpec(start, stop, step),
            Vds = vdsVal,
            Vsb = vsbVal,
            Includes = resolvedIncludes,
            Section = corner,
            JobDir = jobRoot,
            ResultsCsv = "results.csv"
        };

        var ctx = new Cascode.Bench.TestbenchContext
        {
            Spec = spec,
            WorkspaceRoot = _state.WorkspaceRoot,
            PdkRoot = _state.PdkRoot ?? _state.WorkspaceRoot,
            DeckPaths = resolvedIncludes,
            IncludePathsWithSection = decksWithSection,
            IncludePathsWithoutSection = extraIncludes,
            Section = corner,
            Args = userParams.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.OrdinalIgnoreCase),
        };

        try
        {
            var reg = Cascode.Bench.HarnessService.CreateDefault(_state.WorkspaceRoot);
            var gen = new Cascode.Bench.TestbenchGenerator(reg);
            var files = gen.Generate(ctx);
            _state.AddMessage($"Generated testbench: {files.NetlistPath}");
            _state.AddMessage($"Spec: {files.SpecPath}");
            _state.AddMessage("Run your simulator manually and then 'char export' to derive gm/Id.");
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            _state.AddMessage($"Generation failed: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private WorkspaceScanResult? EnsureScan()
    {
        if (_state.Scan is not null) return _state.Scan;
        var scanPath = WorkspaceState.GetScanPath(_state.WorkspaceRoot);
        if (File.Exists(scanPath))
        {
            try
            {
                _state.Scan = _storage.Load(scanPath);
                _state.SelectedDeckIndex = _state.Scan.ModelDecks.Count > 0 ? 0 : null;
                return _state.Scan;
            }
            catch (Exception ex)
            {
                _state.AddMessage($"Failed to load cached scan: {ex.Message}");
            }
        }
        _state.AddMessage("No workspace scan available. Run pdk scan.");
        return null;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}

