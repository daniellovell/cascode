namespace Cascode.Render.Tests;

using Cascode.Language;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;

public class PlacementGoldenTests
{
    [Theory]
    [InlineData(
        "tests/golden/cas/ota/OTA5TSingleEnded.el.cai",
        "tests/golden/render/OTA5TSingleEnded.placement.csv"
    )]
    [InlineData(
        "tests/golden/cas/ota/OTA5TFullyDiff.el.cai",
        "tests/golden/render/OTA5TFullyDiff.placement.csv"
    )]
    [InlineData(
        "tests/golden/cas/lna/LNA_CSCascodeInductivelyDegenerated_Sky130.el.cai",
        "tests/golden/render/LNA_CSCascodeInductivelyDegenerated.placement.csv"
    )]
    public void Placement_MatchesGolden(string cascodePath, string goldenPath)
    {
        var repoRoot = GetRepoRoot();
        var fullGoldenPath = Path.Combine(repoRoot, goldenPath);
        var placement = LoadPlacement(cascodePath);

        var expectedPlacements = LoadGoldenPlacements(fullGoldenPath);
        AssertPlacementsMatch(
            expectedPlacements,
            placement.DevicePlacements,
            placement.SymmetryAxis
        );
    }

    [Fact]
    public void Placement_LnaTwoStage_PreservesStageBackboneRelationships()
    {
        var placement = LoadPlacement(
            "tests/golden/cas/lna/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.el.cai"
        );
        var cells = placement.DevicePlacements;

        var m1 = GetRequiredCell(cells, "M1");
        var m2 = GetRequiredCell(cells, "M2");
        var m3 = GetRequiredCell(cells, "M3");
        var rg2 = GetRequiredCell(cells, "RG2");
        var cint = GetRequiredCell(cells, "CINT");
        var ls1 = GetRequiredCell(cells, "LS1");
        var ld1 = GetRequiredCell(cells, "LD1");
        var ld2 = GetRequiredCell(cells, "LD2");
        var rcas1Top = GetRequiredCell(cells, "RCAS1_TOP");
        var rcas1Bot = GetRequiredCell(cells, "RCAS1_BOT");
        var rgb2Top = GetRequiredCell(cells, "RGB2_TOP");
        var rgb2Bot = GetRequiredCell(cells, "RGB2_BOT");

        Assert.Equal(m1.Column, m2.Column);
        Assert.True(
            Math.Abs(m1.Row - m2.Row) <= 2,
            $"Expected M1 and M2 to stay vertically clustered, got rows {m1.Row} and {m2.Row}."
        );
        Assert.True(
            Math.Abs(m3.Row - rg2.Row) <= 1,
            $"Expected M3 and RG2 to remain vertically adjacent, got rows {m3.Row} and {rg2.Row}."
        );
        Assert.True(
            Math.Abs(rg2.Column - m3.Column) <= 1,
            $"Expected RG2 to remain adjacent to M3, got columns {m3.Column} and {rg2.Column}."
        );
        Assert.True(
            cint.Column >= rg2.Column && Math.Abs(cint.Row - rg2.Row) <= 1,
            $"Expected CINT to stay adjacent to RG2, got RG2=({rg2.Row}, {rg2.Column}) and CINT=({cint.Row}, {cint.Column})."
        );
        Assert.True(
            Math.Abs(rcas1Bot.Column - rcas1Top.Column) <= 4
                && Math.Abs(rcas1Bot.Row - rcas1Top.Row) <= 2,
            $"Expected RCAS1 devices to remain proximal, got top=({rcas1Top.Row}, {rcas1Top.Column}) and bottom=({rcas1Bot.Row}, {rcas1Bot.Column})."
        );
        Assert.True(
            Math.Abs(rgb2Bot.Column - rgb2Top.Column) <= 2
                && Math.Abs(rgb2Bot.Row - rgb2Top.Row) <= 2,
            $"Expected RGB2 devices to remain proximal, got top=({rgb2Top.Row}, {rgb2Top.Column}) and bottom=({rgb2Bot.Row}, {rgb2Bot.Column})."
        );
        Assert.True(
            rgb2Top.Row <= rgb2Bot.Row,
            $"Expected RGB2_TOP to remain above RGB2_BOT, got rows {rgb2Top.Row} and {rgb2Bot.Row}."
        );
        Assert.True(
            ls1.Column <= m1.Column && Math.Abs(ls1.Row - m1.Row) <= 1,
            $"Expected LS1 to remain adjacent to M1, got LS1=({ls1.Row}, {ls1.Column}) and M1=({m1.Row}, {m1.Column})."
        );
        Assert.True(
            Math.Max(ld1.Row, ld2.Row) <= 2,
            $"Expected LD1/LD2 to stay in the top rows, got rows {ld1.Row} and {ld2.Row}."
        );
    }

