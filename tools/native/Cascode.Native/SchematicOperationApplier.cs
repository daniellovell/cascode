using System.Text.Json;
using Cascode.Language;

namespace Cascode.Native;

internal static class SchematicOperationApplier
{
    public static void Apply(DocumentState state, JsonElement operation, HashSet<string> changed)
    {
        var opType = RequireString(operation, "type");
        switch (opType)
        {
            case "moveDevice":
                ApplyMoveDevice(state, operation, changed);
                return;
            case "rotateDevice":
                ApplyRotateDevice(state, operation, changed);
                return;
            case "mirrorDevice":
                ApplyMirrorDevice(state, operation, changed);
                return;
            case "movePort":
                ApplyMovePort(state, operation, changed);
                return;
            case "setNetRouteWaypoints":
                ApplySetNetWaypoints(state, operation, changed);
                return;
            case "clearNetRouteWaypoints":
                ApplyClearNetWaypoints(state, operation, changed);
                return;
            case "pinEntity":
                ApplyPinEntity(state, operation, changed, RenderConstraintStrength.Hard);
                return;
            case "unpinEntity":
                ApplyPinEntity(state, operation, changed, RenderConstraintStrength.Soft);
                return;
            case "setConstraintStrength":
                ApplySetStrength(state, operation, changed);
                return;
            case "setDeviceParam":
                ApplySetDeviceParam(state, operation, changed);
                return;
            case "connectTerminals":
                ApplyConnectionChange(state, operation, changed, disconnect: false);
                return;
            case "disconnectTerminals":
                ApplyConnectionChange(state, operation, changed, disconnect: true);
                return;
            default:
                throw new ApiException(
                    "CASAPI-INVALID-REQUEST",
                    $"Unsupported operation '{opType}'."
                );
        }
    }

    private static void ApplyMoveDevice(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var deviceId = RequireString(op, "deviceId");
        var x = RequireInt(op, "x");
        var y = RequireInt(op, "y");
        var circuit = FindCircuit(state);

        var entry = UpsertRenderEntity(circuit, deviceId, RenderEntityKind.Device);
        entry.Place = new RenderPlacement
        {
            Point = CanonicalizePoint(state, circuit, deviceId, op, x, y),
            Strength = RenderConstraintStrength.Hard,
        };
        changed.Add(deviceId);
    }

    private static void ApplyRotateDevice(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var deviceId = RequireString(op, "deviceId");
        var angle = RequireInt(op, "angle");

        var entry = UpsertRenderEntity(FindCircuit(state), deviceId, RenderEntityKind.Device);
        entry.Orientation = new RenderOrientation
        {
            Rotate = angle,
            MirrorX = entry.Orientation?.MirrorX ?? false,
        };
        changed.Add(deviceId);
    }

    private static void ApplyMirrorDevice(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var deviceId = RequireString(op, "deviceId");
        var entry = UpsertRenderEntity(FindCircuit(state), deviceId, RenderEntityKind.Device);
        var current = entry.Orientation;
        entry.Orientation = new RenderOrientation
        {
            Rotate = current?.Rotate ?? 0,
            MirrorX = !(current?.MirrorX ?? false),
        };
        changed.Add(deviceId);
    }

    private static void ApplyMovePort(DocumentState state, JsonElement op, HashSet<string> changed)
    {
        var portName = RequireString(op, "port");
        var x = RequireInt(op, "x");
        var y = RequireInt(op, "y");
        var circuit = FindCircuit(state);

        var entry = UpsertRenderEntity(circuit, portName, RenderEntityKind.Port);
        entry.Place = new RenderPlacement
        {
            Point = CanonicalizePoint(state, circuit, portName, op, x, y),
            Strength = RenderConstraintStrength.Hard,
        };
        changed.Add(portName);
    }

