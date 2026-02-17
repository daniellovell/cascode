using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cascode.Native;

internal static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string SerializeDocument(SchematicDocumentResponse document)
    {
        return JsonSerializer.Serialize(document, ApiJsonContext.Default.SchematicDocumentResponse);
    }

    public static JsonNode SerializeDocumentNode(SchematicDocumentResponse document)
    {
        return JsonSerializer.SerializeToNode(
                document,
                ApiJsonContext.Default.SchematicDocumentResponse
            ) ?? new JsonObject();
    }

    public static JsonNode SerializeStructuralNode(StructuralInfo structural)
    {
        return JsonSerializer.SerializeToNode(structural, ApiJsonContext.Default.StructuralInfo)
            ?? new JsonObject();
    }
}
