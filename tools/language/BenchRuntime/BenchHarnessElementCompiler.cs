using System;
using System.Collections.Generic;
using System.Globalization;
using Cascode.Language.BenchRuntime.Netlist;

namespace Cascode.Language.BenchRuntime;

internal static class BenchHarnessElementCompiler
{
    public static IReadOnlyList<BenchHarnessElement> CompileHarnessElements(
        IReadOnlyList<InstanceDeclaration> instances,
        BenchNetlist netlist,
        BenchMeasurementRunner evalRunner,
        IReadOnlyDictionary<string, BenchValue>? benchParams = null
    )
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(netlist);
        ArgumentNullException.ThrowIfNull(evalRunner);

        var elements = new List<BenchHarnessElement>();

        foreach (var inst in instances)
        {
            // Only a small set of harness primitives are supported initially.
            var type = NormalizeHarnessPrimitiveType(inst.Type);
            if (
                !type.Equals("GND", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("VDC", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("VAC", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("VSIN", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("Port", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("Impedance", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (pin, spiceNet) in EnumerateInstancePins(inst.Id, netlist))
            {
                pins[pin] = spiceNet;
            }

            var parameters = new Dictionary<string, BenchValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in inst.Params)
            {
                parameters[name] = EvaluateInstanceParam(value, evalRunner, benchParams);
            }

            elements.Add(new BenchHarnessElement(type, inst.Id, pins, parameters));
        }

        return elements;
    }

    private static IEnumerable<(string Pin, string SpiceNet)> EnumerateInstancePins(
        string instanceId,
        BenchNetlist netlist
    )
    {
        foreach (var (node, netId) in netlist.NetIdByNode)
        {
            if (node.Kind != BenchNodeKind.InstancePin)
                continue;
            if (!node.A.Equals(instanceId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(node.B))
                continue;

            var spiceNet = netlist.GetNet(netId).SpiceName;
            yield return (node.B!, spiceNet);
        }
    }

    private static string NormalizeHarnessPrimitiveType(string type)
    {
        return type.Equals("Impedor", StringComparison.OrdinalIgnoreCase) ? "Impedance" : type;
    }

    private static BenchValue EvaluateInstanceParam(
        ParamValue v,
        BenchMeasurementRunner evalRunner,
        IReadOnlyDictionary<string, BenchValue>? benchParams
    )
    {
        if (!string.IsNullOrWhiteSpace(v.Numeric))
        {
            // Try quantity first; fallback to scalar number.
            try
            {
                return BenchQuantity.Parse(v.Numeric);
            }
            catch (FormatException)
            {
                if (
                    double.TryParse(
                        v.Numeric,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var d
                    )
                )
                {
                    return new BenchNumber(BenchNumericKind.Scalar, d);
                }
                return new BenchSymbol(v.Numeric);
            }
        }

        if (!string.IsNullOrWhiteSpace(v.Symbolic))
        {
            if (CascodeAstBuilder.TryParseMeasurementExprText(v.Symbolic, out var expr, out _))
            {
                return evalRunner.EvaluateExpressionForPlan(expr!, benchParams);
            }

            return new BenchSymbol(v.Symbolic);
        }

        if (!string.IsNullOrWhiteSpace(v.Literal))
        {
            return new BenchSymbol(v.Literal);
        }

        return new BenchSymbol(string.Empty);
    }
}
