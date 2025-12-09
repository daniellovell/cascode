using System;
using System.Collections.Generic;
using Cascode.Cli.Commands;
using Cascode.Workspace;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class PdkCommandModuleTests
{
    [Theory]
    [InlineData("1v8", "1.8V")]
    [InlineData("01v05", "1.05V")]
    [InlineData("1.8", "1.8V")]
    [InlineData("1.8V", "1.8V")]
    [InlineData("5", "5.0V")]
    public void TryNormalizeVddFilter_NormalizesToPrettyDisplay(string input, string expected)
    {
        var ok = DeviceFilterEvaluator.TryNormalizeVddFilter(input, out var normalized);

        Assert.True(ok);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void TryNormalizeVddFilter_InvalidTokenReturnsFalse()
    {
        var ok = DeviceFilterEvaluator.TryNormalizeVddFilter("not-a-vdd", out var normalized);

        Assert.False(ok);
        Assert.Equal(string.Empty, normalized);
    }

    [Theory]
    [InlineData("1v8", true)]
    [InlineData("1.8", true)]
    [InlineData("1.8V", true)]
    [InlineData("3.3", false)]
    public void DeviceMatchesVddFilters_ComparesAgainstNormalizedTags(string filter, bool expected)
    {
        var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (DeviceFilterEvaluator.TryNormalizeVddFilter(filter, out var normalized))
        {
            filters.Add(normalized);
        }
        else
        {
            filters.Add(filter.ToLowerInvariant());
        }

        var deviceTags = new[] { VddFormatting.PrettyFromVolts(1.8) };
        var device = new Device { VddTags = deviceTags };
        var options = new DeviceFilterOptions(vdds: filters);

        var result = DeviceFilterEvaluator.Matches(device, options);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void DeviceMatchesVddFilters_FailsWhenDeviceIsMissingVdd()
    {
        var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1.8V" };

        var device = new Device { VddTags = Array.Empty<string>() };
        var options = new DeviceFilterOptions(vdds: filters);

        var result = DeviceFilterEvaluator.Matches(device, options);

        Assert.False(result);
    }
}
