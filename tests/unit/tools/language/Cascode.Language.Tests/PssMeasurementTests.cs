using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Cascode.TestSupport;

namespace Cascode.Language.Tests;

public sealed class PssMeasurementTests
{
    [Fact]
    public void Pss_SupplyPower_UsesExplicitSupplyVoltageArgument()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("pss-supply-power");
        var entryPath = Path.Combine(cascodeHome.Path, "entry.cas");
        var outDir = Path.Combine(cascodeHome.Path, "out");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            bench WrapperPss extends AbstractOutputPSS {
              resp OUT : analog

              fill { }
            }
            """
        );

        var link = CascodeLinker.LinkFile(entryPath, outDir, repoRoot);
        Assert.True(
            link.Success,
            string.Join(Environment.NewLine, link.Diagnostics.Select(d => d.Message))
        );
        using var reader = File.OpenText(link.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, link.LinkedCasPath!);
        var bench = linked.BenchDefinitions.Single(b => b.Name == "WrapperPss");

        var runner = new BenchMeasurementRunner(
            bench,
            functions: linked.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["VDD"] = new BenchNumber(BenchNumericKind.VoltageV, 1.8),
                ["VPWR"] = new BenchNumber(BenchNumericKind.VoltageV, 2.5),
            },
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harnessElements: []
        );

        var value = runner.RunMetricWithNamedArgs(
            "SupplyPower",
            new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["supplyVoltage"] = new BenchNumber(BenchNumericKind.Scalar, 2.5),
                ["dcCurrent"] = new BenchNumber(BenchNumericKind.Scalar, -0.002),
            }
        );
        Assert.Equal(0.005, value.Value, precision: 9);
    }

    [Fact]
    public void Pss_SeoOscOutputPower_UsesHarnessDefaultLoadWhenEnvMissing()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("pss-seosc-output-power");
        var entryPath = Path.Combine(cascodeHome.Path, "entry.cas");
        var outDir = Path.Combine(cascodeHome.Path, "out");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit SeoOscTop {
              level EL
              output OUT : analog
              ground GND

              constraints {
                numeric {
                  c_out = pss_bench::OutputPower >= 0W
                }
              }

              benches {
                bind SEOscPSS as pss_bench {
                  bench.OUT--dut.OUT
                }
              }

              harness {
                ground GND = 0V
              }

              fill { }
            }
            """
        );

        var link = CascodeLinker.LinkFile(entryPath, outDir, repoRoot);
        Assert.True(
            link.Success,
            string.Join(Environment.NewLine, link.Diagnostics.Select(d => d.Message))
        );
        using var reader = File.OpenText(link.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, link.LinkedCasPath!);
        var bench = linked.BenchDefinitions.Single(b => b.Name == "SEOscPSS");

        const double periodS = 1e-6;
        const int samples = 2001;
        var t = new double[samples];
        var vout = new double[samples];
        for (var n = 0; n < samples; n++)
        {
            var tn = periodS * n / (samples - 1);
            var phase = 2.0 * Math.PI * tn / periodS;
            t[n] = tn;
            vout[n] = 2.0 * Math.Sin(phase);
        }

        var runner = new BenchMeasurementRunner(
            bench,
            functions: linked.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
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
                            ["OUT"] = vout,
                        }
                    )
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harnessElements: []
        );

        var values = runner.RunMetrics(new[] { "OutputPower" });
        Assert.Equal(2e-9, values["OutputPower"].Value, precision: 12);
    }

    [Fact]
    public void Pss_DiffToDiffOutputPower_UsesHarnessDefaultLoadWhenEnvMissing()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("pss-d2d-output-power");
        var entryPath = Path.Combine(cascodeHome.Path, "entry.cas");
        var outDir = Path.Combine(cascodeHome.Path, "out");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit DiffToDiffTop {
              level EL
              input IN : Diff
              output OUT : Diff
              ground GND

              constraints {
                numeric {
                  c_out = pss_bench::OutputPower >= 0W
                }
              }

              benches {
                bind DiffToDiffPSS as pss_bench {
                  bench.IN--dut.IN
                  bench.OUT--dut.OUT
                }
              }

              harness {
                ground GND = 0V
              }

              fill { }
            }
            """
        );

        var link = CascodeLinker.LinkFile(entryPath, outDir, repoRoot);
        Assert.True(
            link.Success,
            string.Join(Environment.NewLine, link.Diagnostics.Select(d => d.Message))
        );
        using var reader = File.OpenText(link.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, link.LinkedCasPath!);
        var bench = linked.BenchDefinitions.Single(b => b.Name == "DiffToDiffPSS");

        const double periodS = 1e-6;
        const int samples = 2001;
        var t = new double[samples];
        var voutP = new double[samples];
        var voutN = new double[samples];
        for (var n = 0; n < samples; n++)
        {
            var tn = periodS * n / (samples - 1);
            var phase = 2.0 * Math.PI * tn / periodS;
            t[n] = tn;
            voutP[n] = 2.0 * Math.Sin(phase);
            voutN[n] = 0.0;
        }

        var runner = new BenchMeasurementRunner(
            bench,
            functions: linked.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
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
                            ["OUTP"] = voutP,
                            ["OUTN"] = voutN,
                        }
                    )
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUTP", "OUTN" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harnessElements: []
        );

        var values = runner.RunMetrics(new[] { "OutputPower" });
        Assert.Equal(0.02, values["OutputPower"].Value, precision: 9);
    }

    [Fact]
    public void Pss_ComputeDurationPowerAndThd()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench PssBuiltinsBench {{
  stim IN : analog
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1MHz, stabilization_time=10us, harmonics=5)
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
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1MHz, stabilization_time=10us, harmonics=3)
  }}

  measurements {{
    measurement BadDuration : s {{
      return duration(1V)
    }}

    measurement BadPower : W {{
      CurrentWaveform iout = current(pss, harness.src.P)
      return harmonic_power(iout, 50Ohm)
    }}

    measurement BadHarmonicIndex : W {{
      VoltageWaveform vout = voltage(pss, OUT)
      return harmonic_power(vout, 50Ohm, 0.5)
    }}

    measurement BadMean : V {{
      return mean(1V)
    }}

    measurement BadThd : Scalar {{
      VoltageWaveform vout = voltage(pss, OUT)
      return thd(vout, 0.5)
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
            d =>
                d.Message.Contains(
                    "harmonic_power third argument must be an Int",
                    StringComparison.Ordinal
                )
        );
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains(
                    "thd second argument must be an integer scalar",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void Pss_HarmonicPowerSupportsCurrentWaveformOverload()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench HarmonicPowerWaveformOverload {{
  measurements {{
    measurement AvailablePower(VoltageWaveform vin, Impedance zs) : W {{
      return harmonic_power(vin, zs)
    }}

    measurement DeliveredPower(VoltageWaveform vin, CurrentWaveform iin) : W {{
      return harmonic_power(vin, iin)
    }}

    measurement DeliveredSecondHarmonicPower(VoltageWaveform vin, CurrentWaveform iin) : W {{
      return harmonic_power(vin, iin, 2)
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

        var bench = result.Document!.BenchDefinitions.Single(b =>
            b.Name == "HarmonicPowerWaveformOverload"
        );
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase),
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        const double periodS = 1e-6;
        const int samples = 2001;
        var t = new double[samples];
        var vin = new double[samples];
        var iin = new double[samples];
        for (var n = 0; n < samples; n++)
        {
            var tn = periodS * n / (samples - 1);
            var phase = 2.0 * Math.PI * tn / periodS;
            t[n] = tn;
            vin[n] = Math.Sin(phase) + 0.5 * Math.Sin(2.0 * phase);
            iin[n] = 0.01 * Math.Sin(phase) + 0.004 * Math.Sin(2.0 * phase);
        }

        var vinWaveform = new BenchWaveform(t, vin, BenchNumericKind.VoltageV);
        var iinWaveform = new BenchWaveform(t, iin, BenchNumericKind.CurrentA);
        var zSource = new BenchImpedanceParallel(
            new[] { new BenchNumber(BenchNumericKind.ImpedanceOhm, 50.0) }
        );

        var availablePower = runner.RunMetricWithNamedArgs(
            "AvailablePower",
            new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["vin"] = vinWaveform,
                ["zs"] = zSource,
            }
        );
        var deliveredPower = runner.RunMetricWithNamedArgs(
            "DeliveredPower",
            new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["vin"] = vinWaveform,
                ["iin"] = iinWaveform,
            }
        );
        var deliveredSecondHarmonicPower = runner.RunMetricWithNamedArgs(
            "DeliveredSecondHarmonicPower",
            new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["vin"] = vinWaveform,
                ["iin"] = iinWaveform,
            }
        );

        Assert.Equal(0.01, availablePower.Value, precision: 4);
        Assert.Equal(0.005, deliveredPower.Value, precision: 4);
        Assert.Equal(0.001, deliveredSecondHarmonicPower.Value, precision: 4);
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
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1MHz, stabilization_time=10us, harmonics=3)
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

    [Fact]
    public void Pss_HarmonicPowerRejectsMalformedWaveformSamples()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench PssMalformedWaveform {{
  measurements {{
    measurement FundamentalPower(VoltageWaveform vout) : W {{
      return harmonic_power(vout, 50Ohm)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "PssMalformedWaveform");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase),
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() =>
            runner.RunMetricWithNamedArgs(
                "FundamentalPower",
                new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["vout"] = new BenchWaveform(
                        new[] { 0.0, 1e-6 },
                        new[] { 0.25 },
                        BenchNumericKind.VoltageV
                    ),
                }
            )
        );
        Assert.Contains("equal length and at least two samples", ex.Message);
    }

    [Fact]
    public void Pss_HarmonicPowerRejectsNonStrictlyIncreasingTimePoints()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench PssNonIncreasingTime {{
  measurements {{
    measurement FundamentalPower(VoltageWaveform vout) : W {{
      return harmonic_power(vout, 50Ohm)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "PssNonIncreasingTime");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase),
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() =>
            runner.RunMetricWithNamedArgs(
                "FundamentalPower",
                new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["vout"] = new BenchWaveform(
                        new[] { 0.0, 1e-6, 1e-6, 2e-6 },
                        new[] { 0.0, 0.5, 0.5, 1.0 },
                        BenchNumericKind.VoltageV
                    ),
                }
            )
        );
        Assert.Contains("waveform time points must be strictly increasing", ex.Message);
        Assert.Contains("'0'", ex.Message);
    }
}
