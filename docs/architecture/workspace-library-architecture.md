## Workspace Library Architecture

Status: current as of 2026-03-15. Scope: [tools/workspace](../../tools/workspace).

Goal
- Discover libraries, model decks, devices, and device-to-model matches from a PDK workspace and persist the result to a single workspace database consumed by CLI flows.

Responsibilities
- Parse `cds.lib` and related workspace bootstrap files.
- Discover model decks from `.cdsinit` and `libInit` content.
- Scan physical libraries for devices with the required views.
- Extract model metadata and geometry constraints.
- Match devices to models deterministically.
- Persist the scan result to `pdk.db`.

Non-responsibilities
- CLI rendering, interactive UX, or simulator execution.

Key components
- [WorkspaceScanner](../../tools/workspace/WorkspaceScanner.cs) discovers libraries, deck paths, and consolidated Spectre models.
- [PhysicalLibraryScanner](../../tools/workspace/PhysicalLibraryScanner.cs) finds device cells and views.
- [PdkScanService](../../tools/workspace/PdkScanService.cs) orchestrates full scan, match, geometry extraction, and persistence.
- [DeviceModelMatcher](../../tools/workspace/DeviceModelMatcher.cs) ranks device-to-model candidates.
- [ModelGeometryExtractor](../../tools/workspace/ModelGeometryExtractor.cs) projects model geometry into a compact representation.
- [PdkDatabaseWriter](../../tools/workspace/PdkDatabaseWriter.cs) and [PdkDatabaseReader](../../tools/workspace/PdkDatabaseReader.cs) define the persistence boundary.
- [PdkMatchingConfig](../../tools/workspace/PdkMatchingConfig.cs) reads the YAML-only normalization and matching rules.

Pipeline
1. Resolve the workspace root and parse [cds.lib](../../tests/fixtures/pdk/sky130/cds.lib)-style library maps.
2. Discover model decks from `.cdsinit` and `libInit` files.
3. Inspect each deck and consolidate extracted Spectre models.
4. Scan physical libraries for devices that have the required views.
5. Match devices to models using normalization and scoring rules from `CASCODE_HOME/config/pdk-matching-patterns.yml`.
6. Extract model geometry and project it onto matched devices.
7. Rewrite the workspace database at `CASCODE_HOME/workspaces/<hash>/pdk.db`.

Invariants
- Matching is deterministic for a fixed workspace tree and matching config.
- The workspace database is regenerated on each `pdk scan`; there is no migration path for older databases.
- Classification is driven by YAML config, not hard-coded heuristic fallbacks.
- Consumers reuse the persisted database; they do not trigger implicit rescans.

CLI contract
- `pdk scan` is the only command that rebuilds `pdk.db`.
- `pdk devices`, `pdk device`, `pdk match`, `pdk emit primitives`, and characterization commands read from the existing database.
- When matching rules change, the user reruns `pdk scan` for the affected workspace.

Testing
- Unit coverage lives under [tests/unit/tools/workspace](../../tests/unit/tools/workspace).
- Integration coverage uses fixtures such as [tests/fixtures/pdk/sky130](../../tests/fixtures/pdk/sky130) through the CLI and workspace test suites.
