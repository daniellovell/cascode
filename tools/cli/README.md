Cascode CLI

Command-line interface for the Cascode project. Distributed either as a .NET global tool (`Cascode.Cli`) or prebuilt binaries (see GitHub Releases).

Usage
- `cascode --help`
- `cascode synth examples/AmpAuto.cas -o build/AmpAuto.cir`

Project
- Repository: https://github.com/daniellovell/cascode

Architecture
- The CLI now uses `CliHost` (formerly `CascodeShell`) as a thin orchestrator that wires the modular command system and manages the interactive loop.
- A transitional command API has been introduced under `tools/cli/Commands/`:
  - `ICliCommand` describes a command (path, description, visibility, aliases, handler).
  - `DelegateCliCommand` adapts existing handlers to the new shape without moving logic yet.
  - `ICommandModule` is the registration surface for future modules.
- `CommandRegistry` now supports `Register(ICliCommand)` and remains the lookup/dispatch engine.
- Extracted modules:
  - `SystemCommandModule` — help, version, home, log, quit
  - `PdkCommandModule` — `pdk *`, `pdk char *`, model catalog views
  - `BenchCommandModule` — `bench harness *`
  - `BuildCommandModule` — `build`
  - `CharacterizationCommandModule` — `char gen|read|export`
- Shared services in `tools/cli/Services/`:
  - `ModelSummaryHelpers` — class/detail summaries for model catalog
  - `PathUtils` — path normalization and `~` expansion
  - `CharIoHelpers` — CSV loading and sparkline helpers
  - `CharExportService` — derive metrics and write `derived.csv`
- Next steps (tracked in `CLI_REFACTOR.md`): add targeted unit tests for modules and services, and continue expanding CLI snapshots.

Developer Notes
- Build: `dotnet build tools/cli/Cascode.Cli.csproj`
- Interactive: `HOME=$(pwd) dotnet run --project tools/cli/Cascode.Cli.csproj`
- One-shot: append arguments after `--`, e.g. `dotnet run --project tools/cli/Cascode.Cli.csproj -- pdk scan tests/fixtures/pdk/sky130`
