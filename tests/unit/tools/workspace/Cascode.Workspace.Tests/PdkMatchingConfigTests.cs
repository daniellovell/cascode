using System;
using System.IO;
using Cascode.TestSupport;

namespace Cascode.Workspace.Tests;

public sealed class PdkMatchingConfigTests
{
    [Fact]
    public void EnsureInitialized_WritesDefaultFile_UnderCascodeHome()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-matchingcfg-test1");
        var cfgPath = Cascode.Workspace.PdkMatchingConfigManager.GetConfigFilePath();
        Assert.False(File.Exists(cfgPath));

        var created = Cascode.Workspace.PdkMatchingConfigManager.EnsureInitialized();
        Assert.True(created);
        Assert.True(File.Exists(cfgPath));

        var text = File.ReadAllText(cfgPath);
        Assert.Contains("vendor_prefixes", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceModelMatcher_RespectsVendorPrefixFromConfig()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-matchingcfg-test2");
        // Initialize and then extend vendor prefixes
        Cascode.Workspace.PdkMatchingConfigManager.EnsureInitialized();
        var cfgPath = Cascode.Workspace.PdkMatchingConfigManager.GetConfigFilePath();
        var cfg = Cascode.Workspace.PdkMatchingConfigManager.Load();
        cfg.Normalization.VendorPrefixes.Insert(0, "testprefix__");
        var yaml = Cascode.Workspace.DefaultPdkMatchingPatterns.RenderYaml(cfg);
        File.WriteAllText(cfgPath, yaml);
        Cascode.Workspace.PdkMatchingConfigManager.InvalidateCache();

        var devices = new[]
        {
            new Cascode.Workspace.Device
            {
                LibraryName = "lib",
                LibraryPath = "/tmp/lib",
                CellName = "testprefix__nfet_01v8_lvt",
                CellPath = "/tmp/lib/testprefix__nfet_01v8_lvt",
                Class = Cascode.Workspace.DeviceClass.Nmos,
                HasLayout = true,
                HasSymbol = true,
                Views = new [] { "layout", "symbol" },
                VtTags = new [] { "LVT" },
                VddTags = new [] { "01v8" },
                Tags = Array.Empty<string>()
            }
        };

        var models = new[]
        {
            new Cascode.Workspace.SpectreModel(
                name: "nfet_01v8__model",
                modelType: "model",
                deviceClass: Cascode.Workspace.DeviceClass.Nmos,
                voltageDomain: "1.8V",
                thresholdFlavor: "LVT",
                corners: Array.Empty<string>(),
                cornerDetails: Array.Empty<string>(),
                sections: Array.Empty<string>(),
                sourceFiles: Array.Empty<string>(),
                decks: Array.Empty<string>())
        };

        var matches = Cascode.Workspace.DeviceModelMatcher.Match(devices, models);
        Assert.NotEmpty(matches);
        Assert.Contains(matches, m => m.DeviceCanonicalName == devices[0].CanonicalName && m.ModelName == models[0].Name);
    }

    [Fact]
    public void DeviceModelMatcher_MatchesUsingUnpaddedVddTokens()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-matchingcfg-vddtokens");
        Cascode.Workspace.PdkMatchingConfigManager.EnsureInitialized();
        var cfgPath = Cascode.Workspace.PdkMatchingConfigManager.GetConfigFilePath();
        var defaults = Cascode.Workspace.DefaultPdkMatchingPatterns.RenderYaml(Cascode.Workspace.DefaultPdkMatchingPatterns.Build());
        File.WriteAllText(cfgPath, defaults);
        Cascode.Workspace.PdkMatchingConfigManager.InvalidateCache();

        var devices = new[]
        {
            new Cascode.Workspace.Device
            {
                LibraryName = "lib",
                LibraryPath = "/tmp/lib",
                CellName = "nfet_03v3_lvt",
                CellPath = "/tmp/lib/nfet_03v3_lvt",
                Class = Cascode.Workspace.DeviceClass.Nmos,
                HasLayout = true,
                HasSymbol = true,
                Views = new [] { "layout", "symbol" },
                VtTags = new [] { "LVT" },
                VddTags = new [] { "3v3" },
                Tags = Array.Empty<string>()
            }
        };

        var models = new[]
        {
            new Cascode.Workspace.SpectreModel(
                name: "nfet_03v3__model",
                modelType: "model",
                deviceClass: Cascode.Workspace.DeviceClass.Nmos,
                voltageDomain: "3.3V",
                thresholdFlavor: "LVT",
                corners: Array.Empty<string>(),
                cornerDetails: Array.Empty<string>(),
                sections: Array.Empty<string>(),
                sourceFiles: Array.Empty<string>(),
                decks: Array.Empty<string>())
        };

        var matches = Cascode.Workspace.DeviceModelMatcher.Match(devices, models);
        Assert.NotEmpty(matches);
        Assert.Contains(matches, m => m.DeviceCanonicalName == devices[0].CanonicalName && m.ModelName == models[0].Name);
    }
}
