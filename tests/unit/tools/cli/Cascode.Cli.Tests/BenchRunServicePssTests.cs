using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cascode.Bench;
using Cascode.Cli.Services;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Cascode.Language.BenchRuntime.Netlist;
using Cascode.TestSupport;

namespace Cascode.Cli.Tests;

public sealed class BenchRunServicePssTests
{
    [Fact]
    public void ProbeNgspicePssSupportOrReturnError_ReturnsValidationFailure_WhenResolveThrows()
    {
        var args = new BenchRunService.BenchRunArgs(
            "design.cas",
            null,
            null,
            BenchBackendType.Ngspice,
            false,
            false,
            0
        );
        var result = InvokePrivateStatic(
            "ProbeNgspicePssSupportOrReturnError",
            args,
            Path.GetTempPath(),
            new Func<NgspiceLocator.NgspiceInfo>(() =>
                throw new InvalidOperationException("resolve failed")
            ),
            new Func<string, NgspiceCapabilityProbe.ProbeResult>(_ =>
                throw new InvalidOperationException("probe should not run")
            )
        );

        var validation = Assert.IsType<BenchRunService.MultiCircuitBenchRunResult>(result);
        Assert.Equal(2, validation.ExitCode);
        Assert.Equal(new[] { "resolve failed" }, validation.Summary.ValidationErrors);
    }

    [Fact]
    public void ProbeNgspicePssSupportOrReturnError_ReturnsValidationFailure_WhenProbeThrows()
    {
        var args = new BenchRunService.BenchRunArgs(
            "design.cas",
            null,
            null,
            BenchBackendType.Ngspice,
            false,
            false,
            0
        );
        var result = InvokePrivateStatic(
            "ProbeNgspicePssSupportOrReturnError",
            args,
            Path.GetTempPath(),
            new Func<NgspiceLocator.NgspiceInfo>(() =>
                new NgspiceLocator.NgspiceInfo("/tmp/ngspice", 45, 2)
            ),
            new Func<string, NgspiceCapabilityProbe.ProbeResult>(_ =>
                throw new InvalidOperationException("probe failed")
            )
        );

        var validation = Assert.IsType<BenchRunService.MultiCircuitBenchRunResult>(result);
        Assert.Equal(2, validation.ExitCode);
        Assert.Equal(new[] { "probe failed" }, validation.Summary.ValidationErrors);
    }

    [Fact]
    public void ResolveFinalInstancesToRunForCircuit_ExpandsCircuitQualifiedSelectionAndDependencies()
    {
        var fixture = CreateSelectionFixture();
        var selectedInstances = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            InvokePrivateStatic(
                "ResolveInstancesToRunForCircuit",
                "Top:A",
                fixture.InstanceNames,
                fixture.PlanMap,
                fixture.Circuit.Name
            )
        );
        Assert.Equal(new[] { "A" }, selectedInstances);

