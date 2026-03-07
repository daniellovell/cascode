using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

[Collection(InteractiveCliCollection.Name)]
public sealed class PdkInteractivePersistenceTests
{
    private readonly InteractiveCliFixture _fixture;

    public PdkInteractivePersistenceTests(InteractiveCliFixture fixture)
    {
        _fixture = fixture;
    }

    [InteractiveCliFact]
    public async Task PdkDbAndWorkspace_PersistAcrossInteractiveSessions()
    {
        var repoRoot = _fixture.RepoRoot;
        // Use the sky130 fixture which has a complete PDK structure with model decks
        var workspaceAbs = Path.GetFullPath(
            Path.Combine(repoRoot, "tests", "fixtures", "pdk", "sky130")
        );
        var workspaceRel = Path.GetRelativePath(repoRoot, workspaceAbs);

        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            nameof(PdkInteractivePersistenceTests)
        );
        var sharedEnv = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CASCODE_HOME"] = cascodeHome.Path,
            ["COLUMNS"] = "160",
            ["LINES"] = "50",
        };

        // Session 1: set-dir -> scan -> devices, then exit
        await using (
            var session1 = InteractiveCliSession.Start(repoRoot, additionalEnvironment: sharedEnv)
        )
        {
            // Wait for prompt to appear
            await session1.WaitForOutputAsync(
                output =>
                    output.Contains("cascode", StringComparison.OrdinalIgnoreCase)
                    && (output.Contains("/>") || output.Contains("> ")),
                TimeSpan.FromSeconds(10)
            );

            await session1.SendLineAsync($"pdk set-dir {workspaceRel}");
            await session1.WaitForOutputAsync(
                output =>
                    output.Contains("PDK workspace set to", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15)
            );

            // Workspace bar should reflect the new path; match on a stable suffix
            await session1.WaitForOutputAsync(
                output =>
                    output.Contains("Workspace", StringComparison.OrdinalIgnoreCase)
                    && output.Contains(
                        Path.GetFileName(workspaceAbs),
                        StringComparison.OrdinalIgnoreCase
                    ),
                TimeSpan.FromSeconds(15)
            );

            await session1.SendLineAsync("pdk scan");
            // Wait for the pdk.db file to appear (log level may suppress Info messages)
            var dbPath = GetExpectedDbPath(cascodeHome.Path, workspaceAbs);
            await AsyncTest.EventuallyAsync(
                () => File.Exists(dbPath),
                TimeSpan.FromSeconds(90),
                TimeSpan.FromMilliseconds(100),
                $"Timeout waiting for pdk.db at '{dbPath}'."
            );

            // Wait for scan to complete - look for the "PDK database updated" message
            await session1.WaitForOutputAsync(
                output =>
                    output.Contains("PDK database updated", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(90)
            );

            // Ensure devices are available (force list to guarantee a summary message)
            await session1.SendLineAsync("pdk devices --list --limit 1");
            var session1Output = await session1.WaitForOutputAsync(
                output =>
                    output.Contains("Devices:", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("No devices matched", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("No devices discovered", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("Error", StringComparison.OrdinalIgnoreCase)
                    || (
                        output.Contains("Showing ", StringComparison.OrdinalIgnoreCase)
                        && output.Contains(" of ", StringComparison.OrdinalIgnoreCase)
                        && output.Contains("devices", StringComparison.OrdinalIgnoreCase)
                    ),
                TimeSpan.FromSeconds(20)
            );
            Assert.True(
                session1Output.Contains("Devices:", StringComparison.OrdinalIgnoreCase)
                    || session1Output.Contains("Showing ", StringComparison.OrdinalIgnoreCase),
                $"Expected devices to be discoverable in first session. Output: {session1Output}"
            );

            await session1.SendLineAsync("exit");
            await session1.WaitForExitAsync(TimeSpan.FromSeconds(10));
            session1.MarkSuccess();
        }

        // Verify CLI config and database exist after first session
        var configPath = Path.Combine(cascodeHome.Path, "config.json");
        Assert.True(
            File.Exists(configPath),
            $"Expected CLI config at '{configPath}' after set-dir."
        );
        var configJson = await File.ReadAllTextAsync(configPath);
        Assert.Contains(workspaceAbs, configJson, StringComparison.Ordinal);

        // Session 2: reopen CLI and verify devices are available without re-scan
        await using (
            var session2 = InteractiveCliSession.Start(repoRoot, additionalEnvironment: sharedEnv)
        )
        {
            await session2.WaitForOutputAsync(
                output =>
                    output.Contains("cascode", StringComparison.OrdinalIgnoreCase)
                    && (output.Contains("/>") || output.Contains("> ")),
                TimeSpan.FromSeconds(10)
            );

            // Workspace bar should show persisted path now (best-effort; TTY capture can be flaky)
            try
            {
                await session2.WaitForOutputAsync(
                    output =>
                        output.Contains("Workspace", StringComparison.OrdinalIgnoreCase)
                        && output.Contains(
                            Path.GetFileName(workspaceAbs),
                            StringComparison.OrdinalIgnoreCase
                        ),
                    TimeSpan.FromSeconds(10)
                );
            }
            catch (TimeoutException)
            {
                // Continue; we will validate functionality via devices listing below.
            }

            // Devices should load from existing pdk.db without running scan again
            await session2.SendLineAsync("pdk devices --list --limit 1");
            // Fail fast if DB is missing (indicates persisted workspace not applied)
            try
            {
                var maybeError = await session2.WaitForOutputAsync(
                    output =>
                        output.Contains(
                            "No PDK database found",
                            StringComparison.OrdinalIgnoreCase
                        ),
                    TimeSpan.FromSeconds(3)
                );
                Assert.False(
                    maybeError.Contains(
                        "No PDK database found",
                        StringComparison.OrdinalIgnoreCase
                    ),
                    $"Devices should be available without re-scan. Output: {maybeError}"
                );
            }
            catch (TimeoutException)
            {
                // Expected path: no immediate error; proceed to wait for the summary
            }
            var devicesOutput = await session2.WaitForOutputAsync(
                output =>
                    output.Contains("Devices:", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("No devices matched", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("No devices discovered", StringComparison.OrdinalIgnoreCase)
                    || (
                        output.Contains("Showing ", StringComparison.OrdinalIgnoreCase)
                        && output.Contains(" of ", StringComparison.OrdinalIgnoreCase)
                        && output.Contains("devices", StringComparison.OrdinalIgnoreCase)
                    ),
                TimeSpan.FromSeconds(20)
            );
            Assert.True(
                devicesOutput.Contains("Devices:", StringComparison.OrdinalIgnoreCase)
                    || devicesOutput.Contains("Showing ", StringComparison.OrdinalIgnoreCase),
                $"Expected persisted devices to be available without re-scan. Output: {devicesOutput}"
            );

            await session2.SendLineAsync("exit");
            await session2.WaitForExitAsync(TimeSpan.FromSeconds(10));
            session2.MarkSuccess();
        }
    }

    private static string GetExpectedDbPath(string cascodeHome, string workspaceAbs)
    {
        static string Hash(string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(s));
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        var workId = Hash(workspaceAbs);
        return Path.Combine(cascodeHome, "workspaces", workId, "pdk.db");
    }
}
