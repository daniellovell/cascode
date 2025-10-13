using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cascode.Workspace;

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

    /// <summary>
    /// Initializes a SpectreModel with the provided property values.
    /// </summary>
    /// <param name="name">Model name.</param>
    /// <param name="modelType">Model type identifier.</param>
    /// <param name="deviceClass">Device class.</param>
    /// <param name="voltageDomain">Optional voltage domain identifier.</param>
    /// <param name="thresholdFlavor">Optional threshold flavor identifier.</param>
    /// <param name="corners">List of corner names; if null, an empty list is used.</param>
    /// <param name="cornerDetails">List of corner detail strings; if null, an empty list is used.</param>
    /// <param name="sections">List of section names; if null, an empty list is used.</param>
    /// <param name="sourceFiles">List of source file names; if null, an empty list is used.</param>
    /// <param name="decks">List of deck names; if null, an empty list is used.</param>
    public SpectreModel(
        string name,
        string modelType,
        DeviceClass deviceClass,
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
    public DeviceClass DeviceClass { get; set; } = DeviceClass.Unknown;

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