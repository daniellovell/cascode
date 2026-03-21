using System;
using System.IO;
using System.Threading.Tasks;
using Cascode.Language;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkEmitPrimitivesLayoutTests
{
    [Fact]
    public async Task PdkEmitPrimitives_DefaultOutput_WritesStructuredLibrary()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            nameof(PdkEmitPrimitivesLayoutTests)
        );

        var scan = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "scan",
            "tests/fixtures/pdk/sky130"
        );
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(scan, "PDK scan should succeed");

        var emit = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "emit",
            "primitives",
            "--workspace",
            "tests/fixtures/pdk/sky130"
        );
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(
            emit,
            "PDK emit primitives should succeed"
        );

        var outputDir = Path.Combine(repoRoot, "lib", "pdk", "sky130");
        Assert.True(Directory.Exists(outputDir));

        var devicesPath = Path.Combine(outputDir, "devices.cas");
        var resistorsPath = Path.Combine(outputDir, "resistors.cas");
        var capacitorsPath = Path.Combine(outputDir, "capacitors.cas");
        var diodesPath = Path.Combine(outputDir, "diodes.cas");

        Assert.True(File.Exists(devicesPath));
        Assert.True(File.Exists(resistorsPath));
        Assert.True(File.Exists(capacitorsPath));
        Assert.True(File.Exists(diodesPath));

        Assert.False(File.Exists(Path.Combine(repoRoot, "lib", "pdk", "sky130_Primitives.cas")));

        var devicesText = await File.ReadAllTextAsync(devicesPath);
        var resistorsText = await File.ReadAllTextAsync(resistorsPath);
        var capacitorsText = await File.ReadAllTextAsync(capacitorsPath);
        var diodesText = await File.ReadAllTextAsync(diodesPath);

        Assert.StartsWith(
            $"VERSION {CascodeVersion.Current}",
            devicesText,
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            $"VERSION {CascodeVersion.Current}",
            resistorsText,
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            $"VERSION {CascodeVersion.Current}",
            capacitorsText,
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            $"VERSION {CascodeVersion.Current}",
            diodesText,
            StringComparison.Ordinal
        );
        Assert.Contains("library lib.pdk.sky130.devices", devicesText, StringComparison.Ordinal);
        Assert.Contains(
            "library lib.pdk.sky130.resistors",
            resistorsText,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "library lib.pdk.sky130.capacitors",
            capacitorsText,
            StringComparison.Ordinal
        );
        Assert.Contains("library lib.pdk.sky130.diodes", diodesText, StringComparison.Ordinal);

        // Portable size tuple contract: emitted primitives map multiplicity from primSize.M.
        Assert.DoesNotContain("primSize.NF", devicesText, StringComparison.Ordinal);
        Assert.DoesNotContain("NF=[", devicesText, StringComparison.Ordinal);
        Assert.Contains("M=[", devicesText, StringComparison.Ordinal);
        Assert.Contains(
            "primitive nfet_20v0(size primSize) implements NMOS",
            devicesText,
            StringComparison.Ordinal
        );
        Assert.Contains("m = primSize.M", devicesText, StringComparison.Ordinal);
    }
}
