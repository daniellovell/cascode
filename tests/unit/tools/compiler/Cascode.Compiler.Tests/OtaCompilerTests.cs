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
            new CompileOptions("analog.ota.OTA5TSingleEndedSimplified", CasIRLevel.ML)
            {
                LibraryRoots = new[] { repoRoot }
            });

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
    ports [ OUT: analog ]
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

    [Fact]
    public void Compile_InstanceBindings_ElaboratesAsPortConnections()
    {
        var sourcePath = "test.cas";
        var sourceText = @"
package test;
motif Test {
    supply VDD; ground GND;
    ports [ IN: Diff, OUT: analog, VTAIL: bias ]
    use {
        dp = new DiffPair { p=NMOS; hasTail=true } {
            IN.P -> IN.P; IN.N -> IN.N; BASE -> GND; BIAS -> VTAIL;
        };
    }
}";

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToCasir(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("test", CasIRLevel.ML));

        Assert.NotNull(result.CasIR);
        var casir = result.CasIR!;

        // Instance should have all bindings elaborated as port connections
        var dp = Assert.Single(casir.Motifs, m => m.Id == "dp");

        // Bindings should be expanded to port connections
        Assert.True(dp.Ports.TryGetValue("IN.P", out var inP));
        Assert.Equal("IN_P", inP); // IN.P maps to bundle net IN_P

        Assert.True(dp.Ports.TryGetValue("IN.N", out var inN));
        Assert.Equal("IN_N", inN); // IN.N maps to bundle net IN_N

        Assert.True(dp.Ports.TryGetValue("BASE", out var baseNet));
        Assert.Equal("GND", baseNet);

        Assert.True(dp.Ports.TryGetValue("BIAS", out var biasNet));
        Assert.Equal("VTAIL", biasNet);
    }

    [Fact]
    public void Compile_FullOTA5TSingleEnded_IncludesAllBindings()
    {
        var repoRoot = GetRepoRoot();
        var sourcePath = Path.Combine(repoRoot, "lib/std/amp/ota/OTA5TSingleEnded.cas");
        var sourceText = File.ReadAllText(sourcePath);

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToCasir(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("analog.ota.OTA5TSingleEnded", CasIRLevel.ML));

        Assert.NotNull(result.CasIR);
        var casir = result.CasIR!;

        // dp instance should have inline bindings + explicit connect
        var dp = Assert.Single(casir.Motifs, m => m.Id == "dp");
        Assert.Equal(5, dp.Ports.Count); // 4 from bindings + 1 from connect dp.OUT.N -> OUT

        Assert.True(dp.Ports.ContainsKey("IN.P"));
        Assert.True(dp.Ports.ContainsKey("IN.N"));
        Assert.True(dp.Ports.ContainsKey("BASE"));
        Assert.True(dp.Ports.ContainsKey("BIAS"));
        Assert.True(dp.Ports.ContainsKey("OUT.N"));
    }

    [Fact]
    public void Compile_GoldenOTA5TSingleEnded_MatchesSnapshot()
    {
        var repoRoot = GetRepoRoot();
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

        // Compare against the golden CasIR snapshot.
        var actualJson = JsonSerializer.Serialize(
            casir,
            new JsonSerializerOptions { WriteIndented = true });
        var expectedJson = File.ReadAllText(
            Path.Combine(repoRoot, "tests/golden/casir/ota/OTA5TSingleEnded.ml.cir"));
        Assert.Equal(Normalize(expectedJson), Normalize(actualJson));
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

    [Fact]
    public void GenerateGoldenCasir()
    {
        var repoRoot = GetRepoRoot();
        var sources = new[] {
            ("tests/golden/cas/ota/OTA5TSingleEndedSimplified.cas", "tests/golden/casir/ota/OTA5TSingleEndedSimplified.ml.cir"),
            ("tests/golden/cas/ota/OTA5TSingleEnded.cas", "tests/golden/casir/ota/OTA5TSingleEnded.ml.cir")
        };
        foreach (var (src, dst) in sources)
        {
            var sourcePath = Path.Combine(repoRoot, src);
            var sourceText = File.ReadAllText(sourcePath);
            var compiler = new SimpleCascodeCompiler();
            var result = compiler.CompileToCasir(
                new[] { new SourceUnit(sourcePath, sourceText) },
                new CompileOptions("test", CasIRLevel.ML) { LibraryRoots = new[] { repoRoot } });
            var json = JsonSerializer.Serialize(result.CasIR, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(repoRoot, dst), json);
        }
    }
}
