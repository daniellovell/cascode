using System;
using System.IO;

namespace Cascode.Workspace.Tests;

public sealed class PdkMatchingConfigTests
{
    [Fact]
    public void EnsureInitialized_WritesDefaultFile_UnderCascodeHome()
    {
        using var temp = TestUtilities.TempDirectory.Create("cascode-matchingcfg-test1");
        var home = temp.DirectoryPath;
        Environment.SetEnvironmentVariable("CASCODE_HOME", home);
        try
        {
            var cfgPath = Cascode.Workspace.PdkMatchingConfigManager.GetConfigFilePath();
            Assert.False(File.Exists(cfgPath));

            var created = Cascode.Workspace.PdkMatchingConfigManager.EnsureInitialized();
            Assert.True(created);
            Assert.True(File.Exists(cfgPath));

            var text = File.ReadAllText(cfgPath);
            Assert.Contains("vendor_prefixes", text, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CASCODE_HOME", null);
        }
    }

    [Fact]
    public void DeviceModelMatcher_RespectsVendorPrefixFromConfig()
    {
        using var temp = TestUtilities.TempDirectory.Create("cascode-matchingcfg-test2");
        var home = temp.DirectoryPath;
        Environment.SetEnvironmentVariable("CASCODE_HOME", home);
        try
        {
            // Initialize and then extend vendor prefixes
            Cascode.Workspace.PdkMatchingConfigManager.EnsureInitialized();
            var cfgPath = Cascode.Workspace.PdkMatchingConfigManager.GetConfigFilePath();
            var json = File.ReadAllText(cfgPath);
            // inject an extra vendor prefix
            json = json.Replace("\"vendor_prefixes\": [ \"sky130_fd_pr__\" ]", "\"vendor_prefixes\": [ \"testprefix__\", \"sky130_fd_pr__\" ]");
            File.WriteAllText(cfgPath, json);

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
        finally
        {
            Environment.SetEnvironmentVariable("CASCODE_HOME", null);
        }
    }
}
