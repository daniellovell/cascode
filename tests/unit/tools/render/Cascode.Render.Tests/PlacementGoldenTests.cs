namespace Cascode.Render.Tests;

using Cascode.ACIR;
using Cascode.Render.Analysis;
using Cascode.Render.Placement;

public class PlacementGoldenTests
{
    [Theory]
    [InlineData(
        "tests/golden/acir/ota/OTA5TSingleEnded.el.cir",
        "tests/golden/render/OTA5TSingleEnded.placement.csv"
    )]
    [InlineData(
        "tests/golden/acir/ota/OTA5TFullyDiff.el.cir",
        "tests/golden/render/OTA5TFullyDiff.placement.csv"
    )]
    public void Placement_MatchesGolden(string acirPath, string goldenPath)
    {
        // Arrange
        var repoRoot = GetRepoRoot();
        var fullAcirPath = Path.Combine(repoRoot, acirPath);
        var fullGoldenPath = Path.Combine(repoRoot, goldenPath);

        using var reader = File.OpenText(fullAcirPath);
        var readResult = ACIRReader.TryRead(reader, fullAcirPath);
        Assert.True(readResult.Success, "Failed to parse ACIR file");

        var doc = readResult.Document!;
        var elCircuit = doc.Circuits.First(c => c.Level == ACIRLevel.EL);

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

    private static Dictionary<string, (int Row, int Column, bool MirrorX)> LoadGoldenPlacements(
        string path
    )
    {
        var result = new Dictionary<string, (int, int, bool)>();
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
                errors.Add(
                    $"Malformed placement line for device '{malformedDeviceId}': {line}"
                );
                continue;
            }

            var deviceId = parts[0].Trim();
            var rowParsed = int.TryParse(parts[1].Trim(), out var row);
            var colParsed = int.TryParse(parts[2].Trim(), out var col);
            var mirrorXParsed = bool.TryParse(parts[3].Trim(), out var mirrorX);
            if (!rowParsed || !colParsed || !mirrorXParsed)
            {
                errors.Add(
                    $"Malformed placement line for device '{deviceId}': {line}"
                );
                continue;
            }

            result[deviceId] = (row, col, mirrorX);
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
        Dictionary<string, (int Row, int Column, bool MirrorX)> expected,
        IReadOnlyDictionary<string, GridCell> actual,
        int symmetryAxis
    )
    {
        var errors = new List<string>();

        // Check all expected devices are present
        foreach (var (deviceId, (expectedRow, expectedCol, expectedMirrorX)) in expected)
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

            if (actualCell.MirrorX != expectedMirrorX)
            {
                errors.Add(
                    $"Device '{deviceId}' mirrorX mismatch: expected {expectedMirrorX}, got {actualCell.MirrorX}"
                );
            }
        }

        // Check for unexpected devices
        foreach (var (deviceId, cell) in actual)
        {
            if (!expected.ContainsKey(deviceId))
            {
                errors.Add(
                    $"Unexpected device '{deviceId}' at ({cell.Row}, {cell.Column}, mirrorX={cell.MirrorX})"
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
                        $"  {kv.Key}: row={kv.Value.Row}, col={kv.Value.Column}, mirrorX={kv.Value.MirrorX}"
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
