using System.Linq;
using Cascode.Language;

namespace Cascode.Language.Tests;

public class CascodeReaderConstraintsTests
{
    [Fact]
    public void TryParse_ConstraintsWithInlineComments_ParsesCorrectly()
    {
        var content =
            $@"VERSION {CascodeVersion.Current}
circuit Test {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  constraints {{
    bench {{
      c_gbw = transfer_bench::GainBandwidth at net::OUT >= 100MHz  // target gain-bandwidth product
      c_gain = transfer_bench::PassbandGain at net::OUT >= 40dB  // minimum gain requirement
      c_pm = transfer_bench::PhaseMargin at net::OUT >= 60deg  // phase margin for stability
      c_pwr = vdd_pwr::QuiescentPower <= 500uW
    }}
    physical {{
      t_lmin : L >= 180nm on *  // minimum length per tech rules
    }}
  }}
}}
";
        var result = CascodeReader.TryParse(content);
        Assert.True(result.Success);
        Assert.NotNull(result.Document);

        var circuit = result.Document.Circuits[0];
        var constraints = circuit.Constraints;
        Assert.NotNull(constraints);

        // All bench constraints should be parsed despite inline comments
        Assert.Equal(4, constraints.Bench.Count);
        Assert.Contains(constraints.Bench, c => c.Id == "c_gbw" && c.Metric == "GainBandwidth");
        Assert.Contains(constraints.Bench, c => c.Id == "c_gain" && c.Metric == "PassbandGain");
        Assert.Contains(constraints.Bench, c => c.Id == "c_pm" && c.Metric == "PhaseMargin");
        Assert.Contains(constraints.Bench, c => c.Id == "c_pwr" && c.Metric == "QuiescentPower");

        // Tech constraint should be parsed despite inline comment
        Assert.Single(constraints.Physical);
        Assert.Equal("t_lmin", constraints.Physical[0].Id);
        Assert.Equal("L", constraints.Physical[0].Param);
    }

    [Fact]
    public void TryParse_FullLineComments_AreIgnored()
    {
        var content =
            $@"VERSION {CascodeVersion.Current}
circuit Test {{
  level EL
  supply VDD
  ground GND
  output OUT : analog
  constraints {{
    bench {{
      // This is a full line comment
      // This is another full line comment
      c_test = transfer_bench::Metric at net::OUT >= 100MHz
    }}
  }}
}}
";
        var result = CascodeReader.TryParse(content);
        Assert.True(result.Success);
        Assert.NotNull(result.Document);

        var constraints = result.Document.Circuits[0].Constraints;
        Assert.NotNull(constraints);
        Assert.Single(constraints.Bench);
        Assert.Equal("c_test", constraints.Bench[0].Id);
    }

    [Fact]
    public void TryParse_BenchConstraints_BareScalarThresholds_ParseAndRoundTrip()
    {
        var content =
            $@"VERSION {CascodeVersion.Current}
circuit Test {{
  level EL
  supply VDD
  ground GND
  output OUT : analog
  constraints {{
    bench {{
      c_k = sparam_bench::StabilityK(f=1kHz) >= 1
      c_neg = some_bench::SomeMetric <= -0.5
    }}
  }}
}}
";
        var result = CascodeReader.TryParse(content);

        Assert.True(result.Success);
        Assert.NotNull(result.Document);

        var constraints = result.Document.Circuits[0].Constraints;
        Assert.NotNull(constraints);
        Assert.Equal(2, constraints.Bench.Count);

        var cK = constraints.Bench.Single(c => c.Id == "c_k");
        Assert.Equal("1", cK.Value);
        Assert.Equal(string.Empty, cK.Unit);

        var cNeg = constraints.Bench.Single(c => c.Id == "c_neg");
        Assert.Equal("-0.5", cNeg.Value);
        Assert.Equal(string.Empty, cNeg.Unit);

        using var writer = new StringWriter();
        CascodeWriter.Write(result.Document, writer);
        var rendered = writer.ToString();
        Assert.Contains(
            "c_k = sparam_bench::StabilityK(f=1kHz) >= 1",
            rendered,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "c_neg = some_bench::SomeMetric <= -0.5",
            rendered,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void TryParse_BenchConstraints_NoiseDensityThresholds_ParseAndRoundTrip()
    {
        var content =
            $@"VERSION {CascodeVersion.Current}
circuit SpotNoiseConstraintRepro {{
  level EL
  supply VDD
  ground GND
  output OUT : analog
  constraints {{
    bench {{
      c_noise = noise_bench::InputReferredNoise at net::OUT <= 9nV/rtHz
    }}
  }}
}}
";
        var result = CascodeReader.TryParse(content);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Document);

        var constraint = Assert.Single(result.Document.Circuits[0].Constraints!.Bench);
        Assert.Equal("9n", constraint.Value);
        Assert.Equal("V/rtHz", constraint.Unit);

        using var writer = new StringWriter();
        CascodeWriter.Write(result.Document, writer);
        var rendered = writer.ToString();
        Assert.Contains(
            "c_noise = noise_bench::InputReferredNoise at net::OUT <= 9nV/rtHz",
            rendered,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void TryParse_LegacyNumericAndTechConstraintBlocks_AreRejected()
    {
        const string legacyBenchBlock = "numeric";
        const string legacyPhysicalBlock = "tech";
        var content =
            $@"VERSION {CascodeVersion.Current}
circuit Test {{
  level EL
  output OUT : analog
  constraints {{
    {legacyBenchBlock} {{
      c_gain = transfer_bench::PassbandGain >= 40dB
    }}
    {legacyPhysicalBlock} {{
      t_lmin : L >= 180nm on *
    }}
  }}
}}
";

        var result = CascodeReader.TryParse(content);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0001")
        );
    }
}
