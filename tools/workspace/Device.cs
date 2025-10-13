using System;
using System.Collections.Generic;

namespace Cascode.Workspace;

public enum DeviceClass
{
    Unknown = 0,
    Nmos,
    Pmos,
    Bipolar,
    Diode,
    Resistor,
    Capacitor,
    Inductor,
    Moscap,
    TransmissionLine,
    Stdcell,
    Other
}

public enum DeviceSubclass
{
    Unknown = 0,
    // Stdcell subclasses
    Inverter,
    Buffer,
    Nand,
    Nor,
    And,
    Or,
    Xor,
    Xnor,
    Multiplexer,
    Demultiplexer,
    Flipflop,
    Latch,
    Adder,
    // Capacitor subclasses
    MIMCAP,
    MOMCAP,
    MOSCAP,
    VarCap,
    // Resistor subclasses
    TFR,
    RMetal,
    RPoly,
    RWell,
    // MOS device subclasses
    DeepNwell,
    RF
}

public sealed class Device
{
    public string LibraryName { get; init; } = string.Empty;
    public string LibraryPath { get; init; } = string.Empty;
    public string CellName { get; init; } = string.Empty;
    public string CellPath { get; init; } = string.Empty;
    public DeviceClass Class { get; init; } = DeviceClass.Unknown;
    public DeviceSubclass Subclass { get; init; } = DeviceSubclass.Unknown;
    public bool HasLayout { get; init; }
    public bool HasSymbol { get; init; }
    public IReadOnlyList<string> Views { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> VtTags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> VddTags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public string CanonicalName => string.IsNullOrWhiteSpace(LibraryName) ? CellName : $"{LibraryName}__{CellName}";
    public string DisplayName => CellName;
}
