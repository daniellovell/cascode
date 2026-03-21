Integration tests exercise the Cascode toolchain end-to-end. They live under `tests/integration/**`.
The CLI suite ([tests/integration/cli](./cli)) now includes two styles of coverage:

- Run-once invocations via `ProcessStartInfo` for commands that stream directly to stdout.
- Fully interactive Spectre.Console sessions driven through a pseudo-terminal harness.

### Interactive harness

Interactive scenarios use `Infrastructure/InteractiveCliSession`. The helper allocates a UNIX PTY (`openpty`), forks, and replaces the child process with the Cascode CLI (either the native binary or `dotnet run`, depending on what has been built). The parent side exposes:

- `SendLineAsync` to write commands as if typed at the prompt.
- `WaitForOutputAsync` to await arbitrary log or prompt conditions.
- `SendControlCAsync` for interrupt handling.
- `WaitForExitAsync` to join the shell cleanly.

The harness normalises environment variables (`HOME`, `TERM`, `COLUMNS`, `LINES`, etc.) so interactive output is deterministic between machines. It is currently implemented for Linux and macOS; tests are skipped elsewhere.

To add a new interactive regression test:

1. Reference `InteractiveCliCollection` so the shared fixture provides the repository root and serialises interactive runs.
2. Start a session with `InteractiveCliSession.Start(repoRoot)`.
3. Wait for the `cascode/>` prompt before issuing commands.
4. Use `WaitForOutputAsync` with precise predicates (include timeouts) to assert behaviour.
5. End the session with `SendControlCAsync`, wait for the prompt, then `exit` and `WaitForExitAsync`.
6. Call `session.MarkSuccess()` once the test completes normally so the harness does not emit a transcript dump.

The first interactive regression (`PdkScanInteractiveStreamingTests`) is marked `[Fact(Skip=…)]` because it documents the current bug (no progress appears while `pdk scan` runs). Remove the skip once the live refresh fix lands so the test enforces the behaviour going forward.

### Running the integration suite

From the repository root:

```
dotnet test tests/integration/cli/Cascode.Cli.IntegrationTests/Cascode.Cli.IntegrationTests.csproj
```

Interactive tests require a PTY-capable environment. When running locally ensure you are on Linux or macOS and the `libc` symbols used by `InteractiveCliSession` are available. In CI, run them in a job that provides an interactive shell (the default Ubuntu agents work).

### Maintenance notes

- Keep the harness limited to the test project; production code must remain unaware of its testing hooks.
- If the CLI prompt format changes, update the predicate helpers in the interactive tests and document the change here.
- When adding new tests, prefer targeted, short interactions to keep wall clock time low. Long-running scans should be guarded with tight timeouts and explicit interrupts.
