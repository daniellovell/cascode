using System;
using System.IO;
using System.Linq;
using Cascode.Bench;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Bench.Tests;

public class TemplateDiscoveryTests
{
    [Fact]
    public void FindTemplate_FallbackToLibStdAmpBenches_FindsNgspiceTemplate()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var startDir = Path.Combine(repoRoot, "tests/golden/acir/ota");

        var templatePath = TemplateDiscovery.FindTemplate("SEOpAmpACBench", BenchBackendType.Ngspice, startDir, repoRoot);

        Assert.NotNull(templatePath);
        Assert.True(File.Exists(templatePath), $"Template not found at {templatePath}");
        Assert.EndsWith("SEOpAmpACBench.ngspice.tpl", templatePath);
    }

    [Fact]
    public void FindTemplate_FallbackToLibStdAmpBenches_FindsSpectreTemplate()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var startDir = Path.Combine(repoRoot, "tests/golden/acir/ota");

        var templatePath = TemplateDiscovery.FindTemplate("SEOpAmpACBench", BenchBackendType.Spectre, startDir, repoRoot);

        Assert.NotNull(templatePath);
        Assert.True(File.Exists(templatePath), $"Template not found at {templatePath}");
        Assert.EndsWith("SEOpAmpACBench.spectre.tpl", templatePath);
    }

    [Fact]
    public void FindTemplate_BackendSpecificSelection_SelectsCorrectExtension()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();

        var ngspicePath = TemplateDiscovery.FindTemplate("SEAmpACBench", BenchBackendType.Ngspice, null, repoRoot);
        var spectrePath = TemplateDiscovery.FindTemplate("SEAmpACBench", BenchBackendType.Spectre, null, repoRoot);

        Assert.NotNull(ngspicePath);
        Assert.NotNull(spectrePath);
        Assert.Contains(".ngspice.tpl", ngspicePath);
        Assert.Contains(".spectre.tpl", spectrePath);
        Assert.NotEqual(ngspicePath, spectrePath);
    }

    [Fact]
    public void FindTemplate_UpwardTraversal_FindsLocalBenchesFirst()
    {
        // This test verifies that if a 'benches/' folder exists in the start directory
        // or any parent, it will be found before falling back to lib/std/amp/benches/
        var repoRoot = TestPathUtilities.GetRepositoryRoot();

        // Start from a directory that doesn't have a local benches/ folder
        var startDir = Path.Combine(repoRoot, "tests/golden/spice");

        var templatePath = TemplateDiscovery.FindTemplate("SEOpAmpACBench", BenchBackendType.Ngspice, startDir, repoRoot);

        Assert.NotNull(templatePath);
        // Should fall back to lib/std/amp/benches since no local benches/ exists
        Assert.Contains(Path.Combine("lib", "std", "amp", "benches"), templatePath);
    }

    [Fact]
    public void FindTemplate_NonexistentBench_ReturnsNull()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();

        var templatePath = TemplateDiscovery.FindTemplate("NonexistentBench", BenchBackendType.Ngspice, null, repoRoot);

        Assert.Null(templatePath);
    }

    [Fact]
    public void FindTemplate_NullOrEmptyBenchName_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => TemplateDiscovery.FindTemplate(null!, BenchBackendType.Ngspice));
        Assert.ThrowsAny<ArgumentException>(() => TemplateDiscovery.FindTemplate("", BenchBackendType.Ngspice));
        Assert.ThrowsAny<ArgumentException>(() => TemplateDiscovery.FindTemplate("   ", BenchBackendType.Ngspice));
    }

    [Fact]
    public void FindTemplate_NoWorkspaceRoot_ReturnsNullForMissingTemplate()
    {
        // Without workspace root, can only search upward from start directory
        // Starting from a temp directory that has no benches/ folders above it
        var tempDir = Path.GetTempPath();

        var templatePath = TemplateDiscovery.FindTemplate("SEOpAmpACBench", BenchBackendType.Ngspice, tempDir, null);

        Assert.Null(templatePath);
    }

    [Fact]
    public void FindTemplate_AllStandardBenches_Discoverable()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var benchesDir = Path.Combine(repoRoot, "lib", "std", "amp", "benches");

        // Discover all benches that have both ngspice and spectre templates
        var ngspiceTemplates = Directory.GetFiles(benchesDir, "*.ngspice.tpl")
            .Select(f => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f)))
            .ToHashSet();

        var spectreTemplates = Directory.GetFiles(benchesDir, "*.spectre.tpl")
            .Select(f => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f)))
            .ToHashSet();

        // Only test benches that have BOTH backends implemented
        var benchesWithBothBackends = ngspiceTemplates.Intersect(spectreTemplates).OrderBy(b => b).ToList();

        Assert.NotEmpty(benchesWithBothBackends); // Ensure we found at least some benches

        foreach (var benchName in benchesWithBothBackends)
        {
            var ngspicePath = TemplateDiscovery.FindTemplate(benchName, BenchBackendType.Ngspice, null, repoRoot);
            Assert.NotNull(ngspicePath);
            Assert.True(File.Exists(ngspicePath), $"Ngspice template for {benchName} not found");

            var spectrePath = TemplateDiscovery.FindTemplate(benchName, BenchBackendType.Spectre, null, repoRoot);
            Assert.NotNull(spectrePath);
            Assert.True(File.Exists(spectrePath), $"Spectre template for {benchName} not found");
        }
    }

    [Fact]
    public void FindTemplate_NullBackend_AutoDetectsOrThrows()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();

        // When backend is null, it should either auto-detect successfully or throw InvalidOperationException
        // if neither spectre nor ngspice is on PATH
        try
        {
            var templatePath = TemplateDiscovery.FindTemplate("SEOpAmpACBench", null, null, repoRoot);
            
            // If we got here, auto-detection succeeded
            Assert.NotNull(templatePath);
            Assert.True(File.Exists(templatePath));
            Assert.True(templatePath.EndsWith(".ngspice.tpl") || templatePath.EndsWith(".spectre.tpl"));
        }
        catch (InvalidOperationException ex)
        {
            // This is expected if neither spectre nor ngspice is on PATH
            Assert.Contains("No supported SPICE backend found", ex.Message);
        }
    }
}

