using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cascode.Workspace.Tests;

/// <summary>
/// Tests that verify correct device-to-model matching for the sky130 PDK fixture.
/// These tests ensure that devices are matched to their correct models and not
/// incorrectly matched to native/depletion variants or other unrelated models.
/// </summary>
public sealed class DeviceModelMatcherSky130Tests : IDisposable
{
    private readonly Cascode.TestSupport.CascodeHomeScope _home;

    public DeviceModelMatcherSky130Tests()
    {
        _home = Cascode.TestSupport.CascodeHome.CreateInTemp();
    }

    public void Dispose()
    {
        _home.Dispose();
    }

    /// <summary>
    /// Creates a device with the given cell name and class.
    /// </summary>
    private static Device CreateDevice(string cellName, DeviceClass deviceClass, string? vtTag = null, string? vddTag = null)
    {
        return new Device
        {
            LibraryName = "sky130_fd_pr_main",
            LibraryPath = "/test/libs/sky130_fd_pr_main",
            CellName = cellName,
            CellPath = $"/test/libs/sky130_fd_pr_main/{cellName}",
            Class = deviceClass,
            HasLayout = true,
            HasSymbol = true,
            Views = new[] { "layout", "symbol", "spectre" },
            VtTags = vtTag is null ? Array.Empty<string>() : new[] { vtTag },
            VddTags = vddTag is null ? Array.Empty<string>() : new[] { vddTag },
            Tags = Array.Empty<string>()
        };
    }

    /// <summary>
    /// Creates a model with the given name and class.
    /// </summary>
    private static SpectreModel CreateModel(string name, DeviceClass deviceClass, string modelType = "subckt", string? voltageDomain = null, string? thresholdFlavor = null)
    {
        return new SpectreModel(
            name,
            modelType,
            deviceClass,
            voltageDomain,
            thresholdFlavor,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    [Fact]
    public void Match_Nfet01v8_MatchesToCorrectModel_NotNative()
    {
        // Arrange: Create the standard 1.8V NMOS devices
        var devices = new List<Device>
        {
            CreateDevice("nfet_01v8", DeviceClass.Nmos, "SVT", "1.8V"),
            CreateDevice("nfet_01v8_lvt", DeviceClass.Nmos, "LVT", "1.8V"),
        };

        // Arrange: Create models - both standard and native
        var models = new List<SpectreModel>
        {
            // Standard devices - should match
            CreateModel("nfet_01v8", DeviceClass.Nmos, "subckt", "1.8V"),
            CreateModel("nfet_01v8_lvt", DeviceClass.Nmos, "subckt", "1.8V", "LVT"),
            // Native devices - should NOT be matched to standard devices
            CreateModel("nfet_03v3_nvt", DeviceClass.Nmos, "subckt", "3.3V", "NVT"),
            CreateModel("nfet_05v0_nvt", DeviceClass.Nmos, "subckt", "5.0V", "NVT"),
        };

        // Act
        var matches = DeviceModelMatcher.Match(devices, models);

        // Assert: Each device should have a match
        Assert.True(matches.Count >= 2, $"Expected at least 2 matches, got {matches.Count}");

        // Verify specific matches (use first/best match for each device)
        var matchDict = matches
            .GroupBy(m => m.DeviceCanonicalName)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Rank).First().ModelName);

        // nfet_01v8 should match nfet_01v8, NOT nfet_03v3_nvt or nfet_05v0_nvt
        Assert.True(matchDict.ContainsKey("sky130_fd_pr_main__nfet_01v8"));
        Assert.Equal("nfet_01v8", matchDict["sky130_fd_pr_main__nfet_01v8"]);

        // nfet_01v8_lvt should match nfet_01v8_lvt
        Assert.True(matchDict.ContainsKey("sky130_fd_pr_main__nfet_01v8_lvt"));
        Assert.Equal("nfet_01v8_lvt", matchDict["sky130_fd_pr_main__nfet_01v8_lvt"]);
    }

