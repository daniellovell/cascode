namespace Cascode.Render.Svg;

/// <summary>
/// Predefined style configurations for schematic rendering.
/// </summary>
public sealed class StyleSheet
{
    public string WireStroke { get; init; } = "#231f20";
    public string DeviceStroke { get; init; } = "#231f20";
    public string DeviceFill { get; init; } = "none";
    public string LabelFont { get; init; } = "Inter, sans-serif";
    public string LabelColor { get; init; } = "#231f20";
    public string BackgroundColor { get; init; } = "white";
    public int StrokeWidth { get; init; } = 1;
    public int FontSize { get; init; } = 10;
    public int JunctionRadius { get; init; } = 3;

    /// <summary>
    /// Default style: clean professional appearance.
    /// </summary>
    public static StyleSheet Default { get; } = new StyleSheet();

    /// <summary>
    /// Dark style: for dark UI backgrounds.
    /// </summary>
    public static StyleSheet Dark { get; } =
        new StyleSheet
        {
            WireStroke = "#e0e0e0",
            DeviceStroke = "#e0e0e0",
            LabelColor = "#e0e0e0",
            BackgroundColor = "transparent",
        };

    /// <summary>
    /// Minimal style: thin strokes, no labels by default.
    /// </summary>
    public static StyleSheet Minimal { get; } = new StyleSheet { StrokeWidth = 1, FontSize = 8 };

    /// <summary>
    /// Publication style: crisp black lines for print.
    /// </summary>
    public static StyleSheet Publication { get; } =
        new StyleSheet
        {
            WireStroke = "#000000",
            DeviceStroke = "#000000",
            LabelColor = "#000000",
            StrokeWidth = 1,
            LabelFont = "serif",
        };

    /// <summary>
    /// Gets a style by name.
    /// </summary>
    public static StyleSheet GetByName(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "dark" => Dark,
            "minimal" => Minimal,
            "publication" => Publication,
            _ => Default,
        };
    }

    /// <summary>
    /// Generates CSS for embedding in SVG.
    /// </summary>
    public string ToCss()
    {
        return $@"
            .wire {{ stroke: {WireStroke}; stroke-width: {StrokeWidth}; fill: none; }}
            .junction {{ fill: {WireStroke}; }}
            .device {{ stroke: {DeviceStroke}; fill: {DeviceFill}; }}
            .device-label {{ font-family: {LabelFont}; font-size: {FontSize}px; fill: {LabelColor}; }}
            .param-label {{ font-family: {LabelFont}; font-size: {FontSize - 2}px; fill: {LabelColor}; opacity: 0.8; }}
            .port-label {{ font-family: {LabelFont}; font-size: {FontSize}px; fill: {LabelColor}; font-weight: bold; }}
            .rail {{ stroke: {WireStroke}; stroke-width: 2; }}
        ".Trim();
    }
}
