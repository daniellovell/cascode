using System;
using System.IO;
using Cascode.Bench;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class BenchBindingScopedBiasTests
{
    [Fact]
    public void EmitAll_QuiescentPowerBinding_DiffInput_EmitsCommonModeBias()
    {
        var cascode = $$"""
            VERSION {{CascodeVersion.Current}}

            bench QuiescentPower {
              stim PWR : supply
              resp RET : ground

              measurements {
                measurement QuiescentPower : W {
                  return quiescent_power(PWR, RET)
                }
              }
            }

            circuit Top {
              level EL
              supply VDD
              ground GND
              input IN : Diff
              output OUT : analog

              env {
                InputCommonModeRange = 0.9V
                SourceImpedance = 50Ohm
              }

              constraints {
                bench {
                  c_pwr = vdd_pwr::QuiescentPower <= 1mW
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }

              benches {
                bind QuiescentPower as vdd_pwr {
                  bench.PWR--dut.VDD
                  bench.RET--dut.GND

                  GND g = new GND() { .GND--gnd }
                  VDC commonModeVDC = new VDC(V=env.InputCommonModeRange) { .P--vcm, .N--gnd }
                  Impedor sourceP = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { .P--vcm, .N--dut.IN.P }
                  Impedor sourceN = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { .P--vcm, .N--dut.IN.N }
                }
              }

              fill { }
            }
            """;

        var tb = EmitTestbench(cascode, instanceName: "vdd_pwr");

        Assert.Contains("VcommonModeVDC", tb, StringComparison.Ordinal);
        Assert.Contains("RsourceP", tb, StringComparison.Ordinal);
        Assert.Contains("RsourceN", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_QuiescentPowerBinding_SEInput_EmitsInputBias()
    {
        var cascode = $$"""
            VERSION {{CascodeVersion.Current}}

            bench QuiescentPower {
              stim PWR : supply
              resp RET : ground

              measurements {
                measurement QuiescentPower : W {
                  return quiescent_power(PWR, RET)
                }
              }
            }

            circuit Top {
              level EL
              supply VDD
              ground GND
              input IN : analog
              output OUT : analog

              env {
                InputCommonModeRange = 0.9V
                SourceImpedance = 50Ohm
              }

              constraints {
                bench {
                  c_pwr = vdd_pwr::QuiescentPower <= 1mW
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }

              benches {
                bind QuiescentPower as vdd_pwr {
                  bench.PWR--dut.VDD
                  bench.RET--dut.GND

                  GND g = new GND() { .GND--gnd }
                  VDC biasDC = new VDC(V=env.InputCommonModeRange) { .P--vcm, .N--gnd }
                  Impedor sourceZ = new Impedor(Z=env.SourceImpedance) { .P--vcm, .N--dut.IN }
                }
              }

              fill { }
            }
            """;

        var tb = EmitTestbench(cascode, instanceName: "vdd_pwr");

        Assert.Contains("VbiasDC", tb, StringComparison.Ordinal);
        Assert.Contains("RsourceZ", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_SEDCBiasBinding_DiffInput_EmitsCommonModeBias()
    {
        var cascode = $$"""
            VERSION {{CascodeVersion.Current}}

            bench SEDCBias {
              resp OUT : analog

              analysis {
                DCAnalysis dc = new DCAnalysis()
              }

              fill { }

              measurements {
                measurement OutputDCBias : V {
                  return voltage(dc, OUT)
                }
              }
            }

            circuit Top {
              level EL
              supply VDD
              ground GND
              input IN : Diff
              output OUT : analog

              env {
                InputCommonModeRange = 0.9V
                SourceImpedance = 50Ohm
              }

              constraints {
                bench {
                  c_bias = dc_bias::OutputDCBias <= 1V
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }

              benches {
                bind SEDCBias as dc_bias {
                  bench.OUT--dut.OUT

                  GND g = new GND() { .GND--gnd }
                  VDC commonModeVDC = new VDC(V=env.InputCommonModeRange) { .P--vcm, .N--gnd }
                  Impedor sourceP = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { .P--vcm, .N--dut.IN.P }
                  Impedor sourceN = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { .P--vcm, .N--dut.IN.N }
                }
              }

              fill { }
            }
            """;

        var tb = EmitTestbench(cascode, instanceName: "dc_bias");

        Assert.Contains("VcommonModeVDC", tb, StringComparison.Ordinal);
        Assert.Contains("RsourceP", tb, StringComparison.Ordinal);
        Assert.Contains("RsourceN", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_SEDCBiasBinding_SEInput_EmitsInputBias()
    {
        var cascode = $$"""
            VERSION {{CascodeVersion.Current}}

            bench SEDCBias {
              resp OUT : analog

              analysis {
                DCAnalysis dc = new DCAnalysis()
              }

              fill { }

              measurements {
                measurement OutputDCBias : V {
                  return voltage(dc, OUT)
                }
              }
            }

            circuit Top {
              level EL
              supply VDD
              ground GND
              input IN : analog
              output OUT : analog

              env {
                InputCommonModeRange = 0.9V
                SourceImpedance = 50Ohm
              }

              constraints {
                bench {
                  c_bias = dc_bias::OutputDCBias <= 1V
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }

              benches {
                bind SEDCBias as dc_bias {
                  bench.OUT--dut.OUT

                  GND g = new GND() { .GND--gnd }
                  VDC biasDC = new VDC(V=env.InputCommonModeRange) { .P--vcm, .N--gnd }
                  Impedor sourceZ = new Impedor(Z=env.SourceImpedance) { .P--vcm, .N--dut.IN }
                }
              }

              fill { }
            }
            """;

        var tb = EmitTestbench(cascode, instanceName: "dc_bias");

        Assert.Contains("VbiasDC", tb, StringComparison.Ordinal);
        Assert.Contains("RsourceZ", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_QuiescentPowerBinding_NoBias_InputNetsAreFloating()
    {
        var cascode = $$"""
            VERSION {{CascodeVersion.Current}}

            bench QuiescentPower {
              stim PWR : supply
              resp RET : ground

              measurements {
                measurement QuiescentPower : W {
                  return quiescent_power(PWR, RET)
                }
              }
            }

            circuit Top {
              level EL
              supply VDD
              ground GND
              input IN : Diff
              output OUT : analog

              env {
                InputCommonModeRange = 0.9V
                SourceImpedance = 50Ohm
              }

              constraints {
                bench {
                  c_pwr = vdd_pwr::QuiescentPower <= 1mW
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }

              benches {
                bind QuiescentPower as vdd_pwr {
                  bench.PWR--dut.VDD
                  bench.RET--dut.GND
                }
              }

              fill { }
            }
            """;

        var tb = EmitTestbench(cascode, instanceName: "vdd_pwr");

        // Without binding-scoped bias instances, no VDC biases the input nets.
        Assert.DoesNotContain("VcommonModeVDC", tb, StringComparison.Ordinal);
        Assert.DoesNotContain("VbiasDC", tb, StringComparison.Ordinal);
        Assert.DoesNotContain("vcm", tb, StringComparison.OrdinalIgnoreCase);
    }

    private static string EmitTestbench(string cascode, string instanceName)
    {
        var parsed = CascodeReader.TryParse(cascode, "binding_bias.cas");
        Assert.True(parsed.Success, parsed.Diagnostics.ToString());

        using var tmpDir = new TemporaryDirectory();
        var designPath = Path.Combine(tmpDir.Path, "Top.sp");
        File.WriteAllText(designPath, "* dummy design deck");

        BenchTestbenchEmitter.EmitAll(
            parsed.Document!,
            tmpDir.Path,
            BenchBackendType.Ngspice,
            designPaths: new[] { designPath }
        );

        var tbPath = Path.Combine(tmpDir.Path, $"Top_{instanceName}.sp");
        Assert.True(File.Exists(tbPath), $"Expected testbench '{tbPath}' to be written.");
        return File.ReadAllText(tbPath);
    }
}
