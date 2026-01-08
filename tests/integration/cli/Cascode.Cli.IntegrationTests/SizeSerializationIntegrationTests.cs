using System;
using System.IO;
using System.Threading.Tasks;
using Cascode.ACIR;
using Cascode.ACIR.Json;
using Cascode.Cli.IntegrationTests.Infrastructure;
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
    public async Task Emit_FromACIRWriterRoundTrip_PreservesSizePacks()
    {
        var sourcePath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cir"
        );

        ACIRDocument doc;
        using (var reader = File.OpenText(sourcePath))
        {
            doc = ACIRReader.Read(reader, sourcePath);
        }

        var roundTripPath = Path.Combine(_outputDir, "writer-roundtrip.acir.cir");
        await using (var writer = File.CreateText(roundTripPath))
        {
            ACIRWriter.Write(doc, writer);
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

        CliIntegrationTestHelper.AssertSuccess(emit, "emit failed after ACIRWriter round-trip");
        Assert.Contains("OTA5T_Hierarchical.sp", emit.Stdout);
    }

    [Fact]
    public async Task Emit_FromJsonRoundTrip_PreservesSizePacks()
    {
        // JSON conversion only supports a single EL circuit, so keep this input single-circuit.
        var acir =
            $@"ACIR {ACIRVersion.Current}

circuit SizePackSmoke
  level EL
  supply VDD
  ground GND
  port IN : analog
  port OUT : analog
  size InputPair = (W=2u, L=180n, M=1)
  fill:
    net t : analog
    nmos M1 (B->GND, D->OUT, G->IN, S->t) : size=InputPair nmos
    nmos M2 (B->GND, D->t, G->IN, S->GND) : W=2u L=180n M=1 nmos
";

        var doc = ACIRReader.Parse(acir, "size-smoke.cir");

        var json = AcirJsonConverter.ToJson(doc, "SizePackSmoke");

        var jsonPath = Path.Combine(_outputDir, "roundtrip.acir.json");
        await File.WriteAllTextAsync(jsonPath, json);

        var readResult = AcirJsonConverter.FromJson(
            await File.ReadAllTextAsync(jsonPath),
            jsonPath
        );
        Assert.True(readResult.Success, string.Join(Environment.NewLine, readResult.Diagnostics));
        var roundTripped = readResult.Document!;

        var roundTripPath = Path.Combine(_outputDir, "json-roundtrip.acir.cir");
        await using (var writer = File.CreateText(roundTripPath))
        {
            ACIRWriter.Write(roundTripped, writer);
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
