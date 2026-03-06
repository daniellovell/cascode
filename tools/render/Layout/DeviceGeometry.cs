namespace Cascode.Render.Layout;

/// <summary>
/// Centralized geometry for device symbols and routing alignment.
/// </summary>
public static class DeviceGeometry
{
    public const int CellWidth = 45;
    public const int CellHeight = 50;
    public const int RailMargin = 15;
    public const int RoutingPitch = 10;

    public const double MosfetWidth = 17.0;
    public const double MosfetHeight = 26.0;
    public const double PassiveWidth = 26.0;
    public const double PassiveHeight = 9.0;

    public const double PortWidth = 13.5;
    public const double PortHeight = 5.0;
    public const double PortPinX = 13.0;
    public const double PortPinY = 2.5;

    public const double InstanceBlockWidth = 30.0;
    public const double InstanceBlockHeight = 30.0;

    public const double MosfetGateX = 0.5;
    public const double MosfetGateY = 12.5;
    public const double MosfetDrainX = 16.5;
    public const double MosfetDrainY = 0.5;
    public const double MosfetSourceY = 25.5;

    public sealed record MosfetPlacement(
        double X,
        double Y,
        int GateX,
        int GateY,
        int DrainX,
        int DrainY,
        int SourceX,
        int SourceY,
        int AxisX
    );

    public sealed record PassivePlacement(double X, double Y, int PX, int PY, int NX, int NY);

    public sealed record HorizontalPassivePlacement(
        double X,
        double Y,
        int PX,
        int PY,
        int NX,
        int NY
    );

    public sealed record InstanceBlockPlacement(
        double X,
        double Y,
        IReadOnlyDictionary<string, (int X, int Y)> Terminals
    );

    /// <summary>
    /// Computes placement for an instance block.
    /// Supply/ground bindings map to top/bottom center.
    /// Signal ports are distributed along the edge facing the connected devices:
    /// bottom edge for VDD-side blocks, top edge for GND-side blocks.
    /// </summary>
    public static InstanceBlockPlacement GetInstanceBlockPlacement(
        int row,
        int col,
        IReadOnlyList<string> signalPorts,
        IReadOnlySet<string> supplyNames,
        IReadOnlySet<string> groundNames,
        IReadOnlyDictionary<string, string> bindings
    )
    {
        var baseX = GetCellCenterX(col);
        var baseY = GetCellCenterY(row);
        var topLeftX = baseX - InstanceBlockWidth / 2.0;
        var topLeftY = baseY - InstanceBlockHeight / 2.0;

        var terminals = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal);
        var topY = RoundToInt(topLeftY);
        var bottomY = RoundToInt(topLeftY + InstanceBlockHeight);

        var isVddSide = bindings.Values.Any(supplyNames.Contains);

        var signalPortsToPlace = signalPorts.ToList();
        var edgeY = isVddSide ? bottomY : topY;
        var spacing = InstanceBlockWidth / (signalPortsToPlace.Count + 1);

        for (var i = 0; i < signalPortsToPlace.Count; i++)
        {
            var portX = SnapToRoutingGrid(topLeftX + spacing * (i + 1));
            terminals[signalPortsToPlace[i]] = (portX, edgeY);
        }

