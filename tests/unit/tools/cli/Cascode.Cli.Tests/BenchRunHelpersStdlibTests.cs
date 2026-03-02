using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Cli.Services;

namespace Cascode.Cli.Tests;

public sealed class BenchRunHelpersStdlibTests
{
    [Fact]
    public void GetBundledStdlibRoot_ReturnsBaseDirectory_WhenLibStdPresent()
    {
        // The CLI project bundles lib/std/ into its output directory, and the
        // test project references it, so the bundled stdlib is present here.
        var result = BenchRunHelpers.GetBundledStdlibRoot();
        Assert.NotNull(result);
        Assert.True(
            Directory.Exists(Path.Combine(result, "lib", "std")),
            "Bundled lib/std/ should exist under the returned root."
        );
    }

    [Fact]
    public void BuildSearchRoots_IncludesStdlibRoot_WhenWorkspaceRootDiffers()
    {
        var workspace = "/some/project/root";
        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        var stdlibRoot = BenchRunHelpers.GetBundledStdlibRoot();
        Assert.NotNull(stdlibRoot);
        var normalizedStdlib = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stdlibRoot));

        var roots = BenchRunHelpers.BuildSearchRoots(workspace);

        Assert.Equal(expected, roots[0]);
        Assert.True(roots.Count >= 2, "Expected at least workspace + CWD/stdlib.");
        Assert.Contains(roots, r => r == expected);
        Assert.Contains(roots, r => r == normalizedStdlib);
    }

    [Fact]
    public void BuildSearchRoots_Deduplicates_WhenWorkspaceEqualsStdlib()
    {
        var stdlibRoot = BenchRunHelpers.GetBundledStdlibRoot();
        Assert.NotNull(stdlibRoot);

        var roots = BenchRunHelpers.BuildSearchRoots(stdlibRoot);
        var comparer = OperatingSystem.IsLinux()
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        Assert.Equal(roots.Distinct(comparer).Count(), roots.Count);
    }

    [Fact]
    public void BuildSearchRoots_Deduplicates_TrailingSeparatorVariant()
    {
        var stdlibRoot = BenchRunHelpers.GetBundledStdlibRoot();
        Assert.NotNull(stdlibRoot);
        var trimmed = Path.TrimEndingDirectorySeparator(stdlibRoot);

        var rootsFromRaw = BenchRunHelpers.BuildSearchRoots(stdlibRoot);
        var rootsFromTrimmed = BenchRunHelpers.BuildSearchRoots(trimmed);

        Assert.Equal(rootsFromRaw.Count, rootsFromTrimmed.Count);
    }

    [Fact]
    public void BuildSearchRoots_IncludesCwd_BetweenWorkspaceAndStdlib()
    {
        var workspace = "/nonexistent/workspace/root";
        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        var cwd = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Directory.GetCurrentDirectory())
        );
        var roots = BenchRunHelpers.BuildSearchRoots(workspace);
        var rootsList = roots.ToList();

        Assert.Equal(expected, roots[0]);
        var cwdIndex = rootsList.IndexOf(cwd);
        Assert.True(cwdIndex > 0, "CWD should appear after workspace root.");

        var stdlibRoot = BenchRunHelpers.GetBundledStdlibRoot();
        if (stdlibRoot is not null)
        {
            var normalizedStdlib = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stdlibRoot));
            var stdlibIndex = rootsList.IndexOf(normalizedStdlib);
            // CWD and stdlib may deduplicate to the same entry (e.g. in test runners);
            // only assert ordering when they are distinct entries.
            if (normalizedStdlib != cwd)
                Assert.True(cwdIndex < stdlibIndex, "CWD should appear before bundled stdlib.");
        }
    }
}
