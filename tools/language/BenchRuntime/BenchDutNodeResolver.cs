using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.BenchRuntime;

/// <summary>
/// Resolves <c>dut.&lt;path&gt;</c> measurement references to concrete SPICE node keys.
/// </summary>
internal static class BenchDutNodeResolver
{
    public static IReadOnlyDictionary<string, string> ResolveNodeKeys(
        IReadOnlyDictionary<string, Circuit> circuitsByName,
        Circuit dutCircuit,
        IEnumerable<string> pinRefs
    )
    {
        ArgumentNullException.ThrowIfNull(circuitsByName);
        ArgumentNullException.ThrowIfNull(dutCircuit);
        ArgumentNullException.ThrowIfNull(pinRefs);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (
            var pinRef in pinRefs
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        )
        {
            map[pinRef] = ResolveNodeKeyOrThrow(circuitsByName, dutCircuit, pinRef);
        }

        return map;
    }

    private static string ResolveNodeKeyOrThrow(
        IReadOnlyDictionary<string, Circuit> circuitsByName,
        Circuit dutCircuit,
        string pinRef
    )
    {
        var parts = pinRef.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (parts.Length == 0)
        {
            throw new InvalidOperationException("Empty dut node reference.");
        }

        var current = dutCircuit;
        var spiceInstancePath = new List<string>();
        var inlinePrefix = new List<string>();

        // Traverse through instance path segments (everything but the leaf net).
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var instId = parts[i];
            var inst = current.Fill?.Instances.FirstOrDefault(x =>
                x.Id.Equals(instId, StringComparison.OrdinalIgnoreCase)
            );
            if (inst is null)
            {
                throw new InvalidOperationException(
                    $"dut.{pinRef}: unknown instance '{instId}' in circuit '{current.Name}'."
                );
            }

            if (!circuitsByName.TryGetValue(inst.Type, out var next))
            {
                throw new InvalidOperationException(
                    $"dut.{pinRef}: unknown circuit type '{inst.Type}' for instance '{instId}'."
                );
            }

            if (next.Inline)
            {
                inlinePrefix.Add(inst.Id);
            }
            else
            {
                var prefix =
                    inlinePrefix.Count == 0
                        ? inst.Id
                        : string.Join("__", inlinePrefix) + "__" + inst.Id;

                spiceInstancePath.Add("X" + prefix);

                // Once we enter a non-inline subckt, any prior inline prefixes are already
                // baked into the instance name; nested prefixes start fresh inside the subckt.
                inlinePrefix.Clear();
            }

            current = next;
        }

        var leafNet = parts[^1];
        if (
            current.Fill?.Nets.All(n => !n.Id.Equals(leafNet, StringComparison.OrdinalIgnoreCase))
            != false
        )
        {
            throw new InvalidOperationException(
                $"dut.{pinRef}: '{leafNet}' is not a net declared in fill {{}} of circuit '{current.Name}'."
            );
        }

        // Inline nets are flattened via "{hierarchy}__{net}" within the current subckt.
        var netName =
            inlinePrefix.Count == 0 ? leafNet : string.Join("__", inlinePrefix) + "__" + leafNet;

        // ngspice hierarchical node syntax uses dot-separated instance paths:
        //   XDUT.<subckt_instance>.<net>
        // Subckt instances in the emitted SPICE always include a leading 'X'.
        var hierarchy =
            spiceInstancePath.Count == 0 ? string.Empty : string.Join('.', spiceInstancePath) + ".";

        return "XDUT." + hierarchy + netName;
    }
}
