# RFC-0012: Manual Schematic Mode And Editor Contract

Status: Active
Authors: Cascode + Designer maintainers
Created: 2026-03-15
Last Updated: 2026-03-15

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

Manual mode constraints:

- `setPortSide` rejects `auto` side in manual mode
- manual updates must not be rewritten into solver-only behavior

### Diagnostics

Diagnostics returned to editor clients are structured and preserve:

- `severity`
- `code`
- `message`
- `entityRefs` (`deviceId`, `portName`, `netName`, `segmentIndex`)
- `geometry` (`point`, `segment`, `bbox`) where applicable

Clients must not flatten diagnostics into message-only strings.

## Designer Integration Contract

Designer must support authoring (without source hand-editing) for all first-class manual primitives:

- device placement
- device orientation
- port placement
- port side
- net segments

Editor sync behavior:

- after text update, do not force extra auto reflow over manual documents
- preserve native error codes/details through boundary, sync controller, store, and panel

## Canonical References

- Language grammar: `tools/language/Cascode.g4`
- Exact manual resolver: `tools/render/Layout/ExactSchematicResolver.cs`
- Native operation applier: `tools/native/Cascode.Native/SchematicOperationApplier.cs`
- Designer operation flow: `ide/designer/src/features/cascode-schematic/hooks/useSchematicOperations.ts`

