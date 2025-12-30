using System;
using System.IO;
using System.Linq;
using Cascode.TestSupport;

namespace Cascode.Workspace.Tests;

public sealed class VddFormattingTests
{
    [Theory]
    [InlineData("01v8", 1.8, "1.8V")]
    [InlineData("00v9", 0.9, "0.9V")]
    [InlineData("05v0", 5.0, "5.0V")]
    [InlineData("01v05", 1.05, "1.05V")]
    public void TokenParsingAndPrettyPrint(
        string token,
        double expectedVolts,
        string expectedPretty
    )
    {
        Assert.True(Cascode.Workspace.VddFormatting.TryTokenToVolts(token, out var volts));
        Assert.Equal(expectedVolts, volts, 3);
        Assert.Equal(expectedPretty, Cascode.Workspace.VddFormatting.TokenToPretty(token));
    }

    [Fact]
    public void WritesNumericVolts_ForDevices()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-vdd-floats-db");
        var dbPath = Path.Combine(
            Cascode.Workspace.PdkMatchingConfigManager.GetCascodeHome(),
            "workspaces",
            "test",
            "pdk.db"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var scan = new Cascode.Workspace.WorkspaceScanResult(
            workspaceRoot: "/tmp/ws",
            libraries: Array.Empty<Cascode.Workspace.WorkspaceLibrary>(),
            modelDecks: Array.Empty<Cascode.Workspace.ModelDeckRecord>(),
            models: Array.Empty<Cascode.Workspace.SpectreModel>(),
            warnings: Array.Empty<string>()
        );
        var devices = new[]
        {
            new Cascode.Workspace.Device
            {
                LibraryName = "lib",
                LibraryPath = "/tmp/lib",
                CellName = "nfet_01v8_lvt",
                CellPath = "/tmp/lib/nfet_01v8_lvt",
                Class = Cascode.Workspace.DeviceClass.Nmos,
                HasLayout = true,
                HasSymbol = true,
                Views = new[] { "layout", "symbol" },
                VtTags = new[] { "LVT" },
                VddTags = new[] { "01v8" },
                Tags = Array.Empty<string>(),
            },
        };

        Cascode.Workspace.PdkDatabaseWriter.Write(dbPath, scan, devices);

        using (var db = Cascode.Workspace.PdkDatabase.OpenReadOnly(dbPath))
        {
            using var cmd2 = db.Connection.CreateCommand();
            cmd2.CommandText = @"SELECT vdd_tags FROM devices WHERE cell_name='nfet_01v8_lvt'";
            var obj2 = cmd2.ExecuteScalar();
            Assert.NotNull(obj2);
            Assert.Equal(1.8, Convert.ToDouble(obj2));
        }
    }
}
