using System;
using System.Collections.Generic;

namespace Cascode.Language;

public sealed class EnvBlock
{
    public Dictionary<string, string> Entries { get; init; } = new(StringComparer.Ordinal);
}

public sealed class SynthBlock
{
    public Dictionary<string, string> Entries { get; init; } = new(StringComparer.Ordinal);
}