    [Fact]
    public void Placement_LnaStress_CascodeIsVerticallyStacked()
    {
        var placement = LoadPlacement(
            "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_Sky130.cas"
        );

        var m1 = GetRequiredCell(placement.DevicePlacements, "M1");
        var m2 = GetRequiredCell(placement.DevicePlacements, "M2");

        var manhattan = Math.Abs(m2.Column - m1.Column) + Math.Abs(m2.Row - m1.Row);
        Assert.True(
            manhattan <= 3,
            $"Expected M2 and M1 to stay proximal, got distance {manhattan}"
        );
    }

    private static Dictionary<
        string,
        (int Row, int Column, int Rotation, bool MirrorX, bool MirrorY)
    > LoadGoldenPlacements(string path)
    {
        var result = new Dictionary<string, (int, int, int, bool, bool)>();
        var errors = new List<string>();
        var lines = File.ReadAllLines(path);

        // Skip header
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < 4)
            {
                var malformedDeviceId = parts.Length > 0 ? parts[0].Trim() : "<missing>";
                errors.Add($"Malformed placement line for device '{malformedDeviceId}': {line}");
                continue;
            }

            var deviceId = parts[0].Trim();
            var rowParsed = int.TryParse(parts[1].Trim(), out var row);
            var colParsed = int.TryParse(parts[2].Trim(), out var col);
            var rotation = 0;
            var mirrorX = false;
            var rotationParsed = true;
            var mirrorXParsed = true;
            var mirrorY = false;
            var mirrorYParsed = true;
            if (parts.Length >= 5)
            {
                rotationParsed = int.TryParse(parts[3].Trim(), out rotation);
                mirrorXParsed = bool.TryParse(parts[4].Trim(), out mirrorX);
                mirrorYParsed =
                    parts.Length >= 6 ? bool.TryParse(parts[5].Trim(), out mirrorY) : true;
            }
            else
            {
                mirrorXParsed = bool.TryParse(parts[3].Trim(), out mirrorX);
            }
            if (!rowParsed || !colParsed || !rotationParsed || !mirrorXParsed || !mirrorYParsed)
            {
                errors.Add($"Malformed placement line for device '{deviceId}': {line}");
                continue;
            }

            result[deviceId] = (row, col, rotation, mirrorX, parts.Length >= 6 && mirrorY);
        }

        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"Failed to parse placement CSV at '{path}':\n{string.Join("\n", errors)}"
            );
        }

        return result;
    }

    private static void AssertPlacementsMatch(
        Dictionary<
            string,
            (int Row, int Column, int Rotation, bool MirrorX, bool MirrorY)
        > expected,
        IReadOnlyDictionary<string, GridCell> actual,
        int symmetryAxis
    )
    {
        var errors = new List<string>();

        // Check all expected devices are present
        foreach (var (deviceId, (expectedRow, expectedCol, _, _, _)) in expected)
        {
            if (!actual.TryGetValue(deviceId, out var actualCell))
            {
                errors.Add($"Device '{deviceId}' not found in placement");
                continue;
            }

            if (actualCell.Row != expectedRow)
            {
                errors.Add(
                    $"Device '{deviceId}' row mismatch: expected {expectedRow}, got {actualCell.Row}"
                );
            }

            if (actualCell.Column != expectedCol)
            {
                errors.Add(
                    $"Device '{deviceId}' column mismatch: expected {expectedCol}, got {actualCell.Column}"
                );
            }
        }

        // Check for unexpected devices
        foreach (var (deviceId, cell) in actual)
        {
            if (!expected.ContainsKey(deviceId))
            {
                errors.Add(
                    $"Unexpected device '{deviceId}' at ({cell.Row}, {cell.Column}, rot={cell.Rotation}, mirrorX={cell.MirrorX}, mirrorY={cell.MirrorY})"
                );
            }
        }

        if (errors.Count > 0)
        {
            var actualSummary = string.Join(
                "\n",
                actual
                    .OrderBy(kv => kv.Value.Row)
                    .ThenBy(kv => kv.Value.Column)
                    .Select(kv =>
                        $"  {kv.Key}: row={kv.Value.Row}, col={kv.Value.Column}, rot={kv.Value.Rotation}, mirrorX={kv.Value.MirrorX}, mirrorY={kv.Value.MirrorY}"
                    )
            );

            Assert.Fail(
                $"Placement mismatches (symmetryAxis={symmetryAxis}):\n"
                    + string.Join("\n", errors)
                    + $"\n\nActual placements:\n{actualSummary}"
            );
        }
    }

    private static CoarseGridResult LoadPlacement(string cascodePath)
    {
        var repoRoot = GetRepoRoot();
        var fullCascodePath = Path.Combine(repoRoot, cascodePath);

        using var reader = File.OpenText(fullCascodePath);
        var readResult = CascodeReader.TryRead(reader, fullCascodePath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);
        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        return CoarseGridPlacer.Place(topology, graph);
    }

    private static GridCell GetRequiredCell(
        IReadOnlyDictionary<string, GridCell> placements,
        string deviceId
    )
    {
        Assert.True(
            placements.TryGetValue(deviceId, out var cell),
            $"Missing placement for '{deviceId}'."
        );
        return cell;
    }

    private static string GetRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null && !File.Exists(Path.Combine(dir, "Cascode.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? throw new InvalidOperationException("Could not find repo root");
    }
}
