using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Cascode.Workspace;

namespace Cascode.Cli.Services;

/// <summary>
/// Result from executing an external command.
/// </summary>
internal readonly record struct CommandRunResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Runtime abstraction used by <see cref="NgspiceInstaller"/> for command execution and IO.
/// </summary>
internal interface INgspiceInstallerRuntime
{
    string CascodeHome { get; }
    string BaseDirectory { get; }
    int ProcessorCount { get; }
    bool IsWindows { get; }
    bool IsLinux { get; }
    string? CurrentRid();
    string? FindTool(string toolName);
    CommandRunResult RunCommand(
        string fileName,
        IReadOnlyList<string> args,
        string? workingDirectory
    );
    void DownloadFile(string url, string destination);
    string ComputeSha256(string filePath);
    void ExtractTarGz(string archivePath, string destination);
    void ExtractZip(string archivePath, string destination);
    void EnsureExecutable(string path);
}

/// <summary>
/// Default runtime implementation used in production.
/// </summary>
internal sealed class DefaultNgspiceInstallerRuntime : INgspiceInstallerRuntime
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public string CascodeHome => WorkspacePaths.GetCascodeHome();

    public string BaseDirectory => AppContext.BaseDirectory;

    public int ProcessorCount => Environment.ProcessorCount;

    public bool IsWindows => OperatingSystem.IsWindows();

    public bool IsLinux => OperatingSystem.IsLinux();

    public string? CurrentRid() => RuntimeIdentifier.CurrentRid();

    public string? FindTool(string toolName) => ToolLocator.FindOnPath(toolName);

    public CommandRunResult RunCommand(
        string fileName,
        IReadOnlyList<string> args,
        string? workingDirectory
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);
        return new CommandRunResult(
            process.ExitCode,
            stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult()
        );
    }

    public void DownloadFile(string url, string destination)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "cascode-cli");
        using var response = Http.Send(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = response.Content.ReadAsStream();
        using var file = File.Create(destination);
        stream.CopyTo(file);
    }

    public string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void ExtractTarGz(string archivePath, string destination)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzipStream, destination, overwriteFiles: true);
    }

    public void ExtractZip(string archivePath, string destination)
    {
        ZipFile.ExtractToDirectory(archivePath, destination);
    }

    public void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute
        );
    }
}
