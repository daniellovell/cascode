using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cascode.Cli.IntegrationTests.Infrastructure;

internal sealed class InteractiveCliSession : IAsyncDisposable
{
    private const int DefaultColumns = 120;
    private const int DefaultRows = 40;

    private readonly Process _process;
    private readonly Stream _stdout;
    private readonly Stream _stderr;
    private readonly Stream _stdin;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly AsyncAutoResetEvent _outputReady = new();
    private readonly object _sync = new();
    private readonly StringBuilder _buffer = new();
    private readonly Task _stdoutPumpTask;
    private readonly Task _stderrPumpTask;
    private readonly string _transcriptPath;
    private readonly StreamWriter _transcriptWriter;
    private bool _shouldDumpTranscript = true;
    private bool _disposed;

    private InteractiveCliSession(
        Process process,
        Stream stdout,
        Stream stderr,
        Stream stdin,
        string transcriptPath,
        StreamWriter transcriptWriter
    )
    {
        _process = process;
        _stdout = stdout;
        _stderr = stderr;
        _stdin = stdin;
        _transcriptPath = transcriptPath;
        _transcriptWriter = transcriptWriter;
        _stdoutPumpTask = Task.Run(() => PumpStreamAsync(_stdout, _shutdownCts.Token));
        _stderrPumpTask = Task.Run(() => PumpStreamAsync(_stderr, _shutdownCts.Token));
    }

    public void MarkSuccess() => _shouldDumpTranscript = false;

    public static InteractiveCliSession Start(
        string repoRoot,
        IReadOnlyList<string>? args = null,
        IReadOnlyDictionary<string, string>? additionalEnvironment = null
    )
    {
        EnsureLinux();

        var arguments = args is null || args.Count == 0 ? Array.Empty<string>() : args;
        var spec = CliIntegrationTestHelper.BuildCliCommand(repoRoot, arguments);
        var env = PrepareEnvironment(repoRoot, additionalEnvironment);

        var commandText = BuildShellCommand(spec.FileName, spec.Arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = "script",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot,
        };
        startInfo.ArgumentList.Add("-qf");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(commandText);
        startInfo.ArgumentList.Add("/dev/stdout");

        foreach (var kv in env)
        {
            startInfo.Environment[kv.Key] = kv.Value;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start 'script'.");
        }

        var transcriptPath = Path.Combine(
            Path.GetTempPath(),
            $"cascode-interactive-{Guid.NewGuid():N}.log"
        );
        var transcriptWriter = new StreamWriter(
            new FileStream(transcriptPath, FileMode.Create, FileAccess.Write, FileShare.Read)
        )
        {
            AutoFlush = true,
            NewLine = "\n",
        };

        return new InteractiveCliSession(
            process,
            process.StandardOutput.BaseStream,
            process.StandardError.BaseStream,
            process.StandardInput.BaseStream,
            transcriptPath,
            transcriptWriter
        );
    }

    public string CapturedOutput
    {
        get
        {
            lock (_sync)
            {
                return _buffer.ToString();
            }
        }
    }

