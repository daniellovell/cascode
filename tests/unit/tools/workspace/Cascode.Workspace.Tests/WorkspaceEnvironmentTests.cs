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
}
