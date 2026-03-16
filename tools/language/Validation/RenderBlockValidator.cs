using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

public sealed class RenderBlockValidationResult
{
    public required RenderBlock? Render { get; init; }
    public required IReadOnlyList<RenderValidationMessage> Messages { get; init; }
}

public sealed class RenderValidationMessage
{
    public required string Text { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
}

/// <summary>
/// Validates and prunes stale render block entries against circuit structure.
/// </summary>
public static class RenderBlockValidator
{
    /// <summary>
    /// Get the validated and pruned render block for a circuit.
    /// </summary>
    /// <param name="circuit">The circuit whose render block will be validated and pruned.</param>
    /// <returns>The validated RenderBlock with stale or invalid entries removed, or <c>null</c> if no valid render entities remain.</returns>
    /// <exception cref="System.Exception">Thrown if <paramref name="circuit"/> is <c>null</c>.</exception>
    public static RenderBlock? Prune(Circuit circuit)
    {
        return Validate(circuit).Render;
    }

    /// <summary>
    /// Validate and prune the Render block of a circuit, producing a validated render and any validation messages.
    /// </summary>
    /// <param name="circuit">The circuit whose Render block will be validated and pruned.</param>
    /// <returns>
    /// A <see cref="RenderBlockValidationResult"/> whose <see cref="RenderBlockValidationResult.Render"/> is a pruned RenderBlock
    /// (or <c>null</c> if no entities remain) and whose <see cref="RenderBlockValidationResult.Messages"/> contains validation messages.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="circuit"/> is <c>null</c>.</exception>
    public static RenderBlockValidationResult Validate(Circuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        if (circuit.Render is null || circuit.Render.Entities.Count == 0)
        {
            return new RenderBlockValidationResult
            {
                Render = null,
                Messages = Array.Empty<RenderValidationMessage>(),
            };
        }

        var messages = new List<RenderValidationMessage>();
        var devicesByName = BuildRenderableDevices(circuit);
        var ports = circuit.Ports.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var nets = BuildNetSet(circuit);

        var entities = new List<RenderEntity>(circuit.Render.Entities.Count);
        foreach (var entry in circuit.Render.Entities)
        {
            var kind = ResolveKind(entry.Name, devicesByName, ports, nets);
            if (kind == RenderEntityKind.Unknown)
            {
                messages.Add(
                    new RenderValidationMessage
                    {
                        Text =
                            $"Stale render entry '{entry.Name}' was removed because the entity no longer exists.",
                        Line = entry.SourceLine,
                        Column = entry.SourceColumn,
                    }
                );
                continue;
            }

            var normalized = new RenderEntity
            {
                Name = entry.Name,
                Kind = kind,
                SourceLine = entry.SourceLine,
                SourceColumn = entry.SourceColumn,
            };

            switch (kind)
            {
                case RenderEntityKind.Device:
                    normalized.Place = ValidatePlacement(
                        entry.Place,
                        devicesByName,
                        ports,
                        allowRelative: false,
                        messages,
                        entry
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
                        entry
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
                        entry
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

    /// <summary>
    /// Determine the RenderEntityKind for a given render entity name.
    /// </summary>
    /// <param name="name">The render entity identifier to classify.</param>
    /// <param name="devicesByName">Mapping of device identifiers to their declarations.</param>
    /// <param name="ports">Set of known port names.</param>
    /// <param name="nets">Set of known net names.</param>
    /// <returns>
    /// `RenderEntityKind.Device` if <paramref name="name"/> is a device id,
    /// `RenderEntityKind.Port` if it is a port name,
    /// `RenderEntityKind.Net` if it is a net name, or
    /// `RenderEntityKind.Unknown` if none match.
    /// </returns>
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

    /// <summary>
    /// Builds a set of all net identifiers referenced by the given circuit.
    /// </summary>
    /// <param name="circuit">The circuit to extract net identifiers from.</param>
    /// <returns>A set containing net IDs declared in circuit.Fill.Nets, supply names, ground names, port names, and net names from device bindings.</returns>
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

        foreach (var device in PrimitiveInstanceAdapter.EnumerateDevices(circuit.Fill))
        {
            foreach (var (_, netName) in device.Bindings)
            {
                nets.Add(netName);
            }
        }

        return nets;
    }

    private static IReadOnlyDictionary<string, DeviceDeclaration> BuildRenderableDevices(
        Circuit circuit
    )
    {
        var devices = new Dictionary<string, DeviceDeclaration>(StringComparer.Ordinal);
        foreach (var device in PrimitiveInstanceAdapter.EnumerateDevices(circuit.Fill))
        {
            devices[device.Id] = device;
        }

        return devices;
    }

    /// <summary>
    /// Validate a render placement and return it if it is valid; otherwise record a validation message and return null.
    /// </summary>
    /// <param name="place">The placement expression to validate.</param>
    /// <param name="devicesByName">Map of device identifiers to declarations used to validate device anchors.</param>
    /// <param name="ports">Set of valid port names used to validate port anchors.</param>
    /// <param name="allowRelative">Whether relative point expressions are permitted.</param>
    /// <param name="messages">List to append validation messages to when the placement is removed.</param>
    /// <param name="entityName">Name of the entity being validated; included in any appended messages.</param>
    /// <returns>The original <paramref name="place"/> if valid; otherwise <c>null</c>.</returns>
    private static RenderPlacement? ValidatePlacement(
        RenderPlacement? place,
        IReadOnlyDictionary<string, DeviceDeclaration> devicesByName,
        IReadOnlySet<string> ports,
        bool allowRelative,
        List<RenderValidationMessage> messages,
        RenderEntity entry
    )
    {
        if (place is null)
        {
            return null;
        }

        if (!TryValidatePoint(place.Point, devicesByName, ports, allowRelative))
        {
            messages.Add(
                new RenderValidationMessage
                {
                    Text =
                        $"Render place for '{entry.Name}' was removed due to an invalid anchor or point expression.",
                    Line = entry.SourceLine,
                    Column = entry.SourceColumn,
                }
            );
            return null;
        }

        return place;
    }

    /// <summary>
    /// Filter and validate a sequence of waypoint expressions, removing any points with invalid anchors.
    /// </summary>
    /// <param name="points">Waypoint expressions to validate.</param>
    /// <param name="devicesByName">Device declarations keyed by device id, used to validate reference anchors.</param>
    /// <param name="ports">Set of valid port names used to validate reference anchors.</param>
    /// <param name="messages">List to append validation messages describing removed waypoints.</param>
    /// <param name="entityName">Name of the render entity (used in validation messages).</param>
    /// <returns>A list containing only the waypoint expressions that passed validation; if <paramref name="points"/> was empty, the same empty list is returned.</returns>
    private static IReadOnlyList<RenderPointExpression> ValidateWaypoints(
        IReadOnlyList<RenderPointExpression> points,
        IReadOnlyDictionary<string, DeviceDeclaration> devicesByName,
        IReadOnlySet<string> ports,
        List<RenderValidationMessage> messages,
        RenderEntity entry
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
                    new RenderValidationMessage
                    {
                        Text =
                            $"A waypoint for net '{entry.Name}' was removed due to an invalid anchor.",
                        Line = entry.SourceLine,
                        Column = entry.SourceColumn,
                    }
                );
                continue;
            }

            valid.Add(point);
        }

        return valid;
    }

