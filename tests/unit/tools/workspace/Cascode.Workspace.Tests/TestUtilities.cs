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
        /// <summary>
        /// Initializes a new TempDirectory instance representing the specified directory path.
        /// </summary>
        /// <param name="directoryPath">Full path to the temporary directory managed by this instance.</param>
        private TempDirectory(string directoryPath)
        {
            DirectoryPath = directoryPath;
        }

        public string DirectoryPath { get; }

        /// <summary>
        /// Create a new temporary directory under the system temporary path using the provided prefix and return a TempDirectory representing it.
        /// </summary>
        /// <param name="prefix">Prefix to use for the directory name; a GUID is appended to ensure uniqueness.</param>
        /// <returns>A TempDirectory representing the created temporary directory.</returns>
        public static TempDirectory Create(string prefix)
        {
            var root = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TempDirectory(root);
        }

        /// <summary>
        /// Attempts to delete the temporary directory and its contents.
        /// </summary>
        /// <remarks>
        /// Performs a best-effort recursive delete of <see cref="DirectoryPath"/>; any exceptions during cleanup are swallowed.
        /// </remarks>
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

        /// <summary>
        /// Initializes a TemporaryWorkspace backed by the specified TempDirectory.
        /// </summary>
        /// <param name="tempDirectory">The TempDirectory that provides the workspace root path and manages its lifetime.</param>
        private TemporaryWorkspace(TempDirectory tempDirectory)
        {
            _tempDirectory = tempDirectory;
            RootPath = tempDirectory.DirectoryPath;
        }

        public string RootPath { get; }

        /// <summary>
        /// Create a temporary workspace whose root is a newly created temporary directory for tests.
        /// </summary>
        /// <returns>A TemporaryWorkspace whose root is a newly created temporary directory with the prefix "cascode-workspace-test".</returns>
        public static TemporaryWorkspace Create()
        {
            var tempDirectory = TempDirectory.Create("cascode-workspace-test");
            return new TemporaryWorkspace(tempDirectory);
        }

        /// <summary>
        /// Creates a directory under the workspace root at the specified relative path.
        /// </summary>
        /// <param name="relativePath">Path relative to the workspace root where the directory should be created.</param>
        /// <returns>The full path to the created directory.</returns>
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

        /// <summary>
        /// Releases resources held by the TemporaryWorkspace and performs best-effort cleanup of its temporary directory.
        /// </summary>
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

        /// <summary>
        /// Initializes a new TempPdkDatabase that wraps the provided temporary directory and opened PDK database.
        /// </summary>
        /// <param name="tempDirectory">The backing temporary directory whose lifetime is tied to this instance.</param>
        /// <param name="databasePath">The full file path to the PDK database.</param>
        /// <param name="database">The opened PDK database instance to expose via the Database property.</param>
        private TempPdkDatabase(
            TempDirectory tempDirectory,
            string databasePath,
            Cascode.Workspace.PdkDatabase database
        )
        {
            _tempDirectory = tempDirectory;
            DatabasePath = databasePath;
            _database = database;
        }

        public string DatabasePath { get; }

        public Cascode.Workspace.PdkDatabase Database => _database;

        /// <summary>
        /// Creates a new temporary directory, initializes a PDK database file named "pdk.db" inside it, and opens that database.
        /// </summary>
        /// <returns>A TempPdkDatabase containing the path to the created database file and the opened PDK database instance.</returns>
        public static TempPdkDatabase Create()
        {
            var tempDirectory = TempDirectory.Create("cascode-pdkdb-test");
            var dbPath = Path.Combine(tempDirectory.DirectoryPath, "pdk.db");
            var database = Cascode.Workspace.PdkDatabase.Open(dbPath);
            return new TempPdkDatabase(tempDirectory, dbPath, database);
        }

        /// <summary>
        /// Disposes the underlying PDK database and the temporary directory backing this instance.
        /// </summary>
        /// <remarks>
        /// Safe to call multiple times; subsequent calls have no effect.
        /// </remarks>
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
