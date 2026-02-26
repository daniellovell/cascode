using System;
using System.IO;
using System.Linq;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class BenchTestbenchEmitterOpParamTests
{
    private sealed class SubcktIncludeResolver : IBenchIncludeResolver
    {
        private readonly string _deviceKey;

        public SubcktIncludeResolver(string deviceKey)
        {
            _deviceKey = deviceKey;
        }

        public BenchIncludeResolution Resolve(
            Circuit circuit,
            Cascode.Bench.BenchBackendType backend,
            CascodeDocument? document = null
        )
        {
            return new BenchIncludeResolution(
                WithSection: Array.Empty<string>(),
                WithoutSection: Array.Empty<string>(),
                Section: null
            )
            {
                DeviceModelMap = new System.Collections.Generic.Dictionary<
                    string,
                    DeviceModelResolution
                >(StringComparer.OrdinalIgnoreCase)
                {
                    [_deviceKey] = new DeviceModelResolution(ModelName: _deviceKey, IsSubckt: true),
                },
            };
        }
    }

    [Fact]
    public void CompileAllPlans_SkipsUnconstrainedBenchBindings()
    {
        var cascode = """
VERSION 4.0
bench UnusedBench { stim IN : analog measurements { measurement Dummy : V { return 1V } } }
circuit Top {
  level EL
  input IN : analog
  benches { bind UnusedBench as unused { bench.IN--dut.IN } }
  fill { }
}
""";

        var parsed = CascodeReader.TryParse(cascode, "unused_bench.cas");
        Assert.True(parsed.Success, parsed.Diagnostics.ToString());

        var plans = BenchCompiler.CompileAllPlans(parsed.Document!);
        Assert.Empty(plans);
    }

    [Fact]
    public void EmitPlans_UsesPrimitiveOpPath_ForWrapperSubckt()
    {
        var cascode = """
VERSION 4.0

primitive NMOS Wrap(size primSize) {
  device "wrap_key"
  params {
    w = primSize.W
    l = primSize.L
    nf = primSize.M
    __op_path0 = mleaf
  }
}

bench OpBench {
  analysis {
    DCAnalysis dc = new DCAnalysis()
  }

  measurements {
    measurement Gm : S {
      return op_param(dc, dut, gm)
    }
  }
}

circuit Top {
  level EL
  ground GND
  input D : bias
  input G : bias
  input S : bias
  input B : bias

  constraints {
    numeric {
      c_gm = op::Gm >= 0S
    }
  }

  benches {
    bind OpBench as op {
    }
  }

  fill {
    NMOS DUT = new Wrap(size(W=1u, L=1u, M=1)) {
      .D--D
      .G--G
      .S--S
      .B--B
    }
  }
}
""";

        var parsed = CascodeReader.TryParse(cascode, "op_param_wrapper.cas");
        Assert.True(parsed.Success, parsed.Diagnostics.ToString());
        var doc = parsed.Document!;

        using var tmpDir = new TemporaryDirectory();
        var designPath = Path.Combine(tmpDir.Path, "Top.sp");
        File.WriteAllText(designPath, "* dummy design deck");

        BenchTestbenchEmitter.EmitAll(
            doc,
            tmpDir.Path,
            Cascode.Bench.BenchBackendType.Ngspice,
            designPaths: new[] { designPath },
            includeResolver: new SubcktIncludeResolver("wrap_key")
        );

        var tbPath = Path.Combine(tmpDir.Path, "Top_op.sp");
        Assert.True(File.Exists(tbPath), "Expected testbench to be written.");
        var tb = File.ReadAllText(tbPath);

        Assert.Contains(
            "let op_gm = @m.xdut.xdut.mleaf[gm]",
            tb,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
