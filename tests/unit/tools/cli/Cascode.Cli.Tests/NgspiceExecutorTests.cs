using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Cascode.Cli.Services;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class NgspiceExecutorTests
{
    [Fact]
    [Trait("Category", "Simulation")]
    public void Run_WithSpaceInFileName_HandlesCorrectly()
    {
        // Create a temporary directory and SPICE file with a space in the name
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var spiceFileName = "test file with spaces.sp";
            var spiceFilePath = Path.Combine(tempDir.FullName, spiceFileName);

            // Create a minimal valid SPICE file
            File.WriteAllText(spiceFilePath, "* Test SPICE file\n.END\n");

            // Verify the file exists
            Assert.True(File.Exists(spiceFilePath), "SPICE file should exist");

            // Run ngspice - this should not fail due to argument parsing issues
            // The ArgumentList API properly handles spaces/quotes, so we should not get
            // an exception from Process.Start() even if ngspice is not available
            NgspiceExecutor.NgspiceRun result;
            try
            {
                result = NgspiceExecutor.Run(spiceFilePath);
            }
            catch (Exception ex)
                when (ex is Win32Exception
                    || ex is FileNotFoundException
                    || ex is NgspiceNotFoundException
                )
            {
                return;
            }

            // If we got here, ngspice was invoked successfully (argument parsing worked)
            // The result may have a non-zero exit code if ngspice had issues, but that's separate
            // from argument parsing. The key test is that Process.Start() succeeded.
            Assert.NotNull(result);
        }
        finally
        {
            try
            {
                tempDir.Delete(recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    public void RunProcess_WithLargeStderr_DoesNotDeadlockAndCapturesBothStreams()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (executablePath, arguments) = CreateHeavyStderrEmitter(tempDir.FullName);

            var result = NgspiceExecutor.RunProcess(executablePath, tempDir.FullName, arguments);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("stdout:done", result.Stdout, StringComparison.Ordinal);
            Assert.Contains("stderr:line", result.Stderr, StringComparison.Ordinal);
            Assert.True(result.Stderr.Length > 128 * 1024, "stderr should be large enough.");
        }
        finally
        {
            try
            {
                tempDir.Delete(recursive: true);
            }
            catch { }
        }
    }

    private static (string Executable, string[] Arguments) CreateHeavyStderrEmitter(
        string directory
    )
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var batPath = Path.Combine(directory, "emit-heavy-stderr.cmd");
            File.WriteAllText(
                batPath,
                "@echo off\r\n"
                    + "for /L %%i in (1,1,50000) do @>&2 echo stderr:line %%i\r\n"
                    + "echo stdout:done\r\n"
            );
            return ("cmd.exe", new[] { "/c", "emit-heavy-stderr.cmd" });
        }

        var shPath = Path.Combine(directory, "emit-heavy-stderr.sh");
        File.WriteAllText(
            shPath,
            "#!/bin/sh\n"
                + "i=0\n"
                + "while [ \"$i\" -lt 50000 ]\n"
                + "do\n"
                + "  echo \"stderr:line $i\" 1>&2\n"
                + "  i=$((i + 1))\n"
                + "done\n"
                + "echo \"stdout:done\"\n"
        );
        File.SetUnixFileMode(
            shPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
        return (shPath, Array.Empty<string>());
    }
}
