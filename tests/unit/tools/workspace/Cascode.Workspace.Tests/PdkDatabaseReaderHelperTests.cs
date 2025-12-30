using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Cascode.Workspace.Tests;

public sealed class PdkDatabaseReaderHelperTests
{
    [Fact]
    public void LoadDevicesFiltered_FiltersAndPaginates()
    {
        using var tempDb = TestUtilities.TempPdkDatabase.Create();
        var db = tempDb.Database;
        var dbPath = tempDb.DatabasePath;

        using var tx = db.Connection.BeginTransaction();
        // Insert devices
        void AddDevice(long id, string lib, string cell, int cls)
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                @"INSERT INTO devices(id, canonical_name, display_name, lib_name, lib_path, cell_name, cell_path, device_class, device_subclass, has_layout, has_symbol)
                                VALUES ($id, $key, $disp, $lib, $libp, $cell, $cellp, $cls, 0, 1, 1)";
            cmd.Parameters.Add(new SqliteParameter("$id", id));
            cmd.Parameters.Add(new SqliteParameter("$key", $"{lib}__{cell}"));
            cmd.Parameters.Add(new SqliteParameter("$disp", cell));
            cmd.Parameters.Add(new SqliteParameter("$lib", lib));
            cmd.Parameters.Add(new SqliteParameter("$libp", $"/{lib}"));
            cmd.Parameters.Add(new SqliteParameter("$cell", cell));
            cmd.Parameters.Add(new SqliteParameter("$cellp", $"/{lib}/{cell}"));
            cmd.Parameters.Add(new SqliteParameter("$cls", cls));
            cmd.ExecuteNonQuery();
        }
        AddDevice(1, "libA", "nmos_x1", (int)DeviceClass.Nmos);
        AddDevice(2, "libA", "nmos_x2", (int)DeviceClass.Nmos);
        AddDevice(3, "libB", "pmos_x1", (int)DeviceClass.Pmos);

