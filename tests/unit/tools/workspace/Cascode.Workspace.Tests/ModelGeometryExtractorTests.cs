using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace Cascode.Workspace.Tests;

public class ModelGeometryExtractorTests
{
    private readonly ITestOutputHelper _output;

    public ModelGeometryExtractorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Tests geometry extraction from a binned model file with:
    /// - A .subckt definition with default w/l parameters
    /// - Multiple binned .model definitions with lmin/lmax/wmin/wmax
    /// - Continuation lines (+) after comment lines (*)
    ///
    /// This is a common pattern in Sky130 PDK model files.
    /// </summary>
    [Fact]
    public void Extract_BinnedModelWithContinuationAfterComment_ExtractsGeometry()
    {
        // Snippet from sky130_fd_pr__nfet_03v3_nvt.pm3.spice
        // Note: continuation lines (+) come after comment lines (*)
        var spiceContent =
            @"
.subckt  nfet_03v3_nvt d g s b
.param  l = 1 w = 1 nf = 1.0
msky130_fd_pr__nfet_03v3_nvt d g s b sky130_fd_pr__nfet_03v3_nvt__model l = l w = w nf = nf
.model sky130_fd_pr__nfet_03v3_nvt__model.0 nmos
* DC IV MOS Parameters
+ lmin = 4.95e-07 lmax = 5.05e-07 wmin = 9.995e-06 wmax = 1.0005e-5
+ level = 54.0
.model sky130_fd_pr__nfet_03v3_nvt__model.1 nmos
* DC IV MOS Parameters
+ lmin = 4.95e-07 lmax = 5.05e-07 wmin = 9.95e-07 wmax = 1.005e-6
+ level = 54.0
.model sky130_fd_pr__nfet_03v3_nvt__model.2 nmos
* DC IV MOS Parameters
+ lmin = 5.95e-07 lmax = 6.05e-07 wmin = 9.95e-07 wmax = 1.005e-6
+ level = 54.0
.model sky130_fd_pr__nfet_03v3_nvt__model.6 nmos
* DC IV MOS Parameters
+ lmin = 7.95e-07 lmax = 8.05e-07 wmin = 4.15e-07 wmax = 4.25e-7
+ level = 54.0
.ends nfet_03v3_nvt
";
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, spiceContent);

            var model = new SpectreModel
            {
                Name = "nfet_03v3_nvt",
                ModelType = "subckt",
                SourceFiles = new[] { tempFile },
                Decks = Array.Empty<string>(),
            };

            var geometry = ModelGeometryExtractor.Extract(new[] { model });

            Assert.Single(geometry);
            var geom = geometry[0];

            _output.WriteLine(
                $"Extracted: WMin={geom.WMin}, WMax={geom.WMax}, LMin={geom.LMin}, LMax={geom.LMax}"
            );
            _output.WriteLine($"Source={geom.Source}");

            // Should find both subckt and model
            Assert.Equal("mixed", geom.Source);

            // Geometry should be extracted from the binned models
            // LMin should be min of all lmin values: 4.95e-07
            // LMax should be max of all lmax values: 8.05e-07
            // WMin should be min of all wmin values: 4.15e-07
            // WMax should be max of all wmax values: 1.0005e-5
            Assert.NotNull(geom.LMin);
            Assert.NotNull(geom.LMax);
            Assert.NotNull(geom.WMin);
            Assert.NotNull(geom.WMax);

            Assert.Equal(4.95e-07, geom.LMin!.Value, precision: 10);
            Assert.Equal(8.05e-07, geom.LMax!.Value, precision: 10);
            Assert.Equal(4.15e-07, geom.WMin!.Value, precision: 10);
            Assert.Equal(1.0005e-5, geom.WMax!.Value, precision: 10);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Tests geometry extraction when lmin/lmax/wmin/wmax are on the same line as .model
    /// (no continuation lines needed).
    /// </summary>
    [Fact]
    public void Extract_ModelWithGeometryOnSameLine_ExtractsGeometry()
    {
        var spiceContent =
            @"
.subckt  simple_nfet d g s b
.param  l = 1u w = 1u nf = 1
m1 d g s b simple_nfet__model l=l w=w nf=nf
.model simple_nfet__model nmos lmin=0.18u lmax=10u wmin=0.22u wmax=100u level=54
.ends simple_nfet
";
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, spiceContent);

