## Cascode CLI Architecture

Status: current as of 2025-10-08. Scope: `tools/cli` only.

Goal
- Provide an interactive and non-interactive CLI that delegates to domain libraries without embedding domain logic.

Responsibilities
- Startup, command routing, interactive loop, config, and logging.
- Aggregates services from `tools/workspace` and other libraries; renders via Spectre.Console.

Non-responsibilities
- PDK scanning, database writes/reads, characterization algorithms, or SPICE orchestration logic (owned by `tools/workspace` / other libs).

Assemblies & boundaries
- CLI may depend on: `tools/workspace`, `tools/parser`, logging/config helpers.
- Nothing may depend on the CLI. No UI types leak out of `tools/cli`.

Key components
- `CliHost` — process entry, dependency wiring, interactive loop. Creates `CommandContext` per invocation.
- `ICommandRegistry` — maps command name → `ICliCommand`.
- `ICliCommand` — metadata (name, aliases, description) + `Execute(CommandContext ctx)`.
- `ICommandModule` — groups and registers related commands (PDK, Bench, Build, System).
- `ShellState` — minimal UI/shared state (e.g., active workspace, recent results).
- `ShellRenderer` — Spectre.Console rendering; consumes view models produced by commands.
- Services — thin adapters over domain libraries, e.g., `WorkspaceService` using `WorkspaceScanner`, `PdkDatabaseReader/Writer`.

Primary flow
1) Parse invocation (argv or interactive input) into `CommandInvocation`.
2) Resolve handler via `ICommandRegistry`.
3) Build `CommandContext` (console, services, `ShellState`, cancellation, options).
4) Execute command; return `CommandResult` (status + payload/view models).
5) `ShellRenderer` updates panels in interactive mode; non-interactive prints structured output.

Command surface (stable entry points)
- System: `help`, `version`, `log`, `exit|quit`.
- PDK: `pdk scan`, `pdk devices`, `pdk device <name>`, `pdk set-dir <path>|--clear`, and characterization entry points `pdk char …` (delegates).
- Bench/Build modules may add `bench …`, `build …` commands (thin orchestration only).

Error handling & diagnostics
- Commands return typed results; errors include user-facing message + technical details (for logs).
- Logs go to the on-screen pane in TUI; non-interactive writes to stdout/stderr for tests.

Determinism & config
- Honor `CASCODE_SEED` for any randomized sampling.
- Use `HOME` override to keep config writable in sandboxes: `HOME=$(pwd) dotnet run --project tools/cli/Cascode.Cli.csproj`.

Testing
- Unit: option/argument binding, command routing, and small services (mocks for domain libs).
- Integration: run `dotnet run -- …` against `tests/fixtures/pdk/sky130`; verify stdout/golden outputs.
- Architecture: enforce “no deps on CLI from other tools”.

Extensibility
- New command = new `ICliCommand` with help/usage + tests.
- New domain area = new `ICommandModule` wiring multiple commands and using existing libraries.
- Keep handlers small; push domain work into services/libraries.

Open items
- Background jobs/async progress in interactive mode.
- Stable, machine-readable JSON output mode across commands.

