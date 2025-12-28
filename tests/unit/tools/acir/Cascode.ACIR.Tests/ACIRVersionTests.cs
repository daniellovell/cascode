using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cascode.ACIR.Tests;

public class ACIRVersionTests
{
    [Fact]
    public void AllGoldenFiles_UseCurrentVersion()
    {
        var repoRoot = FindRepoRoot();
        var goldenDir = Path.Combine(repoRoot, "tests", "golden", "acir");
        
        if (!Directory.Exists(goldenDir))
        {
            // If golden directory doesn't exist, skip test
            return;
        }

        var cirFiles = Directory.GetFiles(goldenDir, "*.cir", SearchOption.AllDirectories);
        
        foreach (var file in cirFiles)
        {
            var firstLine = File.ReadLines(file).FirstOrDefault();
            Assert.NotNull(firstLine);
            
            var expected = $"ACIR {ACIRVersion.Current}";  // "ACIR 1.0"
            Assert.True(
                firstLine.StartsWith(expected),
                $"File {Path.GetRelativePath(repoRoot, file)} has version header '{firstLine}' but expected '{expected}'. " +
                $"Update the file header or regenerate if structure changed.");
        }
    }

    [Fact]
    public void Reader_AcceptsSameMajorDifferentMinor()
    {
        var content = "ACIR 1.5\ncircuit Test\n  level EL";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success);  // No error for minor mismatch
        Assert.Equal(1, result.Document!.VersionMajor);
        Assert.Equal(5, result.Document.VersionMinor);
    }

    [Fact]
    public void Reader_RejectsDifferentMajor()
    {
        var content = "ACIR 2.0\ncircuit Test\n  level EL";
        var result = ACIRReader.TryParse(content);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("ACIR0001"));
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null && !File.Exists(Path.Combine(dir, "Cascode.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? throw new InvalidOperationException("Could not find repo root");
    }
}

