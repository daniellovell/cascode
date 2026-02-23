# Getting Started with Cascode

This guide is a practical entry point to the unified Cascode language and toolchain. It focuses on
what you can do today: describe circuits with explicit connectivity, bind reusable benches, and
check constraints against simulation results.

For the normative language rules, see `spec/language/README.md`. For practical authoring patterns
(style, cookbook, troubleshooting), see `docs/language/README.md`.

## Prerequisites

This repository can be exercised either with an installed `cascode` binary or by running the CLI
from source:

```sh
dotnet run --project tools/cli/Cascode.Cli.csproj -- --help
```

Bench execution typically uses `ngspice` by default; ensure it is available on your PATH if you plan
to run benches locally.

## Run a complete example (RC lowpass)

The repo includes a small, self-contained EL example with a declarative bench:
`tests/golden/cas/bench/RcLowpass.el.cai`.

To emit simulator netlists:

```sh
dotnet run --project tools/cli/Cascode.Cli.csproj -- emit tests/golden/cas/bench/RcLowpass.el.cai --out build/rclowpass
```

To run benches and capture results:

```sh
dotnet run --project tools/cli/Cascode.Cli.csproj -- bench run tests/golden/cas/bench/RcLowpass.el.cai --out build/rclowpass
```

To verify numeric constraints against the produced results:

```sh
dotnet run --project tools/cli/Cascode.Cli.csproj -- verify tests/golden/cas/bench/RcLowpass.el.cai build/rclowpass/results.json
```

`verify` evaluates every declared numeric constraint. If a constraint cannot be measured from the
provided results, it is reported as failed.

The key idea in this example is that the circuit, the bench, and the constraint reference all live
in one language with one syntax. The circuit’s `fill {}` block is just explicit connectivity:

```cascode
fill {
  Resistor R1 = new Ideal_Resistor(size(R=1k)) { .P--IN.P, .N--OUT }
  Capacitor C1 = new Ideal_Capacitor(size(C=1p)) { .P--OUT, .N--GND }
}
```

The bench is a declarative description of what to stimulate, what analyses to run, and what to
measure (Chapter 4). The circuit binds the bench to the DUT and gives the mapping a stable name:

```cascode
benches {
  bind DiffToSELowpass as lp {
    bench.IN--dut.IN
    bench.OUT--dut.OUT
    dut.GND--g0
  }
}

constraints {
  numeric { c_fc = lp::LowpassBandwidth >= 50MHz }
}
```

## Core concepts (mental model)

Cascode centers on a small set of constructs.

A `circuit` declares terminals and a `level` (HL/ML/EL). EL circuits are emission-ready for SPICE.
Structure lives in `fill {}` blocks as named instances and explicit wiring using `--` and
`.Terminal--Net` bindings.

A `bench` declares measurement intent. It has terminals (with `stim`/`resp` roles), optional
bench-local structure in `fill {}`, one or more analyses in `analysis {}`, and typed measurement
expressions in `measurements {}`.

Bench reuse is achieved through bindings: `benches { bind ... as ... { ... } }` maps a bench onto a
particular DUT. Constraints then reference measurements through the binding name using
`binding::Measurement`.

Simulation configuration is split between `env {}` (bench-facing assumptions) and `harness {}`
(concrete simulation setup for emission). This separation is deliberate: it keeps intent and
execution distinct while still allowing a single source file to describe both.

These concepts are expanded in:

- `spec/language/Ch02_Core_Concepts.md`
- `spec/language/Ch04_Bench_System.md`
- `docs/language/bench-cookbook.md`

## Toolchain pipeline (vision)

Today, the core stages are:

1. `cascode link`: resolve `include` directives and write a self-contained `.cai`.
   Use `--no-link-benches` when you want a smaller, include-pruned linked artifact for prompt workflows.
2. `cascode emit`: emit simulator netlists from EL circuits.
3. `cascode bench run` and `cascode verify`: execute and check constrained benches.

The long-horizon flow preserves explicit stage boundaries:

- `cascode syn` (synthesis) consumes `.hl/.ml.cai` plus guidance (conventionally extracted to
  `<name>.synth.yaml`) and produces `.el.cai`.
- `cascode par` (place-and-route) consumes `.el.cai` and produces physical layout artifacts.
  Cascode reserves `.cal` for Cascode Layout files; the `.cal` format is specified separately from
  the language surface described in `spec/language/`.

## Next steps

If you want to go deeper, the best entry points are:

- `docs/language/style.md` for repository conventions
- `docs/language/connectors.md` to understand `attach` and connector-driven hierarchy
- `docs/language/troubleshooting.md` for common errors and diagnostics
- `spec/language/Ch03_Syntax_Reference.md` for grammar-aligned syntax
