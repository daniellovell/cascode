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

namespace Cascode.Cli.Services;

public static class BenchRunService
{
    public sealed record BenchRunArgs(
        string AcirPath,
        string BenchName,
        string OutputDir,
        BenchBackendType Backend);

    public sealed record BenchRunResult(
        int ExitCode,
        IReadOnlyList<string> Messages);

    public static bool TryParseArgs(string[] args, out BenchRunArgs parsed, out string error)
    {
        parsed = new BenchRunArgs(string.Empty, string.Empty, Path.Combine(Directory.GetCurrentDirectory(), "build"), BenchBackendType.Ngspice);
        error = string.Empty;

        string? acirPath = null;
        string? benchName = null;
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "build");
        var backend = BenchBackendType.Ngspice;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--acir" && i + 1 < args.Length)
            {
                acirPath = args[++i];
            }
            else if (args[i] == "--bench" && i + 1 < args.Length)
            {
                benchName = args[++i];
            }
            else if (args[i] == "--out" && i + 1 < args.Length)
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
        }

        if (string.IsNullOrWhiteSpace(acirPath) || string.IsNullOrWhiteSpace(benchName))
        {
            error = "Error: --acir and --bench are required.";
            return false;
        }

        if (!File.Exists(acirPath))
        {
            error = $"Error: ACIR file '{acirPath}' not found.";
            return false;
        }

        parsed = new BenchRunArgs(Path.GetFullPath(acirPath), benchName, outputDir, backend);
        return true;
    }

    public static BenchRunResult Run(string workspaceRoot, BenchRunArgs args)
    {
        var messages = new List<string>();
        var outputDir = Path.GetFullPath(args.OutputDir);
        Directory.CreateDirectory(outputDir);

        var doc = ReadAcir(args.AcirPath);
        var circuit = doc.Circuits.FirstOrDefault(c => c.Level == ACIRLevel.EL)
            ?? throw new InvalidOperationException("No EL-level circuits found in ACIR document.");

        var resolvedWorkspaceRoot = FindWorkspaceRoot(args.AcirPath) ?? workspaceRoot;
        if (string.IsNullOrWhiteSpace(resolvedWorkspaceRoot))
        {
            resolvedWorkspaceRoot = Directory.GetCurrentDirectory();
        }

        var emit = SpiceEmitter.ValidateAndEmit(doc, outputDir, args.Backend, resolvedWorkspaceRoot);
        if (!emit.Validation.IsValid)
        {
            var first = emit.Validation.GetErrors().FirstOrDefault()?.ToString() ?? "Emission failed.";
            return new BenchRunResult(2, new[] { first });
        }

        var testbenchPath = FindTestbenchPath(emit.Emit.TestbenchPaths, circuit.Name, args.BenchName);
        NgspiceRun run;
        try
        {
            run = RunNgspice(testbenchPath);
        }
        catch (Exception ex)
        {
            messages.Add($"Failed to run ngspice: {ex.Message}");
            return new BenchRunResult(1, messages);
        }
        if (run.ExitCode != 0)
        {
            messages.Add($"Simulation failed (exit {run.ExitCode}).");
            messages.Add(run.Stderr);
            return new BenchRunResult(1, messages);
        }

        var sweepNames = circuit.Harness?.Sweeps?.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var points = ParsePoints(run.Stdout, sweepNames);
        var results = ParseResults(run.Stdout, circuit, args.BenchName);

        var resultsPath = Path.Combine(Path.GetDirectoryName(testbenchPath)!, $"{circuit.Name}_{args.BenchName}_results.json");
        File.WriteAllText(resultsPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

        var tracePath = Path.Combine(Path.GetDirectoryName(testbenchPath)!, $"{circuit.Name}_{args.BenchName}_trace.jsonl");
        WriteTraceJsonl(tracePath, args, circuit, testbenchPath, points, results);

        var report = ComplianceChecker.Check(circuit, results);
        messages.Add($"Testbench: {testbenchPath}");
        messages.Add($"Trace: {tracePath}");
        messages.Add($"Results: {resultsPath}");
        messages.Add($"Compliance: {report.PassedCount}/{report.TotalCount} constraints satisfied");

        return new BenchRunResult(report.FailedCount == 0 ? 0 : 1, messages);
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
            bench = new { name = args.BenchName },
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
