using System;
using Cascode.Bench;

namespace Cascode.Language;

public static class SiValue
{
    public static string Format(double value)
    {
        if (value == 0)
        {
            return "0";
        }

        var absValue = Math.Abs(value);
        var sign = value < 0 ? "-" : "";

        var (divisor, suffix) = absValue switch
        {
            >= 1e12 => (1e12, "T"),
            >= 1e9 => (1e9, "G"),
            >= 1e6 => (1e6, "M"),
            >= 1e3 => (1e3, "K"),
            >= 1 => (1.0, ""),
            >= 1e-3 => (1e-3, "m"),
            >= 1e-6 => (1e-6, "u"),
            >= 1e-9 => (1e-9, "n"),
            >= 1e-12 => (1e-12, "p"),
            _ => (1e-15, "f"),
        };

        var scaled = absValue / divisor;
        var formatted = scaled.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);

        return $"{sign}{formatted}{suffix}";
    }

    public static string FormatForBackend(double value, BenchBackendType backend)
    {
        if (value == 0)
        {
            return "0";
        }

        var absValue = Math.Abs(value);
        var sign = value < 0 ? "-" : "";

        // ngspice uses MEG for mega (M means milli).
        var megaSuffix = backend == BenchBackendType.Ngspice ? "MEG" : "M";

        var (divisor, suffix) = absValue switch
        {
            >= 1e12 => (1e12, "T"),
            >= 1e9 => (1e9, "G"),
            >= 1e6 => (1e6, megaSuffix),
            >= 1e3 => (1e3, "K"),
            >= 1 => (1.0, ""),
            >= 1e-3 => (1e-3, "m"),
            >= 1e-6 => (1e-6, "u"),
            >= 1e-9 => (1e-9, "n"),
            >= 1e-12 => (1e-12, "p"),
            _ => (1e-15, "f"),
        };

        var scaled = absValue / divisor;
        var formatted = scaled.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);

        return $"{sign}{formatted}{suffix}";
    }

    public static string TransformForBackend(string value, BenchBackendType backend)
    {
        if (string.IsNullOrWhiteSpace(value) || backend != BenchBackendType.Ngspice)
        {
            return value;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return value;
        }

        int numericEnd = -1;
        for (var i = trimmed.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(trimmed[i]) || trimmed[i] == '.')
            {
                numericEnd = i;
                break;
            }
        }

        if (numericEnd < 0)
        {
            return value;
        }

        var prefixIndex = numericEnd + 1;
        if (prefixIndex >= trimmed.Length)
        {
            return value;
        }

        var prefixChar = trimmed[prefixIndex];
        if (prefixChar != 'M')
        {
            return value;
        }

        var numericPart = trimmed[..prefixIndex];
        var unitSuffix = prefixIndex + 1 < trimmed.Length ? trimmed[(prefixIndex + 1)..] : "";
        return numericPart + "MEG" + unitSuffix;
    }

    public static bool TryParse(
        string valueStr,
        out double result,
        bool stripUnits,
        bool allowSubUnity
    )
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(valueStr))
        {
            return false;
        }

        var cleanedValue = valueStr.Trim();

        if (stripUnits)
        {
            foreach (var suffix in new[] { "Ohm", "ohm", "Hz", "V", "A", "F", "H", "W", "s", "S" })
            {
                if (cleanedValue.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    cleanedValue = cleanedValue[..^suffix.Length].Trim();
                    break;
                }
            }
        }

        if (cleanedValue.Length == 0)
        {
            return false;
        }

        var multiplier = 1.0;
        if (char.IsLetter(cleanedValue[^1]))
        {
            var lastChar = cleanedValue[^1];
            var upperChar = char.ToUpperInvariant(lastChar);

            multiplier = upperChar switch
            {
                'T' => 1e12,
                'G' => 1e9,
                'M' => 1e6,
                'K' => 1e3,
                'U' when allowSubUnity => 1e-6,
                'N' when allowSubUnity => 1e-9,
                'P' when allowSubUnity => 1e-12,
                'F' when allowSubUnity => 1e-15,
                _ => 1.0,
            };

            if (allowSubUnity && lastChar == 'm')
            {
                multiplier = 1e-3;
            }

            if (multiplier != 1.0)
            {
                cleanedValue = cleanedValue[..^1];
            }
        }

        if (
            double.TryParse(
                cleanedValue,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            result = parsed * multiplier;
            return true;
        }

        return false;
    }
}
