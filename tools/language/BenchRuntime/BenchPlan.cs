using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language;
using Cascode.Language.BenchRuntime.Netlist;

namespace Cascode.Language.BenchRuntime;

public sealed record BenchPlanAnalysis(
    BenchValueType Type,
    string Name,
    string Space,
    int Samples,
    double StartHz,
    double StopHz,
    double? StartS = null,
    double? StopS = null,
    BenchTerminalRef? OutputTerminal = null,
    string? NoiseInputSource = null,
    double? StepS = null,
    bool EnableNoise = false
);

public sealed record BenchHarnessElement(
    string Type,
    string Id,
    IReadOnlyDictionary<string, string> Pins,
    IReadOnlyDictionary<string, BenchValue> Parameters
);

public sealed record BenchPlan(
    string CircuitName,
    string BindingName,
    string InstanceName,
    IReadOnlyList<MetricCallArg> InvocationArgs,
    string BenchName,
    BenchDefinition Bench,
    BenchBinding Binding,
    IReadOnlyDictionary<string, FunctionDefinition> Functions,
    IReadOnlyList<BenchPlanAnalysis> Analyses,
    IReadOnlyDictionary<string, BenchTerminalRef> Terminals,
    IReadOnlyDictionary<string, BenchValue> Env,
    IReadOnlyDictionary<string, BenchValue> Harness,
    IReadOnlyDictionary<string, BenchValue> Constraints,
    IReadOnlyList<BenchHarnessElement> HarnessElements,
    bool RequiresCurrents,
    bool RequiresOpParams,
    IReadOnlyList<string> DutOrderedNets,
    string DutSubcktName,
    IReadOnlyList<string> AcNodeKeys,
    IReadOnlyList<string> DutAcNodeKeys,
    IReadOnlyDictionary<string, string> DutNodeKeyByPinRef,
    BenchNetlist Netlist
)
{
    public int NumPorts =>
        HarnessElements.Count(e => e.Type.Equals("Port", StringComparison.OrdinalIgnoreCase));
}
