using System;

namespace Cascode.Language;

public static class MeasurementSignature
{
    public static string Create(string name, int arity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(arity);
        return $"{name}#{arity}";
    }

    public static string Create(MeasurementDefinition measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        return Create(measurement.Name, measurement.Parameters.Count);
    }

    public static string ZeroArg(string name) => Create(name, arity: 0);
}
