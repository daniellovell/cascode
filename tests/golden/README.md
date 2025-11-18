Golden regression assets
========================

This tree is the home for end-to-end regression artifacts that exercise the
Cascode → CasIR → SPICE flow. It is intentionally kept data-only so that
tests across different assemblies can consume the same canonical inputs and
expected outputs.

Layout (intended)

- `tests/golden/cas/…` — canonical Cascode source files used in regressions.
  - Example: `cas/ota/OTA5TSingleEndedSimplified.cas`.
- `tests/golden/casir/…` — CasIR JSON snapshots (`*.cir`) emitted by the
  compiler for those sources (HL/ML/EL where applicable).
- `tests/golden/spice/…` — SPICE netlists and bench harness netlists generated
  from CasIR for back-end regression and cross-simulator checks.

For the v0 OTA slice the CasIR “golden” already lives under
`tests/golden/casir/ota/OTA5TSingleEndedSimplified.ml.cir`, and the compiler
unit test (`OtaCompilerTests`) loads it from disk. Additional motifs/modules
can now be added following the same pattern. 
