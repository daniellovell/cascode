using System;
using System.Collections.Generic;

namespace Cascode.Language;

public sealed record IncludeDirective(string Name);

public enum BenchTerminalRole
{
    Stim,
    Resp,
}

public sealed record BenchTerminal(BenchTerminalRole Role, string Name, string Type);

public sealed class BenchDefinition
{
    public required string Name { get; init; }
    public List<BenchTerminal> Terminals { get; init; } = new();
    public FillBlock? Fill { get; init; }
    public List<FunctionDefinition> Functions { get; init; } = new();
    public List<AnalysisDeclaration> Analyses { get; init; } = new();
    public List<MeasurementDefinition> Measurements { get; init; } = new();
}

public enum BenchValueType
{
    Bool,

    // Bench terminals (stim/resp) used as values in measurement expressions.
    Terminal,

    // Physical quantities
    Frequency,
    VoltageRatio,
    TransferFunction,
    GainSpectrum,
    PhaseSpectrum,
    VoltageSpectrum,
    CurrentSpectrum,
    NoiseSpectrum,
    NoiseSpectralDensity,
    IntegratedNoise,
    Impedance,
    Capacitance,
    Inductance,
    Voltage,
    Current,
    Time,
    Phase,
    Scalar,

    // Time-domain compound types
    VoltageWaveform,
    CurrentWaveform,

    // Element references (used for current probing)
    ElementPin,

    // Analysis types
    ACAnalysis,
    DCAnalysis,
    TranAnalysis,
    NoiseAnalysis,
    STBAnalysis,
}

public sealed record TypedParameter(BenchValueType Type, string Name);

public sealed class FunctionDefinition
{
    public required string Name { get; init; }
    public List<TypedParameter> Parameters { get; init; } = new();
    public required BenchValueType ReturnType { get; init; }
    public List<BenchStatement> Body { get; init; } = new();
}

public sealed class AnalysisDeclaration
{
    public required BenchValueType Type { get; init; }
    public required string Name { get; init; }
    public Dictionary<string, MeasurementExpr> Parameters { get; init; } =
        new(StringComparer.Ordinal);
}

public sealed class MeasurementDefinition
{
    public required string Name { get; init; }
    public List<TypedParameter> Parameters { get; init; } = new();
    public required string Unit { get; init; }
    public List<BenchStatement> Body { get; init; } = new();
}

public abstract record BenchStatement;

public sealed record BenchVarDecl(BenchValueType Type, string Name, MeasurementExpr Expr)
    : BenchStatement;

public sealed record BenchIf(
    BoolExpr Condition,
    IReadOnlyList<BenchStatement> ThenBody,
    IReadOnlyList<BenchStatement>? ElseBody
) : BenchStatement;

public sealed record BenchReturn(MeasurementExpr Expr) : BenchStatement;

public enum MeasurementScope
{
    Env,
    Constraints,
    Harness,
}

public sealed record ScopedValueRef(MeasurementScope Scope, string Name);

public enum ComparisonOp
{
    Gte,
    Lte,
    Gt,
    Lt,
    Eq,
}

public abstract record BoolExpr;

public sealed record BoolExists(ScopedValueRef Ref) : BoolExpr;

public sealed record BoolTruthy(MeasurementExpr Expr) : BoolExpr;

public sealed record BoolCompare(ComparisonOp Op, MeasurementExpr Left, MeasurementExpr Right)
    : BoolExpr;

public abstract record MeasurementExpr;

public sealed record MeasurementBinary(string Op, MeasurementExpr Left, MeasurementExpr Right)
    : MeasurementExpr;

public sealed record MeasurementUnary(string Op, MeasurementExpr Operand) : MeasurementExpr;

public sealed record MeasurementCall(string Name, IReadOnlyList<MeasurementCallArg> Args)
    : MeasurementExpr;

public sealed record MeasurementCallArg(string? Name, MeasurementExpr Value);

public sealed record MeasurementMethodCall(
    MeasurementExpr Receiver,
    string Method,
    IReadOnlyList<MeasurementCallArg> Args
) : MeasurementExpr;

public sealed record MeasurementNumber(string Raw) : MeasurementExpr;

public sealed record MeasurementQuantity(string Raw) : MeasurementExpr;

public sealed record MeasurementPath(string Path) : MeasurementExpr;

public sealed record MeasurementScopedAccess(ScopedValueRef Ref) : MeasurementExpr;

public sealed record MeasurementDutAccess(string PinRef) : MeasurementExpr;

public sealed record MeasurementConditional(
    BoolExpr Condition,
    MeasurementExpr ThenExpr,
    MeasurementExpr ElseExpr
) : MeasurementExpr;

public sealed class BenchBinding
{
    public required string BenchName { get; init; }
    public required string BindingName { get; init; }
    public List<BenchBindingStatement> Statements { get; init; } = new();
}

public sealed class BenchBindingExtension
{
    public required string BindingName { get; init; }
    public List<BenchBindingStatement> Statements { get; init; } = new();
}

public abstract record BenchBindingStatement;

public sealed record BenchTerminalMapping(string BenchTerminal, string DutPinRef)
    : BenchBindingStatement;

public sealed record BenchDutConnection(string DutPinRef, string PinRef) : BenchBindingStatement;

public sealed record BenchBindingInstance(InstanceDeclaration Instance) : BenchBindingStatement;