            var model = new SpectreModel
            {
                Name = "simple_nfet",
                ModelType = "subckt",
                SourceFiles = new[] { tempFile },
                Decks = Array.Empty<string>(),
            };

            var geometry = ModelGeometryExtractor.Extract(new[] { model });

            Assert.Single(geometry);
            var geom = geometry[0];

            _output.WriteLine(
                $"Extracted: WMin={geom.WMin}, WMax={geom.WMax}, LMin={geom.LMin}, LMax={geom.LMax}"
            );

            Assert.NotNull(geom.LMin);
            Assert.NotNull(geom.LMax);
            Assert.NotNull(geom.WMin);
            Assert.NotNull(geom.WMax);

            Assert.Equal(0.18e-6, geom.LMin!.Value, precision: 10);
            Assert.Equal(10e-6, geom.LMax!.Value, precision: 10);
            Assert.Equal(0.22e-6, geom.WMin!.Value, precision: 10);
            Assert.Equal(100e-6, geom.WMax!.Value, precision: 10);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Tests that geometry with spaces around '=' is correctly parsed.
    /// SPICE allows: lmin = 1e-6 or lmin=1e-6
    /// </summary>
    [Fact]
    public void Extract_GeometryWithSpacesAroundEquals_ExtractsCorrectly()
    {
        var spiceContent =
            @"
.subckt  spaced_nfet d g s b
.param  l = 1u w = 1u
m1 d g s b spaced_nfet__model l = l w = w
.model spaced_nfet__model nmos
+ lmin = 0.5e-6 lmax = 1.0e-6 wmin = 0.42e-6 wmax = 10e-6
+ level = 54
.ends spaced_nfet
";
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, spiceContent);

            var model = new SpectreModel
            {
                Name = "spaced_nfet",
                ModelType = "subckt",
                SourceFiles = new[] { tempFile },
                Decks = Array.Empty<string>(),
            };

            var geometry = ModelGeometryExtractor.Extract(new[] { model });

            Assert.Single(geometry);
            var geom = geometry[0];

            _output.WriteLine(
                $"Extracted: WMin={geom.WMin}, WMax={geom.WMax}, LMin={geom.LMin}, LMax={geom.LMax}"
            );

            Assert.NotNull(geom.LMin);
            Assert.NotNull(geom.LMax);
            Assert.NotNull(geom.WMin);
            Assert.NotNull(geom.WMax);

            Assert.Equal(0.5e-6, geom.LMin!.Value, precision: 10);
            Assert.Equal(1.0e-6, geom.LMax!.Value, precision: 10);
            Assert.Equal(0.42e-6, geom.WMin!.Value, precision: 10);
            Assert.Equal(10e-6, geom.WMax!.Value, precision: 10);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Tests extraction of default w/l values from subckt parameters.
    /// </summary>
    [Fact]
    public void Extract_SubcktWithDefaults_ExtractsDefaults()
    {
        var spiceContent =
            @"
.subckt  nfet_with_defaults d g s b w=1u l=180n nf=1
m1 d g s b nfet_with_defaults__model w=w l=l nf=nf
.model nfet_with_defaults__model nmos level=54
.ends nfet_with_defaults
";
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, spiceContent);

            var model = new SpectreModel
            {
                Name = "nfet_with_defaults",
                ModelType = "subckt",
                SourceFiles = new[] { tempFile },
                Decks = Array.Empty<string>(),
            };

            var geometry = ModelGeometryExtractor.Extract(new[] { model });

            Assert.Single(geometry);
            var geom = geometry[0];

            _output.WriteLine(
                $"Extracted defaults: WDefault={geom.WDefault}, LDefault={geom.LDefault}, NfDefault={geom.NfDefault}"
            );

            Assert.NotNull(geom.WDefault);
            Assert.NotNull(geom.LDefault);
            Assert.NotNull(geom.NfDefault);

            Assert.Equal(1e-6, geom.WDefault!.Value, precision: 10);
            Assert.Equal(180e-9, geom.LDefault!.Value, precision: 10);
            Assert.Equal(1, geom.NfDefault!.Value);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
