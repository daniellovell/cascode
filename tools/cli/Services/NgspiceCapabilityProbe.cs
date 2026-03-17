using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Cascode.Cli.Services;

internal static class NgspiceCapabilityProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private const string UnsupportedCommandMarker = "no such command available in ngspice";
    private const string UnsupportedHelpMarker = "Sorry, no help for pss.";

    internal sealed record ProbeResult(bool SupportsPss, string ProbeOutput);

    internal static ProbeResult ProbePssSupport(string ngspicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ngspicePath);

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"cascode-ngspice-pss-probe-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(tempRoot);
        try
        {
            var deckPath = Path.Combine(tempRoot, "probe.cir");
            File.WriteAllText(
                deckPath,
                """
                * ngspice pss capability probe
                V1 in 0 0
                R1 in 0 1k
                .control
                help pss
                pss 1MEG 1u in 64 3 20 1e-3
                quit
                .endc
                .end
                """
            );

            var run = RunProcessWithTimeout(
                ngspicePath,
                tempRoot,
                "-b",
                Path.GetFileName(deckPath)
            );
            var output =
                $"ExitCode: {run.ExitCode}\nStdout:\n{run.Stdout}\nStderr:\n{run.Stderr}".Trim();
            var supportsPss =
                run.ExitCode == 0
                && output.IndexOf(UnsupportedCommandMarker, StringComparison.OrdinalIgnoreCase) < 0
                && output.IndexOf(UnsupportedHelpMarker, StringComparison.OrdinalIgnoreCase) < 0;

            return new ProbeResult(supportsPss, output);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch { }
        }
    }

    private static CommandRunResult RunProcessWithTimeout(
        string executablePath,
        string workingDirectory,
        params string[] arguments
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch { }

            throw new TimeoutException(
                $"Timed out probing ngspice PSS support after {ProbeTimeout.TotalSeconds:0} seconds."
            );
        }

        Task.WaitAll(stdoutTask, stderrTask);
        return new CommandRunResult(
            process.ExitCode,
            stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult()
        );
    }
}
