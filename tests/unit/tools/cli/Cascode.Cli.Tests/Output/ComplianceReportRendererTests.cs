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
    public void WriteCompliancePlain_UsesDeterministicOrdering()
    {
        var report = new ComplianceReport
        {
            Results =
            [
                new ConstraintResult
                {
                    Id = "c_pass",
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
                    Id = "c_fail",
                    Metric = "Gain",
                    Operator = ">=",
                    Expected = 10,
                    Unit = "Hz",
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
                "Compliance: 1/2 (50% PASS)",
                "PASS:",
                "  c_pass: Gain >= 1 Hz (actual 2 Hz)",
                "FAIL:",
                "  c_fail: Gain >= 10 Hz (actual missing)",
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
