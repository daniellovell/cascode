## Cascode CLI Architecture

Status: current as of 2026-03-15. Scope: [tools/cli](../../tools/cli).

Goal
- Provide interactive and run-once command execution while keeping language, bench, render, and workspace logic in the lower-layer assemblies.

Responsibilities
- Process startup, command registration, argument dispatch, interactive shell state, and user-facing rendering.
- Thin orchestration over the language, workspace, bench, and render libraries.
- Simulator installation and update UX.

Non-responsibilities
- Parsing, linking, validation, bench planning, rendering algorithms, PDK scanning, or database persistence logic.

Assemblies and boundaries
- [tools/cli/Cascode.Cli.csproj](../../tools/cli/Cascode.Cli.csproj) references:
  [tools/workspace](../../tools/workspace),
  [tools/bench](../../tools/bench),
  [tools/language](../../tools/language), and
  [tools/render](../../tools/render).
- Nothing depends on the CLI. UI types stay inside [tools/cli](../../tools/cli).

Key components
- [CliHost](../../tools/cli/CliHost.cs) is the composition root. It builds shell state, registers commands, and chooses interactive vs run-once execution.
- [CommandRegistry](../../tools/cli/CommandRegistry.cs) resolves tokenized input to registered commands.
- [ICommandModule](../../tools/cli/Commands/ICommandModule.cs) groups related command registrations.
- [ICliCommand](../../tools/cli/Commands/ICliCommand.cs) is the command contract used by the registry.
- [ShellState](../../tools/cli/ShellState.cs) stores active workspace state, logs, busy status, and view-model data for the TUI.
- [ShellRenderer](../../tools/cli/ShellRenderer.cs) renders the Spectre.Console layout from `ShellState`.
- Services under [tools/cli/Services](../../tools/cli/Services) adapt lower-layer APIs into CLI-facing workflows.

Command surface
- System: `help`, `home`, `quit`.
- Design flow: `convert`, `link`, `emit`, `erc`, `render`.
- Bench and verification: `bench run`, `verify`.
- Characterization: `char gen`, `char read`, `char export`.
- PDK workspace: `pdk scan`, `pdk devices`, `pdk device`, `pdk match`, `pdk set-dir`, `pdk emit primitives`.
- PDK characterization: `pdk char config`, `pdk char run`, `pdk char read`, `pdk char status`.
- Environment: `install ngspice`, `update`.

Primary flow
1. [Program.cs](../../tools/cli/Program.cs) creates `CliHost`.
2. `CliHost` registers command modules and initializes logging.
3. The registry resolves the user input to a handler.
4. The handler delegates domain work into the lower-layer assemblies or CLI services.
5. The CLI renders human-readable output or structured JSON, depending on the command.

Interactive shell
- Interactive mode renders one persistent Spectre.Console live layout instead of redrawing a fresh screen per command.
- `ShellState` raises render requests as logs and long-running work update the UI.
- Run-once mode uses the same command handlers but writes directly to the console.

Configuration and persistence
- `CASCODE_HOME` controls writable CLI state such as config, ngspice installs, and workspace-local metadata.
- PDK matching rules live at `CASCODE_HOME/config/pdk-matching-patterns.yml`.
- `pdk scan` regenerates the workspace database for the selected PDK root; the CLI does not migrate older `pdk.db` files.

Testing
- Unit coverage lives under [tests/unit/tools/cli](../../tests/unit/tools/cli).
- Integration coverage lives under [tests/integration/cli](../../tests/integration/cli) and exercises the live command surface against fixtures such as [tests/fixtures/pdk/sky130](../../tests/fixtures/pdk/sky130).
