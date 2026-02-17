using System.Text.Json.Nodes;

namespace Cascode.Native;

internal sealed class ApiException : Exception
{
    public string Code { get; }
    public JsonNode? Details { get; }

    public ApiException(string code, string message, JsonNode? details = null)
        : base(message)
    {
        Code = code;
        Details = details;
    }
}
