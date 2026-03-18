using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cascode.Cli.Services;

internal enum ArtifactFreshnessStatus
{
    Fresh,
    Stale,
    Unknown,
}

internal sealed record ArtifactFreshnessResult(ArtifactFreshnessStatus Status, string Reason);

internal static class BenchArtifactProvenanceStore
{
    private sealed record ArtifactProvenanceManifest(
        int Version,
        IReadOnlyList<ArtifactSourceFingerprint> Sources
    );

    private sealed record ArtifactSourceFingerprint(string Path, string Sha256);

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public static void Write(string artifactPath, IReadOnlyList<string> sourcePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        ArgumentNullException.ThrowIfNull(sourcePaths);

        var manifest = new ArtifactProvenanceManifest(
            Version: 1,
            Sources: NormalizeSourcePaths(sourcePaths)
                .Select(path => new ArtifactSourceFingerprint(path, ComputeSha256(path)))
                .ToArray()
        );

        File.WriteAllText(
            GetManifestPath(artifactPath),
            JsonSerializer.Serialize(manifest, s_jsonOptions)
        );
    }

    public static ArtifactFreshnessResult EvaluateFreshness(
        string artifactPath,
        IReadOnlyList<string> sourcePaths
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        ArgumentNullException.ThrowIfNull(sourcePaths);

        var manifestPath = GetManifestPath(artifactPath);
        if (!File.Exists(manifestPath))
        {
            return new ArtifactFreshnessResult(
                ArtifactFreshnessStatus.Unknown,
                $"has no provenance manifest at '{manifestPath}'"
            );
        }

        ArtifactProvenanceManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ArtifactProvenanceManifest>(
                File.ReadAllText(manifestPath)
            );
        }
        catch (Exception ex)
        {
            return new ArtifactFreshnessResult(
                ArtifactFreshnessStatus.Stale,
                $"has an unreadable provenance manifest '{manifestPath}': {ex.Message}"
            );
        }

        if (manifest is null || manifest.Version != 1 || manifest.Sources.Count == 0)
        {
            return new ArtifactFreshnessResult(
                ArtifactFreshnessStatus.Stale,
                $"has an invalid provenance manifest '{manifestPath}'"
            );
        }

        var currentSources = NormalizeSourcePaths(sourcePaths);
        var manifestSources = manifest
            .Sources.Select(source => Path.GetFullPath(source.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (
            currentSources.Count != manifestSources.Length
            || !currentSources.SequenceEqual(manifestSources, StringComparer.OrdinalIgnoreCase)
        )
        {
            return new ArtifactFreshnessResult(
                ArtifactFreshnessStatus.Stale,
                "records a different source dependency set"
            );
        }

        foreach (var source in manifest.Sources)
        {
            var fullPath = Path.GetFullPath(source.Path);
            if (!File.Exists(fullPath))
            {
                return new ArtifactFreshnessResult(
                    ArtifactFreshnessStatus.Stale,
                    $"records missing source dependency '{fullPath}'"
                );
            }

            var currentHash = ComputeSha256(fullPath);
            if (!string.Equals(currentHash, source.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ArtifactFreshnessResult(
                    ArtifactFreshnessStatus.Stale,
                    $"does not match source dependency '{fullPath}'"
                );
            }
        }

        return new ArtifactFreshnessResult(ArtifactFreshnessStatus.Fresh, string.Empty);
    }

    internal static string GetManifestPath(string artifactPath)
    {
        var fullArtifactPath = Path.GetFullPath(artifactPath);
        var directory = Path.GetDirectoryName(fullArtifactPath) ?? Directory.GetCurrentDirectory();
        var fileStem = Path.GetFileNameWithoutExtension(fullArtifactPath);
        return Path.Combine(directory, $"{fileStem}.provenance.json");
    }

    private static IReadOnlyList<string> NormalizeSourcePaths(IReadOnlyList<string> sourcePaths)
    {
        return sourcePaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }
}
