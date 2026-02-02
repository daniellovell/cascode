using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cascode.Bench;
using Cascode.Language;

namespace Cascode.Language.BenchRuntime;

public static class BenchTestbenchEmitter
{
    public static IReadOnlyList<string> EmitAll(
        CascodeDocument document,
        string outputDir,
        BenchBackendType backend,
        IReadOnlyList<string> designPaths,
        IBenchIncludeResolver? includeResolver = null
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        ArgumentNullException.ThrowIfNull(designPaths);

        var plans = BenchCompiler.CompileAllPlans(document);
        return EmitPlans(document, plans, outputDir, backend, designPaths, includeResolver);
    }

    public static IReadOnlyList<string> EmitPlans(
        CascodeDocument document,
        IReadOnlyList<BenchPlan> plans,
        string outputDir,
        BenchBackendType backend,
        IReadOnlyList<string> designPaths,
        IBenchIncludeResolver? includeResolver = null
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        ArgumentNullException.ThrowIfNull(designPaths);

        var circuitsByName = document.Circuits.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var written = new List<string>(plans.Count);

        foreach (var plan in plans)
        {
            circuitsByName.TryGetValue(plan.CircuitName, out var circuit);
            var include = circuit is null
                ? null
                : includeResolver?.Resolve(circuit, backend, document);

            var tbPath = BenchRuntimePaths.GetTestbenchPath(
                outputDir,
                plan.CircuitName,
                plan.BindingName
            );
            Directory.CreateDirectory(Path.GetDirectoryName(tbPath)!);
            File.WriteAllText(
                tbPath,
                RenderTestbench(plan, backend, designPaths, include, outputDir)
            );
            written.Add(tbPath);
        }

        return written;
    }

