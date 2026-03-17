# RFC-0012: Manual Schematic Mode And Editor Contract

Status: Active
Authors: Daniel Lovell
Created: 2026-03-15
Last Updated: 2026-03-16

---

## Purpose

Define the current canonical contract between Cascode language/render/native layers and the Designer editor for schematic authoring in `auto` and `manual` modes.

This RFC supersedes historical manual-routing behavior that referenced waypoint (`wp`) semantics.

## Language Contract

- `render { mode auto|manual }` is the only supported render mode syntax.
- `seg <pointExpr> <pointExpr>` is the only persisted net geometry primitive.
- `wp` is removed and must not be reintroduced.

### Mode Semantics

- **`mode auto`**
  - placement and routing remain solver-driven
  - explicit `seg` entries are routing guidance, not exact geometry constraints
- **`mode manual`**
  - geometry is explicit and fail-fast
  - missing explicit geometry is an error
  - no silent fallback to auto behavior

### Manual Completeness Requirements

In `mode manual`, the render block must contain explicit geometry for:

- device placement (`place`)
- device orientation (`orient`) when edited
- port placement (`place`)
- port side (`side`) with concrete side (`left|right|top|bottom`)
- net geometry (`seg`)

## Connectivity Semantics For Manual Segments

- endpoint on another segment interior: **connects** (T-junction)
- crossing without shared endpoint: **does not connect**
- dangling or disconnected manual geometry: **error**

## Native API Contract

### Render Mode Inputs

- render calls support:
  - `auto`
  - `manual`
  - `respectDocument` (uses source `render { mode ... }`)

### Schematic Operations

Supported authoring operations include:

- `moveDevice`
- `rotateDevice`
- `mirrorDevice`
- `movePort`
- `setPortSide`
- `setNetSegments`

Workflow-level native APIs include:

- `schematic.applyPlacementEdits`
- `schematic.previewRoute`
- `schematic.applyRouteEdit`

Manual mode constraints:

- `setPortSide` rejects `auto` side in manual mode
- manual updates must not be rewritten into solver-only behavior
- workflow APIs may recompute and persist explicit orthogonal `seg` geometry when the document is already in `mode manual`

### Diagnostics

Diagnostics returned to editor clients are structured and preserve:

- `severity`
- `code`
- `message`
- `entityRefs` (`deviceId`, `portName`, `netName`, `segmentIndex`)
- `geometry` (`point`, `segment`, `bbox`) where applicable

Clients must not flatten diagnostics into message-only strings.
