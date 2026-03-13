using System.Collections.Generic;
using System.IO;
using Cascode.Cli.Output;
using Cascode.Language;
using Spectre.Console;

namespace Cascode.Cli.Tests.Output;

public sealed class VerifyReportRendererTests
{
    [Fact]
    public void Render_SingleCircuitSpectre_IncludesResultFooter()
    {
        var report = CreateSingleCircuitReport();

        var rendered = RenderSpectre(report);

        Assert.Contains("Result: 1/1 constraints satisfied", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SingleCircuitSpectre_IncludesCircuitHeaderAndComplianceSummary()
    {
        var report = CreateSingleCircuitReport();

        var rendered = RenderSpectre(report);

        Assert.Contains("RcLowpass", rendered, StringComparison.Ordinal);
        Assert.Contains("Compliance: 1/1 (100% PASS)", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MultiCircuitSpectre_IncludesGlobalResult()
    {
        var report = CreateMultiCircuitReport();

        var rendered = RenderSpectre(report);

        Assert.Contains(
            "Global Result: 2/2 circuits compliant",
            rendered,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Render_MultiCircuitSpectre_IncludesPerCircuitResults()
    {
        var report = CreateMultiCircuitReport();

        var rendered = RenderSpectre(report);

        Assert.Contains("Result: 1/1 constraints satisfied", rendered, StringComparison.Ordinal);
        Assert.Contains("RcLowpassA", rendered, StringComparison.Ordinal);
        Assert.Contains("RcLowpassB", rendered, StringComparison.Ordinal);
    }

    private static VerifyReport CreateSingleCircuitReport()
    {
        return new VerifyReport(
            [
                CreateCircuitReport(
                    "RcLowpass",
                    "/tmp/RcLowpass_results.json",
                    "c_bw",
                    "LowpassBandwidth"
                ),
            ],
            new VerifyGlobalReport(
                ArtifactCount: 1,
                TotalCircuits: 1,
                PassedCircuits: 1,
                FailedCircuits: 0,
                TotalConstraints: 1,
                PassedConstraints: 1
            )
        );
    }

    private static VerifyReport CreateMultiCircuitReport()
    {
        return new VerifyReport(
            [
                CreateCircuitReport(
                    "RcLowpassA",
                    "/tmp/RcLowpassA_results.json",
                    "c_bw_a",
                    "LowpassBandwidth"
                ),
                CreateCircuitReport(
                    "RcLowpassB",
                    "/tmp/RcLowpassB_results.json",
                    "c_bw_b",
                    "LowpassBandwidth"
                ),
            ],
            new VerifyGlobalReport(
                ArtifactCount: 2,
                TotalCircuits: 2,
                PassedCircuits: 2,
                FailedCircuits: 0,
                TotalConstraints: 2,
                PassedConstraints: 2
            )
        );
    }

    private static VerifyCircuitReport CreateCircuitReport(
        string circuitName,
        string artifactPath,
        string constraintId,
        string metric
    )
    {
        return new VerifyCircuitReport(
            circuitName,
            ["lp"],
            [artifactPath],
            new ComplianceReport
            {
                Results =
                [
                    new ConstraintResult
                    {
                        Id = constraintId,
                        Metric = metric,
                        Operator = ">=",
                        Expected = 1_000,
                        Unit = "Hz",
                        Actual = 1_200,
                        ActualUnit = "Hz",
                        Passed = true,
                    },
                ],
            }
        );
    }

    private static string RenderSpectre(VerifyReport report)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(writer),
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
            }
        );
        var output = new CaptureSpectreCliOutput(console);

        VerifyReportRenderer.Render(output, report);

        return writer.ToString();
    }

    private sealed class CaptureSpectreCliOutput : ICliOutput
    {
        public CaptureSpectreCliOutput(IAnsiConsole console)
        {
            Out = console;
            Err = console;
        }

        public CliOutputMode Mode => CliOutputMode.Spectre;
        public IAnsiConsole? Out { get; }
        public IAnsiConsole? Err { get; }

        public void WriteLine(string text) => Out!.WriteLine(text);

        public void WriteErrorLine(string text) => Err!.WriteLine(text);

        public void Info(string text) => Out!.MarkupLine(text);

        public void Success(string text) => Out!.MarkupLine(text);

        public void Warning(string text) => Err!.MarkupLine(text);

        public void Error(string text) => Err!.MarkupLine(text);

        public T RunWithProgress<T>(string initialStatus, Func<Action<string>, T> run) =>
            run(_ => { });

        public T RunWithMultiTaskProgress<T>(Func<IBenchProgressContext, T> run) =>
            run(new CaptureBenchProgressContext());
    }

    private sealed class CaptureBenchProgressContext : IBenchProgressContext
    {
        public IBenchTask AddTask(string description) => new CaptureBenchTask();
    }

    private sealed class CaptureBenchTask : IBenchTask
    {
        public void UpdateDescription(string description) { }

        public void StartTask() { }

        public void StopTask() { }
    }
}
