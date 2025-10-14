using System;
using System.IO;
using Cascode.TestSupport;

namespace Cascode.Workspace.Tests;

public sealed class PdkMatchingConfigInvalidYamlTests
{
    [Fact]
    public void Load_InvalidYaml_FallsBackToDefaults_AndCaches()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-badcfg");

        try
        {
            var path = Cascode.Workspace.PdkMatchingConfigManager.GetConfigFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "normalization: [ this is: not valid: yaml");

            // First load should not throw and should return defaults
            var cfg1 = Cascode.Workspace.PdkMatchingConfigManager.Load();
            Assert.NotNull(cfg1.Classify);
            Assert.True(cfg1.Classify.Classes.Count > 0);

            // Second load should hit cache, also returning a non-empty config
            var cfg2 = Cascode.Workspace.PdkMatchingConfigManager.Load();
            Assert.NotNull(cfg2.Classify);
            Assert.True(cfg2.Classify.Classes.Count > 0);
        }
        finally
        {
            Cascode.Workspace.PdkMatchingConfigManager.InvalidateCache();
        }
    }
}
