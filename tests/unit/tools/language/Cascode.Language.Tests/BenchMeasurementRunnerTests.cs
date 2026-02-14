using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
        Assert.True(result.Success);

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
}
