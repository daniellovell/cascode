using Cascode.ACIR;

namespace Cascode.ACIR.Tests;

public class ACIRReaderConstraintsTests
{
    [Fact]
    public void TryParse_ConstraintsWithInlineComments_ParsesCorrectly()
    {
        var content =
            $@"ACIR {ACIRVersion.Current}
circuit Test
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  constraints:
    numeric:
      c_gbw : GainBandwidth @ OUT >= 100M Hz  // target gain-bandwidth product
      c_gain : PassbandGain @ OUT >= 40 dB  // minimum gain requirement
      c_pm : PhaseMargin @ OUT >= 60 deg  // phase margin for stability
      c_pwr : Power <= 500u W
    tech:
      t_lmin : L >= 180n m on *  // minimum length per tech rules
    measure:
      m_gbw : SEOpAmpACBench GainBandwidth @ OUT  // measure GBW
      m_gain : SEOpAmpACBench PassbandGain @ OUT
";
        var result = ACIRReader.TryParse(content);
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
        Assert.Contains(constraints.Numeric, c => c.Id == "c_pwr" && c.Metric == "Power");

        // Tech constraint should be parsed despite inline comment
        Assert.Single(constraints.Tech);
        Assert.Equal("t_lmin", constraints.Tech[0].Id);
        Assert.Equal("L", constraints.Tech[0].Param);

        // Measure intents should be parsed despite inline comment
        Assert.Equal(2, constraints.Measure.Count);
        Assert.Contains(constraints.Measure, m => m.Id == "m_gbw");
        Assert.Contains(constraints.Measure, m => m.Id == "m_gain");
    }

    [Fact]
    public void TryParse_FullLineComments_AreIgnored()
    {
        var content =
            $@"ACIR {ACIRVersion.Current}
circuit Test
  level EL
  supply VDD
  ground GND
  port OUT : analog
  constraints:
    numeric:
      // This is a full line comment
      // This is another full line comment
      c_test : Metric @ OUT >= 100M Hz
";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);
        Assert.NotNull(result.Document);

        var constraints = result.Document.Circuits[0].Constraints;
        Assert.NotNull(constraints);
        Assert.Single(constraints.Numeric);
        Assert.Equal("c_test", constraints.Numeric[0].Id);
    }
}