    private static void ApplySetNetWaypoints(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var netName = RequireString(op, "net");
        var circuit = FindCircuit(state);
        var entry = UpsertRenderEntity(circuit, netName, RenderEntityKind.Net);

        entry.Route = entry.Route is null
            ? new RenderRoute
            {
                Mode = RenderRouteMode.Ortho,
                Strength = RenderConstraintStrength.Hard,
            }
            : new RenderRoute { Mode = entry.Route.Mode, Strength = RenderConstraintStrength.Hard };
        entry.Waypoints.Clear();

        if (!op.TryGetProperty("waypoints", out var waypoints))
        {
            changed.Add(netName);
            return;
        }

        foreach (var point in waypoints.EnumerateArray())
        {
            var x = RequireInt(point, "x");
            var y = RequireInt(point, "y");
            entry.Waypoints.Add(CanonicalizePoint(state, circuit, netName, point, x, y));
        }

        changed.Add(netName);
    }

    private static void ApplyClearNetWaypoints(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var netName = RequireString(op, "net");
        var entry = UpsertRenderEntity(FindCircuit(state), netName, RenderEntityKind.Net);
        entry.Waypoints.Clear();
        entry.Route = null;
        changed.Add(netName);
    }

    private static void ApplyPinEntity(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed,
        RenderConstraintStrength strength
    )
    {
        var name = RequireString(op, "entity");
        var entry = UpsertRenderEntity(FindCircuit(state), name, RenderEntityKind.Unknown);

        if (entry.Place is not null)
        {
            entry.Place = new RenderPlacement { Point = entry.Place.Point, Strength = strength };
        }

        if (entry.Route is not null)
        {
            entry.Route = new RenderRoute { Mode = entry.Route.Mode, Strength = strength };
        }

        changed.Add(name);
    }

    private static void ApplySetStrength(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var value = RequireString(op, "strength").ToLowerInvariant();
        var strength = value switch
        {
            "hard" => RenderConstraintStrength.Hard,
            "soft" => RenderConstraintStrength.Soft,
            "hint" => RenderConstraintStrength.Hint,
            _ => throw new ApiException("CASAPI-INVALID-REQUEST", $"Invalid strength '{value}'."),
        };

        ApplyPinEntity(state, op, changed, strength);
    }

    private static void ApplySetDeviceParam(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var deviceId = RequireString(op, "deviceId");
        var param = RequireString(op, "param");
        var value = RequireString(op, "value");

        var fill = FindCircuit(state).Fill;
        var index = fill?.Devices.FindIndex(d => d.Id == deviceId) ?? -1;
        if (index < 0)
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", $"Unknown device '{deviceId}'.");
        }

        var device = fill!.Devices[index];
        var sizeEntries = device.Size is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(device.Size.Entries, StringComparer.Ordinal);
        sizeEntries[param] = value;

