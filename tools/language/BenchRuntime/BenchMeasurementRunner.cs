using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Cascode.Language.BenchRuntime;

public sealed class BenchMeasurementRunner
{
    private readonly BenchDefinition _bench;
    private readonly IReadOnlyDictionary<string, FunctionDefinition> _functions;
    private readonly IReadOnlyDictionary<string, MeasurementDefinition> _measurements;
    private readonly IReadOnlyDictionary<string, AnalysisContext> _analyses;
    private readonly IReadOnlyDictionary<string, BenchTerminalRef> _terminals;
    private readonly IReadOnlyDictionary<string, BenchValue> _env;
    private readonly IReadOnlyDictionary<string, BenchValue> _harness;
    private readonly IReadOnlyDictionary<string, BenchValue> _constraints;

    private readonly Dictionary<string, BenchValue> _measurementCache = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly HashSet<string> _measurementStack = new(StringComparer.OrdinalIgnoreCase);

    public sealed record AnalysisContext(
        string Name,
        double StartHz,
        double StopHz,
        AcDataset? Ac,
        NoiseDataset? Noise = null
    );

    public BenchMeasurementRunner(
        BenchDefinition bench,
        IReadOnlyDictionary<string, FunctionDefinition> functions,
        IReadOnlyDictionary<string, AnalysisContext> analyses,
        IReadOnlyDictionary<string, BenchTerminalRef> terminals,
        IReadOnlyDictionary<string, BenchValue> env,
        IReadOnlyDictionary<string, BenchValue> harness,
        IReadOnlyDictionary<string, BenchValue> constraints
    )
    {
        _bench = bench;
        _functions = functions;
        _measurements = bench.Measurements.ToDictionary(
            m => m.Name,
            StringComparer.OrdinalIgnoreCase
        );
        _analyses = analyses;
        _terminals = terminals;
        _env = env;
        _harness = harness;
        _constraints = constraints;
    }

    public IReadOnlyDictionary<string, (double Value, string Unit)> RunAll()
    {
        var results = new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _bench.Measurements)
        {
            var v = EvaluateMeasurement(m.Name);
            var n = RequireNumber(v, $"measurement '{m.Name}'");
            results[m.Name] = (n.Value, m.Unit);
        }

