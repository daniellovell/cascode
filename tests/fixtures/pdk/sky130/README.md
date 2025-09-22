# Sky130 Virtuoso PDK Fixture

This fixture packages the open-source Sky130 Cadence Virtuoso enablement so the
Cascode CLI can exercise its Virtuoso workspace discovery flow without touching
an installed PDK. It mirrors the layout expected by `pdk scan`, model deck
cataloging, and future OpenAccess ingestion.

## Top-Level Layout

```
sky130/
├─ .cdsinit           # Session bootstrap; points Spectre at stdcells.scs
├─ cds.lib            # Library map for Virtuoso/OpenAccess
├─ stdcells.scs       # Aggregate model deck referenced from .cdsinit
├─ cells/             # Device master cells (layout/symbol data)
├─ libs/              # OpenAccess libraries and Spectre decks
└─ models/            # Primitive model taxonomy and corner decks
```

### `.cdsinit`
- Uses `envSetVal("spectre.envOpts" "modelFiles" ...)` to seed the Spectre model
  search path with `./stdcells.scs`. Cascode should parse this line to locate the
  entry point for model discovery.
- Loads supplemental SKILL scripts via `prependInstallPath`, matching Cadence’s
  reference setup. These calls can be ignored by the CLI but confirm we are in a
  Virtuoso-style workspace.

### `cds.lib`
- Declares OpenAccess libraries such as `sky130_fd_pr_main` as well as the
  digital standard-cell bundles (`sky130_scl_*`).
- Provides canonical names the CLI can surface when listing available PDK
  libraries. Paths are relative to the fixture root, which is why setting
  `CASCODE_PDK_ROOT` (or using `pdk set-dir`) is required before scanning.

### `stdcells.scs`
- Aggregates Spectre-ready model decks via `include` statements that reference
  the digital libraries’ Spectre netlists under `libs/sky130_scl_* /spectre`.
- The `$WORK_DIR` token matches Cadence’s expectation; the CLI should substitute
  the resolved PDK root before launching Spectre jobs or follow includes during
  static analysis.

### `libs/`
- Contains the full OpenAccess library trees shipped by Cadence (`sky130_fd_pr`
  for primitive devices and several `sky130_scl_*` libraries for standard
  cells). Each library carries technology files (`techfile.tf`, `tech.db`),
  category catalogs (`*.Cat`), and—where applicable—Spectre decks in
  `spectre/`.
- When `pdk scan` walks the include hierarchy starting from `stdcells.scs`, it
  will land in these `spectre` subdirectories to find corner-specific decks.

### `cells/`
- Mirrors Cadence’s installation of primitive cell master data. The directory
  names are aligned with device names that appear in generated netlists (for
  example `nfet_01v8`, `pfet_01v8_hvt`, and the ESD devices).
- While the CLI does not currently parse layout data, this tree provides a
  realistic anchor for future OpenAccess queries.

### `models/`
- Breaks down raw Spectre models by category:
  - `corners/<corner>/` holds the SPICE decks for `tt`, `ff`, `ss`, `sf`, `fs`,
    and other PVT corners used by Spectre simulations.
  - `continuous/`, `parameters/`, `parasitics/`, and `r+c/` replicate the
    additional include trees referenced by the corner decks.
  - Specialized SONOS subdirectories (`sonos`, `sonos_e`, etc.) demonstrate how
    reliability/aging variants are organized.
- These paths are what `/pdk models` should surface once scanning discovers all
  nested `include` statements.

## Using the Fixture

1. Point the CLI at this directory:
   ```bash
   CASCODE_PDK_ROOT=tests/fixtures/pdk/sky130 dotnet run --project tools/cli/Cascode.Cli.csproj
   ```
   or run `pdk set-dir tests/fixtures/pdk/sky130` from inside the shell.
2. Execute `pdk scan` to populate the local model database. The scan should
   follow `.cdsinit → stdcells.scs → libs/.../spectre/*.scs` and enumerate the
   SPICE decks under `models/corners/*`.
3. Subsequent commands like `pdk models` and the planned `/char gen` preview can
   leverage the discovered paths to launch Spectre with realistic includes.

## Notes

- All content remains open-source (per the original SkyWater/Cadence release).
- No changes were made to the supplied files; additions (like Cascode
  characterization outputs) should live alongside this tree under
  `tests/fixtures/pdk/sky130` to keep the fixture hermetic.

