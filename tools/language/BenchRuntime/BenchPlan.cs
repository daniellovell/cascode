using System.Collections.Generic;
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
    double? StepS = null
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
    IReadOnlyList<string> DutOrderedNets,
    string DutSubcktName,
    IReadOnlyList<string> AcNodeKeys,
    IReadOnlyList<string> DutAcNodeKeys,
    IReadOnlyDictionary<string, string> DutNodeKeyByPinRef,
    BenchNetlist Netlist
);
