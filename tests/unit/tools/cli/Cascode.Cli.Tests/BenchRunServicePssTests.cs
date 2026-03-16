using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cascode.Cli.Services;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Cascode.Language.BenchRuntime.Netlist;
using Cascode.TestSupport;

namespace Cascode.Cli.Tests;

public sealed class BenchRunServicePssTests
{
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
        File.WriteAllText(nodesPath, "0 1.0\n1e-6 0.5\n");

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
    public void CreatePssAnalysisContext_ThrowsWhenCurrentWrdataLengthDiffersFromNodes()
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
        File.WriteAllText(nodesPath, "0 1.0\n1e-6 0.5\n");

        var currentsPath = BenchRuntimePaths.GetPssCurrentsWrdataPath(
            tempDir.Path,
            plan.CircuitName,
            plan.InstanceName,
            analysis.Name
        );
        File.WriteAllText(currentsPath, "0 0.1\n");

        var ex = Assert.Throws<TargetInvocationException>(() =>
            InvokeCreatePssAnalysisContext(analysis, plan, testbenchPath)
        );
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal(
            "PSSAnalysis 'pss' current waveform length (1) does not match node waveform length (2).",
            inner.Message
        );
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

    private static BenchPlan CreatePlan(bool requiresCurrents)
    {
        return new BenchPlan(
            CircuitName: "Top",
            BindingName: "bench",
            InstanceName: "bench",
            InvocationArgs: Array.Empty<MetricCallArg>(),
            BenchName: "PssBench",
            Bench: new BenchDefinition { Name = "PssBench" },
            Binding: new BenchBinding { BenchName = "PssBench", BindingName = "bench" },
            Functions: new Dictionary<string, FunctionDefinition>(StringComparer.OrdinalIgnoreCase),
            Analyses: Array.Empty<BenchPlanAnalysis>(),
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
