using System.Collections.Generic;
using System.IO;
using Cascode.Language;

namespace Cascode.Language.Tests;

public class CascodeWriterTests
{
    [Fact]
    public void CascodeWriter_ParallelLoad_EmitsCanonicalFormat()
    {
        var circuit = new Circuit
        {
            Name = "Test",
            Level = CascodeLevel.EL,
            Harness = new HarnessBlock
            {
                Loads = new List<LoadValue>
                {
                    new()
                    {
                        Net = "OUT",
                        Elements = new List<LoadElement>
                        {
                            new LoadElement("C", "1pF"),
                            new LoadElement("R", "1MOhm"),
                        },
                    },
                },
            },
        };
        var doc = new CascodeDocument { Circuits = new List<Circuit> { circuit } };
        using var writer = new StringWriter();
        CascodeWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("load OUT (C=1pF || R=1MOhm)", output);
    }

    [Fact]
    public void CascodeWriter_WithBiases_EmitsBiasLines()
    {
        var circuit = new Circuit
        {
            Name = "TestWithBias",
            Level = CascodeLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Ports = new List<PortDeclaration>
            {
                new()
                {
                    Direction = PortDirection.Input,
                    Name = "VTAIL",
                    Type = "bias",
                },
                new()
                {
                    Direction = PortDirection.Output,
                    Name = "OUT",
                    Type = "analog",
                },
            },
            Fill = new FillBlock(),
            Harness = new HarnessBlock
            {
                Supplies = new List<SupplyValue>
                {
                    new() { Net = "VDD", Value = "1.8V" },
                },
                Biases = new List<BiasValue>
                {
                    new() { Net = "VTAIL", Value = "0.7V" },
                    new() { Net = "VBIAS", Value = "0.5V" },
                },
                Loads = new List<LoadValue>
                {
                    new()
                    {
                        Net = "OUT",
                        Elements = new List<LoadElement> { new LoadElement("C", "100fF") },
                    },
                },
            },
        };
        var doc = new CascodeDocument { Circuits = new List<Circuit> { circuit } };
        using var writer = new StringWriter();
        CascodeWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("bias VTAIL = 0.7V", output);
        Assert.Contains("bias VBIAS = 0.5V", output);
        Assert.Contains("supply VDD = 1.8V", output);
        Assert.Contains("load OUT C=100fF", output);
    }

    [Fact]
    public void CascodeWriter_WithSweep_EmitsSweepLine()
    {
        var circuit = new Circuit
        {
            Name = "TestWithSweep",
            Level = CascodeLevel.EL,
            Harness = new HarnessBlock
            {
                Sweeps = new List<SweepCondition>
                {
                    new()
                    {
                        Name = "InputDCBias",
                        Start = "0.3V",
                        Step = "100mV",
                        Stop = "1.5V",
                        IsAuto = false,
                    },
                },
            },
        };
        var doc = new CascodeDocument { Circuits = new List<Circuit> { circuit } };
        using var writer = new StringWriter();
        CascodeWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.Contains("sweep InputDCBias [0.3V:100mV:1.5V]", output);
    }

    [Fact]
    public void CascodeWriter_Constraints_PreserveDeclarationOrder()
    {
        var circuit = new Circuit
        {
            Name = "OrderTest",
            Level = CascodeLevel.EL,
            Constraints = new ConstraintsBlock
            {
                Numeric = new List<NumericConstraint>
                {
                    new()
                    {
                        Id = "z_numeric",
                        BenchBase = "transfer",
                        Metric = "Gain",
                        Op = ">=",
                        Value = "10",
                        Unit = "dB",
                    },
                    new()
                    {
                        Id = "a_numeric",
                        BenchBase = "transfer",
                        Metric = "Bandwidth",
                        Op = ">=",
                        Value = "5M",
                        Unit = "Hz",
                    },
                },
                Tech = new List<TechConstraint>
                {
                    new()
                    {
                        Id = "z_tech",
                        Param = "vdd",
                        Op = "==",
                        Value = "1.8",
                        Unit = "V",
                        Scope = "global",
                    },
                    new()
                    {
                        Id = "a_tech",
                        Param = "temp",
                        Op = "==",
                        Value = "27",
                        Unit = "C",
                        Scope = "global",
                    },
                },
                Graph = new List<GraphConstraint>
                {
                    new()
                    {
                        Id = "z_graph",
                        Rule = "connected",
                        Properties = new Dictionary<string, string>(),
                    },
                    new()
                    {
                        Id = "a_graph",
                        Rule = "acyclic",
                        Properties = new Dictionary<string, string>(),
                    },
                },
            },
        };
        var doc = new CascodeDocument { Circuits = new List<Circuit> { circuit } };
        using var writer = new StringWriter();
        CascodeWriter.Write(doc, writer);
        var output = writer.ToString();

        Assert.True(
            output.IndexOf("z_numeric = transfer::Gain >= 10dB", System.StringComparison.Ordinal)
                < output.IndexOf(
                    "a_numeric = transfer::Bandwidth >= 5MHz",
                    System.StringComparison.Ordinal
                )
        );
        Assert.True(
            output.IndexOf("z_tech : vdd == 1.8V on global", System.StringComparison.Ordinal)
                < output.IndexOf("a_tech : temp == 27C on global", System.StringComparison.Ordinal)
        );
        Assert.True(
            output.IndexOf("z_graph : connected ...", System.StringComparison.Ordinal)
                < output.IndexOf("a_graph : acyclic ...", System.StringComparison.Ordinal)
        );
    }
}
