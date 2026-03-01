using System;
using System.IO;
using Cascode.Bench;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class BenchTestbenchEmitterSParamTests
{
    [Fact]
    public void EmitAll_EmitsSingleEndedPortSources_WithDefaultImpedance()
    {
        var cascode = """
VERSION 4.0

bench SpBench {
  resp P1 : analog
  resp P2 : analog

  fill {
    net gnd : ground

    GND g = new GND() {
      .GND--gnd
    }

    Port p1 = new Port(N=1, Z=50Ohm, V=0V) {
      .P--P1
      .N--gnd
    }

    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {
      .P--P2
      .N--gnd
    }
  }

  analysis {
    SPAnalysis sp = new SPAnalysis(space=Log, samples=10, start=1Hz, stop=1kHz)
  }

  measurements {
    measurement Dummy : V {
      return 1V
    }
  }
}

circuit Top {
  level EL
  input IN : analog
  output OUT : analog

  constraints {
    numeric {
      c1 = sp::Dummy >= 0V
    }
  }

  benches {
    bind SpBench as sp {
      bench.P1--dut.IN
      bench.P2--dut.OUT
    }
  }

  fill { }
}
""";

        var tb = EmitTestbench(cascode, instanceName: "sp");

        Assert.Contains("sp dec 10 1 1K 0", tb, StringComparison.Ordinal);
        Assert.Contains("Vp1 P1 gnd DC 0 portnum=1 z0=50", tb, StringComparison.Ordinal);
        Assert.Contains("Vp2 P2 gnd DC 0 portnum=2 z0=50", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_UsesPortImpedanceOverride_WhenProvided()
    {
        var cascode = """
VERSION 4.0

bench SpBench {
  resp P1 : analog
  resp P2 : analog

  fill {
    net gnd : ground

    GND g = new GND() {
      .GND--gnd
    }

    Port p1 = new Port(N=1, Z=75Ohm, V=0V) {
      .P--P1
      .N--gnd
    }

    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {
      .P--P2
      .N--gnd
    }
  }

  analysis {
    SPAnalysis sp = new SPAnalysis(space=Log, samples=10, start=1Hz, stop=1kHz)
  }

  measurements {
    measurement Dummy : V {
      return 1V
    }
  }
}

circuit Top {
  level EL
  input IN : analog
  output OUT : analog

  constraints {
    numeric {
      c1 = sp::Dummy >= 0V
    }
  }

  benches {
    bind SpBench as sp {
      bench.P1--dut.IN
      bench.P2--dut.OUT
    }
  }

  fill { }
}
""";

        var tb = EmitTestbench(cascode, instanceName: "sp");

        Assert.Contains("sp dec 10 1 1K 0", tb, StringComparison.Ordinal);
        Assert.Contains("Vp1 P1 gnd DC 0 portnum=1 z0=75", tb, StringComparison.Ordinal);
        Assert.Contains("Vp2 P2 gnd DC 0 portnum=2 z0=50", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_AppendsNoiseFlag_WhenSpNoiseIsEnabled()
    {
        var cascode = """
VERSION 4.0

bench SpBench {
  resp P1 : analog
  resp P2 : analog

  fill {
    net gnd : ground

    GND g = new GND() {
      .GND--gnd
    }

    Port p1 = new Port(N=1, Z=50Ohm, V=0V) {
      .P--P1
      .N--gnd
    }

    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {
      .P--P2
      .N--gnd
    }
  }

  analysis {
    SPAnalysis sp = new SPAnalysis(space=Log, samples=10, start=1Hz, stop=1kHz, noise=1)
  }

  measurements {
    measurement Dummy : V {
      return 1V
    }
  }
}

circuit Top {
  level EL
  input IN : analog
  output OUT : analog

  constraints {
    numeric {
      c1 = sp::Dummy >= 0V
    }
  }

  benches {
    bind SpBench as sp {
      bench.P1--dut.IN
      bench.P2--dut.OUT
    }
  }

  fill { }
}
""";

        var tb = EmitTestbench(cascode, instanceName: "sp");

        Assert.Contains("sp dec 10 1 1K 1", tb, StringComparison.Ordinal);
        Assert.Contains("wrdata Top_sp__sp.sp.nf.wrdata NF NFmin Rn", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_DoesNotEmitNoiseWrdata_WhenSpNoiseIsDisabled()
    {
        var cascode = """
VERSION 4.0

bench SpBench {
  resp P1 : analog
  resp P2 : analog

  fill {
    net gnd : ground

    GND g = new GND() {
      .GND--gnd
    }

    Port p1 = new Port(N=1, Z=50Ohm, V=0V) {
      .P--P1
      .N--gnd
    }

    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {
      .P--P2
      .N--gnd
    }
  }

  analysis {
    SPAnalysis sp = new SPAnalysis(space=Log, samples=10, start=1Hz, stop=1kHz, noise=0)
  }

  measurements {
    measurement Dummy : V {
      return 1V
    }
  }
}

circuit Top {
  level EL
  input IN : analog
  output OUT : analog

  constraints {
    numeric {
      c1 = sp::Dummy >= 0V
    }
  }

  benches {
    bind SpBench as sp {
      bench.P1--dut.IN
      bench.P2--dut.OUT
    }
  }

  fill { }
}
""";

        var tb = EmitTestbench(cascode, instanceName: "sp");

        Assert.Contains("sp dec 10 1 1K 0", tb, StringComparison.Ordinal);
        Assert.DoesNotContain(".sp.nf.wrdata", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_DoesNotEmitPortSources_WhenNoSPAnalysis()
    {
        var cascode = """
VERSION 4.0

bench DcBench {
  resp P1 : analog
  resp P2 : analog

  fill {
    net gnd : ground

    GND g = new GND() {
      .GND--gnd
    }

    Port p1 = new Port(N=1, Z=50Ohm, V=0V) {
      .P--P1
      .N--gnd
    }

    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {
      .P--P2
      .N--gnd
    }
  }

  analysis {
    DCAnalysis dc = new DCAnalysis()
  }

  measurements {
    measurement Dummy : V {
      return 1V
    }
  }
}

circuit Top {
  level EL
  input IN : analog
  output OUT : analog

  constraints {
    numeric {
      c1 = dc::Dummy >= 0V
    }
  }

  benches {
    bind DcBench as dc {
      bench.P1--dut.IN
      bench.P2--dut.OUT
    }
  }

  fill { }
}
""";

        var tb = EmitTestbench(cascode, instanceName: "dc");

        Assert.DoesNotContain(" portnum ", tb, StringComparison.Ordinal);
        Assert.DoesNotContain("* ports", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_ResolvesEnvImpedance_ForPortImpedance()
    {
        var cascode = """
VERSION 4.0

function get_source_impedance(Impedance fallback) : Impedance {
  if env.SourceImpedance { return env.SourceImpedance }
  return fallback
}

bench SpBench {
  resp P1 : analog
  resp P2 : analog

  fill {
    net gnd : ground

    GND g = new GND() {
      .GND--gnd
    }

    Port p1 = new Port(N=1, Z=get_source_impedance(50Ohm), V=0V) {
      .P--P1
      .N--gnd
    }

    Port p2 = new Port(N=2, Z=50Ohm, V=0V) {
      .P--P2
      .N--gnd
    }
  }

  analysis {
    SPAnalysis sp = new SPAnalysis(space=Log, samples=10, start=1Hz, stop=1kHz)
  }

  measurements {
    measurement Dummy : V {
      return 1V
    }
  }
}

circuit Top {
  level EL
  input IN : analog
  output OUT : analog

  env {
    SourceImpedance = 50Ohm
  }

  constraints {
    numeric {
      c1 = sp::Dummy >= 0V
    }
  }

  benches {
    bind SpBench as sp {
      bench.P1--dut.IN
      bench.P2--dut.OUT
    }
  }

  fill { }
}
""";

        var tb = EmitTestbench(cascode, instanceName: "sp");

        Assert.Contains("portnum=1 z0=50", tb, StringComparison.Ordinal);
        Assert.Contains("portnum=2 z0=50", tb, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAll_ResolvesLoadImpedance_ForPortTwoZ0()
    {
        var cascode = """
VERSION 4.0

function get_source_impedance(Impedance fallback) : Impedance {
  if env.SourceImpedance { return env.SourceImpedance }
  return fallback
}

function get_load_impedance(Impedance fallback) : Impedance {
  if env.LoadImpedance { return env.LoadImpedance }
  return fallback
}

bench SpBench {
  resp P1 : analog
  resp P2 : analog

  fill {
    net gnd : ground

    GND g = new GND() {
      .GND--gnd
    }

    Port p1 = new Port(N=1, Z=get_source_impedance(50Ohm), V=0V) {
      .P--P1
      .N--gnd
    }

    Port p2 = new Port(N=2, Z=get_load_impedance(50Ohm), V=0V) {
      .P--P2
      .N--gnd
    }
  }

  analysis {
    SPAnalysis sp = new SPAnalysis(space=Log, samples=10, start=1Hz, stop=1kHz)
  }

  measurements {
    measurement Dummy : V {
      return 1V
    }
  }
}

circuit Top {
  level EL
  input IN : analog
  output OUT : analog

  env {
    SourceImpedance = 50Ohm
    LoadImpedance = 10kOhm
  }

  constraints {
    numeric {
      c1 = sp::Dummy >= 0V
    }
  }

  benches {
    bind SpBench as sp {
      bench.P1--dut.IN
      bench.P2--dut.OUT
    }
  }

  fill { }
}
""";

        var tb = EmitTestbench(cascode, instanceName: "sp");

        Assert.Contains("portnum=1 z0=50", tb, StringComparison.Ordinal);
        Assert.Contains("portnum=2 z0=10K", tb, StringComparison.Ordinal);
    }

    private static string EmitTestbench(string cascode, string instanceName)
    {
        var parsed = CascodeReader.TryParse(cascode, "bench_ports.cas");
        Assert.True(parsed.Success, parsed.Diagnostics.ToString());

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
