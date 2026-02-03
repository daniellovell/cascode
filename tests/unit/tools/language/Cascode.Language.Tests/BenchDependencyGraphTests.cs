using System.Collections.Generic;
using System.Linq;
using Cascode.Language;
using Cascode.Language.BenchRuntime;

namespace Cascode.Language.Tests;

public class BenchDependencyGraphTests
{
    [Fact]
    public void TryParseMeasurementExprText_ParsesBenchMeasurementRef()
    {
        Assert.True(
            CascodeAstBuilder.TryParseMeasurementExprText(
                "transfer_bench::PassbandGain",
                out var expr,
                out var diags
            ),
            string.Join("; ", diags.Select(d => d.Message))
        );

        var r = Assert.IsType<MeasurementBenchMeasurementRef>(expr);
        Assert.Equal("transfer_bench", r.BindingAlias);
        Assert.Equal("PassbandGain", r.MeasurementName);
        Assert.Empty(r.Args);
    }

    [Fact]
    public void Build_ExtractsCrossBenchDependency_AndToposorts()
    {
        var circuit = new Circuit { Name = "C" };

        var constraints = new List<NumericConstraint>
        {
            new()
            {
                Id = "c_cmrr",
                BenchBase = "cmrr_bench",
                Bench = "cmrr_bench",
                Metric = "CMRR",
                MetricArgs = new List<MetricCallArg>(),
                Op = ">=",
                Value = "20",
                Unit = "dB",
            },
        };

        var benchByBindingAlias = new Dictionary<string, BenchDefinition>(
            System.StringComparer.OrdinalIgnoreCase
        )
        {
            ["transfer_bench"] = new BenchDefinition
            {
                Name = "DiffToDiffTransfer",
                Measurements = new List<MeasurementDefinition>
                {
                    new() { Name = "PassbandGain", Unit = "dB" },
                },
            },
            ["cmrr_bench"] = new BenchDefinition
            {
                Name = "DiffCMRejection",
                Measurements = new List<MeasurementDefinition>
                {
                    new()
                    {
                        Name = "CMRR",
                        Unit = "dB",
                        Parameters = new List<TypedParameter>
                        {
                            new(BenchValueType.VoltageRatio, "dmGain"),
                        },
                    },
                },
            },
        };

        var bindingMeasurementExportsByBindingAlias = new Dictionary<
            string,
            IReadOnlyDictionary<string, BenchBindingMeasurementExport>
        >(System.StringComparer.OrdinalIgnoreCase)
        {
            ["cmrr_bench"] = new Dictionary<string, BenchBindingMeasurementExport>(
                System.StringComparer.OrdinalIgnoreCase
            )
            {
                ["CMRR"] = new BenchBindingMeasurementExport(
                    Name: "CMRR",
                    Parameters: new List<TypedParameter>(),
                    Unit: "dB",
                    Target: new MeasurementBenchMeasurementRef(
                        BindingAlias: "base",
                        MeasurementName: "CMRR",
                        Args: new List<BenchMeasurementRefArg>
                        {
                            new(
                                Name: "dmGain",
                                Text: "transfer_bench::PassbandGain",
                                Expr: new MeasurementBenchMeasurementRef(
                                    BindingAlias: "transfer_bench",
                                    MeasurementName: "PassbandGain",
                                    Args: new List<BenchMeasurementRefArg>()
                                )
                            ),
                        }
                    )
                ),
            },
        };

        Assert.True(
            BenchDependencyGraph.TryBuild(
                circuit,
                constraints,
                benchByBindingAlias,
                bindingMeasurementExportsByBindingAlias,
                out var graph,
                out var diags
            ),
            string.Join("; ", diags.Select(d => d.Message))
        );

        var rootId = "cmrr_bench/CMRR";
        var depId = "transfer_bench/PassbandGain";

        Assert.Contains(depId, graph.DependenciesById[rootId]);

        var levels = graph.GetExecutionLevels();
        Assert.Equal(2, levels.Count);
        Assert.Contains(depId, levels[0]);
        Assert.Contains(rootId, levels[1]);
    }

    [Fact]
    public void Build_DetectsCircularBenchDependencies()
    {
        var circuit = new Circuit { Name = "C" };

        var constraints = new List<NumericConstraint>
        {
            new()
            {
                Id = "c_a",
                BenchBase = "a",
                Bench = "a",
                Metric = "M",
                MetricArgs = new List<MetricCallArg> { new("x", "b::Gain") },
            },
            new()
            {
                Id = "c_b",
                BenchBase = "b",
                Bench = "b",
                Metric = "N",
                MetricArgs = new List<MetricCallArg> { new("x", "a::Gain") },
            },
        };

        var benchByBindingAlias = new Dictionary<string, BenchDefinition>(
            System.StringComparer.OrdinalIgnoreCase
        )
        {
            ["a"] = new BenchDefinition
            {
                Name = "A",
                Measurements = new List<MeasurementDefinition>
                {
                    new() { Name = "Gain", Unit = "dB" },
                    new()
                    {
                        Name = "M",
                        Unit = "dB",
                        Parameters = new List<TypedParameter>
                        {
                            new(BenchValueType.VoltageRatio, "x"),
                        },
                    },
                },
            },
            ["b"] = new BenchDefinition
            {
                Name = "B",
                Measurements = new List<MeasurementDefinition>
                {
                    new() { Name = "Gain", Unit = "dB" },
                    new()
                    {
                        Name = "N",
                        Unit = "dB",
                        Parameters = new List<TypedParameter>
                        {
                            new(BenchValueType.VoltageRatio, "x"),
                        },
                    },
                },
            },
        };

        Assert.False(
            BenchDependencyGraph.TryBuild(
                circuit,
                constraints,
                benchByBindingAlias,
                new Dictionary<
                    string,
                    IReadOnlyDictionary<string, BenchBindingMeasurementExport>
                >(),
                out _,
                out var diags
            )
        );
        Assert.Contains(
            diags,
            d => d.Message.StartsWith("CAS3018:", System.StringComparison.Ordinal)
        );
    }
}
