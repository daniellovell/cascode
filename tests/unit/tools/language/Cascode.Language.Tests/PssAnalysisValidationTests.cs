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
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1GHz)
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
            d =>
                d.Message.Contains(
                    "missing required parameter 'stabilization_time'",
                    StringComparison.Ordinal
                )
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
    PSSAnalysis pss = new PSSAnalysis(
      guess_frequency=1V,
      stabilization_time=1Hz,
      harmonics=1ns,
      iterations=1Hz,
      steady_coef=1ns,
      uic=1V)
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
        Assert.Contains("pss.guess_frequency", joined, StringComparison.Ordinal);
        Assert.Contains("expects 'Frequency'", joined, StringComparison.Ordinal);
        Assert.Contains("pss.stabilization_time", joined, StringComparison.Ordinal);
        Assert.Contains("expects 'Time'", joined, StringComparison.Ordinal);
        Assert.Contains("pss.harmonics", joined, StringComparison.Ordinal);
        Assert.Contains("expects 'Scalar'", joined, StringComparison.Ordinal);
        Assert.Contains("pss.iterations", joined, StringComparison.Ordinal);
        Assert.Contains("pss.steady_coef", joined, StringComparison.Ordinal);
        Assert.Contains("pss.uic", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void PssAnalysis_ConstantHarmonicsMustBePositiveInteger()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BadPssHarmonics {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1GHz, stabilization_time=1ns, harmonics=0.5)
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
        var result = CascodeReader.TryRead(reader, "bad_pss_harmonics.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains("pss.harmonics", StringComparison.Ordinal)
                && d.Message.Contains(">= 1", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void PssAnalysis_UicMustBeZeroOrOne()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BadPssUic {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1GHz, stabilization_time=1ns, harmonics=3, uic=2)
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
        var result = CascodeReader.TryRead(reader, "bad_pss_uic.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains("pss.uic", StringComparison.Ordinal)
                && d.Message.Contains("must be 0 or 1", StringComparison.Ordinal)
                && d.Message.Contains("2", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void PssAnalysis_UicRejectsNonInteger()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BadPssUicNonInt {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1GHz, stabilization_time=1ns, harmonics=3, uic=0.5)
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
        var result = CascodeReader.TryRead(reader, "bad_pss_uic_nonint.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains("pss.uic", StringComparison.Ordinal)
                && d.Message.Contains("must be 0 or 1", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void PssAnalysis_IterationsMustBePositiveInteger()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BadPssIterations {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1GHz, stabilization_time=1ns, harmonics=3, iterations=0)
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
        var result = CascodeReader.TryRead(reader, "bad_pss_iterations.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains("pss.iterations", StringComparison.Ordinal)
                && d.Message.Contains(">= 1", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void PssAnalysis_IterationsRejectsNonInteger()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BadPssIterationsNonInt {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1GHz, stabilization_time=1ns, harmonics=3, iterations=0.5)
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
        var result = CascodeReader.TryRead(reader, "bad_pss_iterations_nonint.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains("pss.iterations", StringComparison.Ordinal)
                && d.Message.Contains("integer >= 1", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void PssAnalysis_SteadyCoefMustBeNonNegative()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench BadPssSteadyCoef {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1GHz, stabilization_time=1ns, harmonics=3, steady_coef=-0.1)
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
        var result = CascodeReader.TryRead(reader, "bad_pss_steady_coef.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains("pss.steady_coef", StringComparison.Ordinal)
                && d.Message.Contains(">= 0", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void PssAnalysis_StartStopResolveToTime()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench PssStartStopTime {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1GHz, stabilization_time=1ns, harmonics=3)
  }}

  measurements {{
    measurement StartTime : s {{
      return pss.start
    }}
    measurement StopTime : s {{
      return pss.stop
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "pss_start_stop_time.cas");
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message))
        );
    }

    [Fact]
    public void PssAnalysis_StartStopRejectFrequencyContext()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench PssStartStopFrequencyMismatch {{
  resp OUT : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1GHz, stabilization_time=1ns, harmonics=3)
  }}

  measurements {{
    measurement Bad : Hz {{
      return pss.start
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "pss_start_stop_freq_mismatch.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains("Return type 'Time'", StringComparison.Ordinal)
                && d.Message.Contains("expected 'Frequency'", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void PssAnalysis_RequiresRespTerminalDuringSemanticCheck()
    {
        var cascode =
            $@"VERSION {CascodeVersion.Current}

bench MissingPssResp {{
  stim IN : analog

  analysis {{
    PSSAnalysis pss = new PSSAnalysis(guess_frequency=1GHz, stabilization_time=1ns, harmonics=3)
  }}

  measurements {{
    measurement Freq : Hz {{
      return 1Hz
    }}
  }}
}}
";

        using var reader = new StringReader(cascode);
        var result = CascodeReader.TryRead(reader, "missing_pss_resp.cas");
        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Message.Contains("PSSAnalysis 'pss'", StringComparison.Ordinal)
                && d.Message.Contains("resp terminal", StringComparison.OrdinalIgnoreCase)
        );
    }
}
