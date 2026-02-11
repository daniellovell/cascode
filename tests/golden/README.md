Golden regression assets
========================

This tree is the home for end-to-end regression artifacts that exercise the
Cascode → SPICE flow. It is intentionally kept data-only so that
tests across different assemblies can consume the same canonical inputs and
expected outputs.

Layout (intended)

- `tests/golden/cas/…` — canonical linked Cascode source files used in regressions.
  - Example: `cas/ota/OTA5TSingleEndedSimplified.cai`.
- `tests/golden/cas/…` — Cascode text snapshots (`*.cai`) emitted by the
  compiler for those sources (HL/ML/EL where applicable).
  - Files are named `{circuit}.{level}.cai` (e.g., `OTA5TSingleEnded.ml.cai`).
  - The `.cai` extension indicates linked Cascode files (default mode is self-contained; include-pruned mode may retain a minimal include set).
- `tests/golden/results/…` — simulation results (JSON) used to verify constraint compliance.

For the v0 OTA slice the Cascode "golden" already lives under
`tests/golden/cas/ota/OTA5TSingleEndedSimplified.ml.cai`, and the compiler
unit test (`OtaCompilerTests`) loads it from disk. Additional motifs/modules
can now be added following the same pattern.
