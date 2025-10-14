using System;
using System.Collections.Generic;
using System.IO;

using Cascode.Workspace;
using static Cascode.Workspace.Tests.TestUtilities;

namespace Cascode.Workspace.Tests;

public sealed class WorkspaceEnvironmentTests
{
    [Fact]
    public void ParserExpandsIncludesUsingVariablesDeclaredInWorkspaceBashrc()
    {
        var envVarName = $"CASCODE_TEST_{Guid.NewGuid():N}".ToUpperInvariant();

        using var workspace = TemporaryWorkspace.Create();

        Environment.SetEnvironmentVariable(envVarName, null);
        Assert.Null(Environment.GetEnvironmentVariable(envVarName));

        try
        {
            var vendorRoot = workspace.CreateDirectory("vendor");
            var modelsDir = Path.Combine(vendorRoot, "models");
            Directory.CreateDirectory(modelsDir);

            var bashrcContent = $"export {envVarName}=\"{vendorRoot}\"{Environment.NewLine}";
            workspace.WriteFile(".bashrc", bashrcContent);

            var includeExpression = $"${{{envVarName}}}/cds.lib";
            workspace.WriteFile("cds.lib", $"INCLUDE {includeExpression}{Environment.NewLine}");

            var includedLib = $"DEFINE analog_lib ${{{envVarName}}}/models{Environment.NewLine}";
            workspace.WriteFile(Path.Combine("vendor", "cds.lib"), includedLib);

            var parser = new CdsLibParser();
            var warnings = new List<string>();

            var libraries = parser.Parse(workspace.RootPath, warnings);

            Assert.Equal(vendorRoot, Environment.GetEnvironmentVariable(envVarName));
            Assert.DoesNotContain(warnings, w => w.Contains("does not exist", StringComparison.OrdinalIgnoreCase));

            var library = Assert.Single(libraries);
            Assert.Equal("analog_lib", library.Name);

            var expectedPath = Path.GetFullPath(Path.Combine(vendorRoot, "models"));
            Assert.Equal(expectedPath, library.Path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public void ParserHandlesEscapedQuotesAndHashInBashrcValues()
    {
        var envVarName = $"CASCODE_TEST_{Guid.NewGuid():N}".ToUpperInvariant();

        using var workspace = TemporaryWorkspace.Create();

        Environment.SetEnvironmentVariable(envVarName, null);
        Assert.Null(Environment.GetEnvironmentVariable(envVarName));

        try
        {
            // Create a directory that will be referenced by the environment variable
            var testDir = workspace.CreateDirectory("test_dir");

            // Test that escaped quotes don't break comment detection
            // The value contains an escaped quote and a # character, which should NOT be treated as a comment
            // The actual comment after the closing quote SHOULD be removed
            var bashrcContent = $"export {envVarName}=\"{testDir}/with/\\\"escaped\\\" and #hash\" # this is a comment{Environment.NewLine}";
            workspace.WriteFile(".bashrc", bashrcContent);

            // Reference the variable in cds.lib so the parser will load it
            var includeExpression = $"${{{envVarName}}}/cds.lib";
            workspace.WriteFile("cds.lib", $"INCLUDE {includeExpression}{Environment.NewLine}");

            var parser = new CdsLibParser();
            var warnings = new List<string>();

            parser.Parse(workspace.RootPath, warnings);

            var actualValue = Environment.GetEnvironmentVariable(envVarName);
            Assert.NotNull(actualValue);
            // After unwrapping quotes and processing escapes, we should get: {testDir}/with/"escaped" and #hash
            Assert.Equal($"{testDir}/with/\"escaped\" and #hash", actualValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }
}
