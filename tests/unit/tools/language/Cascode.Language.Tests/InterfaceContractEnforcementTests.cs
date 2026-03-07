using System;
using System.IO;
using System.Linq;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class InterfaceContractEnforcementTests
{
    [Fact]
    public void LinkFile_Fails_WhenCircuitDoesNotImplementDeclaredInterfaceContract()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("iface-contract-link-fail");
        var outDir = Path.Combine(cascodeHome.Path, "out");
        var entryPath = Path.Combine(cascodeHome.Path, "entry.cas");
        File.WriteAllText(
            entryPath,
            $$"""
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
            """
        );

        var result = CascodeLinker.LinkFile(entryPath, outDir, cascodeHome.Path);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "CAS3031" || diagnostic.Code == "CAS3032"
        );
        Assert.True(
            !Directory.Exists(outDir)
                || Directory.GetFiles(outDir, "*.cai", SearchOption.AllDirectories).Length == 0
        );
    }

    [Fact]
    public void LinkFile_WithNoLinkBenches_Fails_WhenCircuitDoesNotImplementDeclaredInterfaceContract()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("iface-contract-link-pruned-fail");
        var outDir = Path.Combine(cascodeHome.Path, "out");
        var entryPath = Path.Combine(cascodeHome.Path, "entry.cas");
        File.WriteAllText(
            entryPath,
            $$"""
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
            """
        );

        var result = CascodeLinker.LinkFile(
            entryPath,
            outDir,
            cascodeHome.Path,
            new CascodeLinkOptions(LinkBenchMode.None, LinkIncludePolicy.Default)
        );

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "CAS3031" || diagnostic.Code == "CAS3032"
        );
        Assert.True(
            !Directory.Exists(outDir)
                || Directory.GetFiles(outDir, "*.cai", SearchOption.AllDirectories).Length == 0
        );
    }

    [Fact]
    public void LinkFile_Fails_WhenInterfaceBenchBindingContradictsInterfaceContract()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("iface-binding-link-fail");
        var outDir = Path.Combine(cascodeHome.Path, "out");
        var entryPath = Path.Combine(cascodeHome.Path, "entry.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            interface BrokenDifferentialFilter {
              ground GND
              input IN : Diff
              output OUT : analog

              benches {
                bind DiffToDiffTransfer as transfer_bench {
                  bench.IN--dut.IN
                  bench.OUT--dut.OUT
                }
              }
            }
            """
        );

        var result = CascodeLinker.LinkFile(entryPath, outDir, repoRoot);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CAS3005");
        Assert.Contains(
            result.Diagnostics,
            diagnostic =>
                diagnostic.Message.Contains("BrokenDifferentialFilter", StringComparison.Ordinal)
        );
    }
}
