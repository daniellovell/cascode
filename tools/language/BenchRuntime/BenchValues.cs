using System;
using System.Collections.Generic;
using System.Numerics;

namespace Cascode.Language.BenchRuntime;

public enum BenchNumericKind
{
    Scalar,
    FrequencyHz,
    VoltageRatioDb,
    PhaseDeg,
    VoltageV,
    CurrentA,
    ImpedanceOhm,
    CapacitanceF,
    InductanceH,
    TimeS,
    NoiseVoltageVPerRtHz,
    NoiseCurrentAPerRtHz,
    IntegratedNoiseVrms,
    IntegratedNoiseArms,
}

public abstract record BenchValue;

public sealed record BenchNumber(BenchNumericKind Kind, double Value) : BenchValue;

public sealed record BenchTerminalRef(string Name, IReadOnlyList<string> LeafNodes) : BenchValue;

/// <summary>
/// Represents an intentionally-absent value (e.g. optional constraint not provided).
/// </summary>
public sealed record BenchMissing : BenchValue
{
    public static readonly BenchMissing Value = new();

    private BenchMissing() { }
}

public sealed record BenchTransferFunction(double[] FrequenciesHz, Complex[] Values) : BenchValue;

public sealed record BenchRealFunction(
    double[] FrequenciesHz,
    double[] Values,
    BenchNumericKind RangeKind
) : BenchValue;

public sealed record BenchNoiseFunction(double[] FrequenciesHz, double[] ValuesVPerRtHz)
    : BenchValue;

public sealed record BenchAnalysisRef(string Name) : BenchValue;

public sealed record BenchBool(bool Value) : BenchValue;

public sealed record BenchSymbol(string Name) : BenchValue;
