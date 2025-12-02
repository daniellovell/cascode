using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.Workspace;
using Xunit;

namespace Cascode.Workspace.Tests;

public sealed class DeviceCharPlannerTests : IDisposable
{
    private readonly string _tempDir;

    public DeviceCharPlannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cascode_device_plan_{Guid.NewGuid():N}");
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
    public void Plan_UsesDeviceGeometryAndFiltersDevices()
    {
        var dbPath = Path.Combine(_tempDir, "plan.db");
        var includePath = Path.Combine(_tempDir, "model.scs");
        // Include a .lib directive so FileHasLibrarySections returns true
        File.WriteAllText(includePath, "simulator lang=spectre\n.lib ttt\n.model nmos_model nmos\n.endl ttt");

        var models = new List<SpectreModel>
        {
            new("nmos_model", "subckt", DeviceClass.Nmos, "1.8V", "LVT", SpectreModel.EmptyStringList, SpectreModel.EmptyStringList, SpectreModel.EmptyStringList, new[] { includePath }, SpectreModel.EmptyStringList)
            {
                DefinitionContexts = new[] { new ModelContext { Corner = "tt", Section = "ttt", IncludePath = includePath } }
            },
            new("pmos_model", "subckt", DeviceClass.Pmos, "1.8V", "HVT", SpectreModel.EmptyStringList, SpectreModel.EmptyStringList, SpectreModel.EmptyStringList, new[] { includePath }, SpectreModel.EmptyStringList)
        };

        var scan = new WorkspaceScanResult(
            _tempDir,
            new[] { new WorkspaceLibrary("lib", _tempDir) },
            Array.Empty<ModelDeckRecord>(),
            models,
            Array.Empty<string>());

        var devices = new List<Device>
        {
            new()
            {
                LibraryName = "lib",
                LibraryPath = _tempDir,
                CellName = "dev_n1",
                CellPath = Path.Combine(_tempDir, "dev_n1"),
                Class = DeviceClass.Nmos,
                Subclass = DeviceSubclass.Unknown,
                HasLayout = true,
                HasSymbol = true,
                Views = new[] { "layout", "symbol" },
                VtTags = new[] { "LVT" },
                VddTags = new[] { "01v8" },
                Tags = Array.Empty<string>()
            },
            new()
            {
                LibraryName = "lib",
                LibraryPath = _tempDir,
                CellName = "dev_p1",
                CellPath = Path.Combine(_tempDir, "dev_p1"),
                Class = DeviceClass.Pmos,
                Subclass = DeviceSubclass.Unknown,
                HasLayout = true,
                HasSymbol = true,
                Views = new[] { "layout", "symbol" },
                VtTags = new[] { "HVT" },
                VddTags = new[] { "01v8" },
                Tags = Array.Empty<string>()
            }
        };

        PdkDatabaseWriter.Write(dbPath, scan, devices);

        var matches = new List<DeviceModelMatchRecord>
        {
            new() { DeviceCanonicalName = devices[0].CanonicalName, ModelName = "nmos_model", Quality = "exact", Rank = 1 },
            new() { DeviceCanonicalName = devices[1].CanonicalName, ModelName = "pmos_model", Quality = "exact", Rank = 1 }
        };
        PdkDatabaseWriter.UpsertMatches(dbPath, matches);

        var modelGeom = new List<ModelGeometry>
        {
            new() { ModelName = "nmos_model", WDefault = 2e-6, LDefault = 0.2e-6, NfDefault = 2, WMin = 1e-6, LMin = 0.1e-6, WMax = 10e-6, LMax = 1e-6, Source = "model" }
        };
        PdkDatabaseWriter.UpsertGeometry(dbPath, modelGeom);
        PdkDatabaseWriter.UpsertDeviceGeometry(dbPath, devices, matches, modelGeom);

        // Override device geometry to prove the planner reads device-level values.
        using (var db = PdkDatabase.Open(dbPath))
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE device_geometry SET w_default=5e-06, l_default=3e-07, nf_default=4 WHERE device_id=(SELECT id FROM devices WHERE canonical_name=$n)";
            var p = cmd.CreateParameter(); p.ParameterName = "$n"; p.Value = devices[0].CanonicalName; cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
        }

        var filters = new DeviceFilterOptions(classes: new[] { "nmos" }, vts: new[] { "LVT" }, vdds: new[] { "1.8V" }, infra: null, matched: null);
        var plans = DeviceCharPlanner.Plan(dbPath, DeviceCharPlannerOptions.Create("spectre", "tt", 0, filters));

