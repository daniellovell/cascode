using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Language.BenchRuntime;
using Cascode.Language.BenchRuntime.Netlist;
using Cascode.TestSupport;

namespace Cascode.Language.Tests;

public sealed class BenchAnalysisCompilerPssTests
{
    [Fact]
    public void Compile_PssAnalysis_ProducesPlanFieldsIncludingOscNode()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("bench-analysis-compiler-pss");

        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench DiffPss(Frequency guess_freq = 1GHz) {{
  resp OUT : Diff

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(fguess=guess_freq, tstab=12ns, harmonics=7)
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message))
        );

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "DiffPss");
        var evalRunner = new BenchMeasurementRunner(
            bench,
            functions: new Dictionary<string, FunctionDefinition>(StringComparer.OrdinalIgnoreCase),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["OUT"] = new BenchTerminalRef("OUT", new[] { "out_p", "out_n" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var analyses = BenchAnalysisCompiler.Compile(
            bench,
            evalRunner,
            EmptyNetlist(),
            new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["guess_freq"] = new BenchNumber(BenchNumericKind.FrequencyHz, 2.4e9),
            }
        );

        var pss = Assert.Single(analyses);
        Assert.Equal(BenchValueType.PSSAnalysis, pss.Type);
        Assert.Equal(2.4e9, Assert.IsType<double>(pss.FguessHz));
        Assert.Equal(12e-9, Assert.IsType<double>(pss.TstabS), precision: 15);
        Assert.Equal(7, pss.Harmonics);
        Assert.Equal("out_p", pss.OscNode);
    }

    [Fact]
    public void Compile_PssAnalysis_UsesConfiguredOptionalParametersAndDefaults()
    {
        var configuredCascode =
            $@"VERSION {CascodeVersion.Current}

bench ConfiguredPss {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(
      fguess=1GHz,
      tstab=2ns,
      harmonics=9,
      iterations=1000,
      steady_coef=0.1,
      uic=1)
  }}
}}
";

        var configured = CompileSingle(configuredCascode, "ConfiguredPss", "OUT", "out");
        Assert.Equal(1000, configured.Iterations);
        Assert.Equal(0.1, Assert.IsType<double>(configured.SteadyCoef), precision: 15);
        Assert.True(configured.UseInitialConditions);

        var defaultCascode =
            $@"VERSION {CascodeVersion.Current}

bench DefaultPss {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(fguess=1GHz, tstab=2ns, harmonics=9)
  }}
}}
";

        var defaults = CompileSingle(defaultCascode, "DefaultPss", "OUT", "out");
        Assert.Equal(50, defaults.Iterations);
        Assert.Equal(1e-3, Assert.IsType<double>(defaults.SteadyCoef), precision: 15);
        Assert.False(defaults.UseInitialConditions);
    }

    [Fact]
    public void Compile_PssAnalysis_RequiresRespTerminal()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("bench-analysis-compiler-pss");

        var bench = new BenchDefinition
        {
            Name = "MissingResp",
            Terminals = { new BenchTerminal(BenchTerminalRole.Stim, "IN", "analog") },
            Analyses =
            {
                new AnalysisDeclaration
                {
                    Type = BenchValueType.PSSAnalysis,
                    Name = "pss",
                    Parameters = new Dictionary<string, MeasurementExpr>(StringComparer.Ordinal)
                    {
                        ["fguess"] = new MeasurementQuantity("1GHz"),
                        ["tstab"] = new MeasurementQuantity("1ns"),
                        ["harmonics"] = new MeasurementNumber("3"),
                    },
                },
            },
        };
        var evalRunner = new BenchMeasurementRunner(
            bench,
            functions: new Dictionary<string, FunctionDefinition>(StringComparer.OrdinalIgnoreCase),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new BenchTerminalRef("IN", new[] { "in" }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BenchAnalysisCompiler.Compile(bench, evalRunner, EmptyNetlist())
        );
        Assert.Contains("resp terminal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static BenchPlanAnalysis CompileSingle(
        string cascode,
        string benchName,
        string terminalName,
        string nodeName
    )
    {
        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message))
        );

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == benchName);
        var evalRunner = new BenchMeasurementRunner(
            bench,
            functions: new Dictionary<string, FunctionDefinition>(StringComparer.OrdinalIgnoreCase),
            analyses: new Dictionary<string, BenchMeasurementRunner.AnalysisContext>(
                StringComparer.OrdinalIgnoreCase
            ),
            terminals: new Dictionary<string, BenchTerminalRef>(StringComparer.OrdinalIgnoreCase)
            {
                [terminalName] = new BenchTerminalRef(terminalName, new[] { nodeName }),
            },
            env: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            harness: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase),
            constraints: new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase)
        );

        return Assert.Single(BenchAnalysisCompiler.Compile(bench, evalRunner, EmptyNetlist()));
    }

    private static BenchNetlist EmptyNetlist()
    {
        return new BenchNetlist(
            nets: [new BenchNet(new BenchNetId(0), "0", IsSpice0: true)],
            components: Array.Empty<BenchComponent>(),
            netIdByNode: new Dictionary<BenchNode, BenchNetId>(),
            attributesByNetId: new Dictionary<BenchNetId, BenchNetAttributes>
            {
                [new BenchNetId(0)] = new BenchNetAttributes(
                    IsSpice0: true,
                    HasIndependentVoltageSource: false,
                    HasLoadElement: false,
                    HasGroundTieElement: false
                ),
            }
        );
    }
}
