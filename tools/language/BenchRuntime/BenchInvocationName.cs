using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cascode.Language.BenchRuntime;

/// <summary>
/// Computes deterministic, file-safe bench instance names from bench invocation arguments.
/// </summary>
public static class BenchInvocationName
{
    /// <summary>
    /// Computes a deterministic bench instance name from the base binding name and arguments.
    /// When arguments are empty, returns the base name unchanged.
    /// Otherwise, returns BaseName__hashcode where hashcode is an 8-character hex hash of the
    /// canonicalized argument string.
    /// </summary>
    /// <param name="baseName">The bench binding name (e.g., "tran_bench").</param>
    /// <param name="args">The bench invocation arguments (e.g., stim_freq=1kHz).</param>
    /// <returns>A deterministic, file-safe instance name.</returns>
    public static string Compute(string baseName, IReadOnlyList<MetricCallArg> args)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            return baseName;
        }

        // Canonicalize: sort by arg name (case-insensitive), trim values.
        var canonical = string.Join(
            ",",
            args.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(a => $"{a.Name}={a.Value.Trim()}")
        );

        // Compute a short hash for file-safety and determinism.
        var hash = ComputeHash8(canonical);
        return $"{baseName}__{hash}";
    }

    /// <summary>
    /// Computes a deterministic bench instance name from the base binding name and a
    /// dictionary of argument values.
    /// </summary>
    public static string Compute(string baseName, IReadOnlyDictionary<string, string> args)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            return baseName;
        }

        var argList = args.Select(kvp => new MetricCallArg(kvp.Key, kvp.Value)).ToList();
        return Compute(baseName, argList);
    }

    private static string ComputeHash8(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        // Take first 4 bytes (8 hex chars) for a compact yet collision-resistant identifier.
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }
}
