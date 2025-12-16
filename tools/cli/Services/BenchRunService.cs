using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cascode.ACIR;
using Cascode.Bench;
using Cascode.Parser;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Services;

public class BenchRunService
{
    private readonly ILogger<BenchRunService> _logger;

    public BenchRunService(ILogger<BenchRunService> logger)
    {
        _logger = logger;
    }

    public sealed record BenchRunArgs(
        string AcirPath,
        string? BenchName,
        string? OutputDir,
        BenchBackendType Backend);

    public sealed record BenchRunResult(
        int ExitCode,
        IReadOnlyList<string> Messages);

    public static bool TryParseArgs(string[] args, out BenchRunArgs parsed, out string error)
    {
        parsed = new BenchRunArgs(string.Empty, null, null, BenchBackendType.Ngspice);
        error = string.Empty;

        string? acirPath = null;
        string? benchName = null;
        string? outputDir = null;
        var backend = BenchBackendType.Ngspice;
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--acir" && i + 1 < args.Length)
            {
                acirPath = args[++i];
            }
            else if ((args[i] == "--bench" || args[i] == "-b") && i + 1 < args.Length)
            {
                benchName = args[++i];
            }
            else if ((args[i] == "--out" || args[i] == "-o") && i + 1 < args.Length)
            {
                outputDir = args[++i];
            }
            else if (args[i] == "--backend" && i + 1 < args.Length)
            {
                var backendStr = args[++i].ToLowerInvariant();
                if (backendStr != "ngspice")
                {
                    error = $"Error: unsupported backend '{backendStr}'. Only 'ngspice' is supported currently.";
                    return false;
                }
                backend = BenchBackendType.Ngspice;
            }
            else if (args[i].StartsWith("-", StringComparison.Ordinal))
            {
                error = $"Error: unknown option '{args[i]}'.";
                return false;
            }
            else
            {
                positionals.Add(args[i]);
            }
        }

        if (string.IsNullOrWhiteSpace(acirPath) && positionals.Count >= 1)
        {
            acirPath = positionals[0];
        }

        if (string.IsNullOrWhiteSpace(benchName) && positionals.Count >= 2)
        {
            benchName = positionals[1];
        }

        if (string.IsNullOrWhiteSpace(acirPath))
        {
            error = "Error: ACIR file path is required.";
            return false;
        }

        if (!File.Exists(acirPath))
        {
            error = $"Error: ACIR file '{acirPath}' not found.";
            return false;
        }

