using System;
using System.IO;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.Language;
using Cascode.Language.Json;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class SizeSerializationIntegrationTests : IDisposable
{
    private readonly string _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
    private readonly CascodeHomeScope _cascodeHome;
    private readonly string _outputDir;

    public SizeSerializationIntegrationTests()
    {
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(_repoRoot, "size-serialization");
        _outputDir = Path.Combine(_cascodeHome.Path, "out");
        Directory.CreateDirectory(_outputDir);
    }

    [Fact]
    public async Task Emit_FromCascodeWriterRoundTrip_PreservesSizePacks()
    {
        var sourcePath = Path.Combine(
            _repoRoot,
            "tests/golden/cas/hierarchy/OTA5T_Hierarchical.el.cai"
        );

        CascodeDocument doc;
        using (var reader = File.OpenText(sourcePath))
        {
            doc = CascodeReader.Read(reader, sourcePath);
        }

        var roundTripPath = Path.Combine(_outputDir, "writer-roundtrip.cas");
        await using (var writer = File.CreateText(roundTripPath))
        {
            CascodeWriter.Write(doc, writer);
        }

        var emit = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            roundTripPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emit, "emit failed after CascodeWriter round-trip");
        Assert.Contains("OTA5T_Hierarchical.sp", emit.Stdout);
    }

    [Fact]
    public async Task Emit_FromJsonRoundTrip_PreservesSizePacks()
    {
        // JSON conversion only supports a single EL circuit, so keep this input single-circuit.
        var cascode =
            $@"VERSION {CascodeVersion.Current}

primitive Level1_NMOS(size primSize) implements NMOS {{
  device ""level1_nmos""
  params {{
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }}
}}

circuit SizePackSmoke(size InputPair = size(W=2u, L=180n, M=1)) {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog
  fill {{
    net t : analog
    NMOS M1 = new Level1_NMOS(InputPair) {{
      .B--GND
      .D--OUT
      .G--IN
      .S--t
    }}
    NMOS M2 = new Level1_NMOS(size(W=2u, L=180n, M=1)) {{
      .B--GND
      .D--t
      .G--IN
      .S--GND
    }}
  }}
}}
";

        var doc = CascodeReader.Parse(cascode, "size-smoke.cas");

        var json = CascodeJsonConverter.ToJson(doc, "SizePackSmoke");

        var jsonPath = Path.Combine(_outputDir, "roundtrip.cascode.json");
        await File.WriteAllTextAsync(jsonPath, json);

        var readResult = CascodeJsonConverter.FromJson(
            await File.ReadAllTextAsync(jsonPath),
            jsonPath
        );
        Assert.True(readResult.Success, string.Join(Environment.NewLine, readResult.Diagnostics));
        var roundTripped = readResult.Document!;

        var roundTripPath = Path.Combine(_outputDir, "json-roundtrip.cas");
        await using (var writer = File.CreateText(roundTripPath))
        {
            CascodeWriter.Write(roundTripped, writer);
        }

        var emit = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            roundTripPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emit, "emit failed after JSON round-trip");
        Assert.True(File.Exists(Path.Combine(_outputDir, "SizePackSmoke.sp")));
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
    }
}
