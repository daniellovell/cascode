using System;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class PdkDevicesCommandTests
{
    [Fact]
    public async Task PdkDevicesCommand_WithValidWorkspace_PrintsDeviceSummary()
    {
        var repoRoot = Infrastructure.CliIntegrationTestHelper.GetRepositoryRoot();
        using var cascodeHome = Infrastructure.CliIntegrationTestHelper.CreateCascodeHome(repoRoot, nameof(PdkDevicesCommandTests));
        var scanResult = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "scan",
            "tests/fixtures/pdk/sky130");
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(scanResult);

        var devicesResult = await Infrastructure.CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            cascodeHome,
            "pdk",
            "devices",
            "--workspace",
            "tests/fixtures/pdk/sky130",
            "--class",
            "nmos");
        Infrastructure.CliIntegrationTestHelper.AssertSuccess(devicesResult);
        Assert.True(
            devicesResult.Stdout.Contains("nfet_01v8", StringComparison.Ordinal),
            $"Expected device summary to include 'nfet_01v8'. Stdout: {devicesResult.Stdout}{Environment.NewLine}Stderr: {devicesResult.Stderr}");
    }
}
