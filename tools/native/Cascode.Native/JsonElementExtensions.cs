using System.Text.Json;

namespace Cascode.Native;

internal static class JsonElementExtensions
{
    internal static string RequireString(this JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String)
        {
            return child.GetString()!;
        }

        throw new ApiException("CASAPI-INVALID-REQUEST", $"Missing string field '{name}'.");
    }

    internal static int RequireInt(this JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var child))
        {
            if (child.TryGetInt32(out var intValue))
                return intValue;
            // Accept floating-point numbers and round — the layout API returns
            // exact double positions but the language only supports integers.
            if (child.TryGetDouble(out var doubleValue))
                return (int)Math.Round(doubleValue);
        }

        throw new ApiException("CASAPI-INVALID-REQUEST", $"Missing integer field '{name}'.");
    }

    internal static string? TryGetString(this JsonElement element, string name)
    {
        return
            element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String
            ? child.GetString()
            : null;
    }
}
