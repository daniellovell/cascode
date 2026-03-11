# Placement Guidelines

Status: Draft
Authors: Titan Yuan
Created: 2026-03-05
Last Updated: 2026-03-10

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
6. For NMOS and PMOS devices, the gate edge is constrained to face the signal source whenever the source direction is unambiguous at coarse-placement time. Input and bias ports force the gate to the west edge unless the device participates in a detected differential pair, and an internal point-to-point gate net (exactly one gate terminal plus one non-gate device terminal, excluding body/shield terminals) forces the gate to face that terminal's side when the source device ends up strictly left or right of the MOS device.
7. Devices participating in a detected differential pair must occupy the same row and distinct columns. Within each detected pair, the left device gate must face west and the right device gate must face east, so the two gates point in opposite outward directions about the pair's vertical centerline.
8. Devices participating in a detected current mirror must occupy the same row and pairwise-distinct columns. For mirrors with more than two devices, all devices in the mirror group share one common row.
9. For a point-to-point non-rail connection that resolves to a straight horizontal or vertical segment at coarse-placement time, no terminal on any third device may lie strictly on that segment unless that terminal is bound to the same net. This constraint is terminal-based rather than anchor-based: it blocks off-net terminals from sitting on another signal's straight connection even when the owning device is not itself an endpoint of that signal.
10. A detected symmetric passive pair must occupy the same row and distinct columns.
11. A passive classified as horizontal by topology analysis must remain horizontal when it touches a branching non-rail net, except when it is part of a detected symmetric passive pair. In practice this covers fanout and feedback spines where rotating the passive vertical would collapse multiple same-net elements into one column and obscure the branch structure; symmetric passive pairs are exempt because their columns are already constrained to remain distinct.

### Soft Constraints

1. Input and bias ports are biased toward the left side.
2. Output ports are biased toward the right side.
3. Symmetric device groups are biased toward a shared vertical symmetry axis.
4. Any pair of MOS devices that share a net through non-body terminals is biased toward topology-aware alignment. Pairs assigned to the same coarse topology row prefer row alignment; pairs assigned to different coarse rows prefer column alignment. When the pair is the mirrored pair of a detected symmetric group, equal-row placement mirrored about the vertical symmetry axis is treated as an alternative aligned state. This generic rule is suppressed on the three-device CMOS branching nets handled by the dedicated L-shape objective.
5. CMOS devices sharing a non-rail signal are additionally biased toward local clustering by Manhattan distance rather than a strict same-row or same-column rule. When exactly three such devices form an L shape with two on a common row and the third off-row, the off-row device is biased toward the vertical centerline of the horizontal pair.
6. Same-flavor drain/source chains are biased toward matching mirror-X orientation, but this soft preference yields to the hard gate-facing rule above when the two disagree.

No compactness bias and no default-orientation stability bias are included in the current objective.

### Post-Optimization Cleanup

After the solver returns a placement, the grid is compacted by remapping occupied rows and columns to dense zero-based indices. This removes all empty bands, including internal empty rows or columns, not only outer margins.
