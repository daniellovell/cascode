using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Workspace.Tests;

public sealed class CharLutWriterTests : IDisposable
{
    private readonly CascodeHomeScope _cascodeHomeHelper;

    public CharLutWriterTests()
    {
        _cascodeHomeHelper = CascodeHome.CreateInTemp();
    }

    public void Dispose()
    {
        _cascodeHomeHelper.Dispose();
    }

    [Fact]
    public void WriteCharRun_InsertsRunMetadata()
    {
        // Arrange
        var dbPath = Path.Combine(_cascodeHomeHelper.Path, "test.db");
        SetupMinimalDatabase(dbPath);

        var run = new CharRunRecord
        {
            ModelName = "test_model",
            Corner = "tt",
            Backend = "spectre",
            Timestamp = DateTime.UtcNow,
            W_M = 1e-6,
            L_M = 180e-9,
            Nf = 1,
            Vds = 0.9,
            Vsb = 0.0,
            TemperatureC = 27.0,
            Status = "complete",
            JobDir = "/tmp/char/job1",
        };

        // Act
        var runId = CharLutWriter.WriteCharRun(dbPath, run);

        // Assert
        Assert.True(runId > 0);
        var loaded = CharLutReader.LoadCharRun(dbPath, runId);
        Assert.NotNull(loaded);
        Assert.Equal("test_model", loaded.ModelName);
        Assert.Equal("tt", loaded.Corner);
        Assert.Equal("spectre", loaded.Backend);
        Assert.Equal(1e-6, loaded.W_M, precision: 12);
        Assert.Equal(180e-9, loaded.L_M, precision: 12);
        Assert.Equal(1, loaded.Nf);
        Assert.Equal(0.9, loaded.Vds, precision: 6);
        Assert.Equal(27.0, loaded.TemperatureC, precision: 6);
        Assert.Equal("complete", loaded.Status);
    }

    [Fact]
    public void WriteLutPoints_InsertsDataPoints()
    {
        // Arrange
        var dbPath = Path.Combine(_cascodeHomeHelper.Path, "test_points.db");
        SetupMinimalDatabase(dbPath);

        var run = new CharRunRecord
        {
            ModelName = "test_model",
            Corner = "tt",
            Backend = "spectre",
            Timestamp = DateTime.UtcNow,
            W_M = 1e-6,
            L_M = 180e-9,
            Nf = 1,
            Vds = 0.9,
            Vsb = 0.0,
            TemperatureC = 27.0,
            Status = "complete",
            JobDir = "/tmp/char/job2",
        };
        var runId = CharLutWriter.WriteCharRun(dbPath, run);

        var points = new List<CharLutPoint>
        {
            new()
            {
                Vgs = 0.0,
                Id = 1e-12,
                Gm = 1e-9,
                GmOverId = 1000,
            },
            new()
            {
                Vgs = 0.3,
                Id = 1e-9,
                Gm = 1e-6,
                GmOverId = 1000,
            },
            new()
            {
                Vgs = 0.6,
                Id = 1e-6,
                Gm = 1e-3,
                GmOverId = 1000,
                Vth = 0.4,
            },
            new()
            {
                Vgs = 0.9,
                Id = 1e-4,
                Gm = 1e-2,
                GmOverId = 100,
                Vth = 0.4,
                GmRo = 50,
            },
        };

        // Act
        CharLutWriter.WriteLutPoints(dbPath, runId, points);

        // Assert
        var loaded = CharLutReader.LoadLutPoints(dbPath, runId);
        Assert.Equal(4, loaded.Count);
        Assert.Equal(0.0, loaded[0].Vgs, precision: 6);
        Assert.Equal(0.9, loaded[3].Vgs, precision: 6);
        Assert.Equal(1e-4, loaded[3].Id!.Value, precision: 12);
        Assert.Equal(50, loaded[3].GmRo!.Value, precision: 6);
    }

