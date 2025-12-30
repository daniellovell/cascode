using YamlDotNet.Serialization;

namespace Cascode.Bench.Yaml;

public sealed class HarnessYaml
{
    [YamlMember(Alias = "id")]
    public string Id { get; init; } = string.Empty;

    [YamlMember(Alias = "version")]
    public string? Version { get; init; } = null;

    [YamlMember(Alias = "description")]
    public string? Description { get; init; } = null;

    [YamlMember(Alias = "backends")]
    public List<string> Backends { get; init; } = new();

    [YamlMember(Alias = "params")]
    public Dictionary<string, HarnessParamYaml> Params { get; init; } = new();

    [YamlMember(Alias = "vectors")]
    public HarnessVectorsYaml? Vectors { get; init; } = null;

    [YamlMember(Alias = "templates")]
    public HarnessTemplatesYaml Templates { get; init; } = new();
}

public sealed class HarnessParamYaml
{
    [YamlMember(Alias = "type")]
    public string Type { get; init; } = "string";

    [YamlMember(Alias = "default")]
    public object? Default { get; init; } = null;

    [YamlMember(Alias = "desc")]
    public string? Description { get; init; } = null;

    [YamlMember(Alias = "required")]
    public bool Required { get; init; } = false;

    [YamlMember(Alias = "choices")]
    public List<object>? Choices { get; init; } = null;
}

public sealed class HarnessVectorsYaml
{
    [YamlMember(Alias = "columns")]
    public List<string> Columns { get; init; } = new();

    [YamlMember(Alias = "optional")]
    public List<string>? Optional { get; init; } = null;
}

public sealed class HarnessTemplatesYaml
{
    [YamlMember(Alias = "spectre")]
    public string? Spectre { get; init; } = null;

    [YamlMember(Alias = "ngspice")]
    public string? Ngspice { get; init; } = null;
}
