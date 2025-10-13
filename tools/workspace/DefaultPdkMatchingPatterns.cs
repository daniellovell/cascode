using System;

namespace Cascode.Workspace;

/// <summary>
/// Single, centralized location for the default PDK device↔model matching patterns.
///
/// The content is JSON that is also valid YAML (YAML 1.2 is a superset of JSON).
/// Keeping it in JSON lets us parse it with System.Text.Json without adding
/// external YAML dependencies to the workspace layer.
/// </summary>
internal static class DefaultPdkMatchingPatterns
{
    public const string FileName = "pdk-matching-patterns.yml";

    public static PdkMatchingConfig Build()
    {
        var cfg = new PdkMatchingConfig
        {
            Normalization = new PdkMatchingConfig.NormalizationSection
            {
                VendorPrefixes = new() { "sky130_fd_pr__" },
                ModelSuffixRegex = "(?:__|_)(?:model(?:_base)?|base)(?:\\.\\d+)?$",
                VtTokens = new() { "ulvt", "llvt", "slvt", "lvt", "rvt", "svt", "nvt", "hvt", "mvt" },
                VddTokenRegex = "_\\d+v\\d+\\b",
                VddExtractRegex = "(?<n>\\d+)(?:\\.(?<f>\\d+))?v"
            },
            Behavior = new PdkMatchingConfig.BehaviorSection
            {
                MinAcceptScore = 30,
                AmbiguousMargin = 3,
                InfraPenaltyNonEsd = 5,
                EsdKeyword = "esd"
            },
            Classify = new PdkMatchingConfig.ClassifySection
            {
                InfraTokens = new() { "tap", "subtap", "nwelltap", "diffconn", "polyconn", "via", "vias", "customvias" },
                Classes = new(System.StringComparer.OrdinalIgnoreCase)
                {
                    ["stdcell"] = new PdkMatchingConfig.ClassPattern { Prefixes = new() { "inv", "buf", "nand", "nor", "and", "or", "xor", "xnor", "mux", "demux", "dff", "ff", "latch", "add", "nd", "nr" } },
                    ["nmos"] = new PdkMatchingConfig.ClassPattern { Contains = new() { "nmos", "nfet", "nch" }, Regex = new() { "_nch", "_n_" } },
                    ["pmos"] = new PdkMatchingConfig.ClassPattern { Contains = new() { "pmos", "pfet", "pch" }, Regex = new() { "_pch", "_p_" } },
                    ["bipolar"] = new PdkMatchingConfig.ClassPattern { Contains = new() { "npn", "pnp" } },
                    ["diode"] = new PdkMatchingConfig.ClassPattern { Contains = new() { "diode" }, Prefixes = new() { "d" } },
                    ["resistor"] = new PdkMatchingConfig.ClassPattern { Contains = new() { "res", "tfr", "rmetal", "rpoly", "rwell" }, Prefixes = new() { "r" } },
                    ["capacitor"] = new PdkMatchingConfig.ClassPattern { Contains = new() { "cap", "moscap", "nmoscap", "pmoscap" }, Prefixes = new() { "c" } },
                    ["inductor"] = new PdkMatchingConfig.ClassPattern { Contains = new() { "ind" }, Prefixes = new() { "l" } },
                    ["transmissionline"] = new PdkMatchingConfig.ClassPattern { Contains = new() { "tline" } },
                },
                Subclasses = new(System.StringComparer.OrdinalIgnoreCase)
                {
                    ["stdcell"] = new System.Collections.Generic.Dictionary<string, PdkMatchingConfig.ClassPattern>(System.StringComparer.OrdinalIgnoreCase)
                    {
                        ["inverter"] = new() { Prefixes = new() { "inv" } },
                        ["buffer"] = new() { Prefixes = new() { "buf" } },
                        ["nand"] = new() { Prefixes = new() { "nand", "nd" } },
                        ["nor"] = new() { Prefixes = new() { "nor", "nr" } },
                        ["and"] = new() { Prefixes = new() { "and" } },
                        ["or"] = new() { Prefixes = new() { "or" } },
                        ["xor"] = new() { Prefixes = new() { "xor" } },
                        ["xnor"] = new() { Prefixes = new() { "xnor" } },
                        ["multiplexer"] = new() { Prefixes = new() { "mux" } },
                        ["demultiplexer"] = new() { Prefixes = new() { "demux" } },
                        ["flipflop"] = new() { Prefixes = new() { "dff", "ff" } },
                        ["latch"] = new() { Prefixes = new() { "latch" } },
                        ["adder"] = new() { Prefixes = new() { "add" } },
                    },
                    ["capacitor"] = new System.Collections.Generic.Dictionary<string, PdkMatchingConfig.ClassPattern>(System.StringComparer.OrdinalIgnoreCase)
                    {
                        ["moscap"] = new() { Contains = new() { "moscap", "nmoscap", "pmoscap" } },
                        ["mimcap"] = new() { Contains = new() { "mimcap" } },
                        ["momcap"] = new() { Contains = new() { "momcap" } },
                        ["varcap"] = new() { Contains = new() { "varcap" } },
                    },
                    ["resistor"] = new System.Collections.Generic.Dictionary<string, PdkMatchingConfig.ClassPattern>(System.StringComparer.OrdinalIgnoreCase)
                    {
                        ["tfr"] = new() { Contains = new() { "tfr" } },
                        ["rmetal"] = new() { Contains = new() { "rmetal" } },
                        ["rpoly"] = new() { Contains = new() { "rpoly" } },
                        ["rwell"] = new() { Contains = new() { "rwell" } },
                    },
                    ["nmos"] = new System.Collections.Generic.Dictionary<string, PdkMatchingConfig.ClassPattern>(System.StringComparer.OrdinalIgnoreCase)
                    {
                        ["deepnwell"] = new() { Contains = new() { "dnw" } },
                        ["rf"] = new() { Contains = new() { "rf" } },
                    },
                    ["pmos"] = new System.Collections.Generic.Dictionary<string, PdkMatchingConfig.ClassPattern>(System.StringComparer.OrdinalIgnoreCase)
                    {
                        ["rf"] = new() { Contains = new() { "rf" } },
                    },
                }
            }
        };
        return cfg;
    }

    public static string RenderYaml(PdkMatchingConfig cfg)
    {
        var serializer = new YamlDotNet.Serialization.SerializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(YamlDotNet.Serialization.DefaultValuesHandling.OmitDefaults)
            .Build();
        // prepend a brief header comment for discoverability
        var body = serializer.Serialize(cfg);
        return "# Cascode PDK device↔model matching configuration\n" + body;
    }
}
