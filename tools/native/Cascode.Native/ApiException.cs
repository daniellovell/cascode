using System.Text.Json.Nodes;

namespace Cascode.Native;

internal sealed class ApiException : Exception
{
    public string Code { get; }
    public JsonNode? Details { get; }

    /// <summary>
    /// Initializes a new ApiException containing an API error code, a human-readable message, and optional JSON details.
    /// </summary>
    /// <param name="code">The machine-readable error code identifying the API error.</param>
    /// <param name="message">A human-readable message describing the error.</param>
    /// <param name="details">Optional JSON payload with additional error details; may be null.</param>
    public ApiException(string code, string message, JsonNode? details = null)
        : base(message)
    {
        Code = code;
        Details = details;
    }
}
