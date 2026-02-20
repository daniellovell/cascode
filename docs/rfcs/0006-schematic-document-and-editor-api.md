# RFC-0006: First-Class `render {}` Blocks and Native Editor API

Status: Draft
Authors: Codex (proposed), Daniel Lovell (review)
Created: 2026-02-10
Last Updated: 2026-02-16
Target Version: Cascode 0.6.x (package); format version 3.2
Related: ide/designer WYSIWYG editing, Node bindings for Cascode toolchain

---

## Abstract

This RFC proposes a first-class `render {}` block in Cascode as the canonical mechanism for persistent schematic layout intent. Structural circuit content remains the electrical source of truth; `render {}` stores sparse, user-authored visual constraints and overrides; the schematic engine computes full geometry and routing from structure plus render intent; WYSIWYG edits are back-annotated into `render {}`; and users can clear `render {}` and re-run auto-layout at any time.

This RFC also defines the native API required by editors: a NativeAOT C ABI from Cascode C# via `[UnmanagedCallersOnly]`, a Node.js N-API addon wrapping that C ABI, and the complete editor RPC surface including async `bench` jobs.

---

## 1. Problem Statement

The existing schematic pipeline computes layout transiently and emits it as SVG. User visual edits have no stable source-level representation, reopening a file cannot reliably preserve manual layout intent, and editor integrations depend on command-specific output quirks.

We need a language-level layout intent format that is stable under source evolution, sparse enough to avoid file bloat and merge noise, expressive enough for tasteful manual tuning, and separate from electrical semantics.

---

## 2. Goals and Non-Goals

### Goals

Introduce first-class circuit-local `render {}` blocks. Prefer relative anchor references over absolute coordinates. Preserve manual schematic edits reproducibly. Keep users free from coarse-grid lock-in. Support reset/reflow workflows that drop or partially ignore render data. Expose a complete native API for editor use.

### Non-Goals

Making visual layout affect electrical behavior. Defining UI library choices. Encoding full solver state in source. Supporting arbitrary SVG drawing primitives in `render {}` v1.

---

## 3. Current State

The Cascode render pipeline is implemented in `tools/render/` and follows a fixed sequence of stages.

`CircuitFlattener` expands inline circuits into a flat device list with hierarchically prefixed names. `CircuitGraph` builds a bidirectional connectivity graph from the flattened circuit, mapping nets to device-terminal connections and classifying ports by direction. `TopologyAnalyzer` walks the connectivity graph to assign devices to vertical topology rows (row 0 nearest VDD, higher rows nearer GND), identify symmetric groups (differential pairs, current mirrors, load pairs), detect floating passives, and determine passive orientations.

`CoarseGridPlacer` feeds topology results into a Google OR-Tools CP-SAT constraint solver that places devices on a discrete grid. The solver enforces no-overlap (devices in the same row use distinct columns), symmetry constraints (paired devices satisfy `col_L + col_R = 2 * axis`), fill-row insertion for horizontal passives between topology rows, and column specialization. The objective minimizes terminal-aware Manhattan wire length plus a compactness penalty. The solver runs single-threaded with a 2-second time limit and respects `CASCODE_SEED` for determinism.

`MazeRouter` routes power rails as horizontal lines with vertical drops, then routes signal nets via minimum spanning tree decomposition and obstacle-aware pathfinding. `SvgRenderer` converts the final placement and routing into SVG, using `DeviceGeometry` constants for symbol dimensions, terminal positions, and grid alignment.

The existing pipeline becomes the engine. `render {}` blocks provide user-facing constraints that are injected into the solver before it runs. The rendered output feeds back into `render {}` through back-annotation.

---

## 4. Design Principles

Structure is canonical for circuit meaning. Render intent is canonical for user visual choices. Computed geometry is derived and disposable. Relative references are preferred because they survive reflow better. `render {}` should be sparse and deterministic. The solver must support mixed hard/soft constraints.

---

## 5. First-Class `render {}` in Language

### 5.1 Placement

`render {}` is a circuit member block, syntactically parallel to `fill {}`, `constraints {}`, `harness {}`, and `slot {}`. It is valid inside `circuit` declarations at all elaboration levels (see section 10.1 for level-specific semantics).

