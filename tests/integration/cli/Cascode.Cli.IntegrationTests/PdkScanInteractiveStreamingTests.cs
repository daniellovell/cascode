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

    // Note: Skipped on CI due to pseudo‑TTY flakiness; non-interactive tests cover streaming outputs via logs.
    [Infrastructure.InteractiveCliFact]
    public async Task PdkScan_InteractiveMode_StreamsProgressDuringScan()
    {
        await using var session = InteractiveCliSession.Start(_fixture.RepoRoot);

        await session.WaitForOutputAsync(
            output => output.Contains("cascode", StringComparison.OrdinalIgnoreCase) && (output.Contains("/>") || output.Contains("> ")),
            TimeSpan.FromSeconds(10));

        await session.SendLineAsync("pdk scan tests/fixtures/pdk/sky130");

        // Verify that progress messages stream incrementally by polling
        // Count lines appearing over time to prove output isn't buffered until completion
        var lineCountSnapshots = new System.Collections.Generic.List<int>();
        var pollInterval = TimeSpan.FromMilliseconds(200);
        var maxPolls = 50; // 50 * 200ms = 10 seconds max

        for (int i = 0; i < maxPolls; i++)
        {
            await Task.Delay(pollInterval);
            var output = session.CapturedOutput;
            var lineCount = output.Split('\n').Length;
            lineCountSnapshots.Add(lineCount);

            // If we see completion message, we're done polling
            if (output.Contains("PDK database updated", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Scan failed", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        // Verify that line count increased over multiple polls (proving incremental streaming)
        var increasingPolls = 0;
        for (int i = 1; i < lineCountSnapshots.Count; i++)
        {
            if (lineCountSnapshots[i] > lineCountSnapshots[i - 1])
            {
                increasingPolls++;
            }
        }

        Assert.True(increasingPolls >= 2,
            $"Expected line count to increase in at least 2 polls (incremental streaming), but only increased {increasingPolls} times. " +
            $"Line counts: [{string.Join(", ", lineCountSnapshots)}]");

        // Wait for scan to complete with generous timeout for CI
        await session.WaitForOutputAsync(
            output => output.Contains("PDK database updated", StringComparison.OrdinalIgnoreCase) ||
                      output.Contains("Scan failed", StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromMinutes(2));

        // Verify expected progress messages appeared
        var finalOutput = session.CapturedOutput;
        Assert.Contains("Scanning workspace", finalOutput, StringComparison.OrdinalIgnoreCase);

        await session.SendLineAsync("exit");
        await session.WaitForExitAsync(TimeSpan.FromSeconds(10));
        session.MarkSuccess();
    }
}
