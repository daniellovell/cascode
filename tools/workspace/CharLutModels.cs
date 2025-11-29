using System;
using System.Collections.Generic;

namespace Cascode.Workspace;

/// <summary>
/// Metadata for a single characterization run.
/// </summary>
public sealed class CharRunRecord
{
    public long Id { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string Corner { get; set; } = string.Empty;
    public string Backend { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double W_M { get; set; }
    public double L_M { get; set; }
    public int Nf { get; set; }
    public double Vds { get; set; }
    public double Vsb { get; set; }
    public double TemperatureC { get; set; }
    public string Status { get; set; } = string.Empty;
    public string JobDir { get; set; } = string.Empty;
}

/// <summary>
/// A single data point in the characterization LUT.
/// </summary>
public sealed class CharLutPoint
{
    public double Vgs { get; set; }
    public double? Id { get; set; }
    public double? Gm { get; set; }
    public double? Gds { get; set; }
    public double? GmOverId { get; set; }
    public double? Vth { get; set; }
    public double? Vdsat { get; set; }
    public double? Ro { get; set; }
    public double? GmRo { get; set; }
    public double? Ft { get; set; }
    public double? Cgs { get; set; }
    public double? Cgd { get; set; }
}

/// <summary>
/// Precomputed summary statistics for a characterization run.
/// </summary>
public sealed class CharRunSummary
{
    public long RunId { get; set; }
    public double? GmIdPeak { get; set; }
    public double? VgsAtPeakGmId { get; set; }
    public double? VthExtracted { get; set; }
    public double? IdAtVth { get; set; }
    public double? GmRoMax { get; set; }
    public double? FtMax { get; set; }
    public double? SaturationMargin { get; set; }
}

/// <summary>
/// Characterization coverage report showing which models have been characterized at which corners.
/// </summary>
public sealed class CharacterizationCoverage
{
    public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Corners { get; init; } = Array.Empty<string>();
    public int TotalRuns { get; init; }

    private readonly HashSet<string> _runSet;

    public CharacterizationCoverage(IReadOnlyList<string> models, IReadOnlyList<string> corners, int totalRuns, HashSet<string> runSet)
    {
        Models = models;
        Corners = corners;
        TotalRuns = totalRuns;
        _runSet = runSet;
    }

    public bool HasRun(string modelName, string corner)
        => _runSet.Contains($"{modelName}|{corner}");
}
