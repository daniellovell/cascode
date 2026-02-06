namespace Cascode.Render.Svg;

using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// Provides access to embedded SVG device symbols.
/// </summary>
public static partial class SymbolLibrary
{
    private static readonly Lazy<Dictionary<string, string>> _symbols = new(LoadSymbols);

    /// <summary>
    /// Gets the NMOS transistor symbol SVG content.
    /// </summary>
    public static string Nmos => GetSymbol("nmos");

    /// <summary>
    /// Gets the PMOS transistor symbol SVG content.
    /// </summary>
    public static string Pmos => GetSymbol("pmos");

    /// <summary>
    /// Gets the resistor symbol SVG content.
    /// </summary>
    public static string Resistor => GetSymbol("resistor");

    /// <summary>
    /// Gets the capacitor symbol SVG content.
    /// </summary>
    public static string Capacitor => GetSymbol("capacitor");

    /// <summary>
    /// Gets the inductor symbol SVG content.
    /// </summary>
    public static string Inductor => GetSymbol("inductor");

    /// <summary>
    /// Gets the supply symbol SVG content.
    /// </summary>
    public static string Supply => GetSymbol("supply");

    /// <summary>
    /// Gets the port symbol SVG content.
    /// </summary>
    public static string Port => GetSymbol("port");

    /// <summary>
    /// Gets a symbol by device type name.
    /// </summary>
    public static string GetSymbolForDevice(string deviceType)
    {
        var type = DeviceTypeHelper.Normalize(deviceType);
        return type switch
        {
            "nmos" => Nmos,
            "pmos" => Pmos,
            "resistor" => Resistor,
            "capacitor" => Capacitor,
            "inductor" => Inductor,
            "port" => Port,
            _ => string.Empty,
        };
    }

    private static string GetSymbol(string name)
    {
        return _symbols.Value.TryGetValue(name, out var svg) ? svg : string.Empty;
    }

    private static Dictionary<string, string> LoadSymbols()
    {
        var symbols = new Dictionary<string, string>();
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        foreach (var resourceName in resourceNames)
        {
            if (!resourceName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Extract symbol name from resource name
            var symbolName = ExtractSymbolName(resourceName);
            if (string.IsNullOrEmpty(symbolName))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            var svgContent = reader.ReadToEnd();

            // Extract inner content (remove XML declaration and outer SVG wrapper)
            var innerContent = ExtractInnerSvg(svgContent);
            symbols[symbolName] = innerContent;
        }

        return symbols;
    }

    private static string ExtractSymbolName(string resourceName)
    {
        // Resource names are like "Cascode.Render.symbols.nmos.svg"
        var parts = resourceName.Split('.');
        if (parts.Length < 2)
        {
            return string.Empty;
        }

        // Get the second-to-last part (filename without extension)
        return parts[^2].ToLowerInvariant();
    }

    private static string ExtractInnerSvg(string svgContent)
    {
        // Remove XML declaration
        svgContent = XmlDeclarationRegex().Replace(svgContent, "");

        // Extract content between <svg> and </svg> tags
        var match = SvgContentRegex().Match(svgContent);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return svgContent;
    }

    [GeneratedRegex(@"<\?xml[^?]*\?>", RegexOptions.Singleline)]
    private static partial Regex XmlDeclarationRegex();

    [GeneratedRegex(@"<svg[^>]*>(.*)</svg>", RegexOptions.Singleline)]
    private static partial Regex SvgContentRegex();
}
