using System;
using System.Globalization;
using System.Linq;
using Cascode.Language;
using Spectre.Console;

namespace Cascode.Cli.Output;

internal static class ComplianceReportRenderer
{
    public static string FormatComplianceSummary(ComplianceReport compliance)
    {
        var passPercentage =
            compliance.TotalCount > 0
                ? (int)Math.Round(100.0 * compliance.PassedCount / compliance.TotalCount)
                : 0;
        return $"{compliance.PassedCount}/{compliance.TotalCount} ({passPercentage}% PASS)";
    }

    public static void WriteCompliancePlain(Action<string> writeLine, ComplianceReport compliance)
    {
        writeLine($"Compliance: {FormatComplianceSummary(compliance)}");

        var passedConstraints = compliance.Results.Where(r => r.Passed).ToArray();
        var failedConstraints = compliance.Results.Where(r => !r.Passed).ToArray();

        if (passedConstraints.Length > 0)
        {
            writeLine("PASS:");
            foreach (var pass in passedConstraints)
            {
                writeLine(FormatConstraintPlain(pass));
            }
        }

        if (failedConstraints.Length > 0)
        {
            writeLine("FAIL:");
            foreach (var failure in failedConstraints)
            {
                writeLine(FormatConstraintPlain(failure));
            }
        }
    }

    public static string FormatConstraintPlain(ConstraintResult result)
    {
        var where = string.IsNullOrWhiteSpace(result.Node)
            ? result.Metric
            : $"{result.Metric}@{result.Node}";
        var expected = $"{result.Operator} {FormatNumber(result.Expected)} {result.Unit}".TrimEnd();
        var actual = result.Actual is null
            ? result.FailureReason == ConstraintResult.BenchError
                ? "error"
                : "missing"
            : $"{FormatNumber(result.Actual.Value)} {result.ActualUnit ?? result.Unit}".TrimEnd();
        return $"  {result.Id}: {where} {expected} (actual {actual})";
    }

    public static void RenderComplianceTable(ComplianceReport compliance, IAnsiConsole console)
    {
        console.MarkupLine(
            $"[grey]Compliance:[/] {Markup.Escape(FormatComplianceSummary(compliance))}"
        );

        if (compliance.Results.Count == 0)
        {
            return;
        }

        console.WriteLine();
        var table = new Table().Border(TableBorder.Simple);
        table.AddColumn(new TableColumn("[grey]Status[/]").LeftAligned().NoWrap().Width(6));
        table.AddColumn(new TableColumn("[grey]Id[/]").LeftAligned().NoWrap().Width(14));
        table.AddColumn(new TableColumn("[grey]Metric[/]").LeftAligned().Width(48));
        table.AddColumn(new TableColumn("[grey]Expected[/]").LeftAligned().NoWrap().Width(18));
        table.AddColumn(new TableColumn("[grey]Actual[/]").LeftAligned().NoWrap().Width(18));

        foreach (var r in compliance.Results)
        {
            var status = r.Passed ? "[green]PASS[/]" : "[red]FAIL[/]";
            var where = string.IsNullOrWhiteSpace(r.Node) ? r.Metric : $"{r.Metric}@{r.Node}";
            var expected = $"{r.Operator} {FormatNumber(r.Expected)} {r.Unit}".TrimEnd();
            var actual = r.Actual is null
                ? r.FailureReason == ConstraintResult.BenchError
                    ? "error"
                    : "missing"
                : $"{FormatNumber(r.Actual.Value)} {r.ActualUnit ?? r.Unit}".TrimEnd();

            table.AddRow(
                status,
                Markup.Escape(r.Id),
                Markup.Escape(where),
                Markup.Escape(expected),
                Markup.Escape(actual)
            );
        }

        SpectreTableSizing.ApplyStandardWidth(console, table);
        console.Write(table);
    }

    public static void RenderUncheckedConstraints(ComplianceReport compliance, IAnsiConsole console)
    {
        if (compliance.UncheckedByBench.Count == 0)
        {
            return;
        }

        console.WriteLine();
        console.MarkupLine("[yellow]Unchecked constraints:[/]");
        foreach (var (bench, unchecked_) in compliance.UncheckedByBench)
        {
            if (unchecked_.Count == 0)
            {
                continue;
            }

            console.MarkupLine($"[grey]{Markup.Escape(bench)}[/]");
            foreach (var c in unchecked_)
            {
                console.MarkupLine($"  [grey]{Markup.Escape(c.Id)}[/] {Markup.Escape(c.Metric)}");
            }
        }
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("G6", CultureInfo.InvariantCulture);
    }
}
