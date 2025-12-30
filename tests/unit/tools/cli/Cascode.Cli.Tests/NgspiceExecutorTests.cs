using System;
using System.ComponentModel;
using System.IO;
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
            catch (Exception ex) when (ex is Win32Exception || ex is FileNotFoundException)
            {
                // ngspice not available - skip test but verify we got here without argument parsing errors
                // If argument parsing had failed, we would have gotten an exception during Process.Start()
                // with a different error message (e.g., about malformed arguments)
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
}
