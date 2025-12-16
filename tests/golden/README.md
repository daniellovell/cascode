Golden regression assets
========================

This tree is the home for end-to-end regression artifacts that exercise the
Cascode → ACIR → SPICE flow. It is intentionally kept data-only so that
tests across different assemblies can consume the same canonical inputs and
expected outputs.

Layout (intended)

- `tests/golden/cas/…` — canonical Cascode source files used in regressions.
  - Example: `cas/ota/OTA5TSingleEndedSimplified.cas`.
- `tests/golden/acir/…` — ACIR text snapshots (`*.cir`) emitted by the
  compiler for those sources (HL/ML/EL where applicable).
  - Files are named `{circuit}.{level}.cir` (e.g., `OTA5TSingleEnded.ml.cir`).
- `tests/golden/results/…` — simulation results (JSON) used to verify constraint compliance.

For the v0 OTA slice the ACIR "golden" already lives under
`tests/golden/acir/ota/OTA5TSingleEndedSimplified.ml.cir`, and the compiler
unit test (`OtaCompilerTests`) loads it from disk. Additional motifs/modules
can now be added following the same pattern.