        return new InstanceBlockPlacement(topLeftX, topLeftY, terminals);
    }

    /// <summary>
    /// Terminal positions for any device type, used for terminal-aware wire length calculations.
    /// Coordinates are absolute pixel positions after placement.
    /// </summary>
    public sealed record TerminalPositions(
        double X,
        double Y,
        IReadOnlyDictionary<string, (int X, int Y)> Terminals
    );

    public static double GetCellCenterX(int col)
    {
        return col * CellWidth + CellWidth / 2.0;
    }

    public static double GetCellCenterY(int row)
    {
        return row * CellHeight + RailMargin + CellHeight / 2.0;
    }

    public static int SnapToRoutingGrid(double value)
    {
        return (int)Math.Round(value / RoutingPitch, MidpointRounding.AwayFromZero) * RoutingPitch;
    }

    public static int RoundToInt(double value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    public static MosfetPlacement GetMosfetPlacement(int row, int col, bool mirrorX)
    {
        var baseX = GetCellCenterX(col);
        var baseY = GetCellCenterY(row);

        var axisX = SnapToRoutingGrid(baseX + MosfetWidth / 2.0);
        var drainRelX = mirrorX ? MosfetWidth - MosfetDrainX : MosfetDrainX;
        var gateRelX = mirrorX ? MosfetWidth - MosfetGateX : MosfetGateX;

        var topLeftX = axisX - drainRelX;

        var drainY = RoundToInt(baseY - (MosfetHeight / 2.0 - MosfetDrainY));
        var topLeftY = drainY - MosfetDrainY;
        var sourceY = drainY + RoundToInt(MosfetSourceY - MosfetDrainY);

        var gateX = RoundToInt(topLeftX + gateRelX);
        var gateY = RoundToInt(topLeftY + MosfetGateY);

        return new MosfetPlacement(
            X: topLeftX,
            Y: topLeftY,
            GateX: gateX,
            GateY: gateY,
            DrainX: axisX,
            DrainY: drainY,
            SourceX: axisX,
            SourceY: sourceY,
            AxisX: axisX
        );
    }

    public static PassivePlacement GetPassivePlacement(int row, int col)
    {
        var baseX = GetCellCenterX(col);
        var baseY = GetCellCenterY(row);
        var axisX = SnapToRoutingGrid(baseX + MosfetWidth / 2.0);

        var topLeftX = axisX - PassiveHeight / 2.0;
        var topLeftY = baseY - PassiveWidth / 2.0;

        var pY = RoundToInt(baseY - PassiveWidth / 2.0);
        var nY = RoundToInt(baseY + PassiveWidth / 2.0);

        return new PassivePlacement(X: topLeftX, Y: topLeftY, PX: axisX, PY: pY, NX: axisX, NY: nY);
    }

    /// <summary>
    /// Computes placement for a horizontal passive (resistor/capacitor).
    /// The passive spans horizontally with P terminal toward the outer edge
    /// and N terminal toward the center (symmetry axis).
    /// </summary>
    /// <param name="row">Grid row</param>
    /// <param name="col">Grid column</param>
    /// <param name="columnCount">Total number of columns in the grid</param>
    /// <param name="isLeftOfAxis">True if this device is left of the symmetry axis</param>
    public static HorizontalPassivePlacement GetHorizontalPassivePlacement(
        int row,
        int col,
        int columnCount,
        bool isLeftOfAxis
    )
    {
        var baseX = GetCellCenterX(col);
        var baseY = GetCellCenterY(row);

        // For horizontal passive, width and height are swapped
        var topLeftX = baseX - PassiveWidth / 2.0;
        var topLeftY = baseY - PassiveHeight / 2.0;

        // P terminal is on the outer edge (away from center)
        // N terminal is toward the center
        int pX,
            nX;
        if (isLeftOfAxis)
        {
            // Left of axis: P on left, N on right (toward center)
            pX = RoundToInt(baseX - PassiveWidth / 2.0);
            nX = RoundToInt(baseX + PassiveWidth / 2.0);
        }
        else
        {
            // Right of axis: P on right, N on left (toward center)
            pX = RoundToInt(baseX + PassiveWidth / 2.0);
            nX = RoundToInt(baseX - PassiveWidth / 2.0);
        }

        var termY = RoundToInt(baseY);

        return new HorizontalPassivePlacement(
            X: topLeftX,
            Y: topLeftY,
            PX: pX,
            PY: termY,
            NX: nX,
            NY: termY
        );
    }

    /// <summary>
    /// Computes terminal positions for any device type at a given grid position.
    /// Used for terminal-aware wire length optimization in the SAT solver.
    /// </summary>
    /// <param name="deviceType">Type of device (nmos, pmos, resistor, capacitor)</param>
    /// <param name="row">Grid row</param>
    /// <param name="col">Grid column</param>
    /// <param name="mirrorX">Whether the device is mirrored horizontally</param>
    /// <param name="isHorizontalPassive">Whether this passive is oriented horizontally</param>
    /// <param name="columnCount">Total number of columns (for horizontal passive orientation)</param>
    /// <param name="symmetryAxis">Column index of the symmetry axis</param>
    public static TerminalPositions GetTerminalPositions(
        string deviceType,
        int row,
        int col,
        bool mirrorX,
        bool isHorizontalPassive,
        int columnCount,
        int symmetryAxis
    )
    {
        var type = deviceType.ToLowerInvariant();
        var terminals = new Dictionary<string, (int X, int Y)>();
        double x,
            y;

        if (type is "nmos" or "nfet" or "pmos" or "pfet")
        {
            var isPmos = type is "pmos" or "pfet";
            var p = GetMosfetPlacement(row, col, mirrorX);
            x = p.X;
            y = p.Y;

            terminals["G"] = (p.GateX, p.GateY);
            terminals["D"] = (p.DrainX, isPmos ? p.SourceY : p.DrainY);
            terminals["S"] = (p.SourceX, isPmos ? p.DrainY : p.SourceY);
        }
        else if (type is "resistor" or "capacitor" or "inductor")
        {
            if (isHorizontalPassive)
            {
                var isLeftOfAxis = col < symmetryAxis;
                var p = GetHorizontalPassivePlacement(row, col, columnCount, isLeftOfAxis);
                x = p.X;
                y = p.Y;
                terminals["P"] = (p.PX, p.PY);
                terminals["N"] = (p.NX, p.NY);
            }
            else
            {
                var p = GetPassivePlacement(row, col);
                x = p.X;
                y = p.Y;
                terminals["P"] = (p.PX, p.PY);
                terminals["N"] = (p.NX, p.NY);
            }
        }
        else if (type == "instance")
        {
            x = GetCellCenterX(col) - InstanceBlockWidth / 2.0;
            y = GetCellCenterY(row) - InstanceBlockHeight / 2.0;
        }
        else
        {
            x = GetCellCenterX(col);
            y = GetCellCenterY(row);
        }

        return new TerminalPositions(x, y, terminals);
    }

    /// <summary>
    /// Gets the terminal offset from cell center for SAT wire length computation.
    /// Returns (deltaColumn, deltaRow) in cell units, accounting for terminal position
    /// relative to the cell center.
    /// </summary>
    public static (double DeltaCol, double DeltaRow) GetTerminalOffset(
        string deviceType,
        string terminal,
        bool mirrorX,
        bool isHorizontalPassive,
        bool isLeftOfAxis
    )
    {
        var type = deviceType.ToLowerInvariant();

        if (type is "nmos" or "nfet" or "pmos" or "pfet")
        {
            var isPmos = type is "pmos" or "pfet";
            terminal = terminal.ToUpperInvariant();

            // Gate offset in column direction
            if (terminal == "G")
            {
                // Gate is offset horizontally from axis
                var gateOffsetRatio = (MosfetGateX - MosfetDrainX) / CellWidth;
                return (mirrorX ? -gateOffsetRatio : gateOffsetRatio, 0);
            }

            // Drain/Source offset in row direction
            if (terminal == "D")
            {
                var drainRowOffset = isPmos ? 0.25 : -0.25;
                return (0, drainRowOffset);
            }

            if (terminal == "S")
            {
                var sourceRowOffset = isPmos ? -0.25 : 0.25;
                return (0, sourceRowOffset);
            }
        }
        else if (type is "resistor" or "capacitor" or "inductor")
        {
            terminal = terminal.ToUpperInvariant();

            if (isHorizontalPassive)
            {
                // Horizontal passive: terminals offset in column direction
                var halfWidthRatio = (PassiveWidth / 2.0) / CellWidth;
                if (terminal == "P")
                {
                    // P terminal is on outer edge
                    return (isLeftOfAxis ? -halfWidthRatio : halfWidthRatio, 0);
                }

                if (terminal == "N")
                {
                    // N terminal is toward center
                    return (isLeftOfAxis ? halfWidthRatio : -halfWidthRatio, 0);
                }
            }
            else
            {
                // Vertical passive: terminals offset in row direction
                var halfHeightRatio = (PassiveWidth / 2.0) / CellHeight;
                if (terminal == "P")
                {
                    return (0, -halfHeightRatio);
                }

                if (terminal == "N")
                {
                    return (0, halfHeightRatio);
                }
            }
        }

        return (0, 0);
    }

    /// <summary>
    /// Compute the MOSFET symbol's top-left corner in render units given the
    /// terminal centroid and mirror state.
    ///
    /// The centroid is the average of Gate, Drain, and Source terminal positions.
    /// Because the MOSFET symbol is asymmetric, centering the bbox on the centroid
    /// places the left edge to the right of the Gate terminal. This method computes
    /// the correct origin so that the bbox exactly covers the symbol extent.
    /// </summary>
    public static (double X, double Y) GetMosfetBboxOrigin(
        double centroidX,
        double centroidY,
        bool mirrorX
    )
    {
        // Terminal X positions relative to the symbol's top-left:
        //   unmirrored: Gate=0.5, Drain=16.5, Source=16.5
        //   mirrored:   Gate=16.5, Drain=0.5, Source=0.5
        var gateRelX = mirrorX ? MosfetWidth - MosfetGateX : MosfetGateX;
        var drainRelX = mirrorX ? MosfetWidth - MosfetDrainX : MosfetDrainX;
        var dx = (gateRelX + 2 * drainRelX) / (3.0 * RoutingPitch);

        // Terminal Y positions relative to the symbol's top-left are the same
        // regardless of mirrorX: Drain=0.5, Gate=12.5, Source=25.5
        var dy = (MosfetDrainY + MosfetGateY + MosfetSourceY) / (3.0 * RoutingPitch);

        return (centroidX - dx, centroidY - dy);
    }
}
