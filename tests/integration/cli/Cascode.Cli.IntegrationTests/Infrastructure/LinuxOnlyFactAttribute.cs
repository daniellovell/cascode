using System;
using Xunit;

namespace Cascode.Cli.IntegrationTests.Infrastructure;

/// <summary>
/// Fact attribute that skips the test on non-Linux platforms.
/// Useful for tests that depend on Linux-specific utilities like util-linux 'script'.
/// </summary>
public sealed class LinuxOnlyFactAttribute : FactAttribute
{
    public LinuxOnlyFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = "This test requires Linux (depends on util-linux utilities).";
        }
    }
}
