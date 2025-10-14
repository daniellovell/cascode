using System;
using System.Collections.Generic;
using System.IO;

namespace Cascode.TestSupport;

/// <summary>
/// Provides helpers for creating isolated CASCODE_HOME directories during tests.
/// </summary>
public static class CascodeHome
{
    /// <summary>
    /// Creates an isolated CASCODE_HOME under the specified root directory.
    /// </summary>
    /// <param name="rootDirectory">Base directory that will contain CASCODE_HOME instances.</param>
    /// <param name="prefix">Prefix for the generated directory name.</param>
    /// <param name="setEnvironmentVariable">Whether to update the process-level CASCODE_HOME variable.</param>
    /// <param name="deleteOnDispose">Whether to delete the directory when the scope ends.</param>
    public static CascodeHomeScope CreateUnder(string rootDirectory, string prefix = "cascode-home", bool setEnvironmentVariable = true, bool deleteOnDispose = true)
    {
        Directory.CreateDirectory(rootDirectory);
        var candidate = Path.Combine(rootDirectory, $"{prefix}-{Guid.NewGuid():N}");
        return new CascodeHomeScope(candidate, setEnvironmentVariable, deleteOnDispose);
    }

    /// <summary>
    /// Creates an isolated CASCODE_HOME under the system temporary directory.
    /// </summary>
    /// <param name="prefix">Prefix for the generated directory name.</param>
    /// <param name="setEnvironmentVariable">Whether to update the process-level CASCODE_HOME variable.</param>
    /// <param name="deleteOnDispose">Whether to delete the directory when the scope ends.</param>
    public static CascodeHomeScope CreateInTemp(string prefix = "cascode-home", bool setEnvironmentVariable = true, bool deleteOnDispose = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "cascode-tests");
        return CreateUnder(root, prefix, setEnvironmentVariable, deleteOnDispose);
    }
}

/// <summary>
/// Holds an isolated CASCODE_HOME directory for the lifetime of a test scope.
/// </summary>
public sealed class CascodeHomeScope : IDisposable
{
    private readonly string _path;
    private readonly bool _setEnvironmentVariable;
    private readonly bool _deleteOnDispose;
    private readonly string? _previous;
    private bool _disposed;

    internal CascodeHomeScope(string path, bool setEnvironmentVariable, bool deleteOnDispose)
    {
        _path = path;
        _setEnvironmentVariable = setEnvironmentVariable;
        _deleteOnDispose = deleteOnDispose;
        Directory.CreateDirectory(_path);
        if (_setEnvironmentVariable)
        {
            _previous = Environment.GetEnvironmentVariable("CASCODE_HOME");
            Environment.SetEnvironmentVariable("CASCODE_HOME", _path);
        }
    }

    public string Path => _path;

    /// <summary>
    /// Applies this CASCODE_HOME to a process environment dictionary.
    /// </summary>
    public void ApplyTo(IDictionary<string, string?> environment)
    {
        environment["CASCODE_HOME"] = _path;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_setEnvironmentVariable)
        {
            Environment.SetEnvironmentVariable("CASCODE_HOME", _previous);
        }
        if (_deleteOnDispose)
        {
            try
            {
                if (Directory.Exists(_path))
                {
                    Directory.Delete(_path, recursive: true);
                }
            }
            catch
            {
                // Suppress cleanup errors to keep tests resilient on Windows.
            }
        }
    }
}
