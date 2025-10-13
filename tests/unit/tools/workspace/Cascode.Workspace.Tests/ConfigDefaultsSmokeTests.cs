using System;
using Cascode.TestSupport;

namespace Cascode.Workspace.Tests;

public sealed class ConfigDefaultsSmokeTests
{
    [Fact]
    public void Defaults_Load_With_Classify_And_Subclasses()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-defaults");
        var cfg = Cascode.Workspace.PdkMatchingConfigManager.Load();
        Assert.NotNull(cfg);
        Assert.NotNull(cfg.Classify);
        Assert.True(cfg.Classify.Classes.Count > 0);
        Assert.True(cfg.Classify.Subclasses.Count > 0);
        Assert.Contains("stdcell", cfg.Classify.Classes.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("stdcell", cfg.Classify.Subclasses.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
