using System;
using System.Collections.Generic;

namespace Cascode.Language.BenchRuntime.Netlist;

internal static class BenchNodeRefParser
{
    public static BenchNode Parse(
        string raw,
        IReadOnlySet<string> instanceIds,
        IReadOnlySet<string> benchTerminalLeaves
    )
    {
        raw = raw.Trim();
        if (raw.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            return BenchNode.Spice0;
        }

        if (raw.StartsWith("dut.", StringComparison.OrdinalIgnoreCase))
        {
            return BenchNode.DutTerminal(raw["dut.".Length..]);
        }

        if (benchTerminalLeaves.Contains(raw))
        {
            return BenchNode.BenchTerminalLeaf(raw);
        }

        if (BenchNode.TryParseInstancePin(raw, out var pin) && instanceIds.Contains(pin.InstanceId))
        {
            return BenchNode.InstancePin(pin.InstanceId, pin.Pin);
        }

        return BenchNode.BenchNet(raw);
    }
}
