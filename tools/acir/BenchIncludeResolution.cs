using System;
using System.Collections.Generic;
using Cascode.Bench;

namespace Cascode.ACIR;

public sealed record DeviceModelResolution(string ModelName, bool IsSubckt);

public sealed record BenchIncludeResolution(
    IReadOnlyList<string> WithSection,
    IReadOnlyList<string> WithoutSection,
    string? Section)
{
    public IReadOnlyDictionary<string, DeviceModelResolution> DeviceModelMap { get; init; }
        = new Dictionary<string, DeviceModelResolution>(StringComparer.OrdinalIgnoreCase);
}

public interface IBenchIncludeResolver
{
    BenchIncludeResolution Resolve(Circuit circuit, BenchBackendType backend);
}
