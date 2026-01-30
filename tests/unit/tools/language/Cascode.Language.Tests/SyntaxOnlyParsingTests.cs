using System;
using System.IO;
using System.Linq;
using Cascode.Language;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class SyntaxOnlyParsingTests
{
    [Fact]
    public void SyntaxOnlyParse_StdlibBenches_AreParseable()
    {
        var repoRoot = Cascode.TestSupport.TestPathUtilities.GetRepositoryRoot();

        var noisePath = Path.Combine(repoRoot, "lib", "std", "bench", "NoiseBenches.cas");
        var noiseText = File.ReadAllText(noisePath);
        var noise = CascodeParserFacade.Parse(noisePath, noiseText, CascodeParseOptions.SyntaxOnly);
        Assert.True(
            noise.Success,
            string.Join(
                "\n",
                noise.Diagnostics.Select(d => $"{d.FilePath}:{d.Line}:{d.Column}: {d.Message}")
            )
        );
        Assert.Contains(noise.Document!.BenchDefinitions, b => b.Name == "DiffToSENoise");

        var transferPath = Path.Combine(repoRoot, "lib", "std", "bench", "TransferBenches.cas");
        var transferText = File.ReadAllText(transferPath);
        var transfer = CascodeParserFacade.Parse(
            transferPath,
            transferText,
            CascodeParseOptions.SyntaxOnly
        );
        Assert.True(
            transfer.Success,
            string.Join(
                "\n",
                transfer.Diagnostics.Select(d => $"{d.FilePath}:{d.Line}:{d.Column}: {d.Message}")
            )
        );
        Assert.Contains(transfer.Document!.BenchDefinitions, b => b.Name == "DiffToSETransfer");
    }
}