        // Views
        void AddView(long did, string view)
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO device_views(device_id, view) VALUES ($id, $v)";
            cmd.Parameters.Add(new SqliteParameter("$id", did));
            cmd.Parameters.Add(new SqliteParameter("$v", view));
            cmd.ExecuteNonQuery();
        }
        AddView(1, "layout");
        AddView(1, "symbol");
        AddView(2, "layout");
        AddView(2, "symbol");
        AddView(3, "layout");

        tx.Commit();

        // Filter: NMOS only, page size 2
        var page = Cascode.Workspace.PdkDatabaseReader.LoadDevicesFiltered(
            dbPath,
            DeviceClass.Nmos,
            limit: 2,
            offset: 0
        );
        Assert.Equal(2, page.Count);
        Assert.All(page, d => Assert.Equal(DeviceClass.Nmos, d.Class));
        Assert.All(page, d => Assert.Contains("layout", d.Views));
    }

    [Fact]
    public void GetPreferredIncludesForModel_PrefersSpectre()
    {
        using var tempDb = TestUtilities.TempPdkDatabase.Create();
        var db = tempDb.Database;
        var dbPath = tempDb.DatabasePath;

        using var tx = db.Connection.BeginTransaction();
        // model
        long mid;
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO models(name, model_type, device_class) VALUES ('m1','model',1); SELECT last_insert_rowid();";
            mid = (long)(cmd.ExecuteScalar()!);
        }
        // decks
        void AddDeck(string path)
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO model_decks(model_id, path) VALUES ($id, $p)";
            cmd.Parameters.Add(new SqliteParameter("$id", mid));
            cmd.Parameters.Add(new SqliteParameter("$p", path));
            cmd.ExecuteNonQuery();
        }
        AddDeck("/pdk/models/hspice/m1.sp");
        AddDeck("/pdk/models/spectre/toplevel.scs");
        tx.Commit();

        var prefs = Cascode.Workspace.PdkDatabaseReader.GetPreferredIncludesForModel(dbPath, "m1");
        Assert.NotEmpty(prefs);
        Assert.Contains("spectre", prefs[0], StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".scs", prefs[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetContextsForModelAndCorner_UsesSectionsAndPreferredInclude()
    {
        using var tempDb = TestUtilities.TempPdkDatabase.Create();
        var db = tempDb.Database;
        var dbPath = tempDb.DatabasePath;
        using var tx = db.Connection.BeginTransaction();
        // model
        long mid;
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO models(name, model_type, device_class) VALUES ('m2','subckt',1); SELECT last_insert_rowid();";
            mid = (long)(cmd.ExecuteScalar()!);
        }
        // include deck first (so contexts can reference it)
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO model_decks(model_id, path) VALUES ($id, $p)";
            cmd.Parameters.Add(new SqliteParameter("$id", mid));
            cmd.Parameters.Add(new SqliteParameter("$p", "/pdk/models/spectre/toplevel.scs"));
            cmd.ExecuteNonQuery();
        }
        long? includeId;
        using (var ins = db.Connection.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText =
                "INSERT INTO includes(path) VALUES ($p) ON CONFLICT(path) DO NOTHING;";
            ins.Parameters.Add(new SqliteParameter("$p", "/pdk/models/spectre/toplevel.scs"));
            ins.ExecuteNonQuery();
        }
        using (var sel = db.Connection.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT id FROM includes WHERE path=$p";
            sel.Parameters.Add(new SqliteParameter("$p", "/pdk/models/spectre/toplevel.scs"));
            includeId = (long?)sel.ExecuteScalar();
        }

        // corners: two sections for 'tt'
        void AddCorner(string? corner, string? section)
        {
            long? cornerId = null,
                sectionId = null;
            if (!string.IsNullOrWhiteSpace(corner))
            {
                using (var ins = db.Connection.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText =
                        "INSERT INTO corners(name) VALUES ($n) ON CONFLICT(name) DO NOTHING;";
                    ins.Parameters.Add(new SqliteParameter("$n", corner));
                    ins.ExecuteNonQuery();
                }
                using (var sel = db.Connection.CreateCommand())
                {
                    sel.Transaction = tx;
                    sel.CommandText = "SELECT id FROM corners WHERE name=$n";
                    sel.Parameters.Add(new SqliteParameter("$n", corner));
                    cornerId = (long?)sel.ExecuteScalar();
                }
            }
            if (!string.IsNullOrWhiteSpace(section))
            {
                using (var ins = db.Connection.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText =
                        "INSERT INTO sections(name) VALUES ($n) ON CONFLICT(name) DO NOTHING;";
                    ins.Parameters.Add(new SqliteParameter("$n", section));
                    ins.ExecuteNonQuery();
                }
                using (var sel = db.Connection.CreateCommand())
                {
                    sel.Transaction = tx;
                    sel.CommandText = "SELECT id FROM sections WHERE name=$n";
                    sel.Parameters.Add(new SqliteParameter("$n", section));
                    sectionId = (long?)sel.ExecuteScalar();
                }
            }
            using (var insCtx = db.Connection.CreateCommand())
            {
                insCtx.Transaction = tx;
                insCtx.CommandText =
                    "INSERT INTO model_contexts(model_id, corner_id, detail_id, section_id, include_id) VALUES ($m, $c, NULL, $s, $i)";
                insCtx.Parameters.Add(new SqliteParameter("$m", mid));
                insCtx.Parameters.Add(new SqliteParameter("$c", (object?)cornerId ?? DBNull.Value));
                insCtx.Parameters.Add(
                    new SqliteParameter("$s", (object?)sectionId ?? DBNull.Value)
                );
                insCtx.Parameters.Add(
                    new SqliteParameter("$i", (object?)includeId ?? DBNull.Value)
                );
                insCtx.ExecuteNonQuery();
            }
        }
        AddCorner("tt", "tt_core");
        AddCorner("tt", "tt_io");
        tx.Commit();

        var ctx = Cascode.Workspace.PdkDatabaseReader.GetContextsForModelAndCorner(
            dbPath,
            "m2",
            "tt"
        );
        Assert.Equal(2, ctx.Count);
        Assert.All(
            ctx,
            c => Assert.Contains("/spectre/", c.IncludePath, StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            ctx,
            c => string.Equals(c.Section, "tt_core", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            ctx,
            c => string.Equals(c.Section, "tt_io", StringComparison.OrdinalIgnoreCase)
        );
    }
}
