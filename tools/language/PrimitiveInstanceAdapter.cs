using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Cascode.Language;

public static class PrimitiveInstanceAdapter
{
    public static IEnumerable<DeviceDeclaration> EnumerateDevices(FillBlock? fill)
    {
        if (fill is null)
        {
            yield break;
        }

        foreach (var device in fill.Devices)
        {
            yield return device;
        }

        foreach (var instance in fill.Instances)
        {
            if (TryCreateDeviceDeclaration(instance, out var device))
            {
                yield return device;
            }
        }
    }

    public static bool TryCreateDeviceDeclaration(
        InstanceDeclaration instance,
        [NotNullWhen(true)] out DeviceDeclaration? device
    )
    {
        if (!TryGetPrimitiveKind(instance, out var primitiveKind))
        {
            device = null;
            return false;
        }

        string? sizeName = null;
        SizePack? sizePack = null;
        if (instance.Sizes.TryGetValue("value", out var inlineSize))
        {
            sizePack = inlineSize;
        }
        else if (
            instance.Params.TryGetValue("value", out var value)
            && !string.IsNullOrWhiteSpace(value.Symbolic)
        )
        {
            sizeName = value.Symbolic;
        }

        device = new DeviceDeclaration
        {
            DeviceType = primitiveKind,
            Id = instance.Id,
            Primitive = InstanceTargetResolver.GetReferenceName(instance.Type),
            SizeName = sizeName,
            Size = sizePack,
            Bindings = instance.Bindings,
        };
        return true;
    }

    private static bool TryGetPrimitiveKind(
        InstanceDeclaration instance,
        [NotNullWhen(true)] out string? primitiveKind
    )
    {
        primitiveKind =
            instance.DeclaredType ?? InstanceTargetResolver.GetReferenceName(instance.Type);
        return primitiveKind
            is "NMOS"
                or "PMOS"
                or "Resistor"
                or "Capacitor"
                or "Inductor"
                or "Diode";
    }
}
