using System;
using System.Collections.Generic;
using System.Numerics;

namespace Cascode.Language.BenchRuntime;

public enum BenchNumericKind
{
    Scalar,
    FrequencyHz,
    VoltageRatioLinear,
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

public sealed record BenchError(string Message) : BenchValue;

public sealed record BenchElementPinRef(string ElementId, string Pin) : BenchValue;

/// <summary>
/// Represents an intentionally-absent value (e.g. optional constraint not provided).
/// </summary>
public sealed record BenchMissing : BenchValue
{
    public static readonly BenchMissing Value = new();

    private BenchMissing() { }
}

public sealed record BenchTransferFunction(double[] FrequenciesHz, Complex[] Values) : BenchValue;

public sealed record BenchGainSpectrum(
    double[] FrequenciesHz,
    double[] Values,
    BenchNumericKind ValueKind
) : BenchValue;

public sealed record BenchPhaseSpectrum(double[] FrequenciesHz, double[] Degrees) : BenchValue;

public sealed record BenchNoiseSpectrum(double[] FrequenciesHz, double[] ValuesVPerRtHz)
    : BenchValue;

public sealed record BenchVoltageSpectrum(double[] FrequenciesHz, Complex[] Values) : BenchValue;

public sealed record BenchCurrentSpectrum(double[] FrequenciesHz, Complex[] Values) : BenchValue;

public sealed record BenchAnalysisRef(string Name) : BenchValue;

public sealed record BenchBool(bool Value) : BenchValue;

public sealed record BenchSymbol(string Name) : BenchValue;

/// <summary>
/// A parallel impedance network represented as a set of primitive components (R/C/L) in parallel.
/// This stays intentionally constrained: the RFC only requires the parallel-combination operator (||).
/// </summary>
public sealed record BenchImpedanceParallel(IReadOnlyList<BenchNumber> Elements) : BenchValue;

public sealed record TranDataset(
    double[] TimePoints,
    IReadOnlyDictionary<string, double[]> NodeVoltages
);

public sealed record BenchWaveform(
    double[] TimePointsS,
    double[] Values,
    BenchNumericKind ValueKind
) : BenchValue;
