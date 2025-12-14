## Cascode CLI Architecture

Status: current as of 2025-12-10. Scope: `tools/cli` only.

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
- Emit/ERC: `emit <acir_file> [--out <dir>] [--backend <ngspice|spectre>] [--json]`, `erc <acir_file> [--require-pdk] [--json]`.
- Bench/Build modules may add `bench …`, `build …` commands (thin orchestration only).

Error handling & diagnostics
- Commands return typed results; errors include user-facing message + technical details (for logs).
- Logs go to the on-screen pane in TUI; non-interactive writes to stdout/stderr for tests.

Determinism & config
- Honor `CASCODE_SEED` for any randomized sampling.
- Use `CASCODE_HOME` to keep state/config writable in sandboxes: `CASCODE_HOME=$(pwd)/.it/local dotnet run --project tools/cli/Cascode.Cli.csproj`.

PDK matching config
- `pdk scan` initializes YAML at `CASCODE_HOME/config/pdk-matching-patterns.yml` on first run and logs the path.
- Users edit this YAML to control normalization, class/subclass classification, and matching thresholds.
- We never migrate existing workspace databases; rerun `pdk scan` after changing YAML to regenerate `pdk.db` for that workspace.

Testing
- Unit: option/argument binding, command routing, and small services (mocks for domain libs).
- Integration: run `dotnet run -- …` against `tests/fixtures/pdk/sky130`; verify stdout/golden outputs.
- Architecture: enforce "no deps on CLI from other tools".

Extensibility
- New command = new `ICliCommand` with help/usage + tests.
- New domain area = new `ICommandModule` wiring multiple commands and using existing libraries.
- Keep handlers small; push domain work into services/libraries.

Open items
- Background jobs/async progress in interactive mode.

JSON output mode
- Commands supporting `--json` emit machine-readable JSON instead of human-readable text.
- Currently supported: `emit`, `erc`.
- Output schema for validation commands:

```json
{
  "success": true,
  "exitCode": 0,
  "errors": [{ "code": "ERC-001", "severity": "error", "message": "...", "location": "...", "suggestion": "..." }],
  "warnings": [...],
  "summary": { "errorCount": 0, "warningCount": 2 }
}
```

- The `emit` command extends this with `designPaths` and `testbenchPaths` arrays on success.
- Exit codes remain unchanged: 0 = success, 1 = validation failure, 2 = parse/structural error.

Live rendering and event model
- Interactive mode renders a single persistent Spectre.Console Layout inside `AnsiConsole.Live`. The layout instance is not recreated and the console is never cleared during a command.
- UI updates are event-driven, with a short periodic refresh for responsiveness. `ShellState` centralizes state and exposes `Changed` and `RequestRender()`; producers (logger via `ShellLoggerProvider.AddMessage`, long‑running task milestones) raise these to trigger a redraw. The Live loop also refreshes on a ~100 ms timeout to keep the prompt spinner responsive even when no logs are emitted.
- On `Changed` or timeout, only affected panels are rebuilt, then the Live context is refreshed:
  - Log panel: `ShellRenderer.BuildLog(state)`
  - Navigator panel: `ShellRenderer.BuildNavigator(state)`
  - Details panel: `ShellRenderer.BuildDeckDetails(state)`
  - Prompt row: `ShellRenderer.BuildPrompt(state)`
- The prompt row always remains visible. During long‑running work, `ShellState.StartBusy("…")/StopBusy()` dims the prompt and shows a lightweight text spinner (`ShellState.GetSpinnerFrame()`), advanced by the periodic refresh (not tied to log writes).
- Thread‑safety: log messages are appended under a lock; renderers read via `ShellState.GetMessagesSnapshot()` to avoid concurrent modification.
- Do not nest Spectre `Status`/`Progress` inside the TUI. These controls own the console and are not composed within the Live‑hosted layout. Use the event‑driven panel updates instead.
- Run‑once mode is unchanged and streams via SimpleConsole; the same logger feeds both modes.
