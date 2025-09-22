using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cascode.Cli;

internal sealed class CliConfig
{
    [JsonPropertyName("pdkRoot")]
    public string? PdkRoot { get; set; }
}

internal sealed class CliConfigStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public CliConfig Load()
    {
        var path = WorkspaceState.GetConfigPath();
        if (!File.Exists(path))
        {
            return new CliConfig();
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<CliConfig>(json, SerializerOptions) ?? new CliConfig();
            Normalize(config);
            return config;
        }
        catch
        {
            return new CliConfig();
        }
    }

    public void Save(CliConfig config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        Normalize(config);
        var path = WorkspaceState.GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(path, json);
    }

    private static void Normalize(CliConfig config)
    {
        if (config.PdkRoot is null)
        {
            return;
        }

        try
        {
            config.PdkRoot = Path.GetFullPath(config.PdkRoot);
        }
        catch
        {
            config.PdkRoot = null;
        }
    }
}
