using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Cascode.Workspace.Tests;

public sealed class ModelGeometryExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public ModelGeometryExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cascode_geom_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Extract_WithSharedFile_ReadsFileOnce()
    {
        // Arrange: Create a model file with two models
        var modelFile = Path.Combine(_tempDir, "models.spm");
        File.WriteAllText(modelFile, @"
.model model_a nmos wmin=0.18u wmax=10u lmin=0.15u lmax=5u
.model model_b pmos wmin=0.24u wmax=12u lmin=0.18u lmax=6u
");

        var models = new List<SpectreModel>
        {
            new SpectreModel
            {
                Name = "model_a",
                ModelType = "nmos",
                DeviceClass = DeviceClass.Nmos,
                SourceFiles = new[] { modelFile }
            },
            new SpectreModel
            {
                Name = "model_b",
                ModelType = "pmos",
                DeviceClass = DeviceClass.Pmos,
                SourceFiles = new[] { modelFile }
            }
        };

        // Act
        var geometry = ModelGeometryExtractor.Extract(models);

        // Assert: Both models should be extracted correctly
        Assert.Equal(2, geometry.Count);

        var geomA = geometry.FirstOrDefault(g => g.ModelName == "model_a");
        Assert.NotNull(geomA);
        Assert.NotNull(geomA.WMin);
        Assert.NotNull(geomA.WMax);
        Assert.NotNull(geomA.LMin);
        Assert.NotNull(geomA.LMax);
        Assert.Equal(0.18e-6, geomA.WMin.Value, precision: 12);
        Assert.Equal(10e-6, geomA.WMax.Value, precision: 12);
        Assert.Equal(0.15e-6, geomA.LMin.Value, precision: 12);
        Assert.Equal(5e-6, geomA.LMax.Value, precision: 12);

        var geomB = geometry.FirstOrDefault(g => g.ModelName == "model_b");
        Assert.NotNull(geomB);
        Assert.NotNull(geomB.WMin);
        Assert.NotNull(geomB.WMax);
        Assert.NotNull(geomB.LMin);
        Assert.NotNull(geomB.LMax);
        Assert.Equal(0.24e-6, geomB.WMin.Value, precision: 12);
        Assert.Equal(12e-6, geomB.WMax.Value, precision: 12);
        Assert.Equal(0.18e-6, geomB.LMin.Value, precision: 12);
        Assert.Equal(6e-6, geomB.LMax.Value, precision: 12);
    }

    [Fact]
    public void Extract_WithSubckt_ExtractsDefaults()
    {
        // Arrange
        var subcktFile = Path.Combine(_tempDir, "subckt.spm");
        File.WriteAllText(subcktFile, @"
.subckt nfet_device d g s b w=1u l=0.18u nf=1
parameters w=1u l=0.18u nf=1
m1 d g s b base_nmos w=w l=l nf=nf
.ends
");

        var models = new List<SpectreModel>
        {
            new SpectreModel
            {
                Name = "nfet_device",
                ModelType = "subckt",
                DeviceClass = DeviceClass.Nmos,
                SourceFiles = new[] { subcktFile }
            }
        };

        // Act
        var geometry = ModelGeometryExtractor.Extract(models);

        // Assert
        Assert.Single(geometry);
        var geom = geometry[0];
        Assert.Equal("nfet_device", geom.ModelName);
        Assert.NotNull(geom.WDefault);
        Assert.NotNull(geom.LDefault);
        Assert.NotNull(geom.NfDefault);
        Assert.Equal(1e-6, geom.WDefault.Value, precision: 12);
        Assert.Equal(0.18e-6, geom.LDefault.Value, precision: 12);
        Assert.Equal(1, geom.NfDefault.Value);
        Assert.Equal("subckt", geom.Source);
    }

    [Fact]
    public void Extract_WithContinuationLines_HandlesCorrectly()
    {
        // Arrange
        var modelFile = Path.Combine(_tempDir, "multiline.spm");
        File.WriteAllText(modelFile, @"
.model long_model nmos \
+ wmin=0.15u wmax=20u \
+ lmin=0.12u lmax=10u
");

        var models = new List<SpectreModel>
        {
            new SpectreModel
            {
                Name = "long_model",
                ModelType = "nmos",
                DeviceClass = DeviceClass.Nmos,
                SourceFiles = new[] { modelFile }
            }
        };

        // Act
        var geometry = ModelGeometryExtractor.Extract(models);

        // Assert
        Assert.Single(geometry);
        var geom = geometry[0];
        Assert.NotNull(geom.WMin);
        Assert.NotNull(geom.WMax);
        Assert.NotNull(geom.LMin);
        Assert.NotNull(geom.LMax);
        Assert.Equal(0.15e-6, geom.WMin.Value, precision: 12);
        Assert.Equal(20e-6, geom.WMax.Value, precision: 12);
        Assert.Equal(0.12e-6, geom.LMin.Value, precision: 12);
        Assert.Equal(10e-6, geom.LMax.Value, precision: 12);
    }

    [Fact]
    public void Extract_WithMultipleReferencesToSameFile_ProducesConsistentResults()
    {
        // Arrange: One file, many models
        var sharedFile = Path.Combine(_tempDir, "shared.spm");
        File.WriteAllText(sharedFile, @"
.model m1 nmos wmin=0.1u wmax=5u lmin=0.1u lmax=2u
.model m2 nmos wmin=0.2u wmax=6u lmin=0.15u lmax=3u
.model m3 nmos wmin=0.3u wmax=7u lmin=0.2u lmax=4u
.model m4 nmos wmin=0.4u wmax=8u lmin=0.25u lmax=5u
.model m5 nmos wmin=0.5u wmax=9u lmin=0.3u lmax=6u
");

        var models = Enumerable.Range(1, 5).Select(i => new SpectreModel
        {
            Name = $"m{i}",
            ModelType = "nmos",
            DeviceClass = DeviceClass.Nmos,
            SourceFiles = new[] { sharedFile }
        }).ToList();

        // Act
        var geometry = ModelGeometryExtractor.Extract(models);

        // Assert: All 5 models should be extracted correctly despite sharing a file
        Assert.Equal(5, geometry.Count);
        for (int i = 1; i <= 5; i++)
        {
            var geom = geometry.FirstOrDefault(g => g.ModelName == $"m{i}");
            Assert.NotNull(geom);
            Assert.NotNull(geom.WMin);
            Assert.Equal((i * 0.1) * 1e-6, geom.WMin.Value, precision: 12);
        }
    }

    [Fact]
    public void Extract_WithMissingFile_SkipsGracefully()
    {
        // Arrange
        var validFile = Path.Combine(_tempDir, "valid.spm");
        File.WriteAllText(validFile, ".model valid_model nmos wmin=0.1u wmax=5u");

        var models = new List<SpectreModel>
        {
            new SpectreModel
            {
                Name = "valid_model",
                ModelType = "nmos",
                DeviceClass = DeviceClass.Nmos,
                SourceFiles = new[] { validFile }
            },
            new SpectreModel
            {
                Name = "missing_model",
                ModelType = "nmos",
                DeviceClass = DeviceClass.Nmos,
                SourceFiles = new[] { Path.Combine(_tempDir, "nonexistent.spm") }
            }
        };

        // Act
        var geometry = ModelGeometryExtractor.Extract(models);

        // Assert: Only the valid model should be in results
        Assert.Single(geometry);
        Assert.Equal("valid_model", geometry[0].ModelName);
    }

    [Fact]
    public void Extract_WithEmptyModelsList_ReturnsEmptyList()
    {
        // Act
        var geometry = ModelGeometryExtractor.Extract(new List<SpectreModel>());

        // Assert
        Assert.Empty(geometry);
    }

    [Fact]
    public void Extract_WithModelHavingNoSourceFiles_SkipsModel()
    {
        // Arrange
        var models = new List<SpectreModel>
        {
            new SpectreModel
            {
                Name = "no_sources",
                ModelType = "nmos",
                DeviceClass = DeviceClass.Nmos,
                SourceFiles = Array.Empty<string>()
            }
        };

        // Act
        var geometry = ModelGeometryExtractor.Extract(models);

        // Assert
        Assert.Empty(geometry);
    }

    [Fact]
    public void Extract_WithMixedModelAndSubckt_IdentifiesAsMixed()
    {
        // Arrange
        var mixedFile = Path.Combine(_tempDir, "mixed.spm");
        File.WriteAllText(mixedFile, @"
.model mixed_device nmos wmin=0.1u wmax=5u lmin=0.1u lmax=2u

.subckt mixed_device d g s b w=1u l=0.18u
m1 d g s b mixed_device w=w l=l
.ends
");

        var models = new List<SpectreModel>
        {
            new SpectreModel
            {
                Name = "mixed_device",
                ModelType = "nmos",
                DeviceClass = DeviceClass.Nmos,
                SourceFiles = new[] { mixedFile }
            }
        };

        // Act
        var geometry = ModelGeometryExtractor.Extract(models);

        // Assert
        Assert.Single(geometry);
        Assert.Equal("mixed", geometry[0].Source);
    }
}