    public async Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        await WriteAsync(line + "\n", cancellationToken).ConfigureAwait(false);
    }

    public Task SendControlCAsync(CancellationToken cancellationToken = default) =>
        WriteBytesAsync(new byte[] { 0x03 }, cancellationToken);

    public async Task<string> WaitForOutputAsync(
        Func<string, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(predicate);
        using var linked = CreateLinkedTokenSource(timeout, cancellationToken);

        while (true)
        {
            linked.Token.ThrowIfCancellationRequested();
            var snapshot = CapturedOutput;
            if (predicate(snapshot))
                return snapshot;
            try
            {
                await _outputReady.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Condition not met within {timeout.TotalSeconds:F1}s.");
            }
        }
    }

    public Task<int> WaitForExitAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_process.HasExited)
            return Task.FromResult(_process.ExitCode);
        return WaitForExitCoreAsync(timeout, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        _shutdownCts.Cancel();
        try
        {
            await _stdoutPumpTask.ConfigureAwait(false);
        }
        catch { }
        try
        {
            await _stderrPumpTask.ConfigureAwait(false);
        }
        catch { }

        await DisposeStreamAsync(_stdout).ConfigureAwait(false);
        await DisposeStreamAsync(_stderr).ConfigureAwait(false);
        await DisposeStreamAsync(_stdin).ConfigureAwait(false);
        await DisposeWriterAsync(_transcriptWriter).ConfigureAwait(false);

        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch { }
            try
            {
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch { }
        }

        if (_shouldDumpTranscript)
            DumpTranscript();
        else
            TryDeleteTranscript();
        _process.Dispose();
        _shutdownCts.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task PumpStreamAsync(Stream source, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                int read;
                try
                {
                    read = await source
                        .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }
                if (read == 0)
                    break;

                var chunk = Encoding.UTF8.GetString(buffer, 0, read);
                lock (_sync)
                {
                    _buffer.Append(chunk);
                }
                try
                {
                    await _transcriptWriter.WriteAsync(chunk).ConfigureAwait(false);
                }
                catch { }
                _outputReady.Set();
            }
        }
        finally
        {
            _outputReady.Set();
        }
    }

    private Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        return WriteBytesAsync(payload, cancellationToken);
    }

    private async Task WriteBytesAsync(byte[] payload, CancellationToken cancellationToken)
    {
        await _stdin.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private void DumpTranscript()
    {
        try
        {
            var exitCode = _process.HasExited ? _process.ExitCode : (int?)null;
            var sb = new StringBuilder();
            sb.AppendLine("===== cascode interactive transcript =====");
            sb.AppendLine(
                exitCode.HasValue ? $"Exit code: {exitCode}" : "Exit code: (process still running)"
            );
            sb.AppendLine($"Transcript file: {_transcriptPath}");
            sb.AppendLine("--- output ---");
            var t = CapturedOutput;
            sb.Append(t);
            if (!t.EndsWith('\n'))
                sb.AppendLine();
            sb.AppendLine("===== end transcript =====");
            Console.Error.Write(sb.ToString());
        }
        catch { }
    }

    private void TryDeleteTranscript()
    {
        try
        {
            if (File.Exists(_transcriptPath))
                File.Delete(_transcriptPath);
        }
        catch { }
    }

    private static string BuildShellCommand(string fileName, IReadOnlyList<string> args)
    {
        static string Q(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "''";
            return "'" + s.Replace("'", "'\\''") + "'";
        }
        var parts = new List<string> { Q(fileName) };
        foreach (var a in args)
            parts.Add(Q(a));
        return string.Join(' ', parts);
    }

    private static CancellationTokenSource CreateLinkedTokenSource(
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        if (timeout == Timeout.InfiniteTimeSpan)
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        return linked;
    }

    private async Task<int> WaitForExitCoreAsync(
        TimeSpan? timeout,
        CancellationToken cancellationToken
    )
    {
        if (timeout.HasValue)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout.Value);
            await _process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        else
        {
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        return _process.ExitCode;
    }

    private static Dictionary<string, string> PrepareEnvironment(
        string repoRoot,
        IReadOnlyDictionary<string, string>? additionalEnvironment
    )
    {
        var env = new Dictionary<string, string>(
            CliIntegrationTestHelper.BuildDeterministicEnvironment(repoRoot),
            StringComparer.Ordinal
        )
        {
            ["TERM"] = "xterm-256color",
            ["COLUMNS"] = DefaultColumns.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            ["LINES"] = DefaultRows.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["LC_ALL"] = "C",
            ["CASCODE_TEST_MODE"] = "interactive",
            ["PWD"] = repoRoot,
        };
        if (additionalEnvironment is not null)
            foreach (var kv in additionalEnvironment)
                env[kv.Key] = kv.Value;
        return env;
    }

    private static void EnsureLinux()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException(
                "Interactive tests require util-linux 'script'."
            );
    }

    private static async Task DisposeStreamAsync(Stream s)
    {
        if (s is IAsyncDisposable ad)
        {
            await ad.DisposeAsync().ConfigureAwait(false);
        }
        else
            s.Dispose();
    }

    private static async Task DisposeWriterAsync(TextWriter w)
    {
        if (w is IAsyncDisposable ad)
        {
            await ad.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            await w.FlushAsync().ConfigureAwait(false);
            w.Dispose();
        }
    }
}
