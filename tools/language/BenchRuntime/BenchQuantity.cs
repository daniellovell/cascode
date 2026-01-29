using System;

namespace Cascode.Language.BenchRuntime;

/// <summary>
/// Parses Cascode quantity literals (e.g. 10MHz, 3dB, 1.8V, 50Ohm, 1pF, 10ms).
/// </summary>
internal static class BenchQuantity
{
    public static BenchValue Parse(string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0)
        {
            throw new InvalidOperationException("Empty quantity.");
        }

        // Compound noise units (rtHz = sqrt(Hz)).
        if (raw.EndsWith("/rtHz", StringComparison.OrdinalIgnoreCase))
        {
            // Examples: nV/rtHz, pA/rtHz
            var baseUnit = raw[..^"/rtHz".Length].Trim();
            if (
                baseUnit.EndsWith("V", StringComparison.OrdinalIgnoreCase)
                && SiValue.TryParse(baseUnit, out var v, stripUnits: true, allowSubUnity: true)
            )
            {
                return new BenchNumber(BenchNumericKind.NoiseVoltageVPerRtHz, v);
            }
            if (
                baseUnit.EndsWith("A", StringComparison.OrdinalIgnoreCase)
                && SiValue.TryParse(baseUnit, out var a, stripUnits: true, allowSubUnity: true)
            )
            {
                return new BenchNumber(BenchNumericKind.NoiseCurrentAPerRtHz, a);
            }

            throw new InvalidOperationException(
                $"Invalid noise spectral density quantity '{raw}'."
            );
        }

        // Integrated noise (RMS). Examples: nVrms, pArms.
        if (raw.EndsWith("rms", StringComparison.OrdinalIgnoreCase))
        {
            var baseUnit = raw[..^"rms".Length].Trim();
            if (
                baseUnit.EndsWith("V", StringComparison.OrdinalIgnoreCase)
                && SiValue.TryParse(baseUnit, out var v, stripUnits: true, allowSubUnity: true)
            )
            {
                return new BenchNumber(BenchNumericKind.IntegratedNoiseVrms, v);
            }
            if (
                baseUnit.EndsWith("A", StringComparison.OrdinalIgnoreCase)
                && SiValue.TryParse(baseUnit, out var a, stripUnits: true, allowSubUnity: true)
            )
            {
                return new BenchNumber(BenchNumericKind.IntegratedNoiseArms, a);
            }

            throw new InvalidOperationException($"Invalid integrated noise quantity '{raw}'.");
        }

        if (raw.EndsWith("dB", StringComparison.OrdinalIgnoreCase))
        {
            var num = raw[..^2];
            return new BenchNumber(BenchNumericKind.VoltageRatioDb, ParseSiNumber(num));
        }

        if (raw.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            var num = raw[..^3];
            return new BenchNumber(BenchNumericKind.PhaseDeg, ParseSiNumber(num));
        }

        if (raw.EndsWith("Hz", StringComparison.OrdinalIgnoreCase))
        {
            return new BenchNumber(BenchNumericKind.FrequencyHz, ParseSiWithUnits(raw));
        }

        // Peak-to-peak voltage; stored in volts for now.
        if (raw.EndsWith("Vpp", StringComparison.OrdinalIgnoreCase))
        {
            var num = raw[..^3];
            return new BenchNumber(BenchNumericKind.VoltageV, ParseSiWithUnits(num + "V"));
        }

        if (raw.EndsWith("V", StringComparison.OrdinalIgnoreCase))
        {
            return new BenchNumber(BenchNumericKind.VoltageV, ParseSiWithUnits(raw));
        }

        if (raw.EndsWith("A", StringComparison.OrdinalIgnoreCase))
        {
            return new BenchNumber(BenchNumericKind.CurrentA, ParseSiWithUnits(raw));
        }

        if (raw.EndsWith("Ohm", StringComparison.OrdinalIgnoreCase))
        {
            return new BenchNumber(BenchNumericKind.ImpedanceOhm, ParseSiWithUnits(raw));
        }

        if (raw.EndsWith("F", StringComparison.OrdinalIgnoreCase))
        {
            return new BenchNumber(BenchNumericKind.CapacitanceF, ParseSiWithUnits(raw));
        }

        if (raw.EndsWith("H", StringComparison.OrdinalIgnoreCase))
        {
            return new BenchNumber(BenchNumericKind.InductanceH, ParseSiWithUnits(raw));
        }

        if (raw.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            return new BenchNumber(BenchNumericKind.TimeS, ParseSiWithUnits(raw));
        }

        // Fall back: treat as unitless scalar.
        return new BenchNumber(BenchNumericKind.Scalar, ParseSiNumber(raw));
    }

    private static double ParseSiNumber(string raw)
    {
        if (!SiValue.TryParse(raw, out var value, stripUnits: true, allowSubUnity: true))
        {
            throw new InvalidOperationException($"Invalid numeric quantity '{raw}'.");
        }

        return value;
    }

    private static double ParseSiWithUnits(string raw)
    {
        if (!SiValue.TryParse(raw, out var value, stripUnits: true, allowSubUnity: true))
        {
            throw new InvalidOperationException($"Invalid quantity '{raw}'.");
        }

        return value;
    }
}
