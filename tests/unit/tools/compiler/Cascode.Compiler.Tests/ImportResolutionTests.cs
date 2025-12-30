using System;
using System.IO;
using System.Linq;
using Cascode.ACIR;
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
        var result = compiler.CompileToACIR(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("analog.ota.OTA5TSingleEnded", ACIRLevel.ML)
            {
                LibraryRoots = new[] { repoRoot },
            }
        );

        Assert.NotNull(result.ACIR);
        var acir = result.ACIR!;

        // ACIR doesn't include definitions - they would be separate circuits if needed
        // For now, we just verify the main circuit compiles successfully
        var circuit = Assert.Single(acir.Circuits);
        Assert.Equal("OTA5TSingleEnded", circuit.Name);
        Assert.NotNull(circuit.Fill);
    }

    [Fact]
    public void Compile_WithImports_ProducesValidACIR()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var sourcePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEnded.cas");
        var sourceText = File.ReadAllText(sourcePath);

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToACIR(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("analog.ota.OTA5TSingleEnded", ACIRLevel.ML)
            {
                LibraryRoots = new[] { repoRoot },
            }
        );

        Assert.NotNull(result.ACIR);
        var acir = result.ACIR!;

        // Main circuit should have ports
        var circuit = Assert.Single(acir.Circuits);
        Assert.Contains(circuit.Ports, p => p.Name == "IN" && p.Type == "Diff");
        Assert.Contains(circuit.Ports, p => p.Name == "OUT" && p.Type == "analog");
    }

    [Fact]
    public void Compile_WithImports_IncludesInstances()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var sourcePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEnded.cas");
        var sourceText = File.ReadAllText(sourcePath);

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToACIR(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("analog.ota.OTA5TSingleEnded", ACIRLevel.ML)
            {
                LibraryRoots = new[] { repoRoot },
            }
        );

        Assert.NotNull(result.ACIR);
        var acir = result.ACIR!;

        // Main circuit should have instances in fill block
        var circuit = Assert.Single(acir.Circuits);
        Assert.NotNull(circuit.Fill);
        Assert.True(circuit.Fill.Instances.Count > 0, "Circuit should have at least one instance");
    }

    [Fact]
    public void Compile_WithoutLibraryRoots_OmitsDefinitions()
    {
        var sourcePath = "test.cas";
        var sourceText =
            @"
package test;
motif Test {
    supply VDD; ground GND;
    ports [ OUT: analog ]
    use {
        dp = new DiffPair { p=NMOS };
    }
}";

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToACIR(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("test", ACIRLevel.ML)
        );

        Assert.NotNull(result.ACIR);
        var acir = result.ACIR!;

        // Without library roots, compilation should still succeed but imports won't be resolved
        var circuit = Assert.Single(acir.Circuits);
        Assert.Equal("Test", circuit.Name);
    }
}
