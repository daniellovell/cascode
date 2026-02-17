using System.Text.Json.Nodes;

namespace Cascode.Native;

internal sealed class ApiError
{
    public string Schema { get; init; } = "cascode.error/1.0";
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public JsonNode? Details { get; init; }

    public static string ToJson(string code, string message, JsonNode? details = null)
    {
        var root = new JsonObject
        {
            ["schema"] = "cascode.error/1.0",
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