        var finalInstances = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            InvokePrivateStatic(
                "ResolveFinalInstancesToRunForCircuit",
                selectedInstances,
                fixture.InstanceNames,
                fixture.Graph
            )
        );
        Assert.Equal(new[] { "A", "B" }, finalInstances);
    }

    [Fact]
    public void CreatePssAnalysisContext_ThrowsWhenCurrentWrdataHasNoPoints()
    {
        using var tempDir = new TemporaryDirectory();
        var analysis = new BenchPlanAnalysis(
            BenchValueType.PSSAnalysis,
            "pss",
            "time",
            Samples: 0,
            StartHz: 0,
            StopHz: 0
        );
        var plan = CreatePlan(requiresCurrents: true);
        var testbenchPath = Path.Combine(tempDir.Path, "Top_bench.sp");
        File.WriteAllText(testbenchPath, "* dummy");

        var nodesPath = BenchRuntimePaths.GetPssWrdataPath(
            tempDir.Path,
            plan.CircuitName,
            plan.InstanceName,
            analysis.Name
        );
        File.WriteAllText(nodesPath, "0 1.0\n1e-6 0.5\n2e-6 0.25\n");

        var currentsPath = BenchRuntimePaths.GetPssCurrentsWrdataPath(
            tempDir.Path,
            plan.CircuitName,
            plan.InstanceName,
            analysis.Name
        );
        File.WriteAllText(currentsPath, string.Empty);

        var ex = Assert.Throws<TargetInvocationException>(() =>
            InvokeCreatePssAnalysisContext(analysis, plan, testbenchPath)
        );
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal("PSSAnalysis 'pss' produced no current waveform points.", inner.Message);
    }

    [Fact]
    public void CreatePssAnalysisContext_ThrowsWhenNodeWrdataHasSinglePoint()
    {
        using var tempDir = new TemporaryDirectory();
        var analysis = new BenchPlanAnalysis(
            BenchValueType.PSSAnalysis,
            "pss",
            "time",
            Samples: 0,
            StartHz: 0,
            StopHz: 0
        );
        var plan = CreatePlan(requiresCurrents: false);
        var testbenchPath = Path.Combine(tempDir.Path, "Top_bench.sp");
        File.WriteAllText(testbenchPath, "* dummy");

        var nodesPath = BenchRuntimePaths.GetPssWrdataPath(
            tempDir.Path,
            plan.CircuitName,
            plan.InstanceName,
            analysis.Name
        );
        File.WriteAllText(nodesPath, "0 1.0\n");

        var ex = Assert.Throws<TargetInvocationException>(() =>
            InvokeCreatePssAnalysisContext(analysis, plan, testbenchPath)
        );
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal("PSSAnalysis 'pss' produced fewer than two waveform points.", inner.Message);
    }

    [Fact]
    public void CreatePssAnalysisContext_ThrowsWhenNodeWrdataContainsNonFiniteTimePoint()
    {
        using var tempDir = new TemporaryDirectory();
        var analysis = new BenchPlanAnalysis(
            BenchValueType.PSSAnalysis,
            "pss",
            "time",
            Samples: 0,
            StartHz: 0,
            StopHz: 0
        );
        var plan = CreatePlan(requiresCurrents: false);
        var testbenchPath = Path.Combine(tempDir.Path, "Top_bench.sp");
        File.WriteAllText(testbenchPath, "* dummy");

        var nodesPath = BenchRuntimePaths.GetPssWrdataPath(
            tempDir.Path,
            plan.CircuitName,
            plan.InstanceName,
            analysis.Name
        );
        File.WriteAllText(nodesPath, "0 1.0\nNaN 0.5\n2e-6 0.25\n");

        var ex = Assert.Throws<TargetInvocationException>(() =>
            InvokeCreatePssAnalysisContext(analysis, plan, testbenchPath)
        );
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal(
            "PSSAnalysis 'pss' produced a non-finite waveform time point at index 1.",
            inner.Message
        );
    }

    [Fact]
    public void CreatePssAnalysisContext_ThrowsWhenNodeWrdataIsNonMonotonic()
    {
        using var tempDir = new TemporaryDirectory();
        var analysis = new BenchPlanAnalysis(
            BenchValueType.PSSAnalysis,
            "pss",
            "time",
            Samples: 0,
            StartHz: 0,
            StopHz: 0
        );
        var plan = CreatePlan(requiresCurrents: true);
        var testbenchPath = Path.Combine(tempDir.Path, "Top_bench.sp");
        File.WriteAllText(testbenchPath, "* dummy");

        var nodesPath = BenchRuntimePaths.GetPssWrdataPath(
            tempDir.Path,
            plan.CircuitName,
            plan.InstanceName,
            analysis.Name
        );
        File.WriteAllText(nodesPath, "0 1.0\n2e-6 0.5\n1e-6 0.25\n");

        var ex = Assert.Throws<TargetInvocationException>(() =>
            InvokeCreatePssAnalysisContext(analysis, plan, testbenchPath)
        );
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal(
            "PSSAnalysis 'pss' produced a non-monotonic waveform time axis at index 2.",
            inner.Message
        );
    }

    [Fact]
    public void CreatePssAnalysisContext_ThrowsWhenCurrentWrdataTimestampsDifferFromNodes()
    {
        using var tempDir = new TemporaryDirectory();
        var analysis = new BenchPlanAnalysis(
            BenchValueType.PSSAnalysis,
            "pss",
            "time",
            Samples: 0,
            StartHz: 0,
            StopHz: 0
        );
        var plan = CreatePlan(requiresCurrents: true);
        var testbenchPath = Path.Combine(tempDir.Path, "Top_bench.sp");
        File.WriteAllText(testbenchPath, "* dummy");

        var nodesPath = BenchRuntimePaths.GetPssWrdataPath(
            tempDir.Path,
            plan.CircuitName,
            plan.InstanceName,
            analysis.Name
        );
        File.WriteAllText(nodesPath, "0 1.0\n1e-6 0.5\n2e-6 0.25\n");

        var currentsPath = BenchRuntimePaths.GetPssCurrentsWrdataPath(
            tempDir.Path,
            plan.CircuitName,
            plan.InstanceName,
            analysis.Name
        );
        File.WriteAllText(currentsPath, "0 0.1\n1.1e-6 0.2\n2e-6 0.3\n");

        var ex = Assert.Throws<TargetInvocationException>(() =>
            InvokeCreatePssAnalysisContext(analysis, plan, testbenchPath)
        );
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("current waveform timestamp at index 1", inner.Message);
        Assert.Contains("does not match node waveform timestamp", inner.Message);
    }

    [Fact]
    public void CreatePssAnalysisContext_PropagatesConfiguredHarmonics()
    {
        using var tempDir = new TemporaryDirectory();
        var analysis = new BenchPlanAnalysis(
            BenchValueType.PSSAnalysis,
            "pss",
            "time",
            Samples: 0,
            StartHz: 0,
            StopHz: 0,
            Harmonics: 7
        );
        var plan = CreatePlan(requiresCurrents: false);
        var testbenchPath = Path.Combine(tempDir.Path, "Top_bench.sp");
        File.WriteAllText(testbenchPath, "* dummy");

        var nodesPath = BenchRuntimePaths.GetPssWrdataPath(
            tempDir.Path,
            plan.CircuitName,
            plan.InstanceName,
            analysis.Name
        );
        File.WriteAllText(nodesPath, "0 1.0\n1e-6 0.5\n");

        var context = Assert.IsType<BenchMeasurementRunner.AnalysisContext>(
            InvokeCreatePssAnalysisContext(analysis, plan, testbenchPath)
        );
        Assert.Equal(7, context.PssHarmonics);
    }

    private static (
        Circuit Circuit,
        IReadOnlyDictionary<string, BenchPlan> PlanMap,
        BenchDependencyGraph Graph,
        IReadOnlyList<string> InstanceNames
    ) CreateSelectionFixture()
    {
        var circuit = new Circuit
        {
            Name = "Top",
            Constraints = new ConstraintsBlock
            {
                Numeric = new List<NumericConstraint>
                {
                    new()
                    {
                        Id = "c_root",
                        BenchBase = "A",
                        Bench = "A",
                        Metric = "Root",
                        MetricArgs = new List<MetricCallArg> { new("dep", "B::Dep") },
                        Op = ">=",
                        Value = "0",
                        Unit = "V",
                    },
                },
            },
        };

        var planA = CreatePlan("Top", "A", "A", "Root", requiresCurrents: false);
        var planB = CreatePlan(
            "Top",
            "B",
            "B",
            "Dep",
            requiresCurrents: false,
            includePssAnalysis: true
        );
        var planMap = new Dictionary<string, BenchPlan>(StringComparer.OrdinalIgnoreCase)
        {
            ["Top:A"] = planA,
            ["Top:B"] = planB,
        };

        var benchByBindingAlias = new Dictionary<string, BenchDefinition>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["A"] = planA.Bench,
            ["B"] = planB.Bench,
        };

        var exportsByBindingAlias = new Dictionary<
            string,
            IReadOnlyDictionary<string, BenchBindingMeasurementExport>
        >(StringComparer.OrdinalIgnoreCase);

        var built = BenchDependencyGraph.TryBuild(
            circuit,
            circuit.Constraints.Numeric,
            benchByBindingAlias,
            exportsByBindingAlias,
            out var graph,
            out var diagnostics
        );
        Assert.True(built);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        return (circuit, planMap, graph, new[] { "A", "B" });
    }

    private static object? InvokeCreatePssAnalysisContext(
        BenchPlanAnalysis analysis,
        BenchPlan plan,
        string testbenchPath
    )
    {
        var method = typeof(BenchRunService).GetMethod(
            "CreatePssAnalysisContext",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.NotNull(method);
        return method.Invoke(null, new object[] { analysis, plan, testbenchPath });
    }

    private static object? InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = typeof(BenchRunService).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.NotNull(method);
        return method.Invoke(null, args);
    }

    private static BenchPlan CreatePlan(bool requiresCurrents) =>
        CreatePlan("Top", "bench", "bench", "OUT", requiresCurrents);

    private static BenchPlan CreatePlan(
        string circuitName,
        string bindingName,
        string instanceName,
        string measurementName,
        bool requiresCurrents,
        bool includePssAnalysis = false
    )
    {
        return new BenchPlan(
            CircuitName: circuitName,
            BindingName: bindingName,
            InstanceName: instanceName,
            InvocationArgs: Array.Empty<MetricCallArg>(),
            BenchName: bindingName,
            Bench: new BenchDefinition
            {
                Name = bindingName,
                Measurements = new List<MeasurementDefinition>
                {
                    new() { Name = measurementName, Unit = "V" },
                },
            },
            Binding: new BenchBinding { BenchName = bindingName, BindingName = bindingName },
            Functions: new Dictionary<string, FunctionDefinition>(StringComparer.OrdinalIgnoreCase),
            Analyses: includePssAnalysis
                ? new List<BenchPlanAnalysis>
                {
                    new(
                        BenchValueType.PSSAnalysis,
                        "pss",
                        "time",
                        Samples: 0,
                        StartHz: 0,
                        StopHz: 0
                    ),
                }
                : Array.Empty<BenchPlanAnalysis>(),
            Terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase),
            Env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            Harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            Constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            HarnessElements:
            [
                new BenchHarnessElement(
                    "VSIN",
                    "vin",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
                ),
            ],
            RequiresCurrents: requiresCurrents,
            RequiresOpParams: false,
            DutOrderedNets: Array.Empty<string>(),
            DutSubcktName: "dut",
            AcNodeKeys: new[] { "OUT" },
            DutAcNodeKeys: Array.Empty<string>(),
            DutNodeKeyByPinRef: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Netlist: new BenchNetlist(
                Array.Empty<BenchNet>(),
                Array.Empty<BenchComponent>(),
                new Dictionary<BenchNode, BenchNetId>(),
                new Dictionary<BenchNetId, BenchNetAttributes>()
            )
        );
    }
}