    [Fact]
    public void WriteRunSummary_ComputesAndStoresPeakMetrics()
    {
        // Arrange
        var dbPath = Path.Combine(_cascodeHomeHelper.Path, "test_summary.db");
        SetupMinimalDatabase(dbPath);

        var run = new CharRunRecord
        {
            ModelName = "test_model",
            Corner = "tt",
            Backend = "spectre",
            Timestamp = DateTime.UtcNow,
            W_M = 1e-6,
            L_M = 180e-9,
            Nf = 1,
            Vds = 0.9,
            Vsb = 0.0,
            TemperatureC = 27.0,
            Status = "complete",
            JobDir = "/tmp/char/job3",
        };
        var runId = CharLutWriter.WriteCharRun(dbPath, run);

        // Points with varying gm/Id - peak at Vgs=0.4
        var points = new List<CharLutPoint>
        {
            new()
            {
                Vgs = 0.2,
                Id = 1e-9,
                Gm = 5e-9,
                GmOverId = 5.0,
            },
            new()
            {
                Vgs = 0.3,
                Id = 1e-8,
                Gm = 2e-7,
                GmOverId = 20.0,
                Vth = 0.35,
            },
            new()
            {
                Vgs = 0.4,
                Id = 1e-7,
                Gm = 3e-6,
                GmOverId = 30.0,
                Vth = 0.35,
            }, // Peak gm/Id
            new()
            {
                Vgs = 0.5,
                Id = 1e-6,
                Gm = 2e-5,
                GmOverId = 20.0,
                Vth = 0.35,
                GmRo = 45,
            },
            new()
            {
                Vgs = 0.6,
                Id = 1e-5,
                Gm = 1e-4,
                GmOverId = 10.0,
                Vth = 0.35,
                GmRo = 50,
                Ft = 1e10,
            }, // Max gm*ro and ft
            new()
            {
                Vgs = 0.7,
                Id = 1e-4,
                Gm = 5e-4,
                GmOverId = 5.0,
                Vth = 0.35,
                GmRo = 40,
                Ft = 8e9,
            },
        };
        CharLutWriter.WriteLutPoints(dbPath, runId, points);

        // Act
        CharLutWriter.WriteRunSummary(dbPath, runId);

        // Assert
        var summary = CharLutReader.LoadRunSummary(dbPath, runId);
        Assert.NotNull(summary);
        Assert.Equal(30.0, summary.GmIdPeak!.Value, precision: 6);
        Assert.Equal(0.4, summary.VgsAtPeakGmId!.Value, precision: 6);
        Assert.Equal(0.35, summary.VthExtracted!.Value, precision: 6);
        Assert.Equal(50, summary.GmRoMax!.Value, precision: 6);
        Assert.Equal(1e10, summary.FtMax!.Value, precision: 6);
    }

    [Fact]
    public void ImportFromDerivedCsv_ParsesAndStoresLut()
    {
        // Arrange
        var dbPath = Path.Combine(_cascodeHomeHelper.Path, "test_import.db");
        SetupMinimalDatabase(dbPath);

        var jobDir = Path.Combine(_cascodeHomeHelper.Path, "job_import");
        Directory.CreateDirectory(jobDir);

        // Create a mock spec.json
        var specJson =
            @"{
            ""model_name"": ""test_model"",
            ""corner"": ""tt"",
            ""backend"": ""spectre"",
            ""w_m"": 1e-6,
            ""l_m"": 1.8e-7,
            ""nf"": 1,
            ""vds_fixed"": 0.9,
            ""vsb_fixed"": 0.0,
            ""temperature_c"": 27.0
        }";
        File.WriteAllText(Path.Combine(jobDir, "spec.json"), specJson);

        // Create a mock derived.csv with realistic gm/Id curve (peak in weak inversion)
        var derivedCsv =
            @"vgs,vds,id,gm,gm_over_id,vth,gm_ro,ft
0.0,0.9,1e-12,1e-11,10,,,
0.3,0.9,1e-9,2e-8,20,0.35,,
0.6,0.9,1e-6,3e-5,30,0.35,45,5e9
0.9,0.9,1e-4,2e-3,20,0.35,50,1e10
1.2,0.9,1e-3,5e-3,5,0.35,30,8e9";
        File.WriteAllText(Path.Combine(jobDir, "derived.csv"), derivedCsv);

        // Act
        var runId = CharLutWriter.ImportFromJobDir(dbPath, jobDir);

        // Assert
        Assert.True(runId > 0);

        var run = CharLutReader.LoadCharRun(dbPath, runId);
        Assert.NotNull(run);
        Assert.Equal("test_model", run.ModelName);
        Assert.Equal("tt", run.Corner);

        var points = CharLutReader.LoadLutPoints(dbPath, runId);
        Assert.Equal(5, points.Count);
        Assert.Equal(0.6, points[2].Vgs, precision: 6);
        Assert.Equal(1e-6, points[2].Id!.Value, precision: 12);

