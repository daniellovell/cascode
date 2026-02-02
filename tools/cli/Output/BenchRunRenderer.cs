using System;
using System.IO;
using System.Linq;
using Cascode.Cli.Services;
using Cascode.Language;
using Spectre.Console;

namespace Cascode.Cli.Output;

internal static class BenchRunRenderer
{
    public static void Render(
        BenchRunService.MultiCircuitBenchRunSummary summary,
        bool verbose,
        ICliOutput output
    )
    {
        if (output.Mode == CliOutputMode.Spectre && output.Out is not null)
        {
            RenderSpectre(summary, verbose, output.Out);
            return;
        }

        RenderPlain(summary, verbose, output.WriteLine);
    }

    public static void RenderTiming(BenchRunTimingReport report, bool verbose, ICliOutput output)
    {
        if (!verbose)
        {
            return;
        }

        if (output.Mode == CliOutputMode.Spectre && output.Err is not null)
        {
            RenderTimingSpectre(report, output.Err);
            return;
        }

        RenderTimingPlain(report, Console.Error);
    }

    private static void RenderPlain(
        BenchRunService.MultiCircuitBenchRunSummary summary,
        bool verbose,
        Action<string> writeLine
    )
    {
        // Single circuit: use simpler format matching old behavior
        if (summary.CircuitSummaries.Count == 1)
        {
            var circuitSummary = summary.CircuitSummaries[0];
            writeLine(
                $"Circuit: {circuitSummary.CircuitName} ({summary.Backend.ToString().ToLowerInvariant()})"
            );
            writeLine($"Artifacts: {FormatDir(summary.OutputDir, verbose)}");

            var succeeded = circuitSummary
                .Benches.Where(b => b.Succeeded)
                .Select(b => b.Name)
                .ToArray();
            var failed = circuitSummary
                .Benches.Where(b => !b.Succeeded)
                .Select(b => b.Name)
                .ToArray();

            if (succeeded.Length > 0)
            {
                writeLine($"Ran: {string.Join(", ", succeeded)}");
            }

            if (failed.Length > 0)
            {
                writeLine($"Simulation: FAIL ({string.Join(", ", failed)})");
            }

            WriteCompliancePlain(writeLine, circuitSummary.Compliance);
            return;
        }

        // Multiple circuits: use multi-circuit format
        writeLine($"Backend: {summary.Backend.ToString().ToLowerInvariant()}");
        writeLine($"Artifacts: {FormatDir(summary.OutputDir, verbose)}");
        writeLine($"Circuits: {summary.CircuitSummaries.Count}");
        writeLine("");

        foreach (var circuitSummary in summary.CircuitSummaries)
        {
            writeLine($"=== {circuitSummary.CircuitName} ===");

            var succeeded = circuitSummary
                .Benches.Where(b => b.Succeeded)
                .Select(b => b.Name)
                .ToArray();
            var failed = circuitSummary
                .Benches.Where(b => !b.Succeeded)
                .Select(b => b.Name)
                .ToArray();

            if (succeeded.Length > 0)
            {
                writeLine($"  Ran: {string.Join(", ", succeeded)}");
            }

            if (failed.Length > 0)
            {
                writeLine($"  FAILED: {string.Join(", ", failed)}");
            }

            var compliance = circuitSummary.Compliance;
            var passPercentage =
                compliance.TotalCount > 0
                    ? (int)Math.Round(100.0 * compliance.PassedCount / compliance.TotalCount)
                    : 0;
            writeLine(
                $"  Compliance: {compliance.PassedCount}/{compliance.TotalCount} ({passPercentage}% PASS)"
            );

            if (verbose)
            {
                foreach (var result in compliance.Results.Where(r => !r.Passed))
                {
                    var formatted = FormatConstraintPlain(result).TrimStart();
                    writeLine($"    FAIL {formatted}");
                }
            }

            writeLine("");
        }

        // Global summary
        writeLine("=== GLOBAL SUMMARY ===");
        writeLine(
            $"Total Benches: {summary.TotalBenchesRun} ({summary.TotalBenchesSucceeded} passed, {summary.TotalBenchesFailed} failed)"
        );

        var globalCompliance = summary.GlobalCompliance;
        var globalPassPct =
            globalCompliance.TotalCount > 0
                ? (int)
                    Math.Round(100.0 * globalCompliance.PassedCount / globalCompliance.TotalCount)
                : 0;
        writeLine(
            $"Global Compliance: {globalCompliance.PassedCount}/{globalCompliance.TotalCount} ({globalPassPct}% PASS)"
        );
    }

