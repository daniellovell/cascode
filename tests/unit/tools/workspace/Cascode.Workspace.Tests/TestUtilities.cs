using System;
using System.IO;

namespace Cascode.Workspace.Tests;

/// <summary>
/// Shared test utilities for workspace tests.
/// </summary>
internal static class TestUtilities
{
    internal sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string directoryPath)
        {
            DirectoryPath = directoryPath;
        }

        public string DirectoryPath { get; }

        public static TempDirectory Create(string prefix)
        {
            var root = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TempDirectory(root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    /// <summary>
    /// Creates a temporary workspace for testing that is automatically cleaned up on disposal.
    /// </summary>
    internal sealed class TemporaryWorkspace : IDisposable
    {
        private readonly TempDirectory _tempDirectory;

        private TemporaryWorkspace(TempDirectory tempDirectory)
        {
            _tempDirectory = tempDirectory;
            RootPath = tempDirectory.DirectoryPath;
        }

        public string RootPath { get; }

        public static TemporaryWorkspace Create()
        {
            var tempDirectory = TempDirectory.Create("cascode-workspace-test");
            return new TemporaryWorkspace(tempDirectory);
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
            _tempDirectory.Dispose();
        }
    }

    internal sealed class TempPdkDatabase : IDisposable
    {
        private readonly TempDirectory _tempDirectory;
        private readonly Cascode.Workspace.PdkDatabase _database;
        private bool _disposed;

        private TempPdkDatabase(TempDirectory tempDirectory, string databasePath, Cascode.Workspace.PdkDatabase database)
        {
            _tempDirectory = tempDirectory;
            DatabasePath = databasePath;
            _database = database;
        }

        public string DatabasePath { get; }

        public Cascode.Workspace.PdkDatabase Database => _database;

        public static TempPdkDatabase Create()
        {
            var tempDirectory = TempDirectory.Create("cascode-pdkdb-test");
            var dbPath = Path.Combine(tempDirectory.DirectoryPath, "pdk.db");
            var database = Cascode.Workspace.PdkDatabase.Open(dbPath);
            return new TempPdkDatabase(tempDirectory, dbPath, database);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _database.Dispose();
            _tempDirectory.Dispose();
        }
    }
}
