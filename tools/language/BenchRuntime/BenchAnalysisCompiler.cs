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
        BenchNetlist netlist,
        IReadOnlyDictionary<string, BenchValue>? benchParams = null
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
                var raw = evalRunner.EvaluateExpressionForPlan(spaceExpr, benchParams);
                if (raw is BenchSymbol sym && !string.IsNullOrWhiteSpace(sym.Name))
                {
                    space = sym.Name.Trim().ToLowerInvariant();
                }
            }

            var enableNoise = false;
            if (
                a.Type == BenchValueType.SPAnalysis
                && a.Parameters.TryGetValue("noise", out var noiseExpr)
            )
            {
                var raw = evalRunner.EvaluateExpressionForPlan(noiseExpr, benchParams);
                if (
                    raw is not BenchNumber noise
                    || noise.Kind != BenchNumericKind.Scalar
                    || (noise.Value != 0 && noise.Value != 1)
                )
                {
                    throw new InvalidOperationException(
                        $"SPAnalysis '{a.Name}' noise must be 0 or 1."
                    );
                }

                enableNoise = noise.Value == 1;
            }

            var samples = 100;
            if (a.Parameters.TryGetValue("samples", out var samplesExpr))
            {
                var raw = evalRunner.EvaluateExpressionForPlan(samplesExpr, benchParams);
                if (raw is BenchNumber n)
                {
                    samples = (int)n.Value;
                }
            }

            if (a.Type == BenchValueType.ACAnalysis || a.Type == BenchValueType.SPAnalysis)
            {
                if (!a.Parameters.TryGetValue("start", out var startExpr))
                {
                    throw new InvalidOperationException(
                        $"{a.Type} '{a.Name}' missing required parameter 'start'."
                    );
                }
                if (!a.Parameters.TryGetValue("stop", out var stopExpr))
                {
                    throw new InvalidOperationException(
                        $"{a.Type} '{a.Name}' missing required parameter 'stop'."
                    );
                }

                var startV =
                    evalRunner.EvaluateExpressionForPlan(startExpr, benchParams) as BenchNumber;
                var stopV =
                    evalRunner.EvaluateExpressionForPlan(stopExpr, benchParams) as BenchNumber;
                if (startV is null)
                {
                    throw new InvalidOperationException(
                        $"{a.Type} '{a.Name}' start did not evaluate to a number."
                    );
                }
                if (stopV is null)
                {
                    throw new InvalidOperationException(
                        $"{a.Type} '{a.Name}' stop did not evaluate to a number."
                    );
                }
                if (
                    startV.Kind != BenchNumericKind.FrequencyHz
                    || stopV.Kind != BenchNumericKind.FrequencyHz
                )
                {
                    throw new InvalidOperationException(
                        $"{a.Type} '{a.Name}' start/stop must be Frequency values."
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
                        EnableNoise: enableNoise
                    )
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

                var startV =
                    evalRunner.EvaluateExpressionForPlan(startExpr, benchParams) as BenchNumber;
                var stopV =
                    evalRunner.EvaluateExpressionForPlan(stopExpr, benchParams) as BenchNumber;
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
                    evalRunner.EvaluateExpressionForPlan(outputExpr, benchParams)
                        as BenchTerminalRef
                    ?? throw new InvalidOperationException(
                        $"NoiseAnalysis '{a.Name}' output did not evaluate to a terminal."
                    );
                if (
                    startV.Kind != BenchNumericKind.FrequencyHz
                    || stopV.Kind != BenchNumericKind.FrequencyHz
                )
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

                var stopV =
                    evalRunner.EvaluateExpressionForPlan(stopExpr, benchParams) as BenchNumber;
                var startV = a.Parameters.TryGetValue("start", out var startExpr)
                    ? evalRunner.EvaluateExpressionForPlan(startExpr, benchParams) as BenchNumber
                    : new BenchNumber(BenchNumericKind.TimeS, 0);
                var stepV = a.Parameters.TryGetValue("step", out var stepExpr)
                    ? evalRunner.EvaluateExpressionForPlan(stepExpr, benchParams) as BenchNumber
                    : null;

                if (startV is null || startV.Kind != BenchNumericKind.TimeS)
                {
                    throw new InvalidOperationException(
                        $"TranAnalysis '{a.Name}' start must be a Time value."
                    );
                }
                if (stopV is null || stopV.Kind != BenchNumericKind.TimeS)
                {
                    throw new InvalidOperationException(
                        $"TranAnalysis '{a.Name}' stop must be a Time value."
                    );
                }

                if (stepV is not null && stepV.Kind != BenchNumericKind.TimeS)
                {
                    throw new InvalidOperationException(
                        $"TranAnalysis '{a.Name}' step must be a Time value."
                    );
                }

                // Use a conservative default step if not specified: 1/1000 of stop time.
                var stepS = stepV?.Value ?? Math.Max(stopV.Value / 1000.0, 1e-12);

                analyses.Add(
                    new BenchPlanAnalysis(
                        a.Type,
                        a.Name,
                        "",
                        0,
                        0,
                        0,
                        StartS: startV.Value,
                        StopS: stopV.Value,
                        StepS: stepS
                    )
                );
            }

            if (a.Type == BenchValueType.PSSAnalysis)
            {
                if (!a.Parameters.TryGetValue("fguess", out var fguessExpr))
                {
                    throw new InvalidOperationException(
                        $"PSSAnalysis '{a.Name}' missing required parameter 'fguess'."
                    );
                }
                if (!a.Parameters.TryGetValue("tstab", out var tstabExpr))
                {
                    throw new InvalidOperationException(
                        $"PSSAnalysis '{a.Name}' missing required parameter 'tstab'."
                    );
                }
                if (!a.Parameters.TryGetValue("harmonics", out var harmonicsExpr))
                {
                    throw new InvalidOperationException(
                        $"PSSAnalysis '{a.Name}' missing required parameter 'harmonics'."
                    );
                }

                var fguessV =
                    evalRunner.EvaluateExpressionForPlan(fguessExpr, benchParams) as BenchNumber;
                var tstabV =
                    evalRunner.EvaluateExpressionForPlan(tstabExpr, benchParams) as BenchNumber;
                var harmonicsV =
                    evalRunner.EvaluateExpressionForPlan(harmonicsExpr, benchParams) as BenchNumber;

                if (fguessV is null || fguessV.Kind != BenchNumericKind.FrequencyHz)
                {
                    throw new InvalidOperationException(
                        $"PSSAnalysis '{a.Name}.fguess' expects 'Frequency'."
                    );
                }

                if (tstabV is null || tstabV.Kind != BenchNumericKind.TimeS)
                {
                    throw new InvalidOperationException(
                        $"PSSAnalysis '{a.Name}.tstab' expects 'Time'."
                    );
                }

                if (
                    harmonicsV is null
                    || harmonicsV.Kind != BenchNumericKind.Scalar
                    || harmonicsV.Value < 1
                    || harmonicsV.Value != Math.Round(harmonicsV.Value)
                )
                {
                    throw new InvalidOperationException(
                        $"PSSAnalysis '{a.Name}.harmonics' expects a positive integer."
                    );
                }

                var outputTerminal = ResolvePssOutputTerminal(bench, evalRunner, benchParams);
                if (outputTerminal.LeafNodes.Count == 0)
                {
                    throw new InvalidOperationException(
                        "PSSAnalysis requires at least one resp terminal."
                    );
                }

                var iterations = EvaluateOptionalPositiveIntegerParam(
                    a,
                    "iterations",
                    50,
                    evalRunner,
                    benchParams
                );
                var steadyCoef = EvaluateOptionalPositiveScalarParam(
                    a,
                    "steady_coef",
                    1e-3,
                    evalRunner,
                    benchParams
                );
                var useInitialConditions = EvaluateOptionalBinaryFlagParam(
                    a,
                    "uic",
                    defaultValue: false,
                    evalRunner,
                    benchParams
                );

                analyses.Add(
                    new BenchPlanAnalysis(
                        a.Type,
                        a.Name,
                        "",
                        0,
                        0,
                        0,
                        FguessHz: fguessV.Value,
                        TstabS: tstabV.Value,
                        Harmonics: (int)harmonicsV.Value,
                        OscNode: outputTerminal.LeafNodes[0],
                        Iterations: iterations,
                        SteadyCoef: steadyCoef,
                        UseInitialConditions: useInitialConditions
                    )
                );
            }

            if (a.Type == BenchValueType.DCAnalysis)
            {
                // For now, we treat DCAnalysis as a DC operating point (op). Sweeps are modeled
                // via circuit-level harness sweeps instead of bench-defined .dc cards.
                if (a.Parameters.Count != 0)
                {
                    throw new InvalidOperationException(
                        $"DCAnalysis '{a.Name}' does not accept parameters. Use circuit harness sweeps for multi-point evaluation."
                    );
                }

                analyses.Add(new BenchPlanAnalysis(a.Type, a.Name, "", 0, 0, 0));
            }
        }

        return analyses;
    }

    private static BenchTerminalRef ResolvePssOutputTerminal(
        BenchDefinition bench,
        BenchMeasurementRunner evalRunner,
        IReadOnlyDictionary<string, BenchValue>? benchParams
    )
    {
        var output = bench.Terminals.FirstOrDefault(t => t.Role == BenchTerminalRole.Resp);
        if (output is null)
        {
            throw new InvalidOperationException("PSSAnalysis requires at least one resp terminal.");
        }

        return evalRunner.EvaluateExpressionForPlan(new MeasurementPath(output.Name), benchParams)
                as BenchTerminalRef
            ?? throw new InvalidOperationException(
                $"PSSAnalysis terminal '{output.Name}' did not resolve to a terminal."
            );
    }

    private static int EvaluateOptionalPositiveIntegerParam(
        AnalysisDeclaration analysis,
        string name,
        int defaultValue,
        BenchMeasurementRunner evalRunner,
        IReadOnlyDictionary<string, BenchValue>? benchParams
    )
    {
        if (!analysis.Parameters.TryGetValue(name, out var expression))
        {
            return defaultValue;
        }

        var value = evalRunner.EvaluateExpressionForPlan(expression, benchParams) as BenchNumber;
        if (
            value is null
            || value.Kind != BenchNumericKind.Scalar
            || !double.IsFinite(value.Value)
            || value.Value < 1
            || value.Value != Math.Round(value.Value)
            || value.Value > int.MaxValue
        )
        {
            throw new InvalidOperationException(
                $"PSSAnalysis '{analysis.Name}.{name}' expects a positive integer."
            );
        }

        return checked((int)value.Value);
    }

    private static double EvaluateOptionalPositiveScalarParam(
        AnalysisDeclaration analysis,
        string name,
        double defaultValue,
        BenchMeasurementRunner evalRunner,
        IReadOnlyDictionary<string, BenchValue>? benchParams
    )
    {
        if (!analysis.Parameters.TryGetValue(name, out var expression))
        {
            return defaultValue;
        }

        var value = evalRunner.EvaluateExpressionForPlan(expression, benchParams) as BenchNumber;
        if (
            value is null
            || value.Kind != BenchNumericKind.Scalar
            || !double.IsFinite(value.Value)
            || value.Value <= 0
        )
        {
            throw new InvalidOperationException(
                $"PSSAnalysis '{analysis.Name}.{name}' expects a positive scalar."
            );
        }

        return value.Value;
    }

    private static bool EvaluateOptionalBinaryFlagParam(
        AnalysisDeclaration analysis,
        string name,
        bool defaultValue,
        BenchMeasurementRunner evalRunner,
        IReadOnlyDictionary<string, BenchValue>? benchParams
    )
    {
        if (!analysis.Parameters.TryGetValue(name, out var expression))
        {
            return defaultValue;
        }

        var value = evalRunner.EvaluateExpressionForPlan(expression, benchParams) as BenchNumber;
        if (
            value is null
            || value.Kind != BenchNumericKind.Scalar
            || !double.IsFinite(value.Value)
            || value.Value != Math.Round(value.Value)
            || value.Value is not 0 and not 1
        )
        {
            throw new InvalidOperationException(
                $"PSSAnalysis '{analysis.Name}.{name}' expects 0 or 1."
            );
        }

        return value.Value == 1;
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
