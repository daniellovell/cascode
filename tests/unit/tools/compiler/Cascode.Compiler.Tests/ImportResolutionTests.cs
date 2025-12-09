using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cascode.CasIR;
using Cascode.Compiler;
using Cascode.Parser;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Compiler.Tests;

/// <summary>
/// Tests for import resolution and motif definition collection.
/// </summary>
public class ImportResolutionTests
{
    [Fact]
    public void Compile_WithImports_IncludesDefinitionsForReferencedMotifs()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var sourcePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEnded.cas");
        var sourceText = File.ReadAllText(sourcePath);

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToCasir(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("analog.ota.OTA5TSingleEnded", CasIRLevel.ML)
            {
                LibraryRoots = new[] { repoRoot }
            });

        Assert.NotNull(result.CasIR);
        var casir = result.CasIR!;

        // Should have definitions for DiffPair and CurrentMirror
        Assert.NotNull(casir.Definitions);
        Assert.Equal(2, casir.Definitions!.Count);

        var diffPair = casir.Definitions.SingleOrDefault(d => d.Name == "DiffPair");
        Assert.NotNull(diffPair);
        Assert.Equal("lib.std.prim", diffPair!.Package);
        Assert.Contains("DiffPairLike", diffPair.Implements!);

        var currentMirror = casir.Definitions.SingleOrDefault(d => d.Name == "CurrentMirror");
        Assert.NotNull(currentMirror);
        Assert.Equal("lib.std.prim", currentMirror!.Package);
        Assert.Contains("CurrentMirrorLike", currentMirror.Implements!);
    }

    [Fact]
    public void Compile_DefinitionIncludesPorts()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var sourcePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEnded.cas");
        var sourceText = File.ReadAllText(sourcePath);

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToCasir(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("analog.ota.OTA5TSingleEnded", CasIRLevel.ML)
            {
                LibraryRoots = new[] { repoRoot }
            });

        Assert.NotNull(result.CasIR);
        var casir = result.CasIR!;

        var diffPair = casir.Definitions!.Single(d => d.Name == "DiffPair");

        // DiffPair should have IN, OUT, BASE ports
        Assert.NotNull(diffPair.Ports);
        Assert.Contains(diffPair.Ports, p => p.Name == "IN" && p.Kind == "Diff");
        Assert.Contains(diffPair.Ports, p => p.Name == "OUT" && p.Kind == "Diff");
        Assert.Contains(diffPair.Ports, p => p.Name == "BASE" && p.Kind == "analog");
    }

    [Fact]
    public void Compile_DefinitionIncludesInstances()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var sourcePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEnded.cas");
        var sourceText = File.ReadAllText(sourcePath);

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToCasir(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("analog.ota.OTA5TSingleEnded", CasIRLevel.ML)
            {
                LibraryRoots = new[] { repoRoot }
            });

        Assert.NotNull(result.CasIR);
        var casir = result.CasIR!;

        var diffPair = casir.Definitions!.Single(d => d.Name == "DiffPair");

        // DiffPair should have internal instances
        Assert.NotNull(diffPair.Instances);
        Assert.True(diffPair.Instances.Count > 0, "DiffPair should have at least one instance");

        // At minimum, M_N should exist (even if type parsing needs improvement)
        Assert.Contains(diffPair.Instances, i => i.Id == "M_N");
    }

    [Fact]
    public void Compile_WithoutLibraryRoots_OmitsDefinitions()
    {
        var sourcePath = "test.cas";
        var sourceText = @"
package test;
motif Test {
    supply VDD; ground GND;
    ports [ OUT: analog ]
    use {
        dp = new DiffPair { p=NMOS };
    }
}";

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToCasir(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("test", CasIRLevel.ML));

        Assert.NotNull(result.CasIR);
        var casir = result.CasIR!;

        // Without library roots, definitions cannot be resolved
        Assert.Null(casir.Definitions);
    }
}