        Assert.Single(plans);
        var plan = plans[0];
        Assert.Equal(devices[0].CanonicalName, plan.DeviceName);
        Assert.Equal(5e-6, plan.Width, 12);
        Assert.Equal(3e-7, plan.Length, 12);
        Assert.Equal(4, plan.Nf);
        Assert.Equal("ttt", plan.Section);
        Assert.Contains(includePath, plan.IncludePathsWithSection, StringComparer.OrdinalIgnoreCase);
        Assert.True(plan.IsSubckt);
        Assert.True(plan.VgsStop >= 1.2);
        Assert.True(plan.Vds > 0.1);
    }

    [Fact]
    public void Plan_DetectsLibraryKeyword()
    {
        var dbPath = Path.Combine(_tempDir, "plan_lib.db");
        var includePath = Path.Combine(_tempDir, "model_lib.scs");
        File.WriteAllText(includePath, "simulator lang=spectre\nlibrary ttt\n.model nmos_model nmos\nendlibrary ttt");

        SetupAndVerifyPlan(dbPath, includePath, expectSection: true);
    }

    [Fact]
    public void Plan_IgnoresLibraryPrefix()
    {
        var dbPath = Path.Combine(_tempDir, "plan_prefix.db");
        var includePath = Path.Combine(_tempDir, "model_prefix.scs");
        // "library_foo" should not be detected as "library" keyword
        File.WriteAllText(includePath, "simulator lang=spectre\nlibrary_foo ttt\n.model nmos_model nmos");

        SetupAndVerifyPlan(dbPath, includePath, expectSection: false);
    }

    private void SetupAndVerifyPlan(string dbPath, string includePath, bool expectSection)
    {
        var models = new List<SpectreModel>
        {
            new("nmos_model", "subckt", DeviceClass.Nmos, "1.8V", "LVT", SpectreModel.EmptyStringList, SpectreModel.EmptyStringList, SpectreModel.EmptyStringList, new[] { includePath }, SpectreModel.EmptyStringList)
            {
                DefinitionContexts = new[] { new ModelContext { Corner = "tt", Section = "ttt", IncludePath = includePath } }
            }
        };

        var scan = new WorkspaceScanResult(
            _tempDir,
            new[] { new WorkspaceLibrary("lib", _tempDir) },
            Array.Empty<ModelDeckRecord>(),
            models,
            Array.Empty<string>());

        var devices = new List<Device>
        {
            new()
            {
                LibraryName = "lib",
                LibraryPath = _tempDir,
                CellName = "dev_n1",
                CellPath = Path.Combine(_tempDir, "dev_n1"),
                Class = DeviceClass.Nmos,
                Subclass = DeviceSubclass.Unknown,
                HasLayout = true,
                HasSymbol = true,
                Views = new[] { "layout", "symbol" },
                VtTags = new[] { "LVT" },
                VddTags = new[] { "01v8" },
                Tags = Array.Empty<string>()
            }
        };

        PdkDatabaseWriter.Write(dbPath, scan, devices);

        var matches = new List<DeviceModelMatchRecord>
        {
            new() { DeviceCanonicalName = devices[0].CanonicalName, ModelName = "nmos_model", Quality = "exact", Rank = 1 }
        };
        PdkDatabaseWriter.UpsertMatches(dbPath, matches);

        var modelGeom = new List<ModelGeometry>
        {
            new() { ModelName = "nmos_model", WDefault = 1e-6, LDefault = 1e-6, NfDefault = 1, WMin = 1e-6, LMin = 1e-6, WMax = 1e-6, LMax = 1e-6, Source = "model" }
        };
        PdkDatabaseWriter.UpsertGeometry(dbPath, modelGeom);
        PdkDatabaseWriter.UpsertDeviceGeometry(dbPath, devices, matches, modelGeom);

        var filters = new DeviceFilterOptions(classes: new[] { "nmos" }, vts: null, vdds: null, infra: null, matched: null);
        var plans = DeviceCharPlanner.Plan(dbPath, DeviceCharPlannerOptions.Create("spectre", "tt", 0, filters));

        Assert.Single(plans);
        var plan = plans[0];
        if (expectSection)
        {
            Assert.Equal("ttt", plan.Section);
            Assert.Contains(includePath, plan.IncludePathsWithSection, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Null(plan.Section);
            Assert.Contains(includePath, plan.IncludePathsWithoutSection, StringComparer.OrdinalIgnoreCase);
        }
    }
}
