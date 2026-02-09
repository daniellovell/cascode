using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.BenchRuntime;

public static class BenchPlanBuilder
{
    public static BenchPlan Build(
        CascodeDocument document,
        Circuit circuit,
        BenchBinding binding,
        string instanceName,
        IReadOnlyList<MetricCallArg> invocationArgs
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(instanceName);
        ArgumentNullException.ThrowIfNull(invocationArgs);

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

        // Convert invocation args to BenchValue dictionary for use in expression evaluation.
        var benchParams = ConvertInvocationArgsToBenchParams(invocationArgs);

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
            terminalCompilation.Netlist,
            benchParams
        );

        var harnessElements = BenchHarnessElementCompiler.CompileHarnessElements(
            harnessCompilation.Instances,
            terminalCompilation.Netlist,
            evalRunner,
            benchParams
        );

        var requiresCurrents =
            BenchPrimitiveCallFinder.ContainsCall(bench, "current")
            || BenchPrimitiveCallFinder.ContainsCall(bench, "quiescent_power");
        var requiresOpParams = BenchPrimitiveCallFinder.ContainsCall(bench, "op_param");

        return new BenchPlan(
            circuit.Name,
            binding.BindingName,
            instanceName,
            invocationArgs,
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
            requiresOpParams,
            terminalCompilation.DutOrderedNets,
            dutSubcktName,
            terminalCompilation.AcNodeKeys,
            terminalCompilation.DutAcNodeKeys,
            terminalCompilation.DutNodeKeyByPinRef,
            terminalCompilation.Netlist
        );
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

    private static IReadOnlyDictionary<string, BenchValue> ConvertInvocationArgsToBenchParams(
        IReadOnlyList<MetricCallArg> invocationArgs
    )
    {
        var result = new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in invocationArgs)
        {
            var value = BenchQuantity.Parse(arg.Value);
            result[arg.Name] = value;
        }
        return result;
    }
}
