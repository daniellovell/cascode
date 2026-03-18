using System;
using System.IO;
using Cascode.Cli.Services;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class BenchArtifactProvenanceStoreTests
{
    [Fact]
    public void Write_CreatesProvenanceSidecar_AndEvaluateFreshnessReturnsFresh()
    {
        using var tempDir = new TemporaryDirectory();
        var sourcePath = Path.Combine(tempDir.Path, "design.cas");
        var artifactPath = Path.Combine(tempDir.Path, "Design_results.json");

        File.WriteAllText(sourcePath, "VERSION 4.0\n");
        File.WriteAllText(artifactPath, "{}");

        BenchArtifactProvenanceStore.Write(artifactPath, new[] { sourcePath });

        Assert.True(File.Exists(BenchArtifactProvenanceStore.GetManifestPath(artifactPath)));

        var freshness = BenchArtifactProvenanceStore.EvaluateFreshness(
            artifactPath,
            new[] { sourcePath }
        );

        Assert.Equal(ArtifactFreshnessStatus.Fresh, freshness.Status);
        Assert.Equal(string.Empty, freshness.Reason);
    }

    [Fact]
    public void EvaluateFreshness_ReturnsStale_WhenSourceContentChangesWithoutNewerTimestamp()
    {
        using var tempDir = new TemporaryDirectory();
        var sourcePath = Path.Combine(tempDir.Path, "design.cas");
        var artifactPath = Path.Combine(tempDir.Path, "Design_results.json");

        File.WriteAllText(sourcePath, "alpha");
        File.WriteAllText(artifactPath, "{}");
        BenchArtifactProvenanceStore.Write(artifactPath, new[] { sourcePath });

        var artifactWriteTime = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(artifactPath, artifactWriteTime);

        File.WriteAllText(sourcePath, "beta");
        File.SetLastWriteTimeUtc(sourcePath, artifactWriteTime.AddMinutes(-1));

        var freshness = BenchArtifactProvenanceStore.EvaluateFreshness(
            artifactPath,
            new[] { sourcePath }
        );

        Assert.Equal(ArtifactFreshnessStatus.Stale, freshness.Status);
        Assert.Contains(sourcePath, freshness.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateFreshness_ReturnsUnknown_WhenManifestIsMissing()
    {
        using var tempDir = new TemporaryDirectory();
        var sourcePath = Path.Combine(tempDir.Path, "design.cas");
        var artifactPath = Path.Combine(tempDir.Path, "Design_results.json");

        File.WriteAllText(sourcePath, "VERSION 4.0\n");
        File.WriteAllText(artifactPath, "{}");

        var freshness = BenchArtifactProvenanceStore.EvaluateFreshness(
            artifactPath,
            new[] { sourcePath }
        );

        Assert.Equal(ArtifactFreshnessStatus.Unknown, freshness.Status);
        Assert.Contains("no provenance manifest", freshness.Reason, StringComparison.Ordinal);
    }
}
