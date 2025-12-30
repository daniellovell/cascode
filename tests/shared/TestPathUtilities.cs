using System;
using System.IO;

namespace Cascode.TestSupport;

/// <summary>
/// Provides helpers for locating repository paths and fixtures in a robust way.
/// </summary>
public static class TestPathUtilities
{
    /// <summary>
    /// Locates the repository root directory by searching upward for 'Cascode.sln'.
    /// </summary>
    public static string GetRepositoryRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cascode.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Unable to locate repository root starting from '{baseDirectory}'. "
                + "Ensure you are running tests from within the repository or a marker file exists."
        );
    }

    /// <summary>
    /// Gets the absolute path to the 'tests/fixtures' directory.
    /// Can be overridden by the 'FIXTURES_DIR' environment variable.
    /// </summary>
    public static string GetFixturesDirectory()
    {
        var envPath = Environment.GetEnvironmentVariable("FIXTURES_DIR");
        if (!string.IsNullOrEmpty(envPath))
        {
            var resolvedPath = Path.GetFullPath(envPath);
            if (!Directory.Exists(resolvedPath))
            {
                throw new DirectoryNotFoundException(
                    $"Fixtures directory not found at '{resolvedPath}' (from FIXTURES_DIR environment variable). "
                        + "Ensure the directory exists or set 'FIXTURES_DIR' to a valid path."
                );
            }
            return resolvedPath;
        }

        var repoRoot = GetRepositoryRoot();
        var fixturesPath = Path.Combine(repoRoot, "tests", "fixtures");

        if (!Directory.Exists(fixturesPath))
        {
            throw new DirectoryNotFoundException(
                $"Fixtures directory not found at '{fixturesPath}'. "
                    + "Ensure the repository structure is correct or set 'FIXTURES_DIR'."
            );
        }

        return fixturesPath;
    }
}
