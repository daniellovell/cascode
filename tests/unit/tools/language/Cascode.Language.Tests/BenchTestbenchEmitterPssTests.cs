using System;
using System.IO;
using System.Linq;
using Cascode.Bench;
using Cascode.Language.BenchRuntime;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class BenchTestbenchEmitterPssTests
{
    [Fact]
    public void EmitAll_EmitsPssCommandAndVoltageWrdata()
    {
        var cascode = """
VERSION 4.0

bench PssBench {
  resp OUT : analog

  fill {
    net gnd : ground
    GND g = new GND() { .GND--gnd }
    Impedor loadZ = new Impedor(Z=50Ohm) {
      .P--OUT
      .N--gnd
    }
  }

  analysis {
    PSSAnalysis pss = new PSSAnalysis(
      fguess=2.4GHz,
      tstab=10ns,
      harmonics=7,
      iterations=1000,
      steady_coef=0.1,
      uic=1)
  }

  measurements {
    measurement Freq : Hz {
      return 1Hz
    }
  }
}

circuit Top {
  level EL
  output OUT : analog

  constraints {
    numeric {
      c_freq = pss::Freq >= 0Hz
    }
  }

  benches {
    bind PssBench as pss {
      bench.OUT--dut.OUT
    }
  }

  fill { }
}
""";

        var tb = EmitTestbench(cascode, instanceName: "pss");
        Assert.Contains(
            "pss 2.4G 10n OUT 1000 7 1000 0.1 uic",
            tb,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains("setplot pss1", tb, StringComparison.Ordinal);
        Assert.Contains("wrdata Top_pss__pss.pss.wrdata v(OUT)", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_EmitsPssCurrentWrdata_WhenCurrentIsRequested()
    {
        var cascode = """
VERSION 4.0

bench PssBench {
  stim IN : analog
  resp OUT : analog

  fill {
    net gnd : ground
    GND g = new GND() { .GND--gnd }

    VSIN vin = new VSIN(A=10mV, freq=1GHz, phase=0deg) {
      .P--IN
      .N--gnd
    }

    Impedor loadZ = new Impedor(Z=50Ohm) {
      .P--OUT
      .N--gnd
    }
  }

  analysis {
    PSSAnalysis pss = new PSSAnalysis(fguess=1GHz, tstab=5ns, harmonics=5)
  }

  measurements {
    measurement InputCurrentPeriod : s {
      CurrentWaveform iin = current(pss, harness.vin.P)
      return duration(iin)
    }
  }
}

circuit Top {
  level EL
  input IN : analog
  output OUT : analog

  constraints {
    numeric {
      c_ip = pss::InputCurrentPeriod >= 0s
    }
  }

  benches {
    bind PssBench as pss {
      bench.IN--dut.IN
      bench.OUT--dut.OUT
    }
  }

  fill { }
}
""";

        var tb = EmitTestbench(cascode, instanceName: "pss");
        Assert.Contains("pss 1G 5n OUT 1000 5 50 1e-3", tb, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "pss 1G 5n OUT 1000 5 50 1e-3 uic",
            tb,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "wrdata Top_pss__pss.pss.currents.wrdata i(Vvin)",
            tb,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void EmitAll_EmitsKickAsAttocapWithInitialCondition()
    {
        var cascode = """
VERSION 4.0

bench PssBench {
  resp OUT : analog

  fill {
    net gnd : ground
    GND g = new GND() { .GND--gnd }
    Kick kick = new Kick(ic=0.25) {
      .P--OUT
      .N--gnd
    }
    Impedor loadZ = new Impedor(Z=50Ohm) {
      .P--OUT
      .N--gnd
    }
  }

  analysis {
    PSSAnalysis pss = new PSSAnalysis(fguess=2.4GHz, tstab=10ns, harmonics=7, uic=1)
  }

  measurements {
    measurement Freq : Hz {
      return 1Hz
    }
  }
}

circuit Top {
  level EL
  output OUT : analog

  constraints {
    numeric {
      c_freq = pss::Freq >= 0Hz
    }
  }

  benches {
    bind PssBench as pss {
      bench.OUT--dut.OUT
    }
  }

  fill { }
}
""";

        var tb = EmitTestbench(cascode, instanceName: "pss");
        Assert.Contains("Ckick OUT gnd 1e-18 ic=", tb, StringComparison.Ordinal);
    }

    private static string EmitTestbench(string cascode, string instanceName)
    {
        var parsed = CascodeReader.TryParse(cascode, "bench_pss.cas");
        Assert.True(
            parsed.Success,
            string.Join(Environment.NewLine, parsed.Diagnostics.Select(d => d.Message))
        );

        using var tmpDir = new TemporaryDirectory();
        var designPath = Path.Combine(tmpDir.Path, "Top.sp");
        File.WriteAllText(designPath, "* dummy design deck");

        BenchTestbenchEmitter.EmitAll(
            parsed.Document!,
            tmpDir.Path,
            BenchBackendType.Ngspice,
            designPaths: new[] { designPath }
        );

        var tbPath = Path.Combine(tmpDir.Path, $"Top_{instanceName}.sp");
        Assert.True(File.Exists(tbPath), $"Expected testbench '{tbPath}' to be written.");
        return File.ReadAllText(tbPath);
    }
}
