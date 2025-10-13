using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cascode.Workspace;

public sealed class PdkMatchingConfig
{
    [YamlMember(Alias = "normalization"), JsonPropertyName("normalization")] public NormalizationSection Normalization { get; set; } = new();
    [YamlMember(Alias = "behavior"), JsonPropertyName("behavior")] public BehaviorSection Behavior { get; set; } = new();
    [YamlMember(Alias = "classify"), JsonPropertyName("classify")] public ClassifySection Classify { get; set; } = new();

    public sealed class NormalizationSection
    {
        [YamlMember(Alias = "vendor_prefixes"), JsonPropertyName("vendor_prefixes")] public List<string> VendorPrefixes { get; set; } = new();
        [YamlMember(Alias = "model_suffix_regex"), JsonPropertyName("model_suffix_regex")] public string ModelSuffixRegex { get; set; } = string.Empty;
        [YamlMember(Alias = "vt_tokens"), JsonPropertyName("vt_tokens")] public List<string> VtTokens { get; set; } = new();
        [YamlMember(Alias = "vdd_token_regex"), JsonPropertyName("vdd_token_regex")] public string VddTokenRegex { get; set; } = string.Empty;
        [YamlMember(Alias = "vdd_extract_regex"), JsonPropertyName("vdd_extract_regex")] public string VddExtractRegex { get; set; } = string.Empty;
    }

    public sealed class BehaviorSection
    {
        [YamlMember(Alias = "min_accept_score"), JsonPropertyName("min_accept_score")] public int MinAcceptScore { get; set; }
        [YamlMember(Alias = "ambiguous_margin"), JsonPropertyName("ambiguous_margin")] public int AmbiguousMargin { get; set; }
        [YamlMember(Alias = "infra_penalty_non_esd"), JsonPropertyName("infra_penalty_non_esd")] public int InfraPenaltyNonEsd { get; set; }
        [YamlMember(Alias = "esd_keyword"), JsonPropertyName("esd_keyword")] public string EsdKeyword { get; set; } = "esd";
    }

    public sealed class ClassifySection
    {
        [YamlMember(Alias = "infra_tokens"), JsonPropertyName("infra_tokens")] public List<string> InfraTokens { get; set; } = new();
        [YamlMember(Alias = "classes"), JsonPropertyName("classes")] public Dictionary<string, ClassPattern> Classes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        // Map: class name -> (subclass name -> pattern)
        [YamlMember(Alias = "subclasses"), JsonPropertyName("subclasses")] public Dictionary<string, Dictionary<string, ClassPattern>> Subclasses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ClassPattern
    {
        [YamlMember(Alias = "prefixes"), JsonPropertyName("prefixes")] public List<string>? Prefixes { get; set; }
        [YamlMember(Alias = "contains"), JsonPropertyName("contains")] public List<string>? Contains { get; set; }
        [YamlMember(Alias = "regex"), JsonPropertyName("regex")] public List<string>? Regex { get; set; }
        [YamlMember(Alias = "exclude_contains"), JsonPropertyName("exclude_contains")] public List<string>? ExcludeContains { get; set; }
        [YamlMember(Alias = "exclude_regex"), JsonPropertyName("exclude_regex")] public List<string>? ExcludeRegex { get; set; }
    }
}

/// <summary>
/// Resolves CASCODE_HOME and manages the PDK matching patterns file.
/// </summary>
public static class PdkMatchingConfigManager
{
    private static readonly object s_cacheLock = new();
    private static PdkMatchingConfig? s_cachedConfig;
    private static string? s_cachedPath;
    private static DateTime s_cachedMtimeUtc;

    public static void InvalidateCache()
    {
        lock (s_cacheLock)
        {
            s_cachedConfig = null;
            s_cachedPath = null;
            s_cachedMtimeUtc = DateTime.MinValue;
        }
    }
    /// <summary>Returns CASCODE_HOME root folder, honoring the environment override.</summary>
    public static string GetCascodeHome()
    {
        var cascodeHome = Environment.GetEnvironmentVariable("CASCODE_HOME");
        if (!string.IsNullOrWhiteSpace(cascodeHome)) return cascodeHome!;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile)) userProfile = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(userProfile)) userProfile = Directory.GetCurrentDirectory();
        return Path.Combine(userProfile, ".cascode");
    }

    /// <summary>Absolute path to the patterns file under CASCODE_HOME/config/.</summary>
    public static string GetConfigFilePath()
    {
        var root = GetCascodeHome();
        var cfgDir = Path.Combine(root, "config");
        return Path.Combine(cfgDir, DefaultPdkMatchingPatterns.FileName);
    }

    /// <summary>
    /// Ensures the patterns file exists. If missing, writes defaults. Returns true when created.
    /// </summary>
    public static bool EnsureInitialized()
    {
        var path = GetConfigFilePath();
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        if (File.Exists(path)) return false;
        var yaml = DefaultPdkMatchingPatterns.RenderYaml(DefaultPdkMatchingPatterns.Build());
        File.WriteAllText(path, yaml);
        return true;
    }

    /// <summary>
    /// Loads the matching configuration, creating the file with defaults if absent.
    /// Never throws: on error, returns defaults embedded in the assembly.
    /// </summary>
    public static PdkMatchingConfig Load(Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        string path = GetConfigFilePath();
        try
        {
            if (!File.Exists(path)) EnsureInitialized();

            var mtimeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            lock (s_cacheLock)
            {
                if (s_cachedConfig is not null && string.Equals(s_cachedPath, path, StringComparison.Ordinal) && s_cachedMtimeUtc == mtimeUtc)
                {
                    return s_cachedConfig;
                }
            }

            var text = File.ReadAllText(path);
            var cfg = DeserializeYaml(text);
            if (cfg is null || cfg.Classify is null || (cfg.Classify.Classes.Count == 0 && cfg.Classify.Subclasses.Count == 0))
            {
                // Migrate legacy configs (pre-classify) to default patterns
                cfg = DeserializeDefaults();
            }

            lock (s_cacheLock)
            {
                s_cachedConfig = cfg;
                s_cachedPath = path;
                s_cachedMtimeUtc = mtimeUtc;
            }
            return cfg;
        }
        catch (Exception ex)
        {
            try
            {
                if (logger is null)
                {
                    Console.Error.WriteLine($"[cascode] Failed to load PDK matching config at '{path}': {ex.Message}. Using embedded defaults.");
                }
                // If a logger was provided, prefer basic message to avoid extension method dependency
                else
                {
                    var msg = $"Failed to load PDK matching config at '{path}': {ex.Message}. Using embedded defaults.";
                    logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, new Microsoft.Extensions.Logging.EventId(0, "PdkMatchingConfigLoadError"), msg, ex, static (s, e) => s);
                }
            }
            catch { /* ignore console failures */ }

            var fallback = DeserializeDefaults();
            // Cache the fallback to avoid repeated parse attempts until file changes
            var mtimeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            lock (s_cacheLock)
            {
                s_cachedConfig = fallback;
                s_cachedPath = path;
                s_cachedMtimeUtc = mtimeUtc;
            }
            return fallback;
        }
    }

    private static PdkMatchingConfig DeserializeDefaults()
    {
        return DefaultPdkMatchingPatterns.Build();
    }

    private static PdkMatchingConfig? DeserializeYaml(string text)
    {
        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<PdkMatchingConfig>(text);
    }
}
