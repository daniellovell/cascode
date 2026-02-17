using System;
using System.Collections.Generic;
using Cascode.Bench;
using Cascode.Cli.Services;
using Cascode.Language;
using Xunit;

namespace Cascode.Cli.Tests;

public class BenchResultParserTests
{
    [Fact]
    public void TryParseResultLine_EmptyValue_RecordsAsNaN()
    {
        var circuit = new Circuit { Name = "TestCircuit" };

        var stdout =
            @"
RESULT: PassbandGain =   dB
RESULT: GainBandwidth = 1e6 Hz
";

        var result = BenchResultParser.ParseResults(stdout, circuit, "transfer_bench");

        // Should have both measurements
        Assert.Equal(2, result.Measurements.Count);

        // PassbandGain should be NaN (failed measurement)
        Assert.True(result.Measurements.ContainsKey("PassbandGain"));
        Assert.True(double.IsNaN(result.Measurements["PassbandGain"].Value));
        Assert.Equal("dB", result.Measurements["PassbandGain"].Unit);

        // GainBandwidth should be valid
        Assert.True(result.Measurements.ContainsKey("GainBandwidth"));
        Assert.Equal(1e6, result.Measurements["GainBandwidth"].Value);
        Assert.Equal("Hz", result.Measurements["GainBandwidth"].Unit);
    }

    [Fact]
    public void TryParseResultLine_ValidValue_ParsesCorrectly()
    {
        var circuit = new Circuit { Name = "TestCircuit" };

        var stdout =
            @"
RESULT: PassbandGain = 42.5 dB
RESULT: PhaseMargin = 65.2 deg
";

        var result = BenchResultParser.ParseResults(stdout, circuit, "transfer_bench");

        Assert.Equal(2, result.Measurements.Count);
        Assert.Equal(42.5, result.Measurements["PassbandGain"].Value);
        Assert.Equal("dB", result.Measurements["PassbandGain"].Unit);
        Assert.Equal(65.2, result.Measurements["PhaseMargin"].Value);
        Assert.Equal("deg", result.Measurements["PhaseMargin"].Unit);
    }

    [Fact]
    public void TryParseResultLine_MixedValidAndFailed_ParsesBoth()
    {
        var circuit = new Circuit { Name = "TestCircuit" };

        var stdout =
            @"
RESULT: PassbandGain = 40.2 dB
RESULT: GainBandwidth =   Hz
RESULT: PhaseMargin = 60.5 deg
";

        var result = BenchResultParser.ParseResults(stdout, circuit, "transfer_bench");

        Assert.Equal(3, result.Measurements.Count);
        Assert.Equal(40.2, result.Measurements["PassbandGain"].Value);
        Assert.True(double.IsNaN(result.Measurements["GainBandwidth"].Value));
        Assert.Equal(60.5, result.Measurements["PhaseMargin"].Value);
    }
}
