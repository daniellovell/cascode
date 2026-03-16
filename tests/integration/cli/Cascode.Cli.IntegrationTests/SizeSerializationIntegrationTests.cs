using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        var sourceText =
            $@"VERSION {CascodeVersion.Current}

primitive NMOS_Level1(size primSize) implements NMOS {{
  device ""nmos_level1""
  params {{
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }}
}}

circuit WriterRoundTrip(size InputPair = size(W=2u, L=180n, M=1), size Tail = size(W=4u, L=180n, M=1)) {{
  level EL
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  fill {{
    net tail_node : analog
    NMOS M1 = new NMOS_Level1(InputPair) {{
      .B--GND
      .D--OUT
      .G--IN
      .S--tail_node
    }}
    NMOS M2 = new NMOS_Level1(Tail) {{
      .B--GND
      .D--tail_node
      .G--IN
      .S--GND
    }}
  }}
}}";

        var doc = CascodeReader.Parse(sourceText, "writer-roundtrip-source.cas");

        var roundTripPath = Path.Combine(_outputDir, "writer-roundtrip.cas");
        await using (var writer = File.CreateText(roundTripPath))
        {
            CascodeWriter.Write(doc, writer);
        }

        var roundTripText = await File.ReadAllTextAsync(roundTripPath);
        Assert.Contains(
            "circuit WriterRoundTrip(size InputPair = size(L=180n, M=1, W=2u), size Tail = size(L=180n, M=1, W=4u)) {",
            roundTripText,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "NMOS M1 = new NMOS_Level1(InputPair)",
            roundTripText,
            StringComparison.Ordinal
        );
        Assert.Contains("NMOS M2 = new NMOS_Level1(Tail)", roundTripText, StringComparison.Ordinal);

        var reparsedDoc = CascodeReader.Parse(roundTripText, roundTripPath);
        var expectedCircuitSizes = new[]
        {
            "WriterRoundTrip:InputPair:L=180n|M=1|W=2u",
            "WriterRoundTrip:Tail:L=180n|M=1|W=4u",
        };
        var expectedDeviceSizeUsages = new[]
        {
            "WriterRoundTrip:M1:named:InputPair",
            "WriterRoundTrip:M2:named:Tail",
        };

        Assert.Equal(expectedCircuitSizes, GetCircuitSizeSnapshots(doc));
        Assert.Equal(expectedCircuitSizes, GetCircuitSizeSnapshots(reparsedDoc));
        Assert.Equal(expectedDeviceSizeUsages, GetDeviceSizeSnapshots(doc));
        Assert.Equal(expectedDeviceSizeUsages, GetDeviceSizeSnapshots(reparsedDoc));
        Assert.Equal(GetCircuitSizeSnapshots(doc), GetCircuitSizeSnapshots(reparsedDoc));
        Assert.Equal(GetDeviceSizeSnapshots(doc), GetDeviceSizeSnapshots(reparsedDoc));

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
        Assert.Contains("WriterRoundTrip.sp", emit.Stdout);
    }

    [Fact]
    public async Task Emit_FromJsonRoundTrip_PreservesSizePacks()
    {
        // JSON conversion only supports a single EL circuit, so keep this input single-circuit.
        var cascode =
            $@"VERSION {CascodeVersion.Current}

primitive NMOS_Level1(size primSize) implements NMOS {{
  device ""nmos_level1""
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
    NMOS M1 = new NMOS_Level1(InputPair) {{
      .B--GND
      .D--OUT
      .G--IN
      .S--t
    }}
    NMOS M2 = new NMOS_Level1(size(W=2u, L=180n, M=1)) {{
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

    private static string[] GetCircuitSizeSnapshots(CascodeDocument document)
    {
        return document
            .Circuits.SelectMany(circuit =>
                circuit.Sizes.Select(size =>
                    $"{circuit.Name}:{size.Name}:{FormatSizePack(size.Default)}"
                )
            )
            .OrderBy(snapshot => snapshot, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] GetDeviceSizeSnapshots(CascodeDocument document)
    {
        return document
            .Circuits.SelectMany(circuit =>
                (circuit.Fill?.Devices ?? new List<DeviceDeclaration>()).Select(device =>
                    $"{circuit.Name}:{device.Id}:{FormatDeviceSize(device)}"
                )
            )
            .OrderBy(snapshot => snapshot, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FormatDeviceSize(DeviceDeclaration device)
    {
        if (device.SizeName is not null)
        {
            return $"named:{device.SizeName}";
        }

        if (device.Size is not null)
        {
            return $"inline:{FormatSizePack(device.Size)}";
        }

        return "none";
    }

    private static string FormatSizePack(SizePack? pack)
    {
        if (pack is null)
        {
            return "<required>";
        }

        return string.Join(
            "|",
            pack.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}={entry.Value}")
        );
    }
}
