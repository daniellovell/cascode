using System.Text.Json;
using Cascode.Language;
using Cascode.Language.Json;

namespace Cascode.Language.Tests;

public class AcirJsonConverterEdgeCaseTests
{
    [Fact]
    public void ToJson_EmptyCircuit_HandlesGracefully()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "Empty",
                    Level = ACIRLevel.EL,
                    Supplies = [],
                    Grounds = [],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.Equal(
            "Empty",
            parsed.RootElement.GetProperty("circuit").GetProperty("name").GetString()
        );
        Assert.Equal(0, parsed.RootElement.GetProperty("components").GetArrayLength());
    }

    [Fact]
    public void RoundTrip_EmptyCircuit_Preserves()
    {
        var original = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "Empty",
                    Level = ACIRLevel.EL,
                    Supplies = [],
                    Grounds = [],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(original);
        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        Assert.Equal("Empty", roundTripped.Circuits[0].Name);
        Assert.Empty(roundTripped.Circuits[0].Supplies);
        Assert.Empty(roundTripped.Circuits[0].Grounds);
        Assert.Empty(roundTripped.Circuits[0].Ports);
        Assert.Empty(roundTripped.Circuits[0].Fill!.Devices);
    }

    [Fact]
    public void ToJson_MinimalDocument_OnlyRequiredFields()
    {
        var doc = CreateMinimalCircuit();

        var json = AcirJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.Equal(
            "Min",
            parsed.RootElement.GetProperty("circuit").GetProperty("name").GetString()
        );
        Assert.Equal(
            "EL",
            parsed.RootElement.GetProperty("circuit").GetProperty("level").GetString()
        );
        Assert.Equal(0, parsed.RootElement.GetProperty("supplies").GetArrayLength());
        Assert.Equal(0, parsed.RootElement.GetProperty("grounds").GetArrayLength());
        Assert.Equal(0, parsed.RootElement.GetProperty("ports").GetArrayLength());
    }

    [Fact]
    public void RoundTrip_MinimalDocument_Preserves()
    {
        var original = CreateMinimalCircuit();

        var json = AcirJsonConverter.ToJson(original);
        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        Assert.Equal("Min", roundTripped.Circuits[0].Name);
        Assert.Equal(ACIRLevel.EL, roundTripped.Circuits[0].Level);
    }

    [Fact]
    public void ToJson_MissingOptionalHarness_OmitsField()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "NoHarness",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                    Harness = null,
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("harness", out _));
    }

    [Fact]
    public void ToJson_MissingOptionalConstraints_OmitsField()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "NoConstraints",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                    Constraints = null,
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("constraints", out _));
    }

    [Fact]
    public void ToJson_EmptyConstraintsBlock_OmitsField()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "EmptyConstraints",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                    Constraints = new ConstraintsBlock
                    {
                        Numeric = [],
                        Tech = [],
                        Graph = [],
                    },
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("constraints", out _));
    }

    [Fact]
    public void ToJson_EmptyHarnessBlock_OmitsField()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "EmptyHarness",
                    Level = ACIRLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                    Harness = new HarnessBlock
                    {
                        Supplies = [],
                        Biases = [],
                        Loads = [],
                        Sweeps = [],
                    },
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("harness", out _));
    }

    [Fact]
    public void FromJson_EmptyArrays_CreatesEmptyLists()
    {
        var json =
            $@"{{
            ""acirVersion"": ""{ACIRVersion.Current}"",
            ""circuit"": {{ ""name"": ""Test"", ""level"": ""EL"" }},
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benchDefinitions"": []
        }}";

        var result = AcirJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var doc = result.Document!;

        Assert.Empty(doc.Circuits[0].Supplies);
        Assert.Empty(doc.Circuits[0].Grounds);
        Assert.Empty(doc.Circuits[0].Ports);
        Assert.Empty(doc.Circuits[0].Fill!.Nets);
        Assert.Empty(doc.Circuits[0].Fill!.Devices);
        Assert.Empty(doc.BenchDefinitions);
    }

    [Fact]
    public void ToJson_EmptyBenchDefinitions_OmitsBenchDefinitions()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            BenchDefinitions = [],
            Circuits =
            [
                new Circuit
                {
                    Name = "Test",
                    Level = ACIRLevel.EL,
                    Supplies = [],
                    Grounds = [],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };

        var json = AcirJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("benchDefinitions", out _));
    }

    private static ACIRDocument CreateMinimalCircuit()
    {
        return new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "Min",
                    Level = ACIRLevel.EL,
                    Supplies = [],
                    Grounds = [],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };
    }
}
