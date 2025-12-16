using System;
using System.IO;
using System.Linq;
using Cascode.ACIR;
using Cascode.Compiler;
using Cascode.Parser;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Compiler.Tests;

public class OtaCompilerTests
{
    [Fact]
    public void Compile_SimpleOtaMotif_ProducesACIRWithDpAndOutNet()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var sourcePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEndedSimplified.cas");
        var sourceText = File.ReadAllText(sourcePath);

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToACIR(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("analog.ota.OTA5TSingleEndedSimplified", ACIRLevel.ML)
            {
                LibraryRoots = new[] { repoRoot }
            });

        Assert.NotNull(result.ACIR);
        var acir = result.ACIR!;

        // Circuit should exist
        var circuit = Assert.Single(acir.Circuits);
        Assert.Equal("OTA5TSingleEndedSimplified", circuit.Name);
        Assert.Equal(ACIRLevel.ML, circuit.Level);

        // Fill block should contain instances
        Assert.NotNull(circuit.Fill);
        var dp = Assert.Single(circuit.Fill.Instances, i => i.Id == "dp");
        Assert.True(dp.Bindings.TryGetValue("OUT.N", out var net));
        Assert.Equal("OUT", net);

        Assert.Contains(circuit.Fill.Instances, i => i.Id == "cm");

        // Compare against the golden ACIR snapshot.
        using var writer = new StringWriter();
        ACIRWriter.Write(acir, writer);
        var actualAcir = writer.ToString();
        var expectedAcir = File.ReadAllText(
            Path.Combine(repoRoot, "tests/golden/acir/ota/OTA5TSingleEndedSimplified.ml.cir"));
        Assert.Equal(Normalize(expectedAcir), Normalize(actualAcir));
    }

    [Fact]
    public void Compile_NoMotifDeclaration_ReturnsCas0001Diagnostic()
    {
        var sourcePath = "test.cas";
        var sourceText = "package test;\n";

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToACIR(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("test", ACIRLevel.ML));

        Assert.Null(result.ACIR);
        var cas0001Diagnostic = Assert.Single(
            result.Diagnostics,
            d => d.Message.Contains("CAS0001: No motif declaration found"));
        Assert.Equal("CAS0001", cas0001Diagnostic.Code);
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
        var result = compiler.CompileToACIR(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("test", ACIRLevel.ML));

        Assert.Null(result.ACIR);
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
        var result = compiler.CompileToACIR(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("test", ACIRLevel.ML));

        Assert.NotNull(result.ACIR);
        var acir = result.ACIR!;

        // Circuit should exist
        var circuit = Assert.Single(acir.Circuits);
        Assert.NotNull(circuit.Fill);

        // Instance should have all bindings elaborated as terminal connections
        var dp = Assert.Single(circuit.Fill.Instances, i => i.Id == "dp");

        // Bindings should be expanded to terminal connections
        Assert.True(dp.Bindings.TryGetValue("IN.P", out var inP));
        Assert.Equal("IN_P", inP); // IN.P maps to bundle net IN_P

        Assert.True(dp.Bindings.TryGetValue("IN.N", out var inN));
        Assert.Equal("IN_N", inN); // IN.N maps to bundle net IN_N

        Assert.True(dp.Bindings.TryGetValue("BASE", out var baseNet));
        Assert.Equal("GND", baseNet);

        Assert.True(dp.Bindings.TryGetValue("BIAS", out var biasNet));
        Assert.Equal("VTAIL", biasNet);
    }

    [Fact]
    public void Compile_FullOTA5TSingleEnded_IncludesAllBindings()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var sourcePath = Path.Combine(repoRoot, "lib/std/amp/ota/OTA5TSingleEnded.cas");
        var sourceText = File.ReadAllText(sourcePath);

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToACIR(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("analog.ota.OTA5TSingleEnded", ACIRLevel.ML));

        Assert.NotNull(result.ACIR);
        var acir = result.ACIR!;

        // Circuit should exist
        var circuit = Assert.Single(acir.Circuits);
        Assert.NotNull(circuit.Fill);

        // dp instance should have inline bindings + explicit connect
        var dp = Assert.Single(circuit.Fill.Instances, i => i.Id == "dp");
        Assert.Equal(5, dp.Bindings.Count); // 4 from bindings + 1 from connect dp.OUT.N -> OUT

        Assert.True(dp.Bindings.ContainsKey("IN.P"));
        Assert.True(dp.Bindings.ContainsKey("IN.N"));
        Assert.True(dp.Bindings.ContainsKey("BASE"));
        Assert.True(dp.Bindings.ContainsKey("BIAS"));
        Assert.True(dp.Bindings.ContainsKey("OUT.N"));
    }

    [Fact]
    public void Compile_GoldenOTA5TSingleEnded_MatchesSnapshot()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var sourcePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEnded.cas");
        var sourceText = File.ReadAllText(sourcePath);

        var compiler = new SimpleCascodeCompiler();
        var result = compiler.CompileToACIR(
            new[] { new SourceUnit(sourcePath, sourceText) },
            new CompileOptions("analog.ota.OTA5TSingleEnded", ACIRLevel.ML)
            {
                LibraryRoots = new[] { repoRoot }
            });

        Assert.NotNull(result.ACIR);
        var acir = result.ACIR!;

        // Compare against the golden ACIR snapshot.
        using var writer = new StringWriter();
        ACIRWriter.Write(acir, writer);
        var actualAcir = writer.ToString();
        var expectedAcir = File.ReadAllText(
            Path.Combine(repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.ml.cir"));
        Assert.Equal(Normalize(expectedAcir), Normalize(actualAcir));
    }

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n").Trim();
}
