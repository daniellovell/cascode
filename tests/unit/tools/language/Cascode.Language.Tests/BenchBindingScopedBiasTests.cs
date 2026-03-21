using System;
using System.IO;
using System.Linq;
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
        var cascode = """
            VERSION 4.0

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
                numeric {
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
        var cascode = """
            VERSION 4.0

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
                numeric {
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
        var cascode = """
            VERSION 4.0

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
                numeric {
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
        var cascode = """
            VERSION 4.0

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
                numeric {
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
    public void EmitAll_InputDcBinding_SingleEndedOpAmp_EmitsSenseSourcesAndOutputAnchor()
    {
        var cascode = $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit Top implements SingleEndedOpAmp {
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
                numeric {
                  c_iib = input_dc_bench::InputBiasCurrent <= 1nA
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }

              fill { }
            }
            """;

        var tb = EmitTestbench(cascode, instanceName: "input_dc_bench");

        Assert.Contains("VinputCommonModeVDC", tb, StringComparison.Ordinal);
        Assert.Contains("RsourceP", tb, StringComparison.Ordinal);
        Assert.Contains("RsourceN", tb, StringComparison.Ordinal);
        Assert.Contains("VsenseP", tb, StringComparison.Ordinal);
        Assert.Contains("VsenseN", tb, StringComparison.Ordinal);
        Assert.Contains("RoutputAnchor", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_InputDcBinding_FullyDifferentialOpAmp_EmitsCrossCoupledFeedback()
    {
        var cascode = $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit Top implements FullyDifferentialOpAmp {
              level EL
              supply VDD
              ground GND
              input IN : Diff
              output OUT : Diff

              env {
                InputCommonModeRange = 0.9V
                SourceImpedance = 50Ohm
                OutputCommonModeRange = 0.7V
              }

              constraints {
                numeric {
                  c_iib = input_dc_bench::InputBiasCurrent <= 1nA
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }

              fill { }
            }
            """;

        var tb = EmitTestbench(cascode, instanceName: "input_dc_bench");

        Assert.Contains("VoutputCommonModeVDC", tb, StringComparison.Ordinal);
        Assert.Contains("RfeedbackP", tb, StringComparison.Ordinal);
        Assert.Contains("RfeedbackN", tb, StringComparison.Ordinal);
        Assert.Contains("RoutputAnchorP", tb, StringComparison.Ordinal);
        Assert.Contains("RoutputAnchorN", tb, StringComparison.Ordinal);
        Assert.Contains("VsenseP", tb, StringComparison.Ordinal);
        Assert.Contains("VsenseN", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_InputCurrentBinding_SingleEndedAmp_EmitsSenseSourceAndOutputAnchor()
    {
        var cascode = $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit Top implements SingleEndedAmp {
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
                numeric {
                  c_iin = input_current_bench::TerminalCurrent <= 1nA
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }

              fill { }
            }
            """;

        var tb = EmitTestbench(cascode, instanceName: "input_current_bench");

        Assert.Contains("VbiasDC", tb, StringComparison.Ordinal);
        Assert.Contains("RsourceZ", tb, StringComparison.Ordinal);
        Assert.Contains("Vsense", tb, StringComparison.Ordinal);
        Assert.Contains("RoutputAnchor", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_InputDcBinding_MissingSourceImpedance_FailsFast()
    {
        var cascode = $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit Top implements SingleEndedOpAmp {
              level EL
              supply VDD
              ground GND
              input IN : Diff
              output OUT : analog

              env {
                InputCommonModeRange = 0.9V
              }

              constraints {
                numeric {
                  c_iib = input_dc_bench::InputBiasCurrent <= 1nA
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }

              fill { }
            }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            EmitTestbench(cascode, instanceName: "input_dc_bench")
        );

        Assert.Contains(
            "parameter 'source_impedance' did not resolve",
            ex.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void EmitAll_InputDcBinding_FullyDifferentialWithoutOutputCm_FailsFast()
    {
        var cascode = $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit Top implements FullyDifferentialOpAmp {
              level EL
              supply VDD
              ground GND
              input IN : Diff
              output OUT : Diff

              env {
                InputCommonModeRange = 0.9V
                SourceImpedance = 50Ohm
              }

              constraints {
                numeric {
                  c_iib = input_dc_bench::InputBiasCurrent <= 1nA
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }

              fill { }
            }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            EmitTestbench(cascode, instanceName: "input_dc_bench")
        );

        Assert.Contains(
            "parameter 'output_cm' did not resolve",
            ex.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void EmitAll_QuiescentPowerBinding_NoBias_InputNetsAreFloating()
    {
        var cascode = """
            VERSION 4.0

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
                numeric {
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
        using var tmpDir = new TemporaryDirectory();
        CascodeReadResult parsed;
        if (cascode.Contains("include lib.std", StringComparison.Ordinal))
        {
            var entryPath = Path.Combine(tmpDir.Path, "binding_bias.cas");
            File.WriteAllText(entryPath, cascode);

            var outDir = Path.Combine(tmpDir.Path, "linked");
            var repoRoot = TestPathUtilities.GetRepositoryRoot();
            var link = CascodeLinker.LinkFile(entryPath, outDir, repoRoot);
            Assert.True(
                link.Success,
                string.Join(Environment.NewLine, link.Diagnostics.Select(d => d.Message))
            );

            using var linkedReader = new StreamReader(link.LinkedCasPath!);
            parsed = CascodeReader.TryRead(linkedReader, link.LinkedCasPath!);
        }
        else
        {
            parsed = CascodeReader.TryParse(cascode, "binding_bias.cas");
        }

        Assert.True(parsed.Success, parsed.Diagnostics.ToString());

        var designPath = Path.Combine(tmpDir.Path, "Top.sp");
        File.WriteAllText(designPath, "* dummy design deck");

        BenchTestbenchEmitter.EmitAll(
            parsed.Document!,
            tmpDir.Path,
            BenchBackendType.Ngspice,
            designPaths: new[] { designPath }
        );

        var matches = Directory.GetFiles(tmpDir.Path, $"Top_{instanceName}*.sp");
        Assert.True(matches.Length == 1, $"Expected one testbench for '{instanceName}'.");
        var tbPath = matches[0];
        return File.ReadAllText(tbPath);
    }
}
