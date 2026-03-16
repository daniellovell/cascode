using System.Text.Json;
using Cascode.Language;

namespace Cascode.Native;

internal static class SchematicOperationApplier
{
    /// <summary>
    /// Apply a schematic operation described by the JSON `operation` to the given document `state` by dispatching on the operation's "type" field.
    /// </summary>
    /// <param name="state">Current document state to update.</param>
    /// <param name="operation">JSON object describing the operation; must include a required string property "type".</param>
    /// <param name="changed">Set that will be populated with identifiers of entities modified by the operation.</param>
    /// <exception cref="ApiException">Thrown when the operation "type" is not recognized.</exception>
    public static void Apply(DocumentState state, JsonElement operation, HashSet<string> changed)
    {
        var opType = operation.RequireString("type");
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
            case "setPortSide":
                ApplySetPortSide(state, operation, changed);
                return;
            case "setNetSegments":
                ApplySetNetSegments(state, operation, changed);
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
            case "addSupply":
                ApplyAddRail(state, operation, changed, supply: true);
                return;
            case "removeSupply":
                ApplyRemoveRail(state, operation, changed, supply: true);
                return;
            case "addGround":
                ApplyAddRail(state, operation, changed, supply: false);
                return;
            case "removeGround":
                ApplyRemoveRail(state, operation, changed, supply: false);
                return;
            case "deleteDevice":
                ApplyDeleteDevice(state, operation, changed);
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

    /// <summary>
    /// Apply a move operation by setting the device's render placement to the given coordinates.
    /// </summary>
    /// <param name="state">Current document state used to locate the circuit and resolve anchors.</param>
    /// <param name="op">JSON operation object; must contain string "deviceId" and integers "x" and "y".</param>
    /// <param name="changed">Set of identifiers to record the deviceId that was modified.</param>
    private static void ApplyMoveDevice(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var deviceId = op.RequireString("deviceId");
        var x = op.RequireInt("x");
        var y = op.RequireInt("y");
        var circuit = FindCircuit(state);

        var entry = UpsertRenderEntity(circuit, deviceId, RenderEntityKind.Device);
        entry.Place = new RenderPlacement
        {
            Point = CanonicalizePoint(state, circuit, deviceId, op, x, y),
            Strength = RenderConstraintStrength.Hard,
        };
        changed.Add(deviceId);
    }

    /// <summary>
    /// Remove a device declaration and prune any connections and render entities that reference it.
    /// </summary>
    /// <param name="state">Current document state containing the target circuit.</param>
    /// <param name="op">JSON operation object; must contain a string "deviceId".</param>
    /// <param name="changed">Set of identifiers to record the deleted deviceId.</param>
    private static void ApplyDeleteDevice(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var deviceId = op.RequireString("deviceId");
        var circuit = FindCircuit(state);
        var fill =
            circuit.Fill
            ?? throw new ApiException("CASAPI-INVALID-REQUEST", "Circuit has no fill block.");

        var index = fill.Devices.FindIndex(d => d.Id == deviceId);
        if (index < 0)
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", $"Unknown device '{deviceId}'.");
        }

        fill.Devices.RemoveAt(index);

        var prefix = deviceId + ".";
        fill.Connections.RemoveAll(conn =>
            conn.From.Equals(deviceId, StringComparison.Ordinal)
            || conn.To.Equals(deviceId, StringComparison.Ordinal)
            || conn.From.StartsWith(prefix, StringComparison.Ordinal)
            || conn.To.StartsWith(prefix, StringComparison.Ordinal)
        );

        circuit.Render?.Entities.RemoveAll(entity =>
            entity.Name.Equals(deviceId, StringComparison.Ordinal)
        );

        changed.Add(deviceId);
    }

    /// <summary>
    /// Adds a supply or ground declaration to the active circuit.
    /// </summary>
    private static void ApplyAddRail(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed,
        bool supply
    )
    {
        var name = op.RequireString("name");
        var circuit = FindCircuit(state);
        RequireRailNameAvailable(circuit, name);
        if (supply)
        {
            circuit.Supplies.Add(name);
        }
        else
        {
            circuit.Grounds.Add(name);
        }
        changed.Add(name);
    }

