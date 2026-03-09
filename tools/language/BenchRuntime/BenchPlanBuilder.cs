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

        // Convert invocation args plus bench parameter defaults to BenchValue dictionary for use in
        // analysis and harness expression evaluation.
        var benchParams = BuildBenchParams(bench, invocationArgs, evalRunner);

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

    private static IReadOnlyDictionary<string, BenchValue> BuildBenchParams(
        BenchDefinition bench,
        IReadOnlyList<MetricCallArg> invocationArgs,
        BenchMeasurementRunner evalRunner
    )
    {
        var result = new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase);
        var invocationMap = invocationArgs.ToDictionary(
            a => a.Name,
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var parameter in bench.Parameters)
        {
            if (invocationMap.TryGetValue(parameter.Name, out var arg))
            {
                result[parameter.Name] = BenchQuantity.Parse(arg.Value);
                continue;
            }

            if (parameter.Default is not null)
            {
                var value = evalRunner.EvaluateExpressionForPlan(
                    parameter.Default,
                    result
                );
                if (value is BenchMissing)
                {
                    throw new InvalidOperationException(
                        $"Bench '{bench.Name}' parameter '{parameter.Name}' did not resolve."
                    );
                }
                result[parameter.Name] = value;
            }
        }

        foreach (var arg in invocationArgs)
        {
            if (!result.ContainsKey(arg.Name))
            {
                result[arg.Name] = BenchQuantity.Parse(arg.Value);
            }
        }

        return result;
    }
}
