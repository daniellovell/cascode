using System;
using System.IO;
using System.Linq;
using Cascode.Language;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class CascodeLinkerTests
{
    [Fact]
    public void LinkFile_ResolvesIncludes_AndExtractsSynthSidecar()
    {
        var repoRoot = Cascode.TestSupport.TestPathUtilities.GetRepositoryRoot();
        var tmp = Path.Combine(
            Path.GetTempPath(),
            "cascode-link-test-" + Guid.NewGuid().ToString("N")
        );
        var outDir = Path.Combine(tmp, "out");
        Directory.CreateDirectory(tmp);

        var entryPath = Path.Combine(tmp, "entry.cas");
        File.WriteAllText(
            entryPath,
            """
            VERSION 3.0

            include lib.std

            circuit LinkerSmoke {
              level EL
              input IN : Diff
              output OUT : analog
              ground GND

              fill { }

              env {
                InputCommonModeRange = 0.6V
                SourceImpedance = 50Ohm
                LoadImpedance = 1kOhm
              }

              benches {
                bind DiffToSETransfer as transfer_bench {
                  bench.IN--dut.IN
                  bench.OUT--dut.OUT
                  dut.GND--gnd
                }
              }

              synth {
                note = "unit-test"
              }
            }
            """
        );

        var result = CascodeLinker.LinkFile(entryPath, outDir, repoRoot);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.LinkedCasPath);
        Assert.NotNull(result.SynthYamlPath);
        Assert.True(File.Exists(result.LinkedCasPath!));
        Assert.True(File.Exists(result.SynthYamlPath!));

        using var reader = File.OpenText(result.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, result.LinkedCasPath!);

        Assert.Empty(linked.Includes);
        Assert.Contains(linked.BenchDefinitions, b => b.Name == "DiffToSETransfer");
        Assert.Contains(linked.Functions, f => f.Name == "calc_passband_freq");

        var circuit = Assert.Single(linked.Circuits);
        Assert.Equal("LinkerSmoke", circuit.Name);
        Assert.Null(circuit.Synth);
    }

    [Fact]
    public void LinkFile_UsesLibraryNamespaceInheritance_ForStdlibFiles()
    {
        var repoRoot = Cascode.TestSupport.TestPathUtilities.GetRepositoryRoot();
        var tmp = Path.Combine(
            Path.GetTempPath(),
            "cascode-link-test-" + Guid.NewGuid().ToString("N")
        );
        var outDir = Path.Combine(tmp, "out");
        Directory.CreateDirectory(tmp);

        // lib/std/bench/TransferBenches.cas does not include lib.std, but per the RFC
        // namespace inheritance, lib.std.bench should see lib.std (which defines Diff).
        var entryPath = Path.Combine(repoRoot, "lib", "std", "bench", "TransferBenches.cas");
        var result = CascodeLinker.LinkFile(entryPath, outDir, repoRoot);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));

        using var reader = File.OpenText(result.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, result.LinkedCasPath!);

        Assert.Contains(linked.BundleTypes, b => b.Name == "Diff");
        Assert.Contains(linked.BenchDefinitions, b => b.Name == "DiffToSETransfer");
    }
}
