using Cascode.ACIR;

namespace Cascode.ACIR.Tests;

public class ACIRReaderConstraintsTests
{
    [Fact]
    public void TryParse_ConstraintsWithInlineComments_ParsesCorrectly()
    {
        var content =
            $@"ACIR {ACIRVersion.Current}
bench ACBench for SingleEndedOpAmp
  builtin SEOpAmpACBench
  outputs:
    GainBandwidth
    PassbandGain
    PhaseMargin

bench DCBench for SingleEndedOpAmp
  builtin SEOpAmpDCBench
  outputs:
    QuiescentPower

circuit Test implements SingleEndedOpAmp
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  constraints:
    numeric:
      c_gbw : ACBench::GainBandwidth at net::OUT >= 100MHz  // target gain-bandwidth product
      c_gain : ACBench::PassbandGain at net::OUT >= 40dB  // minimum gain requirement
      c_pm : ACBench::PhaseMargin at net::OUT >= 60deg  // phase margin for stability
      c_pwr : DCBench::QuiescentPower <= 500uW
    tech:
      t_lmin : L >= 180nm on *  // minimum length per tech rules
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
      c_test : ACBench::Metric at net::OUT >= 100MHz
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
