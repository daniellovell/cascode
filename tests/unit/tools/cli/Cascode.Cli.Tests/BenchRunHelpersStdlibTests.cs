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
        var roots = BenchRunHelpers.BuildSearchRoots(workspace);

        Assert.Equal(workspace, roots[0]);
        // Workspace, CWD, and bundled stdlib are all distinct in this scenario.
        Assert.True(roots.Count >= 2, "Expected at least workspace + CWD or stdlib.");
        Assert.Contains(roots, r => r == workspace);
    }

    [Fact]
    public void BuildSearchRoots_Deduplicates_WhenWorkspaceEqualsStdlib()
    {
        // When the workspace root is the same as the bundled stdlib root,
        // we should not get duplicates.
        var stdlibRoot = BenchRunHelpers.GetBundledStdlibRoot();
        Assert.NotNull(stdlibRoot);

        var roots = BenchRunHelpers.BuildSearchRoots(stdlibRoot);
        Assert.Equal(roots.Distinct(StringComparer.Ordinal).Count(), roots.Count);
    }

    [Fact]
    public void BuildSearchRoots_IncludesCwd_BetweenWorkspaceAndStdlib()
    {
        var workspace = "/nonexistent/workspace/root";
        var cwd = Directory.GetCurrentDirectory();
        var roots = BenchRunHelpers.BuildSearchRoots(workspace);
        var rootsList = roots.ToList();

        Assert.Equal(workspace, roots[0]);
        // CWD should appear after workspace root.
        var cwdIndex = rootsList.IndexOf(cwd);
        Assert.True(cwdIndex > 0, "CWD should appear after workspace root.");

        // If stdlib is present, CWD should come before it.
        var stdlibRoot = BenchRunHelpers.GetBundledStdlibRoot();
        if (stdlibRoot is not null)
        {
            var stdlibIndex = rootsList.IndexOf(stdlibRoot);
            Assert.True(cwdIndex < stdlibIndex, "CWD should appear before bundled stdlib.");
        }
    }
}
