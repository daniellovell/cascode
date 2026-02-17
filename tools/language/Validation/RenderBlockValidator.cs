using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

public sealed class RenderBlockValidationResult
{
    public required RenderBlock? Render { get; init; }
    public required IReadOnlyList<string> Messages { get; init; }
}

/// <summary>
/// Validates and prunes stale render block entries against circuit structure.
/// </summary>
public static class RenderBlockValidator
{
    public static RenderBlock? Prune(Circuit circuit)
    {
        return Validate(circuit).Render;
    }

    public static RenderBlockValidationResult Validate(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        if (circuit.Render is null || circuit.Render.Entities.Count == 0)
        {
            return new RenderBlockValidationResult
            {
                Render = null,
                Messages = Array.Empty<string>(),
            };
        }

        var messages = new List<string>();
        var devicesByName =
            circuit.Fill?.Devices.ToDictionary(d => d.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, DeviceDeclaration>(StringComparer.Ordinal);
        var ports = circuit.Ports.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var nets = BuildNetSet(circuit);

        var entities = new List<RenderEntity>(circuit.Render.Entities.Count);
        foreach (var entry in circuit.Render.Entities)
        {
            var kind = ResolveKind(entry.Name, devicesByName, ports, nets);
            if (kind == RenderEntityKind.Unknown)
            {
                messages.Add(
                    $"Stale render entry '{entry.Name}' was removed because the entity no longer exists."
                );
                continue;
            }

            var normalized = new RenderEntity { Name = entry.Name, Kind = kind };

            switch (kind)
            {
                case RenderEntityKind.Device:
                    normalized.Place = ValidatePlacement(
                        entry.Place,
                        devicesByName,
                        ports,
                        allowRelative: false,
                        messages,
                        entry.Name
                    );
                    normalized.Orientation = entry.Orientation;
                    normalized.ZIndex = entry.ZIndex;
                    break;

                case RenderEntityKind.Port:
                    normalized.Place = ValidatePlacement(
                        entry.Place,
                        devicesByName,
                        ports,
                        allowRelative: false,
                        messages,
                        entry.Name
                    );
                    normalized.Side = entry.Side;
                    break;

                case RenderEntityKind.Net:
                    normalized.Route = entry.Route;
                    var points = ValidateWaypoints(
                        entry.Waypoints,
                        devicesByName,
                        ports,
                        messages,
                        entry.Name
                    );
                    normalized.Waypoints.AddRange(points);
                    break;
            }

            if (HasEffectiveData(normalized))
            {
                entities.Add(normalized);
            }
        }

        return new RenderBlockValidationResult
        {
            Render = entities.Count == 0 ? null : new RenderBlock { Entities = entities },
            Messages = messages,
        };
    }

    private static RenderEntityKind ResolveKind(
        string name,
        IReadOnlyDictionary<string, DeviceDeclaration> devicesByName,
        IReadOnlySet<string> ports,
        IReadOnlySet<string> nets
    )
    {
        if (devicesByName.ContainsKey(name))
        {
            return RenderEntityKind.Device;
        }

        if (ports.Contains(name))
        {
            return RenderEntityKind.Port;
        }

        if (nets.Contains(name))
        {
            return RenderEntityKind.Net;
        }

        return RenderEntityKind.Unknown;
    }

    private static IReadOnlySet<string> BuildNetSet(Circuit circuit)
    {
        var nets = new HashSet<string>(StringComparer.Ordinal);

        foreach (var net in circuit.Fill?.Nets ?? Enumerable.Empty<NetDeclaration>())
        {
            nets.Add(net.Id);
        }

        foreach (var supply in circuit.Supplies)
        {
            nets.Add(supply);
        }

        foreach (var ground in circuit.Grounds)
        {
            nets.Add(ground);
        }

        foreach (var port in circuit.Ports)
        {
            nets.Add(port.Name);
        }

        foreach (var device in circuit.Fill?.Devices ?? Enumerable.Empty<DeviceDeclaration>())
        {
            foreach (var (_, netName) in device.Bindings)
            {
                nets.Add(netName);
            }
        }

        return nets;
    }

    private static RenderPlacement? ValidatePlacement(
        RenderPlacement? place,
        IReadOnlyDictionary<string, DeviceDeclaration> devicesByName,
        IReadOnlySet<string> ports,
        bool allowRelative,
        List<string> messages,
        string entityName
    )
    {
        if (place is null)
        {
            return null;
        }

        if (!TryValidatePoint(place.Point, devicesByName, ports, allowRelative))
        {
            messages.Add(
                $"Render place for '{entityName}' was removed due to an invalid anchor or point expression."
            );
            return null;
        }

        return place;
    }

    private static IReadOnlyList<RenderPointExpression> ValidateWaypoints(
        IReadOnlyList<RenderPointExpression> points,
        IReadOnlyDictionary<string, DeviceDeclaration> devicesByName,
        IReadOnlySet<string> ports,
        List<string> messages,
        string entityName
    )
    {
        if (points.Count == 0)
        {
            return points;
        }

        var valid = new List<RenderPointExpression>(points.Count);
        foreach (var point in points)
        {
            if (!TryValidatePoint(point, devicesByName, ports, allowRelative: true))
            {
                messages.Add(
                    $"A waypoint for net '{entityName}' was removed due to an invalid anchor."
                );
                continue;
            }

            valid.Add(point);
        }

        return valid;
    }

    private static bool TryValidatePoint(
        RenderPointExpression point,
        IReadOnlyDictionary<string, DeviceDeclaration> devicesByName,
        IReadOnlySet<string> ports,
        bool allowRelative
    )
    {
        switch (point)
        {
            case RenderAbsPoint:
                return true;

            case RenderRelPoint:
                return allowRelative;

            case RenderRefPoint r:
                return ValidateAnchor(r.Anchor, devicesByName, ports);

            default:
                return false;
        }
    }

    private static bool ValidateAnchor(
        string anchor,
        IReadOnlyDictionary<string, DeviceDeclaration> devicesByName,
        IReadOnlySet<string> ports
    )
    {
        if (string.Equals(anchor, "canvas origin", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(anchor, "canvas center", StringComparison.Ordinal))
        {
            return true;
        }

        if (ports.Contains(anchor))
        {
            return true;
        }

        var parts = anchor.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (!devicesByName.TryGetValue(parts[0], out var device))
        {
            return false;
        }

        if (parts.Length == 1)
        {
            return true;
        }

        if (parts.Length > 2)
        {
            return false;
        }

        return GetAllowedTerminals(device.DeviceType).Contains(parts[1], StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> GetAllowedTerminals(string deviceType)
    {
        return deviceType.ToLowerInvariant() switch
        {
            "nmos" or "nfet" or "pmos" or "pfet" => new[] { "G", "D", "S" },
            "resistor" or "capacitor" => new[] { "P", "N" },
            _ => Array.Empty<string>(),
        };
    }

    private static bool HasEffectiveData(RenderEntity entity)
    {
        return entity.Place is not null
            || entity.Orientation is not null
            || entity.ZIndex is not null
            || entity.Side is not null
            || entity.Route is not null
            || entity.Waypoints.Count > 0;
    }
}
