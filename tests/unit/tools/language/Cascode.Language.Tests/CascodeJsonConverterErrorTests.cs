using System;
using System.Text.Json;
using Cascode.Language.Json;

namespace Cascode.Language.Tests;

public class CascodeJsonConverterErrorTests
{
    [Fact]
    public void FromJson_MalformedJson_ReturnsError()
    {
        var json = "{ invalid json";

        var result = CascodeJsonConverter.FromJson(json);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0009")
        );
    }

    [Fact]
    public void FromJson_MissingRequiredFields_ReturnsError()
    {
        var json = $@"{{ ""acirVersion"": ""{CascodeVersion.Current}"" }}";

        var result = CascodeJsonConverter.FromJson(json);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0009")
        );
    }

    [Fact]
    public void FromJson_InvalidVersionFormat_ReturnsError()
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

        var result = CascodeJsonConverter.FromJson(json);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0002")
        );
    }

    [Fact]
    public void FromJson_NonIntegerMajorVersion_ReturnsError()
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

        var result = CascodeJsonConverter.FromJson(json);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Severity == DiagnosticSeverity.Error
                && d.Message.Contains("CAS0002")
                && d.Message.Contains("abc")
        );
    }

    [Fact]
    public void FromJson_NonIntegerMinorVersion_ReturnsError()
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

        var result = CascodeJsonConverter.FromJson(json);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Severity == DiagnosticSeverity.Error
                && d.Message.Contains("CAS0002")
                && d.Message.Contains("xyz")
        );
    }

    [Fact]
    public void FromJson_MissingDotSeparator_ReturnsError()
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

        var result = CascodeJsonConverter.FromJson(json);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Severity == DiagnosticSeverity.Error
                && d.Message.Contains("CAS0002")
                && d.Message.Contains("11")
        );
    }

    [Fact]
    public void FromJson_EmptyVersionString_ReturnsError()
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

        var result = CascodeJsonConverter.FromJson(json);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0002")
        );
    }

    [Fact]
    public void FromJson_NullDocument_ReturnsError()
    {
        var json = "null";

        var result = CascodeJsonConverter.FromJson(json);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0009")
        );
    }

    [Fact]
    public void FromJson_MissingCircuitName_ReturnsError()
    {
        var json =
            $@"{{
            ""acirVersion"": ""{CascodeVersion.Current}"",
            ""circuit"": {{ ""level"": ""EL"" }},
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }}";

        var result = CascodeJsonConverter.FromJson(json);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0009")
        );
    }

    [Fact]
    public void FromJson_InvalidLevel_ReturnsError()
    {
        var json =
            $@"{{
            ""acirVersion"": ""{CascodeVersion.Current}"",
            ""circuit"": {{ ""name"": ""Test"", ""level"": ""XL"" }},
            ""supplies"": [],
            ""grounds"": [],
            ""ports"": [],
            ""nets"": [],
            ""components"": [],
            ""benches"": []
        }}";

        var result = CascodeJsonConverter.FromJson(json);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d =>
                d.Severity == DiagnosticSeverity.Error
                && d.Message.Contains("CAS0008")
                && d.Message.Contains("XL")
        );
    }
}
