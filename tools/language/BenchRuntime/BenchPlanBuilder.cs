using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.BenchRuntime;

public static class BenchPlanBuilder
{
    public static BenchPlan Build(CascodeDocument document, Circuit circuit, BenchBinding binding)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(binding);

        var bench = document.BenchDefinitions.FirstOrDefault(b =>
            b.Name.Equals(binding.BenchName, StringComparison.OrdinalIgnoreCase)
        );
        if (bench is null)
        {
            throw new InvalidOperationException(
                $"Unknown bench '{binding.BenchName}' for binding '{binding.BindingName}'."
            );
        }

        var bundlesByName = BundleExpander.GetBundlesByName(document);
        var functions = BuildFunctions(document, bench);
        var circuitsByName = document.Circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);

        var connectivity = BenchConnectivityBuilder.Build(bench, binding, bundlesByName);
        var harnessCompilation = BenchHarnessCompiler.CompileAndInject(
            circuit,
            binding.BindingName,
            connectivity.Uf,
            connectivity.Instances
        );

        var terminalCompilation = BenchTerminalCompiler.Compile(
            bench,
            circuit,
            bundlesByName,
            circuitsByName,
            connectivity.Uf,
            harnessCompilation.Instances
        );

        var dutSubcktName = SpiceEmitter.GetDefaultVariantName(circuit);

        // Used for evaluating analysis params and harness instance arguments.
        var evalRunner = new BenchMeasurementRunner(
            bench,
            functions,
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: terminalCompilation.Terminals,
            harnessCompilation.Env,
            harnessCompilation.Harness,
            harnessCompilation.Constraints,
            dutNodeKeyByPinRef: terminalCompilation.DutNodeKeyByPinRef
        );

        var analyses = BenchAnalysisCompiler.Compile(
            bench,
            evalRunner,
            terminalCompilation.Netlist
        );

        var harnessElements = BenchHarnessElementCompiler.CompileHarnessElements(
            harnessCompilation.Instances,
            terminalCompilation.Netlist,
            evalRunner
        );

        // Some parameterized measurements (e.g. OutputSwing(at_freq=...)) require specializing the
        // generated testbench. Today we support a small set of such "plan-time" overrides driven
        // by constraint metric invocation arguments.
        var tranFreqHz = TryInferConstraintFrequencyHz(
            circuit,
            binding.BindingName,
            metric: "OutputSwing",
            argName: "at_freq"
        );
        if (tranFreqHz is not null)
        {
            harnessElements = ApplyTranStimulusFrequencyOverride(harnessElements, tranFreqHz.Value);
            analyses = ApplyTranWindowOverride(analyses, tranFreqHz.Value);
        }

        var requiresCurrents =
            BenchPrimitiveCallFinder.ContainsCall(bench, "current")
            || BenchPrimitiveCallFinder.ContainsCall(bench, "quiescent_power");

        return new BenchPlan(
            circuit.Name,
            binding.BindingName,
            bench.Name,
            bench,
            binding,
            functions,
            analyses,
            terminalCompilation.Terminals,
            harnessCompilation.Env,
            harnessCompilation.Harness,
            harnessCompilation.Constraints,
            harnessElements,
            requiresCurrents,
            terminalCompilation.DutOrderedNets,
            dutSubcktName,
            terminalCompilation.AcNodeKeys,
            terminalCompilation.DutAcNodeKeys,
            terminalCompilation.DutNodeKeyByPinRef,
            terminalCompilation.Netlist
        );
    }

    private static double? TryInferConstraintFrequencyHz(
        Circuit circuit,
        string bindingName,
        string metric,
        string argName
    )
    {
        if (circuit.Constraints?.Numeric is null)
        {
            return null;
        }

        var matches = circuit.Constraints.Numeric.Where(c =>
            c.Bench.Equals(bindingName, StringComparison.OrdinalIgnoreCase)
            && c.Metric.Equals(metric, StringComparison.OrdinalIgnoreCase)
        );

        double? hz = null;
        foreach (var c in matches)
        {
            var arg = c.MetricArgs.FirstOrDefault(a =>
                a.Name.Equals(argName, StringComparison.OrdinalIgnoreCase)
            );
            if (arg is null)
            {
                continue;
            }

            var raw = arg.Value.Trim();
            if (raw.Length == 0)
            {
                continue;
            }

            var parsed = BenchQuantity.Parse(raw) as BenchNumber;
            if (parsed is null || parsed.Kind != BenchNumericKind.FrequencyHz)
            {
                throw new InvalidOperationException(
                    $"Constraint '{c.Id}': {metric}({argName}=...) requires a Frequency value, got '{arg.Value}'."
                );
            }

            if (hz is null)
            {
                hz = parsed.Value;
                continue;
            }

            if (Math.Abs(hz.Value - parsed.Value) > 1e-12 * Math.Max(1.0, Math.Abs(hz.Value)))
            {
                throw new InvalidOperationException(
                    $"Multiple {metric} constraints specify different {argName} values for bench '{bindingName}'. Split into separate runs."
                );
            }
        }

        return hz;
    }

    private static IReadOnlyList<BenchHarnessElement> ApplyTranStimulusFrequencyOverride(
        IReadOnlyList<BenchHarnessElement> elements,
        double freqHz
    )
    {
        var updated = new List<BenchHarnessElement>(elements.Count);
        foreach (var e in elements)
        {
            if (!e.Type.Equals("VSIN", StringComparison.OrdinalIgnoreCase))
            {
                updated.Add(e);
                continue;
            }

            // For now, interpret "at_freq" as the frequency for all VSIN sources in the bench.
            var parameters = new Dictionary<string, BenchValue>(
                e.Parameters,
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["freq"] = new BenchNumber(BenchNumericKind.FrequencyHz, freqHz),
            };
            updated.Add(e with { Parameters = parameters });
        }

        return updated;
    }

    private static IReadOnlyList<BenchPlanAnalysis> ApplyTranWindowOverride(
        IReadOnlyList<BenchPlanAnalysis> analyses,
        double freqHz
    )
    {
        // Capture 10 cycles total and evaluate on the last cycle.
        var stop = 10.0 / freqHz;
        var start = 9.0 / freqHz;

        // Default to 200 points per cycle, but never increase step above what the bench asked for.
        var step = 1.0 / (freqHz * 200.0);

        return analyses
            .Select(a =>
                a.Type != BenchValueType.TranAnalysis
                    ? a
                    : a with
                    {
                        StartS = start,
                        StopS = stop,
                        StepS = a.StepS is null ? step : Math.Min(a.StepS.Value, step),
                    }
            )
            .ToList();
    }

    private static IReadOnlyDictionary<string, FunctionDefinition> BuildFunctions(
        CascodeDocument document,
        BenchDefinition bench
    )
    {
        // Bench-local functions override file-level functions by name.
        var map = new Dictionary<string, FunctionDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var fn in document.Functions)
        {
            map[fn.Name] = fn;
        }
        foreach (var fn in bench.Functions)
        {
            map[fn.Name] = fn;
        }
        return map;
    }
}
