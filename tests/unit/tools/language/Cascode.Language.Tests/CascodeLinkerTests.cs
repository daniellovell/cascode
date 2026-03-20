using System;
using System.IO;
using System.Linq;
using Cascode.Language;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class CascodeLinkerTests
{
    [Fact]
    public void LinkFile_ResolvesIncludes_AndExtractsSynthSidecar()
    {
        var repoRoot = Cascode.TestSupport.TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-link-smoke");
        var outDir = Path.Combine(cascodeHome.Path, "out");

        var entryPath = Path.Combine(cascodeHome.Path, "entry.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

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
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-link-nsinherit");
        var outDir = Path.Combine(cascodeHome.Path, "out");

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

    [Fact]
    public void LinkFile_SelfContainedOutput_RetainsImplementedTraitDefinitions()
    {
        var repoRoot = Cascode.TestSupport.TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-link-trait-contract");
        var outDir = Path.Combine(cascodeHome.Path, "out");
        var entryPath = Path.Combine(cascodeHome.Path, "entry.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit TraitRetention implements SingleEndedOpAmp {
              level EL
              supply VDD
              ground GND
              input IN : Diff
              output OUT : analog
            }
            """
        );

        var result = CascodeLinker.LinkFile(entryPath, outDir, repoRoot);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));

        using var reader = File.OpenText(result.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, result.LinkedCasPath!);
        Assert.Empty(linked.Includes);
        Assert.Contains(linked.Traits, trait => trait.Name == "SingleEndedOpAmp");
        Assert.Contains(linked.BundleTypes, bundle => bundle.Name == "Diff");
    }

    [Fact]
    public void LinkFile_ResolvesBenchBaseAcrossIncludedFiles()
    {
        var tmp = Path.Combine(
            Path.GetTempPath(),
            "cascode-link-test-" + Guid.NewGuid().ToString("N")
        );
        var outDir = Path.Combine(tmp, "out");
        Directory.CreateDirectory(tmp);

        var basePath = Path.Combine(tmp, "base.cas");
        File.WriteAllText(
            basePath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            abstract bench AbstractBase {
              abstract stim IN
              abstract resp OUT

              measurements {
                measurement Gain : dB {
                  return 1dB
                }
              }
            }
            """
        );

        var entryPath = Path.Combine(tmp, "entry.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include base

            bench Concrete extends AbstractBase {
              stim IN : analog
              resp OUT : analog
              fill { }
            }

            circuit LinkBenchBase {
              level EL
              input IN : analog
              output OUT : analog

              benches {
                bind Concrete as concrete {
                  bench.IN--dut.IN
                  bench.OUT--dut.OUT
                }
              }
            }
            """
        );

        var result = CascodeLinker.LinkFile(entryPath, outDir, tmp);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.LinkedCasPath);
        Assert.True(File.Exists(result.LinkedCasPath!));

        using var reader = File.OpenText(result.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, result.LinkedCasPath!);

        Assert.Contains(linked.BenchDefinitions, b => b.Name == "Concrete");
        Assert.DoesNotContain(linked.BenchDefinitions, b => b.Name == "AbstractBase");
    }

    [Fact]
    public void LinkFile_WithBenchPruning_PrunesBenchDefinitions_AndShrinksOutput()
    {
        var repoRoot = Cascode.TestSupport.TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-link-benchprune");
        var outDirFull = Path.Combine(cascodeHome.Path, "out-full");
        var outDirPruned = Path.Combine(cascodeHome.Path, "out-pruned");

        var entryPath = Path.Combine(cascodeHome.Path, "entry.hl.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std

            circuit LinkerBenchPrune implements SingleEndedOpAmp {
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

        var full = CascodeLinker.LinkFile(entryPath, outDirFull, repoRoot);
        Assert.True(full.Success, string.Join("\n", full.Diagnostics.Select(d => d.Message)));

        var pruned = CascodeLinker.LinkFile(
            entryPath,
            outDirPruned,
            repoRoot,
            new CascodeLinkOptions(LinkBenchMode.None, LinkIncludePolicy.Default)
        );
        Assert.True(pruned.Success, string.Join("\n", pruned.Diagnostics.Select(d => d.Message)));

        var fullText = File.ReadAllText(full.LinkedCasPath!);
        var prunedText = File.ReadAllText(pruned.LinkedCasPath!);

        Assert.Contains("bench DiffToSETransfer", fullText);
        Assert.DoesNotContain("bench DiffToSETransfer", prunedText);
        Assert.Contains("transfer_bench::GainBandwidth", prunedText);

        using (var reader = File.OpenText(pruned.LinkedCasPath!))
        {
            var linked = CascodeReader.Read(reader, pruned.LinkedCasPath!);
            Assert.Empty(linked.BenchDefinitions);
            Assert.Contains(linked.Includes, inc => inc.Name == "lib.std.amp.SingleEndedOpAmp");
            Assert.Contains(linked.Includes, inc => inc.Name == "lib.std.bench.DiffToSETransfer");
            Assert.DoesNotContain(linked.Includes, inc => inc.Name == "lib.std");
        }

        Assert.True(prunedText.Length < fullText.Length);
    }

    [Fact]
    public void LinkFile_WithBenchPruning_IncludesOnlyConstrainedBenchFamilies()
    {
        var repoRoot = Cascode.TestSupport.TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-link-fd-bench-scope");
        var outDir = Path.Combine(cascodeHome.Path, "out");

        var entryPath = Path.Combine(cascodeHome.Path, "entry.el.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.std.Diff
            include lib.std.amp.FullyDifferentialOpAmp

            circuit BenchScopeFdOpAmp implements FullyDifferentialOpAmp {
              level EL
              supply VDD
              ground GND
              input IN : Diff
              output OUT : Diff
              input VTAIL : bias

              fill { }

              constraints {
                numeric {
                  c_gain = transfer_bench::PassbandGain at net::OUT >= 40dB
                  c_pwr = vdd_pwr::QuiescentPower <= 1mW
                }
              }

              harness {
                supply VDD = 1.8V
                ground GND = 0V
                bias VTAIL = 0.6V
              }

              env {
                InputCommonModeRange = 0.9V
                LoadImpedance = (10kOhm||1fF)
                SourceImpedance = 50Ohm
              }
            }
            """
        );

        var result = CascodeLinker.LinkFile(
            entryPath,
            outDir,
            repoRoot,
            new CascodeLinkOptions(LinkBenchMode.None, LinkIncludePolicy.Default)
        );
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));

        using var reader = File.OpenText(result.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, result.LinkedCasPath!);
        var includeNames = linked.Includes.Select(inc => inc.Name).ToList();

        Assert.Contains("lib.std.bench.AbstractTransfer", includeNames);
        Assert.Contains("lib.std.bench.DiffToDiffTransfer", includeNames);
        Assert.Contains("lib.std.bench.QuiescentPower", includeNames);

        Assert.DoesNotContain("lib.std.bench.AbstractTran", includeNames);
        Assert.DoesNotContain("lib.std.bench.DiffToDiffTran", includeNames);
        Assert.DoesNotContain("lib.std.bench.AbstractNoise", includeNames);
        Assert.DoesNotContain("lib.std.bench.DiffToDiffNoise", includeNames);
        Assert.DoesNotContain("lib.std.bench.AbstractDCBias", includeNames);
        Assert.DoesNotContain("lib.std.bench.DiffDCBias", includeNames);
        Assert.DoesNotContain("lib.std.bench.AbstractCMRejection", includeNames);
        Assert.DoesNotContain("lib.std.bench.DiffCMRejection", includeNames);
        Assert.DoesNotContain("lib.std.bench.AbstractPSRR", includeNames);
        Assert.DoesNotContain("lib.std.bench.SupplyToDiffRejection", includeNames);
    }

    [Fact]
    public void LinkFile_SymbolLevelPdkInclude_PreservesPreciseInclude_WhenBenchLinkingDisabled()
    {
        var repoRoot = Cascode.TestSupport.TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-link-pdkinc");
        var outDir = Path.Combine(cascodeHome.Path, "out");

        var entryPath = Path.Combine(cascodeHome.Path, "entry.el.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.pdk.sky130.devices.nfet_01v8

            circuit UsesOnlyNfet {
              level EL
              supply VDD
              ground GND
              input IN : analog
              output OUT : analog

              fill {
                NMOS M1 = new nfet_01v8(size(W=1u, L=180n, M=1)) { .D--OUT, .G--IN, .S--GND, .B--GND }
              }
            }
            """
        );

        var result = CascodeLinker.LinkFile(
            entryPath,
            outDir,
            repoRoot,
            new CascodeLinkOptions(LinkBenchMode.None, LinkIncludePolicy.ExplicitOnly)
        );

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        using var reader = File.OpenText(result.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, result.LinkedCasPath!);

        Assert.Single(linked.Includes);
        Assert.Equal("lib.pdk.sky130.devices.nfet_01v8", linked.Includes[0].Name);
        Assert.DoesNotContain(linked.Includes, i => i.Name == "lib.pdk.sky130");
    }

    [Fact]
    public void LinkFile_ExplicitOnlyPolicy_FailsForUndeclaredPrimitive_WithSuggestedInclude()
    {
        var repoRoot = Cascode.TestSupport.TestPathUtilities.GetRepositoryRoot();
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-link-explonly");
        var outDir = Path.Combine(cascodeHome.Path, "out");

        var entryPath = Path.Combine(cascodeHome.Path, "entry.el.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

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

        var result = CascodeLinker.LinkFile(
            entryPath,
            outDir,
            repoRoot,
            new CascodeLinkOptions(LinkBenchMode.Full, LinkIncludePolicy.ExplicitOnly)
        );
        Assert.False(result.Success);

        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Contains(
            errors,
            d =>
                d.Message.Contains(
                    "Unresolved primitive reference 'pfet_01v8'",
                    StringComparison.Ordinal
                )
                && d.Message.Contains(
                    "include lib.pdk.sky130.devices.pfet_01v8",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void LinkFile_WithBenchPruning_ResolvesCrossFileBenchHelperFunctionIncludes()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-link-bench-helper-fn");
        var workspaceRoot = cascodeHome.Path;
        var outDir = Path.Combine(workspaceRoot, "out");

        var helperPath = Path.Combine(workspaceRoot, "bench_helpers.cas");
        File.WriteAllText(
            helperPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            library lib.test.bench

            function get_source_impedance(Impedance fallback) : Impedance {
              return fallback
            }
            """
        );

        var benchPath = Path.Combine(workspaceRoot, "bench_defs.cas");
        File.WriteAllText(
            benchPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            library lib.test.bench

            bench HelperBench {
              stim IN : analog
              resp OUT : analog

              fill {
                net gnd : ground
                GND g = new GND() { .GND--gnd }
                VAC ac = new VAC(A=1V, phase=0deg) { .N--gnd }
                Impedor src = new Impedor(Z=get_source_impedance(0Ohm)) { }
                ac.P--src.P
                src.N--IN
              }

              measurements {
                measurement Gain : dB { return 0dB }
              }
            }
            """
        );

        var traitPath = Path.Combine(workspaceRoot, "filter_interface.cas");
        File.WriteAllText(
            traitPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            library lib.test.filter
            include lib.test.bench

            interface HelperFilter {
              input IN : analog
              output OUT : analog

              benches {
                bind HelperBench as helper_bench {
                  bench.IN--dut.IN
                  bench.OUT--dut.OUT
                }
              }
            }
            """
        );

        var entryPath = Path.Combine(workspaceRoot, "entry.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.test.filter.HelperFilter

            circuit LinkHelperFn implements HelperFilter {
              level EL
              input IN : analog
              output OUT : analog

              fill { }

              constraints {
                numeric {
                  c_gain = helper_bench::Gain >= -1dB
                }
              }
            }
            """
        );

        var result = CascodeLinker.LinkFile(
            entryPath,
            outDir,
            workspaceRoot,
            new CascodeLinkOptions(LinkBenchMode.None, LinkIncludePolicy.Default)
        );
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));

        using var reader = File.OpenText(result.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, result.LinkedCasPath!);
        var includeNames = linked.Includes.Select(inc => inc.Name).ToList();

        Assert.Contains("lib.test.bench.HelperBench", includeNames);
        Assert.Contains("lib.test.bench.get_source_impedance", includeNames);
    }

    [Fact]
    public void LinkFile_WithBenchPruning_DoesNotIncludeFunctionsUsedOnlyByPrunedBenches()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-link-pruned-bench-fn");
        var workspaceRoot = cascodeHome.Path;
        var outDir = Path.Combine(workspaceRoot, "out");

        var helperPath = Path.Combine(workspaceRoot, "bench_helpers.cas");
        File.WriteAllText(
            helperPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            library lib.test.helpers

            function get_pruned_impedance(Impedance fallback) : Impedance {
              return fallback
            }
            """
        );

        var entryPath = Path.Combine(workspaceRoot, "entry.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            include lib.test.helpers

            bench ReachableBench {
              stim IN : analog
              resp OUT : analog

              measurements {
                measurement Gain : dB { return 0dB }
              }
            }

            bench PrunedBench {
              stim IN : analog
              resp OUT : analog

              fill {
                net gnd : ground
                GND g = new GND() { .GND--gnd }
                VAC ac = new VAC(A=1V, phase=0deg) { .N--gnd }
                Impedor src = new Impedor(Z=get_pruned_impedance(0Ohm)) { }
                ac.P--src.P
                src.N--IN
              }

              measurements {
                measurement Gain : dB { return 0dB }
              }
            }

            circuit LinkBenchPruningCircuit {
              level EL
              input IN : analog
              output OUT : analog

              fill { }

              benches {
                bind ReachableBench as reachable_bench {
                  bench.IN--dut.IN
                  bench.OUT--dut.OUT
                }

                bind PrunedBench as pruned_bench {
                  bench.IN--dut.IN
                  bench.OUT--dut.OUT
                }
              }

              constraints {
                numeric {
                  c_gain = reachable_bench::Gain >= -1dB
                }
              }
            }
            """
        );

        var result = CascodeLinker.LinkFile(
            entryPath,
            outDir,
            workspaceRoot,
            new CascodeLinkOptions(LinkBenchMode.None, LinkIncludePolicy.Default)
        );
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));

        using var reader = File.OpenText(result.LinkedCasPath!);
        var linked = CascodeReader.Read(reader, result.LinkedCasPath!);
        var includeNames = linked.Includes.Select(inc => inc.Name).ToList();

        Assert.DoesNotContain("lib.test.helpers.get_pruned_impedance", includeNames);
    }

    [Fact]
    public void LinkFile_WithBenchPruning_AllowsBenchLocalHelperFunctionUsedInsideFill()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-link-local-fill-helper");
        var workspaceRoot = cascodeHome.Path;
        var outDir = Path.Combine(workspaceRoot, "out");

        var entryPath = Path.Combine(workspaceRoot, "entry.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            bench SourceImpedanceBench {
              stim IN : analog
              resp OUT : analog

              function resolve_source_impedance(Impedance fallback) : Impedance {
                if env.SourceImpedance { return env.SourceImpedance }
                return fallback
              }

              fill {
                net gnd : ground
                GND g = new GND() { .GND--gnd }
                VAC ac = new VAC(A=1V, phase=0deg) { .N--gnd }
                Impedor src = new Impedor(Z=resolve_source_impedance(0Ohm)) { }
                ac.P--src.P
                src.N--IN
              }

              measurements {
                measurement Gain : dB { return 0dB }
              }
            }

            circuit LocalFillHelperCircuit {
              level EL
              input IN : analog
              output OUT : analog

              env {
                SourceImpedance = 50Ohm
              }

              fill { }

              benches {
                bind SourceImpedanceBench as source_impedance_bench {
                  bench.IN--dut.IN
                  bench.OUT--dut.OUT
                }
              }

              constraints {
                numeric {
                  c_gain = source_impedance_bench::Gain >= -1dB
                }
              }
            }
            """
        );

        var result = CascodeLinker.LinkFile(
            entryPath,
            outDir,
            workspaceRoot,
            new CascodeLinkOptions(LinkBenchMode.None, LinkIncludePolicy.Default)
        );

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void LinkFile_AllowsKickHarnessPrimitiveWithoutPrimitiveDefinition()
    {
        using var cascodeHome = CascodeHome.CreateInTemp("cascode-link-kick-primitive");
        var workspaceRoot = cascodeHome.Path;
        var outDir = Path.Combine(workspaceRoot, "out");

        var entryPath = Path.Combine(workspaceRoot, "entry.cas");
        File.WriteAllText(
            entryPath,
            $$"""
            VERSION {{CascodeVersion.Current}}

            bench PssBench {
              resp OUT : analog

              fill {
                net gnd : ground
                GND g = new GND() { .GND--gnd }
                Kick kick = new Kick(ic=1) {
                  .P--OUT
                  .N--gnd
                }
                Impedor loadZ = new Impedor(Z=50Ohm) {
                  .P--OUT
                  .N--gnd
                }
              }

              analysis {
                PSSAnalysis pss = new PSSAnalysis(guess_frequency=2.4GHz, stabilization_time=10ns, harmonics=7)
              }

              measurements {
                measurement Freq : Hz { return 1Hz }
              }
            }

            circuit Top {
              level EL
              output OUT : analog

              constraints {
                numeric {
                  c_freq = pss::Freq >= 0Hz
                }
              }

              benches {
                bind PssBench as pss {
                  bench.OUT--dut.OUT
                }
              }

              fill { }
            }
            """
        );

        var result = CascodeLinker.LinkFile(
            entryPath,
            outDir,
            workspaceRoot,
            new CascodeLinkOptions(LinkBenchMode.None, LinkIncludePolicy.Default)
        );

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
    }
}
