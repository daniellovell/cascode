using System;
using System.IO;
using Cascode.Cli.Services;
using Cascode.TestSupport;

namespace Cascode.Cli.Tests;

public sealed class NgspiceCapabilityProbeTests
{
    [UnixOnlyFact]
    public void ProbePssSupport_ReturnsTrue_WhenBinarySupportsPss()
    {
        using var tempDir = new TemporaryDirectory();
        var ngspicePath = Path.Combine(tempDir.Path, "ngspice");
        File.WriteAllText(
            ngspicePath,
            """
            #!/bin/sh
            if [ "$1" = "-b" ]; then
              echo "Periodic Steady State Analysis Started"
              echo "pss simulation(s) aborted"
              exit 0
            fi
            echo "** ngspice-45.2 : Circuit level simulation program"
            """
        );
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                ngspicePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }

        var result = NgspiceCapabilityProbe.ProbePssSupport(ngspicePath);

        Assert.True(result.SupportsPss);
        Assert.Contains("Periodic Steady State Analysis Started", result.ProbeOutput);
    }

    [UnixOnlyFact]
    public void ProbePssSupport_ReturnsFalse_WhenBinaryLacksPss()
    {
        using var tempDir = new TemporaryDirectory();
        var ngspicePath = Path.Combine(tempDir.Path, "ngspice");
        File.WriteAllText(
            ngspicePath,
            """
            #!/bin/sh
            if [ "$1" = "-b" ]; then
              echo "pss: no such command available in ngspice"
              echo "Sorry, no help for pss."
              exit 0
            fi
            echo "** ngspice-45.2 : Circuit level simulation program"
            """
        );
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                ngspicePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }

        var result = NgspiceCapabilityProbe.ProbePssSupport(ngspicePath);

        Assert.False(result.SupportsPss);
        Assert.Contains(
            "no such command available",
            result.ProbeOutput,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
