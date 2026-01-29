using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cascode.Language.Tests;

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

        var casFiles = Directory.GetFiles(goldenDir, "*.cas", SearchOption.AllDirectories);

        foreach (var file in casFiles)
        {
            var firstLine = File.ReadLines(file).FirstOrDefault();
            Assert.NotNull(firstLine);

            var expected = $"VERSION {ACIRVersion.Current}";
            Assert.True(
                firstLine.StartsWith(expected),
                $"File {Path.GetRelativePath(repoRoot, file)} has version header '{firstLine}' but expected '{expected}'. "
                    + $"Update the file header or regenerate if structure changed."
            );
        }
    }

    [Fact]
    public void Reader_AcceptsSameMajorDifferentMinor()
    {
        var differentMinor = ACIRVersion.Minor + 4;
        var content =
            $"VERSION {ACIRVersion.Major}.{differentMinor}\n" + "circuit Test {\n  level EL\n}\n";
        var result = ACIRReader.TryParse(content);
        Assert.True(result.Success); // No error for minor mismatch
        Assert.Equal(ACIRVersion.Major, result.Document!.VersionMajor);
        Assert.Equal(differentMinor, result.Document.VersionMinor);
    }

    [Fact]
    public void Reader_RejectsDifferentMajor()
    {
        var differentMajor = ACIRVersion.Major + 1;
        var content = $"VERSION {differentMajor}.0\n" + "circuit Test {\n  level EL\n}\n";
        var result = ACIRReader.TryParse(content);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("CAS0007"));
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
