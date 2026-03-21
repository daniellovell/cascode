using System.Collections.Generic;
using System.Linq;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Cascode.TestSupport;

namespace Cascode.Language.Tests;

public sealed class BenchInvocationPlannerTests
{
    [Fact]
    public void CollectInvocations_PreservesDependencyInvocationArgs()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("bench-invocation-planner");

        var document = new CascodeDocument
        {
            BenchDefinitions =
            [
                new BenchDefinition
                {
                    Name = "DiffToDiffTransfer",
                    Measurements =
                    [
                        new MeasurementDefinition { Name = "PassbandGain", Unit = "dB" },
                    ],
                },
                new BenchDefinition
                {
                    Name = "DiffCMRejection",
                    Measurements = [new MeasurementDefinition { Name = "CMRR", Unit = "dB" }],
                },
            ],
            Circuits = [new Circuit { Name = "Amp" }],
        };

        var circuit = new Circuit
        {
            Name = "Amp",
            BenchBindings =
            [
                new BenchBinding
                {
                    BenchName = "DiffToDiffTransfer",
                    BindingName = "transfer_bench",
                },
                new BenchBinding
                {
                    BenchName = "DiffCMRejection",
                    BindingName = "cmrr_bench",
                    Statements =
                    [
                        new BenchBindingMeasurementExport(
                            Name: "CMRR",
                            Parameters: new List<TypedParameter>(),
                            Unit: "dB",
                            Target: new MeasurementBenchMeasurementRef(
                                BindingAlias: "base",
                                MeasurementName: "CMRR",
                                Args:
                                [
                                    new BenchMeasurementRefArg(
                                        Name: "dmGain",
                                        Text: "transfer_bench::PassbandGain(node=net::OUT)",
                                        Expr: new MeasurementBenchMeasurementRef(
                                            BindingAlias: "transfer_bench",
                                            MeasurementName: "PassbandGain",
                                            Args:
                                            [
                                                new BenchMeasurementRefArg(
                                                    Name: "node",
                                                    Text: "net::OUT",
                                                    Expr: new MeasurementPath("net::OUT")
                                                ),
                                            ]
                                        )
                                    ),
                                ]
                            )
                        ),
                    ],
                },
            ],
            Constraints = new ConstraintsBlock
            {
                Numeric =
                [
                    new NumericConstraint
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
                ],
            },
        };

        var plans = BenchInvocationPlanner.CollectInvocations(document, circuit);

        var transferPlan = Assert.Single(plans, plan => plan.InstanceName == "transfer_bench");
        var arg = Assert.Single(transferPlan.InvocationArgs);
        Assert.Equal("node", arg.Name);
        Assert.Equal("net::OUT", arg.Value);
    }

    [Fact]
    public void CollectInvocations_UsesInheritedInterfaceBenchBindings()
    {
        var document = new CascodeDocument
        {
            BenchDefinitions =
            [
                new BenchDefinition
                {
                    Name = "TransferBench",
                    Measurements = [new MeasurementDefinition { Name = "Gain", Unit = "dB" }],
                },
            ],
            Traits =
            [
                new TraitDefinition
                {
                    Name = "AmpInterface",
                    BenchBindings =
                    [
                        new BenchBinding
                        {
                            BenchName = "TransferBench",
                            BindingName = "transfer_bench",
                        },
                    ],
                },
            ],
        };
        var circuit = new Circuit
        {
            Name = "Amp",
            Traits = ["AmpInterface"],
            Constraints = new ConstraintsBlock
            {
                Numeric =
                [
                    new NumericConstraint
                    {
                        Id = "c_gain",
                        BenchBase = "transfer_bench",
                        Bench = "transfer_bench",
                        Metric = "Gain",
                        MetricArgs = new List<MetricCallArg>(),
                        Op = ">=",
                        Value = "20",
                        Unit = "dB",
                    },
                ],
            },
        };

        var plans = BenchInvocationPlanner.CollectInvocations(document, circuit);

        var plan = Assert.Single(plans);
        Assert.Equal("transfer_bench", plan.Binding.BindingName);
        Assert.Equal("TransferBench", plan.Binding.BenchName);
    }
}
