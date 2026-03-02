using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

/// <summary>
/// Verifies that include resolution works when the input file lives outside the
/// Cascode repository tree, relying on the bundled stdlib in the CLI output directory.
/// </summary>
public sealed class OutOfTreeLinkTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly CascodeHomeScope _cascodeHome;
    private readonly string _workDir;

    public OutOfTreeLinkTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _cascodeHome = CascodeHome.CreateInTemp("out-of-tree-link");
        _workDir = Path.Combine(_cascodeHome.Path, "work");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
    }

    [Fact]
    public async Task Link_OutOfTree_ResolvesStdlibFromBundledPath()
    {
        var entryPath = Path.Combine(_workDir, "stdlib-test.hl.cas");
        await File.WriteAllTextAsync(
            entryPath,
            """
            VERSION 4.0

            include lib.std

            circuit OutOfTreeTest implements SingleEndedOpAmp {
              level HL
              supply VDD
              ground GND
              input IN : Diff
              output OUT : analog

              slot

              env {
                InputCommonModeRange = 0.9V
                SourceImpedance = 50Ohm
                LoadImpedance = 1kOhm
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }
            }
            """
        );

        var outDir = Path.Combine(_workDir, "link-out");
        var result = await RunCliFromWorkDir(_workDir, "link", entryPath, "--out", outDir);

        CliIntegrationTestHelper.AssertSuccess(result, "out-of-tree link with lib.std failed");

        var linkedFile = Directory
            .GetFiles(outDir, "*.cai", SearchOption.TopDirectoryOnly)
            .Single();
        var linkedText = await File.ReadAllTextAsync(linkedFile);
        Assert.Contains("interface SingleEndedOpAmp", linkedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Link_OutOfTree_ResolvesStdlibPdkFromLocalLib()
    {
        // Emit PDK primitives into the work directory, then link a file that
        // references them — all outside the repo tree.
        var pdkFixture = Path.Combine(_repoRoot, "tests", "fixtures", "pdk", "sky130");
        var pdkOutDir = Path.Combine(_workDir, "lib", "pdk", "sky130");

        var scan = await RunCliFromWorkDir(_workDir, "pdk", "scan", pdkFixture);
        CliIntegrationTestHelper.AssertSuccess(scan, "pdk scan failed");

        var emit = await RunCliFromWorkDir(
            _workDir,
            "pdk",
            "emit",
            "primitives",
            "--workspace",
            pdkFixture,
            "--out",
            pdkOutDir
        );
        CliIntegrationTestHelper.AssertSuccess(emit, "pdk emit primitives failed");

        var entryPath = Path.Combine(_workDir, "pdk-test.el.cas");
        await File.WriteAllTextAsync(
            entryPath,
            """
            VERSION 4.0

            include lib.pdk.sky130.devices.nfet_01v8

            circuit PdkOutOfTreeTest {
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
            """
        );

        var linkOutDir = Path.Combine(_workDir, "link-pdk-out");
        var link = await RunCliFromWorkDir(_workDir, "link", entryPath, "--out", linkOutDir);
        CliIntegrationTestHelper.AssertSuccess(link, "out-of-tree link with PDK include failed");

        var linkedFile = Directory
            .GetFiles(linkOutDir, "*.cai", SearchOption.TopDirectoryOnly)
            .Single();
        var linkedText = await File.ReadAllTextAsync(linkedFile);
        Assert.Contains("nfet_01v8", linkedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Link_OutOfTree_ResolvesPdkFromCwd_WhenPdkSetDirActive()
    {
        // Simulate `pdk set-dir` by writing config.json with pdkRoot pointing
        // at the fixture — the config is read from CASCODE_HOME which is set
        // by the CascodeHomeScope helper.
        var pdkFixture = Path.Combine(_repoRoot, "tests", "fixtures", "pdk", "sky130");
        var configDir = _cascodeHome.Path;
        var configPath = Path.Combine(configDir, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            $$"""{"pdkRoot":"{{pdkFixture.Replace("\\", "\\\\")}}"}"""
        );

        // Scan the PDK fixture so the workspace DB exists.
        var scan = await RunCliFromWorkDir(_workDir, "pdk", "scan", pdkFixture);
        CliIntegrationTestHelper.AssertSuccess(scan, "pdk scan failed");

        // Emit PDK primitives into CWD-relative lib/pdk/sky130.
        var pdkOutDir = Path.Combine(_workDir, "lib", "pdk", "sky130");
        var emit = await RunCliFromWorkDir(
            _workDir,
            "pdk",
            "emit",
            "primitives",
            "--pdk",
            "sky130",
            "--out",
            pdkOutDir
        );
        CliIntegrationTestHelper.AssertSuccess(emit, "pdk emit primitives failed");

        // Write a .cas file that includes the emitted PDK library.
        var entryPath = Path.Combine(_workDir, "setdir-pdk-test.el.cas");
        await File.WriteAllTextAsync(
            entryPath,
            """
            VERSION 4.0

            include lib.pdk.sky130.devices.nfet_01v8

            circuit SetDirPdkTest {
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
            """
        );

        var linkOutDir = Path.Combine(_workDir, "link-setdir-out");
        var link = await RunCliFromWorkDir(_workDir, "link", entryPath, "--out", linkOutDir);
        CliIntegrationTestHelper.AssertSuccess(
            link,
            "out-of-tree link with PDK include failed when pdk set-dir is active"
        );

        var linkedFile = Directory
            .GetFiles(linkOutDir, "*.cai", SearchOption.TopDirectoryOnly)
            .Single();
        var linkedText = await File.ReadAllTextAsync(linkedFile);
        Assert.Contains("nfet_01v8", linkedText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs the CLI with the specified working directory instead of the repo root.
    /// This simulates a user invoking cascode from outside the repository tree.
    /// </summary>
    private async Task<CliIntegrationTestHelper.ProcessResult> RunCliFromWorkDir(
        string workingDirectory,
        params string[] args
    )
    {
        var command = CliIntegrationTestHelper.BuildCliCommand(_repoRoot, args);
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            FileName = command.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in command.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var kv in CliIntegrationTestHelper.BuildDeterministicEnvironment(_repoRoot))
        {
            startInfo.Environment[kv.Key] = kv.Value;
        }

        _cascodeHome.ApplyTo(startInfo.Environment);

        var commandLine = $"{startInfo.FileName} {string.Join(' ', startInfo.ArgumentList)}";

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Failed to start CLI");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            CliIntegrationTestHelper.TryKillProcess(process);
            await process.WaitForExitAsync();
            var timedOutStdout = await stdoutTask;
            var timedOutStderr = await stderrTask;
            throw new TimeoutException(
                $"Timed out: {commandLine}\nStdout: {timedOutStdout}\nStderr: {timedOutStderr}"
            );
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new CliIntegrationTestHelper.ProcessResult(
            process.ExitCode,
            stdout,
            stderr,
            commandLine
        );
    }
}
