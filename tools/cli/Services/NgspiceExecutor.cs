using System.Diagnostics;
using System.IO;

namespace Cascode.Cli.Services;

internal static class NgspiceExecutor
{
    public sealed record NgspiceRun(int ExitCode, string Stdout, string Stderr);

    public static NgspiceRun Run(string spiceFile)
    {
        var ngspice = NgspiceLocator.Resolve();

        spiceFile = Path.GetFullPath(spiceFile);
        var workingDir = Path.GetDirectoryName(spiceFile) ?? Directory.GetCurrentDirectory();

        var startInfo = new ProcessStartInfo
        {
            FileName = ngspice.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir,
        };
        startInfo.ArgumentList.Add("-b");
        startInfo.ArgumentList.Add(Path.GetFileName(spiceFile));

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new NgspiceRun(process.ExitCode, stdout, stderr);
    }
}