    [Fact]
    public void Match_HighVoltageDevice_MatchesToHighVoltageModel()
    {
        // Arrange: High-voltage devices
        var devices = new List<Device>
        {
            CreateDevice("nfet_20v0", DeviceClass.Nmos, "SVT", "20.0V"),
            CreateDevice("nfet_20v0_zvt", DeviceClass.Nmos, "SVT", "20.0V"),
            CreateDevice("nfet_g5v0d10v5", DeviceClass.Nmos, "SVT", "5.0V"),
        };

        // Arrange: Models with matching high-voltage variants
        // Use vendor prefix to match real PDK behavior
        var models = new List<SpectreModel>
        {
            CreateModel("sky130_fd_pr__nfet_01v8", DeviceClass.Nmos, "subckt", "1.8V"),
            CreateModel("sky130_fd_pr__nfet_20v0", DeviceClass.Nmos, "subckt", "20.0V"),
            CreateModel("sky130_fd_pr__nfet_20v0_zvt", DeviceClass.Nmos, "subckt", "20.0V"),
            CreateModel("sky130_fd_pr__nfet_g5v0d10v5", DeviceClass.Nmos, "subckt", "5.0V"),
        };

        // Act
        var matches = DeviceModelMatcher.Match(devices, models);

        // Assert
        var matchDict = matches
            .GroupBy(m => m.DeviceCanonicalName)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Rank).First().ModelName);

        // nfet_20v0 should match sky130_fd_pr__nfet_20v0 (vendor prefix in model)
        Assert.True(matchDict.ContainsKey("sky130_fd_pr_main__nfet_20v0"));
        Assert.Equal("sky130_fd_pr__nfet_20v0", matchDict["sky130_fd_pr_main__nfet_20v0"]);

        // nfet_20v0_zvt should match sky130_fd_pr__nfet_20v0_zvt
        Assert.True(matchDict.ContainsKey("sky130_fd_pr_main__nfet_20v0_zvt"));
        Assert.Equal("sky130_fd_pr__nfet_20v0_zvt", matchDict["sky130_fd_pr_main__nfet_20v0_zvt"]);

        // nfet_g5v0d10v5 should match sky130_fd_pr__nfet_g5v0d10v5
        Assert.True(matchDict.ContainsKey("sky130_fd_pr_main__nfet_g5v0d10v5"));
        Assert.Equal("sky130_fd_pr__nfet_g5v0d10v5", matchDict["sky130_fd_pr_main__nfet_g5v0d10v5"]);
    }

    [Fact]
    public void Match_StandardDevice_DoesNotMatchNativeModel()
    {
        // Arrange: A standard 1.8V device and only native models available
        var devices = new List<Device>
        {
            CreateDevice("nfet_01v8", DeviceClass.Nmos, "SVT", "1.8V"),
        };

        // Only native models - no standard nfet_01v8
        var models = new List<SpectreModel>
        {
            CreateModel("nfet_03v3_nvt", DeviceClass.Nmos, "subckt", "3.3V", "NVT"),
            CreateModel("nfet_05v0_nvt", DeviceClass.Nmos, "subckt", "5.0V", "NVT"),
        };

        // Act
        var matches = DeviceModelMatcher.Match(devices, models);

        // Assert: Should not match a standard device to a native model
        // The matching should either return no match or a low-quality class-based match
        // that would be filtered by MinAcceptScore
        var nfet01v8Matches = matches.Where(m => m.DeviceCanonicalName == "sky130_fd_pr_main__nfet_01v8").ToList();

        // If there are matches, they should NOT be to native models
        foreach (var match in nfet01v8Matches)
        {
            Assert.DoesNotContain("nvt", match.ModelName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("native", match.ModelName, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Match_NativeDevice_MatchesToNativeModel()
    {
        // Arrange: A native device and both standard and native models
        var devices = new List<Device>
        {
            CreateDevice("nfet_03v3_nvt", DeviceClass.Nmos, "NVT", "3.3V"),
        };

        var models = new List<SpectreModel>
        {
            CreateModel("nfet_01v8", DeviceClass.Nmos, "subckt", "1.8V"),
            CreateModel("nfet_03v3_nvt", DeviceClass.Nmos, "subckt", "3.3V", "NVT"),
        };

        // Act
        var matches = DeviceModelMatcher.Match(devices, models);

        // Assert: Native device should match to native model
        Assert.Single(matches);
        Assert.Equal("nfet_03v3_nvt", matches[0].ModelName);
    }

    [Fact]
    public void Match_LvtDevice_MatchesToLvtModel()
    {
        // Arrange: An LVT device and both standard and LVT models
        var devices = new List<Device>
        {
            CreateDevice("nfet_01v8_lvt", DeviceClass.Nmos, "LVT", "1.8V"),
        };

        var models = new List<SpectreModel>
        {
            CreateModel("nfet_01v8", DeviceClass.Nmos, "subckt", "1.8V"),
            CreateModel("nfet_01v8_lvt", DeviceClass.Nmos, "subckt", "1.8V", "LVT"),
            CreateModel("nfet_01v8_hvt", DeviceClass.Nmos, "subckt", "1.8V", "HVT"),
        };

        // Act
        var matches = DeviceModelMatcher.Match(devices, models);

        // Assert: LVT device should match to LVT model, not standard or HVT
        Assert.Single(matches);
        Assert.Equal("nfet_01v8_lvt", matches[0].ModelName);
    }

    [Fact]
    public void Match_VendorPrefixNormalization_MatchesCorrectly()
    {
        // Arrange: Device without vendor prefix, model with vendor prefix
        var devices = new List<Device>
        {
            CreateDevice("nfet_20v0", DeviceClass.Nmos, "SVT", "20.0V"),
        };

        var models = new List<SpectreModel>
        {
            CreateModel("sky130_fd_pr__nfet_20v0", DeviceClass.Nmos, "subckt", "20.0V"),
        };

        // Act
        var matches = DeviceModelMatcher.Match(devices, models);

        // Assert: Should match despite vendor prefix difference
        Assert.Single(matches);
        Assert.Equal("sky130_fd_pr__nfet_20v0", matches[0].ModelName);
    }
}

