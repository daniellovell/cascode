using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Language.BenchRuntime;

namespace Cascode.Language.Tests;

public sealed class PssMeasurementTests
{
    [Fact]
    public void Pss_ComputeDurationPowerAndThd()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench PssBuiltinsBench {{
  stim IN : analog
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(fguess=1MHz, tstab=10us, harmonics=5)
  }}

  measurements {{
    measurement Period : s {{
      VoltageWaveform vout = voltage(pss, OUT)
      return duration(vout)
    }}

    measurement CurrentPeriod : s {{
      CurrentWaveform isrc = current(pss, harness.src.P)
      return duration(isrc)
    }}

    measurement MeanVoltage : V {{
      VoltageWaveform vout = voltage(pss, OUT)
      return mean(vout)
    }}

    measurement MeanCurrent : A {{
      CurrentWaveform isrc = current(pss, harness.src.P)
      return mean(isrc)
    }}

    measurement FundamentalPower : W {{
      VoltageWaveform vout = voltage(pss, OUT)
      return harmonic_power(vout, 50Ohm)
    }}

    measurement SecondHarmonicPower : W {{
      VoltageWaveform vout = voltage(pss, OUT)
      return harmonic_power(vout, 50Ohm, 2)
    }}

    measurement Thd2 : Scalar {{
      VoltageWaveform vout = voltage(pss, OUT)
      return thd(vout, 2)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message))
        );

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "PssBuiltinsBench");
        var periodS = 1e-6;
        var samples = 2001;
        var t = new double[samples];
        var v = new double[samples];
        var i = new double[samples];
        for (var n = 0; n < samples; n++)
        {
            var tn = periodS * n / (samples - 1);
            var phase = 2.0 * Math.PI * tn / periodS;
            t[n] = tn;
            v[n] = 0.5 + 2.0 * Math.Sin(phase) + 1.0 * Math.Sin(2.0 * phase);
            i[n] = 0.002 + 0.01 * Math.Sin(phase);
        }

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["pss"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "pss",
                    StartHz: 0,
                    StopHz: 0,
                    StartS: 0,
                    StopS: periodS,
                    Pss: new PssDataset(
                        TimePoints: t,
                        NodeVoltages: new Dictionary<string, double[]>(
                            StringComparer.OrdinalIgnoreCase
                        )
                        {
                            ["OUT"] = v,
                            ["IN"] = new double[samples],
                        }
                    ),
                    PssCurrents: new PssDataset(
                        TimePoints: t,
                        NodeVoltages: new Dictionary<string, double[]>(
                            StringComparer.OrdinalIgnoreCase
                        )
                        {
                            ["Vsrc"] = i,
                        }
                    )
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harnessElements:
            [
                new BenchHarnessElement(
                    Type: "VSIN",
                    Id: "src",
                    Pins: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["P"] = "IN",
                        ["N"] = "0",
                    },
                    Parameters: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
                ),
            ]
        );

        var values = runner.RunMetrics(
            new[]
            {
                "Period",
                "CurrentPeriod",
                "MeanVoltage",
                "MeanCurrent",
                "FundamentalPower",
                "SecondHarmonicPower",
                "Thd2",
            }
        );

        Assert.Equal(periodS, values["Period"].Value, precision: 9);
        Assert.Equal(periodS, values["CurrentPeriod"].Value, precision: 9);
        Assert.Equal(0.5, values["MeanVoltage"].Value, precision: 3);
        Assert.Equal(-0.002, values["MeanCurrent"].Value, precision: 4);
        Assert.Equal(0.04, values["FundamentalPower"].Value, precision: 3);
        Assert.Equal(0.01, values["SecondHarmonicPower"].Value, precision: 3);
        Assert.Equal(0.5, values["Thd2"].Value, precision: 3);
    }

    [Fact]
    public void Pss_RejectInvalidArgumentTypes()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench PssBuiltinTypeErrors {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(fguess=1MHz, tstab=10us, harmonics=3)
  }}

  measurements {{
    measurement BadDuration : s {{
      return duration(1V)
    }}

    measurement BadPower : W {{
      CurrentWaveform iout = current(pss, harness.src.P)
      return harmonic_power(iout, 50Ohm)
    }}

    measurement BadMean : V {{
      return mean(1V)
    }}

    measurement BadThd : Scalar {{
      VoltageWaveform vout = voltage(pss, OUT)
      return thd(vout, 2Hz)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.False(result.Success);

        Assert.Contains(
            result.Diagnostics,
            d => d.Message.Contains("duration first argument must be", StringComparison.Ordinal)
        );
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains(
                    "harmonic_power first argument must be",
                    StringComparison.Ordinal
                )
        );
        Assert.Contains(
            result.Diagnostics,
            d => d.Message.Contains("mean argument must be", StringComparison.Ordinal)
        );
        Assert.Contains(
            result.Diagnostics,
            d => d.Message.Contains("thd second argument must be an Int", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Pss_CurrentReportsPssSpecificMissingVectorError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench PssCurrentMissing {{
  stim IN : analog
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(fguess=1MHz, tstab=10us, harmonics=3)
  }}

  measurements {{
    measurement InputCurrentMax : A {{
      CurrentWaveform iin = current(pss, harness.src.P)
      return iin.Max()
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message))
        );

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "PssCurrentMissing");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["pss"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "pss",
                    StartHz: 0,
                    StopHz: 0,
                    StartS: 0,
                    StopS: 1e-6,
                    Pss: new PssDataset(
                        TimePoints: new[] { 0.0, 1e-6 },
                        NodeVoltages: new Dictionary<string, double[]>(
                            StringComparer.OrdinalIgnoreCase
                        )
                        {
                            ["OUT"] = new[] { 0.0, 0.0 },
                            ["IN"] = new[] { 0.0, 0.0 },
                        }
                    ),
                    PssCurrents: new PssDataset(
                        TimePoints: new[] { 0.0, 1e-6 },
                        NodeVoltages: new Dictionary<string, double[]>(
                            StringComparer.OrdinalIgnoreCase
                        )
                    )
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harnessElements:
            [
                new BenchHarnessElement(
                    Type: "VSIN",
                    Id: "src",
                    Pins: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["P"] = "IN",
                        ["N"] = "0",
                    },
                    Parameters: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
                ),
            ]
        );

        var ex = Assert.Throws<InvalidOperationException>(() =>
            runner.RunMetrics(new[] { "InputCurrentMax" })
        );
        Assert.Contains("current(pss, ...): missing current vector for 'Vsrc'", ex.Message);
    }
}
