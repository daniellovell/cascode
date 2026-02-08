using System;
using System.Collections.Generic;
using Cascode.Language;
using Cascode.Language.BenchRuntime;

namespace Cascode.Language.Tests;

public sealed class BenchConnectivityBuilderTests
{
    [Fact]
    public void Build_MissingTerminalType_ThrowsCas2024()
    {
        var bench = new BenchDefinition
        {
            Name = "MissingType",
            Terminals = [new BenchTerminal(BenchTerminalRole.Stim, "IN", null)],
        };

        var binding = new BenchBinding { BenchName = "MissingType", BindingName = "missing_type" };
        var bundlesByName = new Dictionary<string, BundleType>(StringComparer.Ordinal);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BenchConnectivityBuilder.Build(bench, binding, bundlesByName)
        );
        Assert.Equal(
            "CAS2024: Concrete bench 'MissingType' has terminal 'IN' without a type.",
            ex.Message
        );
    }

    [Fact]
    public void Build_TypedTerminalMapping_ExpandsLeaves()
    {
        var bench = new BenchDefinition
        {
            Name = "SimpleBench",
            Terminals =
            [
                new BenchTerminal(BenchTerminalRole.Stim, "IN", "analog"),
                new BenchTerminal(BenchTerminalRole.Resp, "OUT", "analog"),
            ],
        };

        var binding = new BenchBinding
        {
            BenchName = "SimpleBench",
            BindingName = "simple_bench",
            Statements =
            [
                new BenchTerminalMapping("IN", "dut.IN"),
                new BenchTerminalMapping("OUT", "dut.OUT"),
            ],
        };
        var bundlesByName = new Dictionary<string, BundleType>(StringComparer.Ordinal);

        var connectivity = BenchConnectivityBuilder.Build(bench, binding, bundlesByName);

        Assert.Contains(
            connectivity.BenchTerminalLeaves,
            leaf => leaf.Equals("IN", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            connectivity.BenchTerminalLeaves,
            leaf => leaf.Equals("OUT", StringComparison.OrdinalIgnoreCase)
        );
    }
}
