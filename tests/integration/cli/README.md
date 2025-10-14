## CLI Integration Tests

Target: non-interactive CLI flows (`dotnet run --project tools/cli/Cascode.Cli.csproj -- …`).

Planned suites
- Smoke: `pdk scan`, `pdk devices`, `pdk match` against `tests/fixtures/pdk/sky130` with deterministic env (`CASCODE_HOME=$(pwd)/.it/<run>`, `CASCODE_SEED`).
- Golden output comparisons stored under `tests/golden/cli/**`.

Implementation notes
- Use xUnit or a simple script harness; keep commands reproducible and fast.
- Normalize timestamps/paths before asserting to avoid churn.
