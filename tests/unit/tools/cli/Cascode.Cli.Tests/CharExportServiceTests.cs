using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Cascode.Cli.Services;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class CharExportServiceTests
{
    [Fact]
    public void ExportDerived_FromResultsCsv_ComputesDerivedMetrics()
    {
        using var tmpDir = new TemporaryDirectory();
        var csv = """
point_index,vgs,vds,id,gm,gds,cgg,cds
0,0,0.9,1e-6,2e-3,1e-5,1e-12,3e-13
1,0.01,0.9,2e-6,3e-3,2e-5,2e-12,4e-13
""";
        File.WriteAllText(Path.Combine(tmpDir.Path, "results.csv"), csv);

        var ok = CharExportService.ExportDerived(
            tmpDir.Path,
            metricFilter: null,
            out var derivedPath,
            out var message
        );

        Assert.True(ok, message);
        Assert.True(File.Exists(derivedPath));

        var lines = File.ReadAllLines(derivedPath);
        Assert.Equal(3, lines.Length);

        var header = lines[0].Split(',', StringSplitOptions.RemoveEmptyEntries);
        var vgsIdx = Array.IndexOf(header, "vgs");
        var vdIdx = Array.IndexOf(header, "vds");
        var idIdx = Array.IndexOf(header, "id");
        var gmOverIdIdx = Array.IndexOf(header, "gm_over_id");
        var roIdx = Array.IndexOf(header, "ro");
        var ftIdx = Array.IndexOf(header, "ft");
        var cdsIdx = Array.IndexOf(header, "cds");
        Assert.True(
            vgsIdx >= 0
                && vdIdx >= 0
                && idIdx >= 0
                && gmOverIdIdx >= 0
                && roIdx >= 0
                && ftIdx >= 0
                && cdsIdx >= 0,
            "Required columns missing from derived.csv"
        );

        var r0 = lines[1].Split(',', StringSplitOptions.None);
        Assert.Equal(0.0, double.Parse(r0[vgsIdx], CultureInfo.InvariantCulture), 6);
        Assert.Equal(0.9, double.Parse(r0[vdIdx], CultureInfo.InvariantCulture), 6);
        Assert.Equal(1e-6, double.Parse(r0[idIdx], CultureInfo.InvariantCulture), 12);
        Assert.Equal(2000, double.Parse(r0[gmOverIdIdx], CultureInfo.InvariantCulture), 6);
        Assert.Equal(100000, double.Parse(r0[roIdx], CultureInfo.InvariantCulture), 6);
        Assert.True(double.Parse(r0[ftIdx], CultureInfo.InvariantCulture) > 0);
        Assert.Equal(3e-13, double.Parse(r0[cdsIdx], CultureInfo.InvariantCulture), 12);

        var r1 = lines[2].Split(',', StringSplitOptions.None);
        Assert.Equal(0.01, double.Parse(r1[vgsIdx], CultureInfo.InvariantCulture), 6);
        Assert.Equal(0.9, double.Parse(r1[vdIdx], CultureInfo.InvariantCulture), 6);
        Assert.Equal(2e-6, double.Parse(r1[idIdx], CultureInfo.InvariantCulture), 12);
        Assert.Equal(1500, double.Parse(r1[gmOverIdIdx], CultureInfo.InvariantCulture), 6);
        Assert.Equal(4e-13, double.Parse(r1[cdsIdx], CultureInfo.InvariantCulture), 12);
    }

    // Note: legacy Spectre/oppoint recovery is intentionally unsupported.
}
