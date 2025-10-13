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
    - Converts “1.8V” → “01v8” for comparing to device VDD tags

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
    - If present in a device name, the device is tagged “infra”
  classes: map<className → ClassPattern>
  subclasses: map<className → map<subclassName → ClassPattern>>

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
