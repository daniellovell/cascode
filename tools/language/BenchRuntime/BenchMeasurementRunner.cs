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
    private readonly IReadOnlyDictionary<string, BenchHarnessElement> _harnessElementsById;
    private readonly IReadOnlyDictionary<string, double> _sourceCurrentsByName;
    private readonly IReadOnlyDictionary<string, string> _dutNodeKeyByPinRef;

    private readonly Dictionary<string, BenchValue> _measurementCache = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly HashSet<string> _measurementStack = new(StringComparer.OrdinalIgnoreCase);

    public sealed record AnalysisContext(
        string Name,
        double StartHz,
        double StopHz,
        double StartS,
        double StopS,
        AcDataset? Ac,
        NoiseDataset? Noise = null,
        TranDataset? Tran = null,
        TranDataset? TranCurrents = null,
        AcDataset? AcCurrents = null
    );

    public BenchMeasurementRunner(
        BenchDefinition bench,
        IReadOnlyDictionary<string, FunctionDefinition> functions,
        IReadOnlyDictionary<string, AnalysisContext> analyses,
        IReadOnlyDictionary<string, BenchTerminalRef> terminals,
        IReadOnlyDictionary<string, BenchValue> env,
        IReadOnlyDictionary<string, BenchValue> harness,
        IReadOnlyDictionary<string, BenchValue> constraints,
        IReadOnlyList<BenchHarnessElement>? harnessElements = null,
        IReadOnlyDictionary<string, double>? sourceCurrentsByName = null,
        IReadOnlyDictionary<string, string>? dutNodeKeyByPinRef = null
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
        _harnessElementsById = harnessElements is null
            ? new Dictionary<string, BenchHarnessElement>(StringComparer.OrdinalIgnoreCase)
            : harnessElements.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        _sourceCurrentsByName =
            sourceCurrentsByName
            ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        _dutNodeKeyByPinRef =
            dutNodeKeyByPinRef ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, (double Value, string Unit)> RunAll()
    {
        var results = new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _bench.Measurements)
        {
            // Parameterized measurements require explicit arguments; they are evaluated on-demand
            // via constraints or explicit calls (e.g. IntegratedInputNoise(from=..., to=...)).
            if (m.Parameters.Count != 0)
            {
                continue;
            }

            var v = EvaluateMeasurement(m.Name);
            if (v is BenchError err)
            {
                throw new InvalidOperationException(err.Message);
            }
            var n = RequireNumber(v, $"measurement '{m.Name}'");
            results[m.Name] = (n.Value, m.Unit);
        }

        return results;
    }

    public IReadOnlyDictionary<string, BenchValue> RunAllValues()
    {
        var results = new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _bench.Measurements)
        {
            if (m.Parameters.Count != 0)
            {
                continue;
            }

            results[m.Name] = EvaluateMeasurement(m.Name);
        }

        return results;
    }

    public IReadOnlyDictionary<string, (double Value, string Unit)> RunMetrics(
        IEnumerable<string> measurementNames
    )
    {
        ArgumentNullException.ThrowIfNull(measurementNames);

        var results = new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (
            var name in measurementNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        )
        {
            if (!_measurements.TryGetValue(name, out var m))
            {
                throw new InvalidOperationException($"Unknown measurement '{name}'.");
            }

            var v = EvaluateMeasurement(m.Name);
            if (v is BenchError err)
            {
                throw new InvalidOperationException(err.Message);
            }
            var n = RequireNumber(v, $"measurement '{m.Name}'");
            results[m.Name] = (n.Value, m.Unit);
        }

        return results;
    }

    public IReadOnlyDictionary<string, BenchValue> RunMetricValues(
        IEnumerable<string> measurementNames
    )
    {
        ArgumentNullException.ThrowIfNull(measurementNames);

        var results = new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase);
        foreach (
            var name in measurementNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        )
        {
            if (!_measurements.ContainsKey(name))
            {
                throw new InvalidOperationException($"Unknown measurement '{name}'.");
            }

            results[name] = EvaluateMeasurement(name);
        }

        return results;
    }

    public (double Value, string Unit) RunMetricWithNamedArgs(
        string name,
        IReadOnlyDictionary<string, BenchValue> args
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(args);

        if (!_measurements.TryGetValue(name, out var m))
        {
            throw new InvalidOperationException($"Unknown measurement '{name}'.");
        }
        if (m.Parameters.Count == 0)
        {
            throw new InvalidOperationException($"Measurement '{name}' does not accept arguments.");
        }

        var v = EvaluateMeasurementInvocation(m, args);
        if (v is BenchError err)
        {
            throw new InvalidOperationException(err.Message);
        }
        var n = RequireNumber(v, $"measurement '{m.Name}'");
        return (n.Value, m.Unit);
    }

    public BenchValue RunMetricWithNamedArgsValue(
        string name,
        IReadOnlyDictionary<string, BenchValue> args
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(args);

        if (!_measurements.TryGetValue(name, out var m))
        {
            throw new InvalidOperationException($"Unknown measurement '{name}'.");
        }
        if (m.Parameters.Count == 0)
        {
            throw new InvalidOperationException($"Measurement '{name}' does not accept arguments.");
        }

        return EvaluateMeasurementInvocation(m, args);
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
        if (!_measurements.TryGetValue(name, out var measurement))
        {
            throw new InvalidOperationException($"Unknown measurement '{name}'.");
        }

        if (measurement.Parameters.Count != 0)
        {
            throw new InvalidOperationException(
                $"Measurement '{name}' requires arguments (e.g. {name}(...))."
            );
        }

        return EvaluateMeasurementInvocation(measurement, args: null);
    }

    private BenchValue EvaluateMeasurementInvocation(
        MeasurementDefinition measurement,
        IReadOnlyDictionary<string, BenchValue>? args
    )
    {
        if (measurement.Parameters.Count != 0 && args is null)
        {
            throw new InvalidOperationException(
                $"Measurement '{measurement.Name}' requires arguments."
            );
        }

        var cacheKey = MakeMeasurementCacheKey(measurement, args);
        if (_measurementCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (!_measurementStack.Add(cacheKey))
        {
            var err = new BenchError(
                $"Cyclic measurement dependency detected at '{measurement.Name}'."
            );
            _measurementCache[cacheKey] = err;
            return err;
        }

        BenchValue result = BenchMissing.Value;
        try
        {
            var locals = new Dictionary<string, BenchValue>(StringComparer.Ordinal);
            if (args is not null)
            {
                foreach (var p in measurement.Parameters)
                {
                    if (!args.TryGetValue(p.Name, out var value))
                    {
                        throw new InvalidOperationException(
                            $"Missing argument '{p.Name}' for measurement '{measurement.Name}'."
                        );
                    }
                    locals[p.Name] = value;
                }
            }

            result = ExecuteStatements(measurement.Body, locals);
        }
        catch (Exception ex)
        {
            // A failed measurement should not abort bench evaluation. Capture a stable error and
            // let constraints treat it as a compliance failure.
            result = new BenchError(ex.Message);
        }
        finally
        {
            _measurementCache[cacheKey] = result;
            _measurementStack.Remove(cacheKey);
        }

        return result;
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
            BoolTruthy t => !IsMissing(EvaluateExpr(t.Expr, locals)),
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

    private static bool IsMissing(BenchValue v) => v is BenchMissing;

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
                if (!_dutNodeKeyByPinRef.TryGetValue(d.PinRef, out var nodeKey))
                {
                    throw new InvalidOperationException(
                        $"Unknown dut node reference '{d.PinRef}' (missing from compiled plan)."
                    );
                }
                return new BenchTerminalRef("dut." + d.PinRef, new[] { nodeKey });

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

            case MeasurementMethodCall m:
                return EvaluateMethodCall(m, locals);

            case MeasurementCall call:
                return EvaluateCall(call, locals);
        }

        throw new InvalidOperationException($"Unhandled expression: {expr.GetType().Name}");
    }

    private BenchValue ResolveScopedValue(ScopedValueRef r)
    {
        if (r.Scope == MeasurementScope.Env && _env.TryGetValue(r.Name, out var e))
        {
            return e;
        }

        if (r.Scope == MeasurementScope.Constraints && _constraints.TryGetValue(r.Name, out var c))
        {
            return c;
        }

        if (r.Scope == MeasurementScope.Harness)
        {
            if (_harness.TryGetValue(r.Name, out var h))
            {
                return h;
            }

            if (TryResolveHarnessPin(r.Name, out var pin))
            {
                return pin;
            }
        }

        // Optional scoped values can be absent.
        return BenchMissing.Value;
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
                    if (analysis.Tran is not null)
                    {
                        return new BenchNumber(BenchNumericKind.TimeS, analysis.StartS);
                    }
                    return new BenchNumber(BenchNumericKind.FrequencyHz, analysis.StartHz);
                }
                if (member.Equals("stop", StringComparison.OrdinalIgnoreCase))
                {
                    if (analysis.Tran is not null)
                    {
                        return new BenchNumber(BenchNumericKind.TimeS, analysis.StopS);
                    }
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
            case "voltage":
                return EvalVoltage(call, locals);
            case "current":
                return EvalCurrent(call, locals);
            case "db20":
                return EvalDb20(call, locals);
            case "db10":
                return EvalDb10(call, locals);
            case "noise":
                return EvalNoise(call, locals);
            case "input_referred_noise":
                return EvalInputReferredNoise(call, locals);
            case "abs":
                return EvalAbs(call, locals);
            case "sqrt":
                return EvalSqrt(call, locals);
            case "quiescent_power":
                return EvalQuiescentPower(call, locals);
        }

        // Allow measurements to reference other measurements by name with explicit call syntax.
        if (_measurements.TryGetValue(call.Name, out var measurement))
        {
            if (measurement.Parameters.Count == 0)
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException(
                        $"Measurement '{call.Name}' does not accept arguments."
                    );
                }
                return EvaluateMeasurementInvocation(measurement, args: null);
            }

            var args = BindMeasurementArguments(measurement, call, locals);
            return EvaluateMeasurementInvocation(measurement, args);
        }

        if (!_functions.TryGetValue(call.Name, out var fn))
        {
            throw new InvalidOperationException($"Unknown function '{call.Name}'.");
        }

        var fnArgs = BindCallArguments(fn, call, locals);
        return ExecuteStatements(fn.Body, fnArgs);
    }

    private BenchValue EvaluateMethodCall(
        MeasurementMethodCall call,
        Dictionary<string, BenchValue> locals
    )
    {
        var recv = EvaluateExpr(call.Receiver, locals);

        // TransferFunction methods
        if (recv is BenchTransferFunction tf)
        {
            if (call.Method.Equals("Mag", StringComparison.OrdinalIgnoreCase))
            {
                var values = tf.Values.Select(v => v.Magnitude).ToArray();
                return new BenchGainSpectrum(
                    tf.FrequenciesHz,
                    values,
                    BenchNumericKind.VoltageRatioLinear
                );
            }

            if (call.Method.Equals("Phase", StringComparison.OrdinalIgnoreCase))
            {
                var values = tf.Values.Select(v => v.Phase * 180.0 / Math.PI).ToArray();
                return new BenchPhaseSpectrum(tf.FrequenciesHz, values);
            }
        }

        // GainSpectrum methods
        if (recv is BenchGainSpectrum g)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 1)
                {
                    throw new InvalidOperationException(
                        "GainSpectrum.ValueAt requires 1 argument."
                    );
                }

                var f = RequireFrequency(EvaluateExpr(call.Args[0].Value, locals), "ValueAt");
                var v = InterpolateLogX(g.FrequenciesHz, g.Values, f.Value);
                return new BenchNumber(g.ValueKind, v);
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count < 1)
                {
                    throw new InvalidOperationException(
                        "GainSpectrum.FindCrossing requires a threshold argument."
                    );
                }

                var threshold = RequireNumber(
                    EvaluateExpr(call.Args[0].Value, locals),
                    "FindCrossing(threshold)"
                );
                if (
                    threshold.Kind != g.ValueKind
                    && threshold.Kind != BenchNumericKind.Scalar
                    && g.ValueKind != BenchNumericKind.Scalar
                )
                {
                    throw new InvalidOperationException(
                        $"FindCrossing: threshold kind '{threshold.Kind}' does not match spectrum kind '{g.ValueKind}'."
                    );
                }

                var dir = GetNamedSymbol(call, "dir") ?? "falling";
                var cross = GetNamedInt(call, "cross") ?? 1;
                var from = GetNamedFrequency(call, "from", locals) ?? g.FrequenciesHz.First();
                var to = GetNamedFrequency(call, "to", locals) ?? g.FrequenciesHz.Last();

                var crossing = FindCrossing(
                    g.FrequenciesHz,
                    g.Values,
                    threshold.Value,
                    dir,
                    cross,
                    from,
                    to
                );
                return new BenchNumber(BenchNumericKind.FrequencyHz, crossing);
            }

            if (call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("GainSpectrum.Max takes no arguments.");
                }
                return new BenchNumber(g.ValueKind, g.Values.Max());
            }

            if (call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("GainSpectrum.Min takes no arguments.");
                }
                return new BenchNumber(g.ValueKind, g.Values.Min());
            }
        }

        // PhaseSpectrum methods
        if (recv is BenchPhaseSpectrum p)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 1)
                {
                    throw new InvalidOperationException(
                        "PhaseSpectrum.ValueAt requires 1 argument."
                    );
                }

                var f = RequireFrequency(EvaluateExpr(call.Args[0].Value, locals), "ValueAt");
                var v = InterpolateLogX(p.FrequenciesHz, p.Degrees, f.Value);
                return new BenchNumber(BenchNumericKind.PhaseDeg, v);
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count < 1)
                {
                    throw new InvalidOperationException(
                        "PhaseSpectrum.FindCrossing requires a threshold argument."
                    );
                }

                var threshold = RequireNumber(
                    EvaluateExpr(call.Args[0].Value, locals),
                    "FindCrossing(threshold)"
                );
                if (
                    threshold.Kind != BenchNumericKind.PhaseDeg
                    && threshold.Kind != BenchNumericKind.Scalar
                )
                {
                    throw new InvalidOperationException(
                        $"FindCrossing: threshold kind '{threshold.Kind}' does not match PhaseSpectrum."
                    );
                }

                var dir = GetNamedSymbol(call, "dir") ?? "falling";
                var cross = GetNamedInt(call, "cross") ?? 1;
                var from = GetNamedFrequency(call, "from", locals) ?? p.FrequenciesHz.First();
                var to = GetNamedFrequency(call, "to", locals) ?? p.FrequenciesHz.Last();

                var crossing = FindCrossing(
                    p.FrequenciesHz,
                    p.Degrees,
                    threshold.Value,
                    dir,
                    cross,
                    from,
                    to
                );
                return new BenchNumber(BenchNumericKind.FrequencyHz, crossing);
            }

            if (call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("PhaseSpectrum.Max takes no arguments.");
                }
                return new BenchNumber(BenchNumericKind.PhaseDeg, p.Degrees.Max());
            }

            if (call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("PhaseSpectrum.Min takes no arguments.");
                }
                return new BenchNumber(BenchNumericKind.PhaseDeg, p.Degrees.Min());
            }
        }

        // NoiseSpectrum methods
        if (recv is BenchNoiseSpectrum n)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 1)
                {
                    throw new InvalidOperationException(
                        "NoiseSpectrum.ValueAt requires 1 argument."
                    );
                }

                var f = RequireFrequency(EvaluateExpr(call.Args[0].Value, locals), "ValueAt");
                var v = InterpolateLogX(n.FrequenciesHz, n.ValuesVPerRtHz, f.Value);
                return new BenchNumber(BenchNumericKind.NoiseVoltageVPerRtHz, v);
            }

            if (call.Method.Equals("Integrate", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 2)
                {
                    throw new InvalidOperationException(
                        "NoiseSpectrum.Integrate requires (from, to)."
                    );
                }

                var from = RequireFrequency(EvaluateExpr(call.Args[0].Value, locals), "from");
                var to = RequireFrequency(EvaluateExpr(call.Args[1].Value, locals), "to");
                var rms = IntegrateNoiseRms(
                    n.FrequenciesHz,
                    n.ValuesVPerRtHz,
                    from.Value,
                    to.Value
                );
                return new BenchNumber(BenchNumericKind.IntegratedNoiseVrms, rms);
            }

            if (call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("NoiseSpectrum.Max takes no arguments.");
                }
                return new BenchNumber(
                    BenchNumericKind.NoiseVoltageVPerRtHz,
                    n.ValuesVPerRtHz.Max()
                );
            }

            if (call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("NoiseSpectrum.Min takes no arguments.");
                }
                return new BenchNumber(
                    BenchNumericKind.NoiseVoltageVPerRtHz,
                    n.ValuesVPerRtHz.Min()
                );
            }
        }

        if (recv is BenchVoltageSpectrum vs)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 1)
                {
                    throw new InvalidOperationException(
                        "VoltageSpectrum.ValueAt requires 1 argument."
                    );
                }

                var f = RequireFrequency(EvaluateExpr(call.Args[0].Value, locals), "ValueAt");
                var mags = vs.Values.Select(v => v.Magnitude).ToArray();
                var v = InterpolateLogX(vs.FrequenciesHz, mags, f.Value);
                return new BenchNumber(BenchNumericKind.VoltageV, v);
            }

            if (call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("VoltageSpectrum.Max takes no arguments.");
                }
                return new BenchNumber(BenchNumericKind.VoltageV, vs.Values.Max(v => v.Magnitude));
            }

            if (call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("VoltageSpectrum.Min takes no arguments.");
                }
                return new BenchNumber(BenchNumericKind.VoltageV, vs.Values.Min(v => v.Magnitude));
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count < 1)
                {
                    throw new InvalidOperationException(
                        "VoltageSpectrum.FindCrossing requires a threshold argument."
                    );
                }

                var threshold = RequireNumber(
                    EvaluateExpr(call.Args[0].Value, locals),
                    "FindCrossing(threshold)"
                );
                if (
                    threshold.Kind != BenchNumericKind.VoltageV
                    && threshold.Kind != BenchNumericKind.Scalar
                )
                {
                    throw new InvalidOperationException(
                        $"FindCrossing: threshold kind '{threshold.Kind}' does not match VoltageSpectrum."
                    );
                }

                var dir = GetNamedSymbol(call, "dir") ?? "falling";
                var cross = GetNamedInt(call, "cross") ?? 1;
                var from = GetNamedFrequency(call, "from", locals) ?? vs.FrequenciesHz.First();
                var to = GetNamedFrequency(call, "to", locals) ?? vs.FrequenciesHz.Last();
                var mags = vs.Values.Select(v => v.Magnitude).ToArray();
                var crossing = FindCrossing(
                    vs.FrequenciesHz,
                    mags,
                    threshold.Value,
                    dir,
                    cross,
                    from,
                    to
                );
                return new BenchNumber(BenchNumericKind.FrequencyHz, crossing);
            }
        }

        if (recv is BenchCurrentSpectrum cs)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 1)
                {
                    throw new InvalidOperationException(
                        "CurrentSpectrum.ValueAt requires 1 argument."
                    );
                }

                var f = RequireFrequency(EvaluateExpr(call.Args[0].Value, locals), "ValueAt");
                var mags = cs.Values.Select(v => v.Magnitude).ToArray();
                var v = InterpolateLogX(cs.FrequenciesHz, mags, f.Value);
                return new BenchNumber(BenchNumericKind.CurrentA, v);
            }

            if (call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("CurrentSpectrum.Max takes no arguments.");
                }
                return new BenchNumber(BenchNumericKind.CurrentA, cs.Values.Max(v => v.Magnitude));
            }

            if (call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("CurrentSpectrum.Min takes no arguments.");
                }
                return new BenchNumber(BenchNumericKind.CurrentA, cs.Values.Min(v => v.Magnitude));
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count < 1)
                {
                    throw new InvalidOperationException(
                        "CurrentSpectrum.FindCrossing requires a threshold argument."
                    );
                }

                var threshold = RequireNumber(
                    EvaluateExpr(call.Args[0].Value, locals),
                    "FindCrossing(threshold)"
                );
                if (
                    threshold.Kind != BenchNumericKind.CurrentA
                    && threshold.Kind != BenchNumericKind.Scalar
                )
                {
                    throw new InvalidOperationException(
                        $"FindCrossing: threshold kind '{threshold.Kind}' does not match CurrentSpectrum."
                    );
                }

                var dir = GetNamedSymbol(call, "dir") ?? "falling";
                var cross = GetNamedInt(call, "cross") ?? 1;
                var from = GetNamedFrequency(call, "from", locals) ?? cs.FrequenciesHz.First();
                var to = GetNamedFrequency(call, "to", locals) ?? cs.FrequenciesHz.Last();
                var mags = cs.Values.Select(v => v.Magnitude).ToArray();
                var crossing = FindCrossing(
                    cs.FrequenciesHz,
                    mags,
                    threshold.Value,
                    dir,
                    cross,
                    from,
                    to
                );
                return new BenchNumber(BenchNumericKind.FrequencyHz, crossing);
            }
        }

        // Waveform methods (time-domain)
        if (recv is BenchWaveform w)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 1)
                {
                    throw new InvalidOperationException("Waveform.ValueAt requires 1 argument.");
                }

                var t = RequireTime(EvaluateExpr(call.Args[0].Value, locals), "ValueAt");
                var v = InterpolateLinearX(w.TimePointsS, w.Values, t.Value);
                return new BenchNumber(w.ValueKind, v);
            }

            if (call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("Waveform.Max takes no arguments.");
                }
                return new BenchNumber(w.ValueKind, w.Values.Max());
            }

            if (call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count != 0)
                {
                    throw new InvalidOperationException("Waveform.Min takes no arguments.");
                }
                return new BenchNumber(w.ValueKind, w.Values.Min());
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Args.Count < 1)
                {
                    throw new InvalidOperationException(
                        "Waveform.FindCrossing requires a threshold argument."
                    );
                }

                var threshold = RequireNumber(
                    EvaluateExpr(call.Args[0].Value, locals),
                    "FindCrossing(threshold)"
                );
                if (
                    threshold.Kind != w.ValueKind
                    && threshold.Kind != BenchNumericKind.Scalar
                    && w.ValueKind != BenchNumericKind.Scalar
                )
                {
                    throw new InvalidOperationException(
                        $"FindCrossing: threshold kind '{threshold.Kind}' does not match waveform kind '{w.ValueKind}'."
                    );
                }

                var dir = GetNamedSymbol(call, "dir") ?? "rising";
                var cross = GetNamedInt(call, "cross") ?? 1;
                var from = GetNamedTime(call, "from", locals) ?? w.TimePointsS.First();
                var to = GetNamedTime(call, "to", locals) ?? w.TimePointsS.Last();

                var crossing = FindCrossingLinear(
                    w.TimePointsS,
                    w.Values,
                    threshold.Value,
                    dir,
                    cross,
                    from,
                    to
                );
                return new BenchNumber(BenchNumericKind.TimeS, crossing);
            }
        }

        throw new InvalidOperationException(
            $"Unsupported method call '{call.Method}' on '{recv.GetType().Name}'."
        );
    }

    private BenchValue EvalVoltage(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        if (call.Args.Count != 2)
        {
            throw new InvalidOperationException("voltage requires (analysis, terminal).");
        }

        var analysisName = ResolveAnalysisName(call.Args[0].Value, locals);
        if (!_analyses.TryGetValue(analysisName, out var analysis))
        {
            throw new InvalidOperationException($"voltage: unknown analysis '{analysisName}'.");
        }

        var terminal = RequireTerminal(EvaluateExpr(call.Args[1].Value, locals), "terminal");

        if (analysis.Tran is not null)
        {
            var t = analysis.Tran.TimePoints;
            var values = new double[t.Length];
            for (var i = 0; i < t.Length; i++)
            {
                values[i] = TerminalVoltage(analysis.Tran, terminal, i);
            }
            return new BenchWaveform(t, values, BenchNumericKind.VoltageV);
        }

        if (analysis.Ac is not null)
        {
            var f = analysis.Ac.FrequenciesHz;
            var values = new Complex[f.Length];
            for (var i = 0; i < f.Length; i++)
            {
                values[i] = TerminalVoltage(analysis.Ac, terminal, i);
            }
            return new BenchVoltageSpectrum(f, values);
        }

        throw new InvalidOperationException($"voltage: unsupported analysis '{analysisName}'.");
    }

    private BenchValue EvalCurrent(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        if (call.Args.Count != 2)
        {
            throw new InvalidOperationException("current requires (analysis, element_pin).");
        }

        var analysisName = ResolveAnalysisName(call.Args[0].Value, locals);
        if (!_analyses.TryGetValue(analysisName, out var analysis))
        {
            throw new InvalidOperationException($"current: unknown analysis '{analysisName}'.");
        }

        var pin = EvaluateExpr(call.Args[1].Value, locals) as BenchElementPinRef;
        if (pin is null)
        {
            throw new InvalidOperationException(
                "current: second argument must be a harness element pin (e.g. harness.VDD.P)."
            );
        }

        var sourceName = "V" + pin.ElementId;
        var sign = pin.Pin.Equals("P", StringComparison.OrdinalIgnoreCase) ? -1.0 : 1.0;

        if (analysis.TranCurrents is not null)
        {
            if (!analysis.TranCurrents.NodeVoltages.TryGetValue(sourceName, out var values))
            {
                throw new InvalidOperationException(
                    $"current(tran, ...): missing current vector for '{sourceName}'."
                );
            }

            var signed = values.Select(v => sign * v).ToArray();
            return new BenchWaveform(
                analysis.TranCurrents.TimePoints,
                signed,
                BenchNumericKind.CurrentA
            );
        }

        if (analysis.AcCurrents is not null)
        {
            if (!analysis.AcCurrents.NodeVoltages.TryGetValue(sourceName, out var values))
            {
                throw new InvalidOperationException(
                    $"current(ac, ...): missing current vector for '{sourceName}'."
                );
            }

            var signed = values.Select(v => sign * v).ToArray();
            return new BenchCurrentSpectrum(analysis.AcCurrents.FrequenciesHz, signed);
        }

        throw new InvalidOperationException($"current: unsupported analysis '{analysisName}'.");
    }

    private bool TryResolveHarnessPin(string raw, out BenchElementPinRef pin)
    {
        pin = default!;
        var dot = raw.LastIndexOf('.');
        if (dot <= 0 || dot >= raw.Length - 1)
        {
            return false;
        }

        var baseName = raw[..dot];
        var pinName = raw[(dot + 1)..];
        if (
            !pinName.Equals("P", StringComparison.OrdinalIgnoreCase)
            && !pinName.Equals("N", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        if (_harnessElementsById.ContainsKey(baseName))
        {
            pin = new BenchElementPinRef(baseName, pinName);
            return true;
        }

        // Allow "harness.<net>.P" by mapping it to the injected VDC source (if any).
        var prefix = "hV_" + baseName;
        var elementId = _harnessElementsById
            .Keys.Where(id => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (elementId is null)
        {
            return false;
        }

        pin = new BenchElementPinRef(elementId, pinName);
        return true;
    }

    private Dictionary<string, BenchValue> BindMeasurementArguments(
        MeasurementDefinition measurement,
        MeasurementCall call,
        Dictionary<string, BenchValue> locals
    )
    {
        var values = new Dictionary<string, BenchValue>(StringComparer.Ordinal);

        var positional = call.Args.Where(a => a.Name is null).ToList();
        var named = call
            .Args.Where(a => a.Name is not null)
            .ToDictionary(a => a.Name!, a => a.Value, StringComparer.Ordinal);

        for (var i = 0; i < measurement.Parameters.Count; i++)
        {
            var p = measurement.Parameters[i];
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
                    $"Missing argument '{p.Name}' for measurement '{measurement.Name}'."
                );
            }
        }

        return values;
    }

    private static string MakeMeasurementCacheKey(
        MeasurementDefinition measurement,
        IReadOnlyDictionary<string, BenchValue>? args
    )
    {
        if (measurement.Parameters.Count == 0 || args is null)
        {
            return measurement.Name;
        }

        var parts = new List<string>(measurement.Parameters.Count);
        foreach (var p in measurement.Parameters)
        {
            if (!args.TryGetValue(p.Name, out var v))
            {
                parts.Add(p.Name + "=<missing>");
                continue;
            }

            parts.Add(p.Name + "=" + BenchValueKey(v));
        }

        return $"{measurement.Name}({string.Join(",", parts)})";
    }

    private static string BenchValueKey(BenchValue v)
    {
        return v switch
        {
            BenchNumber n => $"{n.Kind}:{n.Value.ToString("G17", CultureInfo.InvariantCulture)}",
            BenchSymbol s => "sym:" + s.Name,
            BenchTerminalRef t => "term:" + t.Name,
            BenchAnalysisRef a => "analysis:" + a.Name,
            _ when IsMissing(v) => "missing",
            _ => v.GetType().Name,
        };
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

    private BenchNoiseSpectrum EvalNoise(
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

        return new BenchNoiseSpectrum(
            analysis.Noise.FrequenciesHz,
            analysis.Noise.OutputNoiseVPerRtHz
        );
    }

    private BenchNoiseSpectrum EvalInputReferredNoise(
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

        return new BenchNoiseSpectrum(freqs, values);
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

    private static double TerminalVoltage(TranDataset tran, BenchTerminalRef t, int index)
    {
        if (t.LeafNodes.Count == 0)
        {
            return 0;
        }

        if (t.LeafNodes.Count == 1)
        {
            return tran.NodeVoltages[t.LeafNodes[0]][index];
        }

        return tran.NodeVoltages[t.LeafNodes[0]][index] - tran.NodeVoltages[t.LeafNodes[1]][index];
    }

    private BenchGainSpectrum EvalDb20(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        var g = (BenchGainSpectrum)EvaluateExpr(call.Args[0].Value, locals);
        var values = g
            .Values.Select(v => v > 0 ? 20.0 * Math.Log10(v) : double.NegativeInfinity)
            .ToArray();
        return new BenchGainSpectrum(g.FrequenciesHz, values, BenchNumericKind.VoltageRatioDb);
    }

    private BenchGainSpectrum EvalDb10(MeasurementCall call, Dictionary<string, BenchValue> locals)
    {
        var g = (BenchGainSpectrum)EvaluateExpr(call.Args[0].Value, locals);
        var values = g
            .Values.Select(v => v > 0 ? 10.0 * Math.Log10(v) : double.NegativeInfinity)
            .ToArray();
        return new BenchGainSpectrum(g.FrequenciesHz, values, BenchNumericKind.VoltageRatioDb);
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

        // Capture a representative starting value so we can return a meaningful bound when the
        // requested crossing does not exist within the search interval.
        var yAtStart = values[startIndex] - threshold;

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

        // No crossing found: return a conservative bound rather than failing the bench.
        //
        // Example: GainBandwidth uses a falling 0 dB crossing; if the gain never drops below 0 dB
        // within the sweep, the best lower bound we can report is the sweep stop frequency.
        //
        // Similarly, for a rising crossing, if we start above threshold we report the sweep start
        // (crossing occurred before the interval); otherwise we report the sweep stop.
        if (wantFalling)
        {
            return yAtStart > 0 ? toHz : fromHz;
        }

        return yAtStart < 0 ? toHz : fromHz;
    }

    private static double FindCrossingLinear(
        double[] xs,
        double[] ys,
        double threshold,
        string dir,
        int cross,
        double fromX,
        double toX
    )
    {
        if (cross < 1)
        {
            throw new InvalidOperationException("FindCrossing: cross must be >= 1.");
        }

        var startIndex = Array.FindIndex(xs, x => x >= fromX);
        var endIndex = Array.FindLastIndex(xs, x => x <= toX);
        if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
        {
            throw new InvalidOperationException("FindCrossing: empty search range.");
        }

        var wantFalling = dir.Equals("falling", StringComparison.OrdinalIgnoreCase);
        var count = 0;

        var yAtStart = ys[startIndex] - threshold;

        for (var i = startIndex + 1; i <= endIndex; i++)
        {
            var y0 = ys[i - 1] - threshold;
            var y1 = ys[i] - threshold;
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

            // Interpolate linearly on the time axis.
            var x0 = xs[i - 1];
            var x1 = xs[i];
            var t = y0 == y1 ? 0.0 : y0 / (y0 - y1);
            return x0 + t * (x1 - x0);
        }

        if (wantFalling)
        {
            return yAtStart > 0 ? toX : fromX;
        }

        return yAtStart < 0 ? toX : fromX;
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

    private BenchNumber EvalQuiescentPower(
        MeasurementCall call,
        Dictionary<string, BenchValue> locals
    )
    {
        if (call.Args.Count != 2)
        {
            throw new InvalidOperationException(
                "quiescent_power requires two terminals: quiescent_power(PWR, RET)."
            );
        }

        var pwr = RequireTerminal(EvaluateExpr(call.Args[0].Value, locals), "PWR");
        var ret = RequireTerminal(EvaluateExpr(call.Args[1].Value, locals), "RET");
        if (pwr.LeafNodes.Count == 0 || ret.LeafNodes.Count == 0)
        {
            throw new InvalidOperationException("quiescent_power requires scalar terminals.");
        }

        var pwrNet = pwr.LeafNodes[0];
        var retNet = ret.LeafNodes[0];

        // Find the VDC source that actually applies the rail between (PWR, RET).
        BenchHarnessElement? vsrc = null;
        var sign = 1.0;
        foreach (var e in _harnessElementsById.Values)
        {
            if (!e.Type.Equals("VDC", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryGetPinPair(e, out var p, out var n))
            {
                continue;
            }

            if (
                p.Equals(pwrNet, StringComparison.OrdinalIgnoreCase)
                && n.Equals(retNet, StringComparison.OrdinalIgnoreCase)
            )
            {
                vsrc = e;
                sign = 1.0;
                break;
            }

            if (
                p.Equals(retNet, StringComparison.OrdinalIgnoreCase)
                && n.Equals(pwrNet, StringComparison.OrdinalIgnoreCase)
            )
            {
                vsrc = e;
                sign = -1.0;
                break;
            }
        }

        if (vsrc is null)
        {
            throw new InvalidOperationException(
                $"quiescent_power: no VDC source found between nets '{pwrNet}' and '{retNet}'."
            );
        }

        var sourceName = "V" + vsrc.Id;
        if (!_sourceCurrentsByName.TryGetValue(sourceName, out var currentA))
        {
            throw new InvalidOperationException(
                $"quiescent_power: missing current for source '{sourceName}'."
            );
        }

        var v = GetVdcVoltageOrThrow(vsrc);
        var powerW = sign * v * (-currentA);
        return new BenchNumber(BenchNumericKind.Scalar, powerW);
    }

    private static bool TryGetPinPair(BenchHarnessElement e, out string p, out string n)
    {
        p = string.Empty;
        n = string.Empty;

        if (!e.Pins.TryGetValue("P", out var p0) || string.IsNullOrWhiteSpace(p0))
        {
            return false;
        }
        if (!e.Pins.TryGetValue("N", out var n0) || string.IsNullOrWhiteSpace(n0))
        {
            return false;
        }

        p = p0;
        n = n0;
        return true;
    }

    private static double GetVdcVoltageOrThrow(BenchHarnessElement e)
    {
        BenchValue? v = null;
        if (e.Parameters.TryGetValue("V", out var v0))
        {
            v = v0;
        }
        else if (e.Parameters.TryGetValue("value", out var v1))
        {
            v = v1;
        }
        else if (e.Parameters.Count == 1)
        {
            v = e.Parameters.Values.First();
        }

        if (v is BenchNumber n)
        {
            return n.Value;
        }

        throw new InvalidOperationException(
            $"quiescent_power: VDC source '{e.Id}' missing numeric voltage parameter."
        );
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
        if (xs.Length == 0 || ys.Length == 0)
        {
            throw new InvalidOperationException("InterpolateLogX: empty input.");
        }
        if (xs.Length != ys.Length)
        {
            throw new InvalidOperationException(
                $"InterpolateLogX: length mismatch xs={xs.Length} ys={ys.Length}."
            );
        }

        if (xs.Length == 1)
        {
            return ys[0];
        }

        // Log interpolation requires strictly-positive x values. For DC-like data (x==0) or any
        // non-positive axis values, fall back to linear interpolation.
        if (x <= 0 || xs[0] <= 0)
        {
            return InterpolateLinearX(xs, ys, x);
        }

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

    private static double InterpolateLinearX(double[] xs, double[] ys, double x)
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
        var x0 = xs[i0];
        var x1 = xs[i1];
        var t = x0 == x1 ? 0.0 : (x - x0) / (x1 - x0);
        return ys[i0] + t * (ys[i1] - ys[i0]);
    }

    private static string? GetNamedSymbol(MeasurementCall call, string name) =>
        GetNamedSymbol(call.Args, name);

    private static string? GetNamedSymbol(MeasurementMethodCall call, string name) =>
        GetNamedSymbol(call.Args, name);

    private static string? GetNamedSymbol(IReadOnlyList<MeasurementCallArg> args, string name)
    {
        var arg = args.FirstOrDefault(a =>
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

    private static int? GetNamedInt(MeasurementCall call, string name) =>
        GetNamedInt(call.Args, name);

    private static int? GetNamedInt(MeasurementMethodCall call, string name) =>
        GetNamedInt(call.Args, name);

    private static int? GetNamedInt(IReadOnlyList<MeasurementCallArg> args, string name)
    {
        var arg = args.FirstOrDefault(a =>
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

    private double? GetNamedFrequency(
        MeasurementMethodCall call,
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

        return RequireFrequency(EvaluateExpr(arg.Value, locals), $"{call.Method}:{name}").Value;
    }

    private double? GetNamedTime(
        MeasurementMethodCall call,
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

        return RequireTime(EvaluateExpr(arg.Value, locals), $"{call.Method}:{name}").Value;
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

    private static BenchNumber RequireTime(BenchValue v, string context)
    {
        var n = RequireNumber(v, context);
        if (n.Kind != BenchNumericKind.TimeS)
        {
            throw new InvalidOperationException($"Expected Time for {context}, got {n.Kind}.");
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

    // dut node key resolution is provided by BenchDutNodeResolver during plan compilation.
}
