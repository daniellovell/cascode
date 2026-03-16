## Tests Directory Guide

Structure (2025-10-08)

- `unit/tools/<area>/Cascode.<Area>.Tests/` — xUnit projects targeting a single runtime assembly.
  - Current areas in the repo include `acir`, `bench`, `cli`, `compiler`, `language`, `native`, `parser`, `render`, and `workspace`.
  - Add new tests beside the matching production project under `tools/<area>/` and keep them fast/pure.
- [tests/integration/cli](./integration/cli) — CLI integration suites that invoke the live command surface against fixtures such as [tests/fixtures/pdk/sky130](./fixtures/pdk/sky130).
- `golden/` — deterministic outputs compared by integration tests. Update intentionally and explain changes in PRs.
- `fixtures/` — shared inputs (e.g., `fixtures/pdk/sky130`).

Conventions
- One test project per production assembly; project name `Cascode.<Area>.Tests`.
- Reference the production project via `<ProjectReference>` and place helper fakes/mocks inside the test project.
- Keep unit tests deterministic and side-effect free; use integration tests when exercising SPICE/CLI flows.
- Prefer property-based tests for heuristics (e.g., NameNormalization) and golden comparisons for CLI output.

Adding a new test project
1. `dotnet new xunit -n Cascode.<Area>.Tests -o tests/unit/tools/<area>/Cascode.<Area>.Tests`
2. `dotnet add tests/unit/tools/<area>/Cascode.<Area>.Tests/Cascode.<Area>.Tests.csproj reference tools/<area>/Cascode.<Area>.csproj`
3. `dotnet sln Cascode.sln add tests/unit/tools/<area>/Cascode.<Area>.Tests/Cascode.<Area>.Tests.csproj`

See [AGENTS.md](../AGENTS.md) for patch-size limits, determinism rules, and required local commands.
