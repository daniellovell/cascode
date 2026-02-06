namespace Cascode.Render.Svg;

using System.Globalization;

/// <summary>
/// Shared formatting helpers for SVG emission.
/// </summary>
internal static class SvgFormat
{
    /// <summary>
    /// Formats a numeric SVG attribute value using invariant culture and a compact precision.
    /// </summary>
    internal static string F(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Escapes text for safe inclusion in SVG/XML content.
    /// </summary>
    internal static string EscapeXml(string text)
    {
        return text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
