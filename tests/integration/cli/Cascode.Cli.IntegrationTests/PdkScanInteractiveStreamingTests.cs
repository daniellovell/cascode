using System;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

[Collection(InteractiveCliCollection.Name)]
public sealed class PdkScanInteractiveStreamingTests
{
    private readonly InteractiveCliFixture _fixture;

    public PdkScanInteractiveStreamingTests(InteractiveCliFixture fixture)
    {
        _fixture = fixture;
    }

    [Infrastructure.LinuxOnlyFact]
    public async Task PdkScan_InteractiveMode_StreamsProgressDuringScan()
    {
        await using var session = InteractiveCliSession.Start(_fixture.RepoRoot);

        await session.WaitForOutputAsync(
            output => output.Contains("cascode", StringComparison.OrdinalIgnoreCase) && (output.Contains("/>") || output.Contains("> ")),
            TimeSpan.FromSeconds(10));

        await session.SendLineAsync("pdk scan tests/fixtures/pdk/sky130");

        // Verify that progress messages stream during the scan
        // Use longer timeout in CI (30s) to account for slower/variable performance, shorter in dev (5s) for faster feedback
        var progressTimeout = Infrastructure.CliIntegrationTestHelper.IsRunningInCi()
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(5);

        await session.WaitForOutputAsync(
            output => output.Contains("Scanning workspace", StringComparison.OrdinalIgnoreCase) ||
                      output.Contains("Workspace root resolved", StringComparison.OrdinalIgnoreCase) ||
                      output.Contains("Inspecting cdsinit", StringComparison.OrdinalIgnoreCase) ||
                      output.Contains("Inspecting libInit", StringComparison.OrdinalIgnoreCase),
            progressTimeout);

        await session.SendControlCAsync();
        await session.WaitForOutputAsync(
            output => output.Contains("cascode", StringComparison.OrdinalIgnoreCase) && (output.Contains("/>") || output.Contains("> ")),
            TimeSpan.FromSeconds(10));

        await session.SendLineAsync("exit");
        await session.WaitForExitAsync(TimeSpan.FromSeconds(10));
        session.MarkSuccess();
    }
}
