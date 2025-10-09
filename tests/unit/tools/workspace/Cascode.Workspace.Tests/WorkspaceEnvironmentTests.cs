using System;
using System.Collections.Generic;
using System.IO;

using Cascode.Workspace;

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

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TemporaryWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"cascode-workspace-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TemporaryWorkspace(root);
        }

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public void WriteFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(RootPath, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, contents);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
