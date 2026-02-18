namespace Cascode.Render.Svg;

/// <summary>
/// Normalizes and classifies device type strings used during SVG rendering.
/// </summary>
internal static class DeviceTypeHelper
{
    /// <summary>
    /// Normalizes a device type name to a canonical form for comparisons.
    /// </summary>
    internal static string Normalize(string deviceType)
    {
        return deviceType.ToLowerInvariant();
    }

    /// <summary>
    /// Returns true if the normalized device type represents a MOSFET.
    /// </summary>
    internal static bool IsMosfet(string normalizedDeviceType)
    {
        return normalizedDeviceType is "nmos" or "pmos" or "nfet" or "pfet";
    }

    /// <summary>
    /// Returns true if the normalized device type is a passive element symbolized in schematics.
    /// </summary>
    internal static bool IsPassive(string normalizedDeviceType)
    {
        return normalizedDeviceType is "resistor" or "capacitor" or "inductor";
    }

    internal static bool IsInstanceBlock(string normalizedDeviceType)
    {
        return normalizedDeviceType == "instance";
    }
}
