using System;
using System.Linq;
using Cascode.TestSupport;
using Cascode.Workspace;
using Xunit;
using static Cascode.Workspace.Tests.TestUtilities;

namespace Cascode.Workspace.Tests;

public sealed class DefinitionContextAggregationTests
{
    [Fact]
    public void Scan_PreservesDefinitionContextsThroughAggregation()
    {
        using var cascodeHome = CascodeHome.CreateInTemp();
        using var workspace = TemporaryWorkspace.Create();

        workspace.WriteFile(
            ".cdsinit",
            "envSetVal(\"spectre.envOpts\" \"modelFiles\" `string \"./models/example.scs\")"
        );

        workspace.WriteFile(
            "models/example.scs",
            @"simulator lang=spectre
section tt_corner
.model demo_nf nmos
endsection
"
        );

        var scanner = new WorkspaceScanner();
        var result = scanner.Scan(workspace.RootPath);

        var model = result.Models.First(m =>
            m.Name.Equals("demo_nf", StringComparison.OrdinalIgnoreCase)
        );
        Assert.NotEmpty(model.DefinitionContexts);
        var ctx = model.DefinitionContexts[0];
        Assert.Equal("tt", ctx.Corner);
        Assert.Equal("tt_corner", ctx.Section);
        Assert.False(string.IsNullOrWhiteSpace(ctx.IncludePath));

        using var tempDb = TempPdkDatabase.Create();
        PdkDatabaseWriter.Write(tempDb.DatabasePath, result);
        var dbContexts = PdkDatabaseReader.GetAllContextsForModel(tempDb.DatabasePath, "demo_nf");
        Assert.Contains(
            dbContexts,
            c => string.Equals(c.Section, "tt_corner", StringComparison.OrdinalIgnoreCase)
        );
    }
}