        var summary = CharLutReader.LoadRunSummary(dbPath, runId);
        Assert.NotNull(summary);
        Assert.Equal(30.0, summary.GmIdPeak!.Value, precision: 6);
    }

    [Fact]
    public void GetCharacterizationCoverage_ReturnsModelCornerMatrix()
    {
        // Arrange
        var dbPath = Path.Combine(_cascodeHomeHelper.Path, "test_coverage.db");
        SetupMinimalDatabase(dbPath);

        // Add a second model
        using (var db = PdkDatabase.Open(dbPath))
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO models(name, model_type, device_class) VALUES ('model_2', 'nmos', 1)";
            cmd.ExecuteNonQuery();
        }

        // Create runs for different model/corner combinations
        var runs = new[]
        {
            new CharRunRecord
            {
                ModelName = "test_model",
                Corner = "tt",
                Backend = "spectre",
                Timestamp = DateTime.UtcNow,
                W_M = 1e-6,
                L_M = 180e-9,
                Nf = 1,
                Vds = 0.9,
                Vsb = 0,
                TemperatureC = 27,
                Status = "complete",
                JobDir = "/tmp/j1",
            },
            new CharRunRecord
            {
                ModelName = "test_model",
                Corner = "ff",
                Backend = "spectre",
                Timestamp = DateTime.UtcNow,
                W_M = 1e-6,
                L_M = 180e-9,
                Nf = 1,
                Vds = 0.9,
                Vsb = 0,
                TemperatureC = 27,
                Status = "complete",
                JobDir = "/tmp/j2",
            },
            new CharRunRecord
            {
                ModelName = "model_2",
                Corner = "tt",
                Backend = "spectre",
                Timestamp = DateTime.UtcNow,
                W_M = 1e-6,
                L_M = 180e-9,
                Nf = 1,
                Vds = 0.9,
                Vsb = 0,
                TemperatureC = 27,
                Status = "complete",
                JobDir = "/tmp/j3",
            },
        };
        foreach (var r in runs)
            CharLutWriter.WriteCharRun(dbPath, r);

        // Act
        var coverage = CharLutReader.GetCharacterizationCoverage(dbPath);

        // Assert
        Assert.Equal(2, coverage.Models.Count);
        Assert.Contains("test_model", coverage.Models);
        Assert.Contains("model_2", coverage.Models);
        Assert.Equal(2, coverage.Corners.Count);
        Assert.Contains("tt", coverage.Corners);
        Assert.Contains("ff", coverage.Corners);
        Assert.Equal(3, coverage.TotalRuns);
        Assert.True(coverage.HasRun("test_model", "tt"));
        Assert.True(coverage.HasRun("test_model", "ff"));
        Assert.True(coverage.HasRun("model_2", "tt"));
        Assert.False(coverage.HasRun("model_2", "ff"));
    }

    [Fact]
    public void GetDeviceCoverage_UsesDeviceRuns()
    {
        var dbPath = Path.Combine(_cascodeHomeHelper.Path, "test_device_coverage.db");
        SetupMinimalDatabase(dbPath);

        using (var db = PdkDatabase.Open(dbPath))
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText =
                @"
                INSERT INTO devices(canonical_name, display_name, lib_name, lib_path, cell_name, cell_path, device_class, device_subclass, has_layout, has_symbol, vt_tags, vdd_tags, tags)
                VALUES ('lib__d1', 'd1', 'lib', '/tmp', 'd1', '/tmp/d1', 1, 0, 1, 1, 'LVT', 1.8, NULL);
                INSERT INTO devices(canonical_name, display_name, lib_name, lib_path, cell_name, cell_path, device_class, device_subclass, has_layout, has_symbol, vt_tags, vdd_tags, tags)
                VALUES ('lib__p1', 'p1', 'lib', '/tmp', 'p1', '/tmp/p1', 2, 0, 1, 1, 'HVT', 1.8, NULL);";
            cmd.ExecuteNonQuery();
        }

        var runs = new[]
        {
            new CharRunRecord
            {
                ModelName = "test_model",
                DeviceName = "lib__d1",
                Corner = "tt",
                Backend = "spectre",
                Timestamp = DateTime.UtcNow,
                W_M = 1e-6,
                L_M = 180e-9,
                Nf = 1,
                Vds = 0.9,
                Vsb = 0,
                TemperatureC = 27,
                Status = "complete",
                JobDir = "/tmp/j1",
            },
            new CharRunRecord
            {
                ModelName = "test_model",
                DeviceName = "lib__d1",
                Corner = "ff",
                Backend = "spectre",
                Timestamp = DateTime.UtcNow,
                W_M = 1e-6,
                L_M = 180e-9,
                Nf = 1,
                Vds = 0.9,
                Vsb = 0,
                TemperatureC = 27,
                Status = "complete",
                JobDir = "/tmp/j2",
            },
            new CharRunRecord
            {
                ModelName = "test_model",
                DeviceName = "lib__p1",
                Corner = "tt",
                Backend = "spectre",
                Timestamp = DateTime.UtcNow,
                W_M = 1e-6,
                L_M = 180e-9,
                Nf = 1,
                Vds = 0.9,
                Vsb = 0,
                TemperatureC = 27,
                Status = "complete",
                JobDir = "/tmp/j3",
            },
        };
        foreach (var r in runs)
            CharLutWriter.WriteCharRun(dbPath, r);

        var coverage = CharLutReader.GetDeviceCoverage(dbPath);
        Assert.Equal(2, coverage.Devices.Count);
        Assert.Contains("lib__d1", coverage.Devices);
        Assert.Contains("lib__p1", coverage.Devices);
        Assert.Contains("tt", coverage.Corners);
        Assert.Contains("ff", coverage.Corners);
        Assert.Equal(3, coverage.TotalRuns);
        Assert.True(coverage.HasRun("lib__d1", "tt"));
        Assert.True(coverage.HasRun("lib__d1", "ff"));
        Assert.True(coverage.HasRun("lib__p1", "tt"));
        Assert.False(coverage.HasRun("lib__p1", "ff"));
        Assert.Equal(DeviceClass.Nmos, coverage.GetDeviceClass("lib__d1"));
        Assert.Equal(DeviceClass.Pmos, coverage.GetDeviceClass("lib__p1"));
    }

    [Fact]
    public void GetLatestRunForModel_ReturnsNewestRun()
    {
        // Arrange
        var dbPath = Path.Combine(_cascodeHomeHelper.Path, "test_latest.db");
        SetupMinimalDatabase(dbPath);

        var older = new CharRunRecord
        {
            ModelName = "test_model",
            Corner = "tt",
            Backend = "spectre",
            Timestamp = DateTime.UtcNow.AddHours(-1),
            W_M = 1e-6,
            L_M = 180e-9,
            Nf = 1,
            Vds = 0.9,
            Vsb = 0,
            TemperatureC = 27,
            Status = "complete",
            JobDir = "/tmp/old",
        };
        var newer = new CharRunRecord
        {
            ModelName = "test_model",
            Corner = "tt",
            Backend = "spectre",
            Timestamp = DateTime.UtcNow,
            W_M = 2e-6, // Different W to distinguish
            L_M = 180e-9,
            Nf = 1,
            Vds = 0.9,
            Vsb = 0,
            TemperatureC = 27,
            Status = "complete",
            JobDir = "/tmp/new",
        };

        CharLutWriter.WriteCharRun(dbPath, older);
        CharLutWriter.WriteCharRun(dbPath, newer);

        // Act
        var latest = CharLutReader.GetLatestRunForModel(dbPath, "test_model", "tt");

        // Assert
        Assert.NotNull(latest);
        Assert.Equal(2e-6, latest.W_M, precision: 12);
        Assert.Equal("/tmp/new", latest.JobDir);
    }

    [Fact]
    public void WriteCharRun_AssociatesDeviceWhenPresent()
    {
        // Arrange
        var dbPath = Path.Combine(_cascodeHomeHelper.Path, "test_device.db");
        SetupMinimalDatabase(dbPath);

        using (var db = PdkDatabase.Open(dbPath))
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText =
                @"
                INSERT INTO devices(canonical_name, display_name, lib_name, lib_path, cell_name, cell_path, device_class, device_subclass, has_layout, has_symbol)
                VALUES ('lib:cell', 'cell', 'lib', '/tmp/lib', 'cell', '/tmp/cell', 1, 0, 1, 1)";
            cmd.ExecuteNonQuery();
        }

        var run = new CharRunRecord
        {
            ModelName = "test_model",
            DeviceName = "lib:cell",
            Corner = "tt",
            Backend = "spectre",
            Timestamp = DateTime.UtcNow,
            W_M = 1e-6,
            L_M = 180e-9,
            Nf = 1,
            Vds = 0.9,
            Vsb = 0.0,
            TemperatureC = 27.0,
            Status = "complete",
            JobDir = "/tmp/char/job-device",
        };

        // Act
        var runId = CharLutWriter.WriteCharRun(dbPath, run);

        // Assert
        using var db2 = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd2 = db2.Connection.CreateCommand();
        cmd2.CommandText = "SELECT device_id FROM char_runs WHERE id=$id";
        var p = cmd2.CreateParameter();
        p.ParameterName = "$id";
        p.Value = runId;
        cmd2.Parameters.Add(p);
        var deviceId = cmd2.ExecuteScalar();
        Assert.NotNull(deviceId);

        var loaded = CharLutReader.LoadCharRun(dbPath, runId);
        Assert.NotNull(loaded);
        Assert.Equal("lib:cell", loaded!.DeviceName);
    }

    private void SetupMinimalDatabase(string dbPath)
    {
        // Create database with schema including char tables
        using var db = PdkDatabase.Open(dbPath);

        // Ensure char tables exist (they should be created by PdkDatabase.Open after we add them)
        // For now, add a test model so foreign keys work
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO models(name, model_type, device_class) VALUES ('test_model', 'nmos', 1)";
        cmd.ExecuteNonQuery();
    }
}
