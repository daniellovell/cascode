# Tools Overview

## CLI Architecture

- `CliHost` (tools/cli/CliHost.cs) is the composition root. It wires services, registers command modules, and owns the interactive loop and dispatch.
- Command modules live under `tools/cli/Commands/`:
  - `SystemCommandModule` – help, version, home, log, quit
  - `PdkCommandModule` – workspace scanning, model catalog, PDK characterization entry points
  - `CharacterizationCommandModule` – `char` commands for bench generation, reading outputs, exporting derived metrics
  - `BenchCommandModule` – bench execution and results
  - `BuildCommandModule` – placeholder for future build tooling
- Shared adapters reside in `tools/cli/Services/`:
  - `DeviceSummaryHelpers` – summary/detail view state utilities for the device catalog
  - `PathUtils` – repository-aware path normalization (`~`, relative paths)
  - `CharIoHelpers` – CSV parsing, column lookup, sparkline rendering for characterization data
  - `CharExportService` – derives metrics (gm/Id, ro, etc.) from raw characterization results
  - `ShellPrompt` – reusable interactive prompt handler invoked by `CliHost`
- `CommandRegistry` remains the central lookup/dispatch, but now accepts `ICliCommand` registrations to keep modules cohesive.

## Workspace & Bench Libraries

- `tools/workspace` encapsulates Cadence workspace scanning (`WorkspaceScanner`, `WorkspaceScanResult`). CLI modules call into this assembly directly or through thin adapters.
- `tools/bench` contains shared bench helper types (e.g., backend enum, results model, value formatting).

## Development Notes

- Build the CLI: `dotnet build tools/cli/Cascode.Cli.csproj`
- Run interactively: `CASCODE_HOME=$(pwd)/.it/local dotnet run --project tools/cli/Cascode.Cli.csproj`
