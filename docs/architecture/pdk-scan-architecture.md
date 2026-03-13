PDK Scan and Matching Configuration

Overview

Cascode discovers devices and models from a PDK workspace, writes them into a per‑workspace database, and then matches devices to models. The matching behavior is configurable via a YAML file created on first run. Classification is YAML‑only (no heuristics).

- Config path: CASCODE_HOME/config/pdk-matching-patterns.yml
- First run: pdk scan creates the file with sensible defaults and prints the path.
- Edits: change this YAML to tune normalization, class/subclass classification, and matching thresholds without recompiling.

How it is used

- Name classification (tools/workspace/NameNormalization.cs)
  - Uses classify.classes and classify.subclasses exclusively (no fallbacks).
  - VT/infra tokens come from normalization.vt_tokens and classify.infra_tokens.

- Device↔Model matching (tools/workspace/DeviceModelMatcher.cs)
  - Uses normalization.* for name cleanup (vendor prefixes, model suffixes, VT/VDD tokens).
  - Uses behavior.* for scoring thresholds, ambiguity margin, and infra penalties.
  - Writes results into the PDK database (device_model_matches) during pdk scan.

Emit/bench integration

Emission and bench generation reuse the per-workspace pdk.db produced by pdk scan. When Cascode primitives reference PDK-backed devices via their `device "..."` directive (as opposed to built-in generic devices like `resistor` or `nmos_level1`), the CLI resolves model include paths and the preferred section for the current corner, injects those includes into the emitted netlists, and maps the device to the resolved model or subckt name. This flow never triggers a scan; if the database is missing, the CLI logs a warning and proceeds without PDK includes. For shared cluster runs, perform a single scan in a shared CASCODE_HOME and point jobs at the same workspace path (via --workspace or pdk set-dir) so they reuse the database. Corner selection comes from CASCODE_PDK_CORNER and defaults to tt.

Primitive emission and characterization

`pdk emit primitives` reads models from `pdk.db` and generates a structured PDK primitive library at `lib/pdk/<pdk>/`.

Library layout contract:

```text
lib/pdk/<pdk>/
  devices.cas
  resistors.cas
  capacitors.cas
  diodes.cas
```

Each file declares a file-level namespace under `lib.pdk.<pdk>.*` (for example `lib.pdk.sky130.devices`). Consumers can include the full package (`include lib.pdk.<pdk>`) or include specific symbols (`include lib.pdk.<pdk>.devices.nfet_01v8`) when tighter device-availability control is required.

By default, emission keeps canonical parametric family names and skips fixed-only wrapper families when no parametric representative exists. The command reports skipped fixed-only families. Use `--include-fixed` to include fixed wrapper variants.

`char gen` and `pdk char run` depend on parametric MOS primitives from this emitted library. If a selected model maps only to fixed wrappers and no parametric family representative exists, characterization fails fast with guidance instead of silently skipping the model.

Recommended flow

Set the PDK root (`pdk set-dir` or `--workspace`), run `pdk scan` once for that workspace, then run `emit`, `bench`, or `verify` as needed. Subsequent commands reuse the existing pdk.db and do not rescan.

YAML schema

version: integer (reserved)

normalization:
  vendor_prefixes: list of strings
    - Example: [ sky130_fd_pr__ ] — stripped before comparison
  model_suffix_regex: string (regex)
    - Removed from model names before matching; default removes _model / __model(_base) suffixes
  vt_tokens: list of strings
    - Tokens like lvt/rvt/hvt removed for base name matching and extracted as tags
  vdd_token_regex: string (regex)
    - Pattern stripped from names for base form, e.g. _01v8
  vdd_extract_regex: string (regex with groups n,f)
    - Converts "1.8V" → "01v8" for comparing to device VDD tags

behavior:
  min_accept_score: int
    - Minimum score for a candidate model to be considered
  ambiguous_margin: int
    - Models within (top_score − margin) are considered ambiguous ties
  infra_penalty_non_esd: int
    - Score subtraction for non‑ESD matches when a device is tagged as infra
  esd_keyword: string
    - Keyword that identifies ESD models (case‑insensitive contains)

classify:
  infra_tokens: list of strings
    - If present in a device name, the device is tagged "infra"
  classes: map<className → ClassPattern>
  subclasses: map<className → map<subclassName → ClassPattern>>
    - Subclass patterns are evaluated in file order; if more than one matches, the first wins.

ClassPattern fields (any may be used):

- prefixes: list of strings (name startswith)
- contains: list of strings (name contains)
- regex: list of regex (case‑insensitive)
- exclude_contains: list of strings (negation)
- exclude_regex: list of regex (negation)

Examples

1) Map a new vendor prefix and lighten matching thresholds

normalization:
  vendor_prefixes: [ sky130_fd_pr__, my_foundry__ ]
behavior:
  min_accept_score: 25
  ambiguous_margin: 5

2) Treat names starting with foo as NMOS, and add an inverter alias invx

classify:
  classes:
    nmos:
      prefixes: [ foo ]
  subclasses:
    stdcell:
      inverter: { prefixes: [ inv, invx ] }

Precedence and stability

- Classification is defined entirely by YAML; there are no built‑in fallbacks.
- Missing sections are safe: defaults (including classify) are embedded and used for first‑run initialization.
- Changes take effect the next time you run pdk scan.

No DB migrations

- The schema is stable. We never migrate existing workspace databases.
- If classification rules change, run pdk scan again to regenerate pdk.db for that workspace.

Operational notes

- pdk scan initializes the YAML on first run and logs the path.
- The file is global per CASCODE_HOME. (A future iteration can add per‑workspace overrides if needed.)
