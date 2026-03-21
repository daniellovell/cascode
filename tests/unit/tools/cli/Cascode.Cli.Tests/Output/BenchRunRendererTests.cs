using Cascode.Bench;
using Cascode.Cli.Output;
using Cascode.Cli.Services;
using Cascode.Language;
using Spectre.Console;

namespace Cascode.Cli.Tests.Output;

public sealed class BenchRunRendererTests
{
    [Fact]
    public void Render_PlainMultiWithValidationErrors_ShowsErrors()
    {
        var summary = new BenchRunService.MultiCircuitBenchRunSummary(
            BenchBackendType.Ngspice,
            ".",
            Array.Empty<BenchRunService.CircuitBenchRunSummary>(),
            null,
            new ComplianceReport(),
            ValidationErrors:
            [
                "[EMIT-002] Device M1 terminal 'D' references undefined net 'OUT.p'",
                "[EMIT-002] Device M2 terminal 'S' references undefined net 'TAIL'",
            ]
        );
        var output = new CaptureCliOutput();

        BenchRunRenderer.Render(summary, verbose: false, output);

        Assert.Contains("Circuits: 0", output.Lines);
        Assert.Contains("Validation errors:", output.Lines);
        Assert.Contains(output.Lines, line => line.Contains("OUT.p", StringComparison.Ordinal));
        Assert.Contains(output.Lines, line => line.Contains("TAIL", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_PlainSingle_ShowsSharedComplianceSummary()
    {
        var summary = new BenchRunService.MultiCircuitBenchRunSummary(
            BenchBackendType.Ngspice,
            ".",
            new[]
            {
                new BenchRunService.CircuitBenchRunSummary(
                    "RcLowpass",
                    new[]
                    {
                        new BenchRunService.BenchRunBenchSummary(
                            Name: "lp",
                            Succeeded: true,
                            ExitCode: 0,
                            Error: null,
                            Stderr: null,
                            TestbenchPath: null,
                            TracePath: null,
                            ResultsPath: null
                        ),
                    },
                    new ComplianceReport
                    {
                        Results =
                        [
                            new ConstraintResult
                            {
                                Id = "c_bw",
                                Metric = "LowpassBandwidth",
                                Operator = ">=",
                                Expected = 1_000,
                                Unit = "Hz",
                                Actual = 1_200,
                                ActualUnit = "Hz",
                                Passed = true,
                            },
                        ],
                    }
                ),
            },
            null,
            new ComplianceReport()
        );
        var output = new CaptureCliOutput();

        BenchRunRenderer.Render(summary, verbose: false, output);

        Assert.Contains("Compliance: 1/1 (100% PASS)", output.Lines);
        Assert.Contains("  c_bw: LowpassBandwidth >= 1 kHz (actual 1.2 kHz)", output.Lines);
    }

    private sealed class CaptureCliOutput : ICliOutput
    {
        public CliOutputMode Mode => CliOutputMode.Plain;
        public IAnsiConsole? Out => null;
        public IAnsiConsole? Err => null;
        public List<string> Lines { get; } = new();

        public void WriteLine(string text) => Lines.Add(text);

        public void WriteErrorLine(string text) { }

        public void Info(string text) => Lines.Add(text);

        public void Success(string text) => Lines.Add(text);

        public void Warning(string text) { }

        public void Error(string text) { }

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
