using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Cascode.Cli.Services;

internal static class NgspiceExecutor
{
    public sealed record NgspiceRun(int ExitCode, string Stdout, string Stderr);

    public static NgspiceRun Run(string spiceFile)
    {
        var ngspice = NgspiceLocator.Resolve();

        spiceFile = Path.GetFullPath(spiceFile);
        var workingDir = Path.GetDirectoryName(spiceFile) ?? Directory.GetCurrentDirectory();
        return RunProcess(ngspice.Path, workingDir, new[] { "-b", Path.GetFileName(spiceFile) });
    }

    internal static NgspiceRun RunProcess(
        string executablePath,
        string workingDir,
        IReadOnlyList<string> arguments
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Read both redirected streams concurrently to avoid deadlock when one pipe fills.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);

        return new NgspiceRun(
            process.ExitCode,
            stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult()
        );
    }
}
