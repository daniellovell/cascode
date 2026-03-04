using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Language.BenchRuntime;
using Cascode.Language.BenchRuntime.Netlist;

namespace Cascode.Language.Tests;

public sealed class BenchAnalysisCompilerPssTests
{
    [Fact]
    public void Compile_PssAnalysis_ProducesPlanFieldsIncludingOscNode()
    {
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
    public void Compile_PssAnalysis_RequiresRespTerminal()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench MissingResp {{
  stim IN : analog
  analysis {{
    PSSAnalysis pss = new PSSAnalysis(fguess=1GHz, tstab=1ns, harmonics=3)
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "test.cas");
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message))
        );

        var bench = result.Document!.BenchDefinitions.Single(b => b.Name == "MissingResp");
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
