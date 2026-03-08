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
                    Expected = 1_000,
                    Unit = "Hz",
                    Actual = 1_200,
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
                "  c_pass: Gain >= 1 kHz (actual 1.2 kHz)",
                "FAIL:",
                "  c_fail: Gain >= 10 Hz (actual missing)",
            },
            lines
        );
    }

    [Fact]
    public void FormatConstraintPlain_UsesValueFormatterSemantics()
    {
        var result = new ConstraintResult
        {
            Id = "c_slew",
            Metric = "SlewRate",
            Node = "OUT",
            Operator = ">=",
            Expected = 1e-6,
            Unit = "A",
            Actual = 2.5e-6,
            ActualUnit = "A",
            Passed = true,
        };

        var formatted = ComplianceReportRenderer.FormatConstraintPlain(result);

        Assert.Equal("  c_slew: SlewRate@OUT >= 1 uA (actual 2.5 uA)", formatted);
    }

    [Fact]
    public void WriteCompliancePlain_IncludesUncheckedConstraints()
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
                    Expected = 1_000,
                    Unit = "Hz",
                    Actual = 1_200,
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
            UncheckedByBench = new Dictionary<string, List<UncheckedConstraint>>
            {
                ["tran_bench"] =
                [
                    new UncheckedConstraint { Id = "c_swing", Metric = "OutputSwing" },
                    new UncheckedConstraint { Id = "c_settle", Metric = "SettlingTime" },
                ],
            },
        };
        var lines = new List<string>();

        ComplianceReportRenderer.WriteCompliancePlain(lines.Add, report);

        Assert.Equal(
            new[]
            {
                "Compliance: 1/2 (50% PASS)",
                "PASS:",
                "  c_pass: Gain >= 1 kHz (actual 1.2 kHz)",
                "FAIL:",
                "  c_fail: Gain >= 10 Hz (actual missing)",
                "UNCHECKED:",
                "  c_swing: OutputSwing (unchecked)",
                "  c_settle: SettlingTime (unchecked)",
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
