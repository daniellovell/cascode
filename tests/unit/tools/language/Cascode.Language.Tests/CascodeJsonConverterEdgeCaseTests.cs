using System.Text.Json;
using Cascode.Language;
using Cascode.Language.Json;

namespace Cascode.Language.Tests;

public class CascodeJsonConverterEdgeCaseTests
{
    [Fact]
    public void ToJson_EmptyCircuit_HandlesGracefully()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "Empty",
                    Level = CascodeLevel.EL,
                    Supplies = [],
                    Grounds = [],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };

        var json = CascodeJsonConverter.ToJson(doc);
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
        var original = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "Empty",
                    Level = CascodeLevel.EL,
                    Supplies = [],
                    Grounds = [],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };

        var json = CascodeJsonConverter.ToJson(original);
        var result = CascodeJsonConverter.FromJson(json);
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

        var json = CascodeJsonConverter.ToJson(doc);
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

        var json = CascodeJsonConverter.ToJson(original);
        var result = CascodeJsonConverter.FromJson(json);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var roundTripped = result.Document!;

        Assert.Equal("Min", roundTripped.Circuits[0].Name);
        Assert.Equal(CascodeLevel.EL, roundTripped.Circuits[0].Level);
    }

    [Fact]
    public void ToJson_MissingOptionalHarness_OmitsField()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "NoHarness",
                    Level = CascodeLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                    Harness = null,
                },
            ],
        };

        var json = CascodeJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("harness", out _));
    }

    [Fact]
    public void ToJson_MissingOptionalConstraints_OmitsField()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "NoConstraints",
                    Level = CascodeLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                    Constraints = null,
                },
            ],
        };

        var json = CascodeJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("constraints", out _));
    }

    [Fact]
    public void ToJson_EmptyConstraintsBlock_OmitsField()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "EmptyConstraints",
                    Level = CascodeLevel.EL,
                    Supplies = ["VDD"],
                    Grounds = ["GND"],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                    Constraints = new ConstraintsBlock { Bench = [], Physical = [] },
                },
            ],
        };

        var json = CascodeJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("constraints", out _));
    }

    [Fact]
    public void ToJson_EmptyHarnessBlock_OmitsField()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "EmptyHarness",
                    Level = CascodeLevel.EL,
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

        var json = CascodeJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("harness", out _));
    }

    [Fact]
    public void FromJson_EmptyArrays_CreatesEmptyLists()
    {
        var json =
            $@"{{
            ""cascodeVersion"": ""{CascodeVersion.Current}"",
            ""circuit"": {{ ""name"": ""Test"", ""level"": ""EL"" }},
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benchDefinitions"": []
        }}";

        var result = CascodeJsonConverter.FromJson(json);
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
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            BenchDefinitions = [],
            Circuits =
            [
                new Circuit
                {
                    Name = "Test",
                    Level = CascodeLevel.EL,
                    Supplies = [],
                    Grounds = [],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };

        var json = CascodeJsonConverter.ToJson(doc);
        var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("benchDefinitions", out _));
    }

    private static CascodeDocument CreateMinimalCircuit()
    {
        return new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits =
            [
                new Circuit
                {
                    Name = "Min",
                    Level = CascodeLevel.EL,
                    Supplies = [],
                    Grounds = [],
                    Ports = [],
                    Fill = new FillBlock { Devices = [] },
                },
            ],
        };
    }
}
