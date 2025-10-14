using System;
using System.Collections.Generic;
using System.IO;

using Cascode.Workspace;
using static Cascode.Workspace.Tests.TestUtilities;

namespace Cascode.Workspace.Tests;

public sealed class CdsLibParserTests
{
    [Fact]
    public void ParseDefinePreservesQuotedPathsWithSpaces()
    {
        using var workspace = TemporaryWorkspace.Create();

        var libraryDirectory = workspace.CreateDirectory("library path with spaces");
        workspace.WriteFile("cds.lib", $"DEFINE analog_lib \"{libraryDirectory}\"{Environment.NewLine}");

        var parser = new CdsLibParser();
        var warnings = new List<string>();

        var libraries = parser.Parse(workspace.RootPath, warnings);

        Assert.Empty(warnings);

        var library = Assert.Single(libraries);
        Assert.Equal("analog_lib", library.Name);
        Assert.Equal(Path.GetFullPath(libraryDirectory), library.Path);
    }

    [Theory]
    [InlineData("INCLUDE")]
    [InlineData("SOFTINCLUDE")]
    public void ParseIncludeHandlesQuotedPathsWithSpaces(string includeToken)
    {
        using var workspace = TemporaryWorkspace.Create();

        var includeRelativePath = Path.Combine("include directory", "nested cds.lib");
        var includeDirectory = workspace.CreateDirectory("include directory");
        var includedLibraryPath = workspace.CreateDirectory(Path.Combine("include directory", "library location"));

        workspace.WriteFile(
            "cds.lib",
            $"{includeToken} \"{includeRelativePath}\"{Environment.NewLine}");

        workspace.WriteFile(
            Path.Combine("include directory", "nested cds.lib"),
            $"DEFINE nested_lib \"{includedLibraryPath}\"{Environment.NewLine}");

        var parser = new CdsLibParser();
        var warnings = new List<string>();

        var libraries = parser.Parse(workspace.RootPath, warnings);

        Assert.Empty(warnings);

        var library = Assert.Single(libraries);
        Assert.Equal("nested_lib", library.Name);
        Assert.Equal(Path.GetFullPath(includedLibraryPath), library.Path);
    }
}
