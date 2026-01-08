# AGENTS.md

Scope: repo‑wide (subtree AGENTS.md may further restrict).

BEFORE MAKING ANY CHANGE, ASK YOURSELF IN YOUR CHAIN OF THOUGHT: "How can I maximize long-term maintainability and reduce complexity?"
 - Can I achieve a reduction in total lines of code with this feature?
 - Can existing code be reused to avoid duplication?
 - Is there an opportunity to extract a common interface or function to unify the implementation?

## Purpose & Map

- Purpose: bootstrap the Cascode toolchain while keeping the root lean.
- Structure: docs live in `docs/`; language references in `spec/`; canonical motif libraries in `lib/`; runnable examples in `examples/`; implementation code in `tools/cli`, `tools/parser`, `tools/workspace`; regression assets in `tests/`; build artifacts go to `build/` (ignored).
- Where to read first: `docs/architecture/README.md` plus relevant component docs, e.g. `docs/architecture/cli-architecture.md` and `docs/architecture/pdk-scan-architecture.md`.

## Jump Table (start here by task)
- CLI commands & TUI
  - Read: `docs/architecture/cli-architecture.md`
  - Code: `tools/cli/**`
  - Quick verify: `dotnet run --project tools/cli/Cascode.Cli.csproj -- help`
  - PDK flow check: `... -- pdk scan tests/fixtures/pdk/sky130` then `... -- pdk devices --workspace tests/fixtures/pdk/sky130`
- PDK scan & workspace database
  - Read: `docs/architecture/pdk-scan-architecture.md`
  - Code: `tools/workspace/**` (scanner, matcher, DB)
  - Fixture: `tests/fixtures/pdk/sky130`
  - Quick verify: `dotnet run --project tools/cli/Cascode.Cli.csproj -- pdk scan tests/fixtures/pdk/sky130`
- Parser & Cas IR
  - Read: `spec/**`, `docs/architecture/README.md` (parser notes)
  - Code: `tools/parser/**`
  - Verify: use examples under `examples/**`; if parser changes affect CLI, add/adjust a CLI command.
- Examples, motifs, and specs
  - Read: `spec/**`
  - Code/data: `lib/**`, `examples/**`
  - Verify: minimal runnable examples; keep outputs under `build/`
- Tests
  - Read: `tests/README.md`
  - Unit tests: `tests/unit/tools/<area>/Cascode.<Area>.Tests/` (add or update alongside `tools/<area>/` code)
  - Integration/golden: `tests/integration/cli/**`, `tests/golden/**`
  - Determinism: set `CASCODE_SEED`; normalize timestamps/paths before persisting goldens.

## Spec/Documentation Writing Style

Avoid commonly overused AI motifs such as excessive use of bulleted lists and bolded text.
Use professional prose and use precisely the level of verbosity that is required to communicate the intent of the text.

Bold formatting should be reserved for technical terms being defined, critical warnings, or table headers requiring emphasis. Do not bold every subsection label, list lead-in, or organizational marker.

## Boundaries
- `tools/cli`: CLI only; may depend on `tools/workspace`, `tools/parser`. Nothing depends on CLI.
- `tools/workspace`: orchestration + persistence to `pdk.db`. No UI.
- `tools/parser`: pure; no file/DB/network IO.
- No cycles or cross‑layer shortcuts.

## Hard Rules
- ≤400 added LOC per patch; split if larger.
- ≤500 LOC/file; ≤80 LOC/method (justify rare exceptions).
- No dead/unreferenced code or files; remove in the same work.
- `Directory.Build.props` enforces `TreatWarningsAsErrors`, `Nullable=enable`, and `EnableNETAnalyzers` for `tools/*`. Do not duplicate these in individual csproj files.
- No legacy toggles/shims; migrate and delete.
- Build artifacts live in `build/` only.
- Safety: NEVER run `git restore`.
- No DB migrations: never write code to migrate existing `pdk.db` files. When classification rules or matching change, instruct users to rerun `pdk scan` to regenerate the workspace database.
- No migrations means no reader shims. Readers must assume the current schema only
- Logging: when surfacing config or workspace-level errors, prefer dependency-injected `ILogger` so messages reach the CLI/TUI log. Only fall back to `Console.Error` when no logger is available.
- Do not write or commit references to specific fabs or process names (only `sky130` and `gpdk045` are allowed) without explicit permission from the user.
- Do not write comments which reference our in-progress discussions. Comments should reflect the final state of the code, not the path we took to get there.
- Always check `dotnet csharpier format .` when changes to ANY C# file are complete.

## Back-Compat Prohibition
- Zero runtime back‑compat: when data/schema/format changes, do not add conditionals to read old shapes or values.
- Remove the old path in the same patch; do not keep both implementations.

## Testing (must update in same PR)
- Unit + integration + architecture tests for touched code.
- Use `tests/fixtures/pdk/sky130`; keep critical golden outputs under `tests/golden/**`.
- Deterministic by default: normalize time/paths/order; set `CASCODE_SEED` for randomness.
- CASCODE_HOME isolation: MUST use `Cascode.TestSupport.CascodeHome.Create…` helpers (wired into test csproj via `tests/shared/`). NEVER manually create temp dirs for home or call `Environment.SetEnvironmentVariable("CASCODE_HOME", ...)` in tests. Each test must own and dispose an isolated home to avoid cross-run interference.

## Required Local Checks
- Build: `dotnet build tools/cli/Cascode.Cli.csproj`.
- Test: `dotnet test Cascode.sln --configuration Release`.
- Run: `dotnet run --project tools/cli/Cascode.Cli.csproj -- pdk scan tests/fixtures/pdk/sky130`.
- Verify: `... -- pdk devices --workspace tests/fixtures/pdk/sky130` and `... -- pdk set-dir tests/fixtures/pdk/sky130`.

## PR Checklist
- Short plan of steps (final state).
- Tests added/updated; golden deltas intentional.
- Exact `dotnet run -- …` commands + notable results.
- Within size limits; no dead code; no build artifacts committed.

## Retiring/Replacing (docs & code)
- Delete superseded files in the same PR; do not leave stubs.
- Update all in-repo references (grep before merge) to the new paths.
- If external links exist, add a tiny redirect file only with an issue to remove it in ≤30 days.
- Document the move in the PR body (“Replaces X with Y”).
- Ban "temporary transition code"; if present, it must fail fast outside that window

## ACIR Versioning

- Canonical version: `tools/acir/ACIRVersion.cs` (MAJOR.MINOR format)
- **Major bump**: breaking changes - reader rejects different majors
- **Minor bump**: additive-only changes - reader accepts any minor within same major
- YOU MUST bump version when changing: `ACIRDocument.cs`, `ACIRReader.cs`, `ACIRWriter.cs`, `ACIRBenchAdapter.cs`, `ACIRTemplateHarness.cs`, or template data contracts
- On bump: update all `tests/golden/acir/**/*.cir` headers to latest MAJOR.MINOR
- On bump: inspect and update all ACIR versioning in unit/integration tests to be up to date with the latest features.
- NEVER add conditional parsing for different minors - unknown fields/syntax silently ignored

## Anti‑Patterns
- Cross‑layer deps; IO in parser; UI outside CLI.
- God files/classes; silent behavior changes; “temporary” duplication.
- Flags that preserve old/new paths indefinitely.

## When Unsure
- Stop and write a design doc; prefer smaller patches; delete over deprecate.
