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

        var connectivity = BenchConnectivityBuilder.Build(bench, binding, bundlesByName);
        var harnessCompilation = BenchHarnessCompiler.CompileAndInject(
            circuit,
            binding.BindingName,
            connectivity.Uf,
            connectivity.Instances
        );

        var evalTerminals = new Dictionary<string, BenchTerminalRef>(
            StringComparer.OrdinalIgnoreCase
        );

        // Used for evaluating analysis params and harness instance arguments.
        var evalRunner = new BenchMeasurementRunner(
            bench,
            functions,
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: evalTerminals,
            harnessCompilation.Env,
            harnessCompilation.Harness,
            harnessCompilation.Constraints
        );

        var terminalCompilation = BenchTerminalCompiler.Compile(
            bench,
            circuit,
            bundlesByName,
            connectivity.Uf,
            harnessCompilation.Instances
        );

        foreach (var kvp in terminalCompilation.Terminals)
        {
            evalTerminals[kvp.Key] = kvp.Value;
        }

        var dutSubcktName = SpiceEmitter.GetDefaultVariantName(circuit);

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
            terminalCompilation.DutOrderedNets,
            dutSubcktName,
            terminalCompilation.AcNodeKeys,
            terminalCompilation.DutAcNodeKeys,
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
}
