using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

internal static class NameNormalization
{
    private static readonly string[] VtTokens = { "ulvt", "llvt", "slvt", "lvt", "rvt", "svt", "nvt", "hvt", "mvt" };
    private static readonly string[] InfraTokens = { "tap", "subtap", "nwelltap", "diffconn", "polyconn", "via", "vias", "customvias" };

    public static SpectreModelDeviceClass ClassifyByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return SpectreModelDeviceClass.Unknown;

        var lower = name.ToLowerInvariant();
        if (lower.Contains("nmos") || lower.Contains("nfet") || lower.Contains("_nch") || lower.Contains("_n_")) return SpectreModelDeviceClass.Nmos;
        if (lower.Contains("pmos") || lower.Contains("pfet") || lower.Contains("_pch") || lower.Contains("_p_")) return SpectreModelDeviceClass.Pmos;
        if (lower.Contains("npn") || lower.Contains("pnp")) return SpectreModelDeviceClass.Bipolar;
        if (lower.Contains("diode") || lower.StartsWith("d")) return SpectreModelDeviceClass.Diode;
        if (lower.Contains("res") || lower.StartsWith("r")) return SpectreModelDeviceClass.Resistor;
        if (lower.Contains("cap") || lower.StartsWith("c")) return SpectreModelDeviceClass.Capacitor;
        if (lower.Contains("ind") || lower.StartsWith("l")) return SpectreModelDeviceClass.Inductor;
        if (lower.Contains("tline")) return SpectreModelDeviceClass.TransmissionLine;
        if (lower.Contains("moscap")) return SpectreModelDeviceClass.Moscap;
        return SpectreModelDeviceClass.Other;
    }

    public static IReadOnlyList<string> ExtractVtTags(string name)
    {
        var lower = name.ToLowerInvariant();
        var tags = new List<string>();
        foreach (var vt in VtTokens)
        {
            if (lower.Contains("_" + vt) || lower.EndsWith(vt, StringComparison.Ordinal)) tags.Add(vt.ToUpperInvariant());
        }
        return tags;
    }

    public static IReadOnlyList<string> ExtractVddTags(string name)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(name, @"\d+v\d+", RegexOptions.IgnoreCase))
        {
            list.Add(m.Value.ToLowerInvariant());
        }
        return list;
    }

    public static bool LooksInfra(string name)
    {
        var lower = name.ToLowerInvariant();
        return InfraTokens.Any(tok => lower.Contains(tok));
    }
}