    private static void WriteCompliancePlain(Action<string> writeLine, ComplianceReport compliance)
    {
        var passPercentage =
            compliance.TotalCount > 0
                ? (int)Math.Round(100.0 * compliance.PassedCount / compliance.TotalCount)
                : 0;
        writeLine(
            $"Compliance: {compliance.PassedCount}/{compliance.TotalCount} ({passPercentage}% PASS)"
        );

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

    private static void RenderSpectre(
        BenchRunService.MultiCircuitBenchRunSummary summary,
        bool verbose,
        IAnsiConsole console
    )
    {
        // Keep the measurement report first-class: print it cleanly on stdout.
        if (summary.CircuitSummaries.Count == 1)
        {
            var circuitSummary = summary.CircuitSummaries[0];
            console.Write(
                new Rule(
                    $"[bold]{Markup.Escape(circuitSummary.CircuitName)}[/] [grey]({summary.Backend.ToString().ToLowerInvariant()})[/]"
                )
                {
                    Style = Style.Parse("grey"),
                }
            );

            console.MarkupLine(
                $"[grey]Artifacts:[/] {Markup.Escape(Path.GetFullPath(summary.OutputDir))}"
            );

            var succeeded = circuitSummary
                .Benches.Where(b => b.Succeeded)
                .Select(b => b.Name)
                .ToArray();
            if (succeeded.Length > 0)
            {
                console.MarkupLine($"[grey]Ran:[/] {Markup.Escape(string.Join(", ", succeeded))}");
            }

            var failed = circuitSummary
                .Benches.Where(b => !b.Succeeded)
                .Select(b => b.Name)
                .ToArray();
            if (failed.Length > 0)
            {
                console.MarkupLine(
                    $"[red]Simulation: FAIL[/] ({Markup.Escape(string.Join(", ", failed))})"
                );
            }

            console.WriteLine();
            RenderComplianceTable(circuitSummary.Compliance, console);
            if (verbose)
            {
                RenderUncheckedConstraints(circuitSummary.Compliance, console);
            }

            return;
        }

        console.Write(
            new Rule(
                $"[bold]Bench Run[/] [grey]({summary.Backend.ToString().ToLowerInvariant()})[/]"
            )
            {
                Style = Style.Parse("grey"),
            }
        );
        console.MarkupLine(
            $"[grey]Artifacts:[/] {Markup.Escape(Path.GetFullPath(summary.OutputDir))}"
        );
        console.MarkupLine($"[grey]Circuits:[/] {summary.CircuitSummaries.Count}");
        console.WriteLine();

        var circuitsTable = new Table().Border(TableBorder.Simple);
        circuitsTable.AddColumn(new TableColumn("[grey]Circuit[/]").LeftAligned().NoWrap());
        circuitsTable.AddColumn(new TableColumn("[grey]Benches[/]").RightAligned().NoWrap());
        circuitsTable.AddColumn(new TableColumn("[grey]Compliance[/]").RightAligned().NoWrap());
        foreach (var cs in summary.CircuitSummaries)
        {
            var benches = cs.Benches.Count;
            var compliance = cs.Compliance;
            var pct =
                compliance.TotalCount > 0
                    ? (int)Math.Round(100.0 * compliance.PassedCount / compliance.TotalCount)
                    : 0;
            var complianceText = $"{compliance.PassedCount}/{compliance.TotalCount} ({pct}%)";
            circuitsTable.AddRow(
                Markup.Escape(cs.CircuitName),
                benches.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Markup.Escape(complianceText)
            );
        }
        SpectreTableSizing.ApplyStandardWidth(console, circuitsTable);
        console.Write(circuitsTable);

        console.WriteLine();
        var global = summary.GlobalCompliance;
        var globalPct =
            global.TotalCount > 0
                ? (int)Math.Round(100.0 * global.PassedCount / global.TotalCount)
                : 0;
        console.MarkupLine(
            $"[grey]Global:[/] {global.PassedCount}/{global.TotalCount} ({globalPct}% PASS)"
        );
    }

    private static void RenderComplianceTable(ComplianceReport compliance, IAnsiConsole console)
    {
        var passPct =
            compliance.TotalCount > 0
                ? (int)Math.Round(100.0 * compliance.PassedCount / compliance.TotalCount)
                : 0;
        console.MarkupLine(
            $"[grey]Compliance:[/] {compliance.PassedCount}/{compliance.TotalCount} ({passPct}% PASS)"
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
                ? r.Message.StartsWith("Measurement error:", StringComparison.OrdinalIgnoreCase)
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

    private static void RenderUncheckedConstraints(
        ComplianceReport compliance,
        IAnsiConsole console
    )
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

    private static void RenderTimingSpectre(BenchRunTimingReport report, IAnsiConsole console)
    {
        console.WriteLine();
        console.Write(new Rule("[bold]Timing[/]") { Style = Style.Parse("grey") });

        var steps = new Table().Border(TableBorder.Simple);
        steps.AddColumn(new TableColumn("[grey]Step[/]").LeftAligned());
        steps.AddColumn(new TableColumn("[grey]Time[/]").RightAligned().NoWrap());
        steps.AddRow("Total", FormatDuration(report.Total));
        foreach (var s in report.Steps)
        {
            steps.AddRow(Markup.Escape(s.Name), Markup.Escape(FormatDuration(s.Elapsed)));
        }
        SpectreTableSizing.ApplyStandardWidth(console, steps);
        console.Write(steps);

        if (report.Benches.Count == 0)
        {
            return;
        }

        console.WriteLine();
        var benches = new Table().Border(TableBorder.Simple);
        benches.AddColumn(new TableColumn("[grey]Bench[/]").LeftAligned());
        benches.AddColumn(new TableColumn("[grey]Total[/]").RightAligned().NoWrap());
        benches.AddColumn(new TableColumn("[grey]Sim[/]").RightAligned().NoWrap());
        benches.AddColumn(new TableColumn("[grey]Parse[/]").RightAligned().NoWrap());
        benches.AddColumn(new TableColumn("[grey]Eval[/]").RightAligned().NoWrap());
        benches.AddColumn(new TableColumn("[grey]Write[/]").RightAligned().NoWrap());

        foreach (var b in report.Benches.OrderByDescending(b => b.Total))
        {
            benches.AddRow(
                Markup.Escape($"{b.CircuitName}/{b.BenchName}"),
                Markup.Escape(FormatDuration(b.Total)),
                Markup.Escape(FormatDuration(b.Simulation)),
                Markup.Escape(FormatDuration(b.ParseOutputs)),
                Markup.Escape(FormatDuration(b.EvaluateMeasurements)),
                Markup.Escape(FormatDuration(b.WriteArtifacts))
            );
        }
        SpectreTableSizing.ApplyStandardWidth(console, benches);
        console.Write(benches);
    }

    private static void RenderTimingPlain(BenchRunTimingReport report, TextWriter writer)
    {
        writer.WriteLine("");
        writer.WriteLine("Timing:");
        writer.WriteLine($"  Total: {FormatDuration(report.Total)}");

        if (report.Steps.Count > 0)
        {
            writer.WriteLine("  Steps:");
            foreach (var step in report.Steps)
            {
                writer.WriteLine($"    {step.Name}: {FormatDuration(step.Elapsed)}");
            }
        }

        if (report.Benches.Count > 0)
        {
            writer.WriteLine("  Benches:");
            foreach (var b in report.Benches.OrderByDescending(b => b.Total))
            {
                writer.WriteLine(
                    $"    {b.CircuitName}/{b.BenchName}: total {FormatDuration(b.Total)} (sim {FormatDuration(b.Simulation)}, parse {FormatDuration(b.ParseOutputs)}, eval {FormatDuration(b.EvaluateMeasurements)}, write {FormatDuration(b.WriteArtifacts)})"
                );
            }
        }
    }

    private static string FormatDir(string path, bool verbose)
    {
        _ = verbose;
        return Path.GetFullPath(path);
    }

    private static string FormatConstraintPlain(ConstraintResult result)
    {
        var where = string.IsNullOrWhiteSpace(result.Node)
            ? result.Metric
            : $"{result.Metric}@{result.Node}";
        var expected = $"{result.Operator} {FormatNumber(result.Expected)} {result.Unit}".TrimEnd();
        var actual = result.Actual is null
            ? result.Message.StartsWith("Measurement error:", StringComparison.OrdinalIgnoreCase)
                ? "error"
                : "missing"
            : $"{FormatNumber(result.Actual.Value)} {result.ActualUnit ?? result.Unit}".TrimEnd();
        return $"  {result.Id}: {where} {expected} (actual {actual})";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
        {
            return elapsed.TotalMilliseconds.ToString(
                    "F0",
                    System.Globalization.CultureInfo.InvariantCulture
                ) + "ms";
        }

        if (elapsed.TotalSeconds < 10)
        {
            return elapsed.TotalSeconds.ToString(
                    "F1",
                    System.Globalization.CultureInfo.InvariantCulture
                ) + "s";
        }

        if (elapsed.TotalMinutes < 1)
        {
            return elapsed.TotalSeconds.ToString(
                    "F0",
                    System.Globalization.CultureInfo.InvariantCulture
                ) + "s";
        }

        if (elapsed.TotalHours < 1)
        {
            return elapsed.ToString("mm\\:ss", System.Globalization.CultureInfo.InvariantCulture);
        }

        return elapsed.ToString("hh\\:mm\\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}
