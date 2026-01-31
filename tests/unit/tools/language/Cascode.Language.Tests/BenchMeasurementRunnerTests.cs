using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Language.BenchRuntime;

namespace Cascode.Language.Tests;

public sealed class BenchMeasurementRunnerTests
{
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
}