```cascode
circuit OTA5T {
  level EL
  ...

  render {
    M1 {
      place ref INP 120 0 hard
      orient 0
    }

    gate_net {
      route ortho soft
      wp ref M1.G
      wp rel 0 60
      wp ref M2.G
    }
  }
}
```

Entity names in `render {}` are bare identifiers — the parser does not require `device`, `net`, or `port` prefixes. Semantic validation resolves the entity kind from the circuit's structural declarations, matching how fill-block connections (`M1.G--INP`) are parsed without syntactic type annotations. Anchor references use Cascode's standard dot notation (`M1.G` for terminal G of device M1). Constraint strength is a trailing modifier on the statement it qualifies rather than a separate `strength` statement. No `version` statement appears inside `render {}` because the file-level `VERSION` declaration already governs format compatibility.

Entities with a single `place` override can use a one-liner form without braces: `M1 place ref INP 120 0 hard`. Entities with multiple properties use block form. Each entity name may appear at most once in a render block.

### 5.2 Block Scope

`render {}` applies only to the containing circuit. There is no implicit inheritance across circuits, and cross-circuit render references are not supported.

### 5.3 Writer Rule

The canonical writer must keep `render {}` output deterministic with stable entity ordering, omit empty `render {}` blocks, and emit only constructs supported by the current format version.

---

## 6. Render Data Model

### 6.1 Canonical vs Derived

Canonical source-level data consists of structural circuit content and `render {}` declarations. Derived runtime/editor data (bounding boxes, terminal world coordinates, full routed segment lists, solver internals) may appear in API responses but is never stored in source.

### 6.2 Coordinate Model

All render coordinates are integers in **render units** (ru). One render unit equals one routing pitch, which is currently 10 pixels in the SVG output (`DeviceGeometry.RoutingPitch`). The origin is the top-left corner of the schematic canvas.

At current dimensions a MOSFET symbol occupies roughly 2 x 3 ru, a coarse grid cell is 4.5 x 5 ru, and a port symbol is about 1.4 x 0.5 ru. This granularity is sufficient for manual adjustments; the solver handles fine-grained placement within cells. Using integer coordinates eliminates floating-point precision concerns entirely. Writers emit integer literals with no normalization needed.

### 6.3 Allowed Entities in `render {}` v1

Three entity kinds are writable in render blocks: fill-block devices (placement, orientation, constraint strength), circuit ports (placement, side assignment, constraint strength), and fill-block nets (routing mode, waypoints, constraint strength). Entity names are bare identifiers — the parser uses a unified `renderEntity` rule and defers kind resolution to the semantic validation pass, which checks the identifier against the circuit's structural declarations.

Group constraints (align, distribute, symmetry) are deferred to v1.1. No electrical declarations are permitted inside `render {}`.

### 6.4 Device Overrides (v1)

A device entry supports `place <pointExpr> <strength?>` for position override with optional trailing strength; `orient <int> mirror?` for orientation where the integer is rotation in degrees (0, 90, 180, 270) and the optional `mirror` keyword enables mirror-x (omit for no mirror); and an optional `zindex <int>` for rendering z-order override. Constraint strength is a trailing keyword (`hard`, `soft`, or `hint`) on the `place` statement.

Device parameter edits, connectivity changes, and symbol overrides are not allowed in render blocks.

### 6.5 Port Overrides (v1)

A port entry supports `place <pointExpr> <strength?>` for position override with optional trailing strength, and `side <left|right|top|bottom|auto>` for preferred edge of the schematic boundary. The `place` syntax is identical to device placement.

### 6.6 Net Route Overrides (v1)

A net entry supports `route <ortho|auto> <strength?>` for routing strategy with optional trailing strength, and one or more `wp <pointExpr>` statements listed in order for waypoints.

Net endpoints are derived from connectivity; waypoints represent interior path preferences. The router may insert additional segments between waypoints unless the route strength is `hard` and solvable, in which case the exact waypoint sequence is enforced (see section 15, resolved question on hard net routes).

### 6.7 Constraint Strength

