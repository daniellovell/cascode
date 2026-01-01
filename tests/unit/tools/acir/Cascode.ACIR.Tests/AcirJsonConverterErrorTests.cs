using System;
using System.Text.Json;
using Cascode.ACIR.Json;

namespace Cascode.ACIR.Tests;

public class AcirJsonConverterErrorTests
{
    [Fact]
    public void FromJson_MalformedJson_ThrowsJsonException()
    {
        var json = "{ invalid json";

        Assert.Throws<JsonException>(() => AcirJsonConverter.FromJson(json));
    }

    [Fact]
    public void FromJson_MissingRequiredFields_ThrowsJsonException()
    {
        var json = $@"{{ ""acirVersion"": ""{ACIRVersion.Current}"" }}";

        Assert.Throws<JsonException>(() => AcirJsonConverter.FromJson(json));
    }

    [Fact]
    public void FromJson_InvalidVersionFormat_ThrowsFormatException()
    {
        var json =
            @"{
            ""acirVersion"": ""invalid"",
            ""circuit"": { ""name"": ""Test"", ""level"": ""EL"" },
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }";

        var ex = Assert.Throws<FormatException>(() => AcirJsonConverter.FromJson(json));
        Assert.Contains("Invalid ACIR version", ex.Message);
    }

    [Fact]
    public void FromJson_NonIntegerMajorVersion_ThrowsFormatException()
    {
        var json =
            @"{
            ""acirVersion"": ""abc.1"",
            ""circuit"": { ""name"": ""Test"", ""level"": ""EL"" },
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }";

        var ex = Assert.Throws<FormatException>(() => AcirJsonConverter.FromJson(json));
        Assert.Contains("Invalid ACIR version", ex.Message);
        Assert.Contains("abc", ex.Message);
    }

    [Fact]
    public void FromJson_NonIntegerMinorVersion_ThrowsFormatException()
    {
        var json =
            @"{
            ""acirVersion"": ""1.xyz"",
            ""circuit"": { ""name"": ""Test"", ""level"": ""EL"" },
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }";

        var ex = Assert.Throws<FormatException>(() => AcirJsonConverter.FromJson(json));
        Assert.Contains("Invalid ACIR version", ex.Message);
        Assert.Contains("xyz", ex.Message);
    }

    [Fact]
    public void FromJson_MissingDotSeparator_ThrowsFormatException()
    {
        var json =
            @"{
            ""acirVersion"": ""11"",
            ""circuit"": { ""name"": ""Test"", ""level"": ""EL"" },
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }";

        var ex = Assert.Throws<FormatException>(() => AcirJsonConverter.FromJson(json));
        Assert.Contains("Invalid ACIR version", ex.Message);
        Assert.Contains("11", ex.Message);
    }

    [Fact]
    public void FromJson_EmptyVersionString_ThrowsFormatException()
    {
        var json =
            @"{
            ""acirVersion"": """",
            ""circuit"": { ""name"": ""Test"", ""level"": ""EL"" },
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }";

        var ex = Assert.Throws<FormatException>(() => AcirJsonConverter.FromJson(json));
        Assert.Contains("Invalid ACIR version", ex.Message);
    }

    [Fact]
    public void FromJson_NullDocument_ThrowsArgumentException()
    {
        var json = "null";

        var ex = Assert.Throws<ArgumentException>(() => AcirJsonConverter.FromJson(json));
        Assert.Contains("Failed to parse JSON document", ex.Message);
    }

    [Fact]
    public void FromJson_MissingCircuitName_ThrowsJsonException()
    {
        var json =
            $@"{{
            ""acirVersion"": ""{ACIRVersion.Current}"",
            ""circuit"": {{ ""level"": ""EL"" }},
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }}";

        Assert.Throws<JsonException>(() => AcirJsonConverter.FromJson(json));
    }
}
