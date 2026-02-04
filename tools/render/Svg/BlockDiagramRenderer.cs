namespace Cascode.Render.Svg;

using System.Text;
using Cascode.Language;
using static Cascode.Render.Svg.SvgFormat;

/// <summary>
/// Renders a simple instance block diagram to SVG.
/// </summary>
public sealed class BlockDiagramRenderer
{
    public string Render(Circuit circuit, StyleSheet style, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(options);

        var instances = circuit.Fill?.Instances ?? new List<InstanceDeclaration>();

        const int margin = 40;
        const int rowHeight = 70;
        const int boxHeight = 44;
        const int boxWidth = 520;
        const int boxX = margin;

        var width = options.ExplicitWidth ?? (boxX + boxWidth + margin);
        var height =
            options.ExplicitHeight ?? (margin * 2 + Math.Max(1, instances.Count) * rowHeight);

        var sb = new StringBuilder();
        sb.AppendLine(
            $@"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 {width} {height}"" width=""{width}"" height=""{height}"">"
        );
        sb.AppendLine("<style>");
        sb.AppendLine(style.ToCss());
        sb.AppendLine("</style>");

        if (!string.IsNullOrEmpty(options.Title))
        {
            sb.AppendLine($@"<title>{EscapeXml(options.Title)}</title>");
        }

        if (style.BackgroundColor != "transparent")
        {
            sb.AppendLine(
                $@"<rect width=""{width}"" height=""{height}"" fill=""{style.BackgroundColor}"" />"
            );
        }

        sb.AppendLine(@"<g id=""blocks"">");

        for (var i = 0; i < instances.Count; i++)
        {
            var inst = instances[i];
            var y = margin + i * rowHeight;
            sb.AppendLine(
                $@"<rect class=""block"" x=""{boxX}"" y=""{y}"" width=""{boxWidth}"" height=""{boxHeight}"" />"
            );

            var label = $"{inst.Id}: {inst.Type}";
            var textX = boxX + 10;
            var textY = y + boxHeight / 2 + 4;
            sb.AppendLine(
                $@"<text class=""block-label"" x=""{F(textX)}"" y=""{F(textY)}"">{EscapeXml(label)}</text>"
            );
        }

        if (instances.Count == 0)
        {
            sb.AppendLine(
                $@"<text class=""block-label"" x=""{margin}"" y=""{margin + 20}"">No instances</text>"
            );
        }

        sb.AppendLine("</g>");
        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}
