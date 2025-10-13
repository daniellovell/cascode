using System;
using System.IO;
using Cascode.TestSupport;

namespace Cascode.Workspace.Tests;

public sealed class NameNormalizationConfigOverrideTests
{
    [Fact]
    public void ClassifyByName_UsesConfiguredClassPatterns()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-namecfg");
        var cfgPath = Cascode.Workspace.PdkMatchingConfigManager.GetConfigFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);
        var yml = "version: 1\nclassify:\n  classes:\n    nmos:\n      prefixes: [ foo ]\n";
        File.WriteAllText(cfgPath, yml);

        var cfg = Cascode.Workspace.PdkMatchingConfigManager.Load();
        Assert.True(cfg.Classify.Classes.ContainsKey("nmos"));
        Assert.Contains("foo", cfg.Classify.Classes["nmos"].Prefixes ?? new());
        Assert.Equal(DeviceClass.Nmos, NameNormalization.ClassifyByName("foo_bar"));
    }
}
