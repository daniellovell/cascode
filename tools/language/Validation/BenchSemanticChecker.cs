using System;
using System.Collections.Generic;
using System.Globalization;
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
            // Bench inheritance resolver removes extends/base relationships and abstract benches
            // from linked documents. For partial source documents (with includes), skip benches
            // that are not fully concrete to avoid type-checking placeholders.
            if (
                bench.IsAbstract
                || bench.BaseBench is not null
                || bench.Terminals.Any(t => t.Type is null)
            )
            {
                continue;
            }

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

        // Add bench parameters to the type scope so they can be resolved in analysis expressions.
        foreach (var param in bench.Parameters)
        {
            scope.Values[param.Name] = MeasurementType.FromBenchValueType(param.Type);
        }

        foreach (var terminal in bench.Terminals)
        {
            if (!scope.TryAddValue(terminal.Name, MeasurementType.Terminal(terminal.Type!)))
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

        ValidatePortDeclarations(bench, scope, measurementTypes, benchesByName, diagnostics);
        ValidateSParameterAnalysisDeclarations(bench, diagnostics);

        foreach (var analysis in bench.Analyses)
        {
            scope.Values[analysis.Name] = MeasurementType.FromBenchValueType(analysis.Type);
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

        ValidateMeasurementCycles(bench, globalFunctions, diagnostics);
    }

    private static void ValidateMeasurementCycles(
        BenchDefinition bench,
        IReadOnlyDictionary<string, FunctionDefinition> globalFunctions,
        List<Diagnostic> diagnostics
    )
    {
        var measurementNames = bench
            .Measurements.Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var functionNames = globalFunctions
            .Values.Concat(bench.Functions)
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var functionsByName = new Dictionary<string, FunctionDefinition>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var fn in globalFunctions.Values)
        {
            functionsByName[fn.Name] = fn;
        }
        foreach (var fn in bench.Functions)
        {
            functionsByName[fn.Name] = fn;
        }

        var functionDeps = new Dictionary<
            string,
            (HashSet<string> Measurements, HashSet<string> Functions)
        >(StringComparer.OrdinalIgnoreCase);
        foreach (var fn in functionsByName.Values)
        {
            functionDeps[fn.Name] = CollectDirectDeps(fn.Body, measurementNames, functionNames);
        }

        var measurementDeps = new Dictionary<string, HashSet<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var m in bench.Measurements)
        {
            var direct = CollectDirectDeps(m.Body, measurementNames, functionNames);

            var deps = new HashSet<string>(direct.Measurements, StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>(direct.Functions);
            var visitedFns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (stack.Count > 0)
            {
                var fnName = stack.Pop();
                if (!visitedFns.Add(fnName))
                {
                    continue;
                }
                if (!functionDeps.TryGetValue(fnName, out var dep))
                {
                    continue;
                }

                foreach (var mm in dep.Measurements)
                {
                    deps.Add(mm);
                }
                foreach (var ff in dep.Functions)
                {
                    stack.Push(ff);
                }
            }

            measurementDeps[m.Name] = deps;
        }

        // Standard DFS cycle detection on the measurement-only graph.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var m in bench.Measurements)
        {
            if (visited.Contains(m.Name))
            {
                continue;
            }

            if (TryFindCycle(m.Name, measurementDeps, visited, visiting, path, out var cycle))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2007: Cyclic measurement dependency detected in bench '{bench.Name}': {string.Join(" -> ", cycle)}",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private static void ValidatePortDeclarations(
        BenchDefinition bench,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        var namesByPortNumber = new Dictionary<int, string>();
        foreach (var portInstance in EnumeratePortInstances(bench))
        {
            if (!TryReadPositivePortNumber(portInstance, out var portNumber))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"Port number must be a positive integer; got {ReadPortNumberToken(portInstance) ?? "-1"}.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                continue;
            }

            if (
                namesByPortNumber.TryGetValue(portNumber, out var priorName)
                && !string.Equals(priorName, portInstance.Id, StringComparison.Ordinal)
            )
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"Duplicate port number {portNumber}: '{priorName}' and '{portInstance.Id}'",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
            else
            {
                namesByPortNumber[portNumber] = portInstance.Id;
            }

            if (
                TryReadPortImpedanceToken(portInstance, out var impedanceText)
                && !IsRealValuedPortImpedance(impedanceText, scope, measurementTypes, benchesByName)
            )
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"Port impedance must be real-valued: invalid port impedance on port {portNumber}.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }

        if (
            namesByPortNumber.Count > 0
            && (
                namesByPortNumber.Keys.Min() != 1
                || namesByPortNumber.Keys.Max() != namesByPortNumber.Count
            )
        )
        {
            diagnostics.Add(
                new Diagnostic(
                    "Incorrect port ordering, ports must be numbered sequentially from 1",
                    DiagnosticSeverity.Error,
                    "<bench>",
                    1,
                    1
                )
            );
        }
    }

    private static bool IsRealValuedPortImpedance(
        string impedanceText,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName
    )
    {
        if (
            !CascodeAstBuilder.TryParseMeasurementExprText(impedanceText, out var expr, out _)
            || expr is null
        )
        {
            return false;
        }

        var inferredType = InferExprType(expr, scope, measurementTypes, benchesByName);
        return inferredType.Kind == MeasurementTypeKind.Impedance;
    }

    private static void ValidateSParameterAnalysisDeclarations(
        BenchDefinition bench,
        List<Diagnostic> diagnostics
    )
    {
        var hasPorts = EnumeratePortInstances(bench).Any();
        foreach (var analysis in bench.Analyses.Where(a => a.Type == BenchValueType.SPAnalysis))
        {
            if (!hasPorts)
            {
                diagnostics.Add(
                    new Diagnostic(
                        "SPAnalysis requires at least one Port instance.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private static IEnumerable<InstanceDeclaration> EnumeratePortInstances(BenchDefinition bench)
    {
        if (bench.Fill is null)
        {
            yield break;
        }

        foreach (var instance in bench.Fill.Instances)
        {
            if (instance.Type.Equals("Port", StringComparison.OrdinalIgnoreCase))
            {
                yield return instance;
            }
        }
    }

    private static bool TryReadPositivePortNumber(
        InstanceDeclaration portInstance,
        out int portNumber
    )
    {
        portNumber = 0;
        if (!portInstance.Params.TryGetValue("N", out var nValue))
        {
            return false;
        }

        if (!TryReadIntegerText(ReadParamToken(nValue), out var parsed))
        {
            return false;
        }

        if (parsed <= 0)
        {
            return false;
        }

        portNumber = parsed;
        return true;
    }

    private static string? ReadPortNumberToken(InstanceDeclaration portInstance)
    {
        return portInstance.Params.TryGetValue("N", out var nValue) ? ReadParamToken(nValue) : null;
    }

    private static bool TryReadPortImpedanceToken(
        InstanceDeclaration portInstance,
        out string impedanceText
    )
    {
        impedanceText = string.Empty;
        if (!portInstance.Params.TryGetValue("Z", out var zValue))
        {
            return false;
        }

        var raw = ReadParamToken(zValue);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        impedanceText = raw;
        return true;
    }

    private static string? ReadParamToken(ParamValue value)
    {
        if (!string.IsNullOrWhiteSpace(value.Numeric))
        {
            return value.Numeric;
        }

        if (!string.IsNullOrWhiteSpace(value.Symbolic))
        {
            return value.Symbolic;
        }

        return value.Literal;
    }

    private static bool TryReadIntegerText(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        )
        {
            return false;
        }

        if (parsed != Math.Round(parsed) || parsed < int.MinValue || parsed > int.MaxValue)
        {
            return false;
        }

        value = (int)parsed;
        return true;
    }

    private static bool TryFindCycle(
        string current,
        IReadOnlyDictionary<string, HashSet<string>> deps,
        HashSet<string> visited,
        HashSet<string> visiting,
        List<string> path,
        out IReadOnlyList<string> cycle
    )
    {
        cycle = Array.Empty<string>();
        if (visited.Contains(current))
        {
            return false;
        }

        if (!visiting.Add(current))
        {
            var idx = path.FindIndex(p => p.Equals(current, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                cycle = path.Skip(idx).Concat(new[] { current }).ToArray();
            }
            else
            {
                cycle = new[] { current, current };
            }
            return true;
        }

        path.Add(current);
        if (deps.TryGetValue(current, out var nexts))
        {
            foreach (var next in nexts)
            {
                if (TryFindCycle(next, deps, visited, visiting, path, out cycle))
                {
                    return true;
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        visiting.Remove(current);
        visited.Add(current);
        return false;
    }

    private static (HashSet<string> Measurements, HashSet<string> Functions) CollectDirectDeps(
        IReadOnlyList<BenchStatement> body,
        IReadOnlySet<string> measurementNames,
        IReadOnlySet<string> functionNames
    )
    {
        var ms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var stmt in body)
        {
            CollectDeps(stmt, measurementNames, functionNames, ms, fs);
        }

        return (ms, fs);
    }

    private static void CollectDeps(
        BenchStatement stmt,
        IReadOnlySet<string> measurementNames,
        IReadOnlySet<string> functionNames,
        HashSet<string> calledMeasurements,
        HashSet<string> calledFunctions
    )
    {
        switch (stmt)
        {
            case BenchVarDecl v:
                CollectDeps(
                    v.Expr,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                break;
            case BenchReturn r:
                CollectDeps(
                    r.Expr,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                break;
            case BenchIf i:
                CollectDeps(
                    i.Condition,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                foreach (var s in i.ThenBody)
                {
                    CollectDeps(
                        s,
                        measurementNames,
                        functionNames,
                        calledMeasurements,
                        calledFunctions
                    );
                }
                if (i.ElseBody is not null)
                {
                    foreach (var s in i.ElseBody)
                    {
                        CollectDeps(
                            s,
                            measurementNames,
                            functionNames,
                            calledMeasurements,
                            calledFunctions
                        );
                    }
                }
                break;
        }
    }

    private static void CollectDeps(
        BoolExpr expr,
        IReadOnlySet<string> measurementNames,
        IReadOnlySet<string> functionNames,
        HashSet<string> calledMeasurements,
        HashSet<string> calledFunctions
    )
    {
        switch (expr)
        {
            case BoolCompare c:
                CollectDeps(
                    c.Left,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                CollectDeps(
                    c.Right,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                break;
        }
    }

    private static void CollectDeps(
        MeasurementExpr expr,
        IReadOnlySet<string> measurementNames,
        IReadOnlySet<string> functionNames,
        HashSet<string> calledMeasurements,
        HashSet<string> calledFunctions
    )
    {
        switch (expr)
        {
            case MeasurementCall c:
                if (measurementNames.Contains(c.Name))
                {
                    calledMeasurements.Add(c.Name);
                }
                else if (functionNames.Contains(c.Name))
                {
                    calledFunctions.Add(c.Name);
                }
                foreach (var a in c.Args)
                {
                    CollectDeps(
                        a.Value,
                        measurementNames,
                        functionNames,
                        calledMeasurements,
                        calledFunctions
                    );
                }
                break;
            case MeasurementMethodCall m:
                CollectDeps(
                    m.Receiver,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                foreach (var a in m.Args)
                {
                    CollectDeps(
                        a.Value,
                        measurementNames,
                        functionNames,
                        calledMeasurements,
                        calledFunctions
                    );
                }
                break;
            case MeasurementBinary b:
                CollectDeps(
                    b.Left,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                CollectDeps(
                    b.Right,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                break;
            case MeasurementUnary u:
                CollectDeps(
                    u.Operand,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                break;
            case MeasurementConditional c:
                CollectDeps(
                    c.Condition,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                CollectDeps(
                    c.ThenExpr,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                CollectDeps(
                    c.ElseExpr,
                    measurementNames,
                    functionNames,
                    calledMeasurements,
                    calledFunctions
                );
                break;
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
                    ValidateBuiltinCalls(
                        bench,
                        v.Expr,
                        scope,
                        measurementTypes,
                        benchesByName,
                        diagnostics
                    );
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
                    ValidateBuiltinCalls(
                        bench,
                        r.Expr,
                        scope,
                        measurementTypes,
                        benchesByName,
                        diagnostics
                    );
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
                if (
                    expr is MeasurementScopedAccess hs
                    && hs.Ref.Scope == MeasurementScope.Harness
                    && hs.Ref.Name.Contains('.', StringComparison.Ordinal)
                )
                {
                    // Harness pin references (e.g. harness.VDD.P) are element pins.
                    return MeasurementType.ElementPin();
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

            case MeasurementBenchMeasurementRef:
                // Cross-bench references are resolved from constraints, not from within benches.
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

            case MeasurementMethodCall m:
                return InferMethodCallType(m, scope, measurementTypes, benchesByName);
        }

        return MeasurementType.Scalar();
    }

    private static MeasurementType InferMethodCallType(
        MeasurementMethodCall call,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName
    )
    {
        var recv = InferExprType(call.Receiver, scope, measurementTypes, benchesByName);

        if (
            (
                call.Method.Equals("From", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("To", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("Range", StringComparison.OrdinalIgnoreCase)
            ) && IsArrayKind(recv.Kind)
        )
        {
            return recv;
        }

        if (recv.Kind == MeasurementTypeKind.TransferFunction)
        {
            if (call.Method.Equals("Mag", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.GainSpectrum();
            }

            if (call.Method.Equals("Phase", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.PhaseSpectrum();
            }
        }

        if (recv.Kind == MeasurementTypeKind.SParameterMatrix)
        {
            if (call.Method.Equals("S", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.TransferFunction();
            }

            if (
                call.Method.Equals("ReturnLoss", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("InsertionLoss", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("Isolation", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("MSG", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("MAG", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("NF", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("NFmin", StringComparison.OrdinalIgnoreCase)
            )
            {
                return MeasurementType.GainSpectrum();
            }

            if (call.Method.Equals("Rn", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.ImpedanceSpectrum();
            }

            if (
                call.Method.Equals("VSWR", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("StabilityK", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("MuFactor", StringComparison.OrdinalIgnoreCase)
            )
            {
                return MeasurementType.ScalarSpectrum();
            }

            if (call.Method.Equals("GroupDelay", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.TimeSpectrum();
            }
        }

        if (recv.Kind == MeasurementTypeKind.GainSpectrum)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.VoltageRatio();
            }

            if (
                call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase)
            )
            {
                return MeasurementType.VoltageRatio();
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Frequency();
            }
        }

        if (recv.Kind == MeasurementTypeKind.ScalarSpectrum)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Scalar();
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Frequency();
            }
        }

        if (recv.Kind == MeasurementTypeKind.TimeSpectrum)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Time();
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Frequency();
            }
        }

        if (recv.Kind == MeasurementTypeKind.ImpedanceSpectrum)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Impedance();
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Frequency();
            }
        }

        if (recv.Kind == MeasurementTypeKind.PhaseSpectrum)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Phase();
            }

            if (
                call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase)
            )
            {
                return MeasurementType.Phase();
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Frequency();
            }
        }

        if (recv.Kind == MeasurementTypeKind.ComplexVoltageSpectrum)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.ComplexVoltage();
            }

            if (call.Method.Equals("Mag", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.VoltageSpectrum();
            }

            if (call.Method.Equals("Phase", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.PhaseSpectrum();
            }
        }

        if (recv.Kind == MeasurementTypeKind.ComplexCurrentSpectrum)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.ComplexCurrent();
            }

            if (call.Method.Equals("Mag", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.CurrentSpectrum();
            }

            if (call.Method.Equals("Phase", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.PhaseSpectrum();
            }
        }

        if (recv.Kind == MeasurementTypeKind.VoltageSpectrum)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Voltage();
            }

            if (
                call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase)
            )
            {
                return MeasurementType.Voltage();
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Frequency();
            }
        }

        if (recv.Kind == MeasurementTypeKind.CurrentSpectrum)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Current();
            }

            if (
                call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase)
            )
            {
                return MeasurementType.Current();
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Frequency();
            }
        }

        if (recv.Kind == MeasurementTypeKind.ComplexVoltage)
        {
            if (call.Method.Equals("Mag", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Voltage();
            }

            if (call.Method.Equals("Phase", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Phase();
            }
        }

        if (recv.Kind == MeasurementTypeKind.ComplexCurrent)
        {
            if (call.Method.Equals("Mag", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Current();
            }

            if (call.Method.Equals("Phase", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Phase();
            }
        }

        if (recv.Kind == MeasurementTypeKind.NoiseSpectrum)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.NoiseSpectralDensity();
            }

            if (call.Method.Equals("Integrate", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.IntegratedNoise();
            }

            if (
                call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase)
            )
            {
                return MeasurementType.NoiseSpectralDensity();
            }
        }

        if (recv.Kind == MeasurementTypeKind.Impedance)
        {
            if (
                call.Method.Equals("DiffToShunt", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("ShuntToDiff", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("SplitParallel", StringComparison.OrdinalIgnoreCase)
            )
            {
                return MeasurementType.Impedance();
            }
        }

        if (recv.Kind == MeasurementTypeKind.VoltageWaveform)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Voltage();
            }

            if (
                call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase)
            )
            {
                return MeasurementType.Voltage();
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Time();
            }
        }

        if (recv.Kind == MeasurementTypeKind.CurrentWaveform)
        {
            if (call.Method.Equals("ValueAt", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Current();
            }

            if (
                call.Method.Equals("Max", StringComparison.OrdinalIgnoreCase)
                || call.Method.Equals("Min", StringComparison.OrdinalIgnoreCase)
            )
            {
                return MeasurementType.Current();
            }

            if (call.Method.Equals("FindCrossing", StringComparison.OrdinalIgnoreCase))
            {
                return MeasurementType.Time();
            }
        }

        // Unknown methods: treat as scalar for now and let runtime produce a better error.
        return MeasurementType.Scalar();
    }

    private static bool IsArrayKind(MeasurementTypeKind kind) =>
        kind == MeasurementTypeKind.GainSpectrum
        || kind == MeasurementTypeKind.ScalarSpectrum
        || kind == MeasurementTypeKind.TimeSpectrum
        || kind == MeasurementTypeKind.PhaseSpectrum
        || kind == MeasurementTypeKind.ComplexVoltageSpectrum
        || kind == MeasurementTypeKind.ComplexCurrentSpectrum
        || kind == MeasurementTypeKind.VoltageSpectrum
        || kind == MeasurementTypeKind.CurrentSpectrum
        || kind == MeasurementTypeKind.NoiseSpectrum
        || kind == MeasurementTypeKind.VoltageWaveform
        || kind == MeasurementTypeKind.CurrentWaveform
        || kind == MeasurementTypeKind.TransferFunction;

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
            case "voltage":
            {
                if (call.Args.Count >= 1)
                {
                    var a = InferExprType(
                        call.Args[0].Value,
                        scope,
                        measurementTypes,
                        benchesByName
                    );
                    return a.Kind switch
                    {
                        MeasurementTypeKind.ACAnalysis => MeasurementType.ComplexVoltageSpectrum(),
                        MeasurementTypeKind.DCAnalysis => MeasurementType.Voltage(),
                        MeasurementTypeKind.TranAnalysis => MeasurementType.VoltageWaveform(),
                        _ => MeasurementType.Scalar(),
                    };
                }
                return MeasurementType.Scalar();
            }
            case "current":
            {
                if (call.Args.Count >= 1)
                {
                    var a = InferExprType(
                        call.Args[0].Value,
                        scope,
                        measurementTypes,
                        benchesByName
                    );
                    return a.Kind switch
                    {
                        MeasurementTypeKind.ACAnalysis => MeasurementType.ComplexCurrentSpectrum(),
                        MeasurementTypeKind.TranAnalysis => MeasurementType.CurrentWaveform(),
                        _ => MeasurementType.Scalar(),
                    };
                }
                return MeasurementType.Scalar();
            }
            case "sparam":
                return MeasurementType.SParameterMatrix();
            case "db20":
                return MeasurementType.GainSpectrum();
            case "db10":
                return MeasurementType.GainSpectrum();
            case "noise":
                return MeasurementType.NoiseSpectrum();
            case "input_referred_noise":
                return MeasurementType.NoiseSpectrum();
            case "quiescent_power":
                return MeasurementType.Scalar();
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
            case "period":
                // period(Frequency) returns Time
                return MeasurementType.Time();
            case "op_param":
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

    private static void ValidateBuiltinCalls(
        BenchDefinition bench,
        MeasurementExpr expr,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        switch (expr)
        {
            case MeasurementBinary b:
                ValidateBuiltinCalls(
                    bench,
                    b.Left,
                    scope,
                    measurementTypes,
                    benchesByName,
                    diagnostics
                );
                ValidateBuiltinCalls(
                    bench,
                    b.Right,
                    scope,
                    measurementTypes,
                    benchesByName,
                    diagnostics
                );
                return;
            case MeasurementUnary u:
                ValidateBuiltinCalls(
                    bench,
                    u.Operand,
                    scope,
                    measurementTypes,
                    benchesByName,
                    diagnostics
                );
                return;
            case MeasurementConditional c:
                ValidateBuiltinCallsInBoolExpr(
                    bench,
                    c.Condition,
                    scope,
                    measurementTypes,
                    benchesByName,
                    diagnostics
                );
                ValidateBuiltinCalls(
                    bench,
                    c.ThenExpr,
                    scope,
                    measurementTypes,
                    benchesByName,
                    diagnostics
                );
                ValidateBuiltinCalls(
                    bench,
                    c.ElseExpr,
                    scope,
                    measurementTypes,
                    benchesByName,
                    diagnostics
                );
                return;
            case MeasurementCall call:
                ValidateBuiltinCall(call, scope, measurementTypes, benchesByName, diagnostics);
                foreach (var a in call.Args)
                {
                    ValidateBuiltinCalls(
                        bench,
                        a.Value,
                        scope,
                        measurementTypes,
                        benchesByName,
                        diagnostics
                    );
                }
                return;
            case MeasurementMethodCall m:
                ValidateBuiltinCalls(
                    bench,
                    m.Receiver,
                    scope,
                    measurementTypes,
                    benchesByName,
                    diagnostics
                );
                foreach (var a in m.Args)
                {
                    ValidateBuiltinCalls(
                        bench,
                        a.Value,
                        scope,
                        measurementTypes,
                        benchesByName,
                        diagnostics
                    );
                }
                ValidateSParameterMethodCall(
                    bench,
                    m,
                    scope,
                    measurementTypes,
                    benchesByName,
                    diagnostics
                );
                return;
        }
    }

    private static void ValidateBuiltinCallsInBoolExpr(
        BenchDefinition bench,
        BoolExpr expr,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        switch (expr)
        {
            case BoolCompare c:
                ValidateBuiltinCalls(
                    bench,
                    c.Left,
                    scope,
                    measurementTypes,
                    benchesByName,
                    diagnostics
                );
                ValidateBuiltinCalls(
                    bench,
                    c.Right,
                    scope,
                    measurementTypes,
                    benchesByName,
                    diagnostics
                );
                break;
            case BoolTruthy t:
                ValidateBuiltinCalls(
                    bench,
                    t.Expr,
                    scope,
                    measurementTypes,
                    benchesByName,
                    diagnostics
                );
                break;
        }
    }

    private static void ValidateBuiltinCall(
        MeasurementCall call,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        if (call.Name.Equals("op_param", StringComparison.OrdinalIgnoreCase))
        {
            if (call.Args.Count != 3)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2008: op_param requires exactly 3 arguments, got {call.Args.Count}.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                return;
            }

            var analysisType = InferExprType(
                call.Args[0].Value,
                scope,
                measurementTypes,
                benchesByName
            );
            if (analysisType.Kind != MeasurementTypeKind.DCAnalysis)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2009: op_param first argument must be a DCAnalysis, got '{analysisType}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }

            return;
        }

        if (call.Name.Equals("sparam", StringComparison.OrdinalIgnoreCase))
        {
            if (call.Args.Count != 1)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2010: sparam requires exactly 1 argument, got {call.Args.Count}.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                return;
            }

            var analysisType = InferExprType(
                call.Args[0].Value,
                scope,
                measurementTypes,
                benchesByName
            );
            if (analysisType.Kind != MeasurementTypeKind.SPAnalysis)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS2011: sparam first argument must be an SPAnalysis, got '{analysisType}'.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
            }
        }
    }

    private static void ValidateSParameterMethodCall(
        BenchDefinition bench,
        MeasurementMethodCall call,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        var recv = InferExprType(call.Receiver, scope, measurementTypes, benchesByName);
        if (recv.Kind != MeasurementTypeKind.SParameterMatrix)
        {
            return;
        }

        var sParamMatrixMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "S",
            "InsertionLoss",
            "Isolation",
            "GroupDelay",
            "ReturnLoss",
            "VSWR",
            "StabilityK",
            "MuFactor",
            "MSG",
            "MAG",
            "NF",
            "NFmin",
            "Rn",
        };
        var indexPairMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "S",
            "InsertionLoss",
            "Isolation",
            "GroupDelay",
        };
        var singlePortMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ReturnLoss",
            "VSWR",
        };
        var zeroPortMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "StabilityK",
            "MuFactor",
            "MSG",
            "MAG",
            "NF",
            "NFmin",
            "Rn",
        };
        var twoPortOnlyMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "StabilityK",
            "MuFactor",
            "MSG",
            "MAG",
            "NF",
            "NFmin",
            "Rn",
        };

        if (!sParamMatrixMethods.Contains(call.Method))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"Unknown SParameterMatrix method '{call.Method}'.",
                    DiagnosticSeverity.Error,
                    "<bench>",
                    1,
                    1
                )
            );
            return;
        }

        if (indexPairMethods.Contains(call.Method))
        {
            if (call.Args.Count != 2)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"{call.Method} requires exactly 2 integer port arguments.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                return;
            }

            ValidatePortArgument(
                bench,
                call.Method,
                call.Args[0].Value,
                scope,
                measurementTypes,
                benchesByName,
                diagnostics
            );
            ValidatePortArgument(
                bench,
                call.Method,
                call.Args[1].Value,
                scope,
                measurementTypes,
                benchesByName,
                diagnostics
            );
        }

        if (singlePortMethods.Contains(call.Method))
        {
            if (call.Args.Count != 1)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"{call.Method} requires exactly 1 integer port argument.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                return;
            }

            ValidatePortArgument(
                bench,
                call.Method,
                call.Args[0].Value,
                scope,
                measurementTypes,
                benchesByName,
                diagnostics
            );
        }

        if (zeroPortMethods.Contains(call.Method) && call.Args.Count != 0)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"{call.Method} requires exactly 0 arguments.",
                    DiagnosticSeverity.Error,
                    "<bench>",
                    1,
                    1
                )
            );
            return;
        }

        if (twoPortOnlyMethods.Contains(call.Method))
        {
            var numPorts = EnumeratePortInstances(bench).Count();
            if (numPorts != 2)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"{call.Method} is defined for 2-port networks only; bench declares {numPorts} ports.",
                        DiagnosticSeverity.Error,
                        "<bench>",
                        1,
                        1
                    )
                );
                return;
            }
        }
    }

    private static void ValidatePortArgument(
        BenchDefinition bench,
        string methodName,
        MeasurementExpr arg,
        TypeScope scope,
        IReadOnlyDictionary<string, MeasurementType> measurementTypes,
        IReadOnlyDictionary<string, BenchDefinition> benchesByName,
        List<Diagnostic> diagnostics
    )
    {
        var argType = InferExprType(arg, scope, measurementTypes, benchesByName);
        if (argType.Kind != MeasurementTypeKind.Scalar)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"Port argument to {methodName} must be an integer, got '{argType}'.",
                    DiagnosticSeverity.Error,
                    "<bench>",
                    1,
                    1
                )
            );
            return;
        }

        if (!TryResolveConstantInt(arg, out var portIndex))
        {
            return;
        }

        var maxPort = EnumeratePortInstances(bench).Count();
        if (portIndex < 1 || portIndex > maxPort)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"Port index {portIndex} is out of range; bench declares ports 1..{maxPort}.",
                    DiagnosticSeverity.Error,
                    "<bench>",
                    1,
                    1
                )
            );
        }
    }

    private static bool TryResolveConstantInt(MeasurementExpr expr, out int value)
    {
        if (
            expr is MeasurementNumber number
            && int.TryParse(
                number.Raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            )
        )
        {
            return true;
        }

        value = 0;
        return false;
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
            BenchValueType.TranAnalysis => new Dictionary<string, MeasurementTypeKind>
            {
                ["start"] = MeasurementTypeKind.Time,
                ["stop"] = MeasurementTypeKind.Time,
                ["step"] = MeasurementTypeKind.Time,
            },
            BenchValueType.NoiseAnalysis => new Dictionary<string, MeasurementTypeKind>
            {
                ["start"] = MeasurementTypeKind.Frequency,
                ["stop"] = MeasurementTypeKind.Frequency,
                ["output"] = MeasurementTypeKind.Terminal,
            },
            BenchValueType.SPAnalysis => new Dictionary<string, MeasurementTypeKind>
            {
                ["start"] = MeasurementTypeKind.Frequency,
                ["stop"] = MeasurementTypeKind.Frequency,
                ["noise"] = MeasurementTypeKind.Scalar,
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

            if (
                analysis.Type == BenchValueType.SPAnalysis
                && name.Equals("noise", StringComparison.OrdinalIgnoreCase)
            )
            {
                ValidateSpNoiseFlag(analysis, expr, actual, diagnostics);
            }
        }
    }

    private static void ValidateSpNoiseFlag(
        AnalysisDeclaration analysis,
        MeasurementExpr expr,
        MeasurementType actual,
        List<Diagnostic> diagnostics
    )
    {
        if (actual.Kind != MeasurementTypeKind.Scalar)
        {
            return;
        }

        if (!TryResolveConstantInt(expr, out var noiseFlag))
        {
            return;
        }

        if (noiseFlag == 0 || noiseFlag == 1)
        {
            return;
        }

        diagnostics.Add(
            new Diagnostic(
                $"CAS2006: Analysis parameter '{analysis.Name}.noise' must be 0 or 1, got {noiseFlag}.",
                DiagnosticSeverity.Error,
                "<bench>",
                1,
                1
            )
        );
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
                    || baseType.Kind == MeasurementTypeKind.SPAnalysis
                )
                {
                    if (string.Equals(parts[1], "start", StringComparison.OrdinalIgnoreCase))
                    {
                        type =
                            baseType.Kind == MeasurementTypeKind.TranAnalysis
                                ? MeasurementType.Time()
                                : MeasurementType.Frequency();
                        return true;
                    }
                    if (string.Equals(parts[1], "stop", StringComparison.OrdinalIgnoreCase))
                    {
                        type =
                            baseType.Kind == MeasurementTypeKind.TranAnalysis
                                ? MeasurementType.Time()
                                : MeasurementType.Frequency();
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
        ElementPin,
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
        GainSpectrum,
        ScalarSpectrum,
        PhaseSpectrum,
        TimeSpectrum,
        ComplexVoltageSpectrum,
        ComplexCurrentSpectrum,
        VoltageSpectrum,
        CurrentSpectrum,
        NoiseSpectrum,
        ImpedanceSpectrum,
        VoltageWaveform,
        CurrentWaveform,
        NoiseSpectralDensity,
        IntegratedNoise,
        ComplexVoltage,
        ComplexCurrent,
        SParameterMatrix,
        Terminal,
        ACAnalysis,
        DCAnalysis,
        TranAnalysis,
        NoiseAnalysis,
        STBAnalysis,
        SPAnalysis,
    }

    private sealed record MeasurementType(MeasurementTypeKind Kind, string? TerminalDomain = null)
    {
        public static MeasurementType Bool() => new(MeasurementTypeKind.Bool);

        public static MeasurementType Scalar() => new(MeasurementTypeKind.Scalar);

        public static MeasurementType ElementPin() => new(MeasurementTypeKind.ElementPin);

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

        public static MeasurementType GainSpectrum() => new(MeasurementTypeKind.GainSpectrum);

        public static MeasurementType ScalarSpectrum() => new(MeasurementTypeKind.ScalarSpectrum);

        public static MeasurementType PhaseSpectrum() => new(MeasurementTypeKind.PhaseSpectrum);

        public static MeasurementType TimeSpectrum() => new(MeasurementTypeKind.TimeSpectrum);

        public static MeasurementType ComplexVoltageSpectrum() =>
            new(MeasurementTypeKind.ComplexVoltageSpectrum);

        public static MeasurementType ComplexCurrentSpectrum() =>
            new(MeasurementTypeKind.ComplexCurrentSpectrum);

        public static MeasurementType VoltageSpectrum() => new(MeasurementTypeKind.VoltageSpectrum);

        public static MeasurementType CurrentSpectrum() => new(MeasurementTypeKind.CurrentSpectrum);

        public static MeasurementType NoiseSpectrum() => new(MeasurementTypeKind.NoiseSpectrum);

        public static MeasurementType ImpedanceSpectrum() =>
            new(MeasurementTypeKind.ImpedanceSpectrum);

        public static MeasurementType VoltageWaveform() => new(MeasurementTypeKind.VoltageWaveform);

        public static MeasurementType CurrentWaveform() => new(MeasurementTypeKind.CurrentWaveform);

        public static MeasurementType NoiseSpectralDensity() =>
            new(MeasurementTypeKind.NoiseSpectralDensity);

        public static MeasurementType IntegratedNoise() => new(MeasurementTypeKind.IntegratedNoise);

        public static MeasurementType ComplexVoltage() => new(MeasurementTypeKind.ComplexVoltage);

        public static MeasurementType ComplexCurrent() => new(MeasurementTypeKind.ComplexCurrent);

        public static MeasurementType SParameterMatrix() =>
            new(MeasurementTypeKind.SParameterMatrix);

        public static MeasurementType Terminal(string domain) =>
            new(MeasurementTypeKind.Terminal, TerminalDomain: domain);

        public static MeasurementType FromBenchValueType(BenchValueType type) =>
            type switch
            {
                BenchValueType.Bool => Bool(),
                BenchValueType.Terminal => Terminal("unknown"),
                BenchValueType.Scalar => Scalar(),
                BenchValueType.ElementPin => ElementPin(),
                BenchValueType.Frequency => Frequency(),
                BenchValueType.VoltageRatio => VoltageRatio(),
                BenchValueType.Phase => Phase(),
                BenchValueType.Voltage => Voltage(),
                BenchValueType.Current => Current(),
                BenchValueType.Impedance => Impedance(),
                BenchValueType.Capacitance => Capacitance(),
                BenchValueType.Inductance => Inductance(),
                BenchValueType.TransferFunction => TransferFunction(),
                BenchValueType.GainSpectrum => GainSpectrum(),
                BenchValueType.ScalarSpectrum => ScalarSpectrum(),
                BenchValueType.PhaseSpectrum => PhaseSpectrum(),
                BenchValueType.TimeSpectrum => TimeSpectrum(),
                BenchValueType.ComplexVoltageSpectrum => ComplexVoltageSpectrum(),
                BenchValueType.ComplexCurrentSpectrum => ComplexCurrentSpectrum(),
                BenchValueType.VoltageSpectrum => VoltageSpectrum(),
                BenchValueType.CurrentSpectrum => CurrentSpectrum(),
                BenchValueType.NoiseSpectrum => NoiseSpectrum(),
                BenchValueType.ImpedanceSpectrum => ImpedanceSpectrum(),
                BenchValueType.VoltageWaveform => VoltageWaveform(),
                BenchValueType.CurrentWaveform => CurrentWaveform(),
                BenchValueType.NoiseSpectralDensity => NoiseSpectralDensity(),
                BenchValueType.IntegratedNoise => IntegratedNoise(),
                BenchValueType.SParameterMatrix => SParameterMatrix(),
                BenchValueType.ACAnalysis => new MeasurementType(MeasurementTypeKind.ACAnalysis),
                BenchValueType.DCAnalysis => new MeasurementType(MeasurementTypeKind.DCAnalysis),
                BenchValueType.TranAnalysis => new MeasurementType(
                    MeasurementTypeKind.TranAnalysis
                ),
                BenchValueType.NoiseAnalysis => new MeasurementType(
                    MeasurementTypeKind.NoiseAnalysis
                ),
                BenchValueType.STBAnalysis => new MeasurementType(MeasurementTypeKind.STBAnalysis),
                BenchValueType.SPAnalysis => new MeasurementType(MeasurementTypeKind.SPAnalysis),
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
            if (
                unit.EndsWith("V", StringComparison.OrdinalIgnoreCase)
                || unit.EndsWith("Vpp", StringComparison.OrdinalIgnoreCase)
            )
            {
                return Voltage();
            }
            if (unit.EndsWith("A", StringComparison.OrdinalIgnoreCase))
            {
                return Current();
            }
            if (unit.Equals("dB", StringComparison.OrdinalIgnoreCase))
            {
                return VoltageRatio();
            }
            if (unit.Equals("deg", StringComparison.OrdinalIgnoreCase))
            {
                return Phase();
            }
            if (unit.EndsWith("Ohm", StringComparison.OrdinalIgnoreCase))
            {
                return Impedance();
            }
            if (unit.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                return Time();
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
                return true;
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

            // Element-wise constraints allow spectrum/waveform measurements to be declared
            // with the corresponding scalar physical unit (e.g., dB, V, A, s, deg).
            if (
                target.Kind == MeasurementTypeKind.VoltageRatio
                && value.Kind == MeasurementTypeKind.GainSpectrum
            )
            {
                return true;
            }
            if (
                target.Kind == MeasurementTypeKind.Phase
                && value.Kind == MeasurementTypeKind.PhaseSpectrum
            )
            {
                return true;
            }
            if (
                target.Kind == MeasurementTypeKind.Time
                && value.Kind == MeasurementTypeKind.TimeSpectrum
            )
            {
                return true;
            }
            if (
                target.Kind == MeasurementTypeKind.Voltage
                && value.Kind
                    is MeasurementTypeKind.VoltageSpectrum
                        or MeasurementTypeKind.VoltageWaveform
            )
            {
                return true;
            }
            if (
                target.Kind == MeasurementTypeKind.Current
                && value.Kind
                    is MeasurementTypeKind.CurrentSpectrum
                        or MeasurementTypeKind.CurrentWaveform
            )
            {
                return true;
            }
            if (
                target.Kind == MeasurementTypeKind.Scalar
                && value.Kind == MeasurementTypeKind.ScalarSpectrum
            )
            {
                return true;
            }
            if (
                target.Kind == MeasurementTypeKind.NoiseSpectralDensity
                && value.Kind == MeasurementTypeKind.NoiseSpectrum
            )
            {
                return true;
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

            if (op == "*")
            {
                // Multiplication with scalar preserves the other type.
                if (left.Kind == MeasurementTypeKind.Scalar)
                {
                    return right;
                }
                if (right.Kind == MeasurementTypeKind.Scalar)
                {
                    return left;
                }
            }
            else if (op == "/")
            {
                // Division by scalar preserves the numerator's type.
                if (right.Kind == MeasurementTypeKind.Scalar)
                {
                    return left;
                }

                // Scalar divided by a non-scalar is not representable in this type system.
                // Fall back to scalar to avoid spurious type errors for common expressions.
                if (left.Kind == MeasurementTypeKind.Scalar)
                {
                    return Scalar();
                }
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
                MeasurementTypeKind.Terminal when TerminalDomain is not null =>
                    $"Terminal<{TerminalDomain}>",
                _ => Kind.ToString(),
            };
        }
    }
}
