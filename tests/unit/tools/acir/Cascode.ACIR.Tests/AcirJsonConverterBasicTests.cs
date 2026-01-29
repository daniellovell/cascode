using System.Collections.Generic;
using System.Text.Json;
using Cascode.Language;
using Cascode.Language.Json;

namespace Cascode.Language.Tests;

public class AcirJsonConverterBasicTests
{
    [Fact]
    public void ToJson_SimpleElCircuit_ProducesValidJson()
    {
        var doc = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(doc);

        Assert.NotEmpty(json);
        var parsed = JsonDocument.Parse(json);
        Assert.Equal(
            "OTA5T",
            parsed.RootElement.GetProperty("circuit").GetProperty("name").GetString()
        );
        Assert.Equal(
            "EL",
            parsed.RootElement.GetProperty("circuit").GetProperty("level").GetString()
        );
    }

    [Fact]
    public void ToJson_IncludesVersion()
    {
        var doc = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        Assert.Equal(
            ACIRVersion.Current,
            parsed.RootElement.GetProperty("acirVersion").GetString()
        );
    }

    [Fact]
    public void ToJson_SerializesPorts()
    {
        var doc = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        var ports = parsed.RootElement.GetProperty("ports");
        Assert.Equal(2, ports.GetArrayLength());
        Assert.Equal("IN", ports[0].GetProperty("name").GetString());
        Assert.Equal("analog", ports[0].GetProperty("kind").GetString());
    }

    [Fact]
    public void ToJson_SerializesComponents()
    {
        var doc = CreateSimpleElCircuit();

        var json = AcirJsonConverter.ToJson(doc);

        var parsed = JsonDocument.Parse(json);
        var components = parsed.RootElement.GetProperty("components");
        Assert.Single(components.EnumerateArray());
        var component = components[0];
        Assert.Equal("nmos", component.GetProperty("kind").GetString());
        Assert.Equal("M1", component.GetProperty("name").GetString());
        Assert.Equal("IN", component.GetProperty("connections").GetProperty("G").GetString());
        Assert.Equal("Level1_NMOS", component.GetProperty("primitive").GetString());
        Assert.Equal("1u", component.GetProperty("size").GetProperty("W").GetString());
    }

    [Fact]
    public void ToJsonDocument_NoElCircuit_Throws()
    {
        var doc = new ACIRDocument
        {
            Circuits = [new Circuit { Name = "TestML", Level = ACIRLevel.ML }],
        };

        var ex = Assert.Throws<ArgumentException>(() => AcirJsonConverter.ToJsonDocument(doc));
        Assert.Contains("No EL-level circuit", ex.Message);
    }

    [Fact]
    public void ToJsonDocument_SpecificCircuitNotFound_Throws()
    {
        var doc = CreateSimpleElCircuit();

        var ex = Assert.Throws<ArgumentException>(() =>
            AcirJsonConverter.ToJsonDocument(doc, "NonExistent")
        );
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void FromJson_ValidJson_ProducesAcirDocument()
    {
        var json =
            $@"{{
            ""acirVersion"": ""{ACIRVersion.Current}"",
            ""circuit"": {{ ""name"": ""TestCircuit"", ""level"": ""EL"" }},
            ""supplies"": [""VDD""],
            ""grounds"": [""GND""],
            ""ports"": [{{ ""name"": ""IN"", ""direction"": ""input"", ""kind"": ""analog"" }}],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }}";

        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var doc = result.Document!;

        Assert.NotNull(doc);
        Assert.Single(doc.Circuits);
        Assert.Equal("TestCircuit", doc.Circuits[0].Name);
        Assert.Equal(ACIRLevel.EL, doc.Circuits[0].Level);
        Assert.Equal("VDD", doc.Circuits[0].Supplies[0]);
    }

    [Fact]
    public void FromJson_WithComponents_ParsesCorrectly()
    {
        var json =
            $@"{{
            ""acirVersion"": ""{ACIRVersion.Current}"",
            ""circuit"": {{ ""name"": ""Test"", ""level"": ""EL"" }},
            ""supplies"": [""VDD""],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [{{
                ""kind"": ""nmos"",
                ""name"": ""M1"",
                ""primitive"": ""Level1_NMOS"",
                ""connections"": {{ ""G"": ""IN"", ""D"": ""OUT"", ""S"": ""GND"", ""B"": ""GND"" }},
                ""size"": {{ ""W"": ""1u"", ""L"": ""180n"" }}
            }}],
            ""benches"": []
        }}";

        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var doc = result.Document!;

        var device = doc.Circuits[0].Fill!.Devices[0];
        Assert.Equal("nmos", device.DeviceType);
        Assert.Equal("M1", device.Id);
        Assert.Equal("IN", device.Bindings["G"]);
        Assert.Equal("Level1_NMOS", device.Primitive);
        Assert.Equal("1u", device.Size?.Entries["W"]);
    }

    [Fact]
    public void FromJson_MultipleSuppliesAndGrounds_ParsesAllEntries()
    {
        var json =
            $@"{{
            ""acirVersion"": ""{ACIRVersion.Current}"",
            ""circuit"": {{ ""name"": ""TestCircuit"", ""level"": ""EL"" }},
            ""supplies"": [""VDD"", ""VDDA"", ""VDDD""],
            ""grounds"": [""GND"", ""GNDA"", ""GNDD""],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }}";

        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var doc = result.Document!;

        Assert.Equal(3, doc.Circuits[0].Supplies.Count);
        Assert.Equal("VDD", doc.Circuits[0].Supplies[0]);
        Assert.Equal("VDDA", doc.Circuits[0].Supplies[1]);
        Assert.Equal("VDDD", doc.Circuits[0].Supplies[2]);
        Assert.Equal(3, doc.Circuits[0].Grounds.Count);
        Assert.Equal("GND", doc.Circuits[0].Grounds[0]);
        Assert.Equal("GNDA", doc.Circuits[0].Grounds[1]);
        Assert.Equal("GNDD", doc.Circuits[0].Grounds[2]);
    }

    private static ACIRDocument CreateSimpleElCircuit() => TestFixtures.CreateSimpleElCircuit();
}
