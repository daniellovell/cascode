using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Cascode.Workspace.Tests;

public sealed class PdkIncludeResolverTests
{
    [Fact]
    public void ResolveModelIncludes_UsesSectionedLibraryWhenPresent()
    {
        using var tempDb = TestUtilities.TempPdkDatabase.Create();
        var db = tempDb.Database;
        var dbPath = tempDb.DatabasePath;

        var includePath = Path.Combine(Path.GetDirectoryName(dbPath)!, "sky130.lib.spice");
        File.WriteAllText(includePath, ".lib tt\n.endl\n");

        using var tx = db.Connection.BeginTransaction();

        long modelId;
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO models(name, model_type, device_class) VALUES ('sky130_fd_pr__nfet_01v8','model',1); SELECT last_insert_rowid();";
            modelId = (long)(cmd.ExecuteScalar()!);
        }

        long includeId;
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO includes(path) VALUES ($p); SELECT last_insert_rowid();";
            cmd.Parameters.Add(new SqliteParameter("$p", includePath));
            includeId = (long)(cmd.ExecuteScalar()!);
        }

        long sectionId;
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO sections(name) VALUES ('tt'); SELECT last_insert_rowid();";
            sectionId = (long)(cmd.ExecuteScalar()!);
        }

        long cornerId;
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO corners(name) VALUES ('tt'); SELECT last_insert_rowid();";
            cornerId = (long)(cmd.ExecuteScalar()!);
        }

        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO model_contexts(model_id, corner_id, section_id, include_id)
                                VALUES ($m, $c, $s, $i)";
            cmd.Parameters.Add(new SqliteParameter("$m", modelId));
            cmd.Parameters.Add(new SqliteParameter("$c", cornerId));
            cmd.Parameters.Add(new SqliteParameter("$s", sectionId));
            cmd.Parameters.Add(new SqliteParameter("$i", includeId));
            cmd.ExecuteNonQuery();
        }

        tx.Commit();

        var model = PdkDatabaseReader.LoadModels(dbPath).First(m => m.Name == "sky130_fd_pr__nfet_01v8");
        var includes = PdkIncludeResolver.ResolveModelIncludes(dbPath, model, "tt");

        Assert.Contains(includePath, includes.IncludePaths);
        Assert.Contains(includePath, includes.IncludePathsWithSection);
        Assert.Equal("tt", includes.Section);
        Assert.Empty(includes.IncludePathsWithoutSection);
    }

    [Fact]
    public void ResolveModelIncludes_UsesCornerSuffixMatchingForSourceFiles()
    {
        using var tempDb = TestUtilities.TempPdkDatabase.Create();
        var dbPath = tempDb.DatabasePath;
        var directory = Path.GetDirectoryName(dbPath)!;

        var match = Path.Combine(directory, "model__tt.spice");
        var falseMatchSuffix = Path.Combine(directory, "model__tt_extra.spice");
        var falseMatchPrefix = Path.Combine(directory, "model__ttest.spice");
        File.WriteAllText(match, "*");
        File.WriteAllText(falseMatchSuffix, "*");
        File.WriteAllText(falseMatchPrefix, "*");

        var model = new SpectreModel
        {
            Name = "dummy",
            SourceFiles = new[] { match, falseMatchSuffix, falseMatchPrefix }
        };

        var includes = PdkIncludeResolver.ResolveModelIncludes(dbPath, model, "tt");

        Assert.Single(includes.IncludePaths, match);
        Assert.Empty(includes.IncludePathsWithSection);
        Assert.Single(includes.IncludePathsWithoutSection, match);
    }
}
