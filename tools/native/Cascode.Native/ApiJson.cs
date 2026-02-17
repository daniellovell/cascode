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

    /// <summary>
    /// Serialize a SchematicDocumentResponse into its JSON string representation using the API JSON context.
    /// </summary>
    /// <param name="document">The SchematicDocumentResponse to serialize.</param>
    /// <returns>The JSON string representation of <paramref name="document"/>.</returns>
    public static string SerializeDocument(SchematicDocumentResponse document)
    {
        return JsonSerializer.Serialize(document, ApiJsonContext.Default.SchematicDocumentResponse);
    }

    /// <summary>
    /// Serialize a SchematicDocumentResponse into a JsonNode.
    /// </summary>
    /// <param name="document">The SchematicDocumentResponse to serialize.</param>
    /// <returns>A JsonNode representation of the document; returns an empty <see cref="JsonObject"/> if serialization produces null.</returns>
    public static JsonNode SerializeDocumentNode(SchematicDocumentResponse document)
    {
        return JsonSerializer.SerializeToNode(
                document,
                ApiJsonContext.Default.SchematicDocumentResponse
            ) ?? new JsonObject();
    }

    /// <summary>
    /// Serialize a <see cref="StructuralInfo"/> instance to a <see cref="JsonNode"/>.
    /// </summary>
    /// <param name="structural">The structural information to serialize.</param>
    /// <returns>A <see cref="JsonNode"/> representing the structural object; an empty <see cref="JsonObject"/> if serialization produces null.</returns>
    public static JsonNode SerializeStructuralNode(StructuralInfo structural)
    {
        return JsonSerializer.SerializeToNode(structural, ApiJsonContext.Default.StructuralInfo)
            ?? new JsonObject();
    }
}