        return results;
    }

    internal BenchValue EvaluateExpressionForPlan(
        MeasurementExpr expr,
        IReadOnlyDictionary<string, BenchValue>? locals = null
    )
    {
        var scope = new Dictionary<string, BenchValue>(StringComparer.Ordinal);
        if (locals is not null)
        {
            foreach (var kvp in locals)
            {
                scope[kvp.Key] = kvp.Value;
            }
        }

        return EvaluateExpr(expr, scope);
    }

    private BenchValue EvaluateMeasurement(string name)
    {
        if (_measurementCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        if (!_measurements.TryGetValue(name, out var measurement))
        {
            throw new InvalidOperationException($"Unknown measurement '{name}'.");
        }

        if (!_measurementStack.Add(name))
        {
            throw new InvalidOperationException(
                $"Cyclic measurement dependency detected at '{name}'."
            );
        }

        var locals = new Dictionary<string, BenchValue>(StringComparer.Ordinal);
        var value = ExecuteStatements(measurement.Body, locals);
        _measurementCache[name] = value;
        _measurementStack.Remove(name);
        return value;
    }

    private BenchValue ExecuteStatements(
        IReadOnlyList<BenchStatement> statements,
        Dictionary<string, BenchValue> locals
    )
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case BenchVarDecl v:
                    locals[v.Name] = EvaluateExpr(v.Expr, locals);
                    break;

                case BenchIf i:
                    var cond = EvaluateBool(i.Condition, locals);
                    if (cond)
                    {
                        return ExecuteStatements(
                            i.ThenBody,
                            new Dictionary<string, BenchValue>(locals)
                        );
                    }

                    if (i.ElseBody is not null)
                    {
                        return ExecuteStatements(
                            i.ElseBody,
                            new Dictionary<string, BenchValue>(locals)
                        );
                    }
                    break;

                case BenchReturn r:
                    return EvaluateExpr(r.Expr, locals);
            }
        }

        throw new InvalidOperationException(
            "Missing return statement in measurement/function body."
        );
    }

    private bool EvaluateBool(BoolExpr expr, Dictionary<string, BenchValue> locals)
    {
        return expr switch
        {
            BoolExists e => ScopedHasValue(e.Ref),
            BoolCompare c => Compare(
                c.Op,
                RequireNumber(EvaluateExpr(c.Left, locals), "lhs"),
                RequireNumber(EvaluateExpr(c.Right, locals), "rhs")
            ),
            _ => throw new InvalidOperationException(
                $"Unhandled bool expression: {expr.GetType().Name}"
            ),
        };
    }

    private bool ScopedHasValue(ScopedValueRef r)
    {
        return r.Scope switch
        {
            MeasurementScope.Env => _env.ContainsKey(r.Name),
            MeasurementScope.Harness => _harness.ContainsKey(r.Name),
            MeasurementScope.Constraints => _constraints.ContainsKey(r.Name),
            _ => false,
        };
    }

    private BenchValue EvaluateExpr(MeasurementExpr expr, Dictionary<string, BenchValue> locals)
    {
        switch (expr)
        {
            case MeasurementNumber n:
                return new BenchNumber(BenchNumericKind.Scalar, ParseInvariant(n.Raw));

            case MeasurementQuantity q:
                return ParseQuantity(q.Raw);

            case MeasurementScopedAccess s:
                return ResolveScopedValue(s.Ref);

            case MeasurementDutAccess d:
                // Treat dut.<net> as a terminal whose voltage is probed via hierarchical naming.
                // The testbench emitter is responsible for saving this node in wrdata.
                return new BenchTerminalRef("dut." + d.PinRef, new[] { MakeDutNodeKey(d.PinRef) });

            case MeasurementPath p:
                return ResolvePathValue(p.Path, locals);

            case MeasurementUnary u:
                if (u.Op != "-")
                {
                    throw new InvalidOperationException($"Unsupported unary operator '{u.Op}'.");
                }
                return Negate(RequireNumber(EvaluateExpr(u.Operand, locals), "unary"));

            case MeasurementBinary b:
                return ApplyBinary(
                    b.Op,
                    RequireNumber(EvaluateExpr(b.Left, locals), "left"),
                    RequireNumber(EvaluateExpr(b.Right, locals), "right")
                );

            case MeasurementConditional c:
                return EvaluateBool(c.Condition, locals)
                    ? EvaluateExpr(c.ThenExpr, locals)
                    : EvaluateExpr(c.ElseExpr, locals);

            case MeasurementCall call:
                return EvaluateCall(call, locals);
        }

        throw new InvalidOperationException($"Unhandled expression: {expr.GetType().Name}");
    }

    private BenchValue ResolveScopedValue(ScopedValueRef r)
    {
        return r.Scope switch
        {
            MeasurementScope.Env when _env.TryGetValue(r.Name, out var e) => e,
            MeasurementScope.Harness when _harness.TryGetValue(r.Name, out var h) => h,
            MeasurementScope.Constraints when _constraints.TryGetValue(r.Name, out var c) => c,
            _ => throw new InvalidOperationException(
                $"Undefined scoped value '{r.Scope}.{r.Name}'."
            ),
        };
    }

    private BenchValue ResolvePathValue(string path, Dictionary<string, BenchValue> locals)
    {
        if (locals.TryGetValue(path, out var local))
        {
            return local;
        }

        if (_measurementCache.ContainsKey(path) || _measurements.ContainsKey(path))
        {
            return EvaluateMeasurement(path);
        }

        if (_terminals.TryGetValue(path, out var terminal))
        {
            return terminal;
        }

        // Allow "IN.P" by resolving to the parent terminal.
        var dot = path.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            var root = path[..dot];
            if (_terminals.TryGetValue(root, out terminal))
            {
                return terminal;
            }

            var baseName = root;
            var member = path[(dot + 1)..];
            if (_analyses.TryGetValue(baseName, out var analysis))
            {
                if (member.Equals("start", StringComparison.OrdinalIgnoreCase))
                {
                    return new BenchNumber(BenchNumericKind.FrequencyHz, analysis.StartHz);
                }
                if (member.Equals("stop", StringComparison.OrdinalIgnoreCase))
                {
                    return new BenchNumber(BenchNumericKind.FrequencyHz, analysis.StopHz);
                }
            }
        }

        // Unquoted identifiers are treated as symbols (e.g., dir=falling).
        return new BenchSymbol(path);
    }

    private BenchValue EvaluateCall(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        switch (call.Name)
        {
            case "transfer":
                return EvalTransfer(call, locals);
            case "mag":
                return EvalMag(call, locals);
            case "db20":
                return EvalDb20(call, locals);
            case "db10":
                return EvalDb10(call, locals);
            case "phase":
                return EvalPhase(call, locals);
            case "eval":
                return EvalEval(call, locals);
            case "find_crossing":
                return EvalFindCrossing(call, locals);
            case "noise":
                return EvalNoise(call, locals);
            case "input_referred_noise":
                return EvalInputReferredNoise(call, locals);
            case "integrate":
                return EvalIntegrateNoise(call, locals);
            case "spot_noise":
                return EvalSpotNoise(call, locals);
            case "abs":
                return EvalAbs(call, locals);
            case "sqrt":
                return EvalSqrt(call, locals);
        }

        if (!_functions.TryGetValue(call.Name, out var fn))
        {
            throw new InvalidOperationException($"Unknown function '{call.Name}'.");
        }

        var args = BindCallArguments(fn, call, locals);
        return ExecuteStatements(fn.Body, args);
    }

    private Dictionary<string, BenchValue> BindCallArguments(
        FunctionDefinition fn,
        MeasurementCall call,
        Dictionary<string, BenchValue> locals
    )
    {
        var values = new Dictionary<string, BenchValue>(StringComparer.Ordinal);

        var positional = call.Args.Where(a => a.Name is null).ToList();
        var named = call
            .Args.Where(a => a.Name is not null)
            .ToDictionary(a => a.Name!, a => a.Value, StringComparer.Ordinal);

        for (var i = 0; i < fn.Parameters.Count; i++)
        {
            var p = fn.Parameters[i];
            if (named.TryGetValue(p.Name, out var expr))
            {
                values[p.Name] = EvaluateExpr(expr, locals);
            }
            else if (i < positional.Count)
            {
                values[p.Name] = EvaluateExpr(positional[i].Value, locals);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Missing argument '{p.Name}' for function '{fn.Name}'."
                );
            }
        }

        return values;
    }

    private BenchTransferFunction EvalTransfer(
        MeasurementCall call,
        Dictionary<string, BenchValue> locals
    )
    {
        var analysisRef = EvaluateExpr(call.Args[0].Value, locals);
        var analysisName = analysisRef switch
        {
            BenchAnalysisRef a => a.Name,
            BenchSymbol s => s.Name,
            _ => throw new InvalidOperationException(
                "transfer: first argument must be an analysis reference."
            ),
        };

        if (!_analyses.TryGetValue(analysisName, out var analysis) || analysis.Ac is null)
        {
            throw new InvalidOperationException(
                $"transfer: missing AC dataset for analysis '{analysisName}'."
            );
        }

        var stim = RequireTerminal(EvaluateExpr(call.Args[1].Value, locals), "stim");
        var resp = RequireTerminal(EvaluateExpr(call.Args[2].Value, locals), "resp");

        var f = analysis.Ac.FrequenciesHz;
        var values = new Complex[f.Length];

        for (var i = 0; i < f.Length; i++)
        {
            var vStim = TerminalVoltage(analysis.Ac, stim, i);
            var vResp = TerminalVoltage(analysis.Ac, resp, i);
            values[i] = vStim == Complex.Zero ? Complex.Zero : vResp / vStim;
        }

        return new BenchTransferFunction(f, values);
    }

    private BenchNoiseFunction EvalNoise(
        MeasurementCall call,
        Dictionary<string, BenchValue> locals
    )
    {
        var analysisName = ResolveAnalysisName(call.Args[0].Value, locals);
        if (!_analyses.TryGetValue(analysisName, out var analysis) || analysis.Noise is null)
        {
            throw new InvalidOperationException(
                $"noise: missing Noise dataset for analysis '{analysisName}'."
            );
        }

        // Validate node argument type (even though the dataset is analysis-defined).
        _ = RequireTerminal(EvaluateExpr(call.Args[1].Value, locals), "node");

        return new BenchNoiseFunction(
            analysis.Noise.FrequenciesHz,
            analysis.Noise.OutputNoiseVPerRtHz
        );
    }

    private BenchNoiseFunction EvalInputReferredNoise(
        MeasurementCall call,
        Dictionary<string, BenchValue> locals
    )
    {
        var noiseAnalysisName = ResolveAnalysisName(call.Args[0].Value, locals);
        if (!_analyses.TryGetValue(noiseAnalysisName, out var noise) || noise.Noise is null)
        {
            throw new InvalidOperationException(
                $"input_referred_noise: missing Noise dataset for analysis '{noiseAnalysisName}'."
            );
        }

        var acAnalysisName = ResolveAnalysisName(call.Args[1].Value, locals);
        if (!_analyses.TryGetValue(acAnalysisName, out var ac) || ac.Ac is null)
        {
            throw new InvalidOperationException(
                $"input_referred_noise: missing AC dataset for analysis '{acAnalysisName}'."
            );
        }

        var stim = RequireTerminal(EvaluateExpr(call.Args[2].Value, locals), "stim");
        var resp = RequireTerminal(EvaluateExpr(call.Args[3].Value, locals), "resp");

        var tf = ComputeTransfer(ac.Ac, stim, resp);
        var mags = tf.Values.Select(v => v.Magnitude).ToArray();

        var freqs = noise.Noise.FrequenciesHz;
        var values = new double[freqs.Length];
        for (var i = 0; i < freqs.Length; i++)
        {
            var mag = InterpolateLogX(tf.FrequenciesHz, mags, freqs[i]);
            values[i] =
                mag <= 0 ? double.PositiveInfinity : noise.Noise.OutputNoiseVPerRtHz[i] / mag;
        }

        return new BenchNoiseFunction(freqs, values);
    }

    private BenchNumber EvalSpotNoise(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        var fn = (BenchNoiseFunction)EvaluateExpr(call.Args[0].Value, locals);
        var x = RequireNumber(EvaluateExpr(call.Args[1].Value, locals), "freq");
        if (x.Kind != BenchNumericKind.FrequencyHz)
        {
            throw new InvalidOperationException("spot_noise: second argument must be a Frequency.");
        }

        var value = InterpolateLogX(fn.FrequenciesHz, fn.ValuesVPerRtHz, x.Value);
        return new BenchNumber(BenchNumericKind.NoiseVoltageVPerRtHz, value);
    }

    private BenchNumber EvalIntegrateNoise(
        MeasurementCall call,
        Dictionary<string, BenchValue> locals
    )
    {
        var fn = (BenchNoiseFunction)EvaluateExpr(call.Args[0].Value, locals);
        var fLo = RequireNumber(EvaluateExpr(call.Args[1].Value, locals), "f_lo");
        var fHi = RequireNumber(EvaluateExpr(call.Args[2].Value, locals), "f_hi");
        if (fLo.Kind != BenchNumericKind.FrequencyHz || fHi.Kind != BenchNumericKind.FrequencyHz)
        {
            throw new InvalidOperationException("integrate: bounds must be Frequency values.");
        }

        var rms = IntegrateNoiseRms(fn.FrequenciesHz, fn.ValuesVPerRtHz, fLo.Value, fHi.Value);
        return new BenchNumber(BenchNumericKind.IntegratedNoiseVrms, rms);
    }

    private static double IntegrateNoiseRms(
        double[] freqs,
        double[] values,
        double loHz,
        double hiHz
    )
    {
        if (loHz <= 0 || hiHz <= 0 || hiHz <= loHz)
        {
            throw new InvalidOperationException("integrate: invalid frequency bounds.");
        }

        // Integrate PSD = (V/sqrtHz)^2 over linear frequency, then sqrt for Vrms.
        var start = Math.Max(loHz, freqs.First());
        var stop = Math.Min(hiHz, freqs.Last());
        if (stop <= start)
        {
            throw new InvalidOperationException("integrate: empty integration range.");
        }

        // Build a list of points including exact bounds.
        var xs = freqs.Where(f => f > start && f < stop).ToList();
        xs.Insert(0, start);
        xs.Add(stop);

        double area = 0;
        for (var i = 1; i < xs.Count; i++)
        {
            var x0 = xs[i - 1];
            var x1 = xs[i];
            var y0 = InterpolateLogX(freqs, values, x0);
            var y1 = InterpolateLogX(freqs, values, x1);
            var p0 = y0 * y0;
            var p1 = y1 * y1;
            area += 0.5 * (p0 + p1) * (x1 - x0);
        }

        return Math.Sqrt(area);
    }

    private string ResolveAnalysisName(MeasurementExpr expr, Dictionary<string, BenchValue> locals)
    {
        var v = EvaluateExpr(expr, locals);
        return v switch
        {
            BenchAnalysisRef a => a.Name,
            BenchSymbol s => s.Name,
            _ => throw new InvalidOperationException("Expected an analysis reference."),
        };
    }

    private static BenchTransferFunction ComputeTransfer(
        AcDataset ac,
        BenchTerminalRef stim,
        BenchTerminalRef resp
    )
    {
        var f = ac.FrequenciesHz;
        var values = new Complex[f.Length];

        for (var i = 0; i < f.Length; i++)
        {
            var vStim = TerminalVoltage(ac, stim, i);
            var vResp = TerminalVoltage(ac, resp, i);
            values[i] = vStim == Complex.Zero ? Complex.Zero : vResp / vStim;
        }

        return new BenchTransferFunction(f, values);
    }

    private static Complex TerminalVoltage(AcDataset ac, BenchTerminalRef t, int index)
    {
        if (t.LeafNodes.Count == 0)
        {
            return Complex.Zero;
        }

        if (t.LeafNodes.Count == 1)
        {
            return ac.NodeVoltages[t.LeafNodes[0]][index];
        }

        // Treat a 2-leaf terminal as a differential quantity: V(P) - V(N).
        return ac.NodeVoltages[t.LeafNodes[0]][index] - ac.NodeVoltages[t.LeafNodes[1]][index];
    }

    private BenchRealFunction EvalMag(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        var tf = (BenchTransferFunction)EvaluateExpr(call.Args[0].Value, locals);
        var values = tf.Values.Select(v => v.Magnitude).ToArray();
        return new BenchRealFunction(tf.FrequenciesHz, values, BenchNumericKind.Scalar);
    }

    private BenchRealFunction EvalDb20(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        var f = (BenchRealFunction)EvaluateExpr(call.Args[0].Value, locals);
        var values = f
            .Values.Select(v => v > 0 ? 20.0 * Math.Log10(v) : double.NegativeInfinity)
            .ToArray();
        return new BenchRealFunction(f.FrequenciesHz, values, BenchNumericKind.VoltageRatioDb);
    }

    private BenchRealFunction EvalDb10(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        var f = (BenchRealFunction)EvaluateExpr(call.Args[0].Value, locals);
        var values = f
            .Values.Select(v => v > 0 ? 10.0 * Math.Log10(v) : double.NegativeInfinity)
            .ToArray();
        return new BenchRealFunction(f.FrequenciesHz, values, BenchNumericKind.VoltageRatioDb);
    }

    private BenchRealFunction EvalPhase(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        var tf = (BenchTransferFunction)EvaluateExpr(call.Args[0].Value, locals);
        var values = tf
            .Values.Select(v => Math.Atan2(v.Imaginary, v.Real) * 180.0 / Math.PI)
            .ToArray();
        return new BenchRealFunction(tf.FrequenciesHz, values, BenchNumericKind.PhaseDeg);
    }

    private BenchNumber EvalEval(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        var f = (BenchRealFunction)EvaluateExpr(call.Args[0].Value, locals);
        var x = RequireNumber(EvaluateExpr(call.Args[1].Value, locals), "freq");
        if (x.Kind != BenchNumericKind.FrequencyHz)
        {
            throw new InvalidOperationException("eval: second argument must be a Frequency.");
        }

        var value = InterpolateLogX(f.FrequenciesHz, f.Values, x.Value);
        return new BenchNumber(f.RangeKind, value);
    }

    private BenchNumber EvalFindCrossing(
        MeasurementCall call,
        Dictionary<string, BenchValue> locals
    )
    {
        var fn = (BenchRealFunction)EvaluateExpr(call.Args[0].Value, locals);
        var threshold = RequireNumber(EvaluateExpr(call.Args[1].Value, locals), "threshold");
        if (threshold.Kind != fn.RangeKind)
        {
            throw new InvalidOperationException(
                $"find_crossing: threshold kind '{threshold.Kind}' does not match function range '{fn.RangeKind}'."
            );
        }

        var dir = GetNamedSymbol(call, "dir") ?? "falling";
        var cross = GetNamedInt(call, "cross") ?? 1;
        var from = GetNamedFrequency(call, "from", locals) ?? fn.FrequenciesHz.First();
        var to = GetNamedFrequency(call, "to", locals) ?? fn.FrequenciesHz.Last();

        var crossing = FindCrossing(
            fn.FrequenciesHz,
            fn.Values,
            threshold.Value,
            dir,
            cross,
            from,
            to
        );
        return new BenchNumber(BenchNumericKind.FrequencyHz, crossing);
    }

    private static double FindCrossing(
        double[] freqs,
        double[] values,
        double threshold,
        string dir,
        int cross,
        double fromHz,
        double toHz
    )
    {
        if (cross < 1)
        {
            throw new InvalidOperationException("find_crossing: cross must be >= 1.");
        }

        var startIndex = Array.FindIndex(freqs, f => f >= fromHz);
        var endIndex = Array.FindLastIndex(freqs, f => f <= toHz);
        if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
        {
            throw new InvalidOperationException("find_crossing: empty search range.");
        }

        var wantFalling = dir.Equals("falling", StringComparison.OrdinalIgnoreCase);
        var count = 0;

        for (var i = startIndex + 1; i <= endIndex; i++)
        {
            var y0 = values[i - 1] - threshold;
            var y1 = values[i] - threshold;
            if (double.IsNaN(y0) || double.IsNaN(y1))
            {
                continue;
            }

            var crossed = (y0 >= 0 && y1 <= 0) || (y0 <= 0 && y1 >= 0);
            if (!crossed)
            {
                continue;
            }

            var falling = y0 > y1;
            if (wantFalling != falling)
            {
                continue;
            }

            count++;
            if (count != cross)
            {
                continue;
            }

            // Interpolate linearly in y on a log-frequency x-axis.
            var x0 = Math.Log10(freqs[i - 1]);
            var x1 = Math.Log10(freqs[i]);
            var t = y0 == y1 ? 0.0 : y0 / (y0 - y1);
            var x = x0 + t * (x1 - x0);
            return Math.Pow(10.0, x);
        }

        throw new InvalidOperationException("find_crossing: crossing not found.");
    }

    private BenchNumber EvalAbs(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        var x = RequireNumber(EvaluateExpr(call.Args[0].Value, locals), "abs");
        return new BenchNumber(x.Kind, Math.Abs(x.Value));
    }

    private BenchNumber EvalSqrt(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        var x = RequireNumber(EvaluateExpr(call.Args[0].Value, locals), "sqrt");
        return new BenchNumber(x.Kind, Math.Sqrt(x.Value));
    }

    private static BenchTerminalRef RequireTerminal(BenchValue value, string name)
    {
        return value as BenchTerminalRef
            ?? throw new InvalidOperationException(
                $"Expected terminal for '{name}', got {value.GetType().Name}."
            );
    }

    private static BenchNumber RequireNumber(BenchValue value, string name)
    {
        return value as BenchNumber
            ?? throw new InvalidOperationException(
                $"Expected number for '{name}', got {value.GetType().Name}."
            );
    }

    private static BenchNumber Negate(BenchNumber x) => new(x.Kind, -x.Value);

    private static BenchNumber ApplyBinary(string op, BenchNumber left, BenchNumber right)
    {
        if (op is "+" or "-")
        {
            if (left.Kind != right.Kind)
            {
                throw new InvalidOperationException(
                    $"Binary '{op}' requires matching kinds, got {left.Kind} and {right.Kind}."
                );
            }

            return op == "+"
                ? new BenchNumber(left.Kind, left.Value + right.Value)
                : new BenchNumber(left.Kind, left.Value - right.Value);
        }

        if (op is "*" or "/")
        {
            if (left.Kind == BenchNumericKind.Scalar)
            {
                return op == "*"
                    ? new BenchNumber(right.Kind, left.Value * right.Value)
                    : new BenchNumber(right.Kind, left.Value / right.Value);
            }
            if (right.Kind == BenchNumericKind.Scalar)
            {
                return op == "*"
                    ? new BenchNumber(left.Kind, left.Value * right.Value)
                    : new BenchNumber(left.Kind, left.Value / right.Value);
            }

            if (left.Kind == right.Kind && left.Kind == BenchNumericKind.FrequencyHz)
            {
                return op == "*"
                    ? new BenchNumber(BenchNumericKind.FrequencyHz, left.Value * right.Value)
                    : new BenchNumber(BenchNumericKind.Scalar, left.Value / right.Value);
            }

            throw new InvalidOperationException(
                $"Unsupported binary '{op}' for kinds {left.Kind} and {right.Kind}."
            );
        }

        throw new InvalidOperationException($"Unsupported binary operator '{op}'.");
    }

    private static double InterpolateLogX(double[] xs, double[] ys, double x)
    {
        if (x <= xs[0])
        {
            return ys[0];
        }
        if (x >= xs[^1])
        {
            return ys[^1];
        }

        var i = Array.BinarySearch(xs, x);
        if (i >= 0)
        {
            return ys[i];
        }

        i = ~i;
        var i0 = i - 1;
        var i1 = i;
        var x0 = Math.Log10(xs[i0]);
        var x1 = Math.Log10(xs[i1]);
        var t = (Math.Log10(x) - x0) / (x1 - x0);
        return ys[i0] + t * (ys[i1] - ys[i0]);
    }

    private static string? GetNamedSymbol(MeasurementCall call, string name)
    {
        var arg = call.Args.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)
        );
        if (arg is null)
        {
            return null;
        }

        return arg.Value switch
        {
            MeasurementPath p => p.Path,
            MeasurementQuantity q => q.Raw,
            MeasurementNumber n => n.Raw,
            _ => arg.Value.ToString(),
        };
    }

    private static int? GetNamedInt(MeasurementCall call, string name)
    {
        var arg = call.Args.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)
        );
        if (arg is null)
        {
            return null;
        }

        var raw = arg.Value switch
        {
            MeasurementNumber n => n.Raw,
            _ => null,
        };

        if (raw is null)
        {
            return null;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private double? GetNamedFrequency(
        MeasurementCall call,
        string name,
        Dictionary<string, BenchValue> locals
    )
    {
        var arg = call.Args.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)
        );
        if (arg is null)
        {
            return null;
        }

        return RequireFrequency(EvaluateExpr(arg.Value, locals), $"{call.Name}:{name}").Value;
    }

    private static BenchValue ParseQuantity(string raw)
    {
        return BenchQuantity.Parse(raw);
    }

    private static BenchNumber RequireFrequency(BenchValue v, string context)
    {
        var n = RequireNumber(v, context);
        if (n.Kind != BenchNumericKind.FrequencyHz)
        {
            throw new InvalidOperationException($"Expected Frequency for {context}, got {n.Kind}.");
        }
        return n;
    }

    private static double ParseInvariant(string raw) =>
        double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static double ParseSiNumber(string raw)
    {
        if (!SiValue.TryParse(raw, out var value, stripUnits: true, allowSubUnity: true))
        {
            throw new InvalidOperationException($"Invalid numeric quantity '{raw}'.");
        }

        return value;
    }

    private static bool Compare(ComparisonOp op, BenchNumber left, BenchNumber right)
    {
        if (
            left.Kind != right.Kind
            && left.Kind != BenchNumericKind.Scalar
            && right.Kind != BenchNumericKind.Scalar
        )
        {
            throw new InvalidOperationException($"Cannot compare {left.Kind} to {right.Kind}.");
        }

        var l = left.Value;
        var r = right.Value;
        return op switch
        {
            ComparisonOp.Gte => l >= r,
            ComparisonOp.Lte => l <= r,
            ComparisonOp.Gt => l > r,
            ComparisonOp.Lt => l < r,
            ComparisonOp.Eq => Math.Abs(l - r) < 1e-12,
            _ => throw new InvalidOperationException($"Unknown comparison op '{op}'."),
        };
    }

    private static string MakeDutNodeKey(string pinRef)
    {
        // ngspice hierarchical node syntax uses XDUT:<net>. Keep ':' and sanitize dots.
        return "XDUT:" + pinRef.Replace('.', '_');
    }
}
