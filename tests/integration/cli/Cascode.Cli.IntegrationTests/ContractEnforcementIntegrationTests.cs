using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.Language;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class ContractEnforcementIntegrationTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly CascodeHomeScope _cascodeHome;
    private readonly string _workDir;

    public ContractEnforcementIntegrationTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(
            _repoRoot,
            "contract-enforcement"
        );
        _workDir = Path.Combine(_cascodeHome.Path, "contract-work");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
        if (!Directory.Exists(_workDir))
        {
            return;
        }

        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task Link_FailsAndWritesNoOutput_WhenCircuitViolatesImplementedInterface()
    {
        var inputPath = Path.Combine(_workDir, "broken.cas");
        await File.WriteAllTextAsync(inputPath, BuildBrokenContractDocument());

        var outDir = Path.Combine(_workDir, "link-out");
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "link",
            inputPath,
            "--out",
            outDir
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CAS3031", result.Stderr, StringComparison.Ordinal);
        Assert.True(
            !Directory.Exists(outDir)
                || Directory.GetFiles(outDir, "*.cai", SearchOption.AllDirectories).Length == 0
        );
    }

    [Fact]
    public async Task Emit_Fails_WhenCompleteCaiViolatesImplementedInterface()
    {
        var inputPath = Path.Combine(_workDir, "broken.el.cai");
        await File.WriteAllTextAsync(inputPath, BuildBrokenContractDocument());

        var outDir = Path.Combine(_workDir, "emit-out");
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            inputPath,
            "--out",
            outDir,
            "--backend",
            "ngspice"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CAS3031", result.Stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(outDir, "BrokenFilter.sp")));
    }

    [Fact]
    public async Task Emit_Succeeds_WhenLinkedGoldenPreservesImplementedInterface()
    {
        var inputPath = Path.Combine(_repoRoot, "tests/golden/cas/filters/DiffRCFilter.el.cai");
        var outDir = Path.Combine(_workDir, "emit-golden-out");
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            inputPath,
            "--out",
            outDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(
            result,
            "emit failed for a contract-preserving linked golden"
        );
        Assert.True(File.Exists(Path.Combine(outDir, "DiffRCFilter.sp")));
    }

    [Fact]
    public async Task Erc_Fails_WhenCompleteCaiViolatesImplementedInterface()
    {
        var inputPath = Path.Combine(_workDir, "broken-erc.el.cai");
        await File.WriteAllTextAsync(inputPath, BuildBrokenContractDocument());

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            inputPath
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CAS3031", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Emit_Fails_WhenCompleteCaiOmitsReferencedInterface()
    {
        var inputPath = Path.Combine(_workDir, "missing-interface.el.cai");
        await File.WriteAllTextAsync(inputPath, BuildMissingInterfaceDocument());

        var outDir = Path.Combine(_workDir, "emit-missing-interface-out");
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            inputPath,
            "--out",
            outDir,
            "--backend",
            "ngspice"
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CAS3028", result.Stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(outDir, "MissingInterfaceFilter.sp")));
    }

    [Fact]
    public async Task Erc_Succeeds_WhenLinkedGoldenPreservesImplementedInterface()
    {
        var inputPath = Path.Combine(_repoRoot, "tests/golden/cas/filters/DiffRCFilter.el.cai");
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            inputPath
        );

        CliIntegrationTestHelper.AssertSuccess(
            result,
            "erc failed for a contract-preserving linked golden"
        );
    }

    [Fact]
    public async Task Erc_Fails_WhenCompleteCaiOmitsReferencedInterface()
    {
        var inputPath = Path.Combine(_workDir, "missing-interface-erc.el.cai");
        await File.WriteAllTextAsync(inputPath, BuildMissingInterfaceDocument());

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            inputPath
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CAS3028", result.Stderr, StringComparison.Ordinal);
    }

    private static string BuildBrokenContractDocument()
    {
        return $$"""
            VERSION {{CascodeVersion.Current}}

            bundle Diff {
              P : analog
              N : analog
            }

            interface ExampleFilter {
              input IN : Diff
              output OUT : analog
            }

            circuit BrokenFilter implements ExampleFilter {
              level EL
              input IN : Diff
              output OUT : Diff
            }
            """;
    }

    private static string BuildMissingInterfaceDocument()
    {
        return $$"""
            VERSION {{CascodeVersion.Current}}

            bundle Diff {
              P : analog
              N : analog
            }

            primitive CapacitorIdeal(size primSize) implements Capacitor {
              device "capacitor"
              params {
                C = primSize.C
              }
            }

            primitive ResistorIdeal(size primSize) implements Resistor {
              device "resistor"
              params {
                R = primSize.R
              }
            }

            circuit MissingInterfaceFilter implements ExampleFilter {
              level EL
              ground GND
              input IN : Diff
              output OUT : Diff
              fill {
                Capacitor C_DIFF = new CapacitorIdeal(size(C=1p)) {
                  .N--OUT.N
                  .P--OUT.P
                }
                Resistor R_N = new ResistorIdeal(size(R=10k)) {
                  .N--OUT.N
                  .P--IN.N
                }
                Resistor R_P = new ResistorIdeal(size(R=10k)) {
                  .N--OUT.P
                  .P--IN.P
                }
              }
            }
            """;
    }
}
