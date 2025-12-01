using System.Text.Json;

namespace Cascode.Cli;

internal sealed class CharRunConfig
{
    public string Backend { get; set; } = "spectre"; // spectre|ngspice
    public string Corner { get; set; } = "tt";
    public int Limit { get; set; } = 0; // 0 = all
    public int Jobs { get; set; } = 1;
    public string? OutRoot { get; set; } = null;

    public List<string> Classes { get; set; } = new() { "nmos", "pmos" }; // nmos/pmos
    public List<string> NameContains { get; set; } = new();
    public List<string> NameExcludes { get; set; } = new() { "esd" };
    public List<string> Vt { get; set; } = new(); // ULVT/LLVT/SLVT/LVT/RVT/SVT/NVT/HVT/MVT
    public List<string> Vdd { get; set; } = new();
    public bool? Infra { get; set; } = null;

    public static CharRunConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<CharRunConfig>(json);
                if (cfg is not null) return cfg;
            }
        }
        catch { }

        return new CharRunConfig();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
