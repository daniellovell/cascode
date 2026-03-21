namespace Cascode.Render.Svg;

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// A single vector path element extracted from an SVG symbol.
/// </summary>
/// <param name="D">SVG path <c>d</c> attribute string (M, L, C, Q, A, Z commands).</param>
/// <param name="Style"><c>"stroke"</c> or <c>"fill"</c> — tells the renderer how to draw the path.</param>
public sealed record ParsedSymbolPath(string D, string Style);

/// <summary>
/// A fully parsed device symbol ready for rendering.
/// </summary>
/// <param name="ViewBox">SVG viewBox as <c>[x, y, width, height]</c>.</param>
/// <param name="Paths">Vector paths comprising the symbol artwork.</param>
/// <param name="Terminals">Pin positions in symbol-local coordinates, keyed by terminal name (e.g. "G", "D", "S").</param>
public sealed record ParsedSymbol(
    double[] ViewBox,
    IReadOnlyList<ParsedSymbolPath> Paths,
    IReadOnlyDictionary<string, (double X, double Y)> Terminals
);

/// <summary>
/// Provides access to embedded SVG device symbols.
/// </summary>
public static partial class SymbolLibrary
{
    private sealed record SymbolData(string InnerSvg, double[] ViewBox);

    private static readonly Lazy<Dictionary<string, SymbolData>> _symbols = new(LoadSymbols);

    private static readonly Lazy<Dictionary<string, ParsedSymbol>> _parsedSymbols = new(
        BuildParsedSymbols
    );

    /// <summary>
    /// Symbol-local terminal positions for each device type.
    /// These are the SVG coordinates where pin endpoints are drawn.
    /// </summary>
    private static readonly Dictionary<
        string,
        IReadOnlyDictionary<string, (double X, double Y)>
    > TerminalPositionsByType = new(StringComparer.Ordinal)
    {
        ["nmos"] = new Dictionary<string, (double, double)>
        {
            ["G"] = (0.5, 12.5),
            ["D"] = (16.5, 0.5),
            ["S"] = (16.5, 25.5),
        },
        ["pmos"] = new Dictionary<string, (double, double)>
        {
            ["G"] = (0.5, 12.5),
            ["D"] = (16.5, 25.5),
            ["S"] = (16.5, 0.5),
        },
        ["resistor"] = new Dictionary<string, (double, double)>
        {
            ["P"] = (0.5, 4.5131),
            ["N"] = (25.5, 4.5131),
        },
        ["capacitor"] = new Dictionary<string, (double, double)>
        {
            ["P"] = (0.5, 4.4994),
            ["N"] = (25.5, 4.4994),
        },
        ["inductor"] = new Dictionary<string, (double, double)>
        {
            ["P"] = (0.5, 4.8601),
            ["N"] = (25.5, 4.8601),
        },
        ["port"] = new Dictionary<string, (double, double)> { ["Pin"] = (13.0, 2.5) },
        ["supply"] = new Dictionary<string, (double, double)> { ["Pin"] = (4.5, 8.5) },
    };

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

    /// <summary>
    /// Gets a fully parsed symbol for the given device type, including vector paths,
    /// viewBox, and terminal positions in symbol-local coordinates.
    /// </summary>
    /// <param name="deviceType">Device type string (e.g. "nmos", "resistor"). Case-insensitive.</param>
    /// <returns>A <see cref="ParsedSymbol"/> or <c>null</c> if the device type has no symbol.</returns>
    public static ParsedSymbol? GetParsedSymbol(string deviceType)
    {
        var type = DeviceTypeHelper.Normalize(deviceType);
        return _parsedSymbols.Value.TryGetValue(type, out var parsed) ? parsed : null;
    }

    private static string GetSymbol(string name)
    {
        return _symbols.Value.TryGetValue(name, out var data) ? data.InnerSvg : string.Empty;
    }

