# Chapter 1: Introduction

## 1.1 Purpose and Scope

cascode is a concise analog description language designed for mixed structural and measurement-driven
circuit design. The language empowers designers to express both intent (constraints and operating
environment) and structure (instances and connectivity) within a unified source file (`.cas`),
enabling deterministic linking and emission to simulator netlists.

The language addresses the needs of three primary constituencies. Analog and RF IC designers benefit
from being able to express explicit structure while keeping verification intent close to the design.
Library authors can contribute reusable building blocks, connectors, and standard benches. Automated
tooling benefits from a typed, canonical representation of connectivity, constraints, and provenance.

This specification defines the language surface, core semantics, and the artifacts required by the
toolchain. Detailed algorithms for topology selection and sizing are out of scope; their contracts
and interfaces are in scope.

---

## 1.2 Motivation

Analog and mixed-signal IP forms the foundation of high-performance systems, enabling critical
functions such as clocking, power management, sensing, and high-speed I/O. Despite this importance,
analog design automation significantly lags RTL flows in part because design intent is often captured
at inappropriate abstraction levels: GUI schematics and raw SPICE netlists obscure essential
structure, roles, and constraints.

cascode addresses this representation gap by making intent first-class (constraints, environment, and
benches) while preserving explicit structure (typed terminals, named instances, and deterministic
connectivity). This enables tool-assisted verification, reuse, and diagnostics without relying on
implicit conventions.

---

## 1.3 Design Goals and Non-Goals

### Goals

The language design prioritizes:

- Mixed abstraction capability: structural construction with a clear path to verification.
- Syntactic clarity: constructor-style instantiation and explicit wiring with `--`.
- Type safety for physical quantities: literals such as `1.2V`, `2pF`, `100MHz`, `60deg`, `500uW`.
- Deterministic, diff-friendly text artifacts suitable for golden tests.
- Bench reuse through interface-defined bindings rather than backend templates.

### Non-Goals

cascode does not replace SPICE device models or simulator semantics. It does not specify synthesis
algorithms or optimization strategies, and it does not mandate a specific PDK format or simulator.

---

## 1.4 Source Artifacts and File Types

The cascode toolchain operates on three primary artifact types:

- `.cas`: cascode source (may contain `include` directives).
- `.cai`: linked cascode intermediate (includes a `VERSION` header; self-contained by default, or include-pruned in link bench-prune mode).
- simulator outputs: emitted SPICE netlists and bench testbenches (backend-specific).

In typical use, `cascode link` produces `.cai` outputs, and `cascode emit` consumes EL-level circuits
from either source `.cas` or linked `.cai`.

The `.cai` extension is intentionally distinct from simulator conventions. In particular, `.cir` is
widely used for SPICE netlists, and Cascode-linked artifacts are Cascode-shaped rather than SPICE.
Reserving a distinct extension reduces ambiguity and keeps `.cal` available for Cascode Layout files
in the long-horizon flow (Chapter 2 discusses the stage boundaries; the `.cal` format is specified
separately from this language surface).

---

## 1.5 Cascode in a Few Examples

The examples below use current repository conventions: connectivity is expressed with `--` and
instance bindings use `.Terminal--Net`.

### A minimal bench and binding

The following is excerpted and simplified from `tests/golden/cas/bench/RcLowpass.el.cai`.

```cascode
bench DiffToSELowpass {
  stim IN : Diff
  resp OUT : analog

  fill {
    net g0 : ground
    GND g = new GND() { .GND--g0 }
    VAC vp = new VAC(A=1, phase=0deg) { .P--IN.P, .N--g0 }
    IN.N--g0
  }

  analysis {
    ACAnalysis ac = new ACAnalysis(space=Log, samples=200, start=1Hz, stop=1GHz)
  }

  measurements {
    measurement LowpassBandwidth : Hz {
      TransferFunction H = transfer(ac, IN, OUT)
      GainSpectrum G = db20(H.Mag())
      return G.FindCrossing(-3dB, dir=falling, cross=1, from=ac.start, to=ac.stop)
    }
  }
}

circuit RcLowpass {
  level EL
  input IN : Diff
  output OUT : analog
  ground GND

  fill { /* R/C implementation */ }

  benches {
    bind DiffToSELowpass as lp {
      bench.IN--dut.IN
      bench.OUT--dut.OUT
      dut.GND--g0
    }
  }
}
```

### Constraints reference bench measurements by binding name

```cascode
constraints {
  bench {
    c_fc = lp::LowpassBandwidth >= 50MHz
  }
}
```

The declarative bench system and binding model are specified in Chapter 4.

### Primitives and connector-driven composition

Cascode supports explicit primitive-backed devices and connector-driven structural composition via
`attach`. The following excerpt illustrates `attach` in a hierarchical EL circuit:

```cascode
fill {
  DiffPair dp = new DiffPair(InputPair=size(W=2u, L=180n, M=1), Tail=size(W=4u, L=180n, M=1)) {
    .VDD--VDD
    .GND--GND
    .IN--IN
    .OUT.N--mirror_gate
    .OUT.P--OUT
    .TAIL--VTAIL
  }

  CurrentMirror cm = new CurrentMirror(Sense=size(W=2u, L=180n, M=1), ratio=1) {
    .VDD--VDD
    .GND--GND
    .SENSE--mirror_gate
    .TAP[0]--OUT
  }

  attach cm to dp via CurrentMirrorLike::DiffPairLike
}
```

The intent of `attach` is to make connector mappings explicit and reusable: the wiring implied by the
connector is part of the source, is expanded deterministically, and is visible to downstream tooling.

---

## 1.6 Toolchain Pipeline

Cascode’s long-horizon toolchain separates dependency resolution, synthesis, physical realization,
and verification into explicit stages. The precise algorithms for synthesis and place-and-route are
out of scope for this specification, but the contracts between stages are in scope.

### Linking (`cascode link`)

Linking resolves `include` directives and writes a `.cai` artifact. In the default mode, the output
is self-contained. In include-pruned mode (`--no-link-benches`), bench bindings are preserved but
bench definitions are omitted and represented through a minimal include closure. During linking,
`synth { ... }` blocks are extracted into a sidecar file and removed from the `.cai` output:

- output: `<name>.<level>.cai`
- optional sidecar: `<name>.synth.yaml`

### Synthesis (`cascode syn`)

Synthesis consumes linked inputs and produces EL-level outputs suitable for emission:

- input: `.hl.cai` or `.ml.cai`
- guidance: `.synth.yaml` (by convention or explicitly provided)
- output: `.el.cai`

Synthesis is responsible for topology selection and sizing. The synthesis interface is intended to
be deterministic given the same inputs and guidance.

### Place-and-route (`cascode par`)

Place-and-route consumes EL-level circuits and produces a physical layout representation. cascode
reserves the `.cal` extension for Cascode Layout files, but the `.cal` schema and physical design
semantics are specified separately from the language surface described in this document.

### Emission and Verification

`cascode emit` emits simulator netlists from EL circuits. Benches are executed in a
constraint-driven manner (`cascode bench run`), and numeric constraints are checked against results
(`cascode verify`).
