using System;
using System.Collections.Generic;

namespace Cascode.Workspace;

public sealed class Device
{
    public string LibraryName { get; init; } = string.Empty;
    public string LibraryPath { get; init; } = string.Empty;
    public string CellName { get; init; } = string.Empty;
    public string CellPath { get; init; } = string.Empty;
    public SpectreModelDeviceClass Class { get; init; } = SpectreModelDeviceClass.Unknown;
    public bool HasLayout { get; init; }
    public bool HasSymbol { get; init; }
    public IReadOnlyList<string> Views { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> VtTags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> VddTags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public string CanonicalName => string.IsNullOrWhiteSpace(LibraryName) ? CellName : $"{LibraryName}__{CellName}";
    public string DisplayName => CellName;
}

