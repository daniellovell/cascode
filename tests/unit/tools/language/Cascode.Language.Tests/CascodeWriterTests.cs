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
}
