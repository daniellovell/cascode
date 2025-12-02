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
    public void ExportDerived_FromNutascii_SkipsPointIndices()
    {
        using var tmpDir = new TemporaryDirectory();
        var raw = """
Title: gm_id sample
Date: 12:00:00 PM, Mon Jan 01, 2024
Plotname: DC Analysis `srcSweep': VGS:dc = (0 V -> 0.02 V)
Flags: real
No. Variables:        9
No. Points:      3
Variables:	0	dc	V
		1	s	V plot=0 grid=0
		2	b	V plot=0 grid=0
		3	g	V plot=0 grid=0
		4	d	V plot=0 grid=0
		5	VBS:p	A plot=0 grid=0
		6	VDR:p	A plot=0 grid=0
		7	VGS:p	A plot=0 grid=0
		8	VSS:p	A plot=0 grid=0
Values:
0	0	0	0
	0	0.9	1e-14
	-1e-6	0	1e-6
1	0.01	0	0
	0.01	0.9	1e-14
	-2e-6	0	2e-6
2	0.02	0	0
	0.02	0.9	1e-14
	-3e-6	0	3e-6
""";
        File.WriteAllText(Path.Combine(tmpDir.Path, "sample.raw"), raw);

        var ok = CharExportService.ExportDerived(tmpDir.Path, metricFilter: null, out var derivedPath, out var message);

        Assert.True(ok, message);
        Assert.True(File.Exists(derivedPath));

        var lines = File.ReadAllLines(derivedPath);
        Assert.Equal(4, lines.Length);

        var header = lines[0].Split(',', StringSplitOptions.RemoveEmptyEntries);
        var vgsIdx = Array.IndexOf(header, "vgs");
        var vdIdx = Array.IndexOf(header, "vds");
        var idIdx = Array.IndexOf(header, "id");
        Assert.True(vgsIdx >= 0 && vdIdx >= 0 && idIdx >= 0, "Required columns missing from derived.csv");

        AssertLine(lines[1], vgsIdx, vdIdx, idIdx, 0.0, 0.9, 1e-6);
        AssertLine(lines[2], vgsIdx, vdIdx, idIdx, 0.01, 0.9, 2e-6);
        AssertLine(lines[3], vgsIdx, vdIdx, idIdx, 0.02, 0.9, 3e-6);
    }

    [Fact]
    public void ExportDerived_FromOppointFiles_EmitsOperatingPointMetrics()
    {
        using var tmpDir = new TemporaryDirectory();
        var opp1 = """
Element name = M1
Element type = nmos
Index = 0
Values:
vgs = 0.10
vds = 0.90
ids = 1e-4
gm = 2e-3
gmbs = 1e-4
gds = 1e-5
vth = 0.35
vdsat = 0.1
cgs = 1e-12
cgd = 2e-13
cgg = 1.2e-12
gmoverid = 20
ueff = 0.03
ron = 1000
rseff = 5
rdeff = 6
w_eff = 1e-6
""";
        var opp2 = opp1.Replace("0.10", "0.20", StringComparison.Ordinal).Replace("1e-4", "2e-4", StringComparison.Ordinal).Replace("2e-3", "3e-3", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(tmpDir.Path, "oppoint.0"), opp1);
        File.WriteAllText(Path.Combine(tmpDir.Path, "oppoint.1"), opp2);

        var ok = CharExportService.ExportDerived(tmpDir.Path, metricFilter: null, out var derivedPath, out var message);

        Assert.True(ok, message);
        Assert.True(File.Exists(derivedPath));

        var lines = File.ReadAllLines(derivedPath);
        Assert.Equal(3, lines.Length);

        var header = lines[0].Split(',', StringSplitOptions.RemoveEmptyEntries);
        int idxGmPerW = Array.IndexOf(header, "gm_per_w");
        int idxIdPerW = Array.IndexOf(header, "id_per_w");
        int idxVstar = Array.IndexOf(header, "vstar");
        int idxFt = Array.IndexOf(header, "ft");
        Assert.True(idxGmPerW > 0 && idxIdPerW > 0 && idxVstar > 0 && idxFt > 0, "Missing derived metrics in header.");

        var first = lines[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2000, double.Parse(first[idxGmPerW], CultureInfo.InvariantCulture), 3); // gm_per_w = gm / w_eff
        Assert.Equal(100, double.Parse(first[idxIdPerW], CultureInfo.InvariantCulture), 3);  // id_per_w = id / w_eff
        Assert.True(double.Parse(first[idxVstar], CultureInfo.InvariantCulture) > 0);
        Assert.True(double.Parse(first[idxFt], CultureInfo.InvariantCulture) > 0);
    }

    private static void AssertLine(string line, int vgsIdx, int vdIdx, int idIdx, double expectedVgs, double expectedVd, double expectedId)
    {
        var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expectedVgs, double.Parse(parts[vgsIdx], CultureInfo.InvariantCulture), 6);
        Assert.Equal(expectedVd, double.Parse(parts[vdIdx], CultureInfo.InvariantCulture), 6);
        Assert.Equal(expectedId, double.Parse(parts[idIdx], CultureInfo.InvariantCulture), 6);
    }
}
