using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language.BenchRuntime.Netlist;

namespace Cascode.Language.BenchRuntime;

internal static class BenchAnalysisCompiler
{
    public static IReadOnlyList<BenchPlanAnalysis> Compile(
        BenchDefinition bench,
        BenchMeasurementRunner evalRunner,
        BenchNetlist netlist
    )
    {
        ArgumentNullException.ThrowIfNull(bench);
        ArgumentNullException.ThrowIfNull(evalRunner);
        ArgumentNullException.ThrowIfNull(netlist);

        var analyses = new List<BenchPlanAnalysis>();
        var noiseInputSource = FindNoiseInputSource(netlist);

        foreach (var a in bench.Analyses)
        {
            var space = "dec";
            if (a.Parameters.TryGetValue("space", out var spaceExpr))
            {
                var raw = evalRunner.EvaluateExpressionForPlan(spaceExpr);
                if (raw is BenchSymbol sym && !string.IsNullOrWhiteSpace(sym.Name))
                {
                    space = sym.Name.Trim().ToLowerInvariant();
                }
            }

            var samples = 100;
            if (a.Parameters.TryGetValue("samples", out var samplesExpr))
            {
                var raw = evalRunner.EvaluateExpressionForPlan(samplesExpr);
                if (raw is BenchNumber n)
                {
                    samples = (int)n.Value;
                }
            }

            if (a.Type == BenchValueType.ACAnalysis)
            {
                if (!a.Parameters.TryGetValue("start", out var startExpr))
                {
                    throw new InvalidOperationException(
                        $"ACAnalysis '{a.Name}' missing required parameter 'start'."
                    );
                }
                if (!a.Parameters.TryGetValue("stop", out var stopExpr))
                {
                    throw new InvalidOperationException(
                        $"ACAnalysis '{a.Name}' missing required parameter 'stop'."
                    );
                }

                var startV = evalRunner.EvaluateExpressionForPlan(startExpr) as BenchNumber;
                var stopV = evalRunner.EvaluateExpressionForPlan(stopExpr) as BenchNumber;
                if (startV is null)
                {
                    throw new InvalidOperationException(
                        $"ACAnalysis '{a.Name}' start did not evaluate to a number."
                    );
                }
                if (stopV is null)
                {
                    throw new InvalidOperationException(
                        $"ACAnalysis '{a.Name}' stop did not evaluate to a number."
                    );
                }
                if (startV.Kind != BenchNumericKind.FrequencyHz)
                {
                    throw new InvalidOperationException(
                        $"ACAnalysis '{a.Name}' start/stop must be Frequency values."
                    );
                }

                analyses.Add(
                    new BenchPlanAnalysis(a.Type, a.Name, space, samples, startV.Value, stopV.Value)
                );
            }

            if (a.Type == BenchValueType.NoiseAnalysis)
            {
                if (!a.Parameters.TryGetValue("start", out var startExpr))
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' missing required parameter 'start'."
                    );
                }
                if (!a.Parameters.TryGetValue("stop", out var stopExpr))
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' missing required parameter 'stop'."
                    );
                }
                if (!a.Parameters.TryGetValue("output", out var outputExpr))
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' missing required parameter 'output'."
                    );
                }
                if (noiseInputSource is null)
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' requires at least one VAC source in the bench/binding fill to use as the noise input source."
                    );
                }

                var startV = evalRunner.EvaluateExpressionForPlan(startExpr) as BenchNumber;
                var stopV = evalRunner.EvaluateExpressionForPlan(stopExpr) as BenchNumber;
                if (startV is null)
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' start did not evaluate to a number."
                    );
                }
                if (stopV is null)
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' stop did not evaluate to a number."
                    );
                }

                var output =
                    evalRunner.EvaluateExpressionForPlan(outputExpr) as BenchTerminalRef
                    ?? throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' output did not evaluate to a terminal."
                    );
                if (startV.Kind != BenchNumericKind.FrequencyHz)
                {
                    throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' start/stop must be Frequency values."
                    );
                }

                analyses.Add(
                    new BenchPlanAnalysis(
                        a.Type,
                        a.Name,
                        space,
                        samples,
                        startV.Value,
                        stopV.Value,
                        OutputTerminal: output,
                        NoiseInputSource: noiseInputSource
                    )
                );
            }

            if (a.Type == BenchValueType.TranAnalysis)
            {
                if (!a.Parameters.TryGetValue("stop", out var stopExpr))
                {
                    throw new InvalidOperationException(
                        $"TranAnalysis '{a.Name}' missing required parameter 'stop'."
                    );
                }
                if (!a.Parameters.TryGetValue("step", out var stepExpr))
                {
                    throw new InvalidOperationException(
                        $"TranAnalysis '{a.Name}' missing required parameter 'step'."
                    );
                }

                var stopV = evalRunner.EvaluateExpressionForPlan(stopExpr) as BenchNumber;
                var stepV = evalRunner.EvaluateExpressionForPlan(stepExpr) as BenchNumber;

                if (stopV is null)
                {
                    throw new InvalidOperationException(
                        $"TranAnalysis '{a.Name}' stop did not evaluate to a number."
                    );
                }
                if (stepV is null)
                {
                    throw new InvalidOperationException(
                        $"TranAnalysis '{a.Name}' step did not evaluate to a number."
                    );
                }

                if (stopV.Kind != BenchNumericKind.TimeS)
                {
                    throw new InvalidOperationException(
                        $"TranAnalysis '{a.Name}' stop must be a Time value."
                    );
                }
                if (stepV.Kind != BenchNumericKind.TimeS)
                {
                    throw new InvalidOperationException(
                        $"TranAnalysis '{a.Name}' step must be a Time value."
                    );
                }

                analyses.Add(
                    new BenchPlanAnalysis(a.Type, a.Name, "", 0, 0, stopV.Value, StepS: stepV.Value)
                );
            }
        }

        return analyses;
    }

    private static string? FindNoiseInputSource(BenchNetlist netlist)
    {
        // ngspice "noise" requires an independent input source name. Prefer VAC (explicit AC stimulus),
        // but fall back to a DC source when doing noise-only benches.
        var source = netlist
            .Components.Where(c =>
                c.Type.Equals("VAC", StringComparison.OrdinalIgnoreCase)
                || c.Type.Equals("VDC", StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(c => c.Type.Equals("VAC", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return source is null ? null : "V" + source.Id;
    }
}
