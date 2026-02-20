# AGENTS.md

Scope: repo‑wide (subtree AGENTS.md may further restrict).

BEFORE MAKING ANY CHANGE, ASK YOURSELF IN YOUR CHAIN OF THOUGHT: "How can I maximize long-term maintainability and reduce complexity?"
 - Can I achieve a reduction in total lines of code with this feature?
 - Can existing code be reused to avoid duplication?
 - Is there an opportunity to extract a common interface or function to unify the implementation?

## Purpose & Map

- Purpose: bootstrap the Cascode toolchain while keeping the root lean.
- Structure: docs live in `docs/`; language references in `spec/`; standard library lives in `lib/std/` (bundles, interfaces, circuits, benches, primitives); runnable examples in `examples/`; implementation code in `tools/cli`, `tools/language`, `tools/workspace`, `tools/bench`, `tools/render`; regression assets in `tests/`; build artifacts go to `build/` (ignored).
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
- Language & Cascode IR
  - Read: `spec/**`, `docs/architecture/README.md` (language notes)
  - Code: `tools/language/**` (grammar, reader/writer, linker, validation, emission)
  - Verify: use examples under `examples/**`; if language changes affect CLI, add/adjust a CLI command.
- Standard library & examples
  - Read: `spec/**`
  - Code/data: `lib/std/**`, `examples/**`
  - Verify: minimal runnable examples; keep outputs under `build/`
- Bench system (harnesses + execution)
  - Code: `tools/bench/**` (harness discovery, testbench generation, simulator backends)
  - Code: `tools/language/BenchRuntime/**` (bench planning + measurement evaluation)
- Render (schematic/layout)
  - Code: `tools/render/**`
- Node native editor API (`@cascode/native`)
  - Code: `editors/node/**`
  - Runtime package: `editors/node/package.json`
  - Platform package templates: `editors/node/platform-packages/**`
  - Staging script: `editors/node/scripts/stage-platform-package.mjs`
  - CI release wiring: `.github/workflows/dotnet.yml`, `.github/workflows/release.yml`
  - Local verify:
    - `cd editors/node && npm ci --omit=optional && npm run build`
    - Publish native runtime: `dotnet publish tools/native/Cascode.Native/Cascode.Native.csproj --configuration Release -r <rid> -p:PublishAot=true -o build/native/<rid>`
    - Set `CASCODE_NATIVE_LIB` to the produced shared library and run `npm test`
- Tests
  - Read: `tests/README.md`
  - Unit tests: `tests/unit/tools/<area>/Cascode.<Area>.Tests/` (add or update alongside `tools/<area>/` code)
  - Integration/golden: `tests/integration/cli/**`, `tests/golden/**`
  - Determinism: set `CASCODE_SEED`; normalize timestamps/paths before persisting goldens.

## ANTLR Regeneration

The grammar lives at `tools/language/Cascode.g4`; generated parser/lexer/visitor files live under `tools/language/Generated/`.
After any grammar edit, regenerate with:

```
curl -O https://www.antlr.org/download/antlr-4.13.2-complete.jar
cd tools/language
java -jar ../../antlr-4.13.2-complete.jar -Dlanguage=CSharp -visitor -no-listener -o Generated Cascode.g4
```

You must `cd` into `tools/language` and pass a relative grammar path. ANTLR embeds the source path in every generated file; using an absolute path leaks the developer's home directory into the repository.
Do not use the `-package` flag; the existing generated files use the global namespace.

## Spec/Documentation Writing Style

Avoid commonly overused AI motifs such as excessive use of bulleted lists and bolded text.
Use professional prose and use precisely the level of verbosity that is required to communicate the intent of the text.

Bold formatting should be reserved for technical terms being defined, critical warnings, or table headers requiring emphasis. Do not bold every subsection label, list lead-in, or organizational marker.

## Boundaries
- `tools/cli`: CLI only; may depend on `tools/workspace`, `tools/language`, `tools/bench`, `tools/render`. Nothing depends on CLI.
- `tools/workspace`: orchestration + persistence to `pdk.db`. No UI.
- `tools/language`: language implementation (grammar, AST, validation, linking, emission, bench semantics). Keep parsing/validation pure; no DB/network IO.
- `tools/bench`: harness discovery + testbench generation + simulator backends; no workspace DB.
- `tools/render`: rendering; depends on `tools/language`.
- No cycles or cross‑layer shortcuts.

## Hard Rules
- ≤400 added LOC per patch; split if larger.
- ≤500 LOC/file; ≤80 LOC/method (justify rare exceptions).
- Documentation note: these LOC limits apply to implementation code. Markdown documentation under `docs/` and `spec/` is exempt.
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
- Note: `dotnet csharpier format .` always reports files as formatted; use `git diff` to see actual changes.

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

## Cascode Versioning

- Canonical version: `tools/language/CascodeVersion.cs` (MAJOR.MINOR format)
- **Major bump**: breaking changes - reader rejects different majors
- **Minor bump**: additive-only changes - reader accepts any minor within same major
- On bump: run `scripts/bump_cascode_version.sh` to sync golden file headers
- On bump: inspect and update all Cascode versioning in unit/integration tests to be up to date with the latest features.
- NEVER add conditional parsing for different minors - unknown fields/syntax silently ignored

## Native NPM Versioning

- `@cascode/native` and all `editors/node/platform-packages/*/package.json` versions must match the git tag (`vX.Y.Z`) on release.
- Keep `editors/node/package.json` optional dependency versions in lockstep with that same version.
- The release workflow enforces this in `version-check`; do not bypass by editing workflow logic.
- If a new platform package is added, update all of:
  - `editors/node/platform-packages/<name>/package.json`
  - `editors/node/package.json` optionalDependencies
  - `editors/node/src/index.js` platform mapping
  - `.github/workflows/release.yml` publish matrix

## Anti‑Patterns
- Cross‑layer deps; IO in language core; UI outside CLI.
- God files/classes; silent behavior changes; “temporary” duplication.
- Flags that preserve old/new paths indefinitely.

## When Unsure
- Stop and write a design doc; prefer smaller patches; delete over deprecate.
