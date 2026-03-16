using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cascode.Bench;
using Cascode.Cli.Output;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Cascode.Language.Validation;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Services;

public class BenchRunService
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private static readonly string[] OpParamVectorNames =
    {
        "op_gm",
        "op_gds",
        "op_vth",
        "op_vdsat",
        "op_cgs",
        "op_cgd",
        "op_cgg",
        "op_cds",
        "op_id",
        "op_vgs",
        "op_vds",
    };

    private readonly ILogger<BenchRunService> _logger;
    private readonly Action<string>? _progress;
    private readonly IBenchProgressContext? _progressContext;
    private readonly ICliOutput? _output;
    private readonly object _progressLock = new();

    public BenchRunService(ILogger<BenchRunService> logger, Action<string>? progress = null)
    {
        _logger = logger;
        _progress = progress;
    }

    public BenchRunService(
        ILogger<BenchRunService> logger,
        IBenchProgressContext progressContext,
        ICliOutput? output = null
    )
    {
        _logger = logger;
        _progressContext = progressContext;
        _output = output;
    }

    public sealed record BenchRunArgs(
        string CascodePath,
        string? BenchName,
        string? OutputDir,
        BenchBackendType Backend,
        bool Verbose,
        bool StrictCompliance,
        int Parallelism,
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
        BenchRunTimingReport? Timing = null,
        IReadOnlyList<string>? ValidationErrors = null
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

    private static int ResolveParallelism(int parallelism) =>
        parallelism <= 0 ? Math.Max(1, Environment.ProcessorCount) : parallelism;

    private void Progress(string message)
    {
        if (_progress is null)
        {
            return;
        }

        lock (_progressLock)
        {
            _progress(message);
        }
    }

    private sealed record BenchPrepared(
        string InstanceName,
        string TestbenchPath,
        string? Stderr,
        BenchPlan Plan,
        BenchMeasurementRunner Runner,
        IReadOnlyList<NumericConstraint> ConstraintsForBench,
        IReadOnlyDictionary<string, string?> NodeByMetric,
        IReadOnlyList<BenchResultParser.TracePoint> TracePoints,
        TimeSpan SimulationTime,
        TimeSpan ParseTime
    );

    private sealed record MetricValue(BenchValue Value, string Unit);

    public static bool TryParseArgs(string[] args, out BenchRunArgs parsed, out string error)
    {
        parsed = new BenchRunArgs(
            string.Empty,
            null,
            null,
            BenchBackendType.Ngspice,
            false,
            StrictCompliance: false,
            Parallelism: 0
        );
        error = string.Empty;

        string? cascodePath = null;
        string? benchName = null;
        string? outputDir = null;
        var backend = BenchBackendType.Ngspice;
        var verbose = false;
        var strict = false;
        var parallelism = 0;
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
            else if (args[i] == "--parallel" && i + 1 < args.Length)
            {
                if (
                    !int.TryParse(
                        args[++i],
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out parallelism
                    )
                    || parallelism < 0
                )
                {
                    error = "Error: --parallel expects a non-negative integer.";
                    return false;
                }
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
            Parallelism: parallelism,
            string.IsNullOrWhiteSpace(circuitFilter) ? null : circuitFilter
        );
        return true;
    }

    private sealed record ResolvedPaths(string InputDir, string LinkArtifactsDir);

    private static ResolvedPaths ResolveInputAndOutputPaths(BenchRunArgs args)
    {
        var inputDir =
            Path.GetDirectoryName(Path.GetFullPath(args.CascodePath))
            ?? Directory.GetCurrentDirectory();
        var linkArtifactsDir = args.OutputDir ?? Path.Combine(inputDir, "build", "link", "bench");
        return new ResolvedPaths(inputDir, linkArtifactsDir);
    }

    private (
        CascodeDocument Doc,
        BenchRunArgs Args,
        IReadOnlyList<Circuit> Circuits
    ) PerformLoadAndLink(
        string workspaceRoot,
        BenchRunArgs args,
        ResolvedPaths paths,
        BenchRunTimingCollector timing
    )
    {
        Progress("load+link: start");
        var loadStep = timing.Step("load+link");
        var loaded = CascodeLoadLinkService.LoadAndLinkIfNeeded(
            args.CascodePath,
            workspaceRoot,
            paths.LinkArtifactsDir,
            _logger
        );
        loadStep.Stop();
        Progress(
            $"load+link: done ({loadStep.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s)"
        );
        var updatedArgs = args with { CascodePath = loaded.ResolvedPath };
        var allCircuitsWithBenches = BenchRunHelpers.GetElCircuitsWithBenches(loaded.Document);
        return (loaded.Document, updatedArgs, allCircuitsWithBenches);
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
        var paths = ResolveInputAndOutputPaths(args);
        var (doc, updatedArgs, allCircuitsWithBenches) = PerformLoadAndLink(
            workspaceRoot,
            args,
            paths,
            timing
        );
        args = updatedArgs;

        if (allCircuitsWithBenches.Count == 0)
        {
            _logger.LogError("No EL-level circuits with benches found in Cascode document.");
            return BuildEmptyResult(args, timing);
        }

        var filterResult = ValidateCircuitFilterOrReturnError(
            allCircuitsWithBenches,
            args.CircuitFilter,
            args.Backend,
            args.OutputDir ?? string.Empty,
            out var circuitsWithBenches
        );
        if (filterResult != null)
        {
            return filterResult with
            {
                Summary = filterResult.Summary with { Timing = timing.Build() },
            };
        }

        var outputDir = ResolveOutputDirectory(args.OutputDir, paths.InputDir, circuitsWithBenches);
        Directory.CreateDirectory(outputDir);

        var emitResult = RunEmitPhase(
            doc,
            outputDir,
            workspaceRoot,
            pdkRoot,
            args,
            timing,
            out var emit
        );
        if (emitResult != null)
        {
            return emitResult with
            {
                Summary = emitResult.Summary with { Timing = timing.Build() },
            };
        }

        var circuitSummaries = RunBenchPhase(
            circuitsWithBenches,
            args,
            outputDir,
            doc,
            emit.Emit.TestbenchPaths,
            timing
        );

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

    private MultiCircuitBenchRunResult BuildEmptyResult(
        BenchRunArgs args,
        BenchRunTimingCollector timing
    )
    {
        return new MultiCircuitBenchRunResult(
            2,
            new MultiCircuitBenchRunSummary(
                args.Backend,
                args.OutputDir ?? string.Empty,
                Array.Empty<CircuitBenchRunSummary>(),
                null,
                new ComplianceReport(),
                Timing: timing.Build()
            )
        );
    }

    private static string ResolveOutputDirectory(
        string? explicitOutputDir,
        string inputDir,
        IReadOnlyList<Circuit> circuits
    )
    {
        return explicitOutputDir
            ?? Path.Combine(
                inputDir,
                "build",
                "bench",
                circuits.Count == 1 ? circuits[0].Name : "multi"
            );
    }

    private MultiCircuitBenchRunResult? RunEmitPhase(
        CascodeDocument doc,
        string outputDir,
        string workspaceRoot,
        string? pdkRoot,
        BenchRunArgs args,
        BenchRunTimingCollector timing,
        out ValidatedEmitResult emit
    )
    {
        Progress("emit: start");
        var emitStep = timing.Step("emit");
        var emitResult = EmitAllDesignsOrReturnError(
            doc,
            outputDir,
            workspaceRoot,
            pdkRoot,
            args,
            out emit
        );
        emitStep.Stop();
        Progress(
            $"emit: done ({emitStep.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s)"
        );
        return emitResult;
    }

    private List<CircuitBenchRunSummary> RunBenchPhase(
        IReadOnlyList<Circuit> circuitsWithBenches,
        BenchRunArgs args,
        string outputDir,
        CascodeDocument doc,
        IReadOnlyList<string> testbenchPaths,
        BenchRunTimingCollector timing
    )
    {
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
                args,
                outputDir,
                testbenchPaths,
                planMap,
                timing
            );
            circuitSummaries.Add(circuitSummary);
        }
        return circuitSummaries;
    }

    private CircuitBenchRunSummary RunCircuitBenches(
        Circuit circuit,
        BenchRunArgs args,
        string outputDir,
        IReadOnlyList<string> testbenchPaths,
        IReadOnlyDictionary<string, BenchPlan> planMap,
        BenchRunTimingCollector timing
    )
    {
        // Get instance names from the plan map for this circuit.
        var instanceNames = GetInstanceNamesForCircuit(planMap, circuit.Name);

        // Filter by explicit bench name if provided (matches binding name).
        var instancesToRun = ResolveInstancesToRunForCircuit(
            args.BenchName,
            instanceNames,
            planMap,
            circuit.Name
        );

        var circuitMeasurements = new Dictionary<string, MeasurementResult>(
            StringComparer.OrdinalIgnoreCase
        );
        var benchSummaries = new ConcurrentDictionary<string, BenchRunBenchSummary>(
            StringComparer.OrdinalIgnoreCase
        );

        var benchByBindingAlias = new Dictionary<string, BenchDefinition>(
            StringComparer.OrdinalIgnoreCase
        );
        var bindingMeasurementExportsByBindingAlias = new Dictionary<
            string,
            IReadOnlyDictionary<string, BenchBindingMeasurementExport>
        >(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in planMap.Values)
        {
            if (!plan.CircuitName.Equals(circuit.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            benchByBindingAlias.TryAdd(plan.BindingName, plan.Bench);

            var exports = plan.Binding.Statements.OfType<BenchBindingMeasurementExport>().ToList();
            if (exports.Count > 0)
            {
                var byName = new Dictionary<string, BenchBindingMeasurementExport>(
                    StringComparer.OrdinalIgnoreCase
                );
                foreach (var export in exports)
                {
                    byName[export.Name] = export;
                }
                bindingMeasurementExportsByBindingAlias[plan.BindingName] = byName;
            }
        }

        var constraintsToEvaluate = circuit.Constraints?.Numeric is null
            ? new List<NumericConstraint>()
            : circuit.Constraints.Numeric.Where(c => instancesToRun.Contains(c.Bench)).ToList();

        if (
            !BenchDependencyGraph.TryBuild(
                circuit,
                constraintsToEvaluate,
                benchByBindingAlias,
                bindingMeasurementExportsByBindingAlias,
                out var graph,
                out var graphDiagnostics
            )
        )
        {
            var message = string.Join(
                "; ",
                graphDiagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)
            );
            _logger.LogError("Bench dependency graph build failed: {Message}", message);
            return new CircuitBenchRunSummary(
                circuit.Name,
                new[]
                {
                    new BenchRunBenchSummary(
                        Name: args.BenchName ?? string.Empty,
                        Succeeded: false,
                        ExitCode: 1,
                        Error: message.Length == 0
                            ? "Bench dependency graph build failed."
                            : message,
                        Stderr: null,
                        TestbenchPath: null,
                        TracePath: null,
                        ResultsPath: null
                    ),
                },
                new ComplianceReport()
            );
        }

        // Render the dependency graph if output is available
        if (_output is not null && graph.InvocationsById.Count > 0)
        {
            BenchDependencyGraphRenderer.Render(graph, _output);
        }

        static string? BuildRefInvocationId(MeasurementBenchMeasurementRef r)
        {
            foreach (var a in r.Args)
            {
                if (a.Name is null)
                {
                    return null;
                }
            }

            if (r.Args.Count == 0)
            {
                return $"{r.BindingAlias}/{r.MeasurementName}";
            }

            var parts = r
                .Args.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(a => $"{a.Name}={a.Text}");
            return $"{r.BindingAlias}/{r.MeasurementName}({string.Join(", ", parts)})";
        }

        var metricValuesById = new ConcurrentDictionary<string, MetricValue>(
            StringComparer.Ordinal
        );

        BenchValue ResolveRef(MeasurementBenchMeasurementRef r)
        {
            var id = BuildRefInvocationId(r);
            if (id is null)
            {
                return new BenchError(
                    $"Cross-bench reference '{r.BindingAlias}::{r.MeasurementName}(...)' requires named arguments."
                );
            }

            if (!metricValuesById.TryGetValue(id, out var v))
            {
                return new BenchError(
                    $"Cross-bench dependency '{id}' was not evaluated (missing result)."
                );
            }

            return v.Value;
        }

        var availableInstances = instanceNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredInstances = graph
            .InvocationsById.Values.Select(v => v.BenchInstanceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(availableInstances.Contains)
            .ToList();
        var finalInstancesToRun = instancesToRun
            .Concat(requiredInstances)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var preparedByInstance = new ConcurrentDictionary<string, BenchPrepared>(
            StringComparer.OrdinalIgnoreCase
        );

        var evalTicksByBench = new ConcurrentDictionary<string, long>(
            StringComparer.OrdinalIgnoreCase
        );

        var parallelism = ResolveParallelism(args.Parallelism);

        // Create progress tasks upfront if multi-task progress is available
        var progressTasks = new ConcurrentDictionary<string, IBenchTask>(
            StringComparer.OrdinalIgnoreCase
        );
        if (_progressContext is not null)
        {
            foreach (var name in finalInstancesToRun)
            {
                var task = _progressContext.AddTask($"○ {circuit.Name}/{name}");
                progressTasks[name] = task;
            }
        }

        Parallel.ForEach(
            finalInstancesToRun,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            instanceName =>
            {
                if (progressTasks.TryGetValue(instanceName, out var task))
                {
                    task.UpdateDescription($"● {circuit.Name}/{instanceName} simulating...");
                    task.StartTask();
                }
                else
                {
                    Progress($"bench: run {circuit.Name}/{instanceName}");
                }

                var prepError = TryPrepareBench(
                    circuit,
                    args,
                    testbenchPaths,
                    instanceName,
                    planMap,
                    benchMeasurementRefResolver: ResolveRef,
                    out var simTime,
                    out var parseTime,
                    out var prepared
                );
                if (prepError is not null)
                {
                    benchSummaries[instanceName] = prepError;
                    timing.AddBench(
                        new BenchBenchTiming(
                            circuit.Name,
                            instanceName,
                            Simulation: simTime,
                            ParseOutputs: parseTime,
                            EvaluateMeasurements: TimeSpan.Zero,
                            WriteArtifacts: TimeSpan.Zero
                        )
                    );
                    if (task is not null)
                    {
                        task.UpdateDescription($"✗ {circuit.Name}/{instanceName}");
                        task.StopTask();
                    }
                    return;
                }

                preparedByInstance[instanceName] = prepared!;
                if (task is not null)
                {
                    task.UpdateDescription($"✓ {circuit.Name}/{instanceName}");
                    task.StopTask();
                }
            }
        );

        if (graph.InvocationsById.Count > 0)
        {
            foreach (var level in graph.GetExecutionLevels())
            {
                var groups = level
                    .GroupBy(
                        id => graph.InvocationsById[id].BenchInstanceName,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToList();

                Parallel.ForEach(
                    groups,
                    new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                    group =>
                    {
                        var benchInstance = group.Key;
                        var sw = Stopwatch.StartNew();

                        foreach (var id in group)
                        {
                            var invocation = graph.InvocationsById[id];

                            var unit = string.Empty;
                            if (
                                benchByBindingAlias.TryGetValue(
                                    invocation.BenchBindingAlias,
                                    out var b
                                )
                            )
                            {
                                unit =
                                    b.Measurements.FirstOrDefault(m =>
                                        m.Name.Equals(
                                            invocation.MetricName,
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                    )?.Unit
                                    ?? string.Empty;
                            }
                            if (
                                string.IsNullOrWhiteSpace(unit)
                                && bindingMeasurementExportsByBindingAlias.TryGetValue(
                                    invocation.BenchBindingAlias,
                                    out var exports
                                )
                                && exports.TryGetValue(invocation.MetricName, out var export)
                            )
                            {
                                unit = export.Unit;
                            }

                            if (!preparedByInstance.TryGetValue(benchInstance, out var prepared))
                            {
                                metricValuesById[id] = new MetricValue(
                                    new BenchError(
                                        $"Missing bench run context for '{benchInstance}' (simulation may have failed)."
                                    ),
                                    unit
                                );
                                continue;
                            }

                            if (graph.DependenciesById.TryGetValue(id, out var deps))
                            {
                                var failedDep = deps.FirstOrDefault(dep =>
                                    metricValuesById.TryGetValue(dep, out var dv)
                                    && dv.Value is BenchError
                                );
                                if (!string.IsNullOrWhiteSpace(failedDep))
                                {
                                    metricValuesById[id] = new MetricValue(
                                        new BenchError($"Failed dependency: {failedDep}"),
                                        unit
                                    );
                                    continue;
                                }
                            }

                            var bench = prepared.Plan.Bench;
                            var measurement = bench.Measurements.FirstOrDefault(m =>
                                m.Name.Equals(
                                    invocation.MetricName,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            );
                            unit = measurement?.Unit ?? unit;

                            try
                            {
                                BenchValue value;
                                if (
                                    invocation.Args.Count == 0
                                    && bindingMeasurementExportsByBindingAlias.TryGetValue(
                                        invocation.BenchBindingAlias,
                                        out var exportsByName
                                    )
                                    && exportsByName.TryGetValue(
                                        invocation.MetricName,
                                        out var exportDef
                                    )
                                    && exportDef.Target.BindingAlias.Equals(
                                        "base",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                                {
                                    var argsDict = new Dictionary<string, BenchValue>(
                                        StringComparer.Ordinal
                                    );
                                    foreach (var a in exportDef.Target.Args)
                                    {
                                        if (a.Name is null)
                                        {
                                            continue;
                                        }

                                        argsDict[a.Name] =
                                            prepared.Runner.EvaluateExpressionForPlan(a.Expr);
                                    }

                                    value =
                                        argsDict.Count == 0
                                            ? prepared.Runner.RunMetricValues(
                                                new[] { exportDef.Target.MeasurementName }
                                            )[exportDef.Target.MeasurementName]
                                            : prepared.Runner.RunMetricWithNamedArgsValue(
                                                exportDef.Target.MeasurementName,
                                                argsDict
                                            );
                                }
                                else if (invocation.Args.Count == 0)
                                {
                                    value = prepared.Runner.RunMetricValues(
                                        new[] { invocation.MetricName }
                                    )[invocation.MetricName];
                                }
                                else
                                {
                                    var argsDict = new Dictionary<string, BenchValue>(
                                        StringComparer.Ordinal
                                    );
                                    foreach (var a in invocation.Args)
                                    {
                                        argsDict[a.Name] =
                                            prepared.Runner.EvaluateExpressionForPlan(a.Expr);
                                    }
                                    value = prepared.Runner.RunMetricWithNamedArgsValue(
                                        invocation.MetricName,
                                        argsDict
                                    );
                                }

                                metricValuesById[id] = new MetricValue(value, unit);
                            }
                            catch (Exception ex)
                            {
                                metricValuesById[id] = new MetricValue(
                                    new BenchError(ex.Message),
                                    unit
                                );
                            }
                        }

                        sw.Stop();
                        evalTicksByBench.AddOrUpdate(
                            benchInstance,
                            sw.ElapsedTicks,
                            (_, existing) => existing + sw.ElapsedTicks
                        );
                    }
                );
            }
        }

        foreach (var instanceName in finalInstancesToRun)
        {
            if (benchSummaries.ContainsKey(instanceName))
            {
                continue;
            }

            if (!preparedByInstance.TryGetValue(instanceName, out var prepared))
            {
                benchSummaries[instanceName] = new BenchRunBenchSummary(
                    Name: instanceName,
                    Succeeded: false,
                    ExitCode: 1,
                    Error: "Bench did not produce results (missing prepared runner).",
                    Stderr: null,
                    TestbenchPath: null,
                    TracePath: null,
                    ResultsPath: null
                );
                continue;
            }

            var swEval = Stopwatch.StartNew();
            var valuesToWrite = new Dictionary<string, MetricValue>(
                StringComparer.OrdinalIgnoreCase
            );

            if (prepared.ConstraintsForBench.Count == 0)
            {
                var all = prepared.Runner.RunAllValues();
                foreach (var (metric, v) in all)
                {
                    var unit =
                        prepared
                            .Plan.Bench.Measurements.FirstOrDefault(m =>
                                m.Name.Equals(metric, StringComparison.OrdinalIgnoreCase)
                            )
                            ?.Unit
                        ?? string.Empty;
                    valuesToWrite[metric] = new MetricValue(v, unit);
                }
            }

            foreach (
                var inv in graph.InvocationsById.Values.Where(v =>
                    v.BenchInstanceName.Equals(instanceName, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                if (metricValuesById.TryGetValue(inv.Id, out var v))
                {
                    valuesToWrite[inv.MetricKey] = v;
                }
                else
                {
                    valuesToWrite[inv.MetricKey] = new MetricValue(
                        new BenchError($"Missing evaluation for '{inv.Id}'."),
                        string.Empty
                    );
                }
            }

            swEval.Stop();
            evalTicksByBench.AddOrUpdate(
                instanceName,
                swEval.ElapsedTicks,
                (_, existing) => existing + swEval.ElapsedTicks
            );

            var results = new BenchResult { Circuit = circuit.Name, Bench = instanceName };
            foreach (var (metric, v) in valuesToWrite)
            {
                var node = prepared.NodeByMetric.TryGetValue(metric, out var n) ? n : null;
                var key = node == null ? metric : $"{metric}@{node}";

                if (v.Value is BenchMissing)
                {
                    continue;
                }

                double? resultValue = null;
                double[]? resultValues = null;
                string? error = null;
                if (v.Value is BenchNumber num)
                {
                    resultValue = num.Value;
                }
                else if (v.Value is BenchError err)
                {
                    error = err.Message;
                }
                else if (TryExtractSeriesValues(v.Value, out var extractedValues))
                {
                    resultValues = extractedValues;
                }
                else
                {
                    error = $"Unexpected measurement value type '{v.Value.GetType().Name}'.";
                }

                results.Measurements[key] = new MeasurementResult
                {
                    Metric = metric,
                    Value = resultValue,
                    Values = resultValues,
                    Unit = v.Unit,
                    Node = node,
                    Bench = instanceName,
                    Error = error,
                };
            }

            var swWrite = Stopwatch.StartNew();
            var resultsPath = Path.Combine(
                Path.GetDirectoryName(prepared.TestbenchPath)!,
                $"{circuit.Name}_{instanceName}_results.json"
            );
            File.WriteAllText(
                resultsPath,
                JsonSerializer.Serialize(results, _jsonSerializerOptions)
            );

            var tracePath = Path.Combine(
                Path.GetDirectoryName(prepared.TestbenchPath)!,
                $"{circuit.Name}_{instanceName}_trace.jsonl"
            );
            BenchTraceWriter.WriteTraceJsonl(
                tracePath,
                args with
                {
                    BenchName = instanceName,
                },
                circuit,
                prepared.TestbenchPath,
                prepared.TracePoints.ToList(),
                results
            );

            if (prepared.TracePoints.Count > 0)
            {
                var pointsCsvPath = Path.Combine(
                    Path.GetDirectoryName(prepared.TestbenchPath)!,
                    $"{circuit.Name}_{instanceName}_results.csv"
                );
                WriteTracePointsCsv(
                    pointsCsvPath,
                    prepared.TracePoints,
                    prepared.Plan.Bench.Measurements
                );
            }
            swWrite.Stop();

            lock (circuitMeasurements)
            {
                MergeMeasurements(circuitMeasurements, results.Measurements.Values);
            }

            var evalTime = TimeSpan.FromTicks(
                evalTicksByBench.TryGetValue(instanceName, out var ticks) ? ticks : 0
            );
            timing.AddBench(
                new BenchBenchTiming(
                    circuit.Name,
                    instanceName,
                    Simulation: prepared.SimulationTime,
                    ParseOutputs: prepared.ParseTime,
                    EvaluateMeasurements: evalTime,
                    WriteArtifacts: swWrite.Elapsed
                )
            );

            Progress(
                $"bench: done {circuit.Name}/{instanceName} (sim {prepared.SimulationTime.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s)"
            );

            benchSummaries[instanceName] = new BenchRunBenchSummary(
                Name: instanceName,
                Succeeded: true,
                ExitCode: 0,
                Error: null,
                Stderr: null,
                TestbenchPath: prepared.TestbenchPath,
                TracePath: tracePath,
                ResultsPath: resultsPath
            );
        }

        var combinedResults = BenchResultParser.CreateCombinedResults(
            circuit.Name,
            finalInstancesToRun,
            circuitMeasurements
        );

        // Write combined results file (for verify command compatibility)
        if (finalInstancesToRun.Count > 0 && circuitMeasurements.Count > 0)
        {
            BenchTraceWriter.WriteCombinedResults(outputDir, circuit.Name, combinedResults);
        }

        var compliance = ComplianceChecker.Check(circuit, combinedResults);

        var orderedSummaries = finalInstancesToRun
            .Where(n => benchSummaries.TryGetValue(n, out _))
            .Select(n => benchSummaries[n])
            .ToList();

        return new CircuitBenchRunSummary(circuit.Name, orderedSummaries, compliance);
    }

    /// <summary>
    /// Gets all instance names from the plan map for a specific circuit.
    /// </summary>
    private static IReadOnlyList<string> GetInstanceNamesForCircuit(
        IReadOnlyDictionary<string, BenchPlan> planMap,
        string circuitName
    )
    {
        var prefix = circuitName + ":";
        return planMap
            .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Value.InstanceName)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Resolves instances to run for a circuit, filtering by explicit bench name if provided.
    /// The filter matches against binding names (not instance names with hash).
    /// </summary>
    private static IReadOnlyList<string> ResolveInstancesToRunForCircuit(
        string? explicitBench,
        IReadOnlyList<string> instanceNames,
        IReadOnlyDictionary<string, BenchPlan> planMap,
        string circuitName
    )
    {
        if (string.IsNullOrWhiteSpace(explicitBench))
        {
            return instanceNames;
        }

        // Check for circuit-qualified format: "CircuitName:BenchName"
        string targetBench;
        if (explicitBench.Contains(':'))
        {
            var parts = explicitBench.Split(':', 2);
            var targetCircuit = parts[0];
            targetBench = parts[1];

            // If this isn't the target circuit, skip all instances.
            if (!targetCircuit.Equals(circuitName, StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }
        }
        else
        {
            targetBench = explicitBench;
        }

        // Filter instances by binding name (which is the base name without hash).
        return instanceNames
            .Where(instanceName =>
            {
                var key = BuildPlanKey(circuitName, instanceName);
                if (!planMap.TryGetValue(key, out var plan))
                {
                    return false;
                }

                return plan.BindingName.Equals(targetBench, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
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
                        FailureReason = result.FailureReason,
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
            var validationErrors = emit
                .Validation.GetErrors()
                .Select(FormatValidationError)
                .ToArray();
            var first = validationErrors.FirstOrDefault() ?? "Emission failed.";
            _logger.LogError("Cascode emission validation failed: {Error}", first);
            return new MultiCircuitBenchRunResult(
                2,
                new MultiCircuitBenchRunSummary(
                    args.Backend,
                    outputDir,
                    Array.Empty<CircuitBenchRunSummary>(),
                    null,
                    new ComplianceReport(),
                    ValidationErrors: validationErrors
                )
            );
        }

        return null;
    }

    private static string FormatValidationError(ValidationError error)
    {
        var formatted = $"[{error.Code}] {error.Message}";
        if (!string.IsNullOrWhiteSpace(error.Location))
        {
            formatted += $" (at {error.Location})";
        }

        if (!string.IsNullOrWhiteSpace(error.Suggestion))
        {
            formatted += $" Suggestion: {error.Suggestion}";
        }

        return formatted;
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

    private BenchRunBenchSummary? TryPrepareBench(
        Circuit circuit,
        BenchRunArgs args,
        IReadOnlyList<string> testbenchPaths,
        string benchName,
        IReadOnlyDictionary<string, BenchPlan> planMap,
        Func<MeasurementBenchMeasurementRef, BenchValue>? benchMeasurementRefResolver,
        out TimeSpan simulationTime,
        out TimeSpan parseTime,
        out BenchPrepared? prepared
    )
    {
        prepared = null;
        simulationTime = TimeSpan.Zero;
        parseTime = TimeSpan.Zero;

        var testbenchPath = BenchRunHelpers.FindTestbenchPath(
            testbenchPaths,
            circuit.Name,
            benchName
        );

        NgspiceExecutor.NgspiceRun run;
        var swSim = Stopwatch.StartNew();
        try
        {
            run = NgspiceExecutor.Run(testbenchPath);
        }
        catch (Exception ex)
        {
            simulationTime = swSim.Elapsed;
            _logger.LogError(
                ex,
                "Failed to run ngspice for '{BenchName}': {Message}",
                benchName,
                ex.Message
            );
            Progress($"bench: FAIL {circuit.Name}/{benchName} (ngspice launch)");
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
            simulationTime = swSim.Elapsed;
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
            var tracePoints = new List<BenchResultParser.TracePoint>();

            IReadOnlyDictionary<string, double>? opNodeVoltagesByKey = null;
            IReadOnlyDictionary<string, double>? opCurrentsBySourceName = null;
            IReadOnlyDictionary<string, double>? dutOpParamsByName = null;

            var harnessSweeps = circuit.Harness?.Sweeps;
            var hasHarnessSweep = harnessSweeps is { Count: 1 };
            if (hasHarnessSweep)
            {
                if (!hasDc)
                {
                    throw new InvalidOperationException(
                        $"Circuit '{circuit.Name}' defines a harness sweep, but the bound bench has no DCAnalysis."
                    );
                }

                if (plan.Analyses.Any(a => a.Type != BenchValueType.DCAnalysis))
                {
                    throw new InvalidOperationException(
                        $"Harness sweeps are only supported for DC-only benches currently (circuit '{circuit.Name}')."
                    );
                }
            }

            var outDir = Path.GetDirectoryName(testbenchPath)!;

            var vdcSources = plan
                .HarnessElements.Where(e =>
                    e.Type.Equals("VDC", StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var opWrdataPath = BenchRuntimePaths.GetOpWrdataPath(
                outDir,
                plan.CircuitName,
                plan.InstanceName
            );
            var nodesWrdataPath = BenchRuntimePaths.GetOpNodesWrdataPath(
                outDir,
                plan.CircuitName,
                plan.InstanceName
            );
            var paramsWrdataPath = BenchRuntimePaths.GetOpParamsWrdataPath(
                outDir,
                plan.CircuitName,
                plan.InstanceName
            );

            if (hasHarnessSweep)
            {
                var sweepName = harnessSweeps![0].Name;
                var nodesSweep = NgspiceWrdataTranParser.Parse(nodesWrdataPath, plan.AcNodeKeys);
                if (nodesSweep.TimePoints.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"DC sweep produced no points for '{plan.CircuitName}:{plan.InstanceName}'."
                    );
                }

                TranDataset? currentsSweep = null;
                if (plan.RequiresCurrents && vdcSources.Count > 0)
                {
                    var sourceNames = vdcSources.Select(s => "V" + s.Id).ToList();
                    currentsSweep = NgspiceWrdataTranParser.Parse(opWrdataPath, sourceNames);
                }

                NgspiceVectorDataset? opParamsSweep = null;
                if (plan.RequiresOpParams)
                {
                    opParamsSweep = NgspiceWrdataVectorParser.Parse(
                        paramsWrdataPath,
                        OpParamVectorNames
                    );
                }

                for (var i = 0; i < nodesSweep.TimePoints.Length; i++)
                {
                    var harnessForPoint = new Dictionary<string, BenchValue>(
                        plan.Harness,
                        StringComparer.OrdinalIgnoreCase
                    )
                    {
                        [sweepName] = new BenchNumber(
                            BenchNumericKind.VoltageV,
                            nodesSweep.TimePoints[i]
                        ),
                    };

                    var pointNodeVoltages = new Dictionary<string, double>(
                        StringComparer.OrdinalIgnoreCase
                    );
                    foreach (var key in plan.AcNodeKeys)
                    {
                        pointNodeVoltages[key] = nodesSweep.NodeVoltages[key][i];
                    }

                    var pointCurrentsBySource = new Dictionary<string, double>(
                        StringComparer.OrdinalIgnoreCase
                    );
                    if (currentsSweep is not null)
                    {
                        foreach (var (source, values) in currentsSweep.NodeVoltages)
                        {
                            pointCurrentsBySource[source] = values[i];
                        }
                    }

                    var pointOpParams = new Dictionary<string, double>(
                        StringComparer.OrdinalIgnoreCase
                    );
                    if (opParamsSweep is not null)
                    {
                        if (opParamsSweep.X.Length != nodesSweep.TimePoints.Length)
                        {
                            throw new InvalidOperationException(
                                $"op_param wrdata length mismatch for '{plan.CircuitName}:{plan.InstanceName}'."
                            );
                        }

                        pointOpParams = BuildOpParamsDictionary(opParamsSweep, i);
                    }

                    var pointAnalyses = new Dictionary<
                        string,
                        BenchMeasurementRunner.AnalysisContext
                    >(StringComparer.OrdinalIgnoreCase);
                    foreach (var a in plan.Analyses.Where(a => a.Type == BenchValueType.DCAnalysis))
                    {
                        pointAnalyses[a.Name] = new BenchMeasurementRunner.AnalysisContext(
                            a.Name,
                            StartHz: 0,
                            StopHz: 0,
                            StartS: 0,
                            StopS: 0,
                            Op: pointNodeVoltages
                        );
                    }

                    var runnerForPoint = new BenchMeasurementRunner(
                        plan.Bench,
                        plan.Functions,
                        pointAnalyses,
                        plan.Terminals,
                        plan.Env,
                        harnessForPoint,
                        plan.Constraints,
                        harnessElements: plan.HarnessElements,
                        sourceCurrentsByName: pointCurrentsBySource,
                        dutNodeKeyByPinRef: plan.DutNodeKeyByPinRef,
                        dutOpParamsByName: pointOpParams.Count == 0 ? null : pointOpParams,
                        benchMeasurementRefResolver: benchMeasurementRefResolver
                    );

                    var valuesForPoint = runnerForPoint.RunAllValues();
                    var measurementList = new List<MeasurementResult>();
                    foreach (var m in plan.Bench.Measurements.Where(m => m.Parameters.Count == 0))
                    {
                        if (!valuesForPoint.TryGetValue(m.Name, out var v) || v is BenchMissing)
                        {
                            continue;
                        }

                        var value = double.NaN;
                        string? error = null;
                        if (v is BenchNumber n)
                        {
                            value = n.Value;
                        }
                        else if (v is BenchError err)
                        {
                            error = err.Message;
                        }
                        else
                        {
                            error = $"Unexpected measurement value type '{v.GetType().Name}'.";
                        }

                        measurementList.Add(
                            new MeasurementResult
                            {
                                Metric = m.Name,
                                Value = value,
                                Unit = m.Unit,
                                Bench = benchName,
                                Error = error,
                            }
                        );
                    }

                    tracePoints.Add(
                        new BenchResultParser.TracePoint(
                            i,
                            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                            {
                                [sweepName] = nodesSweep.TimePoints[i],
                            },
                            measurementList
                        )
                    );
                }

                var last = nodesSweep.TimePoints.Length - 1;
                opNodeVoltagesByKey = plan.AcNodeKeys.ToDictionary(
                    k => k,
                    k => nodesSweep.NodeVoltages[k][last],
                    StringComparer.OrdinalIgnoreCase
                );

                if (currentsSweep is not null)
                {
                    opCurrentsBySourceName = currentsSweep.NodeVoltages.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value[last],
                        StringComparer.OrdinalIgnoreCase
                    );
                }

                if (opParamsSweep is not null)
                {
                    dutOpParamsByName = new Dictionary<string, double>(
                        StringComparer.OrdinalIgnoreCase
                    )
                    {
                        ["gm"] = opParamsSweep.ValuesByName["op_gm"][last],
                        ["gds"] = opParamsSweep.ValuesByName["op_gds"][last],
                        ["vth"] = opParamsSweep.ValuesByName["op_vth"][last],
                        ["vdsat"] = opParamsSweep.ValuesByName["op_vdsat"][last],
                        ["cgs"] = opParamsSweep.ValuesByName["op_cgs"][last],
                        ["cgd"] = opParamsSweep.ValuesByName["op_cgd"][last],
                        ["cgg"] = opParamsSweep.ValuesByName["op_cgg"][last],
                        ["cds"] = opParamsSweep.ValuesByName["op_cds"][last],
                        ["id"] = opParamsSweep.ValuesByName["op_id"][last],
                        ["vgs"] = opParamsSweep.ValuesByName["op_vgs"][last],
                        ["vds"] = opParamsSweep.ValuesByName["op_vds"][last],
                    };
                }
            }
            else
            {
                if (hasDc)
                {
                    opNodeVoltagesByKey = NgspiceWrdataOpParser.ParseNodeVoltages(
                        nodesWrdataPath,
                        plan.AcNodeKeys
                    );
                }

                if (plan.RequiresCurrents && vdcSources.Count > 0)
                {
                    var sourceNames = vdcSources.Select(s => "V" + s.Id).ToList();
                    opCurrentsBySourceName = NgspiceWrdataOpParser.ParseCurrents(
                        opWrdataPath,
                        sourceNames
                    );
                }

                if (hasDc && plan.RequiresOpParams)
                {
                    var parsed = NgspiceWrdataVectorParser.Parse(
                        paramsWrdataPath,
                        OpParamVectorNames
                    );
                    if (parsed.X.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"op_param enabled, but no operating-point parameters were parsed from '{paramsWrdataPath}'."
                        );
                    }

                    var last = parsed.X.Length - 1;
                    dutOpParamsByName = BuildOpParamsDictionary(parsed, last);
                }
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

                    analyses[a.Name] = new BenchMeasurementRunner.AnalysisContext(
                        a.Name,
                        StartHz: 0,
                        StopHz: 0,
                        StartS: 0,
                        StopS: 0,
                        Op: opNodeVoltagesByKey
                    );
                    continue;
                }

                if (a.Type == BenchValueType.ACAnalysis)
                {
                    var wrdataPath = BenchRuntimePaths.GetAcWrdataPath(
                        Path.GetDirectoryName(testbenchPath)!,
                        plan.CircuitName,
                        plan.InstanceName,
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
                                plan.InstanceName,
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
                        Ac: ac,
                        AcCurrents: acCurrents
                    );
                }
                else if (a.Type == BenchValueType.NoiseAnalysis)
                {
                    var wrdataPath = BenchRuntimePaths.GetNoiseWrdataPath(
                        Path.GetDirectoryName(testbenchPath)!,
                        plan.CircuitName,
                        plan.InstanceName,
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
                else if (a.Type == BenchValueType.SPAnalysis)
                {
                    var wrdataPath = BenchRuntimePaths.GetSpWrdataPath(
                        Path.GetDirectoryName(testbenchPath)!,
                        plan.CircuitName,
                        plan.InstanceName,
                        a.Name
                    );
                    var sp = NgspiceWrdataSpParser.Parse(wrdataPath, plan.NumPorts);
                    SpNoiseDataset? spNoise = null;
                    if (a.EnableNoise)
                    {
                        var nfWrdataPath = BenchRuntimePaths.GetSpNfWrdataPath(
                            Path.GetDirectoryName(testbenchPath)!,
                            plan.CircuitName,
                            plan.InstanceName,
                            a.Name
                        );
                        spNoise = NgspiceWrdataSpParser.ParseNoiseFigure(nfWrdataPath);
                    }

                    analyses[a.Name] = new BenchMeasurementRunner.AnalysisContext(
                        a.Name,
                        a.StartHz,
                        a.StopHz,
                        StartS: 0,
                        StopS: 0,
                        SParameters: sp,
                        SpNoise: spNoise
                    );
                }
                else if (a.Type == BenchValueType.TranAnalysis)
                {
                    var wrdataPath = BenchRuntimePaths.GetTranWrdataPath(
                        Path.GetDirectoryName(testbenchPath)!,
                        plan.CircuitName,
                        plan.InstanceName,
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
                            plan.InstanceName,
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
                        Tran: nodes,
                        TranCurrents: currents
                    );
                }
                else if (a.Type == BenchValueType.PSSAnalysis)
                {
                    analyses[a.Name] = CreatePssAnalysisContext(a, plan, testbenchPath);
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
                dutNodeKeyByPinRef: plan.DutNodeKeyByPinRef,
                dutOpParamsByName: dutOpParamsByName,
                benchMeasurementRefResolver: benchMeasurementRefResolver
            );

            swParse.Stop();
            parseTime = swParse.Elapsed;

            var constraintsForBench = GetNumericConstraintsForBench(circuit, benchName);
            var nodeByMetric = BuildNodeByMetric(constraintsForBench, circuit);

            prepared = new BenchPrepared(
                InstanceName: benchName,
                TestbenchPath: testbenchPath,
                Stderr: run.Stderr,
                Plan: plan,
                Runner: runner,
                ConstraintsForBench: constraintsForBench,
                NodeByMetric: nodeByMetric,
                TracePoints: tracePoints,
                SimulationTime: simulationTime,
                ParseTime: parseTime
            );

            return null;
        }
        catch (Exception ex)
        {
            swParse.Stop();
            parseTime = swParse.Elapsed;
            _logger.LogError(ex, "Failed to prepare bench runner for '{BenchName}'.", benchName);
            Progress($"bench: FAIL {circuit.Name}/{benchName} (parse)");
            return new BenchRunBenchSummary(
                Name: benchName,
                Succeeded: false,
                ExitCode: 1,
                Error: $"Failed to parse outputs: {ex.Message}",
                Stderr: run.Stderr,
                TestbenchPath: testbenchPath,
                TracePath: null,
                ResultsPath: null
            );
        }
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

    private static BenchMeasurementRunner.AnalysisContext CreatePssAnalysisContext(
        BenchPlanAnalysis a,
        BenchPlan plan,
        string testbenchPath
    )
    {
        var wrdataPath = BenchRuntimePaths.GetPssWrdataPath(
            Path.GetDirectoryName(testbenchPath)!,
            plan.CircuitName,
            plan.InstanceName,
            a.Name
        );

        var nodes = NgspiceWrdataPssParser.Parse(wrdataPath, plan.AcNodeKeys);
        if (nodes.TimePoints.Length == 0)
        {
            throw new InvalidOperationException(
                $"PSSAnalysis '{a.Name}' produced no waveform points."
            );
        }

        PssDataset? currents = null;
        var currentSources = plan
            .HarnessElements.Where(e =>
                e.Type.Equals("VDC", StringComparison.OrdinalIgnoreCase)
                || e.Type.Equals("VAC", StringComparison.OrdinalIgnoreCase)
                || e.Type.Equals("VSIN", StringComparison.OrdinalIgnoreCase)
                || e.Type.Equals("Port", StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (plan.RequiresCurrents && currentSources.Count > 0)
        {
            var iWrdataPath = BenchRuntimePaths.GetPssCurrentsWrdataPath(
                Path.GetDirectoryName(testbenchPath)!,
                plan.CircuitName,
                plan.InstanceName,
                a.Name
            );
            var sourceNames = currentSources.Select(s => "V" + s.Id).ToList();
            currents = NgspiceWrdataPssParser.Parse(iWrdataPath, sourceNames);
        }

        return new BenchMeasurementRunner.AnalysisContext(
            a.Name,
            StartHz: 0,
            StopHz: 0,
            StartS: nodes.TimePoints[0],
            StopS: nodes.TimePoints[nodes.TimePoints.Length - 1],
            Ac: null,
            Pss: nodes,
            PssCurrents: currents
        );
    }

    private static IReadOnlyDictionary<string, BenchPlan> BuildPlanMap(CascodeDocument doc)
    {
        var plans = BenchCompiler.CompileAllPlans(doc);
        var map = new Dictionary<string, BenchPlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            map[BuildPlanKey(plan.CircuitName, plan.InstanceName)] = plan;
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
        // Ports may be bundle-desugared into dotted leaves (e.g. "OUT.P", "OUT.N"). Allow
        // constraints to refer to the parent bundle name (e.g. net::OUT).
        if (
            circuit.Ports.Any(p =>
                p.Name.Equals(path, StringComparison.OrdinalIgnoreCase)
                || p.Name.StartsWith(path + ".", StringComparison.OrdinalIgnoreCase)
            )
        )
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

    private static void WriteTracePointsCsv(
        string path,
        IReadOnlyList<BenchResultParser.TracePoint> points,
        IReadOnlyList<MeasurementDefinition> measurementDefinitions
    )
    {
        if (points.Count == 0)
        {
            return;
        }

        var axisNames = points
            .SelectMany(p => p.AxisValues.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var metricNames = measurementDefinitions
            .Where(m => m.Parameters.Count == 0)
            .Select(m => m.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var writer = new StreamWriter(path);
        var header = new List<string>(1 + axisNames.Count + metricNames.Count) { "point_index" };
        header.AddRange(axisNames);
        header.AddRange(metricNames.Select(n => n.ToLowerInvariant()));
        writer.WriteLine(string.Join(',', header));

        foreach (var point in points)
        {
            var byMetric = point.Measurements.ToDictionary(
                m => m.Metric,
                m => m,
                StringComparer.OrdinalIgnoreCase
            );

            var row = new List<string>(header.Count)
            {
                point.Index.ToString(CultureInfo.InvariantCulture),
            };

            foreach (var axisName in axisNames)
            {
                var v = point.AxisValues.TryGetValue(axisName, out var axis) ? axis : double.NaN;
                row.Add(v.ToString("G17", CultureInfo.InvariantCulture));
            }

            foreach (var metric in metricNames)
            {
                if (byMetric.TryGetValue(metric, out var m))
                {
                    row.Add(
                        m.Value?.ToString("G17", CultureInfo.InvariantCulture)
                            ?? double.NaN.ToString("G17", CultureInfo.InvariantCulture)
                    );
                }
                else
                {
                    row.Add(double.NaN.ToString("G17", CultureInfo.InvariantCulture));
                }
            }

            writer.WriteLine(string.Join(',', row));
        }
    }

    private static Dictionary<string, double> BuildOpParamsDictionary(
        NgspiceVectorDataset dataset,
        int index
    )
    {
        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var vectorName in OpParamVectorNames)
        {
            // Strip the "op_" prefix to get the short param name (e.g. "op_gm" → "gm").
            var shortName = vectorName.Substring(3);
            dict[shortName] = dataset.ValuesByName[vectorName][index];
        }
        return dict;
    }

    private static bool TryExtractSeriesValues(BenchValue value, out double[]? values)
    {
        values = value switch
        {
            BenchGainSpectrum spectrum => spectrum.Values,
            BenchScalarSpectrum spectrum => spectrum.Values,
            BenchTimeSpectrum spectrum => spectrum.ValuesS,
            BenchPhaseSpectrum spectrum => spectrum.Degrees,
            BenchNoiseSpectrum spectrum => spectrum.ValuesVPerRtHz,
            BenchVoltageSpectrum spectrum => spectrum.Values,
            BenchCurrentSpectrum spectrum => spectrum.Values,
            BenchWaveform waveform => waveform.Values,
            _ => null,
        };

        return values is not null;
    }
}
