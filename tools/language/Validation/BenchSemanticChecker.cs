using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

public static class BenchSemanticChecker
{
    public static void Check(CascodeDocument document, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var globalFunctions = document.Functions.ToDictionary(f => f.Name, StringComparer.Ordinal);
        var benchesByName = document.BenchDefinitions.ToDictionary(
            b => b.Name,
            StringComparer.Ordinal
        );

        foreach (var bench in document.BenchDefinitions)
        {
            CheckBenchDefinition(bench, benchesByName, globalFunctions, diagnostics);
        }
    }

    private static void CheckBenchDefinition(
        BenchDefinition bench,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        IReadOnlyDictionary<string, FunctionDefinition> globalFunctions,
        List<Diagnostic> diagnostics
    )
    {
        var scope = new TypeScope(globalFunctions, bench.Functions);

        foreach (var terminal in bench.Terminals)
        {
            if (!scope.TryAddValue(terminal.Name, MeasurementType.Terminal(terminal.Type)))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2001: Duplicate bench terminal '{terminal.Name}' in bench '{bench.Name}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }

        foreach (var analysis in bench.Analyses)
        {
            scope.Values[analysis.Name] = MeasurementType.FromBenchValueType(analysis.Type);
        }

        // Seed measurement types from declaration units for cross-measurement references.
        var measurementTypes = new Dictionary<string, MeasurementType>(StringComparer.Ordinal);
        foreach (var m in bench.Measurements)
        {
            if (!measurementTypes.TryAdd(m.Name, MeasurementType.FromUnit(m.Unit)))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2002: Duplicate measurement '{m.Name}' in bench '{bench.Name}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }

        foreach (var m in bench.Measurements)
        {
            var measurementScope = scope.Clone();
            foreach (var p in m.Parameters)
            {
                measurementScope.Values[p.Name] = MeasurementType.FromBenchValueType(p.Type);
            }

            CheckStatements(
                bench,
                m.Body,
                measurementScope,
                expectedReturn: measurementTypes[m.Name],
                measurementTypes,
                benchesByName,
                diagnostics
            );
        }

        foreach (var analysis in bench.Analyses)
        {
            ValidateAnalysisParams(analysis, scope, measurementTypes, benchesByName, diagnostics);
        }

        foreach (var fn in bench.Functions)
        {
            CheckFunction(bench, fn, scope, measurementTypes, benchesByName, diagnostics);
        }

        foreach (var fn in globalFunctions.Values)
        {
            CheckFunction(bench, fn, scope, measurementTypes, benchesByName, diagnostics);
        }
    }

    private static void CheckFunction(
        BenchDefinition bench,
        FunctionDefinition fn,
        TypeScope benchScope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        var scope = benchScope.Clone();
        foreach (var p in fn.Parameters)
        {
            scope.Values[p.Name] = MeasurementType.FromBenchValueType(p.Type);
        }

        CheckStatements(
            bench,
            fn.Body,
            scope,
            expectedReturn: MeasurementType.FromBenchValueType(fn.ReturnType),
            measurementTypes,
            benchesByName,
            diagnostics
        );
    }

    private static void CheckStatements(
        BenchDefinition bench,
        IReadOnlyList<BenchStatement> statements,
        TypeScope scope,
        MeasurementType expectedReturn,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case BenchVarDecl v:
                    var exprType = InferExprType(v.Expr, scope, measurementTypes, benchesByName);
                    var declaredType = MeasurementType.FromBenchValueType(v.Type);
                    if (!MeasurementType.CanAssign(declaredType, exprType))
                    {
                        diagnostics.Add(
                            new Diagnostic(
                                $"CAS2003: Cannot assign expression of type '{exprType}' to '{declaredType}' for variable '{v.Name}'.",
                                DiagnosticSeverity.Error,
                                "<bench>",
                                1,
                                1
                            )
                        );
                    }
                    scope.Values[v.Name] = declaredType;
                    break;

                case BenchIf i:
                    InferBoolType(i.Condition, scope, measurementTypes, benchesByName, diagnostics);
                    CheckStatements(
                        bench,
                        i.ThenBody.ToList(),
                        scope.Clone(),
                        expectedReturn,
                        measurementTypes,
                        benchesByName,
                        diagnostics
                    );
                    if (i.ElseBody is not null)
                    {
                        CheckStatements(
                            bench,
                            i.ElseBody.ToList(),
                            scope.Clone(),
                            expectedReturn,
                            measurementTypes,
                            benchesByName,
                            diagnostics
                        );
                    }
                    break;

                case BenchReturn r:
                    var returnType = InferExprType(r.Expr, scope, measurementTypes, benchesByName);
                    if (!MeasurementType.CanAssign(expectedReturn, returnType))
                    {
                        diagnostics.Add(
                            new Diagnostic(
                                $"CAS2004: Return type '{returnType}' does not match expected '{expectedReturn}'.",
                                DiagnosticSeverity.Error,
                                "<bench>",
                                1,
                                1
                            )
                        );
                    }
                    break;
            }
        }
    }

