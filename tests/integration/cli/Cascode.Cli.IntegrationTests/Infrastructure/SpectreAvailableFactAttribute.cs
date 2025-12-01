using Cascode.Cli.Services;
using Xunit;

namespace Cascode.Cli.IntegrationTests.Infrastructure;

/// <summary>
/// Fact attribute that skips the test when Spectre simulator is not available on PATH.
/// </summary>
public sealed class SpectreAvailableFactAttribute : FactAttribute
{
    public SpectreAvailableFactAttribute()
    {
        if (!IsSpectreAvailable())
        {
            Skip = "Spectre simulator not available on PATH";
        }
    }

    private static bool IsSpectreAvailable()
    {
        return !string.IsNullOrWhiteSpace(SpectreLocator.FindOnPath());
    }
}
