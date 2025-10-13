using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

internal static class NameNormalization
{

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

        var cfg = PdkMatchingConfigManager.Load();
        return TryClassifyFromConfig(lower, cfg);
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

        var cfg = PdkMatchingConfigManager.Load();
        return TrySubclassFromConfig(lower, cfg);
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
        var vtTokens = PdkMatchingConfigManager.Load().Normalization.VtTokens;
        foreach (var vt in vtTokens)
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
        var tokens = PdkMatchingConfigManager.Load().Classify.InfraTokens;
        return tokens.Any(tok => lower.Contains(tok));
    }

    private static DeviceClass TryClassifyFromConfig(string lower, PdkMatchingConfig cfg)
    {
        if (cfg.Classify.Classes.Count == 0) return DeviceClass.Unknown;

        // Prefer stdcell first when provided to mimic previous heuristics
        string? bestKey = null;
        int bestScore = int.MinValue;

        foreach (var kv in cfg.Classify.Classes)
        {
            var score = MatchScore(kv.Value, lower);
            if (score > bestScore)
            {
                bestScore = score;
                bestKey = score > int.MinValue ? kv.Key : null;
            }
        }

        if (bestKey is null) return DeviceClass.Unknown;
        return MapClass(bestKey);
    }

    private static DeviceSubclass TrySubclassFromConfig(string lower, PdkMatchingConfig cfg)
    {
        if (cfg.Classify.Subclasses.Count == 0) return DeviceSubclass.Unknown;

        // Restrict subclass patterns to the primary class for this name
        var parentClass = TryClassifyFromConfig(lower, cfg);
        var parentKey = parentClass.ToString().ToLowerInvariant();
        if (cfg.Classify.Subclasses.TryGetValue(parentKey, out var subs))
        {
            foreach (var sub in subs)
            {
                var score = MatchScore(sub.Value, lower);
                if (score > int.MinValue) return MapSubclass(sub.Key);
            }
        }
        return DeviceSubclass.Unknown;
    }

    private static int MatchScore(PdkMatchingConfig.ClassPattern pattern, string lower)
    {
        if (pattern.ExcludeContains is not null && pattern.ExcludeContains.Any(tok => lower.Contains(tok))) return int.MinValue;
        if (pattern.ExcludeRegex is not null && pattern.ExcludeRegex.Any(rx => Regex.IsMatch(lower, rx, RegexOptions.IgnoreCase))) return int.MinValue;

        int score = int.MinValue;

        if (pattern.Prefixes is not null)
        {
            foreach (var p in pattern.Prefixes)
            {
                if (lower.StartsWith(p)) score = Math.Max(score, 300 + p.Length);
            }
        }
        if (pattern.Contains is not null)
        {
            foreach (var c in pattern.Contains)
            {
                if (lower.Contains(c)) score = Math.Max(score, 200 + c.Length);
            }
        }
        if (pattern.Regex is not null)
        {
            foreach (var rx in pattern.Regex)
            {
                if (Regex.IsMatch(lower, rx, RegexOptions.IgnoreCase)) score = Math.Max(score, 250);
            }
        }

        return score;
    }

    private static DeviceClass MapClass(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "nmos" => DeviceClass.Nmos,
            "pmos" => DeviceClass.Pmos,
            "bipolar" => DeviceClass.Bipolar,
            "diode" => DeviceClass.Diode,
            "resistor" => DeviceClass.Resistor,
            "capacitor" => DeviceClass.Capacitor,
            "inductor" => DeviceClass.Inductor,
            "moscap" => DeviceClass.Capacitor,
            "transmissionline" or "tline" => DeviceClass.TransmissionLine,
            "stdcell" => DeviceClass.Stdcell,
            _ => DeviceClass.Other
        };
    }

    private static DeviceSubclass MapSubclass(string name)
    {
        return name.ToLowerInvariant() switch
        {
            // Stdcell
            "inverter" => DeviceSubclass.Inverter,
            "buffer" => DeviceSubclass.Buffer,
            "nand" => DeviceSubclass.Nand,
            "nor" => DeviceSubclass.Nor,
            "and" => DeviceSubclass.And,
            "or" => DeviceSubclass.Or,
            "xor" => DeviceSubclass.Xor,
            "xnor" => DeviceSubclass.Xnor,
            "multiplexer" => DeviceSubclass.Multiplexer,
            "demultiplexer" => DeviceSubclass.Demultiplexer,
            "flipflop" => DeviceSubclass.Flipflop,
            "latch" => DeviceSubclass.Latch,
            "adder" => DeviceSubclass.Adder,
            // Capacitor
            "moscap" => DeviceSubclass.MOSCAP,
            "mimcap" => DeviceSubclass.MIMCAP,
            "momcap" => DeviceSubclass.MOMCAP,
            "varcap" => DeviceSubclass.VarCap,
            // Resistor
            "tfr" => DeviceSubclass.TFR,
            "rmetal" => DeviceSubclass.RMetal,
            "rpoly" => DeviceSubclass.RPoly,
            "rwell" => DeviceSubclass.RWell,
            // MOS device subclasses
            "deepnwell" => DeviceSubclass.DeepNwell,
            "rf" => DeviceSubclass.RF,
            _ => DeviceSubclass.Unknown
        };
    }
}
