using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

internal static class NameNormalization
{
    private static readonly string[] VtTokens = { "ulvt", "llvt", "slvt", "lvt", "rvt", "svt", "nvt", "hvt", "mvt" };
    private static readonly string[] InfraTokens = { "tap", "subtap", "nwelltap", "diffconn", "polyconn", "via", "vias", "customvias" };

    /// <summary>
    /// Classifies a component or cell name into a DeviceClass category.
    /// </summary>
    /// <param name="name">The device or cell name to classify. If null or whitespace, classification is <see cref="DeviceClass.Unknown"/>.</param>
    /// <returns>
    /// The inferred <see cref="DeviceClass"/> based on common naming patterns for
    /// standard cells and primitive devices. Returns <see cref="DeviceClass.Unknown"/>
    /// when the input is null or whitespace.
    /// </returns>
    public static DeviceClass ClassifyByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return DeviceClass.Unknown;

        var lower = name.ToLowerInvariant();

        // Check for standard cells first (more specific patterns)
        if (LooksLikeStdcell(lower)) return DeviceClass.Stdcell;

        // Then check for primitive devices
        if (lower.Contains("nmos") || lower.Contains("nfet") || lower.Contains("_nch") || lower.Contains("_n_")) return DeviceClass.Nmos;
        if (lower.Contains("pmos") || lower.Contains("pfet") || lower.Contains("_pch") || lower.Contains("_p_")) return DeviceClass.Pmos;
        if (lower.Contains("npn") || lower.Contains("pnp")) return DeviceClass.Bipolar;
        if (lower.Contains("diode") || lower.StartsWith("d")) return DeviceClass.Diode;
        if (lower.Contains("res") || lower.StartsWith("r")) return DeviceClass.Resistor;
        if (lower.Contains("cap") || lower.StartsWith("c")) return DeviceClass.Capacitor;
        if (lower.Contains("ind") || lower.StartsWith("l")) return DeviceClass.Inductor;
        if (lower.Contains("tline")) return DeviceClass.TransmissionLine;
        if (lower.Contains("moscap")) return DeviceClass.Moscap;
        return DeviceClass.Other;
    }

    /// <summary>
    /// Determines whether the provided lowercase device name appears to be a standard cell based on common name prefixes.
    /// </summary>
    /// <param name="lower">The device name already converted to lowercase.</param>
    /// <returns>
    /// `true` if the name starts with a recognizable standard-cell prefix (for
    /// example, logic gates or flip-flops); `false` otherwise.
    /// </returns>
    private static bool LooksLikeStdcell(string lower)
    {
        // Common standard cell prefixes
        return lower.StartsWith("inv") ||
               lower.StartsWith("buf") ||
               lower.StartsWith("nand") ||
               lower.StartsWith("nor") ||
               lower.StartsWith("and") ||
               lower.StartsWith("or") ||
               lower.StartsWith("xor") ||
               lower.StartsWith("xnor") ||
               lower.StartsWith("mux") ||
               lower.StartsWith("demux") ||
               lower.StartsWith("dff") ||
               lower.StartsWith("ff") ||
               lower.StartsWith("latch") ||
               lower.StartsWith("add") ||
               lower.StartsWith("nd") || // NAND abbreviation
               lower.StartsWith("nr");   // NOR abbreviation
    }

    /// <summary>
    /// Determines the device subclass for a given component or cell name.
    /// </summary>
    /// <param name="name">The component or cell name to classify.</param>
    /// <returns>The detected DeviceSubclass (for example Inverter, Nand, MIMCAP), or DeviceSubclass.Unknown if no subclass matches.</returns>
    /// <remarks>Matching is case-insensitive and uses known prefixes for standard-cell subclasses and substring checks for capacitor and resistor subclasses.</remarks>
    public static DeviceSubclass ClassifySubclass(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return DeviceSubclass.Unknown;

        var lower = name.ToLowerInvariant();

        // Stdcell subclasses
        if (lower.StartsWith("inv")) return DeviceSubclass.Inverter;
        if (lower.StartsWith("buf")) return DeviceSubclass.Buffer;
        if (lower.StartsWith("nand") || lower.StartsWith("nd")) return DeviceSubclass.Nand;
        if (lower.StartsWith("nor") || lower.StartsWith("nr")) return DeviceSubclass.Nor;
        if (lower.StartsWith("and")) return DeviceSubclass.And;
        if (lower.StartsWith("or")) return DeviceSubclass.Or;
        if (lower.StartsWith("xor")) return DeviceSubclass.Xor;
        if (lower.StartsWith("xnor")) return DeviceSubclass.Xnor;
        if (lower.StartsWith("mux")) return DeviceSubclass.Multiplexer;
        if (lower.StartsWith("demux")) return DeviceSubclass.Demultiplexer;
        if (lower.StartsWith("dff") || lower.StartsWith("ff")) return DeviceSubclass.Flipflop;
        if (lower.StartsWith("latch")) return DeviceSubclass.Latch;
        if (lower.StartsWith("add")) return DeviceSubclass.Adder;

        // Capacitor subclasses
        if (lower.Contains("mimcap")) return DeviceSubclass.MIMCAP;
        if (lower.Contains("momcap")) return DeviceSubclass.MOMCAP;
        if (lower.Contains("varcap")) return DeviceSubclass.VarCap;

        // Resistor subclasses
        if (lower.Contains("tfr")) return DeviceSubclass.TFR;
        if (lower.Contains("rmetal")) return DeviceSubclass.RMetal;
        if (lower.Contains("rpoly")) return DeviceSubclass.RPoly;
        if (lower.Contains("rwell")) return DeviceSubclass.RWell;

        return DeviceSubclass.Unknown;
    }

    /// <summary>
    /// Extracts voltage/threshold (VT) variant tags from a device name.
    /// </summary>
    /// <param name="name">The device name to inspect for VT tokens.</param>
    /// <returns>An uppercased list of VT tags found in the name; if no tags are detected, returns a list containing "SVT".</returns>
    public static IReadOnlyList<string> ExtractVtTags(string name)
    {
        var lower = name.ToLowerInvariant();
        var tags = new List<string>();
        foreach (var vt in VtTokens)
        {
            if (lower.Contains("_" + vt) || lower.EndsWith(vt, StringComparison.Ordinal)) tags.Add(vt.ToUpperInvariant());
        }
        if (tags.Count == 0) tags.Add("SVT");
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