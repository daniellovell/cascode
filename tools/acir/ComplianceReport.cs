using System.Collections.Generic;
using System.Linq;
using Cascode.Bench;

namespace Cascode.ACIR;

/// <summary>
/// Report of constraint compliance evaluation.
/// </summary>
public sealed class ComplianceReport
{
    /// <summary>Results for each constraint checked.</summary>
    public List<ConstraintResult> Results { get; init; } = new();

    /// <summary>Number of constraints that passed.</summary>
    public int PassedCount => Results.Count(r => r.Passed);

    /// <summary>Number of constraints that failed.</summary>
    public int FailedCount => Results.Count(r => !r.Passed);

    /// <summary>Total number of constraints checked.</summary>
    public int TotalCount => Results.Count;
}

/// <summary>
/// Result of evaluating a single constraint.
/// </summary>
public sealed class ConstraintResult
{
    /// <summary>Constraint ID (e.g., "c_gbw").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Metric name.</summary>
    public string Metric { get; init; } = string.Empty;

    /// <summary>Optional node where constraint applies.</summary>
    public string? Node { get; init; }

    /// <summary>Physical unit for expected/actual values (e.g., "Hz", "dB", "deg").</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Comparison operator (>=, <=, ==, >, <).</summary>
    public string Operator { get; init; } = string.Empty;

    /// <summary>Original expected value string from the constraint (e.g., "100M").</summary>
    public string ExpectedRaw { get; init; } = string.Empty;

    /// <summary>Expected value (constraint bound).</summary>
    public double Expected { get; init; }

    /// <summary>Actual measured value.</summary>
    public double? Actual { get; init; }

    /// <summary>Unit reported by the measurement, if available.</summary>
    public string? ActualUnit { get; init; }

    /// <summary>Whether the constraint passed.</summary>
    public bool Passed { get; init; }

    /// <summary>Human-readable message describing the result.</summary>
    public string Message { get; init; } = string.Empty;
}