    private static void InferBoolType(
        BoolExpr expr,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        if (expr is BoolExists)
        {
            return;
        }

        if (expr is BoolCompare c)
        {
            var left = InferExprType(c.Left, scope, measurementTypes, benchesByName);
            var right = InferExprType(c.Right, scope, measurementTypes, benchesByName);
            if (!MeasurementType.CanAssign(left, right) && !MeasurementType.CanAssign(right, left))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2005: Incompatible types in comparison: '{left}' vs '{right}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private static MeasurementType InferExprType(
        MeasurementExpr expr,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName
    )
    {
        switch (expr)
        {
            case MeasurementNumber:
                return MeasurementType.Scalar();

            case MeasurementQuantity q:
                return MeasurementType.FromQuantity(q.Raw);

            case MeasurementScopedAccess:
                // Scoped values are constraint/env/harness entries and are resolved at runtime.
                // Prefer the type of a measurement with the same name if it exists (constraints.<Metric>).
                if (
                    expr is MeasurementScopedAccess s
                    && s.Ref.Scope == MeasurementScope.Constraints
                    && measurementTypes.TryGetValue(s.Ref.Name, out var inferred)
                )
                {
                    return inferred;
                }
                return MeasurementType.Scalar();

            case MeasurementDutAccess:
                return MeasurementType.Terminal("unknown");

            case MeasurementPath p:
                if (scope.TryResolvePath(p.Path, out var t))
                {
                    return t;
                }
                if (measurementTypes.TryGetValue(p.Path, out var mt))
                {
                    return mt;
                }
                return MeasurementType.Scalar();

            case MeasurementUnary u:
                var ot = InferExprType(u.Operand, scope, measurementTypes, benchesByName);
                return ot;

            case MeasurementBinary b:
                var lt = InferExprType(b.Left, scope, measurementTypes, benchesByName);
                var rt = InferExprType(b.Right, scope, measurementTypes, benchesByName);
                return MeasurementType.InferBinary(b.Op, lt, rt);

            case MeasurementConditional c:
                var tThen = InferExprType(c.ThenExpr, scope, measurementTypes, benchesByName);
                var tElse = InferExprType(c.ElseExpr, scope, measurementTypes, benchesByName);
                if (MeasurementType.CanAssign(tThen, tElse))
                {
                    return tThen;
                }
                if (MeasurementType.CanAssign(tElse, tThen))
                {
                    return tElse;
                }
                return MeasurementType.Scalar();

            case MeasurementCall call:
                return InferCallType(call, scope, measurementTypes, benchesByName);
        }

        return MeasurementType.Scalar();
    }

    private static MeasurementType InferCallType(
        MeasurementCall call,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName
    )
    {
        // Built-in primitives
        switch (call.Name)
        {
            case "transfer":
                return MeasurementType.TransferFunction();
            case "mag":
                return MeasurementType.RealFunction(MeasurementType.Scalar());
            case "db20":
                return MeasurementType.RealFunction(MeasurementType.VoltageRatio());
            case "phase":
                return MeasurementType.RealFunction(MeasurementType.Phase());
            case "eval":
                if (call.Args.Count >= 1)
                {
                    var fType = InferExprType(
                        call.Args[0].Value,
                        scope,
                        measurementTypes,
                        benchesByName
                    );
                    if (
                        fType.Kind == MeasurementTypeKind.RealFunction
                        && fType.FunctionRange is not null
                    )
                    {
                        return fType.FunctionRange;
                    }
                }
                return MeasurementType.Scalar();
            case "find_crossing":
                return MeasurementType.Frequency();
            case "noise":
                return MeasurementType.NoiseFunction();
            case "input_referred_noise":
                return MeasurementType.NoiseFunction();
            case "integrate":
                return MeasurementType.IntegratedNoise();
            case "spot_noise":
                return MeasurementType.NoiseSpectralDensity();
            case "abs":
                if (call.Args.Count >= 1)
                {
                    return InferExprType(
                        call.Args[0].Value,
                        scope,
                        measurementTypes,
                        benchesByName
                    );
                }
                return MeasurementType.Scalar();
            case "sqrt":
                if (call.Args.Count >= 1)
                {
                    return InferExprType(
                        call.Args[0].Value,
                        scope,
                        measurementTypes,
                        benchesByName
                    );
                }
                return MeasurementType.Scalar();
        }

        // Allow measurement calls (e.g. LowpassBandwidth() or IntegratedInputNoise(from=..., to=...)).
        if (measurementTypes.TryGetValue(call.Name, out var mt))
        {
            return mt;
        }

        // User-defined function
        if (scope.TryResolveFunction(call.Name, out var fn))
        {
            return MeasurementType.FromBenchValueType(fn.ReturnType);
        }

        // Unknown calls treated as scalar.
        return MeasurementType.Scalar();
    }

    private static void ValidateAnalysisParams(
        AnalysisDeclaration analysis,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        // Minimal checks for the RFC examples.
        var expected = analysis.Type switch
        {
            BenchValueType.ACAnalysis => new Dictionary<string, MeasurementTypeKind>
            {
                ["start"] = MeasurementTypeKind.Frequency,
                ["stop"] = MeasurementTypeKind.Frequency,
            },
            BenchValueType.NoiseAnalysis => new Dictionary<string, MeasurementTypeKind>
            {
                ["start"] = MeasurementTypeKind.Frequency,
                ["stop"] = MeasurementTypeKind.Frequency,
                ["output"] = MeasurementTypeKind.Terminal,
            },
            _ => new Dictionary<string, MeasurementTypeKind>(),
        };

        foreach (var (name, expr) in analysis.Parameters)
        {
            if (!expected.TryGetValue(name, out var expectedKind))
            {
                continue;
            }

            var actual = InferExprType(expr, scope, measurementTypes, benchesByName);
            if (actual.Kind != expectedKind)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2006: Analysis parameter '{analysis.Name}.{name}' expects '{expectedKind}' but got '{actual}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private sealed class TypeScope
    {
        private readonly Dictionary<string, FunctionDefinition> _functions;
        public Dictionary<string, MeasurementType> Values { get; } = new(StringComparer.Ordinal);

        public TypeScope(
            IReadOnlyDictionary<string, FunctionDefinition> globalFunctions,
            IReadOnlyList<FunctionDefinition> benchFunctions
        )
        {
            _functions = new Dictionary<string, FunctionDefinition>(
                globalFunctions,
                StringComparer.Ordinal
            );
            foreach (var fn in benchFunctions)
            {
                _functions[fn.Name] = fn;
            }
        }

        public bool TryAddValue(string name, MeasurementType type) => Values.TryAdd(name, type);

        public bool TryResolveFunction(string name, out FunctionDefinition fn) =>
            _functions.TryGetValue(name, out fn!);

        public TypeScope Clone()
        {
            var clone = new TypeScope(_functions, Array.Empty<FunctionDefinition>());
            foreach (var kvp in Values)
            {
                clone.Values[kvp.Key] = kvp.Value;
            }
            return clone;
        }

        public bool TryResolvePath(string path, out MeasurementType type)
        {
            // Allow analysis property access like "ac.start".
            var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && Values.TryGetValue(parts[0], out var baseType))
            {
                if (
                    baseType.Kind == MeasurementTypeKind.ACAnalysis
                    || baseType.Kind == MeasurementTypeKind.NoiseAnalysis
                    || baseType.Kind == MeasurementTypeKind.DCAnalysis
                    || baseType.Kind == MeasurementTypeKind.TranAnalysis
                    || baseType.Kind == MeasurementTypeKind.STBAnalysis
                )
                {
                    if (string.Equals(parts[1], "start", StringComparison.OrdinalIgnoreCase))
                    {
                        type = MeasurementType.Frequency();
                        return true;
                    }
                    if (string.Equals(parts[1], "stop", StringComparison.OrdinalIgnoreCase))
                    {
                        type = MeasurementType.Frequency();
                        return true;
                    }
                }
            }

            // Bench terminals may be referenced as "IN.P" etc; treat as Terminal.
            if (
                parts.Length >= 2
                && Values.TryGetValue(parts[0], out baseType)
                && baseType.Kind == MeasurementTypeKind.Terminal
            )
            {
                type = baseType;
                return true;
            }

            if (Values.TryGetValue(path, out type!))
            {
                return true;
            }

            type = MeasurementType.Scalar();
            return false;
        }
    }

    private enum MeasurementTypeKind
    {
        Bool,
        Scalar,
        Frequency,
        VoltageRatio,
        Phase,
        Voltage,
        Current,
        Impedance,
        Capacitance,
        Inductance,
        Time,
        TransferFunction,
        RealFunction,
        NoiseFunction,
        NoiseSpectralDensity,
        IntegratedNoise,
        Terminal,
        ACAnalysis,
        DCAnalysis,
        TranAnalysis,
        NoiseAnalysis,
        STBAnalysis,
    }

    private sealed record MeasurementType(
        MeasurementTypeKind Kind,
        MeasurementType? FunctionRange = null,
        string? TerminalDomain = null
    )
    {
        public static MeasurementType Bool() => new(MeasurementTypeKind.Bool);

        public static MeasurementType Scalar() => new(MeasurementTypeKind.Scalar);

        public static MeasurementType Frequency() => new(MeasurementTypeKind.Frequency);

        public static MeasurementType VoltageRatio() => new(MeasurementTypeKind.VoltageRatio);

        public static MeasurementType Phase() => new(MeasurementTypeKind.Phase);

        public static MeasurementType Voltage() => new(MeasurementTypeKind.Voltage);

        public static MeasurementType Current() => new(MeasurementTypeKind.Current);

        public static MeasurementType Impedance() => new(MeasurementTypeKind.Impedance);

        public static MeasurementType Capacitance() => new(MeasurementTypeKind.Capacitance);

        public static MeasurementType Inductance() => new(MeasurementTypeKind.Inductance);

        public static MeasurementType Time() => new(MeasurementTypeKind.Time);

        public static MeasurementType TransferFunction() =>
            new(MeasurementTypeKind.TransferFunction);

        public static MeasurementType RealFunction(MeasurementType? range) =>
            new(MeasurementTypeKind.RealFunction, FunctionRange: range);

        public static MeasurementType NoiseFunction() => new(MeasurementTypeKind.NoiseFunction);

        public static MeasurementType NoiseSpectralDensity() =>
            new(MeasurementTypeKind.NoiseSpectralDensity);

        public static MeasurementType IntegratedNoise() => new(MeasurementTypeKind.IntegratedNoise);

        public static MeasurementType Terminal(string domain) =>
            new(MeasurementTypeKind.Terminal, TerminalDomain: domain);

        public static MeasurementType FromBenchValueType(BenchValueType type) =>
            type switch
            {
                BenchValueType.Bool => Bool(),
                BenchValueType.Terminal => Terminal("unknown"),
                BenchValueType.Scalar => Scalar(),
                BenchValueType.Frequency => Frequency(),
                BenchValueType.VoltageRatio => VoltageRatio(),
                BenchValueType.Phase => Phase(),
                BenchValueType.Voltage => Voltage(),
                BenchValueType.Current => Current(),
                BenchValueType.Impedance => Impedance(),
                BenchValueType.Capacitance => Capacitance(),
                BenchValueType.Inductance => Inductance(),
                BenchValueType.TransferFunction => TransferFunction(),
                // RealFunction's range is not part of the surface type; we treat it as "any"
                // and only constrain it when required by a primitive (e.g. find_crossing).
                BenchValueType.RealFunction => RealFunction(null),
                BenchValueType.NoiseFunction => NoiseFunction(),
                BenchValueType.NoiseSpectralDensity => NoiseSpectralDensity(),
                BenchValueType.IntegratedNoise => IntegratedNoise(),
                BenchValueType.ACAnalysis => new MeasurementType(MeasurementTypeKind.ACAnalysis),
                BenchValueType.DCAnalysis => new MeasurementType(MeasurementTypeKind.DCAnalysis),
                BenchValueType.TranAnalysis => new MeasurementType(
                    MeasurementTypeKind.TranAnalysis
                ),
                BenchValueType.NoiseAnalysis => new MeasurementType(
                    MeasurementTypeKind.NoiseAnalysis
                ),
                BenchValueType.STBAnalysis => new MeasurementType(MeasurementTypeKind.STBAnalysis),
                _ => Scalar(),
            };

        public static MeasurementType FromUnit(string unit)
        {
            if (unit.EndsWith("/rtHz", StringComparison.OrdinalIgnoreCase))
            {
                return NoiseSpectralDensity();
            }
            if (unit.EndsWith("rms", StringComparison.OrdinalIgnoreCase))
            {
                return IntegratedNoise();
            }
            if (unit.EndsWith("Hz", StringComparison.OrdinalIgnoreCase))
            {
                return Frequency();
            }
            if (unit.Equals("dB", StringComparison.OrdinalIgnoreCase))
            {
                return VoltageRatio();
            }
            if (unit.Equals("deg", StringComparison.OrdinalIgnoreCase))
            {
                return Phase();
            }
            return Scalar();
        }

        public static MeasurementType FromQuantity(string raw)
        {
            if (raw.EndsWith("dB", StringComparison.OrdinalIgnoreCase))
            {
                return VoltageRatio();
            }
            if (raw.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
            {
                return Phase();
            }
            if (raw.EndsWith("Hz", StringComparison.OrdinalIgnoreCase))
            {
                return Frequency();
            }
            if (raw.EndsWith("Ohm", StringComparison.OrdinalIgnoreCase))
            {
                return Impedance();
            }
            if (raw.EndsWith("F", StringComparison.OrdinalIgnoreCase))
            {
                return Capacitance();
            }
            if (raw.EndsWith("H", StringComparison.OrdinalIgnoreCase))
            {
                return Inductance();
            }
            if (
                raw.EndsWith("V", StringComparison.OrdinalIgnoreCase)
                || raw.Contains("Vpp", StringComparison.OrdinalIgnoreCase)
            )
            {
                return Voltage();
            }
            if (raw.EndsWith("A", StringComparison.OrdinalIgnoreCase))
            {
                return Current();
            }
            if (raw.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                return Time();
            }
            if (raw.EndsWith("/rtHz", StringComparison.OrdinalIgnoreCase))
            {
                return NoiseSpectralDensity();
            }
            if (raw.EndsWith("rms", StringComparison.OrdinalIgnoreCase))
            {
                return IntegratedNoise();
            }

            return Scalar();
        }

        public static bool CanAssign(MeasurementType target, MeasurementType value)
        {
            if (target.Kind == value.Kind)
            {
                if (target.Kind != MeasurementTypeKind.RealFunction)
                {
                    return true;
                }

                // RealFunction is compatible if ranges are compatible.
                if (target.FunctionRange is null || value.FunctionRange is null)
                {
                    return true;
                }
                return CanAssign(target.FunctionRange, value.FunctionRange);
            }

            // Allow scalar to flow into most numeric physical types.
            if (value.Kind == MeasurementTypeKind.Scalar)
            {
                return target.Kind
                    is MeasurementTypeKind.Scalar
                        or MeasurementTypeKind.Frequency
                        or MeasurementTypeKind.VoltageRatio
                        or MeasurementTypeKind.Phase
                        or MeasurementTypeKind.Voltage
                        or MeasurementTypeKind.Current
                        or MeasurementTypeKind.Impedance
                        or MeasurementTypeKind.Capacitance
                        or MeasurementTypeKind.Inductance
                        or MeasurementTypeKind.Time;
            }

            return false;
        }

        public static MeasurementType InferBinary(
            string op,
            MeasurementType left,
            MeasurementType right
        )
        {
            if (op is "+" or "-")
            {
                return left.Kind == right.Kind ? left : Scalar();
            }

            // Multiplication/division with scalar preserves the other type.
            if (left.Kind == MeasurementTypeKind.Scalar)
            {
                return right;
            }
            if (right.Kind == MeasurementTypeKind.Scalar)
            {
                return left;
            }

            // Keep common cases used in the stdlib examples.
            if (left.Kind == right.Kind && left.Kind == MeasurementTypeKind.Frequency)
            {
                return Frequency();
            }

            return Scalar();
        }

        public override string ToString()
        {
            return Kind switch
            {
                MeasurementTypeKind.RealFunction when FunctionRange is not null =>
                    $"RealFunction<{FunctionRange}>",
                MeasurementTypeKind.Terminal when TerminalDomain is not null =>
                    $"Terminal<{TerminalDomain}>",
                _ => Kind.ToString(),
            };
        }
    }
}
