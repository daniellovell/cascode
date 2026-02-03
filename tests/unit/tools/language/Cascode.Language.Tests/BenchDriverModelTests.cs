using System.Collections.Generic;
using Cascode.Language;
using Cascode.Language.BenchRuntime.Netlist;
using Xunit;

namespace Cascode.Language.Tests;

public class BenchDriverModelTests
{
    [Fact]
    public void ShouldInjectGroundTie_WhenNetHasNoSpice0Reference_ReturnsTrue()
    {
        var uf = new BenchUnionFind();
        uf.Ensure(BenchNode.DutTerminal("GND"));

        var model = new BenchDriverModel(uf, []);

        Assert.True(model.ShouldInjectGroundTie(BenchNode.DutTerminal("GND")));
    }

    [Fact]
    public void ShouldInjectGroundTie_WhenNetIsExplicitlyConnectedToSpice0_ReturnsFalse()
    {
        var uf = new BenchUnionFind();
        uf.Union(BenchNode.DutTerminal("GND"), BenchNode.Spice0);

        var model = new BenchDriverModel(uf, []);

        Assert.False(model.ShouldInjectGroundTie(BenchNode.DutTerminal("GND")));
    }

    [Fact]
    public void ShouldInjectGroundTie_WhenNetHasGndTieElement_ReturnsFalse()
    {
        var uf = new BenchUnionFind();
        var inst = new InstanceDeclaration
        {
            Id = "hGND_GND",
            Type = "GND",
            Bindings = new Dictionary<string, string> { ["GND"] = "dut.GND" },
        };

        uf.Union(BenchNode.InstancePin(inst.Id, "GND"), BenchNode.DutTerminal("GND"));

        var model = new BenchDriverModel(uf, [inst]);

        Assert.False(model.ShouldInjectGroundTie(BenchNode.DutTerminal("GND")));
    }

    [Fact]
    public void ShouldInjectGroundTie_WhenNetHasVdcReferencedToSpice0_ReturnsFalse()
    {
        var uf = new BenchUnionFind();
        var inst = new InstanceDeclaration
        {
            Id = "hV_GND",
            Type = "VDC",
            Bindings = new Dictionary<string, string> { ["P"] = "dut.GND", ["N"] = "0" },
        };

        uf.Union(BenchNode.InstancePin(inst.Id, "P"), BenchNode.DutTerminal("GND"));
        uf.Union(BenchNode.InstancePin(inst.Id, "N"), BenchNode.Spice0);

        var model = new BenchDriverModel(uf, [inst]);

        Assert.False(model.ShouldInjectGroundTie(BenchNode.DutTerminal("GND")));
    }

    [Fact]
    public void ShouldInjectSupplyOrBias_WhenNetHasIndependentSource_ReturnsFalse()
    {
        var uf = new BenchUnionFind();
        var inst = new InstanceDeclaration
        {
            Id = "hV_VDD",
            Type = "VDC",
            Bindings = new Dictionary<string, string> { ["P"] = "dut.VDD", ["N"] = "dut.GND" },
        };

        uf.Union(BenchNode.InstancePin(inst.Id, "P"), BenchNode.DutTerminal("VDD"));
        uf.Union(BenchNode.InstancePin(inst.Id, "N"), BenchNode.DutTerminal("GND"));

        var model = new BenchDriverModel(uf, [inst]);

        Assert.False(model.ShouldInjectSupplyOrBias(BenchNode.DutTerminal("VDD")));
    }

    [Fact]
    public void ShouldInjectLoad_WhenNetAlreadyHasLoadElement_ReturnsFalse()
    {
        var uf = new BenchUnionFind();
        var inst = new InstanceDeclaration
        {
            Id = "hLoad_OUT",
            Type = "Impedor",
            Bindings = new Dictionary<string, string> { ["P"] = "dut.OUT", ["N"] = "dut.GND" },
        };

        uf.Union(BenchNode.InstancePin(inst.Id, "P"), BenchNode.DutTerminal("OUT"));
        uf.Union(BenchNode.InstancePin(inst.Id, "N"), BenchNode.DutTerminal("GND"));

        var model = new BenchDriverModel(uf, [inst]);

        Assert.False(model.ShouldInjectLoad(BenchNode.DutTerminal("OUT")));
    }
}
