using System;
using System.IO;
using System.Linq;

namespace Cascode.Language.Tests;

public sealed class PssAnalysisValidationTests
{
    [Fact]
    public void PssAnalysis_MissingRequiredParameters_ReportsAllMissingFields()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench MissingPssParams {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(fguess=1GHz)
  }}

  measurements {{
    measurement Freq : Hz {{
      VoltageWaveform vout = voltage(pss, OUT)
      return 1 / duration(vout)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "missing_pss_params.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Message.Contains("missing required parameter 'tstab'", StringComparison.Ordinal)
        );
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains(
                    "missing required parameter 'harmonics'",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void PssAnalysis_InvalidParameterTypes_ReportTypeErrors()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BadPssTypes {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(fguess=1V, tstab=1Hz, harmonics=1ns)
  }}

  measurements {{
    measurement Freq : Hz {{
      VoltageWaveform vout = voltage(pss, OUT)
      return 1 / duration(vout)
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "bad_pss_types.cas");
        Assert.False(result.Success);

        var joined = string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message));
        Assert.Contains("pss.fguess", joined, StringComparison.Ordinal);
        Assert.Contains("expects 'Frequency'", joined, StringComparison.Ordinal);
        Assert.Contains("pss.tstab", joined, StringComparison.Ordinal);
        Assert.Contains("expects 'Time'", joined, StringComparison.Ordinal);
        Assert.Contains("pss.harmonics", joined, StringComparison.Ordinal);
        Assert.Contains("expects 'Scalar'", joined, StringComparison.Ordinal);
    }
}
