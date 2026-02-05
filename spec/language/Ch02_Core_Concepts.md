# Chapter 2: Core Concepts

> This chapter defines the semantic scaffolding of cascode: the building blocks, how they relate,
> and the invariants the compiler and tools rely upon. Syntax is shown informally; the formal grammar
> appears in Chapter 3. Normative keywords MUST, MUST NOT, SHOULD, and MAY follow
> [RFC 2119](https://www.ietf.org/rfc/rfc2119.txt).

---

## 2.1 Programs, Packages, and Includes

A program comprises one or more `.cas` files organized under a library namespace. The `library`
declaration establishes a namespace, while `include` directives bring in dependencies to be resolved
during linking. Name resolution follows lexical scoping with library-qualified fallback, and
shadowing rules are conventional unless explicitly stated.

Linked outputs (`.cai`) are self-contained and include a `VERSION` header as the first line.

```cascode
VERSION 3.0
library lib.std.amp
include lib.std.bench
```

Includes are resolved during linking. The output of linking is a self-contained `.cai` file with no
remaining `include` directives.

Linking also applies namespace inheritance rules for library lookups: a file in `lib.std.bench` can
resolve symbols from `lib.std` and `lib` without requiring explicit includes for those parent
namespaces. Explicit `include` directives are still the primary way to communicate dependencies.

---

## 2.2 Circuits, Interfaces, Benches, and Primitives

The unified language uses a small set of top-level declarations:

- `bundle`: a structured terminal type.
- `interface`: a contract that circuits implement; may include connectors and bench bindings.
- `circuit`: the primary design unit.
- `bench`: a declarative measurement intent (Chapter 4).
- `primitive`: a mapping from device category to an emittable backend/model binding.

Circuits declare explicit elaboration level (`level HL|ML|EL`). SPICE emission requires EL-level
circuits.

---

## 2.3 Terminals, Domains, and Bundles

Cascode represents connectivity through terminals and nets. Terminals have types (domains or bundle
types), and connections join terminals onto nets deterministically.

### 2.3.1 Terminal Types

The built-in terminal domains are:

| Type | Meaning |
|------|---------|
| `analog` | scalar analog node |
| `digital` | scalar digital node |
| `mixed` | mixed-signal node |
| `clock` | clock node |
| `rf` | RF node |
| `bias` | bias/control node |
| `supply` | supply rail |
| `ground` | return rail |

User-defined bundles provide structured terminal types. A bundle is a named collection of fields:

```cascode
bundle Diff {
  P : analog
  N : analog
}
```

Bundles are used as terminal types on circuits and interfaces (for example, `input IN : Diff`). They
are also used as bench terminals (for example, `stim IN : Diff`).

### 2.3.2 Directionality and Roles

Direction is part of the terminal declaration where the terminal is used:

- Interfaces and circuits declare `input`, `output`, or `io` terminals.
- Bench terminals declare `stim` or `resp` roles.

Bundle field declarations do not carry direction.

---

## 2.4 Interfaces, Connectors, and Attach

An `interface` is a contract that circuits can implement. Interfaces typically declare:

- a set of terminals (`supply`, `ground`, `input`, `output`, `io`)
- optional connector mappings (`connectors { ... }`)
- optional bench bindings (`benches { ... }`)

Connector mappings define how two interface views relate structurally. They are expressed using the
same pin-reference and wire syntax as fill blocks:

```cascode
connectors {
  to DiffPairLike {
    SENSE--OUT.N
    TAP[0]--OUT.P
  }
}
```

Connectors enable `attach` statements in `fill {}` blocks to be expanded deterministically into
explicit wiring.

### Attach

`attach` applies a connector-defined mapping between instances. The syntax names the connector path
explicitly:

```cascode
attach cm to dp via CurrentMirrorLike::DiffPairLike as mirror_node
```

`attach` may chain multiple targets (`attach a to b to c via ...`). When provided, an `as <name>`
identifier names the resulting mapping node for later reference.

Connector application is intended to be mechanical and deterministic: attaching two instances applies
the connector mapping declared by the interface named on the `via` path. If no connector is declared
for that interface pairing, the program is ill-formed.

When connector mappings involve bundles (for example, `Diff` terminals), the mapping is understood at
the level of leaf terminals (such as `.P` and `.N`). As a result, bench bindings and attach mappings
must cover the full leaf set of any mapped bundle terminal.

---

## 2.5 Circuits, `fill {}`, and Explicit Connectivity

The primary design unit is `circuit`. A circuit may implement one or more interfaces and includes an
explicit elaboration level:

```cascode
circuit OTA5T implements SingleEndedOpAmp {
  level EL
  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog

  fill { /* implementation */ }
}
```

Cascode uses three elaboration levels:

- HL (high level): may include unresolved structure (for example, synthesis guidance).
- ML (mid level): structure is concrete but may contain symbolic or technology-independent sizing.
- EL (electrical level): emission-ready; device choices and numeric values are sufficient for SPICE.

SPICE emission requires EL-level circuits. The toolchain’s compilation stages are structured around
linking (dependency resolution), synthesis (HL/ML → EL), and emission.

---

### 2.5.1 `fill {}` blocks

`fill {}` blocks define structure: nets, instances, devices, and connections.

#### Instances and bindings

Instances use constructor syntax with `new`. A binding block maps instance terminals to nets using
`.Terminal--Net`:

```cascode
fill {
  dp = new DiffPair_Pdk(InputPair=size(W=2u, L=180n, M=1)) {
    .IN--IN
    .OUT.P--OUT
    .OUT.N--OUT_N
  }
}
```

Bindings can be written one-per-line or comma-separated.

#### Connectivity and the wire operator

The wire operator `--` connects two pin references. It is used:

- inside binding blocks (`.OUT--net`)
- as a standalone connection (`a--b`)
- in connectors (`A--B`)
- in bench bindings (`bench.X--dut.Y`, `dut.X--net`)

Pin references support dotted field selection and array indices (`OUT.P`, `TAP[0]`).

#### Explicitness (normative)

Connectivity must be explicit. The toolchain must not infer wiring “by name” or “by role”. Bundle
connections must bind all leaf terminals; partial bundle binding is an error.

### 2.5.2 Structural sugar

Fill blocks support small pieces of sugar to reduce boilerplate:

- `repeat idx in [start:stop] { ... }` clones structural statements across an index range.
- `match name { case X: { ... } ... }` selects a structural variant.
- `pair name { ... }` declares a paired/symmetric scope for wiring patterns.

### 2.5.3 Parameters and defaults

Circuits may declare parameters in their signature. Parameters are either:

- `size` packs (commonly used for device sizing), or
- scalar parameters (`real`, `int`, `bool`).

Defaults are permitted for both:

```cascode
circuit CurrentMirror(size Sense = size(W=2u, L=180n, M=1), int ratio = 1) {
  level EL
  // ...
}
```

### 2.5.4 `inline` circuits (reusable structural blocks)

Cascode supports `inline` circuits as a way to define small structural blocks that expand into their
parent during emission. This is the common mechanism for expressing reusable structural blocks
without introducing a separate declaration kind.

```cascode
circuit BiasCell {
  inline
  level EL
  supply VDD
  ground GND
  output VB : analog

  fill { /* small structural block */ }
}
```

When emitting SPICE, inline circuits do not produce standalone `.subckt` definitions. Instead, the
emitter expands their contents into the instantiating context and applies deterministic naming to
avoid collisions.

Inline affects emission and bench node resolution; it does not change the basic semantic model of
circuits, terminals, and wiring.

### 2.5.5 Slots (synthesis placeholders)

Cascode uses `slot` declarations as explicit markers for structure that synthesis must fill.
There are two distinct forms, each suited to a different authoring pattern.

#### Bare slot (circuit as synthesis target)

When a circuit itself is the synthesis target, its interface contract is already declared via
`implements`, and all its terminals are declared in the circuit header. A bare `slot` statement
marks the circuit's implementation as a synthesis placeholder without repeating the interface or
writing identity bindings:

```cascode
circuit MyOpAmp implements SingleEndedOpAmp {
  level HL
  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog
  input VTAIL : bias

  slot

  env {
    InputCommonModeRange = 0.9V
    SourceImpedance = 50Ohm
    LoadImpedance = (1GOhm || 15pF)
  }

  constraints {
    numeric {
      c_gbw  = transfer_bench::GainBandwidth >= 20MHz
      c_gain = transfer_bench::PassbandGain >= 40dB
      c_pm   = transfer_bench::PhaseMargin >= 60deg
    }
  }

  harness {
    supply VDD = 1.8V
    ground GND = 0V
    bias VTAIL in [0.3V:0.9V]
  }

  synth { seed = 123 }
}
```

The circuit's terminal declarations, constraints, environment, and harness collectively form the
synthesis specification. The bare `slot` tells the toolchain that no concrete `fill` block exists
and that `cascode syn` must produce one. Bare `slot` is valid only at circuit body level (not
inside `fill` blocks).

#### Composition slot (sub-block placeholders inside fill)

When a circuit has partial concrete structure and needs synthesis for specific sub-blocks, named
slots go inside the `fill` block. Each composition slot declares an interface contract and
terminal bindings that map the slot's ports into the parent's wiring namespace:

```cascode
circuit MyReceiver {
  level HL
  input RF_IN : analog
  output BB_OUT : Diff

  fill {
    net mid : analog
    slot lna implements LNA {
      .IN--RF_IN
      .OUT--mid
    }
    slot mixer implements Mixer {
      .RF--mid
      .BB--BB_OUT
    }
  }
}
```

Here the bindings carry real information: they wire terminals across different interface contracts
and different naming conventions. A composition slot body contains optional `param` assignments and
binding statements written with the standard `--` wiring operator.

The synthesis stage (`cascode syn`) is responsible for replacing all slots with concrete structure
to produce an EL circuit.

---

## 2.6 Primitives and Devices

Primitives separate device category (NMOS/PMOS/etc.) from the concrete backend model name and its
parameter mapping.

```cascode
primitive NMOS nfet_01v8(size primSize) {
  device "sky130_fd_pr__nfet_01v8"
  params {
    w = primSize.W
    l = primSize.L
    mult = primSize.M
  }
}
```

Devices are instantiated in `fill {}` blocks using a device category keyword and `new` with a
primitive name:

```cascode
fill {
  NMOS M1 = new nfet_01v8(size(W=2u, L=180n, M=1)) { .D--OUT, .G--IN, .S--GND, .B--GND }
}
```

Size packs (`size(...)`) support computed expressions (for example, `size(S.W, S.L, S.M*ratio)`).

### 2.6.1 Passive devices

The language also supports passive device categories (`Resistor`, `Capacitor`, `Inductor`, `Diode`)
through the same primitive and device instantiation mechanism. A common pattern is to define
“ideal” primitives for use in small circuits and testbenches (for example,
`tests/golden/cas/bench/RcLowpass.el.cai`).

---

## 2.7 Constraints, Harness, and Environment

Cascode distinguishes three closely related sources of intent:

- `constraints { ... }` declares requirements in a tool-checked form.
- `env { ... }` specifies values that benches consume as configuration (for example, `LoadImpedance`).
- `harness { ... }` declares the concrete simulation harness for emission (supply values, biases,
  loads/sources, sweeps, and PVT corners).

`env` values are available to benches through the `env.<Name>` scope. Resolved constraint values are
available through `constraints.<Name>`. Harness values are available through `harness.<Name>`.

### 2.7.1 Numeric constraints

```cascode
constraints {
  numeric {
    c_gbw = transfer_bench::GainBandwidth at net::OUT >= 20MHz
  }
}
```

Numeric constraints compare a referenced measurement against a typed quantity literal. Constraints
may optionally include a node scope such as `net::OUT` or `port::IN.P`.

### 2.7.2 Harness syntax (overview)

The `harness { ... }` block declares simulator harness elements and swept conditions for emission.
It is intentionally explicit: values in `harness` are treated as concrete simulation configuration,
not as requirements. Every terminal declared on the circuit MUST appear in the harness block; the
linker rejects circuits with undeclared terminals. This prevents accidental omission from being
silently treated as a synthesis degree of freedom.

Common statements include:

```cascode
harness {
  supply VDD = 1.8V
  ground GND = 0V

  bias VTAIL = 0.6V           // pinned: use this value for simulation
  bias VTAIL                   // unconstrained: synthesis determines value
  bias VTAIL in [0.3V:0.9V]   // bounded: synthesis explores within range

  load OUT C=1pF
  source IN Z=50Ohm

  sweep InputDCBias [0.3V:1.5V]
  // Or request synthesis/linking to choose an execution range:
  // sweep InputDCBias [Auto]

  pvt tt
}
```

Bias terminals support three forms. A pinned bias (`= <quantity>`) provides a concrete value for
simulation. A bare bias (no value) declares the terminal as an unconstrained synthesis degree of
freedom whose value is determined during optimization. A bounded bias (`in [<low>:<high>]`) gives
the synthesis tool a continuous range to explore. Both bare and bounded forms must be resolved
before EL emission; the emitter rejects unresolved bias values.

`[Auto]` sweeps must likewise be resolved before EL emission. EL documents that still contain
`[Auto]` sweep specifications or unresolved bias values are rejected by the emitter.

### 2.7.3 Environment vs harness (intent vs execution)

`env { ... }` provides inputs to benches and analysis configuration. It is the right place to record
values such as `SourceImpedance`, `LoadImpedance`, and common-mode ranges that are consumed by standard
benches (for example, transfer and noise benches in `lib/std/bench`).

`harness { ... }` provides emission-time configuration such as supply values, applied biases, and
explicit sweep points. In general, `env` describes what the bench assumes and `harness` describes what
is simulated.

### 2.7.4 Determinism and golden assets

The toolchain is designed to produce deterministic outputs suitable for golden testing. Constructs
that expand (connector-driven attach, `repeat`, `match`, and `pair`) must elaborate in a stable order.
When emitting textual artifacts, ordering should be deterministic so that meaningful diffs capture
semantic changes rather than incidental reordering.

---

## 2.8 Benches and bindings (overview)

Benches define simulator-independent measurement intent (terminals, fill, analyses, measurements).
Interfaces and circuits bind benches to a circuit under test using `benches { ... }` blocks. A
binding assigns a stable name to the bench mapping:

```cascode
benches {
  bind DiffToSETransfer as transfer_bench {
    bench.IN--dut.IN
    bench.OUT--dut.OUT
  }
}
```

Bench simulation is constraint-driven: benches are emitted and executed when at least one of their
measurements is referenced by a constraint. The complete declarative bench system is specified in
Chapter 4.

---

## 2.9 Units and Dimensions

Literals may specify units including voltage (`V`), current (`A`), capacitance (`F`), inductance
(`H`), frequency (`Hz`), gain (`dB`), phase (`deg`), time (`s`, `ns`, `ps`), power (`W`), and noise
quantities (`nV/rtHz`, `1uVrms`). The compiler enforces dimensional consistency across expressions
used in constraints, analyses, and bench measurements.

```cascode
harness { supply VDD = 1.8V }
constraints { numeric { c_pm = transfer_bench::PhaseMargin >= 60deg } }
```

---

## 2.10 Provenance

Provenance blocks record source attribution and transformation steps:

```cascode
provenance {
  source "examples/ota/OTA5T.cas" [120:1]
  transform "cascode link"
}
```

Provenance is intended to make diagnostics and golden artifacts explainable without relying on
external build context.

---

## 2.11 SPICE interoperability (`wrap spice`)

Cascode can embed a raw SPICE subcircuit and map its pins to Cascode terminal names using `wrap spice`:

```cascode
wrap spice """
.subckt MY_BLOCK in out vdd gnd
* ... SPICE content ...
.ends
""" map {
  IN = in
  OUT = out
  VDD = vdd
  GND = gnd
}
```

`wrap spice` is intended to make existing SPICE blocks usable in a structured Cascode design without
losing explicit terminal naming. The mapping is a simple name association; semantic correctness
(pin directionality, rail legality, and so on) is validated by the normal language rules applied to
the resulting terminals.

---

## 2.12 Synthesis and physical design (overview)

### 2.12.1 Synthesis markers and guidance

A circuit becomes a synthesis target when it contains one or more `slot` declarations (see
Section 2.5.5). A bare `slot` at circuit body level marks the entire circuit for synthesis; named
slots inside `fill` blocks mark individual sub-blocks.

The `synth { ... }` block encodes synthesis guidance (search space, preferences, and other
directives) rather than directly affecting circuit connectivity. A circuit may have both a `slot`
and a `synth` block: the slot declares that synthesis is needed, and the `synth` block provides
guidance for how it should proceed.

During linking, synthesis guidance is extracted into a sidecar `<name>.synth.yaml` file and removed
from the linked `.cai` output. This keeps linked artifacts purely declarative and makes synthesis a
separate, explicit stage.

### 2.12.2 `cascode syn`

`cascode syn` consumes linked designs (`.hl.cai` / `.ml.cai`) plus guidance (`.synth.yaml`) and
produces EL outputs (`.el.cai`). During this stage every slot is replaced with concrete structure,
and any unresolved harness values (bare or bounded bias entries) are resolved to concrete quantities.
The details of topology selection and sizing are out of scope for this specification, but the stage
boundaries and file contracts are in scope.

### 2.12.3 `cascode par` and `.cal`

Physical design (place-and-route) is represented as a distinct stage. cascode reserves `.cal` for
Cascode Layout files. This specification does not define the `.cal` format; it only preserves the
extension and the intended separation between circuit semantics (`.cas` / `.cai`) and layout
semantics (`.cal`).

---

## 2.13 Bench type system quick reference

Bench expressions are statically typed. Types are intentionally semantic (physical quantities and
analysis products) rather than being generic numeric arrays.

### Scalar physical types

| Type | Examples |
|------|----------|
| `Frequency` | `10Hz`, `20MHz` |
| `Voltage` | `1.8V`, `10mV` |
| `Current` | `50uA`, `2mA` |
| `Time` | `1ns`, `10us` |
| `Phase` | `60deg`, `180deg` |
| `VoltageRatio` | `0dB`, `40dB` |
| `Impedance` | `50Ohm`, `1GOhm || 15pF` |
| `Capacitance` | `1pF`, `10fF` |
| `Inductance` | `10nH` |
| `Scalar` | `0.5`, `2` |

Common structured types include `TransferFunction`, `GainSpectrum`, `PhaseSpectrum`, `NoiseSpectrum`,
`VoltageSpectrum`, `CurrentSpectrum`, `VoltageWaveform`, and `CurrentWaveform`.

### Structured types

| Type | How it is commonly produced |
|------|------------------------------|
| `TransferFunction` | `transfer(ac, stim, resp)` |
| `GainSpectrum` | `H.Mag()`, `db20(...)`, `db10(...)` |
| `PhaseSpectrum` | `H.Phase()` |
| `NoiseSpectrum` | `noise(noise_analysis, OUT)` and `input_referred_noise(...)` |
| `VoltageSpectrum` | `voltage(ac, OUT)` |
| `CurrentSpectrum` | `current(ac, harness.VDD.P)` |
| `VoltageWaveform` | `voltage(tran, OUT)` |
| `CurrentWaveform` | `current(tran, harness.VDD.P)` |

### Common built-ins and methods

Built-in constructors and conversions commonly used in the standard library include:

- `transfer(ac, stim, resp)` → `TransferFunction`
- `noise(noise_analysis, terminal)` → `NoiseSpectrum`
- `input_referred_noise(noise_analysis, ac_analysis, stim, resp)` → `NoiseSpectrum`
- `voltage(analysis, terminal)` → `VoltageSpectrum` or `VoltageWaveform`
- `current(analysis, harness_pin)` → `CurrentSpectrum` or `CurrentWaveform`
- `db20(GainSpectrum)` / `db10(GainSpectrum)` → `GainSpectrum` in dB
- `quiescent_power(PWR, RET)` → rail power (for power benches)

Common post-processing methods:

- `H.Mag()` and `H.Phase()` on `TransferFunction`
- `S.ValueAt(x)` and `S.FindCrossing(...)` on spectra
- `N.Integrate(from, to)` on `NoiseSpectrum` (returns integrated RMS noise)
