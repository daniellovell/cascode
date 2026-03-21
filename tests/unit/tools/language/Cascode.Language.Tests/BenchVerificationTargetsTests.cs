using System.Collections.Generic;
using Cascode.Language.BenchRuntime;

namespace Cascode.Language.Tests;

public sealed class BenchVerificationTargetsTests
{
    [Fact]
    public void CollectVerifiableCircuits_ReturnsOnlyNonInlineElCircuitsWithPlannedInvocations()
    {
        var binding = new BenchBinding { BenchName = "TransferBench", BindingName = "transfer" };
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
            Circuits =
            [
                new Circuit
                {
                    Name = "TopAmp",
                    Level = CascodeLevel.EL,
                    BenchBindings = [binding],
                    Constraints = new ConstraintsBlock
                    {
                        Bench =
                        [
                            new MetricConstraint
                            {
                                Id = "c_gain",
                                BenchBase = "transfer",
                                Bench = "transfer",
                                Metric = "Gain",
                                MetricArgs = new List<MetricCallArg>(),
                                Op = ">=",
                                Value = "20",
                                Unit = "dB",
                            },
                        ],
                    },
                },
                new Circuit
                {
                    Name = "InlineHelper",
                    Level = CascodeLevel.EL,
                    Inline = true,
                    BenchBindings = [binding],
                    Constraints = new ConstraintsBlock
                    {
                        Bench =
                        [
                            new MetricConstraint
                            {
                                Id = "c_inline_gain",
                                BenchBase = "transfer",
                                Bench = "transfer",
                                Metric = "Gain",
                                MetricArgs = new List<MetricCallArg>(),
                                Op = ">=",
                                Value = "10",
                                Unit = "dB",
                            },
                        ],
                    },
                },
                new Circuit
                {
                    Name = "HelperWithoutConstraints",
                    Level = CascodeLevel.EL,
                    BenchBindings = [binding],
                },
                new Circuit { Name = "MlWrapper", Level = CascodeLevel.ML },
            ],
        };

        var circuits = BenchVerificationTargets.CollectVerifiableCircuits(document);

        var circuit = Assert.Single(circuits);
        Assert.Equal("TopAmp", circuit.Name);
    }
}
