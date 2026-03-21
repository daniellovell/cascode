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
    numeric {{
      c_gbw = transfer_bench::GainBandwidth at net::OUT >= 100MHz  // target gain-bandwidth product
      c_gain = transfer_bench::PassbandGain at net::OUT >= 40dB  // minimum gain requirement
      c_pm = transfer_bench::PhaseMargin at net::OUT >= 60deg  // phase margin for stability
      c_pwr = vdd_pwr::QuiescentPower <= 500uW
    }}
    tech {{
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

        // All numeric constraints should be parsed despite inline comments
        Assert.Equal(4, constraints.Numeric.Count);
        Assert.Contains(constraints.Numeric, c => c.Id == "c_gbw" && c.Metric == "GainBandwidth");
        Assert.Contains(constraints.Numeric, c => c.Id == "c_gain" && c.Metric == "PassbandGain");
        Assert.Contains(constraints.Numeric, c => c.Id == "c_pm" && c.Metric == "PhaseMargin");
        Assert.Contains(constraints.Numeric, c => c.Id == "c_pwr" && c.Metric == "QuiescentPower");

        // Tech constraint should be parsed despite inline comment
        Assert.Single(constraints.Tech);
        Assert.Equal("t_lmin", constraints.Tech[0].Id);
        Assert.Equal("L", constraints.Tech[0].Param);
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
    numeric {{
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
        Assert.Single(constraints.Numeric);
        Assert.Equal("c_test", constraints.Numeric[0].Id);
    }

    [Fact]
    public void TryParse_NumericConstraints_BareScalarThresholds_ParseAndRoundTrip()
    {
        var content =
            $@"VERSION {CascodeVersion.Current}
circuit Test {{
  level EL
  supply VDD
  ground GND
  output OUT : analog
  constraints {{
    numeric {{
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
        Assert.Equal(2, constraints.Numeric.Count);

        var cK = constraints.Numeric.Single(c => c.Id == "c_k");
        Assert.Equal("1", cK.Value);
        Assert.Equal(string.Empty, cK.Unit);

        var cNeg = constraints.Numeric.Single(c => c.Id == "c_neg");
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
    public void TryParse_NumericConstraints_NoiseDensityThresholds_ParseAndRoundTrip()
    {
        var content =
            $@"VERSION {CascodeVersion.Current}
circuit SpotNoiseConstraintRepro {{
  level EL
  supply VDD
  ground GND
  output OUT : analog
  constraints {{
    numeric {{
      c_noise = noise_bench::InputReferredNoise at net::OUT <= 9nV/rtHz
    }}
  }}
}}
";
        var result = CascodeReader.TryParse(content);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Document);

        var constraint = Assert.Single(result.Document.Circuits[0].Constraints!.Numeric);
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
}