    /// <summary>
    /// Removes a supply or ground declaration and prunes any connections that reference it.
    /// </summary>
    private static void ApplyRemoveRail(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed,
        bool supply
    )
    {
        var name = op.RequireString("name");
        var circuit = FindCircuit(state);
        var rails = supply ? circuit.Supplies : circuit.Grounds;
        if (!rails.Remove(name))
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Unknown {(supply ? "supply" : "ground")} '{name}'."
            );
        }

        if (circuit.Fill is not null)
        {
            circuit.Fill.Connections.RemoveAll(conn =>
                conn.From.Equals(name, StringComparison.Ordinal)
                || conn.To.Equals(name, StringComparison.Ordinal)
            );
        }

        circuit.Render?.Entities.RemoveAll(entity =>
            entity.Name.Equals(name, StringComparison.Ordinal)
        );
        changed.Add(name);
    }

    /// <summary>
    /// Apply a rotation to the specified device's render orientation and record it as changed.
    /// </summary>
    /// <param name="state">The current document state containing circuits and render data.</param>
    /// <param name="op">A JSON operation object that must include "deviceId" and "angle".</param>
    /// <param name="changed">A set that will receive the deviceId of the modified device.</param>
    private static void ApplyRotateDevice(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var deviceId = op.RequireString("deviceId");
        var angle = op.RequireInt("angle");

        var entry = UpsertRenderEntity(FindCircuit(state), deviceId, RenderEntityKind.Device);
        entry.Orientation = new RenderOrientation
        {
            Rotate = angle,
            MirrorX = entry.Orientation?.MirrorX ?? false,
        };
        changed.Add(deviceId);
    }

    /// <summary>
    /// Toggle the horizontal (X) mirror flag for a device's render orientation, preserving its rotation and ensuring a render entry exists.
    /// </summary>
    /// <param name="state">The current document state to modify.</param>
    /// <param name="op">The JSON operation object containing a required "deviceId" field.</param>
    /// <param name="changed">A set that will receive the deviceId to indicate the device was modified.</param>
    private static void ApplyMirrorDevice(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var deviceId = op.RequireString("deviceId");
        var entry = UpsertRenderEntity(FindCircuit(state), deviceId, RenderEntityKind.Device);
        var current = entry.Orientation;
        entry.Orientation = new RenderOrientation
        {
            Rotate = current?.Rotate ?? 0,
            MirrorX = !(current?.MirrorX ?? false),
        };
        changed.Add(deviceId);
    }

    /// <summary>
    /// Updates the render placement of the specified port to the given coordinates and records the port as changed.
    /// </summary>
    private static void ApplyMovePort(DocumentState state, JsonElement op, HashSet<string> changed)
    {
        var portName = op.RequireString("port");
        var x = op.RequireInt("x");
        var y = op.RequireInt("y");
        var circuit = FindCircuit(state);

        var entry = UpsertRenderEntity(circuit, portName, RenderEntityKind.Port);
        entry.Place = new RenderPlacement
        {
            Point = CanonicalizePoint(state, circuit, portName, op, x, y),
            Strength = RenderConstraintStrength.Hard,
        };
        changed.Add(portName);
    }

    /// <summary>
    /// Updates the explicit side declaration for the specified port.
    /// </summary>
    private static void ApplySetPortSide(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var portName = op.RequireString("port");
        var sideValue = op.RequireString("side");
        var side = ParsePortSide(sideValue);
        var circuit = FindCircuit(state);

        if (circuit.Render?.Mode == RenderLayoutMode.Manual && side == RenderPortSide.Auto)
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                "Manual render requires an explicit port side (left/right/top/bottom)."
            );
        }

        var entry = UpsertRenderEntity(circuit, portName, RenderEntityKind.Port);
        entry.Side = side;
        changed.Add(portName);
    }

    /// <summary>
    /// Replace the explicit render segments for the specified net and set the route strength to Hard.
    /// </summary>
    /// <param name="state">Current document state containing the target circuit.</param>
    /// <param name="op">Operation JSON object. Must contain a string property "net" and may contain "segments" — an array of objects with "from"/"to" point payloads.</param>
    /// <param name="changed">Set that will receive the net name to indicate the net's render state was modified.</param>
    private static void ApplySetNetSegments(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var netName = op.RequireString("net");
        var circuit = FindCircuit(state);
        var entry = UpsertRenderEntity(circuit, netName, RenderEntityKind.Net);

        entry.Route = entry.Route is null
            ? new RenderRoute
            {
                Mode = RenderRouteMode.Ortho,
                Strength = RenderConstraintStrength.Hard,
            }
            : new RenderRoute { Mode = entry.Route.Mode, Strength = RenderConstraintStrength.Hard };
        entry.Segments.Clear();

        if (!op.TryGetProperty("segments", out var segments))
        {
            entry.Route = null;
            changed.Add(netName);
            return;
        }

        foreach (var segment in segments.EnumerateArray())
        {
            var from = segment.RequireProperty("from");
            var to = segment.RequireProperty("to");
            entry.Segments.Add(
                new RenderSegment
                {
                    From = CanonicalizePoint(
                        state,
                        circuit,
                        netName,
                        from,
                        from.RequireInt("x"),
                        from.RequireInt("y")
                    ),
                    To = CanonicalizePoint(
                        state,
                        circuit,
                        netName,
                        to,
                        to.RequireInt("x"),
                        to.RequireInt("y")
                    ),
                }
            );
        }

        if (entry.Segments.Count == 0)
        {
            entry.Route = null;
        }

        changed.Add(netName);
    }

    /// <summary>
    /// Apply the given pinning strength to the render entity named in the operation's "entity" field.
    /// </summary>
    /// <param name="state">Current document state used to locate the target circuit.</param>
    /// <param name="op">JSON operation object; must contain an "entity" string identifying the render entity.</param>
    /// <param name="changed">Set that will receive the entity name to indicate it was modified.</param>
    /// <param name="strength">The render constraint strength to apply. If the entity has a Place, its Strength is set to this value; if it has a Route, its Strength is set to this value.</param>
    private static void ApplyPinEntity(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed,
        RenderConstraintStrength strength
    )
    {
        var name = op.RequireString("entity");
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

    /// <summary>
    /// Parse the "strength" field from the operation and apply that placement/route strength to the target entity.
    /// </summary>
    /// <param name="state">Current document state containing the circuit and render data.</param>
    /// <param name="op">JSON operation object that must include a "strength" string and identify the target entity.</param>
    /// <param name="changed">Set to record identifiers of entities that were modified.</param>
    /// <exception cref="ApiException">Thrown when the "strength" value is not one of "hard", "soft", or "hint".</exception>
    private static void ApplySetStrength(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var value = op.RequireString("strength").ToLowerInvariant();
        var strength = value switch
        {
            "hard" => RenderConstraintStrength.Hard,
            "soft" => RenderConstraintStrength.Soft,
            "hint" => RenderConstraintStrength.Hint,
            _ => throw new ApiException("CASAPI-INVALID-REQUEST", $"Invalid strength '{value}'."),
        };

        ApplyPinEntity(state, op, changed, strength);
    }

    /// <summary>
    /// Set or update a size parameter for the specified device in the current circuit and record that the device changed.
    /// </summary>
    /// <param name="op">Operation object that must contain string fields "deviceId", "param", and "value".</param>
    /// <exception cref="ApiException">Thrown when the specified deviceId does not exist in the circuit.</exception>
    private static void ApplySetDeviceParam(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed
    )
    {
        var deviceId = op.RequireString("deviceId");
        var param = op.RequireString("param");
        var value = op.RequireString("value");

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

    /// <summary>
    /// Adds or removes a connection between two terminals specified in the operation.
    /// </summary>
    /// <param name="op">A JSON operation that must contain string fields "from" and "to" identifying the endpoints.</param>
    /// <param name="changed">A set that will be updated with the two endpoint identifiers after the change.</param>
    /// <param name="disconnect">If true, removes any matching connection (in either direction); if false, adds the connection if it does not already exist.</param>
    /// <exception cref="ApiException">
    /// Thrown if the operation is missing required fields, the named circuit cannot be found, or the circuit has no Fill block.
    /// </exception>
    private static void ApplyConnectionChange(
        DocumentState state,
        JsonElement op,
        HashSet<string> changed,
        bool disconnect
    )
    {
        var from = op.RequireString("from");
        var to = op.RequireString("to");
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
            var exists = fill.Connections.Any(conn =>
                (conn.From == from && conn.To == to) || (conn.From == to && conn.To == from)
            );
            if (!exists)
            {
                fill.Connections.Add(new ConnectionStatement { From = from, To = to });
            }
        }

        changed.Add(from);
        changed.Add(to);
    }

    /// <summary>
    /// Locate the circuit in the state's document that matches the current CircuitName.
    /// </summary>
    /// <param name="state">The document state containing Document, DocumentId, and CircuitName.</param>
    /// <returns>The matching <see cref="Circuit"/>.</returns>
    /// <exception cref="ApiException">Thrown when no circuit with <c>state.CircuitName</c> exists in the document.</exception>
    private static Circuit FindCircuit(DocumentState state)
    {
        var circuit = state.Document.Circuits.FirstOrDefault(c => c.Name == state.CircuitName);
        if (circuit is null)
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Circuit '{state.CircuitName}' was not found in document '{state.DocumentId}'."
            );
        }

        return circuit;
    }

    /// <summary>
    /// Ensures a rail name is not already in use by another supply/ground declaration.
    /// </summary>
    private static void RequireRailNameAvailable(Circuit circuit, string name)
    {
        if (
            circuit.Supplies.Any(value => value.Equals(name, StringComparison.Ordinal))
            || circuit.Grounds.Any(value => value.Equals(name, StringComparison.Ordinal))
        )
        {
            throw new ApiException("CASAPI-INVALID-REQUEST", $"Rail '{name}' already exists.");
        }
    }

    /// <summary>
    /// Ensure the circuit has a RenderBlock and return a RenderEntity with the given name, creating one if necessary.
    /// </summary>
    /// <param name="circuit">The circuit to inspect or modify.</param>
    /// <param name="name">The name of the render entity to retrieve or create.</param>
    /// <param name="kind">The desired kind for the entity; if an existing entity is found and <c>kind</c> is not <c>RenderEntityKind.Unknown</c>, the entity's Kind is updated.</param>
    /// <returns>The existing or newly created <see cref="RenderEntity"/> with the specified name.</returns>
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

    /// <summary>
    /// Determine a canonical render point for a subject, resolving an explicit anchor or the nearest semantic anchor when available and falling back to absolute coordinates.
    /// </summary>
    /// <param name="state">Current document state (used to compute anchor maps).</param>
    /// <param name="circuit">Circuit containing anchors and render context.</param>
    /// <param name="subjectName">Name of the subject for which the point is being computed (used to avoid self anchors).</param>
    /// <param name="payload">JSON payload that may include an optional "anchor" string specifying an explicit anchor name.</param>
    /// <param name="x">Absolute X coordinate supplied by the operation.</param>
    /// <param name="y">Absolute Y coordinate supplied by the operation.</param>
    /// <returns>
    /// A RenderRefPoint when an explicit anchor is provided or when a nearest semantic anchor is found (the point is expressed as an offset from that anchor); otherwise a RenderAbsPoint with the given absolute coordinates.
    /// </returns>
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

        var explicitAnchor = payload.TryGetString("anchor");
        if (TryBuildRefPoint(anchors, explicitAnchor, x, y, out var explicitPoint))
        {
            return explicitPoint;
        }

        var nearest = FindNearestAnchor(anchors, subjectName, x, y);
        return nearest is null
            ? new RenderAbsPoint(x, y)
            : new RenderRefPoint(
                nearest,
                (int)Math.Round(x - anchors[nearest].X),
                (int)Math.Round(y - anchors[nearest].Y)
            );
    }

    /// <summary>
    /// Attempt to compute a mapping of anchor names to render points for the given circuit using the schematic constraint resolver.
    /// </summary>
    /// <param name="state">Current document state used as context for constraint computation.</param>
    /// <param name="circuit">Circuit whose anchors and placements will be analyzed.</param>
    /// <returns>A dictionary mapping anchor identifiers to their computed PointValue when successful; otherwise <c>null</c> if constraint computation fails.</returns>
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

    /// <summary>
    /// Builds a reference render point relative to a named anchor when that anchor exists.
    /// </summary>
    /// <param name="anchors">Map of anchor names to their coordinates.</param>
    /// <param name="anchorName">Name of the anchor to reference; may be null or empty.</param>
    /// <param name="x">Absolute x coordinate to convert relative to the anchor.</param>
    /// <param name="y">Absolute y coordinate to convert relative to the anchor.</param>
    /// <param name="point">When the method returns `true`, contains a RenderRefPoint whose offset is (x - anchor.X, y - anchor.Y); otherwise unspecified.</param>
    /// <returns>`true` if <paramref name="anchorName"/> names an entry in <paramref name="anchors"/> and <paramref name="point"/> was produced, `false` otherwise.</returns>
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
            point = new RenderRefPoint(
                anchorName,
                (int)Math.Round(x - anchor.X),
                (int)Math.Round(y - anchor.Y)
            );
            return true;
        }

        point = default!;
        return false;
    }

    /// <summary>
    /// Selects the nearest semantic anchor (excluding anchors that refer to the subject) to the given point using Manhattan distance, with a maximum snap distance of 2.
    /// </summary>
    /// <param name="anchors">Mapping of anchor names to their positions.</param>
    /// <param name="subjectName">Name of the entity for which anchors referring to itself should be ignored.</param>
    /// <param name="x">X coordinate of the query point.</param>
    /// <param name="y">Y coordinate of the query point.</param>
    /// <returns>The name of the closest anchor within a Manhattan distance of 2, or null if none is found.</returns>
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

    /// <summary>
    /// Determines whether an anchor name represents a semantic anchor rather than a canvas anchor.
    /// </summary>
    /// <param name="anchor">The anchor name to test.</param>
    /// <returns>`true` if the anchor does not start with "canvas ", `false` otherwise.</returns>
    private static bool IsSemanticAnchor(string anchor)
    {
        return !anchor.StartsWith("canvas ", StringComparison.Ordinal);
    }

    private static RenderPortSide ParsePortSide(string sideValue)
    {
        return sideValue.ToLowerInvariant() switch
        {
            "left" => RenderPortSide.Left,
            "right" => RenderPortSide.Right,
            "top" => RenderPortSide.Top,
            "bottom" => RenderPortSide.Bottom,
            "auto" => RenderPortSide.Auto,
            _ => throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                $"Invalid port side '{sideValue}'."
            ),
        };
    }

    /// <summary>
    /// Determines whether an anchor name refers to the subject itself.
    /// </summary>
    /// <param name="subjectName">The subject entity's name.</param>
    /// <param name="anchor">The anchor name to test.</param>
    /// <returns>`true` if <c>anchor</c> equals <c>subjectName</c> or starts with "<c>subjectName.</c>", `false` otherwise.</returns>
    private static bool IsSelfAnchor(string subjectName, string anchor)
    {
        return anchor == subjectName
            || anchor.StartsWith($"{subjectName}.", StringComparison.Ordinal);
    }

    /// <summary>
    /// Split an anchor name into its entity and terminal components.
    /// </summary>
    /// <param name="anchor">Anchor identifier, optionally containing a '.' that separates entity and terminal.</param>
    /// <returns>A tuple where <c>Entity</c> is the substring before the first '.', and <c>Terminal</c> is the substring after the first '.' or an empty string if none.</returns>
    private static (string Entity, string Terminal) BuildAnchorSortKey(string anchor)
    {
        var dot = anchor.IndexOf('.');
        return dot < 0 ? (anchor, string.Empty) : (anchor[..dot], anchor[(dot + 1)..]);
    }
}