Constraint strength is a trailing keyword on `place` and `route` statements. Three levels are supported: `hard` means the constraint must be satisfied or the engine returns a diagnostic error; `soft` means the engine prefers the constraint strongly but may violate it with a warning; `hint` is a low-priority preference that the engine readily overrides.

When strength is omitted, defaults apply: engine-generated entries default to `hint`, user direct manipulations (via editor back-annotation) default to `hard`, and source-authored entries with no explicit strength default to `soft`.

---

## 7. Point Expressions and Anchor System

Relative anchoring is the preferred representation because it survives reflow better than absolute coordinates.

### 7.1 Point Expression Kinds

Three forms of point expression are supported.

`abs <x> <y>` places at absolute render-unit coordinates. `ref <anchorRef> <dx> <dy>` places relative to a semantic anchor with an integer offset; the offset pair is optional and defaults to 0 0. `rel <dx> <dy>` is a relative offset from the previous waypoint, valid only inside net route `wp` sequences.

All coordinate values are signed integers in render units.

### 7.2 Anchor References

An anchor reference identifies a semantic point in the schematic using Cascode's standard dot notation. `M1` refers to the device bounding box center. `M1.G` refers to a specific terminal (G, D, S for MOSFETs; P, N for passives). `INP` refers to the port symbol anchor point (ports have exactly one anchor). `canvas origin` is the top-left of the canvas at (0, 0). `canvas center` is the computed center of the canvas.

The anchor kind (device center, device terminal, port anchor) is determined by semantic resolution against the circuit's structural declarations, not by syntactic form. This matches how pin references work throughout the rest of the language.

### 7.3 Canonicalization Policy

Back-annotation should prefer `ref` when a stable semantic anchor is nearby, use `rel` for intermediate waypoints in a chain, and fall back to `abs` only when no stable anchor can be inferred. This minimizes drift and textual noise during iterative edits. Conversion from `abs` to `ref` happens only during back-annotation (edit operations), not during `cas format`.

---

## 8. Engine Integration and Re-render Semantics

### 8.1 Constraint Mapping to SAT Solver

Render constraints feed into the existing `CoarseGridPlacer` OR-Tools CP-SAT model.

A `hard` placement constraint maps to a BoolVar enforcement: the device's row and column variables are fixed to the grid cell corresponding to the render-unit position. The solver treats this as a non-negotiable constraint and reports UNSAT if it conflicts with other hard constraints.

A `soft` constraint maps to a weighted objective term. The solver adds an absolute-value penalty `|var - target|` to the objective function, weighted heavily enough that violations are costly but do not prevent solving. The weight is an engine tuning parameter, not exposed to users.

A `hint` maps to a low-weight objective term using the same mechanism as `soft` but with substantially lower weight, so the solver readily overrides it for better wire length or compactness.

Route constraints (waypoints) are enforced during `MazeRouter` execution. A `hard` route requires the router to visit specified waypoints in order, inserting segments for endpoint connectivity. A `soft` route adds the waypoints as preferred via-points with a cost penalty for deviation.

### 8.2 Re-render Modes

Three re-render modes define how the engine uses render constraints.

`respectRenderBlock` runs the full pipeline (flatten, topology, place, route) with render constraints injected before solving. The topology analyzer still runs; render overrides affect placement where specified, and unconstrained devices are placed normally. This is the default for all rendering.

`reflowUnlocked` keeps `hard` constraints as fixed placements, then re-runs the solver for everything else. `soft` and `hint` constraints are discarded and recomputed. This is useful after a user pins a few critical devices and wants the engine to reoptimize around them.

`rerenderFromScratch` strips the `render {}` block from the in-memory AST and runs the pipeline with no user constraints. The caller may optionally persist the stripping (removing `render {}` from source) or only use it for a transient re-render.

### 8.3 Unsatisfiable Hard Constraints

If the solver cannot satisfy `hard` constraints, it returns an error diagnostic listing the implicated entities. It does not silently degrade to soft unless the caller explicitly opts in via an `allowConstraintRelaxation` parameter on the render request.

---

## 9. Back-Annotation Semantics

### 9.1 Strategy

Back-annotation works in three steps. The editor applies schematic operations (move, rotate, reroute) to the in-memory AST's render block node. The full document is re-serialized via `CascodeWriter`. The updated text is returned to the editor.