        parsed = new BenchRunArgs(Path.GetFullPath(acirPath), string.IsNullOrWhiteSpace(benchName) ? null : benchName, outputDir, backend);
        return true;
    }

    public BenchRunResult Run(string workspaceRoot, BenchRunArgs args)
    {
        var messages = new List<string>();

        var doc = ReadAcir(args.AcirPath);
        var circuit = GetSingleElCircuit(doc);

        var availableBenches = GetAvailableBenchNames(circuit);
        var benchesToRun = ResolveBenchesToRunOrError(args.BenchName, availableBenches, messages);
        if (benchesToRun == null)
        {
            return new BenchRunResult(2, messages);
        }

        var outputDir = ResolveOutputDir(args.OutputDir, circuit.Name, benchesToRun);
        Directory.CreateDirectory(outputDir);

        var resolvedWorkspaceRoot = ResolveWorkspaceRoot(args.AcirPath, workspaceRoot);
        var emit = SpiceEmitter.ValidateAndEmit(doc, outputDir, args.Backend, resolvedWorkspaceRoot);
        if (!emit.Validation.IsValid)
        {
            var first = emit.Validation.GetErrors().FirstOrDefault()?.ToString() ?? "Emission failed.";
            _logger.LogError("ACIR emission validation failed: {Error}", first);
            return new BenchRunResult(2, new[] { first });
        }

        var sweepNames = GetSweepNames(circuit);
        var allMeasurements = new Dictionary<string, MeasurementResult>(StringComparer.OrdinalIgnoreCase);
        var hadSimulationFailure = RunBenches(circuit, args, sweepNames, emit.Emit.TestbenchPaths, benchesToRun, allMeasurements, messages);
        if (allMeasurements.Count == 0)
        {
            return new BenchRunResult(1, messages.Count == 0 ? new[] { "No benches completed successfully." } : messages);
        }

        var combinedResults = CreateCombinedResults(circuit.Name, benchesToRun, allMeasurements);
        if (benchesToRun.Count > 1)
        {
            WriteCombinedResults(outputDir, circuit.Name, combinedResults, messages);
        }

        var report = ComplianceChecker.Check(circuit, combinedResults);
        _logger.LogInformation("Compliance check: {PassedCount}/{TotalCount} constraints satisfied", report.PassedCount, report.TotalCount);
        messages.Add($"Compliance: {report.PassedCount}/{report.TotalCount} constraints satisfied");

        var exit = hadSimulationFailure || report.FailedCount != 0 ? 1 : 0;
        return new BenchRunResult(exit, messages);
    }

    private static Circuit GetSingleElCircuit(ACIRDocument doc)
    {
        return doc.Circuits.FirstOrDefault(c => c.Level == ACIRLevel.EL)
            ?? throw new InvalidOperationException("No EL-level circuits found in ACIR document.");
    }

    private IReadOnlyList<string>? ResolveBenchesToRunOrError(string? explicitBench, string[] availableBenches, List<string> messages)
    {
        if (availableBenches.Length == 0)
        {
            const string msg = "No benches declared in ACIR benches block.";
            _logger.LogError(msg);
            messages.Add(msg);
            return null;
        }

        var benches = ResolveBenchesToRun(availableBenches, explicitBench);
        if (benches == null)
        {
            var list = string.Join(", ", availableBenches);
            var msg = $"Bench '{explicitBench}' not declared in ACIR benches block. Available: {list}";
            _logger.LogError("Bench '{BenchName}' not declared in ACIR benches block. Available: {Available}", explicitBench, list);
            messages.Add(msg);
            return null;
        }

        return benches;
    }

    private static string ResolveOutputDir(string? outputDir, string circuitName, IReadOnlyList<string> benchesToRun)
    {
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            return Path.GetFullPath(outputDir);
        }

        var leaf = benchesToRun.Count == 1 ? $"{circuitName}_{benchesToRun[0]}" : circuitName;
        return Path.Combine(Directory.GetCurrentDirectory(), "build", "bench", leaf);
    }

    private static string ResolveWorkspaceRoot(string acirPath, string workspaceRoot)
    {
        var resolved = FindWorkspaceRoot(acirPath) ?? workspaceRoot;
        return string.IsNullOrWhiteSpace(resolved) ? Directory.GetCurrentDirectory() : resolved;
    }

    private static HashSet<string> GetSweepNames(Circuit circuit)
    {
        return circuit.Harness?.Sweeps?.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
               ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private bool RunBenches(
        Circuit circuit,
        BenchRunArgs args,
        HashSet<string> sweepNames,
        IReadOnlyList<string> testbenchPaths,
        IReadOnlyList<string> benchesToRun,
        Dictionary<string, MeasurementResult> allMeasurements,
        List<string> messages)
    {
        var hadSimulationFailure = false;

        foreach (var benchName in benchesToRun)
        {
            if (!TryRunBench(circuit, args, sweepNames, testbenchPaths, benchName, allMeasurements, messages))
            {
                hadSimulationFailure = true;
            }
        }

        return hadSimulationFailure;
    }

    private bool TryRunBench(
        Circuit circuit,
        BenchRunArgs args,
        HashSet<string> sweepNames,
        IReadOnlyList<string> testbenchPaths,
        string benchName,
        Dictionary<string, MeasurementResult> allMeasurements,
        List<string> messages)
    {
        var testbenchPath = FindTestbenchPath(testbenchPaths, circuit.Name, benchName);

        NgspiceRun run;
        try
        {
            run = RunNgspice(testbenchPath);
        }
        catch (Exception ex)
        {
            var msg = $"Failed to run ngspice for '{benchName}': {ex.Message}";
            _logger.LogError(ex, "Failed to run ngspice for '{BenchName}': {Message}", benchName, ex.Message);
            messages.Add(msg);
            return false;
        }

        if (run.ExitCode != 0)
        {
            _logger.LogError("Simulation '{BenchName}' failed with exit code {ExitCode}. Stderr: {Stderr}", benchName, run.ExitCode, run.Stderr);
            messages.Add($"Simulation '{benchName}' failed (exit {run.ExitCode}).");
            messages.Add(run.Stderr);
            return false;
        }

        var points = ParsePoints(run.Stdout, sweepNames);
        var results = ParseResults(run.Stdout, circuit, benchName);
        MergeMeasurements(allMeasurements, results.Measurements.Values);

        var resultsPath = Path.Combine(Path.GetDirectoryName(testbenchPath)!, $"{circuit.Name}_{benchName}_results.json");
        File.WriteAllText(resultsPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

        var tracePath = Path.Combine(Path.GetDirectoryName(testbenchPath)!, $"{circuit.Name}_{benchName}_trace.jsonl");
        WriteTraceJsonl(tracePath, args with { BenchName = benchName }, circuit, testbenchPath, points, results);

        _logger.LogInformation("Bench '{BenchName}' testbench: {TestbenchPath}", benchName, testbenchPath);
        messages.Add($"Bench '{benchName}' testbench: {testbenchPath}");
        _logger.LogInformation("Bench '{BenchName}' trace: {TracePath}", benchName, tracePath);
        messages.Add($"Bench '{benchName}' trace: {tracePath}");
        _logger.LogInformation("Bench '{BenchName}' results: {ResultsPath}", benchName, resultsPath);
        messages.Add($"Bench '{benchName}' results: {resultsPath}");

        return true;
    }

    private static BenchResult CreateCombinedResults(string circuitName, IReadOnlyList<string> benchesToRun, Dictionary<string, MeasurementResult> measurements)
    {
        return new BenchResult
        {
            Circuit = circuitName,
            Bench = benchesToRun.Count == 1 ? benchesToRun[0] : "all",
            Measurements = measurements.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void WriteCombinedResults(string outputDir, string circuitName, BenchResult combinedResults, List<string> messages)
    {
        var combinedResultsPath = Path.Combine(outputDir, $"{circuitName}_results.json");
        File.WriteAllText(combinedResultsPath, JsonSerializer.Serialize(combinedResults, new JsonSerializerOptions { WriteIndented = true }));
        _logger.LogInformation("Combined results: {ResultsPath}", combinedResultsPath);
        messages.Add($"Combined results: {combinedResultsPath}");
    }

    private static IReadOnlyList<string>? ResolveBenchesToRun(string[] availableBenches, string? explicitBench)
    {
        if (!string.IsNullOrWhiteSpace(explicitBench))
        {
            var match = availableBenches.FirstOrDefault(b => b.Equals(explicitBench, StringComparison.OrdinalIgnoreCase));
            return match == null ? null : new[] { match };
        }

        return availableBenches;
    }

    private static void MergeMeasurements(Dictionary<string, MeasurementResult> target, IEnumerable<MeasurementResult> source)
    {
        foreach (var measurement in source)
        {
            var key = measurement.Node == null ? measurement.Metric : $"{measurement.Metric}@{measurement.Node}";
            target[key] = measurement;
        }
    }

    private static string[] GetAvailableBenchNames(Circuit circuit)
    {
        return circuit.Benches?.Benches.Select(b => b.Name)
                   .Where(b => !string.IsNullOrWhiteSpace(b))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
                   .ToArray()
               ?? Array.Empty<string>();
    }

    private static string? FindWorkspaceRoot(string inputPath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Cascode.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return null;
    }

    private static ACIRDocument ReadAcir(string acirPath)
    {
        ACIRReadResult readResult;
        using (var reader = File.OpenText(acirPath))
        {
            readResult = ACIRReader.TryRead(reader, acirPath);
        }

        if (!readResult.Success)
        {
            var first = readResult.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            throw new InvalidOperationException(first?.Message ?? "Failed to parse ACIR.");
        }

        return readResult.Document!;
    }

    private static string FindTestbenchPath(IReadOnlyList<string> testbenches, string circuitName, string benchName)
    {
        foreach (var path in testbenches)
        {
            var file = Path.GetFileNameWithoutExtension(path);
            var prefix = circuitName + "_";
            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileBench = file.Substring(prefix.Length);
            if (fileBench.Equals(benchName, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        throw new InvalidOperationException($"Testbench for '{benchName}' not emitted.");
    }

    private sealed record NgspiceRun(int ExitCode, string Stdout, string Stderr);

    private static NgspiceRun RunNgspice(string spiceFile)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ngspice",
            Arguments = $"-b \"{spiceFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(spiceFile) ?? Directory.GetCurrentDirectory()
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new NgspiceRun(process.ExitCode, stdout, stderr);
    }

    private sealed record TracePoint(int Index, Dictionary<string, double> AxisValues, List<MeasurementResult> Measurements);

    private static List<TracePoint> ParsePoints(string stdout, HashSet<string> sweepNames)
    {
        var points = new List<TracePoint>();
        var lines = stdout.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("CASCODE_POINT", StringComparison.Ordinal))
            {
                continue;
            }

            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var index = 0;
            var axes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var measurements = new List<MeasurementResult>();

            foreach (var token in tokens.Skip(1))
            {
                var equals = token.IndexOf('=');
                if (equals <= 0 || equals == token.Length - 1)
                {
                    continue;
                }

                var key = token.Substring(0, equals);
                var valueStr = token.Substring(equals + 1);
                if (key.Equals("point_index", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex))
                {
                    index = parsedIndex;
                    continue;
                }

                if (!double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
                {
                    continue;
                }

                var axisName = TryAxisName(key, sweepNames);
                if (axisName != null)
                {
                    axes[axisName] = parsedValue;
                    continue;
                }

                if (TryMeasurement(key, parsedValue, out var measurement))
                {
                    measurements.Add(measurement);
                }
            }

            points.Add(new TracePoint(index, axes, measurements));
        }

        return points.OrderBy(p => p.Index).ToList();
    }

    private static string? TryAxisName(string key, HashSet<string> sweepNames)
    {
        if (!key.EndsWith("_V", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = key[..^2];
        return sweepNames.Contains(name) ? name : null;
    }

    private static bool TryMeasurement(string key, double value, out MeasurementResult measurement)
    {
        measurement = new MeasurementResult();
        var underscore = key.LastIndexOf('_');
        if (underscore <= 0 || underscore == key.Length - 1)
        {
            return false;
        }

        var metric = key.Substring(0, underscore);
        var unit = key.Substring(underscore + 1);
        measurement = new MeasurementResult { Metric = metric, Value = value, Unit = unit };
        return true;
    }

    private static BenchResult ParseResults(string stdout, Circuit circuit, string benchName)
    {
        var results = new BenchResult { Circuit = circuit.Name, Bench = benchName };
        var nodeByMetric = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (circuit.Constraints?.Measure != null)
        {
            foreach (var group in circuit.Constraints.Measure
                         .Where(m => m.Bench.Equals(benchName, StringComparison.OrdinalIgnoreCase))
                         .GroupBy(m => m.Metric, StringComparer.OrdinalIgnoreCase))
            {
                var nodes = group.Select(x => x.Node).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                nodeByMetric[group.Key] = nodes.Count == 1 ? nodes[0] : null;
            }
        }

        foreach (var line in stdout.Split('\n'))
        {
            if (!TryParseResultLine(line, out var metric, out var value, out var unit))
            {
                continue;
            }

            var key = MakeMeasurementKey(results.Measurements, metric, nodeByMetric.TryGetValue(metric, out var node) ? node : null);
            results.Measurements[key] = new MeasurementResult
            {
                Metric = metric,
                Value = value,
                Unit = unit,
                Node = nodeByMetric.TryGetValue(metric, out var n) ? n : null
            };
        }

        return results;
    }

    private static bool TryParseResultLine(string line, out string metric, out double value, out string unit)
    {
        metric = string.Empty;
        unit = string.Empty;
        value = 0;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith("RESULT:", StringComparison.Ordinal))
        {
            return false;
        }

        var match = Regex.Match(trimmed,
            @"^RESULT:\s*(?<metric>[^=]+?)\s*=\s*(?<value>[-+]?(\d+(\.\d*)?|\.\d+)([eE][-+]?\d+)?)\s*(?<unit>\w+)?",
            RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return false;
        }

        metric = match.Groups["metric"].Value.Trim();
        unit = match.Groups["unit"].Value.Trim();
        return double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string MakeMeasurementKey(Dictionary<string, MeasurementResult> existing, string metric, string? node)
    {
        var baseKey = node == null ? metric : $"{metric}@{node}";
        baseKey = baseKey.Replace(' ', '_');
        if (!existing.ContainsKey(baseKey))
        {
            return baseKey;
        }

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseKey}#{i}";
            if (!existing.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    private static void WriteTraceJsonl(
        string tracePath,
        BenchRunArgs args,
        Circuit circuit,
        string testbenchPath,
        List<TracePoint> points,
        BenchResult results)
    {
        var runId = Guid.NewGuid().ToString("N");
        using var writer = new StreamWriter(tracePath);

        WriteJsonl(writer, new
        {
            schema = "cascode.sim.trace",
            version = 1,
            type = "meta",
            run_id = runId,
            ts_utc = DateTimeOffset.UtcNow,
            circuit = new { name = circuit.Name },
            bench = new { name = args.BenchName ?? string.Empty },
            backend = new { name = args.Backend.ToString().ToLowerInvariant() },
            testbench = new { path = testbenchPath }
        });

        if (circuit.Harness?.Sweeps != null && circuit.Harness.Sweeps.Count > 0)
        {
            WriteJsonl(writer, new
            {
                schema = "cascode.sim.trace",
                version = 1,
                type = "axes",
                run_id = runId,
                ts_utc = DateTimeOffset.UtcNow,
                axes = circuit.Harness.Sweeps.Select(s => new { name = s.Name, start = s.Start, stop = s.Stop, step = s.Step }).ToArray()
            });
        }

        foreach (var p in points)
        {
            WriteJsonl(writer, new
            {
                schema = "cascode.sim.trace",
                version = 1,
                type = "point",
                run_id = runId,
                ts_utc = DateTimeOffset.UtcNow,
                point = new { index = p.Index, axis_values = p.AxisValues },
                measurements = p.Measurements
            });
        }

        WriteJsonl(writer, new
        {
            schema = "cascode.sim.trace",
            version = 1,
            type = "summary",
            run_id = runId,
            ts_utc = DateTimeOffset.UtcNow,
            points = new { count = points.Count },
            results
        });
    }

    private static void WriteJsonl(StreamWriter writer, object record)
    {
        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false
        });
        writer.WriteLine(json);
    }
}
