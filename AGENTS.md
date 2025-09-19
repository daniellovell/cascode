# Repository Guidelines

## Project Structure & Module Organization
This repository is bootstrapping the cascode toolchain. Keep the root lean: docs live in `docs/`, language references in `spec/`, canonical motif libraries under `lib/`, runnable examples in `examples/`, implementation code in `tools/cli` and `tools/parser`, and regression assets in `tests/`. If a folder is missing, create it following this layout. Place new `.cas` language features beside related motifs and include a minimal example under `examples/` that exercises the addition.

## Build, Test, and Development Commands
Use the preview CLI once built: `cascode synth examples/AmpAuto.cas -o build/AmpAuto.cir` converts a source spec into CasIR. `cascode verify build/AmpAuto.cir --spice spectre --pdk gpdk045` runs benches against a SPICE backend. `cascode run examples/AmpGuided.cas --pdk gpdk045 --out build/` performs the end-to-end flow. Keep build artifacts in `build/` and avoid committing them. Add convenience scripts under `tools/` if workflows expand.

## Coding Style & Naming Conventions
Follow the idioms shown in `README.md`: two-space indentation inside braces, trailing commas avoided. Use `UpperCamelCase` for classes and motifs (`DiffPairNMOS`), `lowerCamelCase` for ports and signals (`vinp`, `casOut`), and `snake_case` for file names in tooling code. Keep `.cas` files focused—one primary class per file—and include `package` declarations that mirror the directory path. Run your formatter or linter before opening a PR; hook scripts should target the CLI once available.

## Testing Guidelines
House conformance specs under `tests/conformance/` and golden CasIR snapshots under `tests/golden/`. Add a new example-driven test whenever a motif or synthesis rule changes. Run `cascode verify` on affected CasIR files and attach coverage notes (target: reproduce all benches touched by the change). Keep deterministic seeds in test configs so regressions are reproducible.

## Commit & Pull Request Guidelines
Write concise, present-tense summaries (e.g., `Add folded-cascode motif`). Use wrapped body lines at 72 characters to explain intent, risks, and follow-up work. Reference issues with `#id` and note affected examples. PRs should include: scope description, testing evidence (`cascode run ...` output excerpt), and any new artifacts or docs. Request a domain review (language, libraries, or tooling) when touching those areas.
