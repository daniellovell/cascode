using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cascode.Bench;

namespace Cascode.Language;

public enum ConstraintEvaluationMode
{
    BenchScoped,
    AllDeclared,
}

/// <summary>
/// Checks circuit constraints against bench measurement results.
/// </summary>
public static class ComplianceChecker
{
    /// <summary>
    /// Checks numeric constraints from a circuit against measurement results.
    /// Only evaluates constraints whose metrics are measured by the bench that produced the results.
    /// </summary>
    /// <param name="circuit">Circuit containing constraints.</param>
    /// <param name="results">Bench measurement results.</param>
    /// <returns>Compliance report with pass/fail status for each constraint.</returns>
    public static ComplianceReport Check(Circuit circuit, BenchResult results) =>
        Check(circuit, results, ConstraintEvaluationMode.BenchScoped);

    /// <summary>
    /// Checks numeric constraints from a circuit against measurement results.
    /// </summary>
    /// <param name="circuit">Circuit containing constraints.</param>
    /// <param name="results">Bench measurement results.</param>
    /// <param name="mode">Constraint evaluation mode.</param>
    /// <returns>Compliance report with pass/fail status for each constraint.</returns>
    public static ComplianceReport Check(
        Circuit circuit,
        BenchResult results,
        ConstraintEvaluationMode mode
    )
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(results);

        var report = new ComplianceReport();

        if (circuit.Constraints?.Numeric == null || circuit.Constraints.Numeric.Count == 0)
        {
            return report;
        }

        // "all" indicates combined results from multiple benches.
        var isCombinedResults = string.Equals(
            results.Bench,
            "all",
            StringComparison.OrdinalIgnoreCase
        );

        foreach (var constraint in circuit.Constraints.Numeric)
        {
            var benchForConstraint = constraint.Bench;

            // Bench-scoped mode skips constraints measured by other benches.
            if (
                mode == ConstraintEvaluationMode.BenchScoped
                && !isCombinedResults
                && !string.IsNullOrWhiteSpace(benchForConstraint)
                && !string.Equals(
                    benchForConstraint,
                    results.Bench,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                AddUncheckedConstraint(report, benchForConstraint, constraint);
                continue;
            }

            // Evaluate constraint against results
            var result = EvaluateConstraint(constraint, results);
            report.Results.Add(result);
        }

        return report;
    }

    private static void AddUncheckedConstraint(
        ComplianceReport report,
        string benchForConstraint,
        NumericConstraint constraint
    )
    {
        if (!report.UncheckedByBench.TryGetValue(benchForConstraint, out var uncheckedList))
        {
            uncheckedList = new List<UncheckedConstraint>();
            report.UncheckedByBench[benchForConstraint] = uncheckedList;
        }

        uncheckedList.Add(
            new UncheckedConstraint { Id = constraint.Id, Metric = FormatMetricKey(constraint) }
        );
    }

    private static ConstraintResult EvaluateConstraint(
        NumericConstraint constraint,
        BenchResult results
    )
    {
        var metricKey = FormatMetricKey(constraint);

        // Find matching measurement by metric and optional node
        var matchingMeasurement = FindMatchingMeasurement(metricKey, constraint, results);

        if (matchingMeasurement == null)
        {
            return new ConstraintResult
            {
                Id = constraint.Id,
                Metric = metricKey,
                Node = constraint.Node?.ToString(),
                Unit = constraint.Unit,
                Operator = constraint.Op,
                ExpectedRaw = constraint.Value,
                Expected = ParseValue(constraint.Value),
                Actual = null,
                ActualUnit = null,
                Passed = false,
                FailureReason = ConstraintResult.NoMeasurement,
                Message =
                    $"No measurement found for {metricKey}"
                    + (constraint.Node != null ? $" @ {constraint.Node}" : ""),
            };
        }

        var expected = ParseValue(constraint.Value);
        var measurement = matchingMeasurement.Value.Value;
        if (!string.IsNullOrWhiteSpace(measurement.Error))
        {
            return new ConstraintResult
            {
                Id = constraint.Id,
                Metric = metricKey,
                Node = constraint.Node?.ToString(),
                Unit = constraint.Unit,
                Operator = constraint.Op,
                ExpectedRaw = constraint.Value,
                Expected = expected,
                Actual = null,
                ActualUnit = null,
                Passed = false,
                FailureReason = ConstraintResult.BenchError,
                Message = $"Measurement error: {measurement.Error}",
            };
        }

        if (measurement.Values is not null)
        {
            return EvaluateSeriesConstraint(constraint, metricKey, expected, measurement);
        }

        if (!measurement.Value.HasValue)
        {
            return new ConstraintResult
            {
                Id = constraint.Id,
                Metric = metricKey,
                Node = constraint.Node?.ToString(),
                Unit = constraint.Unit,
                Operator = constraint.Op,
                ExpectedRaw = constraint.Value,
                Expected = expected,
                Actual = null,
                ActualUnit = measurement.Unit,
                Passed = false,
                FailureReason = ConstraintResult.NoMeasurement,
                Message = $"Measurement '{metricKey}' did not provide a scalar value.",
            };
        }

        var actual = measurement.Value.Value;
        if (!IsFinite(actual))
        {
            return new ConstraintResult
            {
                Id = constraint.Id,
                Metric = metricKey,
                Node = constraint.Node?.ToString(),
                Unit = constraint.Unit,
                Operator = constraint.Op,
                ExpectedRaw = constraint.Value,
                Expected = expected,
                Actual = actual,
                ActualUnit = measurement.Unit,
                Passed = false,
                FailureReason = ConstraintResult.NonFiniteValue,
                Message =
                    $"Non-finite measurement value: {actual.ToString(CultureInfo.InvariantCulture)}",
            };
        }

        var passed = EvaluateOperator(constraint.Op, actual, expected);

        return new ConstraintResult
        {
            Id = constraint.Id,
            Metric = metricKey,
            Node = constraint.Node?.ToString(),
            Unit = constraint.Unit,
            Operator = constraint.Op,
            ExpectedRaw = constraint.Value,
            Expected = expected,
            Actual = actual,
            ActualUnit = measurement.Unit,
            Passed = passed,
            FailureReason = passed ? null : ConstraintResult.ConstraintViolation,
            Message = passed ? "PASS" : "FAIL",
        };
    }

