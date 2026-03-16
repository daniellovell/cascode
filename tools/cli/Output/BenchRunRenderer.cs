using System;
using System.IO;
using System.Linq;
using Cascode.Bench;
using Cascode.Cli.Services;
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

        RenderTimingPlain(report, output);
    }

    private static void RenderPlain(
        BenchRunService.MultiCircuitBenchRunSummary summary,
        bool verbose,
        Action<string> writeLine
    )
    {
        if (summary.CircuitSummaries.Count == 1)
        {
            RenderPlainSingle(summary, verbose, writeLine);
        }
        else
        {
            RenderPlainMulti(summary, verbose, writeLine);
        }
    }

    private static void RenderPlainSingle(
        BenchRunService.MultiCircuitBenchRunSummary summary,
        bool verbose,
        Action<string> writeLine
    )
    {
        var circuitSummary = summary.CircuitSummaries[0];
        writeLine(
            $"Circuit: {circuitSummary.CircuitName} ({summary.Backend.ToString().ToLowerInvariant()})"
        );
        writeLine($"Artifacts: {FormatDir(summary.OutputDir, verbose)}");
        RenderValidationErrorsPlain(summary.ValidationErrors, writeLine);

        var succeeded = circuitSummary
            .Benches.Where(b => b.Succeeded)
            .Select(b => b.Name)
            .ToArray();
        var failed = circuitSummary.Benches.Where(b => !b.Succeeded).Select(b => b.Name).ToArray();

        if (succeeded.Length > 0)
        {
            writeLine($"Ran: {string.Join(", ", succeeded)}");
        }

        if (failed.Length > 0)
        {
            writeLine($"Simulation: FAIL ({string.Join(", ", failed)})");
        }

        ComplianceReportRenderer.WriteCompliancePlain(writeLine, circuitSummary.Compliance);
    }

    private static void RenderPlainMulti(
        BenchRunService.MultiCircuitBenchRunSummary summary,
        bool verbose,
        Action<string> writeLine
    )
    {
        writeLine($"Backend: {summary.Backend.ToString().ToLowerInvariant()}");
        writeLine($"Artifacts: {FormatDir(summary.OutputDir, verbose)}");
        writeLine($"Circuits: {summary.CircuitSummaries.Count}");
        RenderValidationErrorsPlain(summary.ValidationErrors, writeLine);
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

            writeLine(
                $"  Compliance: {ComplianceReportRenderer.FormatComplianceSummary(circuitSummary.Compliance)}"
            );

            if (verbose)
            {
                foreach (var result in circuitSummary.Compliance.Results.Where(r => !r.Passed))
                {
                    var formatted = ComplianceReportRenderer
                        .FormatConstraintPlain(result)
                        .TrimStart();
                    writeLine($"    FAIL {formatted}");
                }
            }

            writeLine("");
        }

        writeLine("=== GLOBAL SUMMARY ===");
        writeLine(
            $"Total Benches: {summary.TotalBenchesRun} ({summary.TotalBenchesSucceeded} passed, {summary.TotalBenchesFailed} failed)"
        );

        writeLine(
            $"Global Compliance: {ComplianceReportRenderer.FormatComplianceSummary(summary.GlobalCompliance)}"
        );
    }

    private static void RenderSpectre(
        BenchRunService.MultiCircuitBenchRunSummary summary,
        bool verbose,
        IAnsiConsole console
    )
    {
        if (summary.CircuitSummaries.Count == 1)
        {
            RenderSpectreSingle(summary, verbose, console);
        }
        else
        {
            RenderSpectreMulti(summary, console);
        }
    }

    private static void RenderSpectreSingle(
        BenchRunService.MultiCircuitBenchRunSummary summary,
        bool verbose,
        IAnsiConsole console
    )
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
        RenderValidationErrorsSpectre(summary.ValidationErrors, console);

        var succeeded = circuitSummary
            .Benches.Where(b => b.Succeeded)
            .Select(b => b.Name)
            .ToArray();
        if (succeeded.Length > 0)
        {
            console.MarkupLine($"[grey]Ran:[/] {Markup.Escape(string.Join(", ", succeeded))}");
        }

        var failed = circuitSummary.Benches.Where(b => !b.Succeeded).Select(b => b.Name).ToArray();
        if (failed.Length > 0)
        {
            console.MarkupLine(
                $"[red]Simulation: FAIL[/] ({Markup.Escape(string.Join(", ", failed))})"
            );
        }

        console.WriteLine();
        ComplianceReportRenderer.RenderComplianceTable(circuitSummary.Compliance, console);
        if (verbose)
        {
            ComplianceReportRenderer.RenderUncheckedConstraints(circuitSummary.Compliance, console);
        }
    }

    private static void RenderSpectreMulti(
        BenchRunService.MultiCircuitBenchRunSummary summary,
        IAnsiConsole console
    )
    {
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
        RenderValidationErrorsSpectre(summary.ValidationErrors, console);
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
        console.MarkupLine(
            $"[grey]Global:[/] {Markup.Escape(ComplianceReportRenderer.FormatComplianceSummary(summary.GlobalCompliance))}"
        );
    }

    private static void RenderValidationErrorsPlain(
        IReadOnlyList<string>? validationErrors,
        Action<string> writeLine
    )
    {
        if (validationErrors is null || validationErrors.Count == 0)
        {
            return;
        }

        writeLine("Validation errors:");
        foreach (var error in validationErrors)
        {
            writeLine($"  - {error}");
        }
    }

    private static void RenderValidationErrorsSpectre(
        IReadOnlyList<string>? validationErrors,
        IAnsiConsole console
    )
    {
        if (validationErrors is null || validationErrors.Count == 0)
        {
            return;
        }

        console.MarkupLine("[red]Validation errors:[/]");
        foreach (var error in validationErrors)
        {
            console.MarkupLine($"[red]-[/] {Markup.Escape(error)}");
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

    private static void RenderTimingPlain(BenchRunTimingReport report, ICliOutput output)
    {
        output.WriteErrorLine("");
        output.WriteErrorLine("Timing:");
        output.WriteErrorLine($"  Total: {FormatDuration(report.Total)}");

        if (report.Steps.Count > 0)
        {
            output.WriteErrorLine("  Steps:");
            foreach (var step in report.Steps)
            {
                output.WriteErrorLine($"    {step.Name}: {FormatDuration(step.Elapsed)}");
            }
        }

        if (report.Benches.Count > 0)
        {
            output.WriteErrorLine("  Benches:");
            foreach (var b in report.Benches.OrderByDescending(b => b.Total))
            {
                output.WriteErrorLine(
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
