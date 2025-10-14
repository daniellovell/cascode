## Workspace library architecture

Status: current as of 2025-10-08. Scope: `tools/workspace` scan stack and its CLI contract.

Goal
- Build a PDK-agnostic catalog of physical devices and electrical models, match them deterministically, and persist to a single workspace database consumed by the CLI and flows.

Responsibilities
- Parse `cds.lib` to identify logical libraries and paths.
- Discover "Device" cells that have both layout and symbol views.
- Extract Spectre models from model decks (subckt-first; bin suppression).
- Derive geometry constraints (w/l/area/nf ranges) when available.
- Match devices to models using normalized naming and tags.
- Persist results to a workspace SQLite database (`pdk.db`).

Non-responsibilities
- UI rendering (CLI/TUI) and SPICE execution; those consume persisted data.

Assemblies & boundaries
- Lives under `tools/workspace`. May depend on SQLite and file IO.
- Does not depend on `tools/cli` or any UI types.

Key components
- `WorkspaceScanner` — orchestration root; builds the end-to-end scan result.
- `CdsLibParser` — resolves libraries from `cds.lib` (with recursive includes).
- `PhysicalLibraryScanner` — crawls library directories to discover cells and their views.
- `SpectreDeckInspector` — collects model files/sections from `.cdsinit` context.
- `SpectreModelExtractor` — parses decks into logical models; prefers subckts over raw `.model` bins.
- `ModelGeometryExtractor` — normalizes geometry constraints into a compact structure.
- `NameNormalization` — config‑driven classification (no heuristics) for class/subclass/VT/VDD/tags; reads YAML from CASCODE_HOME.
- `DeviceModelMatcher` — produces `DeviceModelMatch` with ranking and notes.
- `PdkDatabaseWriter/Reader` — persistence boundary to `pdk.db`.

Data model (logical)
- Device: id, class (Nmos|Pmos|Stdcell|Capacitor|Resistor|…), subclass (Inverter|Buffer|MIMCAP|TFR|…), library, paths, views, has_layout, has_symbol, vt_tags, vdd_tags, tags.
- Model: name, class (Nmos|Pmos|…), thresholdFlavor, voltageDomain, modelType=subckt|model, sections/decks, geometry.
- DeviceModelMatch: device_id, ordered model_names (subckt-first), quality, notes.

Classification taxonomy
- `DeviceClass` enum (selected): Unknown, Nmos, Pmos, Bipolar, Diode, Resistor, Capacitor, Inductor, TransmissionLine, Stdcell, Other.
- `DeviceSubclass` enum (selected):
  - Stdcell: Inverter, Buffer, Nand, Nor, And, Or, Xor, Xnor, Multiplexer, Demultiplexer, Flipflop, Latch, Adder.
  - Capacitor: MOSCAP, MIMCAP, MOMCAP, VarCap.
  - Resistor: TFR (thin-film), RMetal, RPoly, RWell.
  - MOS devices: DeepNwell, RF.
- `NameNormalization.ClassifyByName()` determines class using YAML only.
- `NameNormalization.ClassifySubclass()` refines classification using per‑class YAML subclass rules.

Pipeline (high level)
1) Parse `cds.lib` → `List<WorkspaceLibrary>` (name, path).
2) Scan physical libraries → `List<Device>` where cells contain both `layout` and `symbol` views.
3) Inspect model decks and extract models (subckt-first; filter bins).
4) Optionally extract geometry constraints from model parameter blocks.
5) Match devices↔models via normalized id + class/vt/vdd tags (ranked; ambiguity recorded).
6) Persist libraries, models, devices, matches, geometry to `pdk.db`.

Invariants
- Device rows represent synthesizable primitives: must have `layout` AND `symbol`.
- Prefer subckt models when available; fall back to raw `.model`.
- Matching is deterministic given same decks and directory tree; random choices forbidden.
- The SQLite database is the single source of truth; no parallel JSON caches.

Incremental scan & performance
- Track file mtimes/hashes for cds.lib/model decks; re-scan when they change.
- Library crawl is single-depth per library; case-insensitive view directory checks.

Observability
- Warnings for missing library paths, cells without required views, ambiguous matches.
- `CASCODE_DEBUG=1` may emit normalized keys and candidate sets for diagnosis.

CLI contract
- `pdk scan` populates `$CASCODE_HOME/workspaces/<hash>/pdk.db` (default `~/.cascode/workspaces/<hash>/pdk.db` when CASCODE_HOME is unset).
- `pdk devices`, `pdk device`, `pdk match` read from `pdk.db` to present catalogs and coverage.

Testing strategy
- Unit: `NameNormalization`, `DeviceModelMatcher`, `PdkDatabaseWriter/Reader` idempotency.
- Integration: Sky130 fixture (`tests/fixtures/pdk/sky130`) end-to-end scan; assert stable counts and golden summaries.
- Architecture: forbid references to CLI; ensure parser remains IO-free.

Open items
- OA-only enumeration (future integration).
- Optional manual override map for edge-case device↔model bindings.
