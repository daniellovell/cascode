using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Cli.Services;
using Cascode.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class CascodeLoadLinkServiceTests
{
    [Fact]
    public void TryLoadAndLinkIfNeeded_CaiWithIncludes_RelinksAndWarns()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("cli-load-link-cai-includes");
        var inputPath = Path.Combine(cascodeHome.Path, "input.el.cai");
        File.WriteAllText(inputPath, BuildIncludeBearingDocument("RelinkFromCai"));
        var logger = new CapturingLogger();

        var ok = CascodeLoadLinkService.TryLoadAndLinkIfNeeded(
            inputPath,
            repoRoot,
            Path.Combine(cascodeHome.Path, "artifacts"),
            logger,
            out var loaded,
            out var diagnostics
        );

        Assert.True(ok, string.Join(Environment.NewLine, diagnostics.Select(d => d.Message)));
        Assert.Equal(Path.GetFullPath(inputPath), loaded.InputPath);
        Assert.NotEqual(Path.GetFullPath(inputPath), loaded.ResolvedPath);
        Assert.True(File.Exists(loaded.ResolvedPath));
        Assert.Contains(
            logger.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains(
                    "still contains include directives",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void TryLoadAndLinkIfNeeded_CaiWithoutIncludes_UsesInputAsIs()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("cli-load-link-cai-no-includes");
        var inputPath = Path.Combine(cascodeHome.Path, "input.el.cai");
        File.WriteAllText(inputPath, BuildIncludeFreeDocument("NoRelinkCai"));
        var logger = new CapturingLogger();

        var ok = CascodeLoadLinkService.TryLoadAndLinkIfNeeded(
            inputPath,
            repoRoot,
            Path.Combine(cascodeHome.Path, "artifacts"),
            logger,
            out var loaded,
            out var diagnostics
        );

        Assert.True(ok, string.Join(Environment.NewLine, diagnostics.Select(d => d.Message)));
        Assert.Equal(Path.GetFullPath(inputPath), loaded.InputPath);
        Assert.Equal(Path.GetFullPath(inputPath), loaded.ResolvedPath);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void TryLoadAndLinkIfNeeded_CasWithIncludes_Relinks()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("cli-load-link-cas-includes");
        var inputPath = Path.Combine(cascodeHome.Path, "input.el.cas");
        File.WriteAllText(inputPath, BuildIncludeBearingDocument("RelinkFromCas"));
        var logger = new CapturingLogger();

        var ok = CascodeLoadLinkService.TryLoadAndLinkIfNeeded(
            inputPath,
            repoRoot,
            Path.Combine(cascodeHome.Path, "artifacts"),
            logger,
            out var loaded,
            out var diagnostics
        );

        Assert.True(ok, string.Join(Environment.NewLine, diagnostics.Select(d => d.Message)));
        Assert.Equal(Path.GetFullPath(inputPath), loaded.InputPath);
        Assert.NotEqual(Path.GetFullPath(inputPath), loaded.ResolvedPath);
        Assert.True(File.Exists(loaded.ResolvedPath));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void TryLoadAndLinkIfNeeded_CasWithoutIncludes_UsesInputAsIs()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("cli-load-link-cas-no-includes");
        var inputPath = Path.Combine(cascodeHome.Path, "input.el.cas");
        File.WriteAllText(inputPath, BuildIncludeFreeDocument("NoRelinkCas"));
        var logger = new CapturingLogger();

        var ok = CascodeLoadLinkService.TryLoadAndLinkIfNeeded(
            inputPath,
            repoRoot,
            Path.Combine(cascodeHome.Path, "artifacts"),
            logger,
            out var loaded,
            out var diagnostics
        );

        Assert.True(ok, string.Join(Environment.NewLine, diagnostics.Select(d => d.Message)));
        Assert.Equal(Path.GetFullPath(inputPath), loaded.InputPath);
        Assert.Equal(Path.GetFullPath(inputPath), loaded.ResolvedPath);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    private static string BuildIncludeBearingDocument(string circuitName)
    {
        return """
            VERSION 4.0

            include lib.pdk.sky130.devices.nfet_01v8

            circuit __CIRCUIT_NAME__ {
              level EL
              supply VDD
              ground GND
              input IN : analog
              output OUT : analog

              fill {
                NMOS M1 = new nfet_01v8(size(W=1u, L=180n, M=1)) {
                  .D--OUT
                  .G--IN
                  .S--GND
                  .B--GND
                }
              }
            }
            """.Replace("__CIRCUIT_NAME__", circuitName, StringComparison.Ordinal);
    }

    private static string BuildIncludeFreeDocument(string circuitName)
    {
        return """
            VERSION 4.0

            circuit __CIRCUIT_NAME__ {
              level EL
            }
            """.Replace("__CIRCUIT_NAME__", circuitName, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();

        IDisposable ILogger.BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        bool ILogger.IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}
