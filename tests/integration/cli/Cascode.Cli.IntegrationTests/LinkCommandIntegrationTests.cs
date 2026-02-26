using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.Language;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class LinkCommandIntegrationTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly CascodeHomeScope _cascodeHome;
    private readonly string _workDir;

    public LinkCommandIntegrationTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(_repoRoot, "link");
        _workDir = Path.Combine(_cascodeHome.Path, "link-work");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
        if (Directory.Exists(_workDir))
        {
            try
            {
                Directory.Delete(_workDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task Link_WithNoLinkBenches_DropsBenchDefinitions_AndReducesOutputSize()
    {
        var entryPath = Path.Combine(_workDir, "input.hl.cas");
        await File.WriteAllTextAsync(
            entryPath,
            """
            VERSION 4.0

            include lib.std

            circuit CliLinkBenchPrune implements SingleEndedOpAmp {
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

              constraints {
                numeric {
                  c_gbw = transfer_bench::GainBandwidth >= 1MHz
                  c_power = vdd_pwr::QuiescentPower <= 1mW
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
              }
            }
            """
        );

        var fullOutDir = Path.Combine(_workDir, "link-full");
        var full = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "link",
            entryPath,
            "--out",
            fullOutDir
        );
        CliIntegrationTestHelper.AssertSuccess(full, "link default mode failed");

        var prunedOutDir = Path.Combine(_workDir, "link-pruned");
        var pruned = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "link",
            entryPath,
            "--out",
            prunedOutDir,
            "--no-link-benches"
        );
        CliIntegrationTestHelper.AssertSuccess(pruned, "link --no-link-benches failed");

        var fullPath = Directory
            .GetFiles(fullOutDir, "*.cai", SearchOption.TopDirectoryOnly)
            .Single();
        var prunedPath = Directory
            .GetFiles(prunedOutDir, "*.cai", SearchOption.TopDirectoryOnly)
            .Single();

        var fullText = await File.ReadAllTextAsync(fullPath);
        var prunedText = await File.ReadAllTextAsync(prunedPath);

        Assert.Contains("bench DiffToSETransfer", fullText, StringComparison.Ordinal);
        Assert.DoesNotContain("bench DiffToSETransfer", prunedText, StringComparison.Ordinal);
        Assert.Contains("transfer_bench::GainBandwidth", prunedText, StringComparison.Ordinal);
        Assert.True(prunedText.Length < fullText.Length);

        using var reader = File.OpenText(prunedPath);
        var linked = CascodeReader.Read(reader, prunedPath);
        Assert.Empty(linked.BenchDefinitions);
    }

    [Fact]
    public async Task Emit_RelinksIncludeBearingCai_FromNoLinkBenchesOutput()
    {
        var entryPath = Path.Combine(_workDir, "emit-relink.el.cas");
        await File.WriteAllTextAsync(
            entryPath,
            """
            VERSION 4.0

            include lib.pdk.sky130.devices.nfet_01v8

            circuit CliRelinkEmit {
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

        var prunedOutDir = Path.Combine(_workDir, "emit-relink-pruned");
        var pruned = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "link",
            entryPath,
            "--out",
            prunedOutDir,
            "--no-link-benches"
        );
        CliIntegrationTestHelper.AssertSuccess(pruned, "link --no-link-benches failed");

        var prunedPath = Directory
            .GetFiles(prunedOutDir, "*.cai", SearchOption.TopDirectoryOnly)
            .Single();
        using (var reader = File.OpenText(prunedPath))
        {
            var linked = CascodeReader.Read(reader, prunedPath);
            Assert.NotEmpty(linked.Includes);
            Assert.Empty(linked.Primitives);
        }

        var emitOutDir = Path.Combine(_workDir, "emit-relink-out");
        var emit = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            prunedPath,
            "--out",
            emitOutDir,
            "--backend",
            "ngspice"
        );
        CliIntegrationTestHelper.AssertSuccess(
            emit,
            "emit failed for include-bearing .cai from --no-link-benches output"
        );

        Assert.True(File.Exists(Path.Combine(emitOutDir, "CliRelinkEmit.sp")));
        var combinedOutput = emit.Stdout + Environment.NewLine + emit.Stderr;
        Assert.Contains(
            "still contains include directives",
            combinedOutput,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Link_WithDeprecatedLinkBenchesOption_FailsAsUnknownOption()
    {
        var entryPath = Path.Combine(_workDir, "deprecated-option.el.cas");
        await File.WriteAllTextAsync(
            entryPath,
            """
            VERSION 4.0

            circuit Minimal {
              level EL
            }
            """
        );

        var outDir = Path.Combine(_workDir, "deprecated-option-out");
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "link",
            entryPath,
            "--out",
            outDir,
            "--link-benches=none"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "unknown option '--link-benches=none'",
            result.Stderr,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Link_Help_DescribesNoLinkBenchesBehavior()
    {
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "link",
            "--help"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "link --help failed");
        Assert.Contains("--no-link-benches", result.Stdout, StringComparison.Ordinal);
        Assert.Contains(
            "omit linked bench definitions",
            result.Stdout,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task Link_WithExplicitOnlyIncludePolicy_RejectsUndeclaredPrimitiveWithSuggestion()
    {
        var entryPath = Path.Combine(_workDir, "strict.el.cas");
        await File.WriteAllTextAsync(
            entryPath,
            """
            VERSION 4.0

            include lib.pdk.sky130.devices.nfet_01v8

            circuit UsesUndeclaredPfet {
              level EL
              supply VDD
              ground GND
              input IN : analog
              output OUT : analog

              fill {
                PMOS M1 = new pfet_01v8(size(W=1u, L=180n, M=1)) { .D--OUT, .G--IN, .S--VDD, .B--VDD }
              }
            }
            """
        );

        var outDir = Path.Combine(_workDir, "link-strict");
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "link",
            entryPath,
            "--out",
            outDir,
            "--include-policy=explicit-only"
        );

        Assert.NotEqual(0, result.ExitCode);
        var combined = result.Stdout + Environment.NewLine + result.Stderr;
        Assert.Contains(
            "Unresolved primitive reference 'pfet_01v8'",
            combined,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "include lib.pdk.sky130.devices.pfet_01v8",
            combined,
            StringComparison.Ordinal
        );
    }
}
