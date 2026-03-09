using System;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "This test requires a Unix-like shell/executable behavior.";
        }
    }
}

public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This test requires Windows PATH extension behavior.";
        }
    }
}
