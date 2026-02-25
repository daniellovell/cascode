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
                Expected = ParseValue(constraint.Value, constraint.Unit),
                Actual = null,
                ActualUnit = null,
                Passed = false,
                FailureReason = ConstraintResult.NoMeasurement,
                Message =
                    $"No measurement found for {metricKey}"
                    + (constraint.Node != null ? $" @ {constraint.Node}" : ""),
            };
        }

        var expected = ParseValue(constraint.Value, constraint.Unit);
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
        var actual = measurement.Value;
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

    private static double ParseValue(string valueStr, string unit)
    {
        // Parse numeric value with unit multipliers
        // Examples: "100M" -> 100e6, "1.5k" -> 1.5e3, "500u" -> 500e-6
        valueStr = valueStr.Trim();

        // Extract numeric part and multiplier suffix
        var multiplier = 1.0;
        var numericPart = valueStr;

        if (valueStr.Length > 0 && char.IsLetter(valueStr[^1]))
        {
            var suffix = valueStr[^1];
            numericPart = valueStr[..^1];

            multiplier = suffix switch
            {
                'k' or 'K' => 1e3,
                'M' => 1e6,
                'm' => 1e-3,
                'G' or 'g' => 1e9,
                'T' or 't' => 1e12,
                'u' or 'U' => 1e-6,
                'n' or 'N' => 1e-9,
                'p' or 'P' => 1e-12,
                'f' or 'F' => 1e-15,
                _ => throw new FormatException(
                    $"Unrecognized unit suffix '{suffix}' in value: {valueStr}"
                ),
            };
        }

        if (
            !double.TryParse(
                numericPart,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value
            )
        )
        {
            throw new FormatException($"Invalid numeric value: {valueStr}");
        }

        return value * multiplier;
    }
}
