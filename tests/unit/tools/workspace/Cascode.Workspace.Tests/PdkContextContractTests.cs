using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Cascode.Workspace.Tests;

public sealed class PdkContextContractTests
{
    private static void WriteAll(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Db_ShouldNotContain_UnobservedCornerDetailPairs()
    {
        // Deck layout:
        // top.scs defines two libs with distinct (corner, detail):
        //   tt_lib noise_best -> include core.scs section=tt_core
        //   ff_lib noise_worst -> include core.scs section=ff_core
        // core.scs defines .model m1 inside each section. There is NO case where (tt, noise_worst) co-occurs.
        using var tempDir = TestUtilities.TempDirectory.Create("cascode-ctx-tests");
        var top = Path.Combine(tempDir.DirectoryPath, "top.scs");
        var core = Path.Combine(tempDir.DirectoryPath, "core.scs");

        WriteAll(top, @"
.lib tt_lib noise_best
include ""core.scs"" section=tt_core
.endl
.lib ff_lib noise_worst
include ""core.scs"" section=ff_core
.endl
");

        WriteAll(core, @"
section tt_core
.model m1 nmos
endsection
section ff_core
.model m1 nmos
endsection
");

        // Extract models
        var warnings = new List<string>();
        var extractor = new Cascode.Workspace.SpectreModelExtractor();
        var models = extractor.Extract(tempDir.DirectoryPath, top, warnings);

        // Write DB using current writer
        var scan = new Cascode.Workspace.WorkspaceScanResult(tempDir.DirectoryPath, Array.Empty<Cascode.Workspace.WorkspaceLibrary>(), Array.Empty<Cascode.Workspace.ModelDeckRecord>(), models, warnings);
        var dbDir = Path.Combine(tempDir.DirectoryPath, ".db");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "pdk.db");
        Cascode.Workspace.PdkDatabaseWriter.Write(dbPath, scan, devices: null);

        // Fetch model id and check that (tt, noise_worst) does NOT exist for m1.
        using var db = Cascode.Workspace.PdkDatabase.OpenReadOnly(dbPath);
        long mid;
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM models WHERE name='m1'";
            mid = (long)(cmd.ExecuteScalar()!);
        }

        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT COUNT(*)
                                FROM model_contexts mc
                                LEFT JOIN corners c ON c.id=mc.corner_id
                                LEFT JOIN sections s ON s.id=mc.section_id
                                WHERE mc.model_id=$id AND c.name='tt' AND s.name='ff_core'";
            cmd.Parameters.Add(new SqliteParameter("$id", mid));
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(0, (int)count);
        }
    }
}
