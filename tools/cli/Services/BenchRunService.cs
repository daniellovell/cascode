using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.Bench;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Services;

public class BenchRunService
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly ILogger<BenchRunService> _logger;
    private readonly Action<string>? _progress;

    public BenchRunService(ILogger<BenchRunService> logger, Action<string>? progress = null)
    {
        _logger = logger;
        _progress = progress;
    }

    public sealed record BenchRunArgs(
        string CascodePath,
        string? BenchName,
        string? OutputDir,
        BenchBackendType Backend,
        bool Verbose,
        bool StrictCompliance,
        string? CircuitFilter = null
    );

    public sealed record BenchRunBenchSummary(
        string Name,
        bool Succeeded,
        int ExitCode,
        string? Error,
        string? Stderr,
        string? TestbenchPath,
        string? TracePath,
        string? ResultsPath
    );

    public sealed record BenchRunSummary(
        string CircuitName,
        BenchBackendType Backend,
        string OutputDir,
        IReadOnlyList<BenchRunBenchSummary> Benches,
        string? CombinedResultsPath,
        ComplianceReport Compliance
    );

    public sealed record BenchRunResult(int ExitCode, BenchRunSummary Summary);

    /// <summary>
    /// Summary for a single circuit's bench run within a multi-circuit execution.
    /// </summary>
    public sealed record CircuitBenchRunSummary(
        string CircuitName,
        IReadOnlyList<BenchRunBenchSummary> Benches,
        ComplianceReport Compliance
    );

    /// <summary>
    /// Aggregated summary across all circuits with benches.
    /// </summary>
    public sealed record MultiCircuitBenchRunSummary(
        BenchBackendType Backend,
        string OutputDir,
        IReadOnlyList<CircuitBenchRunSummary> CircuitSummaries,
        string? GlobalResultsPath,
        ComplianceReport GlobalCompliance,
        BenchRunTimingReport? Timing = null
    )
    {
        public int TotalBenchesRun => CircuitSummaries.Sum(c => c.Benches.Count);
        public int TotalBenchesSucceeded =>
            CircuitSummaries.Sum(c => c.Benches.Count(b => b.Succeeded));
        public int TotalBenchesFailed =>
            CircuitSummaries.Sum(c => c.Benches.Count(b => !b.Succeeded));
    }

    /// <summary>
    /// Result of running benches across all circuits.
    /// </summary>
    public sealed record MultiCircuitBenchRunResult(
        int ExitCode,
        MultiCircuitBenchRunSummary Summary
    );

    private static string BuildPlanKey(string circuitName, string benchName) =>
        $"{circuitName}:{benchName}";

    private void Progress(string message)
    {
        _progress?.Invoke(message);
    }

    public static bool TryParseArgs(string[] args, out BenchRunArgs parsed, out string error)
    {
        parsed = new BenchRunArgs(
            string.Empty,
            null,
            null,
            BenchBackendType.Ngspice,
            false,
            StrictCompliance: false
        );
        error = string.Empty;

        string? cascodePath = null;
        string? benchName = null;
        string? outputDir = null;
        var backend = BenchBackendType.Ngspice;
        var verbose = false;
        var strict = false;
        string? circuitFilter = null;
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--cascode" && i + 1 < args.Length)
            {
                cascodePath = args[++i];
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
            else if (args[i] == "--strict")
            {
                strict = true;
            }
            else if ((args[i] == "--circuit" || args[i] == "-c") && i + 1 < args.Length)
            {
                circuitFilter = args[++i];
            }
            else if (args[i] == "--backend" && i + 1 < args.Length)
            {
                var backendStr = args[++i].ToLowerInvariant();
                if (backendStr != "ngspice")
                {
                    error =
                        $"Error: unsupported backend '{backendStr}'. Only 'ngspice' is supported currently.";
                    return false;
                }
                backend = BenchBackendType.Ngspice;
            }
            else if (args[i].StartsWith('-'))
            {
                error = $"Error: unknown option '{args[i]}'.";
                return false;
            }
            else
            {
                positionals.Add(args[i]);
            }
        }

        if (string.IsNullOrWhiteSpace(cascodePath) && positionals.Count >= 1)
        {
            cascodePath = positionals[0];
        }

        if (string.IsNullOrWhiteSpace(benchName) && positionals.Count >= 2)
        {
            benchName = positionals[1];
        }

        if (string.IsNullOrWhiteSpace(cascodePath))
        {
            error = "Error: Cascode file path is required.";
            return false;
        }

        if (!File.Exists(cascodePath))
        {
            error = $"Error: Cascode file '{cascodePath}' not found.";
            return false;
        }

        parsed = new BenchRunArgs(
            Path.GetFullPath(cascodePath),
            string.IsNullOrWhiteSpace(benchName) ? null : benchName,
            outputDir,
            backend,
            verbose,
            StrictCompliance: strict,
            string.IsNullOrWhiteSpace(circuitFilter) ? null : circuitFilter
        );
        return true;
    }

    public BenchRunResult Run(string workspaceRoot, string? pdkRoot, BenchRunArgs args)
    {
        var loaded = CascodeLoadLinkService.LoadAndLinkIfNeeded(
            args.CascodePath,
            workspaceRoot,
            args.OutputDir,
            _logger
        );
        args = args with { CascodePath = loaded.ResolvedPath };
        var doc = loaded.Document;
        var circuit = BenchRunHelpers.GetSingleElCircuit(doc);

        var availableBenches = BenchRunHelpers.GetAvailableBenchNames(doc, circuit);
        var benchesToRun = ResolveBenchesToRunOrError(
            args.BenchName,
            availableBenches,
            out var benchResolutionError
        );
        if (benchesToRun == null)
        {
            var summary = new BenchRunSummary(
                circuit.Name,
                args.Backend,
                BenchRunHelpers.ResolveOutputDir(
                    args.OutputDir,
                    circuit.Name,
                    Array.Empty<string>()
                ),
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
                            ResultsPath: null
                        ),
                    },
                CombinedResultsPath: null,
                Compliance: new ComplianceReport()
            );
            return new BenchRunResult(2, summary);
        }

        var outputDir = BenchRunHelpers.ResolveOutputDir(
            args.OutputDir,
            circuit.Name,
            benchesToRun
        );
        Directory.CreateDirectory(outputDir);

        var resolvedWorkspaceRoot = loaded.WorkspaceRoot;
        var emit = CascodeEmitPipeline.ValidateAndEmit(
            doc,
            outputDir,
            args.Backend,
            resolvedWorkspaceRoot,
            pdkRoot,
            _logger
        );
        var validationResult = ValidateEmissionOrReturnResult(emit, circuit.Name, args, outputDir);
        if (validationResult != null)
        {
            return validationResult;
        }

        var allMeasurements = new Dictionary<string, MeasurementResult>(
            StringComparer.OrdinalIgnoreCase
        );
        var planMap = BuildPlanMap(doc);
        var benchSummaries = RunBenches(
            doc,
            circuit,
            args,
            emit.Emit.TestbenchPaths,
            benchesToRun,
            allMeasurements,
            planMap
        );
        var noMeasurementsResult = HandleNoMeasurementsOrReturnResult(
            circuit.Name,
            args,
            outputDir,
            benchSummaries,
            allMeasurements
        );
        if (noMeasurementsResult != null)
        {
            return noMeasurementsResult;
        }

        var combinedResults = BenchResultParser.CreateCombinedResults(
            circuit.Name,
            benchesToRun,
            allMeasurements
        );
        string? combinedResultsPath = null;
        if (benchesToRun.Count > 1)
        {
            combinedResultsPath = BenchTraceWriter.WriteCombinedResults(
                outputDir,
                circuit.Name,
                combinedResults
            );
        }

        var report = ComplianceChecker.Check(circuit, combinedResults);
        var hadSimulationFailure = benchSummaries.Any(b => !b.Succeeded);
        var exit =
            hadSimulationFailure || (args.StrictCompliance && report.FailedCount != 0) ? 1 : 0;

        return new BenchRunResult(
            exit,
            new BenchRunSummary(
                circuit.Name,
                args.Backend,
                outputDir,
                benchSummaries,
                combinedResultsPath,
                report
            )
        );
    }

    /// <summary>
    /// Runs benches for all EL circuits with benches, in dependency order (leaves first).
    /// Optionally filtered to a single circuit via CircuitFilter.
    /// </summary>
    public MultiCircuitBenchRunResult RunAll(
        string workspaceRoot,
        string? pdkRoot,
        BenchRunArgs args
    )
    {
        var timing = new BenchRunTimingCollector();

        Progress("load+link: start");
        var loadStep = timing.Step("load+link");
        var loaded = CascodeLoadLinkService.LoadAndLinkIfNeeded(
            args.CascodePath,
            workspaceRoot,
            args.OutputDir,
            _logger
        );
        loadStep.Stop();
        Progress(
            $"load+link: done ({loadStep.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s)"
        );
        args = args with { CascodePath = loaded.ResolvedPath };
        var doc = loaded.Document;
        var allCircuitsWithBenches = BenchRunHelpers.GetElCircuitsWithBenches(doc);

        // Early exit: no circuits with benches
        if (allCircuitsWithBenches.Count == 0)
        {
            _logger.LogError("No EL-level circuits with benches found in Cascode document.");
            var timingReport = timing.Build();
            return new MultiCircuitBenchRunResult(
                2,
                new MultiCircuitBenchRunSummary(
                    args.Backend,
                    args.OutputDir ?? string.Empty,
                    Array.Empty<CircuitBenchRunSummary>(),
                    null,
                    new ComplianceReport(),
                    Timing: timingReport
                )
            );
        }

        // Validate and filter circuits
        var filterResult = ValidateCircuitFilterOrReturnError(
            allCircuitsWithBenches,
            args.CircuitFilter,
            args.Backend,
            args.OutputDir ?? string.Empty,
            out var circuitsWithBenches
        );
        if (filterResult != null)
        {
            var timingReport = timing.Build();
            return filterResult with
            {
                Summary = filterResult.Summary with { Timing = timingReport },
            };
        }

        // Determine output directory
        var outputDir =
            args.OutputDir
            ?? Path.Combine(
                Directory.GetCurrentDirectory(),
                "build",
                "bench",
                circuitsWithBenches.Count == 1 ? circuitsWithBenches[0].Name : "multi"
            );
        Directory.CreateDirectory(outputDir);

        // Emit all testbenches upfront
        Progress("emit: start");
        var emitStep = timing.Step("emit");
        var emitResult = EmitAllDesignsOrReturnError(
            doc,
            outputDir,
            workspaceRoot,
            pdkRoot,
            args,
            out var emit
        );
        emitStep.Stop();
        Progress(
            $"emit: done ({emitStep.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s)"
        );
        if (emitResult != null)
        {
            var timingReport = timing.Build();
            return emitResult with { Summary = emitResult.Summary with { Timing = timingReport } };
        }

        // Run benches for each circuit in dependency order
        Progress("bench-plan: compile start");
        var planStep = timing.Step("bench-plan: compile");
        var planMap = BuildPlanMap(doc);
        planStep.Stop();
        Progress(
            $"bench-plan: compile done ({planStep.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s)"
        );
        var circuitSummaries = new List<CircuitBenchRunSummary>();
        foreach (var circuit in circuitsWithBenches)
        {
            var circuitSummary = RunCircuitBenches(
                circuit,
                doc,
                args,
                outputDir,
                emit.Emit.TestbenchPaths,
                planMap,
                timing
            );
            circuitSummaries.Add(circuitSummary);
        }

        // Aggregate and return results
        var final = AggregateResults(
            circuitSummaries,
            args.Backend,
            outputDir,
            args.StrictCompliance,
            timing.Build()
        );
        Progress("bench: done");
        return final;
    }

    private CircuitBenchRunSummary RunCircuitBenches(
        Circuit circuit,
        CascodeDocument doc,
        BenchRunArgs args,
        string outputDir,
        IReadOnlyList<string> testbenchPaths,
        IReadOnlyDictionary<string, BenchPlan> planMap,
        BenchRunTimingCollector timing
    )
    {
        var availableBenches = BenchRunHelpers.GetAvailableBenchNames(doc, circuit);
        var benchesToRun = ResolveBenchesToRunForCircuit(
            args.BenchName,
            availableBenches,
            circuit.Name
        );
        var circuitMeasurements = new Dictionary<string, MeasurementResult>(
            StringComparer.OrdinalIgnoreCase
        );
        var benchSummaries = new List<BenchRunBenchSummary>();

        var benchCount = benchesToRun.Count;
        var benchIndex = 0;
        foreach (var benchName in benchesToRun)
        {
            benchIndex++;
            Progress($"bench: run {circuit.Name}/{benchName} ({benchIndex}/{benchCount})");
            var summary = TryRunBench(
                doc,
                circuit,
                args,
                testbenchPaths,
                benchName,
                circuitMeasurements,
                planMap,
                timing
            );
            benchSummaries.Add(summary);
        }

        var combinedResults = BenchResultParser.CreateCombinedResults(
            circuit.Name,
            benchesToRun,
            circuitMeasurements
        );

        // Write combined results file (for verify command compatibility)
        if (benchesToRun.Count > 0 && circuitMeasurements.Count > 0)
        {
            BenchTraceWriter.WriteCombinedResults(outputDir, circuit.Name, combinedResults);
        }

        var compliance = ComplianceChecker.Check(circuit, combinedResults);

        return new CircuitBenchRunSummary(circuit.Name, benchSummaries, compliance);
    }

    /// <summary>
    /// Resolves benches to run for a circuit, considering the explicit bench filter.
    /// Supports both "BenchName" and "CircuitName:BenchName" formats.
    /// </summary>
    private IReadOnlyList<string> ResolveBenchesToRunForCircuit(
        string? explicitBench,
        string[] availableBenches,
        string circuitName
    )
    {
        if (string.IsNullOrWhiteSpace(explicitBench))
        {
            return availableBenches;
        }

        // Check for circuit-qualified format: "CircuitName:BenchName"
        if (explicitBench.Contains(':'))
        {
            var parts = explicitBench.Split(':', 2);
            var targetCircuit = parts[0];
            var targetBench = parts[1];

            // If this isn't the target circuit, skip all benches
            if (!targetCircuit.Equals(circuitName, StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }

            // Find matching bench in this circuit
            var match = availableBenches.FirstOrDefault(b =>
                b.Equals(targetBench, StringComparison.OrdinalIgnoreCase)
            );
            return match != null ? new[] { match } : Array.Empty<string>();
        }

        // Unqualified bench name: run on all circuits that have it
        var benchMatch = availableBenches.FirstOrDefault(b =>
            b.Equals(explicitBench, StringComparison.OrdinalIgnoreCase)
        );
        return benchMatch != null ? new[] { benchMatch } : Array.Empty<string>();
    }

    private ComplianceReport AggregateCompliance(
        IReadOnlyList<CircuitBenchRunSummary> circuitSummaries
    )
    {
        var allResults = new List<ConstraintResult>();
        var uncheckedByBench = new Dictionary<string, List<UncheckedConstraint>>();

        foreach (var summary in circuitSummaries)
        {
            var circuitCompliance = summary.Compliance;

            // Prefix constraint IDs with circuit name to avoid collisions
            foreach (var result in circuitCompliance.Results)
            {
                allResults.Add(
                    new ConstraintResult
                    {
                        Id = $"{summary.CircuitName}.{result.Id}",
                        Metric = result.Metric,
                        Node = result.Node,
                        Unit = result.Unit,
                        Operator = result.Operator,
                        ExpectedRaw = result.ExpectedRaw,
                        Expected = result.Expected,
                        Actual = result.Actual,
                        ActualUnit = result.ActualUnit,
                        Passed = result.Passed,
                        Message = result.Message,
                    }
                );
            }

            foreach (var (bench, unchecked_) in circuitCompliance.UncheckedByBench)
            {
                var key = $"{summary.CircuitName}.{bench}";
                uncheckedByBench[key] = unchecked_;
            }
        }

        return new ComplianceReport { Results = allResults, UncheckedByBench = uncheckedByBench };
    }

    /// <summary>
    /// Validates and filters circuits based on CircuitFilter argument.
    /// Returns null if validation passes; otherwise returns error result.
    /// </summary>
    private MultiCircuitBenchRunResult? ValidateCircuitFilterOrReturnError(
        IReadOnlyList<Circuit> allCircuitsWithBenches,
        string? circuitFilter,
        BenchBackendType backend,
        string outputDir,
        out IReadOnlyList<Circuit> filteredCircuits
    )
    {
        filteredCircuits = allCircuitsWithBenches;

        if (string.IsNullOrWhiteSpace(circuitFilter))
        {
            return null;
        }

        var filtered = allCircuitsWithBenches
            .Where(c => c.Name.Equals(circuitFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0)
        {
            var available = string.Join(", ", allCircuitsWithBenches.Select(c => c.Name));
            _logger.LogError(
                "Circuit '{CircuitFilter}' not found or has no benches. Available: {Available}",
                circuitFilter,
                available
            );
            return new MultiCircuitBenchRunResult(
                2,
                new MultiCircuitBenchRunSummary(
                    backend,
                    outputDir,
                    Array.Empty<CircuitBenchRunSummary>(),
                    null,
                    new ComplianceReport()
                )
            );
        }

        filteredCircuits = filtered;
        return null;
    }

    /// <summary>
    /// Emits all testbenches upfront (handles dependencies).
    /// Returns null if emission succeeds; otherwise returns error result.
    /// </summary>
    private MultiCircuitBenchRunResult? EmitAllDesignsOrReturnError(
        CascodeDocument doc,
        string outputDir,
        string workspaceRoot,
        string? pdkRoot,
        BenchRunArgs args,
        out ValidatedEmitResult emit
    )
    {
        emit = null!;

        var resolvedWorkspaceRoot = BenchRunHelpers.ResolveWorkspaceRoot(
            args.CascodePath,
            workspaceRoot
        );
        emit = CascodeEmitPipeline.ValidateAndEmit(
            doc,
            outputDir,
            args.Backend,
            resolvedWorkspaceRoot,
            pdkRoot,
            _logger
        );

        if (!emit.Validation.IsValid)
        {
            var first =
                emit.Validation.GetErrors().FirstOrDefault()?.ToString() ?? "Emission failed.";
            _logger.LogError("Cascode emission validation failed: {Error}", first);
            return new MultiCircuitBenchRunResult(
                2,
                new MultiCircuitBenchRunSummary(
                    args.Backend,
                    outputDir,
                    Array.Empty<CircuitBenchRunSummary>(),
                    null,
                    new ComplianceReport()
                )
            );
        }

        return null;
    }

    /// <summary>
    /// Aggregates results from all circuit bench runs into final summary.
    /// Always succeeds; returns complete MultiCircuitBenchRunResult.
    /// </summary>
    private MultiCircuitBenchRunResult AggregateResults(
        IReadOnlyList<CircuitBenchRunSummary> circuitSummaries,
        BenchBackendType backend,
        string outputDir,
        bool strictCompliance,
        BenchRunTimingReport timing
    )
    {
        var globalCompliance = AggregateCompliance(circuitSummaries);
        var hadSimulationFailure = circuitSummaries.Any(cs => cs.Benches.Any(b => !b.Succeeded));
        var exitCode =
            hadSimulationFailure || (strictCompliance && globalCompliance.FailedCount > 0) ? 1 : 0;

        return new MultiCircuitBenchRunResult(
            exitCode,
            new MultiCircuitBenchRunSummary(
                backend,
                outputDir,
                circuitSummaries,
                null,
                globalCompliance,
                Timing: timing
            )
        );
    }

    private BenchRunResult? ValidateEmissionOrReturnResult(
        ValidatedEmitResult emit,
        string circuitName,
        BenchRunArgs args,
        string outputDir
    )
    {
        if (!emit.Validation.IsValid)
        {
            var first =
                emit.Validation.GetErrors().FirstOrDefault()?.ToString() ?? "Emission failed.";
            _logger.LogError("Cascode emission validation failed: {Error}", first);
            var summary = new BenchRunSummary(
                circuitName,
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
                        ResultsPath: null
                    ),
                },
                CombinedResultsPath: null,
                Compliance: new ComplianceReport()
            );
            return new BenchRunResult(2, summary);
        }

        return null;
    }

    private static BenchRunResult? HandleNoMeasurementsOrReturnResult(
        string circuitName,
        BenchRunArgs args,
        string outputDir,
        List<BenchRunBenchSummary> benchSummaries,
        Dictionary<string, MeasurementResult> allMeasurements
    )
    {
        if (allMeasurements.Count == 0)
        {
            var summary = new BenchRunSummary(
                circuitName,
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
                            ResultsPath: null
                        ),
                    }
                    : benchSummaries,
                CombinedResultsPath: null,
                Compliance: new ComplianceReport()
            );
            return new BenchRunResult(1, summary);
        }

        return null;
    }

    private IReadOnlyList<string>? ResolveBenchesToRunOrError(
        string? explicitBench,
        string[] availableBenches,
        out string? error
    )
    {
        error = null;
        if (availableBenches.Length == 0)
        {
            const string msg =
                "No benches declared for the circuit interfaces in the Cascode document.";
            _logger.LogError(msg);
            error = msg;
            return null;
        }

        var benches = BenchRunHelpers.ResolveBenchesToRun(availableBenches, explicitBench);
        if (benches == null)
        {
            var list = string.Join(", ", availableBenches);
            var msg =
                $"Bench '{explicitBench}' not declared in Cascode bench definitions. Available: {list}";
            _logger.LogError(
                "Bench '{BenchName}' not declared in Cascode bench definitions. Available: {Available}",
                explicitBench,
                list
            );
            error = msg;
            return null;
        }

        return benches;
    }

    private List<BenchRunBenchSummary> RunBenches(
        CascodeDocument doc,
        Circuit circuit,
        BenchRunArgs args,
        IReadOnlyList<string> testbenchPaths,
        IReadOnlyList<string> benchesToRun,
        Dictionary<string, MeasurementResult> allMeasurements,
        IReadOnlyDictionary<string, BenchPlan> planMap
    )
    {
        var timing = new BenchRunTimingCollector();
        var summaries = new List<BenchRunBenchSummary>();

        foreach (var benchName in benchesToRun)
        {
            summaries.Add(
                TryRunBench(
                    doc,
                    circuit,
                    args,
                    testbenchPaths,
                    benchName,
                    allMeasurements,
                    planMap,
                    timing
                )
            );
        }

        return summaries;
    }

    private BenchRunBenchSummary TryRunBench(
        CascodeDocument doc,
        Circuit circuit,
        BenchRunArgs args,
        IReadOnlyList<string> testbenchPaths,
        string benchName,
        Dictionary<string, MeasurementResult> allMeasurements,
        IReadOnlyDictionary<string, BenchPlan> planMap,
        BenchRunTimingCollector timing
    )
    {
        var testbenchPath = BenchRunHelpers.FindTestbenchPath(
            testbenchPaths,
            circuit.Name,
            benchName
        );

        var simTime = TimeSpan.Zero;
        var parseTime = TimeSpan.Zero;
        var evalTime = TimeSpan.Zero;
        var writeTime = TimeSpan.Zero;

        NgspiceExecutor.NgspiceRun run;
        var swSim = Stopwatch.StartNew();
        try
        {
            run = NgspiceExecutor.Run(testbenchPath);
        }
        catch (Exception ex)
        {
            simTime = swSim.Elapsed;
            _logger.LogError(
                ex,
                "Failed to run ngspice for '{BenchName}': {Message}",
                benchName,
                ex.Message
            );
            Progress($"bench: FAIL {circuit.Name}/{benchName} (ngspice launch)");
            timing.AddBench(
                new BenchBenchTiming(
                    circuit.Name,
                    benchName,
                    Simulation: simTime,
                    ParseOutputs: parseTime,
                    EvaluateMeasurements: evalTime,
                    WriteArtifacts: writeTime
                )
            );
            return new BenchRunBenchSummary(
                Name: benchName,
                Succeeded: false,
                ExitCode: 1,
                Error: $"Failed to run ngspice: {ex.Message}",
                Stderr: null,
                TestbenchPath: testbenchPath,
                TracePath: null,
                ResultsPath: null
            );
        }
        finally
        {
            swSim.Stop();
            simTime = swSim.Elapsed;
        }

        if (run.ExitCode != 0)
        {
            _logger.LogError(
                "Simulation '{BenchName}' failed with exit code {ExitCode}. Stderr: {Stderr}",
                benchName,
                run.ExitCode,
                run.Stderr
            );
            Progress(
                $"bench: FAIL {circuit.Name}/{benchName} (exit {run.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)})"
            );
            timing.AddBench(
                new BenchBenchTiming(
                    circuit.Name,
                    benchName,
                    Simulation: simTime,
                    ParseOutputs: parseTime,
                    EvaluateMeasurements: evalTime,
                    WriteArtifacts: writeTime
                )
            );
            return new BenchRunBenchSummary(
                Name: benchName,
                Succeeded: false,
                ExitCode: run.ExitCode,
                Error: $"Simulation failed (exit {run.ExitCode}).",
                Stderr: run.Stderr,
                TestbenchPath: testbenchPath,
                TracePath: null,
                ResultsPath: null
            );
        }

        BenchResult results;
        var swParse = Stopwatch.StartNew();
        try
        {
            var planKey = BuildPlanKey(circuit.Name, benchName);
            if (!planMap.TryGetValue(planKey, out var plan))
            {
                throw new InvalidOperationException(
                    $"Missing compiled bench plan for '{circuit.Name}:{benchName}'."
                );
            }

            var analyses = new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            );

            var hasDc = plan.Analyses.Any(a => a.Type == BenchValueType.DCAnalysis);
            IReadOnlyDictionary<string, double>? opNodeVoltagesByKey = null;
            if (hasDc)
            {
                var nodesWrdataPath = BenchRuntimePaths.GetOpNodesWrdataPath(
                    Path.GetDirectoryName(testbenchPath)!,
                    plan.CircuitName,
                    plan.BindingName
                );
                opNodeVoltagesByKey = NgspiceWrdataOpParser.ParseNodeVoltages(
                    nodesWrdataPath,
                    plan.AcNodeKeys
                );
            }

            foreach (var a in plan.Analyses)
            {
                if (a.Type == BenchValueType.DCAnalysis)
                {
                    if (opNodeVoltagesByKey is null)
                    {
                        throw new InvalidOperationException(
                            $"DCAnalysis '{a.Name}' requires op node voltages, but none were parsed."
                        );
                    }

                    var f = new[] { 0.0 };
                    var nodes = new Dictionary<string, System.Numerics.Complex[]>(
                        StringComparer.OrdinalIgnoreCase
                    );
                    foreach (var (key, v) in opNodeVoltagesByKey)
                    {
                        nodes[key] = new[] { new System.Numerics.Complex(v, 0) };
                    }

                    analyses[a.Name] = new BenchMeasurementRunner.AnalysisContext(
                        a.Name,
                        StartHz: 0,
                        StopHz: 0,
                        StartS: 0,
                        StopS: 0,
                        Ac: new AcDataset(f, nodes)
                    );
                    continue;
                }

                if (a.Type == BenchValueType.ACAnalysis)
                {
                    var wrdataPath = BenchRuntimePaths.GetAcWrdataPath(
                        Path.GetDirectoryName(testbenchPath)!,
                        plan.CircuitName,
                        plan.BindingName,
                        a.Name
                    );

                    var ac = NgspiceWrdataAcParser.Parse(wrdataPath, plan.AcNodeKeys);

                    AcDataset? acCurrents = null;
                    if (plan.RequiresCurrents)
                    {
                        var currentSources = plan
                            .HarnessElements.Where(e =>
                                e.Type.Equals("VDC", StringComparison.OrdinalIgnoreCase)
                                || e.Type.Equals("VAC", StringComparison.OrdinalIgnoreCase)
                                || e.Type.Equals("VSIN", StringComparison.OrdinalIgnoreCase)
                            )
                            .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (currentSources.Count > 0)
                        {
                            var iWrdataPath = BenchRuntimePaths.GetAcCurrentsWrdataPath(
                                Path.GetDirectoryName(testbenchPath)!,
                                plan.CircuitName,
                                plan.BindingName,
                                a.Name
                            );
                            var sourceNames = currentSources.Select(s => "V" + s.Id).ToList();
                            acCurrents = NgspiceWrdataAcParser.Parse(iWrdataPath, sourceNames);
                        }
                    }

                    analyses[a.Name] = new BenchMeasurementRunner.AnalysisContext(
                        a.Name,
                        a.StartHz,
                        a.StopHz,
                        StartS: 0,
                        StopS: 0,
                        ac,
                        AcCurrents: acCurrents
                    );
                }
                else if (a.Type == BenchValueType.NoiseAnalysis)
                {
                    var wrdataPath = BenchRuntimePaths.GetNoiseWrdataPath(
                        Path.GetDirectoryName(testbenchPath)!,
                        plan.CircuitName,
                        plan.BindingName,
                        a.Name
                    );

                    var noise = NgspiceWrdataNoiseParser.Parse(wrdataPath);
                    analyses[a.Name] = new BenchMeasurementRunner.AnalysisContext(
                        a.Name,
                        a.StartHz,
                        a.StopHz,
                        StartS: 0,
                        StopS: 0,
                        Ac: null,
                        Noise: noise
                    );
                }
                else if (a.Type == BenchValueType.TranAnalysis)
                {
                    var wrdataPath = BenchRuntimePaths.GetTranWrdataPath(
                        Path.GetDirectoryName(testbenchPath)!,
                        plan.CircuitName,
                        plan.BindingName,
                        a.Name
                    );

                    var nodes = NgspiceWrdataTranParser.Parse(wrdataPath, plan.AcNodeKeys);

                    TranDataset? currents = null;
                    var currentSources = plan
                        .HarnessElements.Where(e =>
                            e.Type.Equals("VDC", StringComparison.OrdinalIgnoreCase)
                            || e.Type.Equals("VAC", StringComparison.OrdinalIgnoreCase)
                            || e.Type.Equals("VSIN", StringComparison.OrdinalIgnoreCase)
                        )
                        .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (plan.RequiresCurrents && currentSources.Count > 0)
                    {
                        var iWrdataPath = BenchRuntimePaths.GetTranCurrentsWrdataPath(
                            Path.GetDirectoryName(testbenchPath)!,
                            plan.CircuitName,
                            plan.BindingName,
                            a.Name
                        );
                        var sourceNames = currentSources.Select(s => "V" + s.Id).ToList();
                        currents = NgspiceWrdataTranParser.Parse(iWrdataPath, sourceNames);
                    }

                    analyses[a.Name] = new BenchMeasurementRunner.AnalysisContext(
                        a.Name,
                        StartHz: 0,
                        StopHz: 0,
                        StartS: a.StartS ?? 0,
                        StopS: a.StopS ?? 0,
                        Ac: null,
                        Noise: null,
                        Tran: nodes,
                        TranCurrents: currents
                    );
                }
            }

            var vdcSources = plan
                .HarnessElements.Where(e =>
                    e.Type.Equals("VDC", StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var opCurrentsBySourceName = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase
            );
            if (vdcSources.Count > 0)
            {
                var wrdataPath = BenchRuntimePaths.GetOpWrdataPath(
                    Path.GetDirectoryName(testbenchPath)!,
                    plan.CircuitName,
                    plan.BindingName
                );

                var sourceNames = vdcSources.Select(s => "V" + s.Id).ToList();
                foreach (var kvp in NgspiceWrdataOpParser.ParseCurrents(wrdataPath, sourceNames))
                {
                    opCurrentsBySourceName[kvp.Key] = kvp.Value;
                }
            }

            var runner = new BenchMeasurementRunner(
                plan.Bench,
                plan.Functions,
                analyses,
                plan.Terminals,
                plan.Env,
                plan.Harness,
                plan.Constraints,
                harnessElements: plan.HarnessElements,
                sourceCurrentsByName: opCurrentsBySourceName,
                dutNodeKeyByPinRef: plan.DutNodeKeyByPinRef
            );

            swParse.Stop();
            parseTime = swParse.Elapsed;

            var swEval = Stopwatch.StartNew();
            var constraintsForBench = GetNumericConstraintsForBench(circuit, benchName);
            var nodeByMetric = BuildNodeByMetric(constraintsForBench, circuit);
            var values = EvaluateMetricsForConstraintsOrAll(
                runner,
                plan.Bench,
                constraintsForBench
            );
            results = new BenchResult { Circuit = circuit.Name, Bench = benchName };
            foreach (var (metric, v) in values)
            {
                var node = nodeByMetric.TryGetValue(metric, out var n) ? n : null;
                var key = node == null ? metric : $"{metric}@{node}";

                if (v.Value is BenchMissing)
                {
                    // Preserve the "missing" distinction for compliance by omitting the measurement.
                    continue;
                }

                var resultValue = double.NaN;
                string? error = null;
                if (v.Value is BenchNumber num)
                {
                    resultValue = num.Value;
                }
                else if (v.Value is BenchError err)
                {
                    error = err.Message;
                }
                else
                {
                    error = $"Unexpected measurement value type '{v.Value.GetType().Name}'.";
                }

                results.Measurements[key] = new MeasurementResult
                {
                    Metric = metric,
                    Value = resultValue,
                    Unit = v.Unit,
                    Node = node,
                    Bench = benchName,
                    Error = error,
                };
            }

            MergeMeasurements(allMeasurements, results.Measurements.Values);
            swEval.Stop();
            evalTime = swEval.Elapsed;
        }
        catch (Exception ex)
        {
            swParse.Stop();
            parseTime = swParse.Elapsed;
            _logger.LogError(
                ex,
                "Failed to evaluate bench measurements for '{BenchName}'.",
                benchName
            );
            Progress($"bench: FAIL {circuit.Name}/{benchName} (evaluation)");
            timing.AddBench(
                new BenchBenchTiming(
                    circuit.Name,
                    benchName,
                    Simulation: simTime,
                    ParseOutputs: parseTime,
                    EvaluateMeasurements: evalTime,
                    WriteArtifacts: writeTime
                )
            );
            return new BenchRunBenchSummary(
                Name: benchName,
                Succeeded: false,
                ExitCode: 1,
                Error: $"Failed to evaluate measurements: {ex.Message}",
                Stderr: run.Stderr,
                TestbenchPath: testbenchPath,
                TracePath: null,
                ResultsPath: null
            );
        }

        var swWrite = Stopwatch.StartNew();
        var resultsPath = Path.Combine(
            Path.GetDirectoryName(testbenchPath)!,
            $"{circuit.Name}_{benchName}_results.json"
        );
        File.WriteAllText(resultsPath, JsonSerializer.Serialize(results, _jsonSerializerOptions));

        var tracePath = Path.Combine(
            Path.GetDirectoryName(testbenchPath)!,
            $"{circuit.Name}_{benchName}_trace.jsonl"
        );
        BenchTraceWriter.WriteTraceJsonl(
            tracePath,
            args with
            {
                BenchName = benchName,
            },
            circuit,
            testbenchPath,
            new List<BenchResultParser.TracePoint>(),
            results
        );
        swWrite.Stop();
        writeTime = swWrite.Elapsed;

        timing.AddBench(
            new BenchBenchTiming(
                circuit.Name,
                benchName,
                Simulation: simTime,
                ParseOutputs: parseTime,
                EvaluateMeasurements: evalTime,
                WriteArtifacts: writeTime
            )
        );

        Progress(
            $"bench: done {circuit.Name}/{benchName} (sim {simTime.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s)"
        );

        return new BenchRunBenchSummary(
            Name: benchName,
            Succeeded: true,
            ExitCode: 0,
            Error: null,
            Stderr: null,
            TestbenchPath: testbenchPath,
            TracePath: tracePath,
            ResultsPath: resultsPath
        );
    }

    private static IReadOnlyList<NumericConstraint> GetNumericConstraintsForBench(
        Circuit circuit,
        string benchName
    )
    {
        if (circuit.Constraints?.Numeric is null)
        {
            return Array.Empty<NumericConstraint>();
        }

        return circuit
            .Constraints.Numeric.Where(c =>
                string.Equals(c.Bench, benchName, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
    }

    private static Dictionary<string, string?> BuildNodeByMetric(
        IReadOnlyList<NumericConstraint> constraints,
        Circuit circuit
    )
    {
        var nodeByMetric = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (constraints.Count == 0)
        {
            return nodeByMetric;
        }

        foreach (
            var group in constraints.GroupBy(
                c => FormatMetricKey(c),
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            var nodes = group
                .Select(c => c.Node)
                .Where(n => n != null && IsValidConstraintNode(circuit, n))
                .Select(n => n!.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            nodeByMetric[group.Key] = nodes.Count == 1 ? nodes[0] : null;
        }

        return nodeByMetric;
    }

    private static IReadOnlyDictionary<
        string,
        (BenchValue Value, string Unit)
    > EvaluateMetricsForConstraintsOrAll(
        BenchMeasurementRunner runner,
        BenchDefinition bench,
        IReadOnlyList<NumericConstraint> constraints
    )
    {
        if (constraints.Count == 0)
        {
            var all = runner.RunAllValues();
            var evaluations = new Dictionary<string, (BenchValue, string)>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var (metric, v) in all)
            {
                var unit =
                    bench
                        .Measurements.FirstOrDefault(m =>
                            m.Name.Equals(metric, StringComparison.OrdinalIgnoreCase)
                        )
                        ?.Unit
                    ?? string.Empty;
                evaluations[metric] = (v, unit);
            }
            return evaluations;
        }

        // Deduplicate constraint-driven metric invocations (including parameterized metrics).
        var invocations = new Dictionary<string, NumericConstraint>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var c in constraints)
        {
            invocations.TryAdd(FormatMetricKey(c), c);
        }

        var results = new Dictionary<string, (BenchValue, string)>(
            StringComparer.OrdinalIgnoreCase
        );

        // Evaluate non-parameterized metrics in one batch.
        var noArgMetrics = invocations
            .Values.Where(c => c.MetricArgs.Count == 0)
            .Select(c => c.Metric)
            .ToList();
        if (noArgMetrics.Count > 0)
        {
            foreach (var (metric, v) in runner.RunMetricValues(noArgMetrics))
            {
                var unit =
                    bench
                        .Measurements.FirstOrDefault(m =>
                            m.Name.Equals(metric, StringComparison.OrdinalIgnoreCase)
                        )
                        ?.Unit
                    ?? string.Empty;
                results[metric] = (v, unit);
            }
        }

        // Evaluate parameterized metric invocations one-by-one.
        foreach (var (metricKey, c) in invocations)
        {
            if (c.MetricArgs.Count == 0)
            {
                continue;
            }

            // Validate measurement exists on the bench.
            var measurement = bench.Measurements.FirstOrDefault(m =>
                m.Name.Equals(c.Metric, StringComparison.OrdinalIgnoreCase)
            );
            if (measurement is null)
            {
                throw new InvalidOperationException(
                    $"Unknown measurement '{c.Metric}' referenced by constraint '{c.Id}'."
                );
            }

            var args = new Dictionary<string, BenchValue>(StringComparer.Ordinal);
            foreach (var a in c.MetricArgs)
            {
                args[a.Name] = ParseConstraintArgValue(a.Value);
            }

            var unit = measurement.Unit;
            results[metricKey] = (runner.RunMetricWithNamedArgsValue(c.Metric, args), unit);
        }

        return results;
    }

    private static BenchValue ParseConstraintArgValue(string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0)
        {
            return BenchMissing.Value;
        }

        if (raw.Contains("||", StringComparison.Ordinal))
        {
            return new BenchSymbol(raw);
        }

        try
        {
            return BenchQuantity.Parse(raw);
        }
        catch
        {
            return new BenchSymbol(raw);
        }
    }

    private static string FormatMetricKey(NumericConstraint constraint)
    {
        if (constraint.MetricArgs.Count == 0)
        {
            return constraint.Metric;
        }

        var args = constraint
            .MetricArgs.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => $"{a.Name}={a.Value}");

        return $"{constraint.Metric}({string.Join(", ", args)})";
    }

    private static void MergeMeasurements(
        Dictionary<string, MeasurementResult> target,
        IEnumerable<MeasurementResult> source
    )
    {
        foreach (var measurement in source)
        {
            var metricKey =
                measurement.Node == null
                    ? measurement.Metric
                    : $"{measurement.Metric}@{measurement.Node}";
            var key = string.IsNullOrEmpty(measurement.Bench)
                ? metricKey
                : $"{measurement.Bench}/{metricKey}";
            target[key] = measurement;
        }
    }

    private static IReadOnlyDictionary<string, BenchPlan> BuildPlanMap(CascodeDocument doc)
    {
        var plans = BenchCompiler.CompileAllPlans(doc);
        var map = new Dictionary<string, BenchPlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            map[BuildPlanKey(plan.CircuitName, plan.BindingName)] = plan;
        }

        return map;
    }

    // BuildNodeByMetric moved above (constraint-driven; supports parameterized metrics).

    private static bool IsValidConstraintNode(Circuit circuit, NodeRef node)
    {
        if (string.IsNullOrWhiteSpace(node.Path))
        {
            return false;
        }

        if (node.Scope.Equals("net", StringComparison.OrdinalIgnoreCase))
        {
            return HasCircuitNet(circuit, node.Path);
        }

        if (node.Scope.Equals("port", StringComparison.OrdinalIgnoreCase))
        {
            return circuit.Ports.Any(p =>
                p.Name.Equals(node.Path, StringComparison.OrdinalIgnoreCase)
            );
        }

        return true;
    }

    private static bool HasCircuitNet(Circuit circuit, string path)
    {
        if (circuit.Ports.Any(p => p.Name.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (circuit.Supplies.Any(s => s.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (circuit.Grounds.Any(g => g.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }
}
