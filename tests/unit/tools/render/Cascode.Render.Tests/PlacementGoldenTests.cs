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
    [InlineData(
        "tests/golden/cas/lna/LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.el.cai",
        "tests/golden/render/LNA_CSCascodeInductivelyDegenerated_TwoStage.placement.csv"
    )]
    public void Placement_MatchesGolden(string cascodePath, string goldenPath)
    {
        // Arrange
        var repoRoot = GetRepoRoot();
        var fullCascodePath = Path.Combine(repoRoot, cascodePath);
        var fullGoldenPath = Path.Combine(repoRoot, goldenPath);

        using var reader = File.OpenText(fullCascodePath);
        var readResult = CascodeReader.TryRead(reader, fullCascodePath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);

        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);

        // Act
        var placement = CoarseGridPlacer.Place(topology, graph);

        // Assert
        var expectedPlacements = LoadGoldenPlacements(fullGoldenPath);
        AssertPlacementsMatch(
            expectedPlacements,
            placement.DevicePlacements,
            placement.SymmetryAxis
        );
    }

    [Fact]
    public void Placement_LnaStress_CascodeIsVerticallyStacked()
    {
        var repoRoot = GetRepoRoot();
        var fullCascodePath = Path.Combine(
            repoRoot,
            "tests/golden/cas/stress/LNA_CSCascodeInductivelyDegenerated_Sky130.cas"
        );

        using var reader = File.OpenText(fullCascodePath);
        var readResult = CascodeReader.TryRead(reader, fullCascodePath);
        Assert.True(readResult.Success, "Failed to parse Cascode file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == CascodeLevel.EL);
        var graph = CircuitGraph.Build(elCircuit);
        var topology = TopologyAnalyzer.Analyze(graph);
        var placement = CoarseGridPlacer.Place(topology, graph);

        Assert.True(placement.DevicePlacements.TryGetValue("M1", out var m1));
        Assert.True(placement.DevicePlacements.TryGetValue("M2", out var m2));

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
