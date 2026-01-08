using System.Collections.Generic;

namespace Cascode.ACIR;

/// <summary>
/// Declares a named size pack on a circuit with an optional default.
/// </summary>
public sealed class SizeDeclaration
{
    /// <summary>Size pack name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional default value. If null, the size pack is required at instantiation.</summary>
    public SizePack? Default { get; init; }
}

/// <summary>
/// A size pack is a named map of key/value sizing expressions (e.g. W/L/M for MOS).
/// </summary>
public sealed class SizePack
{
    /// <summary>Entries in this size pack.</summary>
    public Dictionary<string, string> Entries { get; init; } = new();
}
