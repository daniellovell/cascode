using System.Text.Json;
using Cascode.ACIR.Validation;

namespace Cascode.ACIR.Tests;

public class ValidationResultTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddError_Throws_WhenCodeNullOrWhitespace(string? invalidCode)
    {
        var result = new ValidationResult();
        Assert.ThrowsAny<ArgumentException>(() => result.AddError(invalidCode!, "message"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddError_Throws_WhenMessageNullOrWhitespace(string? invalidMessage)
    {
        var result = new ValidationResult();
        Assert.ThrowsAny<ArgumentException>(() => result.AddError("ERC-001", invalidMessage!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddWarning_Throws_WhenCodeNullOrWhitespace(string? invalidCode)
    {
        var result = new ValidationResult();
        Assert.ThrowsAny<ArgumentException>(() => result.AddWarning(invalidCode!, "message"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddWarning_Throws_WhenMessageNullOrWhitespace(string? invalidMessage)
    {
        var result = new ValidationResult();
        Assert.ThrowsAny<ArgumentException>(() => result.AddWarning("ERC-005", invalidMessage!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddInfo_Throws_WhenCodeNullOrWhitespace(string? invalidCode)
    {
        var result = new ValidationResult();
        Assert.ThrowsAny<ArgumentException>(() => result.AddInfo(invalidCode!, "message"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddInfo_Throws_WhenMessageNullOrWhitespace(string? invalidMessage)
    {
        var result = new ValidationResult();
        Assert.ThrowsAny<ArgumentException>(() => result.AddInfo("INFO-001", invalidMessage!));
    }

    [Fact]
    public void ToJson_EmptyResult_ReturnsValidJson()
    {
        var result = ValidationResult.Success();
        var json = result.ToJson(0);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Empty(doc.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Empty(doc.RootElement.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public void ToJson_WithErrors_IncludesErrorDetails()
    {
        var result = new ValidationResult();
        result.AddError(
            "ERC-001",
            "Floating gate on device M1",
            "M1.G--n_float",
            "Connect gate to driven net"
        );

        var json = result.ToJson(1);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("exitCode").GetInt32());

        var errors = doc.RootElement.GetProperty("errors").EnumerateArray().ToList();
        Assert.Single(errors);
        Assert.Equal("ERC-001", errors[0].GetProperty("code").GetString());
        Assert.Equal("error", errors[0].GetProperty("severity").GetString());
        Assert.Equal("Floating gate on device M1", errors[0].GetProperty("message").GetString());
        Assert.Equal("M1.G--n_float", errors[0].GetProperty("location").GetString());
        Assert.Equal("Connect gate to driven net", errors[0].GetProperty("suggestion").GetString());
    }

    [Fact]
    public void ToJson_WithWarnings_IncludesWarningDetails()
    {
        var result = new ValidationResult();
        result.AddWarning("ERC-005", "Device using generic model", "device M1");

        var json = result.ToJson(0);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().ToList();
        Assert.Single(warnings);
        Assert.Equal("ERC-005", warnings[0].GetProperty("code").GetString());
        Assert.Equal("warning", warnings[0].GetProperty("severity").GetString());
    }

    [Fact]
    public void ToJson_Summary_IncludesCounts()
    {
        var result = new ValidationResult();
        result.AddError("ERC-001", "Error 1");
        result.AddError("ERC-002", "Error 2");
        result.AddWarning("ERC-005", "Warning 1");

        var json = result.ToJson(1);

        using var doc = JsonDocument.Parse(json);
        var summary = doc.RootElement.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("errorCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("warningCount").GetInt32());
    }

    [Fact]
    public void ToJson_NullFields_OmittedFromOutput()
    {
        var result = new ValidationResult();
        result.AddError("ERC-001", "Error without location or suggestion");

        var json = result.ToJson(1);

        using var doc = JsonDocument.Parse(json);
        var errors = doc.RootElement.GetProperty("errors").EnumerateArray().ToList();
        var error = errors[0];

        // location and suggestion should not be present when null
        Assert.False(error.TryGetProperty("location", out _));
        Assert.False(error.TryGetProperty("suggestion", out _));
    }

    [Fact]
    public void ToJson_ExitCode_ReflectsParameter()
    {
        var result = new ValidationResult();
        result.AddError("EMIT-001", "Structural error");

        var jsonExitCode1 = result.ToJson(1);
        var jsonExitCode2 = result.ToJson(2);

        using var doc1 = JsonDocument.Parse(jsonExitCode1);
        using var doc2 = JsonDocument.Parse(jsonExitCode2);

        Assert.Equal(1, doc1.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(2, doc2.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void ToJson_IsValidJson_CanBeDeserialized()
    {
        var result = new ValidationResult();
        result.AddError("ERC-001", "Test error", "location", "suggestion");
        result.AddWarning("ERC-005", "Test warning");

        var json = result.ToJson(1);

        // Should not throw
        using var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }
}
