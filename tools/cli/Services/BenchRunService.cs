using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cascode.ACIR;
using Cascode.Bench;
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
        BenchBackendType Backend,
        bool Verbose);

    public sealed record BenchRunBenchSummary(
        string Name,
        bool Succeeded,
        int ExitCode,
        string? Error,
        string? Stderr,
        string? TestbenchPath,
        string? TracePath,
        string? ResultsPath);

    public sealed record BenchRunSummary(
        string CircuitName,
        BenchBackendType Backend,
        string OutputDir,
        IReadOnlyList<BenchRunBenchSummary> Benches,
        string? CombinedResultsPath,
        ComplianceReport Compliance);

    public sealed record BenchRunResult(
        int ExitCode,
        BenchRunSummary Summary);

    public static bool TryParseArgs(string[] args, out BenchRunArgs parsed, out string error)
    {
        parsed = new BenchRunArgs(string.Empty, null, null, BenchBackendType.Ngspice, false);
        error = string.Empty;

        string? acirPath = null;
        string? benchName = null;
        string? outputDir = null;
        var backend = BenchBackendType.Ngspice;
        var verbose = false;
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
            else if (args[i] == "--verbose" || args[i] == "-v")
            {
                verbose = true;
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

        parsed = new BenchRunArgs(
            Path.GetFullPath(acirPath),
            string.IsNullOrWhiteSpace(benchName) ? null : benchName,
            outputDir,
            backend,
            verbose);
        return true;
    }

    public BenchRunResult Run(string workspaceRoot, BenchRunArgs args)
    {
        var doc = BenchRunHelpers.ReadAcir(args.AcirPath);
        var circuit = BenchRunHelpers.GetSingleElCircuit(doc);

        var availableBenches = BenchRunHelpers.GetAvailableBenchNames(circuit);
        var benchesToRun = ResolveBenchesToRunOrError(args.BenchName, availableBenches, out var benchResolutionError);
        if (benchesToRun == null)
        {
            var summary = new BenchRunSummary(
                circuit.Name,
                args.Backend,
                BenchRunHelpers.ResolveOutputDir(args.OutputDir, circuit.Name, Array.Empty<string>()),
                benchResolutionError == null
                    ? Array.Empty<BenchRunBenchSummary>()
                    : new[]
                    {
                        new BenchRunBenchSummary(
                            Name: args.BenchName ?? string.Empty,
                            Succeeded: false,
                            ExitCode: 2,
                            Error: benchResolutionError,
                            Stderr: null,
                            TestbenchPath: null,
                            TracePath: null,
                            ResultsPath: null)
                    },
                CombinedResultsPath: null,
                Compliance: new ComplianceReport());
            return new BenchRunResult(2, summary);
        }

        var outputDir = BenchRunHelpers.ResolveOutputDir(args.OutputDir, circuit.Name, benchesToRun);
        Directory.CreateDirectory(outputDir);

        var resolvedWorkspaceRoot = BenchRunHelpers.ResolveWorkspaceRoot(args.AcirPath, workspaceRoot);
        var emit = SpiceEmitter.ValidateAndEmit(doc, outputDir, args.Backend, resolvedWorkspaceRoot);
        if (!emit.Validation.IsValid)
        {
            var first = emit.Validation.GetErrors().FirstOrDefault()?.ToString() ?? "Emission failed.";
            _logger.LogError("ACIR emission validation failed: {Error}", first);
            var summary = new BenchRunSummary(
                circuit.Name,
                args.Backend,
                outputDir,
                new[]
                {
                    new BenchRunBenchSummary(
                        Name: args.BenchName ?? string.Empty,
                        Succeeded: false,
                        ExitCode: 2,
                        Error: first,
                        Stderr: null,
                        TestbenchPath: null,
                        TracePath: null,
                        ResultsPath: null)
                },
                CombinedResultsPath: null,
                Compliance: new ComplianceReport());
            return new BenchRunResult(2, summary);
        }

        var sweepNames = BenchRunHelpers.GetSweepNames(circuit);
        var allMeasurements = new Dictionary<string, MeasurementResult>(StringComparer.OrdinalIgnoreCase);
        var benchSummaries = RunBenches(circuit, args, sweepNames, emit.Emit.TestbenchPaths, benchesToRun, allMeasurements);
        if (allMeasurements.Count == 0)
        {
            var summary = new BenchRunSummary(
                circuit.Name,
                args.Backend,
                outputDir,
                benchSummaries.Count == 0
                    ? new[]
                    {
                        new BenchRunBenchSummary(
                            Name: args.BenchName ?? string.Empty,
                            Succeeded: false,
                            ExitCode: 1,
                            Error: "No benches completed successfully.",
                            Stderr: null,
                            TestbenchPath: null,
                            TracePath: null,
                            ResultsPath: null)
                    }
                    : benchSummaries,
                CombinedResultsPath: null,
                Compliance: new ComplianceReport());
            return new BenchRunResult(1, summary);
        }

        var combinedResults = BenchResultParser.CreateCombinedResults(circuit.Name, benchesToRun, allMeasurements);
        string? combinedResultsPath = null;
        if (benchesToRun.Count > 1)
        {
            combinedResultsPath = BenchTraceWriter.WriteCombinedResults(outputDir, circuit.Name, combinedResults);
        }

        var report = ComplianceChecker.Check(circuit, combinedResults);
        var hadSimulationFailure = benchSummaries.Any(b => !b.Succeeded);
        var exit = hadSimulationFailure || report.FailedCount != 0 ? 1 : 0;

        return new BenchRunResult(
            exit,
            new BenchRunSummary(
                circuit.Name,
                args.Backend,
                outputDir,
                benchSummaries,
                combinedResultsPath,
                report));
    }

    private IReadOnlyList<string>? ResolveBenchesToRunOrError(string? explicitBench, string[] availableBenches, out string? error)
    {
        error = null;
        if (availableBenches.Length == 0)
        {
            const string msg = "No benches declared in ACIR benches block.";
            _logger.LogError(msg);
            error = msg;
            return null;
        }

        var benches = BenchRunHelpers.ResolveBenchesToRun(availableBenches, explicitBench);
        if (benches == null)
        {
            var list = string.Join(", ", availableBenches);
            var msg = $"Bench '{explicitBench}' not declared in ACIR benches block. Available: {list}";
            _logger.LogError("Bench '{BenchName}' not declared in ACIR benches block. Available: {Available}", explicitBench, list);
            error = msg;
            return null;
        }

        return benches;
    }

    private List<BenchRunBenchSummary> RunBenches(
        Circuit circuit,
        BenchRunArgs args,
        HashSet<string> sweepNames,
        IReadOnlyList<string> testbenchPaths,
        IReadOnlyList<string> benchesToRun,
        Dictionary<string, MeasurementResult> allMeasurements)
    {
        var summaries = new List<BenchRunBenchSummary>();

        foreach (var benchName in benchesToRun)
        {
            summaries.Add(TryRunBench(circuit, args, sweepNames, testbenchPaths, benchName, allMeasurements));
        }

        return summaries;
    }

    private BenchRunBenchSummary TryRunBench(
        Circuit circuit,
        BenchRunArgs args,
        HashSet<string> sweepNames,
        IReadOnlyList<string> testbenchPaths,
        string benchName,
        Dictionary<string, MeasurementResult> allMeasurements)
    {
        var testbenchPath = BenchRunHelpers.FindTestbenchPath(testbenchPaths, circuit.Name, benchName);

        NgspiceExecutor.NgspiceRun run;
        try
        {
            run = NgspiceExecutor.Run(testbenchPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run ngspice for '{BenchName}': {Message}", benchName, ex.Message);
            return new BenchRunBenchSummary(
                Name: benchName,
                Succeeded: false,
                ExitCode: 1,
                Error: $"Failed to run ngspice: {ex.Message}",
                Stderr: null,
                TestbenchPath: testbenchPath,
                TracePath: null,
                ResultsPath: null);
        }

        if (run.ExitCode != 0)
        {
            _logger.LogError("Simulation '{BenchName}' failed with exit code {ExitCode}. Stderr: {Stderr}", benchName, run.ExitCode, run.Stderr);
            return new BenchRunBenchSummary(
                Name: benchName,
                Succeeded: false,
                ExitCode: run.ExitCode,
                Error: $"Simulation failed (exit {run.ExitCode}).",
                Stderr: run.Stderr,
                TestbenchPath: testbenchPath,
                TracePath: null,
                ResultsPath: null);
        }

        var points = BenchResultParser.ParsePoints(run.Stdout, sweepNames);
        var results = BenchResultParser.ParseResults(run.Stdout, circuit, benchName);
        BenchResultParser.MergeMeasurements(allMeasurements, results.Measurements.Values);

        var resultsPath = Path.Combine(Path.GetDirectoryName(testbenchPath)!, $"{circuit.Name}_{benchName}_results.json");
        File.WriteAllText(resultsPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

        var tracePath = Path.Combine(Path.GetDirectoryName(testbenchPath)!, $"{circuit.Name}_{benchName}_trace.jsonl");
        BenchTraceWriter.WriteTraceJsonl(tracePath, args with { BenchName = benchName }, circuit, testbenchPath, points, results);

        return new BenchRunBenchSummary(
            Name: benchName,
            Succeeded: true,
            ExitCode: 0,
            Error: null,
            Stderr: null,
            TestbenchPath: testbenchPath,
            TracePath: tracePath,
            ResultsPath: resultsPath);
    }
}
