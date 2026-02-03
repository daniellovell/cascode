using System;

namespace Cascode.Language.BenchRuntime.Netlist;

public enum BenchNodeKind
{
    Spice0,
    DutTerminal,
    BenchNet,
    BenchTerminalLeaf,
    InstancePin,
}

/// <summary>
/// Typed node identifier used during bench compilation.
/// </summary>
public readonly record struct BenchNode(BenchNodeKind Kind, string A, string? B = null)
{
    public static BenchNode Spice0 => new(BenchNodeKind.Spice0, "0");

    public static BenchNode DutTerminal(string terminal) =>
        new(BenchNodeKind.DutTerminal, terminal);

    public static BenchNode BenchNet(string name) => new(BenchNodeKind.BenchNet, name);

    public static BenchNode BenchTerminalLeaf(string name) =>
        new(BenchNodeKind.BenchTerminalLeaf, name);

    public static BenchNode InstancePin(string instanceId, string pin) =>
        new(BenchNodeKind.InstancePin, instanceId, pin);

    public string DebugName =>
        Kind switch
        {
            BenchNodeKind.Spice0 => "0",
            BenchNodeKind.DutTerminal => "dut." + A,
            BenchNodeKind.BenchNet => A,
            BenchNodeKind.BenchTerminalLeaf => A,
            BenchNodeKind.InstancePin => A + "." + (B ?? string.Empty),
            _ => A,
        };

    public static bool TryParseInstancePin(string raw, out (string InstanceId, string Pin) parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var dot = raw.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot >= raw.Length - 1)
            return false;

        var id = raw[..dot];
        var pin = raw[(dot + 1)..];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(pin))
            return false;

        parsed = (id, pin);
        return true;
    }
}