Because `CascodeWriter` produces deterministic output, unchanged regions of the document are byte-stable. No partial or surgical text editing is needed; the writer is the single source of formatted output.

### 9.2 Operation Mapping

WYSIWYG operations map to render block updates. Moving a device creates or updates the entity's `place` statement with trailing `hard`. Rotating or mirroring a device updates `orient`. Moving a port creates or updates the entity's `place` statement with trailing `hard`. Drawing or adjusting a wire updates the net entity's `wp` entries and sets `route` strength to `hard`. Clearing a manual route removes `wp` statements but may keep `route <mode>`. Unlocking an item degrades `hard` to `soft` or removes the entry entirely.

### 9.3 Sparse Update Rules

Only touched entities are written. Unchanged entries remain byte-stable. If an entity returns to the engine-default state, its render override entry is removed.

### 9.4 Stale Reference Handling

A `RenderBlockValidator` checks render references against the circuit's structural declarations. References to nonexistent devices, ports, or nets produce diagnostics. The canonical writer prunes entries that reference nonexistent entities, so formatting a file with stale references cleans them automatically.

Rename is two atomic operations: the structural rename plus a render reference rename. The validator catches inconsistencies if these fall out of sync.

### 9.5 Symbol or Primitive Changes

If a device's primitive changes and its terminal set changes, the engine attempts a semantic anchor remap by terminal name. Missing terminals produce a diagnostic, and invalid anchor references are dropped. Remaining valid overrides are preserved.

---

## 10. Level Semantics, Toolchain Integration, and Versioning

### 10.1 Level Semantics

`render {}` is valid at all circuit elaboration levels.

At EL the circuit is fully sized and implemented. All render concepts apply: device placement, net routing, and port positioning. This is the primary use case for WYSIWYG editing.

At ML the circuit is fully implemented but unsized. The same render concepts apply because the structural content (devices, nets, connections) is identical to EL; only parameter values differ. Placement coordinates remain valid because the schematic topology is the same.

At HL, `render {}` constraints apply to slot composition: placement of slot instances and routing between them. When a high-level circuit is synthesized to ML or EL, render constraints are wiped because the structural content changes completely. The synthesized circuit gets a fresh `render {}` from the engine.

### 10.2 Toolchain Commands

`cas format` preserves render blocks. The canonical writer produces deterministic output, so formatting is a no-op for well-formed render blocks and a cleanup for malformed ones (pruning stale references, normalizing order).

`cas convert` preserves supported render constructs when converting between levels where the structural content is compatible (for example, EL to ML parameter changes). Conversion that changes structural content (HL synthesis) strips render blocks.

`cas emit` ignores render blocks entirely. Render data has no effect on SPICE emission.

The linker, `BundleDesugarer`, and `BenchBindingExtender` pass `render {}` through unchanged, following the same pattern used for `constraints`, `harness`, `env`, and `synth` blocks: a new `Circuit` is constructed with `Render = circuit.Render`.

### 10.3 Linked Output (.cai)

Linked `.cai` files preserve render blocks with their circuits. The linker copies render blocks without modification. Cross-file render references are not supported in v1.

### 10.4 Version Implications

Adding `render {}` is an additive language change. The format version bumps from 3.1 to 3.2 (minor increment). Readers at 3.1 that encounter a `render {}` block will ignore it per the existing unknown-field-tolerance rule, so forward compatibility is maintained.

The RFC targets package version 0.6.x. The format version (3.2) and the package version (0.6.x) are independent versioning tracks: format version tracks the `.cas` file format, while package version tracks the toolchain release.

---

## 11. Schematic API Contract

### 11.1 SchematicDocument

API responses use a `SchematicDocument` JSON structure that separates canonical and derived data:

```json
{
  "schema": "cascode.schematic/1.0",
  "documentId": "doc_1",
  "revision": 4,
  "circuit": "OTA5T",
  "renderSource": {
    "hasRenderBlock": true,
    "mode": "respectRenderBlock"
  },
  "structural": {
    "devices": [
      { "id": "M1", "type": "nmos", "terminals": ["G", "D", "S"] }
    ],
    "ports": [
      { "name": "INP", "direction": "input", "type": "analog" }
    ],
    "nets": [
      { "name": "gate_net", "connections": [["M1", "G"], ["M2", "G"]] }
    ],
    "supplies": ["VDD"],
    "grounds": ["GND"]
  },
  "layout": {
    "devices": [
      {
        "id": "M1",
        "position": { "x": 120, "y": 85 },
        "orientation": { "rotate": 0, "mirrorX": false },
        "bbox": { "x": 112, "y": 72, "width": 17, "height": 26 }
      }
    ],
    "ports": [
      {
        "name": "INP",
        "position": { "x": 0, "y": 85 },
        "side": "left"
      }
    ],
    "nets": [
      {
        "name": "gate_net",
        "segments": [
          { "from": { "x": 112, "y": 85 }, "to": { "x": 112, "y": 145 } },
          { "from": { "x": 112, "y": 145 }, "to": { "x": 232, "y": 145 } }
        ],
        "junctions": []
      }
    ]
  },
  "renderCache": {
    "terminalPoints": {
      "M1": {
        "G": { "x": 112, "y": 85 },
        "D": { "x": 129, "y": 72 },
        "S": { "x": 129, "y": 97 }
      }
    },
    "computedBboxes": {
      "M1": { "x": 112, "y": 72, "width": 17, "height": 26 }
    }
  },
  "diagnostics": []
}
```

The `structural` section mirrors the circuit's AST for editor use. The `layout` section contains all computed geometry: device positions and bounding boxes, port positions and side assignments, and net route segments with junctions. The `renderCache` provides precomputed terminal world coordinates and bounding boxes so the editor can resolve anchor references and draw hit targets without re-running the engine.

### 11.2 Required Editor Methods

The API surface covers document management, conversion, rendering, editing, validation, and job control.

Document management: `document.open`, `document.updateText`, `document.close`.
Conversion: `convert.toStructural`, `convert.toCas`.
Rendering: `render.schematic`.
Editing: `schematic.applyOperations`.
Validation: `erc.run`, `verify.run`.
Emission: `emit.run`.
Jobs: `job.start`, `job.poll`, `job.cancel`.
Extension: `command.execute`.

### 11.3 Operation Types

`schematic.applyOperations` supports `moveDevice` (update device placement, set strength to hard), `rotateDevice` (update device orientation), `mirrorDevice` (toggle mirror-x), `movePort` (update port placement), `setNetRouteWaypoints` (set waypoints for a net), `clearNetRouteWaypoints` (remove manual routing for a net), `pinEntity` (set constraint strength to hard), `unpinEntity` (degrade constraint strength or remove entry), `setConstraintStrength` (set arbitrary strength level), `setDeviceParam` (structural parameter edit, not render), `connectTerminals` (structural connection), and `disconnectTerminals` (structural disconnection).

Every operation must include `opId` and `baseRevision`.

### 11.4 Session Lifecycle and Revision Tracking

A session begins with `document.open`, which parses the source text, runs the render pipeline, and returns the initial `SchematicDocument` at revision 1. The editor applies operations via `schematic.applyOperations`, each of which increments the revision counter. The counter is monotonic and per-document.

All mutation methods require `baseRevision`. If the submitted revision does not match the document's current revision, the method returns `CASAPI-REVISION-CONFLICT` along with the current revision and a summary of which entities have changed. The editor must re-fetch or rebase before retrying.

`document.updateText` replaces the source text entirely, used when the user edits the `.cas` file directly. This re-parses, re-renders, and increments the revision. `document.close` releases all resources for the document.

---

## 12. Native Embedding Architecture

The native embedding architecture has three layers. The stable contract is the C ABI: C# core compiled as a NativeAOT shared library (`libcascode`), exposing functions via `[UnmanagedCallersOnly]`. The `@cascode/native` package is a thin synchronous N-API wrapper that mirrors the C ABI one-to-one — it handles UTF-8 string marshalling and memory lifecycle but introduces no concurrency, threading, or event machinery. Editor integrations build their own async and isolation layer on top, using worker threads, dedicated background processes, child processes, or direct synchronous calls as appropriate for their architecture. A stdio server may exist for diagnostics and testing but is non-primary.