    private static Dictionary<string, SymbolData> LoadSymbols()
    {
        var symbols = new Dictionary<string, SymbolData>(StringComparer.Ordinal);
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        foreach (var resourceName in resourceNames)
        {
            if (!resourceName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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

            var viewBox = ExtractViewBox(svgContent);
            var innerContent = ExtractInnerSvg(svgContent);
            symbols[symbolName] = new SymbolData(innerContent, viewBox);
        }

        return symbols;
    }

    private static Dictionary<string, ParsedSymbol> BuildParsedSymbols()
    {
        var result = new Dictionary<string, ParsedSymbol>(StringComparer.Ordinal);

        foreach (var (name, data) in _symbols.Value)
        {
            var paths = ParseSvgPaths(data.InnerSvg);
            var terminals = TerminalPositionsByType.TryGetValue(name, out var t)
                ? t
                : new Dictionary<string, (double, double)>();

            result[name] = new ParsedSymbol(data.ViewBox, paths, terminals);
        }

        return result;
    }

    /// <summary>
    /// Parses SVG inner content into a list of path entries by extracting
    /// <c>&lt;line&gt;</c>, <c>&lt;polygon&gt;</c>, and <c>&lt;path&gt;</c> elements.
    /// </summary>
    private static List<ParsedSymbolPath> ParseSvgPaths(string innerSvg)
    {
        var paths = new List<ParsedSymbolPath>();

        // Wrap in a root element for XML parsing
        XElement root;
        try
        {
            root = XElement.Parse($"<root>{innerSvg}</root>");
        }
        catch
        {
            return paths;
        }

        foreach (var element in root.Descendants())
        {
            var cls = element.Attribute("class")?.Value ?? "";
            var style = cls.Contains("fill", StringComparison.OrdinalIgnoreCase)
                ? "fill"
                : "stroke";

            switch (element.Name.LocalName)
            {
                case "line":
                    paths.Add(new ParsedSymbolPath(ConvertLineToPath(element), style));
                    break;

                case "polygon":
                    var polygonPath = ConvertPolygonToPath(element);
                    if (polygonPath is not null)
                    {
                        paths.Add(new ParsedSymbolPath(polygonPath, style));
                    }
                    break;

                case "path":
                    var d = element.Attribute("d")?.Value;
                    if (!string.IsNullOrEmpty(d))
                    {
                        paths.Add(new ParsedSymbolPath(d, style));
                    }
                    break;
            }
        }

        return paths;
    }

    /// <summary>
    /// Converts a <c>&lt;line&gt;</c> element to an SVG path <c>d</c> string.
    /// </summary>
    private static string ConvertLineToPath(XElement line)
    {
        var x1 = line.Attribute("x1")?.Value ?? "0";
        var y1 = line.Attribute("y1")?.Value ?? "0";
        var x2 = line.Attribute("x2")?.Value ?? "0";
        var y2 = line.Attribute("y2")?.Value ?? "0";
        return $"M {x1} {y1} L {x2} {y2}";
    }

    /// <summary>
    /// Converts a <c>&lt;polygon&gt;</c> element to an SVG path <c>d</c> string.
    /// </summary>
    private static string? ConvertPolygonToPath(XElement polygon)
    {
        var points = polygon.Attribute("points")?.Value;
        if (string.IsNullOrWhiteSpace(points))
        {
            return null;
        }

        var coords = points.Trim().Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (coords.Length < 4)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"M {coords[0]} {coords[1]}");
        for (var i = 2; i + 1 < coords.Length; i += 2)
        {
            sb.Append(CultureInfo.InvariantCulture, $" L {coords[i]} {coords[i + 1]}");
        }
        sb.Append(" Z");
        return sb.ToString();
    }

    /// <summary>
    /// Extracts the viewBox attribute from the outer <c>&lt;svg&gt;</c> tag as a 4-element array.
    /// </summary>
    private static double[] ExtractViewBox(string svgContent)
    {
        var match = ViewBoxRegex().Match(svgContent);
        if (!match.Success)
        {
            return [0, 0, 0, 0];
        }

        var parts = match.Groups[1].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return [0, 0, 0, 0];
        }

        return
        [
            double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture),
            double.Parse(parts[3], CultureInfo.InvariantCulture),
        ];
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

    [GeneratedRegex(@"viewBox=""([^""]*)""", RegexOptions.Singleline)]
    private static partial Regex ViewBoxRegex();
}
