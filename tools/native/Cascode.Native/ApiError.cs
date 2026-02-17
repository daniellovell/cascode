using System.Text.Json.Nodes;

namespace Cascode.Native;

internal sealed class ApiError
{
    private const string SchemaValue = "cascode.error/1.0";

    public string Schema { get; init; } = SchemaValue;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public JsonNode? Details { get; init; }

    /// <summary>
    /// Create a JSON string that represents an API error using the class schema.
    /// </summary>
    /// <param name="code">A short error identifier.</param>
    /// <param name="message">A human-readable error message.</param>
    /// <param name="details">Optional additional JSON data to include under the "details" property; the node is deep-cloned before inclusion.</param>
    /// <returns>A JSON string containing "schema", "code", "message", and, if provided, "details".</returns>
    public static string ToJson(string code, string message, JsonNode? details = null)
    {
        var root = new JsonObject
        {
            ["schema"] = SchemaValue,
            ["code"] = code,
            ["message"] = message,
        };

        if (details is not null)
        {
            root["details"] = details.DeepClone();
        }

        return root.ToJsonString(ApiJson.Options);
    }
}