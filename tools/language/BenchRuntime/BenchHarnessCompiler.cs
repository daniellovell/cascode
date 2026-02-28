using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language.BenchRuntime.Netlist;

namespace Cascode.Language.BenchRuntime;

internal sealed record BenchHarnessCompilation(
    IReadOnlyDictionary<string, BenchValue> Env,
    IReadOnlyDictionary<string, BenchValue> Harness,
    IReadOnlyDictionary<string, BenchValue> Constraints,
    IReadOnlyList<InstanceDeclaration> Instances
);

internal static class BenchHarnessCompiler
{
    public static BenchHarnessCompilation CompileAndInject(
        Circuit circuit,
        string bindingName,
        BenchUnionFind uf,
        IReadOnlyList<InstanceDeclaration> baseInstances
    )
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(bindingName);
        ArgumentNullException.ThrowIfNull(uf);
        ArgumentNullException.ThrowIfNull(baseInstances);

        var env = BuildEnv(circuit);
        var harness = BuildHarnessScope(circuit, env);
        var constraints = BuildConstraints(circuit, bindingName);

        var instances = baseInstances.ToList();
        var instanceIds = instances.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        ApplyHarnessAutoInjection(circuit, uf, instances, instanceIds);

        return new BenchHarnessCompilation(env, harness, constraints, instances);
    }

    // NOTE: harness element emission lives in BenchHarnessElementCompiler so this compiler can stay
    // focused on scope construction + injection planning.

    private static void ApplyHarnessAutoInjection(
        Circuit circuit,
        BenchUnionFind uf,
        List<InstanceDeclaration> instances,
        HashSet<string> instanceIds
    )
    {
        if (circuit.Harness is null)
        {
            return;
        }

        var drivers = new BenchDriverModel(uf, instances);

        // Prefer a conventional ground name if available, otherwise use the first declared ground.
        var ground =
            circuit.Grounds.FirstOrDefault(g => g.Equals("GND", StringComparison.OrdinalIgnoreCase))
            ?? circuit.Grounds.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

        // If the design has no ground terminal, we can't safely auto-wire supplies/biases/loads.
        if (string.IsNullOrWhiteSpace(ground))
        {
            return;
        }

        string UniqueId(string baseId)
        {
            var id = baseId;
            var i = 1;
            while (instanceIds.Contains(id))
            {
                id = baseId + "_" + i;
                i++;
            }
            return id;
        }

        // Grounds: allow explicit ground references in the harness. "0V" means use a GND() tie.
        foreach (var g in circuit.Harness.Grounds)
        {
            if (string.IsNullOrWhiteSpace(g.Net))
            {
                continue;
            }

            var dutNet = BenchNode.DutTerminal(g.Net);
            if (!drivers.ShouldInjectGroundTie(dutNet))
            {
                continue;
            }

            if (IsZeroVolts(g.Value))
            {
                AddInjected(
                    instances,
                    instanceIds,
                    uf,
                    new InstanceDeclaration
                    {
                        Id = UniqueId("hGND_" + g.Net),
                        Type = "GND",
                        Bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["GND"] = dutNet.DebugName,
                        },
                    }
                );
            }
            else
            {
                AddInjected(
                    instances,
                    instanceIds,
                    uf,
                    new InstanceDeclaration
                    {
                        Id = UniqueId("hV_" + g.Net),
                        Type = "VDC",
                        Bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["P"] = dutNet.DebugName,
                            ["N"] = "0",
                        },
                        Params = new Dictionary<string, ParamValue>(
                            StringComparer.OrdinalIgnoreCase
                        )
                        {
                            ["V"] = new ParamValue { Numeric = g.Value },
                        },
                    }
                );
            }
        }

        // Supplies: VDC from supply net to ground.
        foreach (var s in circuit.Harness.Supplies)
        {
            if (string.IsNullOrWhiteSpace(s.Net))
            {
                continue;
            }

            var dutNet = BenchNode.DutTerminal(s.Net);
            if (!drivers.ShouldInjectSupplyOrBias(dutNet))
            {
                continue;
            }

            AddInjected(
                instances,
                instanceIds,
                uf,
                new InstanceDeclaration
                {
                    Id = UniqueId("hV_" + s.Net),
                    Type = "VDC",
                    Bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["P"] = dutNet.DebugName,
                        ["N"] = BenchNode.DutTerminal(ground).DebugName,
                    },
                    Params = new Dictionary<string, ParamValue>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["V"] = new ParamValue { Numeric = s.Value },
                    },
                }
            );
        }

        // Biases: also treated as DC sources, typically relative to ground.
        foreach (var b in circuit.Harness.Biases)
        {
            if (string.IsNullOrWhiteSpace(b.Net))
            {
                continue;
            }

            var dutNet = BenchNode.DutTerminal(b.Net);
            if (!drivers.ShouldInjectSupplyOrBias(dutNet))
            {
                continue;
            }

            AddInjected(
                instances,
                instanceIds,
                uf,
                new InstanceDeclaration
                {
                    Id = UniqueId("hV_" + b.Net),
                    Type = "VDC",
                    Bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["P"] = dutNet.DebugName,
                        ["N"] = BenchNode.DutTerminal(ground).DebugName,
                    },
                    Params = new Dictionary<string, ParamValue>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["V"] = new ParamValue { Numeric = b.Value },
                    },
                }
            );
        }

        // Loads: Impedor between net and ground. Multiple elements become a parallel "||" composite.
        foreach (var l in circuit.Harness.Loads)
        {
            if (string.IsNullOrWhiteSpace(l.Net) || l.Elements.Count == 0)
            {
                continue;
            }

            var dutNet = BenchNode.DutTerminal(l.Net);
            if (!drivers.ShouldInjectLoad(dutNet))
            {
                continue;
            }

            var z = string.Join(
                "||",
                l.Elements.Select(e => e.Value.Trim()).Where(v => v.Length > 0)
            );
            if (z.Length == 0)
            {
                continue;
            }

            AddInjected(
                instances,
                instanceIds,
                uf,
                new InstanceDeclaration
                {
                    Id = UniqueId("hLoad_" + l.Net),
                    Type = "Impedor",
                    Bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["P"] = dutNet.DebugName,
                        ["N"] = BenchNode.DutTerminal(ground).DebugName,
                    },
                    Params = new Dictionary<string, ParamValue>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Z"] = new ParamValue { Numeric = z },
                    },
                }
            );
        }
    }

    private static void AddInjected(
        List<InstanceDeclaration> instances,
        HashSet<string> instanceIds,
        BenchUnionFind uf,
        InstanceDeclaration injected
    )
    {
        instances.Add(injected);
        instanceIds.Add(injected.Id);

        // Integrate into the connectivity graph (pin → target node).
        foreach (var (pin, target) in injected.Bindings)
        {
            uf.Union(BenchNode.InstancePin(injected.Id, pin), ParseInjectedTarget(target));
        }
    }

    private static BenchNode ParseInjectedTarget(string raw)
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

        throw new InvalidOperationException(
            $"Unsupported injected harness binding target '{raw}'."
        );
    }

    private static bool IsZeroVolts(string raw)
    {
        raw = raw.Trim();
        return raw.Equals("0V", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("0", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("0.0V", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, BenchValue> BuildEnv(Circuit circuit)
    {
        var env = new Dictionary<string, BenchValue>(StringComparer.Ordinal);
        if (circuit.Env is null)
        {
            return env;
        }

        foreach (var (key, raw) in circuit.Env.Entries)
        {
            env[key] = ParseEnvValue(key, raw);
        }

        return env;
    }

    private static BenchValue ParseEnvValue(string key, string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0)
        {
            return BenchMissing.Value;
        }

        // Allow outer parentheses for impedance expressions for ergonomics:
        //   LoadImpedance = (1GOhm || 15pF)
        if (raw.Length >= 2 && raw[0] == '(' && raw[^1] == ')')
        {
            raw = raw[1..^1].Trim();
        }

        var isImpedanceKey =
            key.Equals("SourceImpedance", StringComparison.OrdinalIgnoreCase)
            || key.Equals("LoadImpedance", StringComparison.OrdinalIgnoreCase);

        if (raw.Contains("||", StringComparison.Ordinal))
        {
            var parts = raw.Split(
                "||",
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

            var elements = new List<BenchNumber>(capacity: parts.Length);
            foreach (var part in parts)
            {
                var v = BenchQuantity.Parse(part);
                if (
                    v is not BenchNumber n
                    || n.Kind
                        is not (
                            BenchNumericKind.ImpedanceOhm
                            or BenchNumericKind.CapacitanceF
                            or BenchNumericKind.InductanceH
                        )
                )
                {
                    throw new InvalidOperationException(
                        $"Invalid impedance element '{part}' (expected Ohm/F/H quantity)."
                    );
                }
                elements.Add(n);
            }

            return new BenchImpedanceParallel(elements);
        }

        try
        {
            var parsed = BenchQuantity.Parse(raw);
            if (
                isImpedanceKey
                && parsed is BenchNumber n
                && n.Kind
                    is (
                        BenchNumericKind.ImpedanceOhm
                        or BenchNumericKind.CapacitanceF
                        or BenchNumericKind.InductanceH
                    )
            )
            {
                return new BenchImpedanceParallel(new[] { n });
            }

            return parsed;
        }
        catch (FormatException)
        {
            return new BenchSymbol(raw);
        }
    }

    private static Dictionary<string, BenchValue> BuildHarnessScope(
        Circuit circuit,
        IReadOnlyDictionary<string, BenchValue> env
    )
    {
        var harness = new Dictionary<string, BenchValue>(env, StringComparer.Ordinal);
        if (circuit.Harness is null)
        {
            return harness;
        }

        foreach (var g in circuit.Harness.Grounds)
        {
            if (string.IsNullOrWhiteSpace(g.Net))
            {
                continue;
            }
            harness[g.Net] = ParseHarnessScalar(g.Value);
        }

        foreach (var s in circuit.Harness.Supplies)
        {
            if (string.IsNullOrWhiteSpace(s.Net))
            {
                continue;
            }
            harness[s.Net] = ParseHarnessScalar(s.Value);
        }

        foreach (var b in circuit.Harness.Biases)
        {
            if (string.IsNullOrWhiteSpace(b.Net))
            {
                continue;
            }
            harness[b.Net] = ParseHarnessScalar(b.Value);
        }

        return harness;
    }

    private static BenchValue ParseHarnessScalar(string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0)
        {
            return BenchMissing.Value;
        }

        try
        {
            return BenchQuantity.Parse(raw);
        }
        catch (FormatException)
        {
            return new BenchSymbol(raw);
        }
    }

    private static Dictionary<string, BenchValue> BuildConstraints(
        Circuit circuit,
        string bindingName
    )
    {
        var constraints = new Dictionary<string, BenchValue>(StringComparer.Ordinal);
        if (circuit.Constraints?.Numeric is null)
        {
            return constraints;
        }

        foreach (var c in circuit.Constraints.Numeric)
        {
            if (!c.Bench.Equals(bindingName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (c.MetricArgs.Count != 0)
            {
                // Constraint metric invocations (e.g. Metric(from=..., to=...)) are evaluated directly.
                // The bench "constraints" scope is used only for scalar hints like GainBandwidth.
                continue;
            }

            var raw = c.Value + c.Unit;
            try
            {
                constraints[c.Metric] = BenchQuantity.Parse(raw);
            }
            catch (FormatException)
            {
                constraints[c.Metric] = new BenchSymbol(raw);
            }
        }

        return constraints;
    }
}