    private static string RenderTestbench(
        BenchPlan plan,
        BenchBackendType backend,
        IReadOnlyList<string> designPaths,
        BenchIncludeResolution? includes,
        string outputDir
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine($"* cascode auto-generated: {plan.CircuitName}:{plan.BindingName}");
        sb.AppendLine($".title {plan.CircuitName}_{plan.BindingName}");
        sb.AppendLine(".option numdgt=7");
        sb.AppendLine();

        if (includes is not null)
        {
            foreach (var inc in includes.WithSection)
            {
                if (!string.IsNullOrWhiteSpace(includes.Section))
                {
                    sb.AppendLine($".lib \"{inc}\" {includes.Section}");
                }
                else
                {
                    sb.AppendLine($".include \"{inc}\"");
                }
            }

            foreach (var inc in includes.WithoutSection)
            {
                sb.AppendLine($".include \"{inc}\"");
            }
        }

        foreach (var path in designPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Testbenches run with WorkingDirectory set to the output dir; include local design decks by filename.
            sb.AppendLine($".include \"{Path.GetFileName(path)}\"");
        }

        sb.AppendLine();
        sb.AppendLine("* harness");
        foreach (var e in plan.HarnessElements)
        {
            EmitHarnessElement(sb, e, backend);
        }

        sb.AppendLine();
        sb.AppendLine("* dut");
        sb.AppendLine($"XDUT {string.Join(" ", plan.DutOrderedNets)} {plan.DutSubcktName}");
        sb.AppendLine();

        sb.AppendLine(".control");
        sb.AppendLine("set filetype=ascii");

        var vdcSources = plan
            .HarnessElements.Where(e => e.Type.Equals("VDC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasDc = plan.Analyses.Any(a => a.Type == BenchValueType.DCAnalysis);
        if (vdcSources.Count > 0 || hasDc)
        {
            sb.AppendLine();
            sb.AppendLine("* operating point");
            sb.AppendLine("op");
            sb.AppendLine("setplot op1");

            if (vdcSources.Count > 0)
            {
                sb.AppendLine("* supply currents");
                var opWrdata = BenchRuntimePaths.GetOpWrdataPath(
                    outputDir,
                    plan.CircuitName,
                    plan.BindingName
                );
                sb.Append($"wrdata {Path.GetFileName(opWrdata)}");
                foreach (var s in vdcSources)
                {
                    sb.Append(' ');
                    sb.Append($"i(V{s.Id})");
                }
                sb.AppendLine();
            }

            if (hasDc)
            {
                sb.AppendLine("* node voltages");
                var nodesWrdata = BenchRuntimePaths.GetOpNodesWrdataPath(
                    outputDir,
                    plan.CircuitName,
                    plan.BindingName
                );
                sb.Append($"wrdata {Path.GetFileName(nodesWrdata)}");
                foreach (var node in plan.AcNodeKeys)
                {
                    sb.Append(' ');
                    sb.Append($"v({node})");
                }
                sb.AppendLine();
            }
        }

        var currentSources = plan
            .HarnessElements.Where(e =>
                e.Type.Equals("VDC", StringComparison.OrdinalIgnoreCase)
                || e.Type.Equals("VAC", StringComparison.OrdinalIgnoreCase)
                || e.Type.Equals("VSIN", StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var acIndex = 0;
        foreach (var a in plan.Analyses.Where(a => a.Type == BenchValueType.ACAnalysis))
        {
            acIndex++;
            var start = SiValue.FormatForBackend(a.StartHz, backend);
            var stop = SiValue.FormatForBackend(a.StopHz, backend);

            var space = a.Space.Equals("lin", StringComparison.OrdinalIgnoreCase) ? "lin" : "dec";
            sb.AppendLine($"ac {space} {a.Samples} {start} {stop}");
            sb.AppendLine($"setplot ac{acIndex}");

            var wrdata = BenchRuntimePaths.GetAcWrdataPath(
                outputDir,
                plan.CircuitName,
                plan.BindingName,
                a.Name
            );
            sb.Append($"wrdata {Path.GetFileName(wrdata)}");
            foreach (var node in plan.AcNodeKeys)
            {
                sb.Append(' ');
                sb.Append($"v({node})");
            }
            sb.AppendLine();

            if (plan.RequiresCurrents && currentSources.Count > 0)
            {
                var iWrdata = BenchRuntimePaths.GetAcCurrentsWrdataPath(
                    outputDir,
                    plan.CircuitName,
                    plan.BindingName,
                    a.Name
                );
                sb.Append($"wrdata {Path.GetFileName(iWrdata)}");
                foreach (var s in currentSources)
                {
                    sb.Append(' ');
                    sb.Append($"i(V{s.Id})");
                }
                sb.AppendLine();
            }
        }

        var noiseIndex = 0;
        foreach (var a in plan.Analyses.Where(a => a.Type == BenchValueType.NoiseAnalysis))
        {
            noiseIndex++;

            var start = SiValue.FormatForBackend(a.StartHz, backend);
            var stop = SiValue.FormatForBackend(a.StopHz, backend);

            var space = a.Space.Equals("lin", StringComparison.OrdinalIgnoreCase) ? "lin" : "dec";
            var input =
                a.NoiseInputSource
                ?? throw new InvalidOperationException(
                    $"NoiseAnalysis '{a.Name}' missing NoiseInputSource in plan."
                );
            var output =
                a.OutputTerminal
                ?? throw new InvalidOperationException(
                    $"NoiseAnalysis '{a.Name}' missing OutputTerminal in plan."
                );

            sb.AppendLine(
                $"noise {FormatTerminalVoltageExpr(output)} {input} {space} {a.Samples} {start} {stop}"
            );
            sb.AppendLine($"setplot noise{noiseIndex}");

            var wrdata = BenchRuntimePaths.GetNoiseWrdataPath(
                outputDir,
                plan.CircuitName,
                plan.BindingName,
                a.Name
            );
            sb.AppendLine($"wrdata {Path.GetFileName(wrdata)} onoise_spectrum");
        }

        var tranIndex = 0;
        foreach (var a in plan.Analyses.Where(a => a.Type == BenchValueType.TranAnalysis))
        {
            tranIndex++;
            var stepS =
                a.StepS
                ?? throw new InvalidOperationException(
                    $"TranAnalysis '{a.Name}' missing StepS in plan."
                );
            var stopS =
                a.StopS
                ?? throw new InvalidOperationException(
                    $"TranAnalysis '{a.Name}' missing StopS in plan."
                );

            var step = SiValue.FormatForBackend(stepS, backend);
            var stop = SiValue.FormatForBackend(stopS, backend);
            var start = a.StartS is null ? null : SiValue.FormatForBackend(a.StartS.Value, backend);

            if (a.StartS is not null && a.StartS.Value > 0)
            {
                sb.AppendLine($"tran {step} {stop} {start}");
            }
            else
            {
                sb.AppendLine($"tran {step} {stop}");
            }

            sb.AppendLine($"setplot tran{tranIndex}");

            var wrdata = BenchRuntimePaths.GetTranWrdataPath(
                outputDir,
                plan.CircuitName,
                plan.BindingName,
                a.Name
            );
            sb.Append($"wrdata {Path.GetFileName(wrdata)}");
            foreach (var node in plan.AcNodeKeys)
            {
                sb.Append(' ');
                sb.Append($"v({node})");
            }
            sb.AppendLine();

            if (plan.RequiresCurrents && currentSources.Count > 0)
            {
                var iWrdata = BenchRuntimePaths.GetTranCurrentsWrdataPath(
                    outputDir,
                    plan.CircuitName,
                    plan.BindingName,
                    a.Name
                );
                sb.Append($"wrdata {Path.GetFileName(iWrdata)}");
                foreach (var s in currentSources)
                {
                    sb.Append(' ');
                    sb.Append($"i(V{s.Id})");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("quit");
        sb.AppendLine(".endc");
        sb.AppendLine(".end");

        return sb.ToString();
    }

    private static string FormatTerminalVoltageExpr(BenchTerminalRef terminal)
    {
        if (terminal.LeafNodes.Count == 0)
        {
            return "v(0)";
        }

        if (terminal.LeafNodes.Count == 1)
        {
            return $"v({terminal.LeafNodes[0]})";
        }

        // Treat a 2-leaf terminal as a differential voltage: v(P,N).
        return $"v({terminal.LeafNodes[0]},{terminal.LeafNodes[1]})";
    }

    private static void EmitHarnessElement(
        StringBuilder sb,
        BenchHarnessElement element,
        BenchBackendType backend
    )
    {
        var type = element.Type;
        if (type.Equals("GND", StringComparison.OrdinalIgnoreCase))
        {
            if (!element.Pins.TryGetValue("GND", out var gnd))
            {
                return;
            }

            // Tie a local ground net to SPICE node 0.
            // ngspice treats a 0V source as a shorted VSRC in some setups; use a tiny resistor instead.
            sb.AppendLine($"R{element.Id}_tie {gnd} 0 1u");
            return;
        }

        if (type.Equals("VDC", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetPinPair(element, out var p, out var n))
            {
                return;
            }

            var v = GetParam(element, "V") ?? GetParam(element, "value") ?? GetFirstParam(element);
            var dc = FormatScalarForSpice(v, backend);
            sb.AppendLine($"V{element.Id} {p} {n} DC {dc}");
            return;
        }

        if (type.Equals("VAC", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetPinPair(element, out var p, out var n))
            {
                return;
            }

            var a = GetParam(element, "A") ?? GetParam(element, "ampl") ?? GetFirstParam(element);
            var phase = GetParam(element, "phase");

            var ampl = FormatScalarForSpice(a, backend);
            var deg = phase is null ? "0" : FormatScalarForSpice(phase, backend);
            sb.AppendLine($"V{element.Id} {p} {n} 0 AC {ampl} {deg}");
            return;
        }

        if (type.Equals("VSIN", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetPinPair(element, out var p, out var n))
                return;
            var dc = GetParam(element, "DC") ?? new BenchNumber(BenchNumericKind.VoltageV, 0);
            var a = GetParam(element, "A") ?? GetParam(element, "ampl");
            var freq = GetParam(element, "freq");
            var phase = GetParam(element, "phase");

            // Format scalars using backend-specific formatting
            var dcStr = FormatScalarForSpice(dc, backend);
            var aStr = FormatScalarForSpice(a, backend);
            var freqStr = FormatScalarForSpice(freq, backend);
            var phaseStr = phase != null ? FormatScalarForSpice(phase, backend) : "0";

            // ngspice syntax: Vname n+ n- sin(vo va freq td theta phase)
            // We use 0 for td (time delay) and theta (damping factor)
            sb.AppendLine($"V{element.Id} {p} {n} sin({dcStr} {aStr} {freqStr} 0 0 {phaseStr})");
            return;
        }

        if (type.Equals("Impedance", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetPinPair(element, out var p, out var n))
            {
                return;
            }

            var z = GetParam(element, "Z") ?? GetParam(element, "value") ?? GetFirstParam(element);
            EmitImpedance(sb, element.Id, p, n, z, backend);
            return;
        }
    }

    private static void EmitImpedance(
        StringBuilder sb,
        string id,
        string p,
        string n,
        BenchValue? z,
        BenchBackendType backend
    )
    {
        if (z is null)
        {
            return;
        }

        if (z is BenchNumber num)
        {
            var value = SiValue.FormatForBackend(num.Value, backend);
            switch (num.Kind)
            {
                case BenchNumericKind.ImpedanceOhm:
                    sb.AppendLine($"R{id} {p} {n} {value}");
                    return;
                case BenchNumericKind.CapacitanceF:
                    sb.AppendLine($"C{id} {p} {n} {value}");
                    return;
                case BenchNumericKind.InductanceH:
                    sb.AppendLine($"L{id} {p} {n} {value}");
                    return;
            }
        }

        if (z is BenchSymbol sym)
        {
            var parts = sym.Name.Split(
                "||",
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (parts.Length == 0)
            {
                return;
            }

            if (parts.Length == 1)
            {
                EmitImpedance(sb, id, p, n, BenchQuantity.Parse(parts[0]), backend);
                return;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                EmitImpedance(sb, $"{id}_{i}", p, n, BenchQuantity.Parse(parts[i]), backend);
            }

            return;
        }

        if (z is BenchImpedanceParallel par)
        {
            if (par.Elements.Count == 0)
            {
                return;
            }

            if (par.Elements.Count == 1)
            {
                EmitImpedance(sb, id, p, n, par.Elements[0], backend);
                return;
            }

            for (var i = 0; i < par.Elements.Count; i++)
            {
                EmitImpedance(sb, $"{id}_{i}", p, n, par.Elements[i], backend);
            }
        }
    }

    private static bool TryGetPinPair(BenchHarnessElement e, out string p, out string n)
    {
        p = string.Empty;
        n = string.Empty;

        if (
            !e.Pins.TryGetValue("P", out var p0)
            || string.IsNullOrWhiteSpace(p0)
            || !e.Pins.TryGetValue("N", out var n0)
            || string.IsNullOrWhiteSpace(n0)
        )
        {
            return false;
        }

        p = p0;
        n = n0;
        return true;
    }

    private static BenchValue? GetParam(BenchHarnessElement e, string name)
    {
        return e.Parameters.TryGetValue(name, out var v) ? v : null;
    }

    private static BenchValue? GetFirstParam(BenchHarnessElement e)
    {
        return e.Parameters.Count == 1 ? e.Parameters.Values.First() : null;
    }

    private static string FormatScalarForSpice(BenchValue? v, BenchBackendType backend)
    {
        if (v is null)
        {
            return "0";
        }

        if (v is BenchNumber n)
        {
            return SiValue.FormatForBackend(n.Value, backend);
        }

        if (v is BenchSymbol s)
        {
            // Accept raw quantities (e.g. 50Ohm, 1pF) or bare numerics.
            try
            {
                var parsed = BenchQuantity.Parse(s.Name);
                return parsed is BenchNumber pn ? SiValue.FormatForBackend(pn.Value, backend) : "0";
            }
            catch
            {
                return s.Name;
            }
        }

        return "0";
    }
}
