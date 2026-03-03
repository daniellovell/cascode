using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cascode.Bench;
using Cascode.Language;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Language.Tests;

public class ComplianceCheckerTests
{
    [Theory]
    [InlineData(">=")]
    [InlineData("<=")]
    [InlineData("==")]
    [InlineData(">")]
    [InlineData("<")]
    public void Check_NonFiniteMeasurementValue_FailsConstraint(string op)
    {
        var circuit = CreateCircuitWithConstraint("c_test", "TestMetric", null, op, "20", "dB");
        var results = CreateResultsWithMeasurement(
            "TestMetric",
            double.PositiveInfinity,
            "dB",
            null
        );

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Single(report.Results);
        Assert.False(report.Results[0].Passed);
        Assert.Equal(ConstraintResult.NonFiniteValue, report.Results[0].FailureReason);
    }

    [Theory]
    [InlineData(">=")]
    [InlineData("<=")]
    [InlineData("==")]
    [InlineData(">")]
    [InlineData("<")]
    public void Check_NaNMeasurementValue_FailsConstraint(string op)
    {
        var circuit = CreateCircuitWithConstraint("c_test", "TestMetric", null, op, "20", "dB");
        var results = CreateResultsWithMeasurement("TestMetric", double.NaN, "dB", null);

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Single(report.Results);
        Assert.False(report.Results[0].Passed);
        Assert.Equal(ConstraintResult.NonFiniteValue, report.Results[0].FailureReason);
    }

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
    public void Check_Operators_EvaluateCorrectly(
        string op,
        double threshold,
        double measured,
        bool expectedPass
    )
    {
        var circuit = CreateCircuitWithConstraint(
            "c_test",
            "TestMetric",
            null,
            op,
            threshold.ToString(),
            ""
        );
        var results = CreateResultsWithMeasurement("TestMetric", measured, "", null);

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Single(report.Results);
        Assert.Equal(expectedPass, report.Results[0].Passed);
        if (!expectedPass)
        {
            Assert.Equal(ConstraintResult.ConstraintViolation, report.Results[0].FailureReason);
        }
    }

    [Fact]
    public void Check_ValuesArray_AllPass_Passes()
    {
        var circuit = CreateCircuitWithConstraint(
            "c_spectrum",
            "ForwardGainSpectrum",
            null,
            ">=",
            "-0.97",
            "dB"
        );
        var results = CreateResultsWithArrayMeasurement(
            "ForwardGainSpectrum",
            [-0.95, -0.92, -0.90],
            "dB",
            null
        );

        var report = ComplianceChecker.Check(circuit, results);

        var result = Assert.Single(report.Results);
        Assert.True(result.Passed);
        Assert.Equal(-0.95, result.Actual);
    }

    [Fact]
    public void Check_ValuesArray_OneViolation_Fails()
    {
        var circuit = CreateCircuitWithConstraint(
            "c_spectrum",
            "ForwardGainSpectrum",
            null,
            ">=",
            "-0.97",
            "dB"
        );
        var results = CreateResultsWithArrayMeasurement(
            "ForwardGainSpectrum",
            [-0.95, -0.98, -0.90],
            "dB",
            null
        );

        var report = ComplianceChecker.Check(circuit, results);

        var result = Assert.Single(report.Results);
        Assert.False(result.Passed);
        Assert.Equal(ConstraintResult.ConstraintViolation, result.FailureReason);
        Assert.Equal(-0.98, result.Actual);
    }