    private static ConstraintResult EvaluateSeriesConstraint(
        NumericConstraint constraint,
        string metricKey,
        double expected,
        MeasurementResult measurement
    )
    {
        var series = measurement.Values!;
        if (series.Length == 0)
        {
            return new ConstraintResult
            {
                Id = constraint.Id,
                Metric = metricKey,
                Node = constraint.Node?.ToString(),
                Unit = constraint.Unit,
                Operator = constraint.Op,
                ExpectedRaw = constraint.Value,
                Expected = expected,
                Actual = null,
                ActualUnit = measurement.Unit,
                Passed = false,
                FailureReason = ConstraintResult.EmptySpectrum,
                Message = $"Measurement '{metricKey}' returned no samples.",
            };
        }

        if (series.Any(v => !IsFinite(v)))
        {
            return new ConstraintResult
            {
                Id = constraint.Id,
                Metric = metricKey,
                Node = constraint.Node?.ToString(),
                Unit = constraint.Unit,
                Operator = constraint.Op,
                ExpectedRaw = constraint.Value,
                Expected = expected,
                Actual = null,
                ActualUnit = measurement.Unit,
                Passed = false,
                FailureReason = ConstraintResult.NonFiniteValue,
                Message = "Non-finite measurement value in spectrum/waveform.",
            };
        }

        var passed = series.All(actual => EvaluateOperator(constraint.Op, actual, expected));
        var worstCase = GetWorstCaseActual(constraint.Op, expected, series);

        return new ConstraintResult
        {
            Id = constraint.Id,
            Metric = metricKey,
            Node = constraint.Node?.ToString(),
            Unit = constraint.Unit,
            Operator = constraint.Op,
            ExpectedRaw = constraint.Value,
            Expected = expected,
            Actual = worstCase,
            ActualUnit = measurement.Unit,
            Passed = passed,
            FailureReason = passed ? null : ConstraintResult.ConstraintViolation,
            Message = passed ? "PASS" : "FAIL",
        };
    }

    private static double GetWorstCaseActual(
        string op,
        double expected,
        IReadOnlyList<double> values
    )
    {
        if (values.Count == 0)
        {
            throw new InvalidOperationException("Series must contain at least one value.");
        }

        return op switch
        {
            ">=" or ">" => values.Min(),
            "<=" or "<" => values.Max(),
            "==" => values.Aggregate(
                values[0],
                (worst, current) =>
                    Math.Abs(current - expected) > Math.Abs(worst - expected) ? current : worst
            ),
            _ => throw new InvalidOperationException($"Unknown operator: {op}"),
        };
    }

    private static KeyValuePair<string, MeasurementResult>? FindMatchingMeasurement(
        string metricKey,
        NumericConstraint constraint,
        BenchResult results
    )
    {
        foreach (var kvp in results.Measurements)
        {
            var measurement = kvp.Value;
            // Match by metric name (case-insensitive)
            if (!string.Equals(measurement.Metric, metricKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Match by bench if constraint specifies one
            if (
                !string.IsNullOrEmpty(constraint.Bench)
                && !string.IsNullOrEmpty(measurement.Bench)
                && !string.Equals(
                    measurement.Bench,
                    constraint.Bench,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }

            // If constraint specifies a node, measurement must match (or be null/empty)
            if (constraint.Node != null && !MatchesNode(constraint.Node, measurement.Node))
            {
                continue;
            }

            return kvp;
        }

        return null;
    }

    private static string FormatMetricKey(NumericConstraint constraint)
    {
        if (constraint.MetricArgs.Count == 0)
        {
            return constraint.Metric;
        }

        var args = constraint
            .MetricArgs.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => $"{a.Name}={a.Value}");

        return $"{constraint.Metric}({string.Join(", ", args)})";
    }

    private static bool MatchesNode(NodeRef constraintNode, string? measurementNode)
    {
        if (string.IsNullOrWhiteSpace(measurementNode))
        {
            return false;
        }

        if (measurementNode.Contains("::", StringComparison.Ordinal))
        {
            return string.Equals(
                measurementNode,
                constraintNode.ToString(),
                StringComparison.OrdinalIgnoreCase
            );
        }

        return string.Equals(
            measurementNode,
            constraintNode.Path,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool EvaluateOperator(string op, double actual, double expected)
    {
        if (!IsFinite(actual) || !IsFinite(expected))
        {
            return false;
        }

        return op switch
        {
            ">=" => actual >= expected,
            "<=" => actual <= expected,
            "==" => Math.Abs(actual - expected) < 1e-9, // Floating point comparison
            ">" => actual > expected,
            "<" => actual < expected,
            _ => throw new InvalidOperationException($"Unknown operator: {op}"),
        };
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static double ParseValue(string valueStr)
    {
        return QuantityLiteral.ParseMagnitude(valueStr.Trim());
    }
}
