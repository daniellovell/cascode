using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Cascode.Language.BenchRuntime;

namespace Cascode.Language.Tests;

public sealed class BenchMeasurementRunnerTests
{
    [Fact]
    public void Db20_FloorsZeroMagnitude_ToFiniteValue()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench DbFloorBench {{
  stim IN : analog
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=1, start=1Hz, stop=1Hz)
  }}

  measurements {{
    measurement GainDb : dB {{
      TransferFunction H = transfer(ac, IN, OUT)
      GainSpectrum G = db20(H.Mag())
      return G.ValueAt(1Hz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "DbFloorBench");
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0 },
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["IN"] = new[] { new System.Numerics.Complex(1.0, 0.0) },
                ["OUT"] = new[] { new System.Numerics.Complex(0.0, 0.0) },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 1,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "GainDb" });
        Assert.Equal(-300.0, values["GainDb"].Value);
        Assert.Equal("dB", values["GainDb"].Unit);
    }

    [Fact]
    public void RunMetrics_QuiescentPower_UsesVdcCurrentAndVoltage()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench PowerBench {{
  stim PWR : supply
  resp RET : ground

  measurements {{
    measurement QuiescentPower : W {{
      return quiescent_power(PWR, RET)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "PowerBench");
        var harnessElements = new[]
        {
            new BenchHarnessElement(
                Type: "VDC",
                Id: "hV_VDD",
                Pins: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["P"] = "VDD",
                    ["N"] = "GND",
                },
                Parameters: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["V"] = new BenchNumber(BenchNumericKind.VoltageV, 1.8),
                }
            ),
        };
        var currents = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            // ngspice convention: current drawn from the source is negative, so -I is positive.
            ["VhV_VDD"] = -1e-3,
        };

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["PWR"] = new BenchTerminalRef("PWR", new[] { "VDD" }),
                ["RET"] = new BenchTerminalRef("RET", new[] { "GND" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harnessElements: harnessElements,
            sourceCurrentsByName: currents
        );

        var values = runner.RunMetrics(new[] { "QuiescentPower" });
        Assert.Equal(1.8e-3, values["QuiescentPower"].Value, precision: 12);
        Assert.Equal("W", values["QuiescentPower"].Unit);
    }

    [Fact]
    public void RunMetrics_VoltageDc_ReturnsScalarVoltageFromOperatingPoint()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench DcVoltageBench {{
  resp OUT : analog

  analysis {{
    DCAnalysis dc = new DCAnalysis()
  }}

  measurements {{
    measurement OutputDCBias : V {{
      return voltage(dc, OUT)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "DcVoltageBench");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["dc"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "dc",
                    StartHz: 0,
                    StopHz: 0,
                    StartS: 0,
                    StopS: 0,
                    Op: new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["OUT"] = 0.72,
                    }
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "OutputDCBias" });
        Assert.Equal(0.72, values["OutputDCBias"].Value, precision: 12);
        Assert.Equal("V", values["OutputDCBias"].Unit);
    }

    [Fact]
    public void RunAll_AllowsZeroArgMeasurementCalls()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench TestBench {{
  measurements {{
    measurement A : Hz {{
      return 1Hz
    }}

    measurement B : Hz {{
      return A() + 1Hz
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "TestBench");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.Ordinal),
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunAll();
        Assert.Equal(2.0, values["B"].Value);
        Assert.Equal("Hz", values["B"].Unit);
    }

    [Fact]
    public void RunAll_MeasurementCallWithArgs_Throws()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench TestBench {{
  measurements {{
    measurement A : Hz {{
      return 1Hz
    }}

    measurement Bad : Hz {{
      return A(1Hz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "TestBench");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.Ordinal),
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("does not accept arguments", ex.Message);
    }

    [Fact]
    public void Env_Impedance_AllowsParensAndParallelExpr()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

circuit EnvImpedanceSmoke {{
  level EL
  ground GND

  fill {{ }}

  env {{
    SourceImpedance = 50Ohm
    LoadImpedance = (1GOhm || 15pF)
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message))
        );

        var circuit = Assert.Single(result.Document!.Circuits);
        var uf = new Cascode.Language.BenchRuntime.Netlist.BenchUnionFind();
        var compiled = BenchHarnessCompiler.CompileAndInject(
            circuit,
            bindingName: "any",
            uf,
            baseInstances: Array.Empty<InstanceDeclaration>()
        );

        Assert.True(compiled.Env.TryGetValue("SourceImpedance", out var source));
        var sourceZ = Assert.IsType<BenchImpedanceParallel>(source);
        Assert.Single(sourceZ.Elements);
        Assert.Equal(BenchNumericKind.ImpedanceOhm, sourceZ.Elements[0].Kind);

        Assert.True(compiled.Env.TryGetValue("LoadImpedance", out var load));
        var loadZ = Assert.IsType<BenchImpedanceParallel>(load);
        Assert.Equal(2, loadZ.Elements.Count);
        Assert.Contains(loadZ.Elements, e => e.Kind == BenchNumericKind.ImpedanceOhm);
        Assert.Contains(loadZ.Elements, e => e.Kind == BenchNumericKind.CapacitanceF);
    }

    [Fact]
    public void Impedance_Methods_ImplementHalfCircuitConversions()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench ZBench {{
  measurements {{
    measurement Dummy : Hz {{
      return 1Hz
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "ZBench");
        var env = new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["LoadImpedance"] = new BenchImpedanceParallel(
                new[]
                {
                    new BenchNumber(BenchNumericKind.ImpedanceOhm, 1e9),
                    new BenchNumber(BenchNumericKind.CapacitanceF, 15e-12),
                }
            ),
        };

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase),
            env,
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        Assert.True(
            CascodeAstBuilder.TryParseMeasurementExprText(
                "env.LoadImpedance.DiffToShunt()",
                out var diffToShunt,
                out _
            )
        );
        var zShunt = Assert.IsType<BenchImpedanceParallel>(
            runner.EvaluateExpressionForPlan(diffToShunt!)
        );
        Assert.Contains(
            zShunt.Elements,
            e => e.Kind == BenchNumericKind.ImpedanceOhm && e.Value == 5e8
        );
        Assert.Contains(
            zShunt.Elements,
            e => e.Kind == BenchNumericKind.CapacitanceF && e.Value == 30e-12
        );

        Assert.True(
            CascodeAstBuilder.TryParseMeasurementExprText(
                "env.LoadImpedance.DiffToShunt().ShuntToDiff()",
                out var roundTrip,
                out _
            )
        );
        var zRoundTrip = Assert.IsType<BenchImpedanceParallel>(
            runner.EvaluateExpressionForPlan(roundTrip!)
        );
        Assert.Contains(
            zRoundTrip.Elements,
            e => e.Kind == BenchNumericKind.ImpedanceOhm && e.Value == 1e9
        );
        Assert.Contains(
            zRoundTrip.Elements,
            e => e.Kind == BenchNumericKind.CapacitanceF && e.Value == 15e-12
        );
    }

    [Fact]
    public void BindMeasurementArguments_SingleUnexpectedNamedArg_Throws()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench TestBench {{
  measurements {{
    measurement M(Frequency f) : Hz {{
      return f
    }}

    measurement Caller : Hz {{
      return M(f=1Hz, frq=2Hz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "TestBench");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.Ordinal),
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Unexpected argument(s) 'frq'", ex.Message);
        Assert.Contains("measurement 'M'", ex.Message);
    }

    [Fact]
    public void BindMeasurementArguments_MultipleUnexpectedNamedArgs_Throws()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench TestBench {{
  measurements {{
    measurement M(Frequency f) : Hz {{
      return f
    }}

    measurement Caller : Hz {{
      return M(f=1Hz, extra=2Hz, other=3Hz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "TestBench");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.Ordinal),
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Unexpected argument(s)", ex.Message);
        Assert.Contains("'extra'", ex.Message);
        Assert.Contains("'other'", ex.Message);
        Assert.Contains("measurement 'M'", ex.Message);
    }

    [Fact]
    public void BindCallArguments_UnexpectedNamedArg_Throws()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

function calc(Frequency f) : Frequency {{
  return f
}}

bench TestBench {{
  measurements {{
    measurement Caller : Hz {{
      return calc(f=1Hz, frq=2Hz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "TestBench");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.Ordinal),
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Unexpected argument(s) 'frq'", ex.Message);
        Assert.Contains("function 'calc'", ex.Message);
    }

    [Fact]
    public void BindMeasurementArguments_MixedPositionalAndNamedWithTypo_Throws()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench TestBench {{
  measurements {{
    measurement M(Frequency a, Frequency b) : Hz {{
      return a + b
    }}

    measurement Caller : Hz {{
      return M(1Hz, b=2Hz, bb=3Hz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "TestBench");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.Ordinal),
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Unexpected argument(s) 'bb'", ex.Message);
        Assert.Contains("measurement 'M'", ex.Message);
    }

    [Fact]
    public void BindMeasurementArguments_ValidUsage_DoesNotThrow()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

function add(Frequency a, Frequency b) : Frequency {{
  return a + b
}}

bench TestBench {{
  measurements {{
    measurement M(Frequency f1, Frequency f2) : Hz {{
      return f1 + f2
    }}

    measurement AllPositional : Hz {{
      return M(1Hz, 2Hz)
    }}

    measurement AllNamed : Hz {{
      return M(f1=3Hz, f2=4Hz)
    }}

    measurement Mixed : Hz {{
      return M(5Hz, f2=6Hz)
    }}

    measurement WithFunction : Hz {{
      return add(7Hz, 8Hz) + add(a=9Hz, b=10Hz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "TestBench");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.Ordinal),
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunAll();
        Assert.Equal(3.0, values["AllPositional"].Value);
        Assert.Equal(7.0, values["AllNamed"].Value);
        Assert.Equal(11.0, values["Mixed"].Value);
        Assert.Equal(34.0, values["WithFunction"].Value);
    }

    [Fact]
    public void OpParam_AllowsParameterNameThatMatchesMeasurementName()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench OpParamBench {{
  analysis {{
    DCAnalysis dc = new DCAnalysis()
  }}

  measurements {{
    measurement Gm : S {{
      return op_param(dc, dut, gm)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "OpParamBench");
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["dc"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "dc",
                    StartHz: 0,
                    StopHz: 0,
                    StartS: 0,
                    StopS: 0,
                    Ac: null
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase),
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            dutOpParamsByName: new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["gm"] = 1.23e-3,
            }
        );

        var values = runner.RunMetrics(new[] { "Gm" });
        Assert.Equal(1.23e-3, values["Gm"].Value, precision: 12);
        Assert.Equal("S", values["Gm"].Unit);
    }

    [Fact]
    public void ComplexSpectra_RequireExplicitMagnitude_ForScalarMeasurements()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench ExplicitMagnitudeBench {{
  stim VDD : supply
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=100Hz)
  }}

  measurements {{
    measurement VoltageMagFromSpectrum : V {{
      return voltage(ac, OUT).Mag().ValueAt(10Hz)
    }}

    measurement VoltageMagFromPoint : V {{
      return voltage(ac, OUT).ValueAt(10Hz).Mag()
    }}

    measurement VoltagePhaseFromSpectrum : deg {{
      return voltage(ac, OUT).Phase().ValueAt(10Hz)
    }}

    measurement VoltagePhaseFromPoint : deg {{
      return voltage(ac, OUT).ValueAt(10Hz).Phase()
    }}

    measurement CurrentMagFromSpectrum : A {{
      return current(ac, harness.VDD.P).Mag().ValueAt(10Hz)
    }}

    measurement CurrentMagFromPoint : A {{
      return current(ac, harness.VDD.P).ValueAt(10Hz).Mag()
    }}

    measurement CurrentPhaseFromSpectrum : deg {{
      return current(ac, harness.VDD.P).Phase().ValueAt(10Hz)
    }}

    measurement CurrentPhaseFromPoint : deg {{
      return current(ac, harness.VDD.P).ValueAt(10Hz).Phase()
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
            b.Name == "ExplicitMagnitudeBench"
        );
        var frequencies = new[] { 1.0, 100.0 };
        var acVoltage = new AcDataset(
            FrequenciesHz: frequencies,
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["OUT"] = new[]
                {
                    new System.Numerics.Complex(1.0, 0.0),
                    new System.Numerics.Complex(0.0, 1.0),
                },
            }
        );
        var acCurrent = new AcDataset(
            FrequenciesHz: frequencies,
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                // BenchMeasurementRunner applies sign inversion for harness.<supply>.P.
                ["VhV_VDD"] = new[]
                {
                    new System.Numerics.Complex(-1.0, 0.0),
                    new System.Numerics.Complex(0.0, -1.0),
                },
            }
        );
        var harnessElements = new[]
        {
            new BenchHarnessElement(
                Type: "VDC",
                Id: "hV_VDD",
                Pins: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["P"] = "VDD",
                    ["N"] = "0",
                },
                Parameters: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["V"] = new BenchNumber(BenchNumericKind.VoltageV, 1.8),
                }
            ),
        };

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 100,
                    StartS: 0,
                    StopS: 0,
                    Ac: acVoltage,
                    AcCurrents: acCurrent
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harnessElements: harnessElements
        );

        var values = runner.RunMetrics(
            new[]
            {
                "VoltageMagFromSpectrum",
                "VoltageMagFromPoint",
                "VoltagePhaseFromSpectrum",
                "VoltagePhaseFromPoint",
                "CurrentMagFromSpectrum",
                "CurrentMagFromPoint",
                "CurrentPhaseFromSpectrum",
                "CurrentPhaseFromPoint",
            }
        );

        var expectedMag = 1.0;
        var expectedPhase = 45.0;
        Assert.Equal(expectedMag, values["VoltageMagFromSpectrum"].Value, precision: 12);
        Assert.Equal(expectedMag, values["VoltageMagFromPoint"].Value, precision: 12);
        Assert.Equal(expectedPhase, values["VoltagePhaseFromSpectrum"].Value, precision: 12);
        Assert.Equal(expectedPhase, values["VoltagePhaseFromPoint"].Value, precision: 12);
        Assert.Equal(expectedMag, values["CurrentMagFromSpectrum"].Value, precision: 12);
        Assert.Equal(expectedMag, values["CurrentMagFromPoint"].Value, precision: 12);
        Assert.Equal(expectedPhase, values["CurrentPhaseFromSpectrum"].Value, precision: 12);
        Assert.Equal(expectedPhase, values["CurrentPhaseFromPoint"].Value, precision: 12);
        Assert.Equal(
            values["VoltageMagFromSpectrum"].Value,
            values["VoltageMagFromPoint"].Value,
            precision: 12
        );
        Assert.Equal(
            values["VoltagePhaseFromSpectrum"].Value,
            values["VoltagePhaseFromPoint"].Value,
            precision: 12
        );
        Assert.Equal(
            values["CurrentMagFromSpectrum"].Value,
            values["CurrentMagFromPoint"].Value,
            precision: 12
        );
        Assert.Equal(
            values["CurrentPhaseFromSpectrum"].Value,
            values["CurrentPhaseFromPoint"].Value,
            precision: 12
        );
    }

    [Fact]
    public void ComplexSpectra_ValueAt_InterpolatesPhaseUsingShortestAngularPath()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench WrappedPhaseBench {{
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=100Hz)
  }}

  measurements {{
    measurement WrappedPhaseDeg : deg {{
      return voltage(ac, OUT).ValueAt(10Hz).Phase()
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "WrappedPhaseBench");
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0, 100.0 },
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["OUT"] = new[]
                {
                    System.Numerics.Complex.FromPolarCoordinates(1.0, 170.0 * Math.PI / 180.0),
                    System.Numerics.Complex.FromPolarCoordinates(1.0, -170.0 * Math.PI / 180.0),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 100,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "WrappedPhaseDeg" });
        var phase = values["WrappedPhaseDeg"].Value;
        Assert.True(Math.Abs(Math.Abs(phase) - 180.0) < 1e-9);
        Assert.True(Math.Abs(phase) > 90.0);
    }

    [Fact]
    public void ComplexSpectra_ValueAt_UsesNearestNonZeroEndpointPhase_WhenMagnitudeIsZero()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench NearZeroPhaseBench {{
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=100Hz)
  }}

  measurements {{
    measurement MagFromSpectrum : V {{
      return voltage(ac, OUT).Mag().ValueAt(10Hz)
    }}

    measurement MagFromPoint : V {{
      return voltage(ac, OUT).ValueAt(10Hz).Mag()
    }}

    measurement PhaseFromPoint : deg {{
      return voltage(ac, OUT).ValueAt(10Hz).Phase()
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "NearZeroPhaseBench");
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0, 100.0 },
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["OUT"] = new[]
                {
                    new System.Numerics.Complex(0.0, 0.0),
                    new System.Numerics.Complex(0.0, 1.0),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 100,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(
            new[] { "MagFromSpectrum", "MagFromPoint", "PhaseFromPoint" }
        );
        Assert.Equal(values["MagFromSpectrum"].Value, values["MagFromPoint"].Value, precision: 12);
        Assert.Equal(0.5, values["MagFromPoint"].Value, precision: 12);
        Assert.Equal(90.0, values["PhaseFromPoint"].Value, precision: 12);
    }

    [Fact]
    public void MeasurementCacheKey_DistinguishesDifferentComplexArguments()
    {
        var measurement = new MeasurementDefinition
        {
            Name = "M",
            Unit = "V",
            Parameters = new List<TypedParameter> { new(BenchValueType.Voltage, "sample") },
            Body = new List<BenchStatement>(),
        };
        var argsA = new Dictionary<string, BenchValue>(StringComparer.Ordinal)
        {
            ["sample"] = new BenchComplexNumber(
                BenchNumericKind.VoltageV,
                new System.Numerics.Complex(1.0, 2.0)
            ),
        };
        var argsB = new Dictionary<string, BenchValue>(StringComparer.Ordinal)
        {
            ["sample"] = new BenchComplexNumber(
                BenchNumericKind.VoltageV,
                new System.Numerics.Complex(2.0, 1.0)
            ),
        };

        var makeKey = typeof(BenchMeasurementRunner).GetMethod(
            "MakeMeasurementCacheKey",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.NotNull(makeKey);

        var keyA = (string)makeKey!.Invoke(null, new object?[] { measurement, argsA })!;
        var keyB = (string)makeKey.Invoke(null, new object?[] { measurement, argsB })!;

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void ComplexSpectra_ValueAt_UsesRelativeThresholdForNearZeroPhaseStabilization()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench TinyMagnitudePhaseBench {{
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=100Hz)
  }}

  measurements {{
    measurement PhaseFromPoint : deg {{
      return voltage(ac, OUT).ValueAt(10Hz).Phase()
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
            b.Name == "TinyMagnitudePhaseBench"
        );
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0, 100.0 },
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["OUT"] = new[]
                {
                    new System.Numerics.Complex(-1e-30, 0.0),
                    new System.Numerics.Complex(0.0, 1e-18),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 100,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "PhaseFromPoint" });
        Assert.Equal(90.0, values["PhaseFromPoint"].Value, precision: 9);
    }

    [Fact]
    public void ComplexValueAt_SupportsBinaryArithmetic_AndUnaryNegation()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench ComplexBinaryBench {{
  resp OUT1 : analog
  resp OUT2 : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=100Hz)
  }}

  measurements {{
    measurement AddMag : V {{
      return (voltage(ac, OUT1).ValueAt(10Hz) + voltage(ac, OUT2).ValueAt(10Hz)).Mag()
    }}

    measurement SubMag : V {{
      return (voltage(ac, OUT1).ValueAt(10Hz) - voltage(ac, OUT2).ValueAt(10Hz)).Mag()
    }}

    measurement MulRightScalarMag : V {{
      return (voltage(ac, OUT1).ValueAt(10Hz) * 2).Mag()
    }}

    measurement MulLeftScalarMag : V {{
      return (2 * voltage(ac, OUT1).ValueAt(10Hz)).Mag()
    }}

    measurement DivScalarMag : V {{
      return (voltage(ac, OUT1).ValueAt(10Hz) / 2).Mag()
    }}

    measurement DivComplexPhase : deg {{
      return (voltage(ac, OUT1).ValueAt(10Hz) / voltage(ac, OUT2).ValueAt(10Hz)).Phase()
    }}

    measurement NegatedPhase : deg {{
      return (-voltage(ac, OUT1).ValueAt(10Hz)).Phase()
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "ComplexBinaryBench");
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0, 100.0 },
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["OUT1"] = new[]
                {
                    new System.Numerics.Complex(1.0, 1.0),
                    new System.Numerics.Complex(3.0, 3.0),
                },
                ["OUT2"] = new[]
                {
                    new System.Numerics.Complex(2.0, 0.0),
                    new System.Numerics.Complex(4.0, 0.0),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 100,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT1"] = new BenchTerminalRef("OUT1", new[] { "OUT1" }),
                ["OUT2"] = new BenchTerminalRef("OUT2", new[] { "OUT2" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(
            new[]
            {
                "AddMag",
                "SubMag",
                "MulRightScalarMag",
                "MulLeftScalarMag",
                "DivScalarMag",
                "DivComplexPhase",
                "NegatedPhase",
            }
        );

        Assert.Equal(Math.Sqrt(29.0), values["AddMag"].Value, precision: 12);
        Assert.Equal(Math.Sqrt(5.0), values["SubMag"].Value, precision: 12);
        Assert.Equal(Math.Sqrt(32.0), values["MulRightScalarMag"].Value, precision: 12);
        Assert.Equal(
            values["MulRightScalarMag"].Value,
            values["MulLeftScalarMag"].Value,
            precision: 12
        );
        Assert.Equal(Math.Sqrt(2.0), values["DivScalarMag"].Value, precision: 12);
        Assert.Equal(45.0, values["DivComplexPhase"].Value, precision: 12);
        Assert.Equal(-135.0, values["NegatedPhase"].Value, precision: 12);
    }

    [Fact]
    public void ComplexValueAt_CannotBeReturnedAsVoltageWithoutMag()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench InvalidComplexReturn {{
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=100Hz)
  }}

  measurements {{
    measurement BadVoltage : V {{
      return voltage(ac, OUT).ValueAt(10Hz)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Code == "CAS2004"
                && d.Message.Contains("ComplexVoltage", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Port_NonRealImpedance_ProducesSemanticError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench InvalidPortImpedance {{
  resp P1 : analog
  resp P2 : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port p1 = new Port(N=1, Z=50MHz, V=0V) {{
      .P--P1
      .N--gnd
    }}
    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--P2
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement Dummy : dB {{
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, 1).Mag()).ValueAt(1GHz)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains(
                    "Port impedance must be real-valued: invalid port impedance on port 1."
                )
        );
    }

    [Fact]
    public void Port_ImpedanceBenchParameter_AllowsTypedImpedanceReference()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench TypedPortImpedance(Impedance zref = 50Ohm) {{
  resp P1 : analog
  resp P2 : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port p1 = new Port(N=1, Z=zref, V=0V) {{
      .P--P1
      .N--gnd
    }}
    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--P2
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement Dummy : dB {{
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, 1).Mag()).ValueAt(1GHz)
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
    }

    [Fact]
    public void Port_NonImpedanceBenchParameter_ProducesSemanticError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench InvalidTypedPortImpedance(Frequency wrong = 50MHz) {{
  resp P1 : analog
  resp P2 : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port p1 = new Port(N=1, Z=wrong, V=0V) {{
      .P--P1
      .N--gnd
    }}
    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--P2
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement Dummy : dB {{
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, 1).Mag()).ValueAt(1GHz)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains(
                    "Port impedance must be real-valued: invalid port impedance on port 1."
                )
        );
    }

    [Fact]
    public void SParameterMatrix_SAccessAndReturnLoss_UseMatrixData()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench SParamBench {{
  resp IN : analog
  resp OUT : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port p1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--IN
      .N--gnd
    }}
    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--OUT
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=2, start=1GHz, stop=2GHz)
  }}

  measurements {{
    measurement S21At1G : dB {{
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, 1).Mag()).ValueAt(1GHz)
    }}

    measurement RLInAt1G : dB {{
      SParameterMatrix S = sparam(sp)
      return S.ReturnLoss(1).ValueAt(1GHz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "SParamBench");
        var sp = new BenchSParameterMatrix(
            FrequenciesHz: new[] { 1e9, 2e9 },
            Elements: new Dictionary<BenchPortPair, System.Numerics.Complex[]>
            {
                [new BenchPortPair(2, 1)] = new[]
                {
                    new System.Numerics.Complex(0.5, 0.0),
                    new System.Numerics.Complex(0.25, 0.0),
                },
                [new BenchPortPair(1, 1)] = new[]
                {
                    new System.Numerics.Complex(0.1, 0.0),
                    new System.Numerics.Complex(0.2, 0.0),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["sp"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "sp",
                    StartHz: 1e9,
                    StopHz: 2e9,
                    StartS: 0,
                    StopS: 0,
                    SParameters: sp
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "S21At1G", "RLInAt1G" });
        Assert.Equal(-6.020599913279624, values["S21At1G"].Value, precision: 9);
        Assert.Equal(20.0, values["RLInAt1G"].Value, precision: 9);
    }

    [Fact]
    public void SParameterMatrix_VSWR_ReturnsScalarSpectrum_AndValueAtIsScalar()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench VswrBench {{
  resp IN : analog
  resp OUT : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port p1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--IN
      .N--gnd
    }}
    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--OUT
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=2, start=1GHz, stop=2GHz)
  }}

  measurements {{
    measurement VswrAt1G : Scalar {{
      SParameterMatrix S = sparam(sp)
      ScalarSpectrum vswr = S.VSWR(1)
      return vswr.ValueAt(1GHz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "VswrBench");
        var sp = new BenchSParameterMatrix(
            FrequenciesHz: new[] { 1e9, 2e9 },
            Elements: new Dictionary<BenchPortPair, System.Numerics.Complex[]>
            {
                [new BenchPortPair(1, 1)] = new[]
                {
                    new System.Numerics.Complex(0.5, 0.0),
                    new System.Numerics.Complex(0.6, 0.0),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["sp"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "sp",
                    StartHz: 1e9,
                    StopHz: 2e9,
                    StartS: 0,
                    StopS: 0,
                    SParameters: sp
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var evaluateExpr = typeof(BenchMeasurementRunner).GetMethod(
            "EvaluateExpr",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        Assert.NotNull(evaluateExpr);
        var locals = new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["S"] = sp,
        };
        var vswrExpr = new MeasurementMethodCall(
            new MeasurementPath("S"),
            "VSWR",
            new[] { new MeasurementCallArg(null, new MeasurementNumber("1")) }
        );
        var vswrSpectrum = (BenchValue)
            evaluateExpr!.Invoke(runner, new object[] { vswrExpr, locals })!;
        Assert.IsType<BenchScalarSpectrum>(vswrSpectrum);

        var values = runner.RunMetrics(new[] { "VswrAt1G" });
        Assert.Equal(3.0, values["VswrAt1G"].Value, precision: 9);
        Assert.Equal("Scalar", values["VswrAt1G"].Unit);
    }

    [Fact]
    public void SParameterMatrix_GroupDelay_ReturnsTimeSpectrum_AndValueAtIsTime()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench GroupDelayBench {{
  resp IN : analog
  resp OUT : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port p1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--IN
      .N--gnd
    }}
    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--OUT
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=2, start=1GHz, stop=2GHz)
  }}

  measurements {{
    measurement DelayAt1G : s {{
      SParameterMatrix S = sparam(sp)
      TimeSpectrum gd = S.GroupDelay(2, 1)
      return gd.ValueAt(1GHz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "GroupDelayBench");
        var sp = new BenchSParameterMatrix(
            FrequenciesHz: new[] { 1e9, 2e9 },
            Elements: new Dictionary<BenchPortPair, System.Numerics.Complex[]>
            {
                [new BenchPortPair(2, 1)] = new[]
                {
                    new System.Numerics.Complex(0.0, -1.0),
                    new System.Numerics.Complex(-1.0, 0.0),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["sp"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "sp",
                    StartHz: 1e9,
                    StopHz: 2e9,
                    StartS: 0,
                    StopS: 0,
                    SParameters: sp
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var evaluateExpr = typeof(BenchMeasurementRunner).GetMethod(
            "EvaluateExpr",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        Assert.NotNull(evaluateExpr);
        var locals = new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["S"] = sp,
        };
        var groupDelayExpr = new MeasurementMethodCall(
            new MeasurementPath("S"),
            "GroupDelay",
            new[]
            {
                new MeasurementCallArg(null, new MeasurementNumber("2")),
                new MeasurementCallArg(null, new MeasurementNumber("1")),
            }
        );
        var groupDelaySpectrum = (BenchValue)
            evaluateExpr!.Invoke(runner, new object[] { groupDelayExpr, locals })!;
        Assert.IsType<BenchTimeSpectrum>(groupDelaySpectrum);

        var values = runner.RunMetrics(new[] { "DelayAt1G" });
        Assert.Equal(2.5e-10, values["DelayAt1G"].Value, precision: 15);
        Assert.Equal("s", values["DelayAt1G"].Unit);
    }

    [Fact]
    public void SParameterMatrix_MAG_FallsBackToMSG_WhenKIsBelowOne()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench StabilityBench {{
  resp IN : analog
  resp OUT : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port p1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--IN
      .N--gnd
    }}
    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--OUT
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement MsgAt1G : dB {{
      SParameterMatrix S = sparam(sp)
      return db20(S.MSG()).ValueAt(1GHz)
    }}

    measurement MagAt1G : dB {{
      SParameterMatrix S = sparam(sp)
      return db20(S.MAG()).ValueAt(1GHz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "StabilityBench");
        var sp = new BenchSParameterMatrix(
            FrequenciesHz: new[] { 1e9 },
            Elements: new Dictionary<BenchPortPair, System.Numerics.Complex[]>
            {
                [new BenchPortPair(1, 1)] = new[] { new System.Numerics.Complex(0.8, 0.0) },
                [new BenchPortPair(1, 2)] = new[] { new System.Numerics.Complex(0.4, 0.0) },
                [new BenchPortPair(2, 1)] = new[] { new System.Numerics.Complex(2.0, 0.0) },
                [new BenchPortPair(2, 2)] = new[] { new System.Numerics.Complex(0.8, 0.0) },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["sp"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "sp",
                    StartHz: 1e9,
                    StopHz: 1e9,
                    StartS: 0,
                    StopS: 0,
                    SParameters: sp
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "MsgAt1G", "MagAt1G" });
        Assert.Equal(values["MsgAt1G"].Value, values["MagAt1G"].Value, precision: 12);
    }

    [Fact]
    public void SPAnalysis_WithoutPorts_ProducesSemanticError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench MissingPorts {{
  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=10, start=1GHz, stop=2GHz)
  }}

  measurements {{
    measurement Dummy : dB {{
      return 0
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Message.Contains("SPAnalysis requires at least one Port instance.")
        );
    }

    [Fact]
    public void SPAnalysis_SequentialPortNumbering_Valid_NoErrors()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench TwoPortSequential {{
  resp P1 : analog
  resp P2 : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port port1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--P1
      .N--gnd
    }}
    Port port2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--P2
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement Sanity : dB {{
      return 1dB
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
        Assert.DoesNotContain(
            result.Diagnostics,
            d =>
                d.Severity == DiagnosticSeverity.Error
                && d.Message.Contains(
                    "Incorrect port ordering, ports must be numbered sequentially from 1",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void SPAnalysis_NonSequentialPortNumberingGap_ProducesSemanticError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench GapPortNumbering {{
  resp P1 : analog
  resp P3 : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port port1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--P1
      .N--gnd
    }}
    Port port3 = new Port(N=3, Z=50Ohm, V=0V) {{
      .P--P3
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement Sanity : dB {{
      return 1dB
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains(
                    "Incorrect port ordering, ports must be numbered sequentially from 1"
                )
        );
    }

    [Fact]
    public void SPAnalysis_NonSequentialPortNumberingNotStartingAt1_ProducesSemanticError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench StartsAtTwo {{
  resp P2 : analog
  resp P3 : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port port2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--P2
      .N--gnd
    }}
    Port port3 = new Port(N=3, Z=50Ohm, V=0V) {{
      .P--P3
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement Sanity : dB {{
      return 1dB
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains(
                    "Incorrect port ordering, ports must be numbered sequentially from 1"
                )
        );
    }

    [Fact]
    public void SParameterMatrix_StabilityK_UsesOwningBenchPortCount()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench NoPortsBench {{
  resp A : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=1, start=1Hz, stop=1Hz)
  }}

  measurements {{
    measurement Sanity : dB {{
      return 1dB
    }}
  }}
}}

bench TwoPortBench {{
  resp P1 : analog
  resp P2 : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port port1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--P1
      .N--gnd
    }}
    Port port2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--P2
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement KAt1G : Scalar {{
      SParameterMatrix S = sparam(sp)
      return S.StabilityK().ValueAt(1GHz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "TwoPortBench");
        var sp = new BenchSParameterMatrix(
            FrequenciesHz: new[] { 1e9 },
            Elements: new Dictionary<BenchPortPair, System.Numerics.Complex[]>
            {
                [new BenchPortPair(1, 1)] = new[] { new System.Numerics.Complex(0.3, 0.0) },
                [new BenchPortPair(1, 2)] = new[] { new System.Numerics.Complex(0.1, 0.0) },
                [new BenchPortPair(2, 1)] = new[] { new System.Numerics.Complex(2.0, 0.0) },
                [new BenchPortPair(2, 2)] = new[] { new System.Numerics.Complex(0.3, 0.0) },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["sp"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "sp",
                    StartHz: 1e9,
                    StopHz: 1e9,
                    StartS: 0,
                    StopS: 0,
                    SParameters: sp
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["P1"] = new BenchTerminalRef("P1", new[] { "P1" }),
                ["P2"] = new BenchTerminalRef("P2", new[] { "P2" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "KAt1G" });
        Assert.Equal(2.08025, values["KAt1G"].Value, precision: 12);
        Assert.Equal("Scalar", values["KAt1G"].Unit);
    }

    [Fact]
    public void SParameterMatrix_MSGForNonTwoPortNetwork_ProducesSemanticError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench ThreePortBench {{
  resp P1 : analog
  resp P2 : analog
  resp P3 : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port port1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--P1
      .N--gnd
    }}
    Port port2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--P2
      .N--gnd
    }}
    Port port3 = new Port(N=3, Z=50Ohm, V=0V) {{
      .P--P3
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement MsgVal : dB {{
      SParameterMatrix S = sparam(sp)
      return db20(S.MSG()).ValueAt(1GHz)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains(
                    "MSG is defined for 2-port networks only; bench declares 3 ports."
                )
        );
    }

    [Fact]
    public void SParameterMatrix_UnknownMethod_ProducesSemanticError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench UnknownSParamMethodBench {{
  resp P1 : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port port1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--P1
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement Bogus : Scalar {{
      SParameterMatrix S = sparam(sp)
      return S.Bogus()
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Message.Contains("Unknown SParameterMatrix method 'Bogus'.")
        );
    }

    [Fact]
    public void SParameterMatrix_PortIndexOutOfRange_ProducesSemanticError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench PortRangeBench {{
  resp P1 : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port port1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--P1
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement OutOfRange : dB {{
      SParameterMatrix S = sparam(sp)
      return S.ReturnLoss(2).ValueAt(1GHz)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Message.Contains("Port index 2 is out of range; bench declares ports 1..1.")
        );
    }

    [Fact]
    public void SParameterMatrix_PortArgNotInteger_ProducesSemanticError()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench PortTypeBench {{
  resp P1 : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port port1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--P1
      .N--gnd
    }}
  }}

  analysis {{
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement WrongType : dB {{
      SParameterMatrix S = sparam(sp)
      return S.ReturnLoss(1GHz).ValueAt(1GHz)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Message.Contains("Port argument to ReturnLoss must be an integer")
        );
    }

    [Fact]
    public void Spectrum_Range_TruncatesBand()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench SpectrumRangeBench {{
  stim IN : analog
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=4, start=1Hz, stop=1000Hz)
  }}

  measurements {{
    measurement GainBandMax : dB {{
      GainSpectrum G = db20(transfer(ac, IN, OUT).Mag()).Range(10Hz, 100Hz)
      return G.Max()
    }}

    measurement GainBandAt10 : dB {{
      return db20(transfer(ac, IN, OUT).Mag()).Range(10Hz, 100Hz).ValueAt(10Hz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "SpectrumRangeBench");
        var frequencies = new[] { 1.0, 10.0, 100.0, 1000.0 };
        var ac = new AcDataset(
            FrequenciesHz: frequencies,
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["IN"] = Enumerable
                    .Repeat(new System.Numerics.Complex(1.0, 0.0), frequencies.Length)
                    .ToArray(),
                ["OUT"] = new[]
                {
                    new System.Numerics.Complex(0.1, 0.0),
                    new System.Numerics.Complex(0.5, 0.0),
                    new System.Numerics.Complex(2.0, 0.0),
                    new System.Numerics.Complex(10.0, 0.0),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 1000,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "GainBandMax", "GainBandAt10" });
        Assert.Equal(ToDb20(2.0), values["GainBandMax"].Value, precision: 9);
        Assert.Equal(ToDb20(0.5), values["GainBandAt10"].Value, precision: 9);
    }

    [Fact]
    public void ComplexSpectrum_Range_PreservesComplexPointOperations()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench ComplexRangeBench {{
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=4, start=1Hz, stop=1000Hz)
  }}

  measurements {{
    measurement ComplexBandMag : V {{
      return voltage(ac, OUT).Range(10Hz, 100Hz).ValueAt(10Hz).Mag()
    }}

    measurement ComplexBandPhase : deg {{
      return voltage(ac, OUT).Range(10Hz, 100Hz).ValueAt(10Hz).Phase()
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "ComplexRangeBench");
        var frequencies = new[] { 1.0, 10.0, 100.0, 1000.0 };
        var ac = new AcDataset(
            FrequenciesHz: frequencies,
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["OUT"] = new[]
                {
                    new System.Numerics.Complex(0.0, 0.0),
                    System.Numerics.Complex.FromPolarCoordinates(2.0, Math.PI / 4.0),
                    System.Numerics.Complex.FromPolarCoordinates(4.0, Math.PI / 2.0),
                    System.Numerics.Complex.FromPolarCoordinates(8.0, 3.0 * Math.PI / 4.0),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 1000,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "ComplexBandMag", "ComplexBandPhase" });
        Assert.Equal(2.0, values["ComplexBandMag"].Value, precision: 9);
        Assert.Equal(45.0, values["ComplexBandPhase"].Value, precision: 9);
    }

    [Fact]
    public void Waveform_Range_TruncatesWindowAndInterpolates()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench WaveformRangeBench {{
  resp OUT : analog

  analysis {{
    TranAnalysis tran = new TranAnalysis(step=1ns, start=0ns, stop=5ns)
  }}

  measurements {{
    measurement WindowMax : V {{
      return voltage(tran, OUT).Range(2ns, 4ns).Max()
    }}

    measurement WindowAt3ns : V {{
      return voltage(tran, OUT).Range(2ns, 4ns).ValueAt(3ns)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "WaveformRangeBench");
        var tran = new TranDataset(
            TimePoints: new[] { 0.0, 1e-9, 2e-9, 4e-9, 5e-9 },
            NodeVoltages: new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new[] { 0.0, 1.0, 4.0, 16.0, 25.0 },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["tran"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "tran",
                    StartHz: 0,
                    StopHz: 0,
                    StartS: 0,
                    StopS: 5e-9,
                    Tran: tran
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "WindowMax", "WindowAt3ns" });
        Assert.Equal(16.0, values["WindowMax"].Value, precision: 9);
        Assert.Equal(10.0, values["WindowAt3ns"].Value, precision: 9);
    }

    [Fact]
    public void Range_TypeInference_AllowsTypedChainingForSpectrumAndWaveform()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench TypedRangeBench {{
  stim IN : analog
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=10Hz)
    TranAnalysis tran = new TranAnalysis(step=1ns, start=0ns, stop=2ns)
  }}

  measurements {{
    measurement SpectrumTypeOk : dB {{
      GainSpectrum G = db20(transfer(ac, IN, OUT).Mag()).Range(1Hz, 10Hz)
      return G.ValueAt(1Hz)
    }}

    measurement WaveformTypeOk : V {{
      VoltageWaveform W = voltage(tran, OUT).Range(0ns, 2ns)
      return W.ValueAt(1ns)
    }}

    measurement ComplexSpectrumTypeOk : V {{
      ComplexVoltageSpectrum CV = voltage(ac, OUT).Range(1Hz, 10Hz)
      return CV.ValueAt(1Hz).Mag()
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
    }

    [Fact]
    public void Spectrum_Range_WithTimeArguments_ThrowsAtRuntime()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BadSpectrumRangeBench {{
  stim IN : analog
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=10Hz)
  }}

  measurements {{
    measurement Bad : dB {{
      return db20(transfer(ac, IN, OUT).Mag()).Range(1ns, 2ns).ValueAt(10Hz)
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
            b.Name == "BadSpectrumRangeBench"
        );
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0, 10.0 },
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["IN"] = new[]
                {
                    new System.Numerics.Complex(1.0, 0.0),
                    new System.Numerics.Complex(1.0, 0.0),
                },
                ["OUT"] = new[]
                {
                    new System.Numerics.Complex(1.0, 0.0),
                    new System.Numerics.Complex(1.0, 0.0),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 10,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Expected Frequency for Range.from", ex.Message);
    }

    [Fact]
    public void Waveform_Range_WithFrequencyArguments_ThrowsAtRuntime()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BadWaveformRangeBench {{
  resp OUT : analog

  analysis {{
    TranAnalysis tran = new TranAnalysis(step=1ns, start=0ns, stop=2ns)
  }}

  measurements {{
    measurement Bad : V {{
      return voltage(tran, OUT).Range(10Hz, 100Hz).ValueAt(1ns)
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
            b.Name == "BadWaveformRangeBench"
        );
        var tran = new TranDataset(
            TimePoints: new[] { 0.0, 1e-9, 2e-9 },
            NodeVoltages: new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new[] { 0.0, 1.0, 2.0 },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["tran"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "tran",
                    StartHz: 0,
                    StopHz: 0,
                    StartS: 0,
                    StopS: 2e-9,
                    Tran: tran
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Expected Time for Range.from", ex.Message);
    }

    [Fact]
    public void Spectrum_Range_ThatProducesEmptyBand_ThrowsAtRuntime()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench EmptySpectrumBandBench {{
  stim IN : analog
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=10Hz)
  }}

  measurements {{
    measurement EmptyMax : dB {{
      return db20(transfer(ac, IN, OUT).Mag()).Range(1000Hz, 2000Hz).Max()
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
            b.Name == "EmptySpectrumBandBench"
        );
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0, 10.0 },
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["IN"] = new[]
                {
                    new System.Numerics.Complex(1.0, 0.0),
                    new System.Numerics.Complex(1.0, 0.0),
                },
                ["OUT"] = new[]
                {
                    new System.Numerics.Complex(1.0, 0.0),
                    new System.Numerics.Complex(1.0, 0.0),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 10,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Empty range after slicing.", ex.Message);
    }

    [Fact]
    public void Waveform_Range_ThatProducesEmptyWindow_ThrowsAtRuntime()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench EmptyWaveformWindowBench {{
  resp OUT : analog

  analysis {{
    TranAnalysis tran = new TranAnalysis(step=1ns, start=1ns, stop=3ns)
  }}

  measurements {{
    measurement EmptyMax : V {{
      return voltage(tran, OUT).Range(1ns, 0ns).Max()
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
            b.Name == "EmptyWaveformWindowBench"
        );
        var tran = new TranDataset(
            TimePoints: new[] { 1e-9, 2e-9, 3e-9 },
            NodeVoltages: new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new[] { 1.0, 2.0, 3.0 },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["tran"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "tran",
                    StartHz: 0,
                    StopHz: 0,
                    StartS: 1e-9,
                    StopS: 3e-9,
                    Tran: tran
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Empty range after slicing.", ex.Message);
    }

    [Fact]
    public void BuiltinMethods_NamedAndMixedArguments_ResolveCorrectly()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BuiltinNamedArgsBench {{
  stim IN : analog
  resp OUT : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port p1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--IN
      .N--gnd
    }}
    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--OUT
      .N--gnd
    }}
  }}

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=3, start=1Hz, stop=100Hz)
    NoiseAnalysis noise_ac = new NoiseAnalysis(space=Log, samples=2, start=10Hz, stop=100Hz, output=OUT)
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement NamedValueAt : dB {{
      GainSpectrum G = db20(transfer(ac, IN, OUT).Mag())
      return G.ValueAt(f=10Hz)
    }}

    measurement ReorderedRange : dB {{
      return db20(transfer(ac, IN, OUT).Mag()).Range(from=10Hz, to=100Hz).ValueAt(f=10Hz)
    }}

    measurement NamedIntegrate : Vrms {{
      return noise(noise_ac, OUT).Integrate(from=10Hz, to=100Hz)
    }}

    measurement MixedSAccess : dB {{
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, j=1).Mag()).ValueAt(f=1GHz)
    }}

    measurement NamedReturnLoss : dB {{
      SParameterMatrix S = sparam(sp)
      return S.ReturnLoss(port=1).ValueAt(f=1GHz)
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
            b.Name == "BuiltinNamedArgsBench"
        );
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0, 10.0, 100.0 },
            NodeVoltages: new Dictionary<string, Complex[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new[]
                {
                    new Complex(1.0, 0.0),
                    new Complex(1.0, 0.0),
                    new Complex(1.0, 0.0),
                },
                ["OUT"] = new[]
                {
                    new Complex(0.1, 0.0),
                    new Complex(0.5, 0.0),
                    new Complex(2.0, 0.0),
                },
            }
        );
        var noise = new NoiseDataset(
            FrequenciesHz: new[] { 10.0, 100.0 },
            OutputNoiseVPerRtHz: new[] { 1e-9, 1e-9 }
        );
        var sp = new BenchSParameterMatrix(
            FrequenciesHz: new[] { 1e9 },
            Elements: new Dictionary<BenchPortPair, Complex[]>
            {
                [new BenchPortPair(2, 1)] = new[] { new Complex(0.5, 0.0) },
                [new BenchPortPair(1, 1)] = new[] { new Complex(0.1, 0.0) },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 100,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
                ["noise_ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "noise_ac",
                    StartHz: 10,
                    StopHz: 100,
                    StartS: 0,
                    StopS: 0,
                    Noise: noise
                ),
                ["sp"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "sp",
                    StartHz: 1e9,
                    StopHz: 1e9,
                    StartS: 0,
                    StopS: 0,
                    SParameters: sp
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(
            new[]
            {
                "NamedValueAt",
                "ReorderedRange",
                "NamedIntegrate",
                "MixedSAccess",
                "NamedReturnLoss",
            }
        );
        Assert.Equal(ToDb20(0.5), values["NamedValueAt"].Value, precision: 9);
        Assert.Equal(ToDb20(0.5), values["ReorderedRange"].Value, precision: 9);
        Assert.Equal(Math.Sqrt(90.0) * 1e-9, values["NamedIntegrate"].Value, precision: 15);
        Assert.Equal(ToDb20(0.5), values["MixedSAccess"].Value, precision: 9);
        Assert.Equal(20.0, values["NamedReturnLoss"].Value, precision: 9);
    }

    [Fact]
    public void BuiltinMethod_UnexpectedNamedArg_ThrowsAtRuntime()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench UnexpectedNamedArgBench {{
  stim IN : analog
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=10Hz)
  }}

  measurements {{
    measurement Bad : dB {{
      return db20(transfer(ac, IN, OUT).Mag()).ValueAt(f=100Hz, x=1Hz)
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
            b.Name == "UnexpectedNamedArgBench"
        );
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0, 10.0 },
            NodeVoltages: new Dictionary<string, Complex[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new[] { new Complex(1.0, 0.0), new Complex(1.0, 0.0) },
                ["OUT"] = new[] { new Complex(1.0, 0.0), new Complex(1.0, 0.0) },
            }
        );
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 10,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Unexpected argument(s) 'x' for method 'ValueAt'.", ex.Message);
    }

    [Fact]
    public void BuiltinMethod_RangeNamedArgs_AreOrderIndependent()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench NamedRangeOrderBench {{
  stim IN : analog
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=3, start=1Hz, stop=100Hz)
  }}

  measurements {{
    measurement Positional : dB {{
      return db20(transfer(ac, IN, OUT).Mag()).Range(10Hz, 100Hz).ValueAt(10Hz)
    }}

    measurement ReorderedNamed : dB {{
      return db20(transfer(ac, IN, OUT).Mag()).Range(to=100Hz, from=10Hz).ValueAt(10Hz)
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

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "NamedRangeOrderBench");
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0, 10.0, 100.0 },
            NodeVoltages: new Dictionary<string, Complex[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new[]
                {
                    new Complex(1.0, 0.0),
                    new Complex(1.0, 0.0),
                    new Complex(1.0, 0.0),
                },
                ["OUT"] = new[]
                {
                    new Complex(0.1, 0.0),
                    new Complex(0.5, 0.0),
                    new Complex(2.0, 0.0),
                },
            }
        );
        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 100,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "Positional", "ReorderedNamed" });
        Assert.Equal(values["Positional"].Value, values["ReorderedNamed"].Value, precision: 12);
    }

    [Fact]
    public void Range_WithWrongArgCount_ThrowsAtRuntime()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BadRangeArgCountBench {{
  stim IN : analog
  resp OUT : analog

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=10Hz)
  }}

  measurements {{
    measurement Bad : dB {{
      return db20(transfer(ac, IN, OUT).Mag()).Range(10Hz).ValueAt(10Hz)
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
            b.Name == "BadRangeArgCountBench"
        );
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0, 10.0 },
            NodeVoltages: new Dictionary<string, System.Numerics.Complex[]>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["IN"] = new[]
                {
                    new System.Numerics.Complex(1.0, 0.0),
                    new System.Numerics.Complex(1.0, 0.0),
                },
                ["OUT"] = new[]
                {
                    new System.Numerics.Complex(1.0, 0.0),
                    new System.Numerics.Complex(1.0, 0.0),
                },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 10,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Missing argument 'to' for method 'Range'.", ex.Message);
    }

    [Fact]
    public void BuiltinFunctions_NamedArguments_ResolveCorrectly()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BuiltinFunctionNamedArgsBench {{
  stim IN : analog
  resp OUT : analog

  fill {{
    net gnd : ground
    GND g = new GND() {{ .GND--gnd }}
    Port p1 = new Port(N=1, Z=50Ohm, V=0V) {{
      .P--IN
      .N--gnd
    }}
    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {{
      .P--OUT
      .N--gnd
    }}
  }}

  analysis {{
    ACAnalysis ac = new ACAnalysis(space=Log, samples=2, start=1Hz, stop=10Hz)
    SPAnalysis sp = new SPAnalysis(space=Log, samples=1, start=1GHz, stop=1GHz)
  }}

  measurements {{
    measurement ReorderedVoltage : V {{
      return voltage(terminal=OUT, analysis=ac).ValueAt(f=10Hz).Mag()
    }}

    measurement NamedSparam : dB {{
      SParameterMatrix S = sparam(analysis=sp)
      return db20(S.S(i=1, j=1).Mag()).ValueAt(f=1GHz)
    }}

    measurement NamedPeriod : s {{
      return period(f=2Hz)
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
            b.Name == "BuiltinFunctionNamedArgsBench"
        );
        var ac = new AcDataset(
            FrequenciesHz: new[] { 1.0, 10.0 },
            NodeVoltages: new Dictionary<string, Complex[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new[] { new Complex(1.0, 0.0), new Complex(1.0, 0.0) },
                ["OUT"] = new[] { new Complex(1.0, 0.0), new Complex(2.0, 0.0) },
            }
        );
        var sp = new BenchSParameterMatrix(
            FrequenciesHz: new[] { 1e9 },
            Elements: new Dictionary<BenchPortPair, Complex[]>
            {
                [new BenchPortPair(1, 1)] = new[] { new Complex(0.5, 0.0) },
            }
        );

        var runner = new BenchMeasurementRunner(
            bench,
            functions: result.Document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["ac"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "ac",
                    StartHz: 1,
                    StopHz: 10,
                    StartS: 0,
                    StopS: 0,
                    Ac: ac
                ),
                ["sp"] = new BenchMeasurementRunner.AnalysisContext(
                    Name: "sp",
                    StartHz: 1e9,
                    StopHz: 1e9,
                    StartS: 0,
                    StopS: 0,
                    SParameters: sp
                ),
            },
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "IN" }),
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "OUT" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var values = runner.RunMetrics(new[] { "ReorderedVoltage", "NamedSparam", "NamedPeriod" });
        Assert.Equal(2.0, values["ReorderedVoltage"].Value, precision: 9);
        Assert.Equal(ToDb20(0.5), values["NamedSparam"].Value, precision: 9);
        Assert.Equal(0.5, values["NamedPeriod"].Value, precision: 9);
    }

    [Fact]
    public void BuiltinFunction_UnexpectedNamedArgument_ThrowsAtRuntime()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench UnexpectedNamedFunctionArgBench {{
  measurements {{
    measurement Bad : s {{
      return period(f=10Hz, x=1Hz)
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
            b.Name == "UnexpectedNamedFunctionArgBench"
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

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Unexpected argument(s) 'x' for function 'period'.", ex.Message);
    }

    [Fact]
    public void BuiltinFunction_TooManyPositionalArguments_ThrowsAtRuntime()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench TooManyPositionalFunctionArgsBench {{
  measurements {{
    measurement Bad : V {{
      return abs(1V, 2V)
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
            b.Name == "TooManyPositionalFunctionArgsBench"
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

        var ex = Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Contains("Too many positional arguments for function 'abs'.", ex.Message);
    }

    private static double ToDb20(double magnitude) => 20.0 * Math.Log10(Math.Max(1e-15, magnitude));
}
