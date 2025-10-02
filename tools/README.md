# Tools Overview

## CLI Architecture
- `CliHost` (tools/cli/CliHost.cs) is the composition root. It wires services, registers command modules, and owns the interactive loop and dispatch.
- Command modules live under `tools/cli/Commands/`:
  - `SystemCommandModule` – help, version, home, log, quit
  - `PdkCommandModule` – workspace scanning, model catalog, PDK characterization entry points
  - `CharacterizationCommandModule` – `char` commands for bench generation, reading outputs, exporting derived metrics
  - `BenchCommandModule` – bench harness discovery and display
  - `BuildCommandModule` – placeholder for future build tooling
- Shared adapters reside in `tools/cli/Services/`:
  - `ModelSummaryHelpers` – summary/detail view models for Spectre model catalogs
  - `PathUtils` – repository-aware path normalization (`~`, relative paths)
  - `CharIoHelpers` – CSV parsing, column lookup, sparkline rendering for characterization data
  - `CharExportService` – derives metrics (gm/Id, ro, etc.) from raw characterization results
  - `ShellPrompt` – reusable interactive prompt handler invoked by `CliHost`
- `CommandRegistry` remains the central lookup/dispatch, but now accepts `ICliCommand` registrations to keep modules cohesive.

## Workspace & Bench Libraries
- `tools/workspace` encapsulates Cadence workspace scanning (`WorkspaceScanner`, `WorkspaceScanResult`). CLI modules call into this assembly directly or through thin adapters.
- `tools/bench` contains harness discovery, bench generation, and helper types shared by CLI characterization commands.

## Development Notes
- Build the CLI: `dotnet build tools/cli/Cascode.Cli.csproj`
- Run interactively: `HOME=$(pwd) dotnet run --project tools/cli/Cascode.Cli.csproj`
