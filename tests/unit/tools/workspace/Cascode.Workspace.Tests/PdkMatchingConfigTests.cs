using System;
using System.IO;
using Cascode.TestSupport;

namespace Cascode.Workspace.Tests;

public sealed class PdkMatchingConfigTests
{
    // Test helpers to keep setup consistent and readable across cases.
    private static void ResetConfigToDefaults()
    {
        Cascode.Workspace.PdkMatchingConfigManager.EnsureInitialized();
        var cfgPath = Cascode.Workspace.PdkMatchingConfigManager.GetConfigFilePath();
        var defaultsYaml = Cascode.Workspace.DefaultPdkMatchingPatterns.RenderYaml(Cascode.Workspace.DefaultPdkMatchingPatterns.Build());
        File.WriteAllText(cfgPath, defaultsYaml);
        Cascode.Workspace.PdkMatchingConfigManager.InvalidateCache();
    }

    private static void ResetConfig(Func<Cascode.Workspace.PdkMatchingConfig, Cascode.Workspace.PdkMatchingConfig> mutate)
    {
        Cascode.Workspace.PdkMatchingConfigManager.EnsureInitialized();
        var cfgPath = Cascode.Workspace.PdkMatchingConfigManager.GetConfigFilePath();
        var cfg = mutate(Cascode.Workspace.DefaultPdkMatchingPatterns.Build());
        var yaml = Cascode.Workspace.DefaultPdkMatchingPatterns.RenderYaml(cfg);
        File.WriteAllText(cfgPath, yaml);
        Cascode.Workspace.PdkMatchingConfigManager.InvalidateCache();
    }

    private static Cascode.Workspace.Device MakeDevice(string cellName, Cascode.Workspace.DeviceClass cls = Cascode.Workspace.DeviceClass.Nmos)
    {
        return new Cascode.Workspace.Device
        {
            LibraryName = "lib",
            LibraryPath = "/tmp/lib",
            CellName = cellName,
            CellPath = "/tmp/lib/" + cellName,
            Class = cls,
            HasLayout = true,
            HasSymbol = true,
            Views = new[] { "layout", "symbol" },
            VtTags = Cascode.Workspace.NameNormalization.ExtractVtTags(cellName),
            VddTags = Cascode.Workspace.NameNormalization.ExtractVddTags(cellName),
            Tags = Array.Empty<string>()
        };
    }

    private static Cascode.Workspace.SpectreModel MakeModel(string name, string voltageDomain, Cascode.Workspace.DeviceClass cls = Cascode.Workspace.DeviceClass.Nmos, string type = "model", string vt = "LVT")
    {
        return new Cascode.Workspace.SpectreModel(
            name: name,
            modelType: type,
            deviceClass: cls,
            voltageDomain: voltageDomain,
            thresholdFlavor: vt,
            corners: Array.Empty<string>(),
            cornerDetails: Array.Empty<string>(),
            sections: Array.Empty<string>(),
            sourceFiles: Array.Empty<string>(),
            decks: Array.Empty<string>());
    }

    private static Cascode.Workspace.DeviceModelMatchRecord AssertSingleMatch(Cascode.Workspace.Device device, params Cascode.Workspace.SpectreModel[] models)
    {
        var matches = Cascode.Workspace.DeviceModelMatcher.Match(new[] { device }, models);
        var match = Assert.Single(matches);
        Assert.Equal(device.CanonicalName, match.DeviceCanonicalName);
        Assert.NotEqual("ambiguous", match.Quality);
        return match;
    }
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
        ResetConfig(cfg => { cfg.Normalization.VendorPrefixes.Insert(0, "testprefix__"); return cfg; });

        var dev = MakeDevice("testprefix__nfet_01v8_lvt");
        var model = MakeModel("nfet_01v8__model", "1.8V");
        var match = AssertSingleMatch(dev, model);
        Assert.Equal(model.Name, match.ModelName);
    }

    [Fact]
    public void DeviceModelMatcher_NormalizesVddTokens()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-matchingcfg-vddtokens");
        ResetConfigToDefaults();

        var dev = MakeDevice("nfet_03v3_lvt");
        var m1 = MakeModel("nfet_03v3__model", "3.3V");
        var m2 = MakeModel("nfet_01v8__model", "1.8V");
        var match = AssertSingleMatch(dev, m1, m2);
        Assert.Equal(m1.Name, match.ModelName);
    }

    [Theory]
    [InlineData("nfet_01v2_lvt", "1.2V")]
    [InlineData("nfet_00v9_lvt", "0.9V")]
    [InlineData("nfet_05v0_lvt", "5.0V")]
    [InlineData("nfet_01v05_lvt", "1.05V")]
    public void DeviceModelMatcher_NormalizesCommonVddTokens(string cellName, string modelVoltage)
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-matchingcfg-vddvariants");
        ResetConfigToDefaults();

        var dev = MakeDevice(cellName);
        var model = MakeModel(cellName + "__model", modelVoltage);
        var match = AssertSingleMatch(dev, model);
        Assert.Equal(model.Name, match.ModelName);
    }

    [Fact]
    public void DeviceModelMatcher_PrefersExactVddToken()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-matchingcfg-vddexact");
        ResetConfigToDefaults();

        const string cellName = "nfet_02v5_lvt";
        var dev = MakeDevice(cellName);
        var exact = MakeModel("nfet_02v5__model", "2.5V");
        var near = MakeModel("nfet_02v45__model", "2.45V");
        var match = AssertSingleMatch(dev, exact, near);
        Assert.Equal(exact.Name, match.ModelName);
    }

    [Fact]
    public void DeviceModelMatcher_RespectsCustomVddExtractRegex()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-matchingcfg-vddregex");
        ResetConfig(cfg => { cfg.Normalization.VddExtractRegex = @"vdd(?<n>\d+)p(?<f>\d+)"; return cfg; });

        const string cellName = "nfet_01v8_lvt";
        var dev = MakeDevice(cellName);
        var model = MakeModel("nfet_vdd1p8__model", "VDD1P8");
        var match = AssertSingleMatch(dev, model);
        Assert.Equal(model.Name, match.ModelName);
    }
}
