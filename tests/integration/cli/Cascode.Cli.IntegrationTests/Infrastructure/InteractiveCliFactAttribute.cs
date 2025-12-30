using System;
using Xunit;

namespace Cascode.Cli.IntegrationTests.Infrastructure;

/// <summary>
/// Fact attribute for interactive TUI tests that require Linux and a real pseudo‑TTY.
/// Skips automatically on CI where the pseudo‑TTY layer is flaky.
/// </summary>
public sealed class InteractiveCliFactAttribute : FactAttribute
{
    public InteractiveCliFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = "This interactive test requires Linux (util-linux 'script').";
            return;
        }

        if (CliIntegrationTestHelper.IsRunningInCi())
        {
            Skip =
                "Interactive CLI test skipped in CI due to pseudo‑TTY flakiness; covered by non-interactive integration tests.";
        }
    }
}
