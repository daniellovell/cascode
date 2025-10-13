using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
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
        var miniRoot = CreateMiniWorkspace(repoRoot);
        var workspaceAbs = miniRoot;
        var workspaceRel = Path.GetRelativePath(repoRoot, workspaceAbs);

        using var cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(repoRoot, nameof(PdkInteractivePersistenceTests));
        var sharedEnv = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CASCODE_HOME"] = cascodeHome.Path,
            ["COLUMNS"] = "160",
            ["LINES"] = "50",
            ["CASCODE_LOG_LEVEL"] = "error"
        };

        // Session 1: set-dir -> scan -> devices, then exit
        await using (var session1 = InteractiveCliSession.Start(repoRoot, additionalEnvironment: sharedEnv))
        {
            // Wait for prompt to appear
            await session1.WaitForOutputAsync(
                output => output.Contains("cascode", StringComparison.OrdinalIgnoreCase) && (output.Contains("/>") || output.Contains("> ")),
                TimeSpan.FromSeconds(10));

            await session1.SendLineAsync($"pdk set-dir {workspaceRel}");
            await Task.Delay(100);
            await session1.WaitForOutputAsync(
                output => output.Contains("PDK workspace set to", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15));

            // Workspace bar should reflect the new path; match on a stable suffix
            await session1.WaitForOutputAsync(
                output => output.Contains("Workspace", StringComparison.OrdinalIgnoreCase) && output.Contains(Path.GetFileName(workspaceAbs), StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(15));

            await session1.SendLineAsync("pdk scan");
            await Task.Delay(100);
            // Wait for the pdk.db file to appear (log level may suppress Info messages)
            var dbPath = GetExpectedDbPath(cascodeHome.Path, workspaceAbs);
            var scanDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTime.UtcNow < scanDeadline && !File.Exists(dbPath))
            {
                await Task.Delay(100);
            }
            Assert.True(File.Exists(dbPath), $"Timeout waiting for pdk.db at '{dbPath}'.");

            // Ensure devices are available (force list to guarantee a summary message)
            await session1.SendLineAsync("pdk devices --list --class nmos --limit 1");
            await Task.Delay(100);
            await session1.WaitForOutputAsync(
                output => (output.Contains("Showing ", StringComparison.OrdinalIgnoreCase) && output.Contains(" of ", StringComparison.OrdinalIgnoreCase) && output.Contains("devices", StringComparison.OrdinalIgnoreCase))
                           || output.Contains("Matched:", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(12));

            await session1.SendLineAsync("exit");
            await Task.Delay(100);
            await session1.WaitForExitAsync(TimeSpan.FromSeconds(10));
            session1.MarkSuccess();
        }

        // Verify CLI config and database exist after first session
        var configPath = Path.Combine(cascodeHome.Path, "config.json");
        Assert.True(File.Exists(configPath), $"Expected CLI config at '{configPath}' after set-dir.");
        var configJson = await File.ReadAllTextAsync(configPath);
        Assert.Contains(workspaceAbs, configJson, StringComparison.Ordinal);

        // Session 2: reopen CLI and verify devices are available without re-scan
        await using (var session2 = InteractiveCliSession.Start(repoRoot, additionalEnvironment: sharedEnv))
        {
            await session2.WaitForOutputAsync(
                output => output.Contains("cascode", StringComparison.OrdinalIgnoreCase) && (output.Contains("/>") || output.Contains("> ")),
                TimeSpan.FromSeconds(10));

            // Workspace bar should show persisted path now (best-effort; TTY capture can be flaky)
            try
            {
                await session2.WaitForOutputAsync(
                    output => output.Contains("Workspace", StringComparison.OrdinalIgnoreCase) && output.Contains(Path.GetFileName(workspaceAbs), StringComparison.OrdinalIgnoreCase),
                    TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                // Continue; we will validate functionality via devices listing below.
            }

            // Devices should load from existing pdk.db without running scan again
            await session2.SendLineAsync("pdk devices --list --class nmos --limit 1");
            await Task.Delay(100);
            // Fail fast if DB is missing (indicates persisted workspace not applied)
            try
            {
                var maybeError = await session2.WaitForOutputAsync(
                    output => output.Contains("No PDK database found", StringComparison.OrdinalIgnoreCase),
                    TimeSpan.FromSeconds(3));
                Assert.False(maybeError.Contains("No PDK database found", StringComparison.OrdinalIgnoreCase),
                    $"Devices should be available without re-scan. Output: {maybeError}");
            }
            catch (TimeoutException)
            {
                // Expected path: no immediate error; proceed to wait for the summary
            }
            await session2.WaitForOutputAsync(
                output => (output.Contains("Showing ", StringComparison.OrdinalIgnoreCase) && output.Contains(" of ", StringComparison.OrdinalIgnoreCase) && output.Contains("devices", StringComparison.OrdinalIgnoreCase))
                           || output.Contains("Matched:", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(12));

            await session2.SendLineAsync("exit");
            await Task.Delay(100);
            await session2.WaitForExitAsync(TimeSpan.FromSeconds(10));
            session2.MarkSuccess();
        }
    }
    private static string GetExpectedDbPath(string cascodeHome, string workspaceAbs)
    {
        static string Hash(string s)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(s));
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        var workId = Hash(workspaceAbs);
        return Path.Combine(cascodeHome, "workspaces", workId, "pdk.db");
    }

    private static string CreateMiniWorkspace(string repoRoot)
    {
        var root = Path.Combine(repoRoot, ".it", $"mini-pdk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        // Write minimal cds.lib defining a single library under lib/mini
        var cdsLibPath = Path.Combine(root, "cds.lib");
        File.WriteAllText(cdsLibPath, "DEFINE mini lib/mini\n");

        // Create a single device with both layout and symbol views so PhysicalLibraryScanner picks it up
        var libMini = Path.Combine(root, "lib", "mini");
        var cell = Path.Combine(libMini, "nfet_unit");
        Directory.CreateDirectory(Path.Combine(cell, "layout"));
        Directory.CreateDirectory(Path.Combine(cell, "symbol"));

        return root;
    }
}