The recommended topology uses two isolated processes for Cascode work. `cascode-editor` handles interactive operations (document open, render, edit, ERC) where low latency matters. `cascode-bench` handles long-running simulation jobs where blocking is expected. This separation ensures bench runs never stall the edit loop.

### 12.1 C ABI Exports

Lifecycle and memory: `cascode_create_session(options_json_utf8)` returns a session handle; `cascode_destroy_session(session)` releases it; `cascode_free_string(ptr)` frees any string returned by an API method; `cascode_last_error_json(session)` returns the last error as JSON.

Synchronous methods: `cascode_document_open`, `cascode_document_update_text`, `cascode_document_close`, `cascode_convert_to_structural`, `cascode_convert_to_cas`, `cascode_render_schematic`, `cascode_schematic_apply_ops`, `cascode_erc_run`, `cascode_emit_run`, `cascode_verify_run`, `cascode_command_execute`. Each takes `(session, request_json)` and returns a response JSON string.

Async job methods: `cascode_job_start`, `cascode_job_poll`, `cascode_job_cancel`. Each takes `(session, request_json)`.

Version methods: `cascode_api_version()` and `cascode_schema_version()` return version strings with no session required.

### 12.2 C ABI Memory Model and Error Handling

All string parameters are UTF-8 null-terminated. Response strings are callee-allocated; the caller must free them via `cascode_free_string()`. A NULL return indicates an error; the caller should consult `cascode_last_error_json()`.

Errors follow a structured JSON schema:

```json
{
  "schema": "cascode.error/1.0",
  "code": "CASAPI-PARSE-FAILED",
  "message": "Syntax error at line 14, column 5",
  "details": { "line": 14, "column": 5 }
}
```

Standard error codes include `CASAPI-PARSE-FAILED`, `CASAPI-REVISION-CONFLICT`, `CASAPI-INVALID-SESSION`, `CASAPI-INVALID-REQUEST`, and `CASAPI-SOLVER-UNSAT`.

Managed exceptions never cross the ABI boundary. Every `[UnmanagedCallersOnly]` export wraps its body in a try-catch that captures the exception into the last-error slot and returns NULL or a failure sentinel.

### 12.3 Thread Safety

Each session is single-threaded: concurrent calls on the same session handle are undefined behavior. Multiple sessions may exist concurrently on different threads. The C ABI enforces no locking; callers are responsible for serializing access to a given session.

### 12.4 GC Interaction

Session handles are opaque integer IDs mapped to managed objects via a static `ConcurrentDictionary<int, SessionState>`. No GC handles are pinned. `cascode_create_session` allocates a `SessionState`, inserts it into the dictionary, and returns the key. `cascode_destroy_session` removes it, making the managed objects eligible for collection. Leaked sessions (never destroyed) leak memory proportional to the session's document state.

### 12.5 Node API Shape

Package: `@cascode/native`, located under `editors/node/`.

The core binding is a synchronous interface that mirrors the C ABI one-to-one. Each method maps directly to a `cascode_*` export, handling UTF-8 string conversion and `cascode_free_string` cleanup internally.

```ts
export interface CascodeNative {
  createSession(optionsJson?: string): number
  destroySession(session: number): void
  call(session: number, method: string, requestJson: string): string
  lastErrorJson(session: number): string | null
  apiVersion(): string
  schemaVersion(): string
}
```

`call()` dispatches to the corresponding C ABI function by method name (e.g., `"document.open"` calls `cascode_document_open`). It returns the response JSON string on success or throws on NULL return, attaching the structured error from `lastErrorJson`. `createSession` and `destroySession` map to `cascode_create_session` and `cascode_destroy_session`.

The package also exports typed convenience wrappers that parse the JSON response into TypeScript types:

```ts
export function open(native: CascodeNative, session: number, req: DocumentOpenRequest): DocumentOpenResponse
export function updateText(native: CascodeNative, session: number, req: DocumentUpdateTextRequest): DocumentUpdateTextResponse
export function close(native: CascodeNative, session: number, req: DocumentCloseRequest): void
export function render(native: CascodeNative, session: number, req: RenderSchematicRequest): RenderSchematicResponse
export function applyOps(native: CascodeNative, session: number, req: ApplyOperationsRequest): ApplyOperationsResponse
export function erc(native: CascodeNative, session: number, req: ErcRunRequest): ErcRunResponse
export function emit(native: CascodeNative, session: number, req: EmitRunRequest): EmitRunResponse
export function jobStart(native: CascodeNative, session: number, req: JobStartRequest): JobStartResponse
export function jobPoll(native: CascodeNative, session: number, req: JobPollRequest): JobPollResponse
export function jobCancel(native: CascodeNative, session: number, req: JobCancelRequest): void
```

Each wrapper calls `native.call()`, parses the JSON result, and returns a typed object. These are pure functions with no state or side effects beyond the session mutation.

### 12.6 Node Addon Architecture

The addon is a synchronous N-API binding built with prebuildify using N-API version 9 (stable ABI). Prebuilt binaries are provided for darwin-arm64, darwin-x64, and linux-x64. All C ABI calls are blocking within the addon; it introduces no threads, event emitters, or async machinery. Consumers choose their own concurrency model.

In a desktop designer, the addon loads inside a dedicated background process — for example, an Electron utility process or equivalent runtime with full Node.js capabilities and crash isolation, connected to the UI process via MessagePort-based IPC with cross-process callback marshalling. Blocking calls are acceptable in this context because the process exists solely for Cascode work. The process's message handler serializes access to the session — one `call()` at a time — which satisfies the C ABI's single-threaded session requirement from section 12.3.

The recommended topology runs two background processes. `cascode-editor` hosts a session for interactive operations: document open, render, schematic edits, and ERC. These calls are fast (target under 50ms for single-device moves) and return results directly to the UI. `cascode-bench` hosts a separate session for bench simulation jobs, which may run for seconds or minutes. This separation ensures bench work never blocks edit responsiveness.

### 12.7 Bench as Editor Primitive

Bench runs are modeled as jobs within the editor API. Designers need in-editor run, cancel, progress, and results for bench executions, and bench runs are inherently long-running and must not block the edit loop. The C ABI job model (`job_start`/`job_poll`/`job_cancel`) provides the mechanism.

Bench execution runs in the `cascode-bench` background process. The UI invokes a function to start a bench run, passing the bench name, circuit, and a progress callback. Inside the background process, the function calls `job_start` on the C ABI, then enters a poll loop calling `job_poll` at a regular interval. Each poll that returns progress data invokes the callback, which cross-process callback marshalling delivers to the UI for display.

Cancellation is a separate call from the UI to the `cascode-bench` process. It calls `job_cancel` on the C ABI, which signals the running job to terminate. The poll loop observes the cancellation on its next iteration and returns. Because cancellation is a distinct call rather than shared state, there is no race between the UI's cancel request and the background process's poll loop — the C ABI handles the coordination internally.

---

## 13. Implementation Plan

### Phase 1: Language and Contracts

Add `render {}` grammar rules, AST nodes (`RenderBlock`, `RenderDeviceEntry`, `RenderPortEntry`, `RenderNetEntry`, point expression types), `RenderBlockValidator`, and writer support. Add parse/format round-trip tests for sparse render blocks. Bump format version to 3.2. Add schema definitions for the `SchematicDocument` API response.

### Phase 2: Engine Integration

Inject render constraints into `CoarseGridPlacer` as hard (BoolVar), soft (weighted objective), or hint (low-weight objective) constraints. Extend `MazeRouter` to accept waypoint constraints. Implement all three re-render modes.

### Phase 3: Back-Annotation

Map editor operations to AST render-block mutations. Implement the anchor canonicalization policy (prefer ref over abs). Add `RenderBlockValidator` stale-reference detection and pruning on write.

### Phase 4: Native API

Add `Cascode.Native` NativeAOT project. Expose C ABI functions with the memory and error model described in section 12.2 through 12.4. Add ABI memory ownership, version compatibility, and session lifecycle tests.

### Phase 5: Node Addon

Implement synchronous N-API wrapper with TypeScript declarations. Build with prebuildify for target platforms. Verify addon loads and operates correctly in background process isolation. Add integration tests for boundary round-trip latency and callback-based progress reporting.

