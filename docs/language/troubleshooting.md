# Troubleshooting

This page collects common failure modes when writing Cascode, along with the fastest way to diagnose
and fix them.

## Linking and includes

Unresolved include:

- Symptom: `CAS1008: Unresolved include ...`
- Fix: confirm the included library exists under the expected search root and that the target file’s
  `library ...` name matches the include path. Run `cascode link` to see the diagnostic early.

## Emission and levels

Non-EL circuit used for emission:

- Symptom: emission rejects a circuit because it is not `level EL`.
- Fix: ensure the circuit declares `level EL` and that sizing and device primitives are emission-ready.

Unresolved `[Auto]` sweep at EL:

- Symptom: emitter rejects `[Auto]` sweep specifications.
- Fix: replace `[Auto]` with explicit sweep bounds, or resolve the sweep in a prior stage and persist
  the resolved result before emission.

## Connectivity and bindings

Undefined net / missing terminal:

- Symptom: a `pinRef` references a name that is not declared.
- Fix: check for typos and confirm whether the name is a circuit terminal (`input`/`output`) or a local
  `net`. Prefer declaring nets early in the `fill {}` block.

Missing bench terminal mapping:

- Symptom: binding checker reports an unmapped bench terminal.
- Fix: map the entire terminal. For bundle terminals, ensure all leaves are covered.

Unknown bench terminal in binding:

- Symptom: binding references `bench.X` but the bench has no terminal named `X`.
- Fix: update the binding mapping or the bench’s `stim`/`resp` declarations.

## Electrical rule checks

Floating gate / missing gate binding:

- Symptom: ERC reports a floating gate (common in MOSFET instances).
- Fix: ensure every MOS device binds `.G--...` to a real net and that the net is driven or tied.

VDD–GND short / passive rail bridge:

- Symptom: ERC reports a direct short between rails through an active device or passive component.
- Fix: correct the topology; introduce the intended intermediate nodes; avoid connecting passives
  directly between rails unless explicitly intended.

Run `cascode erc` early when iterating on EL circuits to catch these issues before emission.

## Bench runtime and measurements

Measurement type mismatch:

- Symptom: a measurement’s declared unit implies an expected type, but the body returns a mismatched value.
- Fix: check that the measurement returns the correct kind (`: Hz` returns `Frequency`, `: dB` returns
  `VoltageRatio` in dB, `: Vrms` returns integrated noise).

Missing datasets for an analysis:

- Symptom: runtime errors like “missing AC dataset” or “missing Noise dataset”.
- Fix: ensure the bench declares the required analysis instance and that the backend supports it for
  the selected circuit. For input-referred noise, both `NoiseAnalysis` and `ACAnalysis` must be present.
