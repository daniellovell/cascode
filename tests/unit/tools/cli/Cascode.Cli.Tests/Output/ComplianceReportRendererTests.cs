using Cascode.Cli.Output;
using Cascode.Language;

namespace Cascode.Cli.Tests.Output;

public sealed class ComplianceReportRendererTests
{
    [Fact]
    public void FormatComplianceSummary_ZeroConstraints_ReturnsZeroPercent()
    {
        var report = new ComplianceReport();

        var formatted = ComplianceReportRenderer.FormatComplianceSummary(report);

        Assert.Equal("0/0 (0% PASS)", formatted);
    }

    [Fact]
    public void WriteCompliancePlain_PreservesOrderWithinPassFailGroups()
    {
        var report = new ComplianceReport
        {
            Results =
            [
                new ConstraintResult
                {
                    Id = "z_pass",
                    Metric = "Gain",
                    Operator = ">=",
                    Expected = 1,
                    Unit = "Hz",
                    Actual = 2,
                    ActualUnit = "Hz",
                    Passed = true,
                },
                new ConstraintResult
                {
                    Id = "b_fail",
                    Metric = "Gain",
                    Operator = ">=",
                    Expected = 10,
                    Unit = "Hz",
                    Passed = false,
                    FailureReason = ConstraintResult.NoMeasurement,
                },
                new ConstraintResult
                {
                    Id = "a_pass",
                    Metric = "PhaseMargin",
                    Operator = ">=",
                    Expected = 55,
                    Unit = "deg",
                    Actual = 62,
                    ActualUnit = "deg",
                    Passed = true,
                },
                new ConstraintResult
                {
                    Id = "c_fail",
                    Metric = "Power",
                    Operator = "<=",
                    Expected = 0.2,
                    Unit = "W",
                    Passed = false,
                    FailureReason = ConstraintResult.NoMeasurement,
                },
            ],
        };
        var lines = new List<string>();

        ComplianceReportRenderer.WriteCompliancePlain(lines.Add, report);

        Assert.Equal(
            new[]
            {
                "Compliance: 2/4 (50% PASS)",
                "PASS:",
                "  z_pass: Gain >= 1 Hz (actual 2 Hz)",
                "  a_pass: PhaseMargin >= 55 deg (actual 62 deg)",
                "FAIL:",
                "  b_fail: Gain >= 10 Hz (actual missing)",
                "  c_fail: Power <= 0.2 W (actual missing)",
            },
            lines
        );
    }

    [Fact]
    public void FormatConstraintPlain_BenchError_RendersErrorActual()
    {
        var result = new ConstraintResult
        {
            Id = "c_err",
            Metric = "Pwr",
            Operator = "<=",
            Expected = 1,
            Unit = "W",
            Passed = false,
            FailureReason = ConstraintResult.BenchError,
        };

        var formatted = ComplianceReportRenderer.FormatConstraintPlain(result);

        Assert.Equal("  c_err: Pwr <= 1 W (actual error)", formatted);
    }
}
