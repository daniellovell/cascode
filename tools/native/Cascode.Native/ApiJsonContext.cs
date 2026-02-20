using System.Text.Json.Serialization;

namespace Cascode.Native;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
)]
[JsonSerializable(typeof(SchematicDocumentResponse))]
[JsonSerializable(typeof(StructuralInfo))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