        fill.Devices[index] = new DeviceDeclaration
        {
            DeviceType = device.DeviceType,
            Id = device.Id,
            Primitive = device.Primitive,
            SizeName = device.SizeName,
            Size = new SizePack { Entries = sizeEntries },
            Bindings = new Dictionary<string, string>(device.Bindings, StringComparer.Ordinal),
        };
        changed.Add(deviceId);
    }

    private static void ApplyConnectionChange(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed,
        bool disconnect
    )
    {
        var from = RequireString(op, "from");
        var to = RequireString(op, "to");
        var fill =
            FindCircuit(state).Fill
            ?? throw new ApiException("CASAPI-INVALID-REQUEST", "Circuit has no fill block.");

        if (disconnect)
        {
            fill.Connections.RemoveAll(conn =>
                (conn.From == from && conn.To == to) || (conn.From == to && conn.To == from)
            );
        }
        else
        {
            fill.Connections.Add(new ConnectionStatement { From = from, To = to });
        }

        changed.Add(from);
        changed.Add(to);
    }

    private static Circuit FindCircuit(DocumentState state)
    {
        return state.Document.Circuits.First(c => c.Name == state.CircuitName);
    }

    private static RenderEntity UpsertRenderEntity(
        Circuit circuit,
        string name,
        RenderEntityKind kind
    )
    {
        circuit.Render ??= new RenderBlock();
        var existing = circuit.Render.Entities.FirstOrDefault(entity => entity.Name == name);
        if (existing is not null)
        {
            if (kind != RenderEntityKind.Unknown)
            {
                existing.Kind = kind;
            }

            return existing;
        }

        var created = new RenderEntity { Name = name, Kind = kind };
        circuit.Render.Entities.Add(created);
        return created;
    }

    private static RenderPointExpression CanonicalizePoint(
        DocumentState state,
        Circuit circuit,
        string subjectName,
        JsonElement payload,
        int x,
        int y
    )
    {
        var anchors = TryBuildAnchorMap(state, circuit);
        if (anchors is null)
        {
            return new RenderAbsPoint(x, y);
        }

        var explicitAnchor = TryGetString(payload, "anchor");
        if (TryBuildRefPoint(anchors, explicitAnchor, x, y, out var explicitPoint))
        {
            return explicitPoint;
        }

        var nearest = FindNearestAnchor(anchors, subjectName, x, y);
        return nearest is null
            ? new RenderAbsPoint(x, y)
            : new RenderRefPoint(nearest, x - anchors[nearest].X, y - anchors[nearest].Y);
    }

    private static IReadOnlyDictionary<string, PointValue>? TryBuildAnchorMap(
        DocumentState state,
        Circuit circuit
    )
    {
        try
        {
            var computation = SchematicConstraintResolver.ComputeRender(
                state.Document,
                circuit,
                circuit.Render,
                allowRelaxation: false
            );
            return SchematicConstraintResolver.BuildAnchorMap(
                circuit,
                computation.Placement,
                computation.Routing
            );
        }
        catch (ApiException)
        {
            return null;
        }
    }

    private static bool TryBuildRefPoint(
        IReadOnlyDictionary<string, PointValue> anchors,
        string? anchorName,
        int x,
        int y,
        out RenderRefPoint point
    )
    {
        if (
            !string.IsNullOrWhiteSpace(anchorName)
            && anchors.TryGetValue(anchorName, out var anchor)
        )
        {
            point = new RenderRefPoint(anchorName, x - anchor.X, y - anchor.Y);
            return true;
        }

        point = default!;
        return false;
    }

    private static string? FindNearestAnchor(
        IReadOnlyDictionary<string, PointValue> anchors,
        string subjectName,
        int x,
        int y
    )
    {
        const int MaxSnapDistance = 2;
        var candidates = anchors
            .Where(entry => IsSemanticAnchor(entry.Key) && !IsSelfAnchor(subjectName, entry.Key))
            .Select(entry => new
            {
                Anchor = entry.Key,
                Distance = Math.Abs(x - entry.Value.X) + Math.Abs(y - entry.Value.Y),
                Key = BuildAnchorSortKey(entry.Key),
            })
            .Where(candidate => candidate.Distance <= MaxSnapDistance)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Key.Entity, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Key.Terminal, StringComparer.Ordinal)
            .FirstOrDefault();
        return candidates?.Anchor;
    }

    private static bool IsSemanticAnchor(string anchor)
    {
        return !anchor.StartsWith("canvas ", StringComparison.Ordinal);
    }

    private static bool IsSelfAnchor(string subjectName, string anchor)
    {
        return anchor == subjectName
            || anchor.StartsWith($"{subjectName}.", StringComparison.Ordinal);
    }

    private static (string Entity, string Terminal) BuildAnchorSortKey(string anchor)
    {
        var dot = anchor.IndexOf('.');
        return dot < 0 ? (anchor, string.Empty) : (anchor[..dot], anchor[(dot + 1)..]);
    }

    private static string RequireString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String)
        {
            return child.GetString()!;
        }

        throw new ApiException("CASAPI-INVALID-REQUEST", $"Missing string field '{name}'.");
    }

    private static int RequireInt(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var child) && child.TryGetInt32(out var value))
        {
            return value;
        }

        throw new ApiException("CASAPI-INVALID-REQUEST", $"Missing integer field '{name}'.");
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        return
            element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String
            ? child.GetString()
            : null;
    }
}
