# Placement Guidelines

Status: Draft
Authors: Titan Yuan
Created: 2026-03-05
Last Updated: 2026-03-06

---

## Abstract

This document specifies the placement stage of the circuit schematic renderer. It enumerates the hard and soft constraints, the objective function, and the post-optimization cleanup procedure. A companion routing specification will follow in a separate revision.

In this document, any active device (NMOS, PMOS) or passive component (resistor, capacitor, inductor) is called a *component*. Each component occupies a rectangular cell with four edges (north, south, east, west) and is aware of which terminals sit on which edges. Terminal-to-edge assignments are defined in a default orientation and then transformed by the solved orientation state.

Implementation convention: all components use solved rotation, MirrorX, and MirrorY for terminal-edge mapping, except MOS devices, which ignore MirrorY for terminal geometry and rendering. This keeps MOS terminal behavior aligned with the symbol model used by routing and SVG emission.

Current implementation note: non-primary terminals are not treated as edge terminals for placement. This includes MOS body/shield terminals (B, BULK, BODY, SH, SHIELD), capacitor shields and bulks, resistor bulks, and inductor taps. These terminals are excluded from rail-terminal proximity objectives, and when they appear on non-rail nets they use the default zero terminal offset (device-center approximation) in wire-length evaluation.

---

## Placement Problem

Placing the circuit components onto a 2D grid can be formulated as an optimization problem suitable for a SAT solver. The grid is indexed by integer (row, column) coordinates, where the row coordinate increases from top to bottom and the column coordinate increases from left to right.

### Decision Variables

For each component the solver determines:

1. A (row, column) position on the grid.
2. An orientation drawn from the full set of sixteen combinations: four rotations (0, 90, 180, 270 degrees) combined with two mirror-X states (on, off) and two mirror-Y states (on, off). All sixteen are exposed to the solver; geometrically equivalent orientations for a given component shape are naturally pruned during search because they produce identical objective values and constraint satisfaction.

Components may have different cell widths and heights; the formulation does not assume uniform cell dimensions.

### Objective

The objective minimizes a weighted sum of wire-length and routing-complexity terms.

For each net, terminal coordinates are computed from placed cell geometry and solved orientation. The base wire-length term is the half-perimeter span:

- `spanX = max(terminalX) - min(terminalX)`
- `spanY = max(terminalY) - min(terminalY)`
- `netLength = spanX + spanY`

Signal nets use full weight for `netLength`. Rail nets (VDD/GND) are included with reduced weight:

- `RailWireLengthWeight = 0.25` (implemented as integer ratio `1/4`)

This rail weighting applies only to wire-length. Corner and U-turn penalties are not applied to rail nets.

For non-rail nets, additional orientation-aware penalties model corner complexity:

1. Base corner requirement for each terminal pair (0 when axis-aligned, 1 when both X and Y differ).
2. Endpoint turn penalties when required movement is opposite to the terminal's outward edge direction.
3. Extra dogleg penalties for same-axis outward exits that require four-corner paths.
4. U-turn penalties at endpoints, waived when the route can continue forward on the same line to another terminal on that net.

The U-turn term has its own weight:

- `UTurnPenaltyWeight = 2`

### Hard Constraints

1. Every component may assume any of the sixteen orientations (rotation × mirror-X × mirror-Y).
2. If, after orientation, a component has a terminal connected to VDD on its north edge, no other component may occupy a grid cell in the same column at a smaller row index (i.e., above it). This constraint is edge-specific: it fires only when the VDD terminal resolves to the north edge under the chosen orientation.
3. If, after orientation, a component has a terminal connected to ground on its south edge, no other component may occupy a grid cell in the same column at a larger row index (i.e., below it). As with constraint 2, this is edge-specific to the south edge after orientation.
4. For every pair of components that share the same row or same column and that share a signal at any of their terminals, no component that does not also participate in that signal may be placed strictly between them along that row or column. "Strictly between" excludes the endpoints. For this test only, NMOS and PMOS devices are treated as having an effective 3 x 3 cell footprint centered on their anchor cell (one neighboring cell in each direction), so intervening/alignment checks use this expanded occupancy.
5. If a component does participate in that shared signal and is placed strictly between the pair, it is only allowed when that signal is present on the component edge aligned with the bisected axis under the chosen orientation: west/east for same-row placement and north/south for same-column placement.

### Soft Constraints

1. Input and bias ports are biased toward the left side.
2. Output ports are biased toward the right side.
3. Symmetric device groups are biased toward a shared vertical symmetry axis.

No compactness bias and no default-orientation stability bias are included in the current objective.

### Post-Optimization Cleanup

After the solver returns a placement, the grid is compacted by remapping occupied rows and columns to dense zero-based indices. This removes all empty bands, including internal empty rows or columns, not only outer margins.
