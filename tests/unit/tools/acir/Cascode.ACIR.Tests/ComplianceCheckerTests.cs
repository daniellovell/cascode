using System.Collections.Generic;
using System.IO;
using Cascode.ACIR;
using Cascode.Bench;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.ACIR.Tests;

public class ComplianceCheckerTests
{
    [Theory]
    [InlineData(">=", 100.0, 150.0, true)]
    [InlineData(">=", 100.0, 100.0, true)]
    [InlineData(">=", 100.0, 50.0, false)]
    [InlineData("<=", 100.0, 50.0, true)]
    [InlineData("<=", 100.0, 100.0, true)]
    [InlineData("<=", 100.0, 150.0, false)]
    [InlineData("==", 100.0, 100.0, true)]
    [InlineData("==", 100.0, 100.000001, false)]
    [InlineData(">", 100.0, 150.0, true)]
    [InlineData(">", 100.0, 100.0, false)]
    [InlineData("<", 100.0, 50.0, true)]
    [InlineData("<", 100.0, 100.0, false)]
    public void Check_Operators_EvaluateCorrectly(string op, double threshold, double measured, bool expectedPass)
    {
        var circuit = CreateCircuitWithConstraint("c_test", "TestMetric", null, op, threshold.ToString(), "");
        var results = CreateResultsWithMeasurement("TestMetric", measured, "", null);

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Single(report.Results);
        Assert.Equal(expectedPass, report.Results[0].Passed);
    }

    [Theory]
    [InlineData("100M", 100e6)]
    [InlineData("1k", 1e3)]
    [InlineData("500u", 500e-6)]
    [InlineData("10n", 10e-9)]
    [InlineData("1p", 1e-12)]
    [InlineData("2.5G", 2.5e9)]
    [InlineData("45", 45)]
    public void Check_Units_ParseCorrectly(string valueStr, double expectedValue)
    {
        var circuit = CreateCircuitWithConstraint("c_test", "TestMetric", null, ">=", valueStr, "Hz");
        var results = CreateResultsWithMeasurement("TestMetric", expectedValue, "Hz", null);

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Single(report.Results);
        Assert.True(report.Results[0].Passed, $"Expected {valueStr} to parse to {expectedValue}");
    }

    [Fact]
    public void Check_NodeMatching_MatchesByNode()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Constraints = new ConstraintsBlock
            {
                Numeric = new List<NumericConstraint>
                {
                    new() { Id = "c_out1", Metric = "Gain", Node = "OUT1", Op = ">=", Value = "40", Unit = "dB" },
                    new() { Id = "c_out2", Metric = "Gain", Node = "OUT2", Op = ">=", Value = "30", Unit = "dB" }
                }
            }
        };

        var results = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m_out1"] = new() { Metric = "Gain", Value = 42.0, Unit = "dB", Node = "OUT1" },
                ["m_out2"] = new() { Metric = "Gain", Value = 25.0, Unit = "dB", Node = "OUT2" }
            }
        };

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Equal(2, report.Results.Count);
        Assert.True(report.Results[0].Passed);   // OUT1 >= 40
        Assert.False(report.Results[1].Passed);  // OUT2 >= 30 but measured 25
    }

    [Fact]
    public void Check_MissingMeasurement_ReportsFailure()
    {
        var circuit = CreateCircuitWithConstraint("c_test", "MissingMetric", "OUT", ">=", "100", "Hz");
        var results = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m_other"] = new() { Metric = "OtherMetric", Value = 100.0, Unit = "Hz", Node = "OUT" }
            }
        };

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Single(report.Results);
        Assert.False(report.Results[0].Passed);
        Assert.Contains("No measurement found", report.Results[0].Message);
    }

    [Fact]
    public void Check_NoConstraints_ReturnsEmptyReport()
    {
        var circuit = new Circuit { Name = "TestCircuit" };
        var results = CreateResultsWithMeasurement("SomeMetric", 100.0, "Hz", null);

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Empty(report.Results);
        Assert.Equal(0, report.TotalCount);
    }

    [Fact]
    public void Check_CaseInsensitiveMetricMatching()
    {
        var circuit = CreateCircuitWithConstraint("c_test", "GainBandwidth", "OUT", ">=", "100M", "Hz");
        var results = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m_gbw"] = new() { Metric = "gainbandwidth", Value = 150e6, Unit = "Hz", Node = "OUT" }
            }
        };

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Single(report.Results);
        Assert.True(report.Results[0].Passed);
    }

    [Fact]
    public void Check_WithGoldenACIR_ParsesAndEvaluates()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");
        var resultsPath = Path.Combine(repoRoot, "tests/golden/results/ota/OTA5TSingleEnded_SEOpAmpACBench_results.json");

        using var acirReader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(acirReader);
        Assert.Single(doc.Circuits);

        var circuit = doc.Circuits[0];
        Assert.NotNull(circuit.Constraints);
        Assert.Equal(4, circuit.Constraints.Numeric.Count);

        var resultsJson = File.ReadAllText(resultsPath);
        var results = System.Text.Json.JsonSerializer.Deserialize<BenchResult>(resultsJson);
        Assert.NotNull(results);

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Equal(4, report.TotalCount);
        Assert.Equal(4, report.PassedCount);
        Assert.Equal(0, report.FailedCount);
    }

    [Fact]
    public void Check_ReportCounts_CorrectlyTallied()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Constraints = new ConstraintsBlock
            {
                Numeric = new List<NumericConstraint>
                {
                    new() { Id = "c1", Metric = "M1", Op = ">=", Value = "100", Unit = "" },
                    new() { Id = "c2", Metric = "M2", Op = ">=", Value = "100", Unit = "" },
                    new() { Id = "c3", Metric = "M3", Op = ">=", Value = "100", Unit = "" }
                }
            }
        };

        var results = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m1"] = new() { Metric = "M1", Value = 150.0, Unit = "" },
                ["m2"] = new() { Metric = "M2", Value = 50.0, Unit = "" },
                ["m3"] = new() { Metric = "M3", Value = 200.0, Unit = "" }
            }
        };

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Equal(3, report.TotalCount);
        Assert.Equal(2, report.PassedCount);
        Assert.Equal(1, report.FailedCount);
    }

    private static Circuit CreateCircuitWithConstraint(string id, string metric, string? node, string op, string value, string unit)
    {
        return new Circuit
        {
            Name = "TestCircuit",
            Constraints = new ConstraintsBlock
            {
                Numeric = new List<NumericConstraint>
                {
                    new() { Id = id, Metric = metric, Node = node, Op = op, Value = value, Unit = unit }
                }
            }
        };
    }

    private static BenchResult CreateResultsWithMeasurement(string metric, double value, string unit, string? node)
    {
        return new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m_test"] = new() { Metric = metric, Value = value, Unit = unit, Node = node }
            }
        };
    }
}

