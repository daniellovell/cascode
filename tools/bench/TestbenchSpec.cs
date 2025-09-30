using System.Text.Json.Serialization;

namespace Cascode.Bench;

public sealed class TestbenchSpec
{
    [JsonPropertyName("backend")] public BenchBackendType Backend { get; init; } = BenchBackendType.Ngspice;
    [JsonPropertyName("name")] public string Name { get; init; } = "gm_id";
    [JsonPropertyName("model_name")] public string ModelName { get; init; } = string.Empty;
    [JsonPropertyName("corner")] public string? Corner { get; init; }
        = null;
    [JsonPropertyName("temperature_c")] public double TemperatureC { get; init; } = 27.0;
    [JsonPropertyName("supply_v")] public double SupplyV { get; init; } = 1.8;
    [JsonPropertyName("w_m")] public double W_M { get; init; } = 1e-6;
    [JsonPropertyName("l_m")] public double L_M { get; init; } = 0.18e-6;
    [JsonPropertyName("mult")] public int Mult { get; init; } = 1;
    [JsonPropertyName("nf")] public int Nfingers { get; init; } = 1;

    [JsonPropertyName("vgs_sweep")] public SweepSpec Vgs { get; init; } = new(0.0, 1.2, 0.01);
    [JsonPropertyName("vds_fixed")] public double Vds { get; init; } = 0.9;
    [JsonPropertyName("vsb_fixed")] public double Vsb { get; init; } = 0.0;

    [JsonPropertyName("includes")] public IReadOnlyList<string> Includes { get; init; } = Array.Empty<string>();
    [JsonPropertyName("section")] public string? Section { get; init; } = null;

    [JsonPropertyName("job_dir")] public string JobDir { get; init; } = string.Empty;
    [JsonPropertyName("results_csv")] public string ResultsCsv { get; init; } = "results.csv";
}

public sealed class SweepSpec
{
    public SweepSpec() { }
    public SweepSpec(double start, double stop, double step)
    {
        Start = start; Stop = stop; Step = step;
    }

    [JsonPropertyName("start")] public double Start { get; init; }
    [JsonPropertyName("stop")] public double Stop { get; init; }
    [JsonPropertyName("step")] public double Step { get; init; }
}

public sealed class TestbenchFiles
{
    public string RootDir { get; init; } = string.Empty;
    public string NetlistPath { get; init; } = string.Empty;
    public string RunnerPath { get; init; } = string.Empty;
    public string SpecPath { get; init; } = string.Empty;
    public string ResultsCsv { get; init; } = string.Empty;
}

