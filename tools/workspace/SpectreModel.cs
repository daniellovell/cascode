using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cascode.Workspace;

public enum SpectreModelDeviceClass
{
    Unknown = 0,
    Nmos,
    Pmos,
    Bipolar,
    Diode,
    Resistor,
    Capacitor,
    Inductor,
    Moscap,
    TransmissionLine,
    Other
}

public sealed class SpectreModel
{
    public static readonly IReadOnlyList<string> EmptyStringList = Array.Empty<string>();

    public SpectreModel()
    {
        Corners = EmptyStringList;
        CornerDetails = EmptyStringList;
        Sections = EmptyStringList;
        SourceFiles = EmptyStringList;
        Decks = EmptyStringList;
    }

    public SpectreModel(
        string name,
        string modelType,
        SpectreModelDeviceClass deviceClass,
        string? voltageDomain,
        string? thresholdFlavor,
        IReadOnlyList<string> corners,
        IReadOnlyList<string> cornerDetails,
        IReadOnlyList<string> sections,
        IReadOnlyList<string> sourceFiles,
        IReadOnlyList<string> decks)
        : this()
    {
        Name = name;
        ModelType = modelType;
        DeviceClass = deviceClass;
        VoltageDomain = voltageDomain;
        ThresholdFlavor = thresholdFlavor;
        Corners = corners ?? EmptyStringList;
        CornerDetails = cornerDetails ?? EmptyStringList;
        Sections = sections ?? EmptyStringList;
        SourceFiles = sourceFiles ?? EmptyStringList;
        Decks = decks ?? EmptyStringList;
    }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("modelType")]
    public string ModelType { get; set; } = string.Empty;

    [JsonPropertyName("deviceClass")]
    public SpectreModelDeviceClass DeviceClass { get; set; } = SpectreModelDeviceClass.Unknown;

    [JsonPropertyName("voltageDomain")]
    public string? VoltageDomain { get; set; }
        = null;

    [JsonPropertyName("thresholdFlavor")]
    public string? ThresholdFlavor { get; set; }
        = null;

    [JsonPropertyName("corners")]
    public IReadOnlyList<string> Corners { get; set; }
        = EmptyStringList;

    [JsonPropertyName("cornerDetails")]
    public IReadOnlyList<string> CornerDetails { get; set; }
        = EmptyStringList;

    [JsonPropertyName("sections")]
    public IReadOnlyList<string> Sections { get; set; }
        = EmptyStringList;

    [JsonPropertyName("sourceFiles")]
    public IReadOnlyList<string> SourceFiles { get; set; }
        = EmptyStringList;

    [JsonPropertyName("decks")]
    public IReadOnlyList<string> Decks { get; set; }
        = EmptyStringList;
}
