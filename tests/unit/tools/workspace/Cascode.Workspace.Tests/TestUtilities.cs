using System;
using System.IO;

namespace Cascode.Workspace.Tests;

/// <summary>
/// Shared test utilities for workspace tests.
/// </summary>
internal static class TestUtilities
{
    /// <summary>
    /// Creates a temporary workspace for testing that is automatically cleaned up on disposal.
    /// </summary>
    internal sealed class TemporaryWorkspace : IDisposable
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
