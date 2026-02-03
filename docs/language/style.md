# Cascode source style guide

This guide describes conventions used in the standard library and golden fixtures. It is intended to
make code easy to read, easy to diff, and easy to validate.

## Connectivity and bindings

Use `--` consistently for wiring and bindings:

- Instance bindings: `.Terminal--Net`
- Standalone connections: `a--b`
- Bench bindings: `bench.X--dut.Y` and `dut.X--net`
- Connector mappings: `A--B`

Prefer one binding per line inside `{ ... }` blocks. Comma-separated bindings are fine for short,
obvious cases, but avoid long one-liners that hide meaning in diffs.

## Naming

Use intent-revealing names:

- Bench bindings: `transfer_bench`, `noise_bench`, `tran_bench`, `vdd_pwr`
- Nets: `gnd`, `vcm`, `mirror_gate`, `tail_node` (avoid overly generic `n1`, `n2` unless truly local)
- Instances: `dp`, `cm`, `loadZ`, `sourceP` (short is fine when the role is conventional)

Keep names stable across refactors when possible; many golden tests assume stable ordering and stable
names.

## File organization

- Put reusable benches in `lib/std/bench/**`.
- Put reusable interfaces and connector interfaces near the primitives they describe (often `lib/std/prim/**`).
- Prefer small, single-purpose golden fixtures in `tests/golden/cas/**` that exercise a specific feature.

## Includes

For examples and tests, prefer `include lib.std` unless you are intentionally testing a minimal
include surface. For library code, include the smallest correct dependency.

## Determinism and golden friendliness

Cascode is intended to be diff-friendly. Prefer constructs that lead to stable output:

- Keep ordering stable (declare terminals, then fill, then benches/constraints/harness).
- When using `repeat` and `match`, choose bounds and case names that are deterministic and easy to scan.
- Avoid embedding timestamps or machine-local paths in source or expected outputs.

## Bench conventions

Bench bodies should be small, typed, and explicit:

- Use file-level helper functions in bench libraries when multiple benches share logic.
- Prefer `constraints.*` and `env.*` for configuration rather than hard-coded sweep bounds where feasible.
- Keep measurement bodies short; decompose into helpers if a measurement becomes non-trivial.

For examples, see `docs/language/bench-cookbook.md` and the standard benches under `lib/std/bench/**`.
