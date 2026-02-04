namespace Cascode.Render.Svg;

using Cascode.Language;

/// <summary>
/// Formats device parameter labels for SVG schematic rendering.
/// </summary>
internal static class DeviceParamFormatter
{
    /// <summary>
    /// Formats a concise parameter label string for a device.
    /// </summary>
    internal static string FormatParams(DeviceDeclaration device)
    {
        var parts = new List<string>();
        var type = DeviceTypeHelper.Normalize(device.DeviceType);

        if (DeviceTypeHelper.IsMosfet(type))
        {
            if (device.Size is not null)
            {
                if (device.Size.Entries.TryGetValue("W", out var w))
                {
                    parts.Add($"W={w}");
                }
                if (device.Size.Entries.TryGetValue("L", out var l))
                {
                    parts.Add($"L={l}");
                }
                if (device.Size.Entries.TryGetValue("M", out var m) && m != "1")
                {
                    parts.Add($"M={m}");
                }
            }
            else if (!string.IsNullOrWhiteSpace(device.SizeName))
            {
                parts.Add($"size={device.SizeName}");
            }
        }
        else if (type == "resistor")
        {
            if (device.Size?.Entries.TryGetValue("R", out var r) == true)
            {
                parts.Add($"R={r}");
            }
            else if (!string.IsNullOrWhiteSpace(device.SizeName))
            {
                parts.Add($"size={device.SizeName}");
            }
        }
        else if (type == "capacitor")
        {
            if (device.Size?.Entries.TryGetValue("C", out var c) == true)
            {
                parts.Add($"C={c}");
            }
            else if (!string.IsNullOrWhiteSpace(device.SizeName))
            {
                parts.Add($"size={device.SizeName}");
            }
        }
        else if (type == "inductor")
        {
            if (device.Size?.Entries.TryGetValue("L", out var ind) == true)
            {
                parts.Add($"L={ind}");
            }
            else if (!string.IsNullOrWhiteSpace(device.SizeName))
            {
                parts.Add($"size={device.SizeName}");
            }
        }

        return string.Join(" ", parts);
    }
}
