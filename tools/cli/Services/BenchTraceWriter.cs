using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.Bench;
using Cascode.Language;

namespace Cascode.Cli.Services;

internal static class BenchTraceWriter
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static void WriteTraceJsonl(
        string tracePath,
        BenchRunService.BenchRunArgs args,
        Circuit circuit,
        string testbenchPath,
        List<BenchResultParser.TracePoint> points,
        BenchResult results
    )
    {
        var runId = Guid.NewGuid().ToString("N");
        using var writer = new StreamWriter(tracePath);

        WriteJsonl(
            writer,
            new
            {
                schema = "cascode.sim.trace",
                version = 1,
                type = "meta",
                run_id = runId,
                ts_utc = DateTimeOffset.UtcNow,
                circuit = new { name = circuit.Name },
                bench = new { name = args.BenchName ?? string.Empty },
                backend = new { name = args.Backend.ToString().ToLowerInvariant() },
                testbench = new { path = testbenchPath },
            }
        );

        if (circuit.Harness?.Sweeps != null && circuit.Harness.Sweeps.Count > 0)
        {
            WriteJsonl(
                writer,
                new
                {
                    schema = "cascode.sim.trace",
                    version = 1,
                    type = "axes",
                    run_id = runId,
                    ts_utc = DateTimeOffset.UtcNow,
                    axes = circuit
                        .Harness.Sweeps.Select(s => new
                        {
                            name = s.Name,
                            start = s.Start,
                            stop = s.Stop,
                            step = s.Step,
                        })
                        .ToArray(),
                }
            );
        }

        foreach (var p in points)
        {
            WriteJsonl(
                writer,
                new
                {
                    schema = "cascode.sim.trace",
                    version = 1,
                    type = "point",
                    run_id = runId,
                    ts_utc = DateTimeOffset.UtcNow,
                    point = new { index = p.Index, axis_values = p.AxisValues },
                    measurements = p.Measurements,
                }
            );
        }

        WriteJsonl(
            writer,
            new
            {
                schema = "cascode.sim.trace",
                version = 1,
                type = "summary",
                run_id = runId,
                ts_utc = DateTimeOffset.UtcNow,
                points = new { count = points.Count },
                results,
            }
        );
    }

    public static string WriteCombinedResults(
        string outputDir,
        string circuitName,
        BenchResult combinedResults
    )
    {
        var combinedResultsPath = Path.Combine(outputDir, $"{circuitName}_results.json");
        File.WriteAllText(
            combinedResultsPath,
            JsonSerializer.Serialize(combinedResults, IndentedOptions)
        );
        return combinedResultsPath;
    }

    private static void WriteJsonl(StreamWriter writer, object record)
    {
        var json = JsonSerializer.Serialize(record, CompactOptions);
        writer.WriteLine(json);
    }
}
