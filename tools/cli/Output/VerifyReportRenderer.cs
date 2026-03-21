using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language;
using Spectre.Console;

namespace Cascode.Cli.Output;

internal sealed record VerifyCircuitReport(
    string CircuitName,
    IReadOnlyList<string> Benches,
    IReadOnlyList<string> ArtifactPaths,
    ComplianceReport Compliance
);

internal sealed record VerifyGlobalReport(
    int ArtifactCount,
    int TotalCircuits,
    int PassedCircuits,
    int FailedCircuits,
    int TotalConstraints,
    int PassedConstraints
);

internal sealed record VerifyReport(
    IReadOnlyList<VerifyCircuitReport> Circuits,
    VerifyGlobalReport Global
);

internal static class VerifyReportRenderer
{
    public static void Render(ICliOutput output, VerifyReport summary)
    {
        if (output.Mode == CliOutputMode.Spectre && output.Out is not null)
        {
            RenderSpectre(summary, output.Out);
            return;
        }

        RenderPlain(summary, output.WriteLine);
    }

    private static void RenderSpectre(VerifyReport summary, IAnsiConsole console)
    {
        if (summary.Circuits.Count == 1)
        {
            RenderSpectreCircuit(summary.Circuits[0], console);
            return;
        }

        console.Write(new Rule("[bold]Verify[/]") { Style = Style.Parse("grey") });
        console.MarkupLine($"[grey]Artifacts:[/] {summary.Global.ArtifactCount}");
        console.MarkupLine($"[grey]Circuits:[/] {summary.Global.TotalCircuits}");
        console.MarkupLine(
            $"[grey]Global Compliance:[/] {Markup.Escape(FormatComplianceSummary(summary.Global.PassedConstraints, summary.Global.TotalConstraints))}"
        );
        console.MarkupLine(
            $"[grey]Global Result:[/] {summary.Global.PassedCircuits}/{summary.Global.TotalCircuits} circuits compliant"
        );
        console.WriteLine();

        foreach (var circuit in summary.Circuits)
        {
            RenderSpectreCircuit(circuit, console);
            console.WriteLine();
        }
    }

    private static void RenderSpectreCircuit(VerifyCircuitReport circuit, IAnsiConsole console)
    {
        console.Write(
            new Rule($"[bold]{Markup.Escape(circuit.CircuitName)}[/]")
            {
                Style = Style.Parse("grey"),
            }
        );
        if (circuit.Benches.Count > 0)
        {
            console.MarkupLine(
                $"[grey]Benches:[/] {Markup.Escape(string.Join(", ", circuit.Benches))}"
            );
        }

        WriteArtifactInfoSpectre(circuit, console);

        console.WriteLine();
        ComplianceReportRenderer.RenderComplianceTable(circuit.Compliance, console);
        ComplianceReportRenderer.RenderUncheckedConstraints(circuit.Compliance, console);
        console.MarkupLine(
            $"[grey]Result:[/] {Markup.Escape(FormatCircuitResultSummary(circuit.Compliance))}"
        );
    }

    private static void RenderPlain(VerifyReport summary, Action<string> writeLine)
    {
        if (summary.Circuits.Count == 1)
        {
            RenderPlainSingle(summary.Circuits[0], writeLine);
            return;
        }

        writeLine($"Artifacts: {summary.Global.ArtifactCount}");
        writeLine($"Circuits: {summary.Global.TotalCircuits}");
        writeLine("Circuit Compliance:");
        foreach (var circuit in summary.Circuits)
        {
            var status = circuit.Compliance.FailedCount == 0 ? "PASS" : "FAIL";
            writeLine(
                $"  {circuit.CircuitName}: {status} ({ComplianceReportRenderer.FormatComplianceSummary(circuit.Compliance)})"
            );
        }

        writeLine(
            $"Global Compliance: {FormatComplianceSummary(summary.Global.PassedConstraints, summary.Global.TotalConstraints)}"
        );
        writeLine(
            $"Global Result: {summary.Global.PassedCircuits}/{summary.Global.TotalCircuits} circuits compliant"
        );
        writeLine(string.Empty);

        foreach (var circuit in summary.Circuits)
        {
            RenderPlainCircuit(circuit, writeLine);
        }
    }

    private static void RenderPlainSingle(VerifyCircuitReport circuit, Action<string> writeLine)
    {
        writeLine($"Circuit: {circuit.CircuitName}");
        WriteBenchAndArtifactInfoPlain(circuit, writeLine, indent: string.Empty);
        ComplianceReportRenderer.WriteCompliancePlain(writeLine, circuit.Compliance);
        writeLine($"Result: {FormatCircuitResultSummary(circuit.Compliance)}");
    }

    private static void RenderPlainCircuit(VerifyCircuitReport circuit, Action<string> writeLine)
    {
        writeLine($"=== {circuit.CircuitName} ===");
        WriteBenchAndArtifactInfoPlain(circuit, writeLine, indent: "  ");
        ComplianceReportRenderer.WriteCompliancePlain(
            line => writeLine($"  {line}"),
            circuit.Compliance
        );
        writeLine($"  Result: {FormatCircuitResultSummary(circuit.Compliance)}");
        writeLine(string.Empty);
    }

    private static void WriteArtifactInfoSpectre(VerifyCircuitReport circuit, IAnsiConsole console)
    {
        if (circuit.ArtifactPaths.Count == 1)
        {
            console.MarkupLine($"[grey]Artifact:[/] {Markup.Escape(circuit.ArtifactPaths[0])}");
            return;
        }

        console.MarkupLine($"[grey]Artifacts:[/] {circuit.ArtifactPaths.Count}");
        foreach (var artifactPath in circuit.ArtifactPaths)
        {
            console.MarkupLine($"  [grey]-[/] {Markup.Escape(artifactPath)}");
        }
    }

    private static void WriteBenchAndArtifactInfoPlain(
        VerifyCircuitReport circuit,
        Action<string> writeLine,
        string indent
    )
    {
        if (circuit.Benches.Count > 0)
        {
            writeLine($"{indent}Benches: {string.Join(", ", circuit.Benches)}");
        }

        if (circuit.ArtifactPaths.Count == 1)
        {
            writeLine($"{indent}Artifact: {circuit.ArtifactPaths[0]}");
            return;
        }

        writeLine($"{indent}Artifacts: {circuit.ArtifactPaths.Count}");
        foreach (var artifactPath in circuit.ArtifactPaths)
        {
            writeLine($"{indent}  - {artifactPath}");
        }
    }

    private static string FormatComplianceSummary(int passed, int total)
    {
        var passPercentage = total > 0 ? (int)Math.Round(100.0 * passed / total) : 0;
        return $"{passed}/{total} ({passPercentage}% PASS)";
    }

    private static string FormatCircuitResultSummary(ComplianceReport compliance)
    {
        var summary = $"{compliance.PassedCount}/{compliance.TotalCount} constraints satisfied";
        return compliance.UncheckedCount > 0
            ? $"{summary} ({compliance.UncheckedCount} unchecked)"
            : summary;
    }
}
