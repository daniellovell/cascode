using System;
using System.IO;

namespace Cascode.Workspace.Tests;

public sealed class NameNormalizationConfigOverrideTests
{
    [Fact]
    public void ClassifyByName_UsesConfiguredClassPatterns()
    {
        using var temp = TestUtilities.TempDirectory.Create("cascode-namecfg-test");
        var home = temp.DirectoryPath;
        Environment.SetEnvironmentVariable("CASCODE_HOME", home);
        try
        {
            // Minimal YAML overriding classes to map 'foo*' to NMOS
            var cfgPath = Cascode.Workspace.PdkMatchingConfigManager.GetConfigFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);
            var yml = "version: 1\nclassify:\n  classes:\n    nmos:\n      prefixes: [ foo ]\n";
            File.WriteAllText(cfgPath, yml);

            var cfg = Cascode.Workspace.PdkMatchingConfigManager.Load();
            Assert.True(cfg.Classify.Classes.ContainsKey("nmos"));
            Assert.Contains("foo", cfg.Classify.Classes["nmos"].Prefixes ?? new());
            Assert.Equal(DeviceClass.Nmos, NameNormalization.ClassifyByName("foo_bar"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CASCODE_HOME", null);
        }
    }
}