    [Fact]
    public void Check_ValuesArray_EmptyArray_Fails()
    {
        var circuit = CreateCircuitWithConstraint(
            "c_spectrum",
            "ForwardGainSpectrum",
            null,
            ">=",
            "-0.97",
            "dB"
        );
        var results = CreateResultsWithArrayMeasurement("ForwardGainSpectrum", [], "dB", null);

        var report = ComplianceChecker.Check(circuit, results);

        var result = Assert.Single(report.Results);
        Assert.False(result.Passed);
        Assert.Equal(ConstraintResult.EmptySpectrum, result.FailureReason);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Check_ValuesArray_WithNonFiniteElement_Fails(double sample)
    {
        var circuit = CreateCircuitWithConstraint(
            "c_spectrum",
            "ForwardGainSpectrum",
            null,
            ">=",
            "-0.97",
            "dB"
        );
        var results = CreateResultsWithArrayMeasurement(
            "ForwardGainSpectrum",
            [-0.95, sample, -0.90],
            "dB",
            null
        );

        var report = ComplianceChecker.Check(circuit, results);

        var result = Assert.Single(report.Results);
        Assert.False(result.Passed);
        Assert.Equal(ConstraintResult.NonFiniteValue, result.FailureReason);
    }

    [Theory]
    [InlineData(">=", 5.0, new[] { 5.0, 7.0 }, true, 5.0)]
    [InlineData(">", 5.0, new[] { 5.0, 7.0 }, false, 5.0)]
    [InlineData("<=", 5.0, new[] { 1.0, 5.0 }, true, 5.0)]
    [InlineData("<", 5.0, new[] { 1.0, 5.0 }, false, 5.0)]
    [InlineData("==", 5.0, new[] { 5.0, 5.0 + 1e-10 }, true, 5.0 + 1e-10)]
    [InlineData("==", 5.0, new[] { 5.0, 5.0 + 1e-6 }, false, 5.0 + 1e-6)]
    public void Check_ValuesArray_AllOperators(
        string op,
        double threshold,
        double[] measured,
        bool expectedPass,
        double expectedWorstCase
    )
    {
        var circuit = CreateCircuitWithConstraint(
            "c_array_op",
            "ArrayMetric",
            null,
            op,
            threshold.ToString("G17", System.Globalization.CultureInfo.InvariantCulture),
            ""
        );
        var results = CreateResultsWithArrayMeasurement("ArrayMetric", measured, "", null);

        var report = ComplianceChecker.Check(circuit, results);

        var result = Assert.Single(report.Results);
        Assert.Equal(expectedPass, result.Passed);
        Assert.Equal(expectedWorstCase, result.Actual);
    }

    [Theory]
    [InlineData("100M", 100e6)]
    [InlineData("1k", 1e3)]
    [InlineData("500u", 500e-6)]
    [InlineData("10n", 10e-9)]
    [InlineData("1p", 1e-12)]
    [InlineData("2.5G", 2.5e9)]
    [InlineData("45", 45)]
    [InlineData("5m", 5e-3)]
    [InlineData("2.5m", 2.5e-3)]
    public void Check_Units_ParseCorrectly(string valueStr, double expectedValue)
    {
        var circuit = CreateCircuitWithConstraint(
            "c_test",
            "TestMetric",
            null,
            ">=",
            valueStr,
            "Hz"
        );
        var results = CreateResultsWithMeasurement("TestMetric", expectedValue, "Hz", null);

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Single(report.Results);
        Assert.True(report.Results[0].Passed, $"Expected {valueStr} to parse to {expectedValue}");
    }

    [Theory]
    [InlineData("100X")]
    [InlineData("50Z")]
    [InlineData("1a")]
    public void Check_InvalidSuffix_ThrowsFormatException(string valueStr)
    {
        var circuit = CreateCircuitWithConstraint(
            "c_test",
            "TestMetric",
            null,
            ">=",
            valueStr,
            "Hz"
        );
        var results = CreateResultsWithMeasurement("TestMetric", 100.0, "Hz", null);

        var ex = Assert.Throws<FormatException>(() => ComplianceChecker.Check(circuit, results));
        Assert.Contains("Unrecognized unit suffix", ex.Message);
        Assert.Contains(valueStr, ex.Message);
    }

    [Fact]
    public void Check_MilliAndMegaAreCaseSensitive()
    {
        var circuitMilli = CreateCircuitWithConstraint(
            "c_milli",
            "TestMetric",
            null,
            "==",
            "1m",
            ""
        );
        var circuitMega = CreateCircuitWithConstraint("c_mega", "TestMetric", null, "==", "1M", "");

        var resultsMilli = CreateResultsWithMeasurement("TestMetric", 1e-3, "", null);
        var resultsMega = CreateResultsWithMeasurement("TestMetric", 1e6, "", null);

        var reportMilli = ComplianceChecker.Check(circuitMilli, resultsMilli);
        var reportMega = ComplianceChecker.Check(circuitMega, resultsMega);

        Assert.True(reportMilli.Results[0].Passed, "1m should equal 1e-3 (milli)");
        Assert.True(reportMega.Results[0].Passed, "1M should equal 1e6 (mega)");
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
                    new()
                    {
                        Id = "c_out1",
                        Bench = "TestBench",
                        Metric = "Gain",
                        Node = new NodeRef { Scope = "net", Path = "OUT1" },
                        Op = ">=",
                        Value = "40",
                        Unit = "dB",
                    },
                    new()
                    {
                        Id = "c_out2",
                        Bench = "TestBench",
                        Metric = "Gain",
                        Node = new NodeRef { Scope = "net", Path = "OUT2" },
                        Op = ">=",
                        Value = "30",
                        Unit = "dB",
                    },
                },
            },
        };

        var results = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m_out1"] = new()
                {
                    Metric = "Gain",
                    Value = 42.0,
                    Unit = "dB",
                    Node = "OUT1",
                },
                ["m_out2"] = new()
                {
                    Metric = "Gain",
                    Value = 25.0,
                    Unit = "dB",
                    Node = "OUT2",
                },
            },
        };

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Equal(2, report.Results.Count);
        Assert.True(report.Results[0].Passed); // OUT1 >= 40
        Assert.False(report.Results[1].Passed); // OUT2 >= 30 but measured 25
    }

    [Fact]
    public void Check_MissingMeasurement_ReportsFailure()
    {
        var circuit = CreateCircuitWithConstraint(
            "c_test",
            "MissingMetric",
            "OUT",
            ">=",
            "100",
            "Hz"
        );
        var results = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m_other"] = new()
                {
                    Metric = "OtherMetric",
                    Value = 100.0,
                    Unit = "Hz",
                    Node = "OUT",
                },
            },
        };

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Single(report.Results);
        Assert.False(report.Results[0].Passed);
        Assert.Contains("No measurement found", report.Results[0].Message);
        Assert.Equal(ConstraintResult.NoMeasurement, report.Results[0].FailureReason);
    }

    [Fact]
    public void Check_MeasurementError_ReportsBenchErrorFailureReason()
    {
        var circuit = CreateCircuitWithConstraint(
            "c_test",
            "GainBandwidth",
            "OUT",
            ">=",
            "100M",
            "Hz"
        );
        var results = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m_gbw"] = new()
                {
                    Metric = "GainBandwidth",
                    Value = double.NaN,
                    Unit = "Hz",
                    Node = "OUT",
                    Error = "bench failed",
                },
            },
        };

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Single(report.Results);
        Assert.False(report.Results[0].Passed);
        Assert.Equal(ConstraintResult.BenchError, report.Results[0].FailureReason);
        Assert.Contains("Measurement error", report.Results[0].Message);
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
        var circuit = CreateCircuitWithConstraint(
            "c_test",
            "GainBandwidth",
            "OUT",
            ">=",
            "100M",
            "Hz"
        );
        var results = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m_gbw"] = new()
                {
                    Metric = "gainbandwidth",
                    Value = 150e6,
                    Unit = "Hz",
                    Node = "OUT",
                },
            },
        };

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Single(report.Results);
        Assert.True(report.Results[0].Passed);
    }

    [Fact]
    public void Check_WithGoldenCascode_ParsesAndHas4Constraints()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var cascodePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEnded.el.cai");

        using var reader = File.OpenText(cascodePath);
        var doc = CascodeReader.Read(reader, cascodePath);
        Assert.Single(doc.Circuits);

        var circuit = doc.Circuits[0];
        Assert.NotNull(circuit.Constraints);
        Assert.Equal(5, circuit.Constraints.Numeric.Count);
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
                    new()
                    {
                        Id = "c1",
                        Metric = "M1",
                        Op = ">=",
                        Value = "100",
                        Unit = "",
                    },
                    new()
                    {
                        Id = "c2",
                        Metric = "M2",
                        Op = ">=",
                        Value = "100",
                        Unit = "",
                    },
                    new()
                    {
                        Id = "c3",
                        Metric = "M3",
                        Op = ">=",
                        Value = "100",
                        Unit = "",
                    },
                },
            },
        };

        var results = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m1"] = new()
                {
                    Metric = "M1",
                    Value = 150.0,
                    Unit = "",
                },
                ["m2"] = new()
                {
                    Metric = "M2",
                    Value = 50.0,
                    Unit = "",
                },
                ["m3"] = new()
                {
                    Metric = "M3",
                    Value = 200.0,
                    Unit = "",
                },
            },
        };

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Equal(3, report.TotalCount);
        Assert.Equal(2, report.PassedCount);
        Assert.Equal(1, report.FailedCount);
    }

    [Fact]
    public void Check_PreservesConstraintDeclarationOrderInResults()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Constraints = new ConstraintsBlock
            {
                Numeric = new List<NumericConstraint>
                {
                    new()
                    {
                        Id = "z_first",
                        Bench = "TestBench",
                        Metric = "M1",
                        Op = ">=",
                        Value = "1",
                        Unit = "",
                    },
                    new()
                    {
                        Id = "a_second",
                        Bench = "TestBench",
                        Metric = "M2",
                        Op = ">=",
                        Value = "1",
                        Unit = "",
                    },
                    new()
                    {
                        Id = "m_third",
                        Bench = "TestBench",
                        Metric = "M3",
                        Op = ">=",
                        Value = "1",
                        Unit = "",
                    },
                },
            },
        };

        var results = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m1"] = new()
                {
                    Metric = "M1",
                    Value = 2.0,
                    Unit = "",
                },
                ["m2"] = new()
                {
                    Metric = "M2",
                    Value = 2.0,
                    Unit = "",
                },
                ["m3"] = new()
                {
                    Metric = "M3",
                    Value = 2.0,
                    Unit = "",
                },
            },
        };

        var report = ComplianceChecker.Check(circuit, results);

        Assert.Equal(
            ["z_first", "a_second", "m_third"],
            report.Results.Select(r => r.Id).ToArray()
        );
    }

    private static Circuit CreateCircuitWithConstraint(
        string id,
        string metric,
        string? node,
        string op,
        string value,
        string unit,
        string bench = "TestBench"
    )
    {
        return new Circuit
        {
            Name = "TestCircuit",
            Constraints = new ConstraintsBlock
            {
                Numeric = new List<NumericConstraint>
                {
                    new()
                    {
                        Id = id,
                        Bench = bench,
                        Metric = metric,
                        Node = node != null ? new NodeRef { Scope = "net", Path = node } : null,
                        Op = op,
                        Value = value,
                        Unit = unit,
                    },
                },
            },
        };
    }

    private static BenchResult CreateResultsWithMeasurement(
        string metric,
        double value,
        string unit,
        string? node
    )
    {
        return new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m_test"] = new()
                {
                    Metric = metric,
                    Value = value,
                    Unit = unit,
                    Node = node,
                },
            },
        };
    }

    private static BenchResult CreateResultsWithArrayMeasurement(
        string metric,
        double[] values,
        string unit,
        string? node
    )
    {
        return new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "TestBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["m_test"] = new()
                {
                    Metric = metric,
                    Value = null,
                    Values = values,
                    Unit = unit,
                    Node = node,
                },
            },
        };
    }

    [Fact]
    public void Check_BenchAwareFiltering_OnlyChecksConstraintsForMatchingBench()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Constraints = new ConstraintsBlock
            {
                Numeric = new List<NumericConstraint>
                {
                    new()
                    {
                        Id = "c_gbw",
                        Bench = "transfer_bench",
                        Metric = "GainBandwidth",
                        Node = new NodeRef { Scope = "net", Path = "OUT" },
                        Op = ">=",
                        Value = "100M",
                        Unit = "Hz",
                    },
                    new()
                    {
                        Id = "c_gain",
                        Bench = "transfer_bench",
                        Metric = "PassbandGain",
                        Node = new NodeRef { Scope = "net", Path = "OUT" },
                        Op = ">=",
                        Value = "40",
                        Unit = "dB",
                    },
                    new()
                    {
                        Id = "c_pwr",
                        Bench = "vdd_pwr",
                        Metric = "QuiescentPower",
                        Op = "<=",
                        Value = "500u",
                        Unit = "W",
                    },
                },
            },
        };

        var acResults = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "transfer_bench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["gbw"] = new()
                {
                    Metric = "GainBandwidth",
                    Value = 150e6,
                    Unit = "Hz",
                    Node = "OUT",
                },
                ["gain"] = new()
                {
                    Metric = "PassbandGain",
                    Value = 45.0,
                    Unit = "dB",
                    Node = "OUT",
                },
            },
        };

        var report = ComplianceChecker.Check(circuit, acResults);

        Assert.Equal(2, report.TotalCount);
        Assert.Equal(2, report.PassedCount);
        Assert.Equal(0, report.FailedCount);
        Assert.Single(report.UncheckedByBench);
        Assert.True(report.UncheckedByBench.ContainsKey("vdd_pwr"));
        Assert.Single(report.UncheckedByBench["vdd_pwr"]);
        Assert.Equal("c_pwr", report.UncheckedByBench["vdd_pwr"][0].Id);
    }

    [Fact]
    public void Check_BenchAwareFiltering_VddPwrOnlyChecksPowerConstraint()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Constraints = new ConstraintsBlock
            {
                Numeric = new List<NumericConstraint>
                {
                    new()
                    {
                        Id = "c_gbw",
                        Bench = "transfer_bench",
                        Metric = "GainBandwidth",
                        Node = new NodeRef { Scope = "net", Path = "OUT" },
                        Op = ">=",
                        Value = "100M",
                        Unit = "Hz",
                    },
                    new()
                    {
                        Id = "c_pwr",
                        Bench = "vdd_pwr",
                        Metric = "QuiescentPower",
                        Op = "<=",
                        Value = "500u",
                        Unit = "W",
                    },
                },
            },
        };

        var dcResults = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "vdd_pwr",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["pwr"] = new()
                {
                    Metric = "QuiescentPower",
                    Value = 0.0003,
                    Unit = "W",
                },
            },
        };

        var report = ComplianceChecker.Check(circuit, dcResults);

        Assert.Equal(1, report.TotalCount);
        Assert.Equal(1, report.PassedCount);
        Assert.Single(report.UncheckedByBench);
        Assert.True(report.UncheckedByBench.ContainsKey("transfer_bench"));
    }

    [Fact]
    public void Check_CombinedResults_ChecksAllConstraints()
    {
        // When bench="all" (combined results from multiple benches), all constraints should be checked
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Constraints = new ConstraintsBlock
            {
                Numeric = new List<NumericConstraint>
                {
                    new()
                    {
                        Id = "c_gbw",
                        Bench = "transfer_bench",
                        Metric = "GainBandwidth",
                        Node = new NodeRef { Scope = "net", Path = "OUT" },
                        Op = ">=",
                        Value = "100M",
                        Unit = "Hz",
                    },
                    new()
                    {
                        Id = "c_gain",
                        Bench = "transfer_bench",
                        Metric = "PassbandGain",
                        Node = new NodeRef { Scope = "net", Path = "OUT" },
                        Op = ">=",
                        Value = "40",
                        Unit = "dB",
                    },
                    new()
                    {
                        Id = "c_pwr",
                        Bench = "vdd_pwr",
                        Metric = "QuiescentPower",
                        Op = "<=",
                        Value = "500u",
                        Unit = "W",
                    },
                },
            },
        };

        // Combined results with bench="all" containing measurements from both AC and DC benches
        var combinedResults = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "all",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["GainBandwidth@OUT"] = new()
                {
                    Metric = "GainBandwidth",
                    Value = 150e6,
                    Unit = "Hz",
                    Node = "OUT",
                },
                ["PassbandGain@OUT"] = new()
                {
                    Metric = "PassbandGain",
                    Value = 45.0,
                    Unit = "dB",
                    Node = "OUT",
                },
                ["QuiescentPower"] = new()
                {
                    Metric = "QuiescentPower",
                    Value = 0.0003,
                    Unit = "W",
                },
            },
        };

        var report = ComplianceChecker.Check(circuit, combinedResults);

        // All 3 constraints should be checked (no filtering for combined results)
        Assert.Equal(3, report.TotalCount);
        Assert.Equal(3, report.PassedCount);
        Assert.Equal(0, report.FailedCount);
        // No unchecked constraints since we're checking all
        Assert.Empty(report.UncheckedByBench);
    }

    [Fact]
    public void Check_AllDeclaredMode_EvaluatesAllDeclaredConstraints()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var cascodePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEnded.el.cai");
        var resultsPath = Path.Combine(
            repoRoot,
            "tests/golden/results/ota/OTA5TSingleEnded_DCSwept_vdd_pwr_results.json"
        );

        using var reader = File.OpenText(cascodePath);
        var doc = CascodeReader.Read(reader, cascodePath);
        var circuit = doc.Circuits[0];

        var resultsJson = File.ReadAllText(resultsPath);
        var results = JsonSerializer.Deserialize<BenchResult>(resultsJson);
        Assert.NotNull(results);

        var report = ComplianceChecker.Check(
            circuit,
            results,
            ConstraintEvaluationMode.AllDeclared
        );

        Assert.Equal(5, report.TotalCount);
        Assert.Equal(1, report.PassedCount);
        Assert.Equal(4, report.FailedCount);
        Assert.Empty(report.UncheckedByBench);

        var cPwr = report.Results.Single(r => r.Id == "c_pwr");
        Assert.True(cPwr.Passed);
        Assert.Null(cPwr.FailureReason);

        var cGbw = report.Results.Single(r => r.Id == "c_gbw");
        Assert.False(cGbw.Passed);
        Assert.Equal(ConstraintResult.NoMeasurement, cGbw.FailureReason);
    }

    [Fact]
    public void Check_NoBenchQualifier_ChecksAllConstraints()
    {
        var circuit = new Circuit
        {
            Name = "TestCircuit",
            Constraints = new ConstraintsBlock
            {
                Numeric = new List<NumericConstraint>
                {
                    new()
                    {
                        Id = "c_gbw",
                        Bench = string.Empty,
                        Metric = "GainBandwidth",
                        Op = ">=",
                        Value = "100M",
                        Unit = "Hz",
                    },
                    new()
                    {
                        Id = "c_pwr",
                        Bench = string.Empty,
                        Metric = "QuiescentPower",
                        Op = "<=",
                        Value = "500u",
                        Unit = "W",
                    },
                },
            },
        };

        var results = new BenchResult
        {
            Circuit = "TestCircuit",
            Bench = "SomeBench",
            Measurements = new Dictionary<string, MeasurementResult>
            {
                ["gbw"] = new()
                {
                    Metric = "GainBandwidth",
                    Value = 150e6,
                    Unit = "Hz",
                },
            },
        };

        var report = ComplianceChecker.Check(circuit, results);

        // Both constraints should be checked (no filtering)
        Assert.Equal(2, report.TotalCount);
        Assert.Equal(1, report.PassedCount); // GainBandwidth passes
        Assert.Equal(1, report.FailedCount); // QuiescentPower not measured = fail
        Assert.Empty(report.UncheckedByBench);
    }

    [Fact]
    public void Check_WithGoldenCascode_BenchAwareFiltering_TransferBenchReturns3of3()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var cascodePath = Path.Combine(repoRoot, "tests/golden/cas/ota/OTA5TSingleEnded.el.cai");
        var resultsPath = Path.Combine(
            repoRoot,
            "tests/golden/results/ota/OTA5TSingleEnded_transfer_bench_results.json"
        );

        using var reader = File.OpenText(cascodePath);
        var doc = CascodeReader.Read(reader, cascodePath);
        var circuit = doc.Circuits[0];

        var resultsJson = File.ReadAllText(resultsPath);
        var results = JsonSerializer.Deserialize<BenchResult>(resultsJson);
        Assert.NotNull(results);

        var report = ComplianceChecker.Check(circuit, results);

        // transfer_bench should only check 3 constraints (gain, gbw, pm)
        Assert.Equal(3, report.TotalCount);
        Assert.Equal(3, report.PassedCount);
        Assert.Equal(0, report.FailedCount);

        // Power constraint should be tracked as unchecked
        Assert.Equal(2, report.UncheckedByBench.Count);
        Assert.True(report.UncheckedByBench.ContainsKey("vdd_pwr"));
        var tranBenchKey = report.UncheckedByBench.Keys.Single(k =>
            k.StartsWith("tran_bench", StringComparison.OrdinalIgnoreCase)
        );

        Assert.Single(report.UncheckedByBench["vdd_pwr"]);
        Assert.Single(report.UncheckedByBench[tranBenchKey]);
        Assert.Equal("c_pwr", report.UncheckedByBench["vdd_pwr"][0].Id);
        Assert.Equal("c_swing", report.UncheckedByBench[tranBenchKey][0].Id);
    }
}