    /// <summary>
    /// Determines whether a render point expression is valid in the context of known devices/ports and relative-point policy.
    /// </summary>
    /// <param name="point">The render point expression to validate.</param>
    /// <param name="devicesByName">Mapping of device identifiers to their declarations used to validate device-based anchors.</param>
    /// <param name="ports">Set of known port names used to validate port-based anchors.</param>
    /// <param name="allowRelative">If <c>true</c>, relative points are considered valid; otherwise they are invalid.</param>
    /// <returns><c>true</c> if the point is valid given the available anchors and the relative-point policy, <c>false</c> otherwise.</returns>
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

    /// <summary>
    /// Determines whether a render anchor string refers to a valid canvas point, port, device, or device terminal.
    /// </summary>
    /// <param name="anchor">The anchor expression to validate (examples: "canvas origin", "canvas center", "portName", "deviceName", or "deviceName.terminal").</param>
    /// <param name="devicesByName">Mapping of device identifiers to their declarations used to resolve device names and types.</param>
    /// <param name="ports">Set of known port names used to validate port anchors.</param>
    /// <returns>`true` if the anchor is valid according to canvas keywords, known ports, known device names, or a device terminal allowed for the device's type; `false` otherwise.</returns>
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

        return GetAllowedTerminals(device.DeviceType)
            .Contains(parts[1], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get the allowed terminal labels for a given device type.
    /// </summary>
    /// <param name="deviceType">The device type name (comparison is case-insensitive).</param>
    /// <returns>An array of valid terminal identifiers for the device type (e.g., "G","D","S" for FETs; "P","N" for passive two-terminal devices), or an empty array if the type has no defined terminals.</returns>
    private static IReadOnlyList<string> GetAllowedTerminals(string deviceType)
    {
        return deviceType.ToLowerInvariant() switch
        {
            "nmos" or "nfet" or "pmos" or "pfet" => new[] { "G", "D", "S", "B" },
            "resistor" or "capacitor" or "inductor" => new[] { "P", "N" },
            "diode" => new[] { "A", "K" },
            _ => Array.Empty<string>(),
        };
    }

    /// <summary>
    /// Determine whether a render entity contains any meaningful rendering data.
    /// </summary>
    /// <param name="entity">The render entity to inspect.</param>
    /// <returns>`true` if the entity has a placement, orientation, Z-index, side, route, or one or more waypoints; `false` otherwise.</returns>
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