---

## 14. Testing Requirements

Unit tests must cover `render {}` parse/validate/write round-trips, anchor resolution and canonicalization, hard/soft/hint constraint behavior in the solver, and `RenderBlockValidator` stale reference detection.

Integration tests must cover the full edit to back-annotate to reopen to visual equivalence cycle, `rerenderFromScratch` clearing render blocks, structural rename and delete updating render references, and level-specific render behavior at EL, ML, and HL.

ABI tests must cover UTF-8 payload ownership (callee-allocates, caller-frees), session lifecycle (create, use, destroy, leaked sessions), error model (last-error, exception containment), and version mismatch behavior.

Performance tests must cover incremental edit latency (target under 50ms for single-device move), repeated solve with mixed hard/soft constraints, boundary-process round-trip latency for edit operations, and callback-based progress reporting for bench jobs.

---

## 15. Resolved Questions

**Groups (align/distribute/symmetry):** Deferred to v1.1. The grammar and AST reserve space for `group` entries, but no editor UX or solver support ships in v1. Adding groups is a minor version bump (3.3).

**Hard net routes:** A `hard` route enforces the exact waypoint sequence. The router must pass through specified waypoints in order but may add segments for endpoint connectivity (connecting the first waypoint to the source terminal and the last waypoint to the drain terminal).

**Automatic abs-to-ref conversion:** This happens only during back-annotation (edit operations), not during `cas format`. Formatting is a pure syntactic normalization; it does not resolve anchors or compute geometric relationships.

**Coordinate precision:** Integer render units (1 ru = 1 routing pitch = 10px). No floating-point coordinates appear in `render {}` blocks, so no precision policy is needed.

**Render data storage model:** `render {}` remains in-language as a circuit member block. Visual layout intent is design intent and belongs in the source file alongside structural and constraint declarations. Render blocks are sparse by design (only user overrides), minimizing file size and git noise. Full computed layout is ephemeral and returned in API responses per section 6.1.

**Node addon concurrency model:** The `@cascode/native` addon exposes synchronous bindings. Async dispatch and process isolation are the consumer's responsibility. The recommended topology uses two dedicated background processes: `cascode-editor` for interactive operations and `cascode-bench` for simulation jobs. This two-process topology keeps the edit loop responsive during bench runs without requiring worker threads within the addon.

---

## Appendix A: Grammar Sketch

```ebnf
renderBlock       := "render" "{" renderEntity* "}"

renderEntity      := renderIdRef renderOneLiner
                   | renderIdRef "{" renderField* "}"

renderIdRef       := idPart ("." idPart)*

renderOneLiner    := "place" pointExpr strengthLevel?

renderField       := "place" pointExpr strengthLevel?
                   | "orient" INT "mirror"?
                   | "side" sideValue
                   | "route" routeMode strengthLevel?
                   | "wp" pointExpr
                   | "zindex" INT

strengthLevel     := "hard" | "soft" | "hint"
routeMode         := "ortho" | "auto"
sideValue         := "left" | "right" | "top" | "bottom" | "auto"

pointExpr         := absPoint | refPoint | relPoint
absPoint          := "abs" signedInt signedInt
refPoint          := "ref" anchorRef (signedInt signedInt)?
relPoint          := "rel" signedInt signedInt

anchorRef         := "canvas" ("origin" | "center")
                   | renderIdRef

signedInt         := "-"? INT
```

---

## Appendix B: Minimal Example

```cascode
circuit DiffPair {
  level EL
  supply VDD
  ground GND
  input INP : analog
  input INN : analog
  output OUTP : analog
  output OUTN : analog

  fill {
    net tail : analog
    M1 : nmos
    M2 : nmos
    M1.G -- INP
    M2.G -- INN
    M1.D -- OUTP
    M2.D -- OUTN
    M1.S -- tail
    M2.S -- tail
  }

  render {
    M1 place ref INP 80 0 hard

    M2 place ref M1 120 0 hard

    tail {
      route ortho soft
      wp ref M1.S
      wp rel 0 60
      wp ref M2.S
    }
  }
}
```
