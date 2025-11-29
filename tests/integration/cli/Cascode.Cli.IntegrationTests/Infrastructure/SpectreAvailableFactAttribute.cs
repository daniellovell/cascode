using System;
using System.IO;
using Xunit;

namespace Cascode.Cli.IntegrationTests.Infrastructure;

/// <summary>
/// Fact attribute that skips the test when Spectre simulator is not available.
/// Checks for SPECTRE_BIN or SPECTRE_HOME environment variables.
/// </summary>
public sealed class SpectreAvailableFactAttribute : FactAttribute
{
    public SpectreAvailableFactAttribute()
    {
        if (!IsSpectreAvailable())
        {
            Skip = "Spectre simulator not available (set SPECTRE_BIN or SPECTRE_HOME)";
        }
    }

    private static bool IsSpectreAvailable()
    {
        var bin = Environment.GetEnvironmentVariable("SPECTRE_BIN");
        if (!string.IsNullOrWhiteSpace(bin) && File.Exists(bin))
        {
            return true;
        }

        var home = Environment.GetEnvironmentVariable("SPECTRE_HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            var spectreExe = Path.Combine(home, "bin", "spectre");
            if (File.Exists(spectreExe))
            {
                return true;
            }
        }

        return false;
    }
}
