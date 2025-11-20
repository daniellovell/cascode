using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cascode.CasIR;
using Cascode.Compiler;
using Cascode.Parser;
using Xunit;

namespace Cascode.Compiler.Tests;

public class OtaCompilerTests
{
    [Fact]
    public void Compile_SimpleOtaMotif_ProducesCasirWithDpAndOutNet()
    {
        var repoRoot = GetRepoRoot();
        var sourcePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEndedSimplified.cas");
        var sourceText = File.ReadAllText(sourcePath);

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToCasir(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("analog.ota.OTA5TSingleEndedSimplified", CasIRLevel.ML));

        Assert.NotNull(result.CasIR);
        var casir = result.CasIR!;

        // Nets should include OUT and bundle nets for IN.
        Assert.Contains(casir.Nets, n => n.Id == "OUT");
        Assert.Contains(casir.Bundles, b => b.Id == "IN");

        // Instances should include dp and cm, with OUT.N mapped to OUT on dp.
        var dp = Assert.Single(casir.Motifs, m => m.Id == "dp");
        Assert.True(dp.Ports.TryGetValue("OUT.N", out var net));
        Assert.Equal("OUT", net);

        Assert.Contains(casir.Motifs, m => m.Id == "cm");

        // Compare against the golden CasIR snapshot.
        var actualJson = JsonSerializer.Serialize(
            casir,
            new JsonSerializerOptions { WriteIndented = true });
        var expectedJson = File.ReadAllText(
            Path.Combine(repoRoot, "tests/golden/casir/ota/OTA5TSingleEndedSimplified.ml.cir"));
        Assert.Equal(Normalize(expectedJson), Normalize(actualJson));
    }

    [Fact]
    public void Compile_NoMotifDeclaration_ReturnsCas0001Diagnostic()
    {
        var sourcePath = "test.cas";
        var sourceText = "package test;\n";

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToCasir(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("test", CasIRLevel.ML));

        Assert.Null(result.CasIR);
        var cas0001Diagnostic = Assert.Single(
            result.Diagnostics,
            d => d.Message.Contains("CAS0001: No motif declaration found"));
        Assert.Equal(DiagnosticSeverity.Error, cas0001Diagnostic.Severity);
        Assert.Equal(sourcePath, cas0001Diagnostic.FilePath);
    }

    [Fact]
    public void Compile_InvalidConnections_ReturnsDiagnostics()
    {
        var sourcePath = "test.cas";
        var sourceText = @"
package test;
motif Test {
    supply VDD; ground GND;
    ports [ OUT: electrical ]
    use {
        inst = new SomeMotif {};
        connect InvalidFormat -> OUT;
        connect missing.OUT -> OUT;
        connect inst.PIN -> MissingNet;
    }
}";

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToCasir(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("test", CasIRLevel.ML));

        Assert.Null(result.CasIR);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, d => d.Message.Contains("CAS0002"));
        Assert.Contains(errors, d => d.Message.Contains("CAS0003"));
        Assert.Contains(errors, d => d.Message.Contains("CAS0004"));
    }

    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "Cascode.sln")))
        {
            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                throw new InvalidOperationException("Unable to locate repository root (Cascode.sln).");
            }

            dir = parent.FullName;
        }

        return dir;
    }

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n").Trim();
}
