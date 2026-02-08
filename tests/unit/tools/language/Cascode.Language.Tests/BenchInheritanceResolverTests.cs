using System.IO;
using System.Linq;
using Cascode.Language;

namespace Cascode.Language.Tests;

public sealed class BenchInheritanceResolverTests
{
    [Fact]
    public void TryParse_AbstractBenchInheritance_FlattensConcreteBench()
    {
        var result = Parse(
            """
            abstract bench AbstractTransfer {
              abstract stim IN
              abstract resp OUT

              analysis {
                ACAnalysis ac = new ACAnalysis(start=1Hz, stop=1kHz)
              }

              measurements {
                measurement Gain : dB {
                  return 1dB
                }

                measurement Bandwidth : Hz {
                  return 1Hz
                }
              }
            }

            bench ConcreteTransfer extends AbstractTransfer {
              stim IN : analog
              resp OUT : analog

              override measurement Gain : dB {
                return 2dB
              }

              measurement GroupDelay : Hz {
                return Bandwidth()
              }

              fill { }
            }
            """
        );

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Document);

        var concrete = Assert.Single(result.Document!.BenchDefinitions);
        Assert.Equal("ConcreteTransfer", concrete.Name);
        Assert.False(concrete.IsAbstract);
        Assert.Null(concrete.BaseBench);
        Assert.Single(concrete.Analyses);
        Assert.Equal(3, concrete.Measurements.Count);
        Assert.Contains(concrete.Measurements, m => m.Name == "Gain");
        Assert.Contains(concrete.Measurements, m => m.Name == "Bandwidth");
        Assert.Contains(concrete.Measurements, m => m.Name == "GroupDelay");
        Assert.DoesNotContain(concrete.Measurements, m => m.IsOverride);
    }

    [Fact]
    public void TryParse_ChainedAbstractInheritance_ResolvesTerminalsAndMeasurements()
    {
        var result = Parse(
            """
            abstract bench BaseBench {
              abstract stim IN
              abstract resp OUT

              measurements {
                measurement BaseMetric : dB {
                  return 1dB
                }
              }
            }

            abstract bench MidBench extends BaseBench {
              measurements {
                measurement MidMetric : dB {
                  return BaseMetric()
                }
              }
            }

            bench FinalBench extends MidBench {
              stim IN : analog
              resp OUT : analog
              fill { }
            }
            """
        );

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Document);

        var bench = Assert.Single(result.Document!.BenchDefinitions);
        Assert.Equal("FinalBench", bench.Name);
        Assert.Equal(2, bench.Terminals.Count);
        Assert.All(bench.Terminals, t => Assert.NotNull(t.Type));
        Assert.Equal(2, bench.Measurements.Count);
        Assert.Contains(bench.Measurements, m => m.Name == "BaseMetric");
        Assert.Contains(bench.Measurements, m => m.Name == "MidMetric");
    }

    [Fact]
    public void TryParse_ParameterInheritance_MergesDefaultsAndAddsNewParameters()
    {
        var result = Parse(
            """
            abstract bench AbstractPSRR(Frequency stim_freq = 1kHz) {
              abstract stim IN
              stim PWR : supply
              abstract resp OUT

              measurements {
                measurement Spot : Hz {
                  return stim_freq
                }
              }
            }

            bench ChildPSRR(Frequency stim_freq = 2kHz, Frequency extra = 3kHz) extends AbstractPSRR {
              stim IN : analog
              stim PWR : supply
              resp OUT : analog
              fill { }
            }
            """
        );

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Document);

        var bench = Assert.Single(result.Document!.BenchDefinitions);
        Assert.Equal(2, bench.Parameters.Count);
        Assert.Equal("stim_freq", bench.Parameters[0].Name);
        Assert.Equal("extra", bench.Parameters[1].Name);
        Assert.Equal("2kHz", Assert.IsType<MeasurementQuantity>(bench.Parameters[0].Default!).Raw);
        Assert.Equal(3, bench.Terminals.Count);
        Assert.Contains(bench.Terminals, t => t.Name == "PWR" && t.Type == "supply");
    }

    [Fact]
    public void CascodeWriter_EmitsAbstractExtendsAndOverrideKeywords()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            BenchDefinitions =
            [
                new BenchDefinition
                {
                    Name = "Child",
                    BaseBench = "Base",
                    OverrideAnalysis = true,
                    Terminals =
                    [
                        new BenchTerminal(BenchTerminalRole.Stim, "IN", "analog"),
                        new BenchTerminal(BenchTerminalRole.Resp, "OUT", "analog"),
                    ],
                    Fill = new FillBlock(),
                    Analyses =
                    [
                        new AnalysisDeclaration
                        {
                            Type = BenchValueType.ACAnalysis,
                            Name = "ac",
                            Parameters =
                            {
                                ["start"] = new MeasurementQuantity("1Hz"),
                                ["stop"] = new MeasurementQuantity("1kHz"),
                            },
                        },
                    ],
                    Measurements =
                    [
                        new MeasurementDefinition
                        {
                            Name = "Gain",
                            IsOverride = true,
                            Unit = "dB",
                            Body = [new BenchReturn(new MeasurementQuantity("1dB"))],
                        },
                    ],
                },
            ],
        };

        using var writer = new StringWriter();
        CascodeWriter.Write(doc, writer);
        var text = writer.ToString();

        Assert.Contains("bench Child extends Base", text);
        Assert.Contains("override analysis {", text);
        Assert.Contains("override measurement Gain : dB", text);
    }

    [Theory]
    [MemberData(nameof(DiagnosticCases))]
    public void TryParse_InvalidBenchInheritance_ReportsExpectedDiagnostic(
        string code,
        string source
    )
    {
        var result = Parse(source);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains(code));
    }

    public static TheoryData<string, string> DiagnosticCases =>
        new()
        {
            {
                "CAS2020",
                """
                    bench Child extends Missing {
                      stim IN : analog
                      resp OUT : analog
                      fill { }
                    }
                    """
            },
            {
                "CAS2021",
                """
                    bench Base {
                      stim IN : analog
                      resp OUT : analog
                      fill { }
                    }

                    bench Child extends Base {
                      stim IN : analog
                      resp OUT : analog
                      fill { }
                    }
                    """
            },
            {
                "CAS2022",
                """
                    abstract bench Base {
                      abstract stim IN
                      abstract resp OUT
                    }

                    circuit BoundAbstract {
                      level EL
                      input IN : analog
                      output OUT : analog

                      benches {
                        bind Base as base_bench {
                          bench.IN--dut.IN
                          bench.OUT--dut.OUT
                        }
                      }
                    }
                    """
            },
            {
                "CAS2023",
                """
                    bench NonAbstract {
                      abstract stim IN
                      resp OUT : analog
                      fill { }
                    }
                    """
            },
            {
                "CAS2024",
                """
                    bench MissingType {
                      stim IN
                      resp OUT : analog
                      fill { }
                    }
                    """
            },
            {
                "CAS2024",
                """
                    abstract bench AbstractMissingType {
                      stim IN
                      abstract resp OUT
                    }
                    """
            },
            {
                "CAS2024",
                """
                    abstract bench Base {
                      stim IN : analog
                    }

                    abstract bench Child extends Base {
                      stim IN
                    }
                    """
            },
            {
                "CAS2025",
                """
                    abstract bench Base {
                      abstract stim IN
                      abstract resp OUT
                    }

                    bench Child extends Base {
                      stim IN : analog
                      fill { }
                    }
                    """
            },
            {
                "CAS2026",
                """
                    abstract bench Base {
                      abstract stim IN
                    }

                    bench Child extends Base {
                      resp IN : analog
                      fill { }
                    }
                    """
            },
            {
                "CAS2027",
                """
                    abstract bench InvalidAbstract {
                      abstract stim IN
                      fill { }
                    }
                    """
            },
            {
                "CAS2028",
                """
                    abstract bench Base {
                      abstract stim IN
                    }

                    bench Child extends Base {
                      stim IN : analog
                    }
                    """
            },
            {
                "CAS2029",
                """
                    abstract bench Base {
                      abstract stim IN

                      measurements {
                        measurement A : dB {
                          return 1dB
                        }
                      }
                    }

                    bench Child extends Base {
                      stim IN : analog

                      measurement A : dB {
                        return 2dB
                      }

                      fill { }
                    }
                    """
            },
            {
                "CAS2030",
                """
                    abstract bench A extends B {
                      abstract stim IN
                    }

                    abstract bench B extends A {
                      abstract stim IN
                    }
                    """
            },
            {
                "CAS2031",
                """
                    abstract bench Base {
                      stim IN : analog
                    }

                    bench Child extends Base {
                      stim IN : Diff
                      fill { }
                    }
                    """
            },
            {
                "CAS2032",
                """
                    abstract bench Base {
                      abstract stim IN
                    }

                    bench Child extends Base {
                      stim IN : analog

                      override measurement MissingMetric : dB {
                        return 1dB
                      }

                      fill { }
                    }
                    """
            },
            {
                "CAS2033",
                """
                    abstract bench Base {
                      abstract stim IN
                    }

                    bench Child extends Base {
                      stim IN : analog

                      override analysis {
                        ACAnalysis ac = new ACAnalysis(start=1Hz, stop=10Hz)
                      }

                      fill { }
                    }
                    """
            },
        };

    private static CascodeReadResult Parse(string source)
    {
        var text = $"VERSION {CascodeVersion.Current}\n\n{source}\n";
        return CascodeReader.TryParse(text, "test.cas");
    }
}
