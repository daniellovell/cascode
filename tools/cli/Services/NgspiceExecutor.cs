using System.Diagnostics;
using System.IO;

namespace Cascode.Cli.Services;

internal static class NgspiceExecutor
{
    public sealed record NgspiceRun(int ExitCode, string Stdout, string Stderr);

    public static NgspiceRun Run(string spiceFile)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ngspice",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(spiceFile) ?? Directory.GetCurrentDirectory()
        };
        startInfo.ArgumentList.Add("-b");
        startInfo.ArgumentList.Add(spiceFile);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new NgspiceRun(process.ExitCode, stdout, stderr);
    }
}

