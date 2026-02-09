using System.Collections.Generic;
using Cascode.Language.BenchRuntime;

namespace Cascode.Language.Tests;

public sealed class BenchPrimitiveCallFinderTests
{
    [Fact]
    public void ContainsCall_MethodStylePrimitive_MatchesMethodName()
    {
        var expr = new MeasurementMethodCall(
            Receiver: new MeasurementPath("G"),
            Method: "ValueAt",
            Args: new List<MeasurementCallArg>
            {
                new(Name: null, Value: new MeasurementQuantity("1Hz")),
            }
        );

        var bench = CreateBenchReturning(expr);

        Assert.True(BenchPrimitiveCallFinder.ContainsCall(bench, "ValueAt"));
        Assert.True(BenchPrimitiveCallFinder.ContainsCall(bench, "valueat"));
    }

    [Fact]
    public void ContainsCall_FunctionStylePrimitive_MatchesCallName()
    {
        var expr = new MeasurementCall(
            Name: "current",
            Args: new List<MeasurementCallArg>
            {
                new(Name: null, Value: new MeasurementPath("VDD")),
            }
        );

        var bench = CreateBenchReturning(expr);

        Assert.True(BenchPrimitiveCallFinder.ContainsCall(bench, "current"));
    }

    [Fact]
    public void ContainsCall_NestedMethodStylePrimitiveInArgs_IsDetected()
    {
        var nested = new MeasurementMethodCall(
            Receiver: new MeasurementPath("G"),
            Method: "FindCrossing",
            Args:
            [
                new(Name: null, Value: new MeasurementQuantity("0dB")),
                new(Name: "dir", Value: new MeasurementPath("falling")),
            ]
        );

        var expr = new MeasurementCall(
            Name: "abs",
            Args: new List<MeasurementCallArg> { new(Name: null, Value: nested) }
        );

        var bench = CreateBenchReturning(expr);

        Assert.True(BenchPrimitiveCallFinder.ContainsCall(bench, "FindCrossing"));
    }

    [Fact]
    public void ContainsCall_WhenNameAbsent_ReturnsFalse()
    {
        var expr = new MeasurementCall(
            Name: "current",
            Args: new List<MeasurementCallArg>
            {
                new(Name: null, Value: new MeasurementPath("VDD")),
            }
        );

        var bench = CreateBenchReturning(expr);

        Assert.False(BenchPrimitiveCallFinder.ContainsCall(bench, "NotAThing"));
    }

    private static BenchDefinition CreateBenchReturning(MeasurementExpr expr)
    {
        return new BenchDefinition
        {
            Name = "TestBench",
            Measurements =
            [
                new MeasurementDefinition
                {
                    Name = "M",
                    Unit = "Hz",
                    Body = [new BenchReturn(expr)],
                },
            ],
        };
    }
}
