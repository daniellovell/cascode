using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class GoldenBenchBindingValidationTests
{
    [Fact]
    public void GoldenFiles_WithInterfaceNumericConstraints_HaveNoUnknownBenchBindingDiagnostics()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var goldenRoot = Path.Combine(repoRoot, "tests", "golden", "cas");
        var candidates = Directory
            .GetFiles(goldenRoot, "*.cai", SearchOption.AllDirectories)
            .Where(path => !IsUnderInvalidDirectory(path))
            .Where(HasInterfaceNumericConstraints)
            .OrderBy(Path.GetFullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.NotEmpty(candidates);

        using var cascodeHome = CascodeHome.CreateInTemp("golden-bench-binding-validation");
        var failures = new List<string>();

        foreach (var goldenPath in candidates)
        {
            var relativePath = Path.GetRelativePath(repoRoot, goldenPath);
            var workDir = Path.Combine(
                cascodeHome.Path,
                "bench-binding-check",
                SanitizePath(relativePath)
            );
            Directory.CreateDirectory(workDir);

            var copiedGoldenPath = Path.Combine(workDir, "golden.cas");
            File.WriteAllText(copiedGoldenPath, File.ReadAllText(goldenPath));

            var entryPath = Path.Combine(workDir, "entry.cas");
            File.WriteAllText(
                entryPath,
                $"VERSION {CascodeVersion.Current}\n\ninclude lib.std\ninclude golden\n"
            );

            var outDir = Path.Combine(workDir, "out");
            var link = CascodeLinker.LinkFile(entryPath, outDir, repoRoot);
            var cas3008 = link.Diagnostics.Where(d => d.Code == "CAS3008").ToList();

            if (cas3008.Count > 0)
            {
                failures.Add(
                    $"{relativePath}: {string.Join(" | ", cas3008.Select(d => d.Message))}"
                );
            }
        }

        Assert.True(
            failures.Count == 0,
            "Expected no CAS3008 diagnostics in linked golden files:\n"
                + string.Join('\n', failures)
        );
    }

    private static bool HasInterfaceNumericConstraints(string path)
    {
        using var reader = File.OpenText(path);
        var parsed = CascodeReader.TryRead(reader, path);
        if (!parsed.Success || parsed.Document is null)
        {
            return false;
        }

        return parsed.Document.Circuits.Any(c =>
            c.Traits is { Count: > 0 } && c.Constraints is { Numeric.Count: > 0 }
        );
    }

    private static bool IsUnderInvalidDirectory(string path)
    {
        var marker = $"{Path.DirectorySeparatorChar}invalid{Path.DirectorySeparatorChar}";
        return path.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizePath(string relativePath)
    {
        var value = relativePath.Replace(Path.DirectorySeparatorChar, '_');
        return value.Replace(Path.AltDirectorySeparatorChar, '_');
    }
}
