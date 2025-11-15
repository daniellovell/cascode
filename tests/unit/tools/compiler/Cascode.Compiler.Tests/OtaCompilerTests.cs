using System;
using System.IO;
using System.Text.Json;
using Cascode.Compiler;
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
            new CompileOptions("analog.ota.OTA5TSingleEndedSimplified", "ML"));

        Assert.NotNull(result.Casir);
        var casir = result.Casir!;

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
