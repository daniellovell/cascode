# Chapter 3: Syntax Reference

This chapter is a syntax-oriented reference for the unified Cascode language. Cascode source files
use the `.cas` extension. Tool-linked intermediate artifacts use `.cai` (self-contained by default,
or include-pruned when requested by linker mode).

The authoritative grammar is `tools/language/Cascode.g4`. This chapter documents the surface syntax
as it is used throughout the standard library (`lib/std/**`), examples, and golden tests.

## Quick index

- File headers and top-level declarations: Section 3.1
- Names and pin references: Section 3.2
- Literals (numbers and quantities): Section 3.3
- Provenance: Section 3.4
- Bundles: Section 3.5
- Interfaces and connectors: Section 3.6
- Circuits and members: Section 3.7
- Primitives and devices: Sections 3.8–3.9
- Fill blocks and connectivity: Section 3.10
- Constraints, benches sections, environment, and harness (syntax): Section 3.11
- Bench helper functions (syntax): Section 3.12

## Syntax index

| Construct | Example | Section |
|----------|---------|---------|
| `VERSION` header | `VERSION 5.0` | 3.1.1 |
| `library` | `library lib.std.amp` | 3.1.2 |
| `include` | `include lib.std` | 3.1.3 |
| `wrap spice` | `wrap spice """...""" map { ... }` | 3.1.5 |
| `bundle` | `bundle Diff { P : analog }` | 3.5 |
| `interface` | `interface SingleEndedOpAmp { ... }` | 3.6 |
| `connectors` | `connectors { to X { A--B } }` | 3.6 |
| `bench` | `bench DiffToSETransfer { ... }` | Chapter 4 |
| `Port` (bench harness) | `Port p1 = new Port(N=1, Z=50Ohm, V=0V) { .P--P1, .N--gnd }` | Chapter 4 |
| `function` | `function f(...) : Frequency { ... }` | 3.12 |
| `primitive` | `primitive NMOS NMOS_Level1(size s) { ... }` | 3.8 |
| device instance | `NMOS M1 = new NMOS_Level1(S) { ... }` | 3.9 |
| `circuit` | `circuit OTA5T implements SingleEndedOpAmp { ... }` | 3.7 |
| `inline` | `inline` | 3.7 |
| `slot` | `slot` | 3.7 |
| `synth {}` | `synth { seed = 123 }` | 3.7 |
| `fill {}` | `fill { net n : analog  DiffPair dp = new DiffPair { ... } }` | 3.10 |
| `attach` | `attach cm to dp via A::B as name` | 3.10 |
| `benches {}` | `benches { bind X as y { ... } }` | 3.11 |
| `constraints {}` | `constraints { bench { c = lp::LowpassBandwidth >= 1MHz } }` | 3.11 |
| `env {}` | `env { LoadImpedance = 50Ohm }` | 3.11 |
| `harness {}` | `harness { supply VDD = 1.8V }` | 3.11 |

---

## 3.0 Summary

A Cascode file consists of an optional `VERSION` header followed by a sequence of top-level
declarations. The most common declarations are:

- `library ...` (file-level package metadata)
- `include ...` (dependency inclusion)
- `wrap spice """...""" map { ... }` (embed and map a SPICE subcircuit)
- `bundle ... { ... }`
- `interface ... { ... }`
- `bench ... { ... }`
- `function ... ( ... ) : <type> { ... }` (helper functions for measurements)
- `primitive ... { ... }`
- `circuit ... { ... }`

This chapter focuses on lexical structure and file-level syntax. The detailed syntax for types,
connectivity, primitives, circuits, and benches is specified in later sections and in the dedicated
chapters that introduce those subsystems.

---

## 3.1 File Headers and Top-Level Declarations

### 3.1.1 Version Header

Files may begin with a version header:

```cascode
VERSION 5.0
```

Source `.cas` files may omit the header, but tool-linked outputs are expected to include it.
The canonical version is defined in `tools/language/CascodeVersion.cs`.

### 3.1.2 File Package (`library`)

Files may include a package annotation:

```cascode
library lib.std.amp
```

`library` establishes a qualified name for the file’s contents.

### 3.1.3 Includes (`include`)

Dependencies are expressed with `include`:

```cascode
include lib.std
include lib.std.bench
include lib.pdk.sky130.devices.nfet_01v8
```

`include` names are qualified identifiers (dot-separated). The linker accepts both package-style
includes (for example `include lib.std` or `include lib.pdk.sky130`) and symbol-level includes
(for example `include lib.pdk.sky130.devices.nfet_01v8`).

By default, `cascode link` produces a self-contained `.cai` and resolves includes by materializing
required definitions into the output. With `--no-link-benches`, linking switches to an
include-preserving mode for bench dependencies: bench bindings remain intact, bench definitions are
omitted, and the output keeps a deterministic, pruned include set.

For stricter experiments, `cascode link --include-policy=explicit-only` limits symbol availability
to the explicit include closure (plus required transitive dependencies) and reports unresolved
symbols with actionable include suggestions.

### 3.1.4 Strings

Cascode supports:

- Normal strings: `"..."` (used for provenance and similar metadata)
- Triple-quoted strings: `"""..."""` (used for embedding multi-line text, such as SPICE content)

### 3.1.5 SPICE wrapping (`wrap spice`)

SPICE subcircuits can be embedded at the file level and mapped to Cascode terminal names:

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

`wrap spice` is a convenience for integrating existing simulator content into a structured Cascode
design without losing explicit terminal naming. The mapping associates Cascode-facing names with the
SPICE pin names of the embedded subcircuit.

### 3.1.6 Minimal complete file

The following minimal example shows the main structural forms in a single file:

```cascode
VERSION 5.0
library examples.rc

bundle Diff {
  P : analog
  N : analog
}

primitive Resistor IdealR(size s) { device "resistor" params { R = s.R } }
primitive Capacitor IdealC(size s) { device "capacitor" params { C = s.C } }

bench DiffToSELowpass {
  stim IN : Diff
  resp OUT : analog
  fill { net g0 : ground  GND g = new GND() { .GND--g0 }  IN.N--g0 }
  analysis { ACAnalysis ac = new ACAnalysis(space=Log, samples=200, start=1Hz, stop=1GHz) }
  measurements { measurement LowpassBandwidth : Hz { return 1Hz } }
}

circuit RcLowpass {
  level EL
  input IN : Diff
  output OUT : analog
  ground GND
  fill {
    Resistor R1 = new IdealR(size(R=1k)) { .P--IN.P, .N--OUT }
    Capacitor C1 = new IdealC(size(C=1p)) { .P--OUT, .N--GND }
  }
  benches { bind DiffToSELowpass as lp { bench.IN--dut.IN  bench.OUT--dut.OUT  dut.GND--g0 } }
  constraints { bench { c_fc = lp::LowpassBandwidth >= 50MHz } }
  harness { ground GND = 0V }
}
```

---

## 3.2 Names and Paths

### 3.2.1 Identifiers and Qualified Names

An identifier matches:

```
[A-Za-z_][A-Za-z0-9_]*
```

A qualified name is a dot-separated sequence of identifier parts:

```cascode
lib.std.bench
```

In several contexts (notably pin paths), Cascode permits keywords to appear as identifier parts so
that names like `load.Z` can be expressed without escaping.

### 3.2.2 Pin References

Pin references support field selection and indexing:

```cascode
OUT
IN.P
TAP[0]
dp.OUT.N
```

Pin references are used throughout `fill {}` connectivity, bench bindings (`bench.X--dut.Y`), and
constraint node scopes (`net::OUT`, `port::IN.P`).

---

## 3.3 Literals and Lexical Elements

### 3.3.1 Numbers

`NUMBER` tokens represent integer or floating-point values with optional scientific notation:

```cascode
1
3.14
1e-6
```

### 3.3.2 Quantities (Numbers with Units)

`QUANTITY` tokens represent a numeric value followed by an SI prefix and a unit string:

```cascode
1.8V
20MHz
50Ohm
15pF
60deg
0dB
1uVrms
```

The supported SI prefixes in quantity literals are:

| Prefix | Factor |
|--------|--------|
| `f` | 1e-15 |
| `p` | 1e-12 |
| `n` | 1e-9 |
| `u` | 1e-6 |
| `m` | 1e-3 |
| `k` | 1e3 |
| `M` | 1e6 |
| `G` | 1e9 |
| `T` | 1e12 |

The unit string is alphabetic (for example, `V`, `Hz`, `Ohm`, `F`, `deg`, `dB`, `Vrms`).

### 3.3.3 Comments and Whitespace

Line comments begin with `//` and extend to end-of-line. Whitespace is generally insignificant.
Newlines are not semantic in most constructs, but are used to disambiguate a leading `.` that begins
a terminal binding line inside `{ ... }` blocks.

---

## 3.4 Provenance (Source Attribution)

Cascode supports explicit provenance blocks for tracing generated or transformed content:

```cascode
provenance {
  source "examples/ota/OTA5T.cas" [120:1]
  transform "cascode link"
}
```

`source` records an origin file (and optionally a `[line:column]` location). `transform` records the
name of a transformation step. `alias` entries record identifier renames performed by transforms.

---

## 3.5 Bundle Declarations

Bundles define structured terminal types. A bundle is a named collection of fields, where each field
has a terminal type (`analog`, `supply`, another bundle name, etc.):

```cascode
bundle Diff {
  P : analog
  N : analog
}
```

Bundle field declarations do not carry direction; direction is introduced where the bundle is used
as a terminal type (for example, `input IN : Diff`).

---

## 3.6 Interface Declarations

Interfaces define a contract that circuits can implement. An interface may declare:

- `supply` and `ground` terminals
- `input` / `output` / `io` terminals
- `connectors { ... }` (structural connector mappings between interface views)
- `benches { ... }` (bench bindings inherited by implementers)

Example:

```cascode
interface SingleEndedOpAmp {
  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog

  connectors {
    to SomeOtherTrait {
      IN.P--INP
      IN.N--INN
    }
  }
}
```

Connector mappings use `--` between pin references. Pin references support field selection and array
indices (Section 3.2.2).

---

## 3.7 Circuit Declarations

Circuits are declared with the `circuit` keyword, an optional parameter signature, an optional
`implements` clause, and a brace-delimited body:

```cascode
circuit OTA5T(size InputPair, size Tail, int ratio = 1) implements SingleEndedOpAmp {
  level EL
  // ...
}
```

### 3.7.1 Parameters and Defaults

Circuit parameters appear in the signature. Supported parameter kinds are:

- `size <Name>` with an optional default `size(...)` expression
- `real`, `int`, and `bool` parameters with optional defaults

### 3.7.2 Body Members

Within the circuit body, the following members may appear (order is not significant):

- `level HL | ML | EL`
- `inline` (marks a circuit as eligible for inlining during emission / lowering)
- `library ...` (package metadata for the circuit)
- `supply <Name>` and `ground <Name>`
- `input` / `output` / `io` terminal declarations
- `env { ... }`, `fill { ... }`, `constraints { ... }`, `harness { ... }`, `benches { ... }`
- `synth { ... }` (synthesis guidance, typically extracted during linking)
- `provenance { ... }`

The detailed syntax for `fill {}` connectivity, primitive devices, bench bindings, and constraints
is specified in the remaining sections of this chapter and in the dedicated bench chapter.

### 3.7.3 Inline circuits (`inline`)

The `inline` keyword marks a circuit for inline expansion during SPICE emission:

```cascode
circuit BiasCell {
  inline
  level EL
  // ...
}
```

Inline circuits are expanded into their instantiating context rather than being emitted as separate
`.subckt` definitions.

### 3.7.4 Slots (`slot`)

Slots are high-level placeholders intended to be filled during synthesis.

A bare `slot` statement marks the circuit itself as a synthesis target. It is valid only at circuit
body level and implies that the circuit has no `fill` block — the entire implementation is to be
synthesized. The circuit's own `implements` clause and terminal declarations serve as the interface
contract:

```cascode
circuit MyOpAmp implements SingleEndedOpAmp {
  level HL
  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog
  input VTAIL : bias

  slot
}
```

If the sub-block graph and wiring are already known, the circuit is ML at that hierarchy and must
use `fill { ... }` instead. Structural composition is not expressed with `slot { ... }`.

### 3.7.5 Synthesis guidance (`synth {}`)

The `synth { ... }` block is a set of key/value entries that communicate synthesis guidance:

```cascode
circuit MyTop {
  level HL

  synth {
    seed = 123
    objective = minimize_power
    vdd = 1.8V
    notes = "do not use large capacitors"
  }
}
```

During linking, synthesis guidance is typically extracted into a sidecar `<name>.synth.yaml` file
and removed from the linked `.cai` output.

---

## 3.8 Primitive Declarations

Primitives define the mapping from a device category (NMOS, PMOS, passive devices, etc.) to a named
simulator/model implementation and its parameter mapping.

### 3.8.1 Syntax

```cascode
primitive NMOS NMOS_Level1(size primSize) {
  device "nmos_level1"
  params {
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }
}
```

The primitive header specifies:

- The device category keyword (`NMOS`, `PMOS`, `Resistor`, `Capacitor`, `Inductor`, `Diode`).
- A primitive name (identifier).
- A parameter list (currently the standard idiom is a single `size` pack parameter).

The body contains:

- `device "<string>"`: the backend/model identifier for emission.
- `params { ... }`: a mapping from model parameter names to Cascode expressions.

### 3.8.2 Size Packs

Size packs are constructed with the `size(...)` expression. Two forms are supported:

Key/value form:

```cascode
size S = size(W=2u, L=180n, M=1)
```

Positional form:

```cascode
size S = size(2u, 180n, 1)
```

Primitive parameter maps typically reference size fields using dotted access:

```cascode
primSize.W
primSize.L
primSize.M
```

Computed sizes are permitted via normal expressions (for example, `Sense.M*ratio`).

---

## 3.9 Device Declarations (Primitive Instances)

Devices are instantiated inside `fill {}` blocks using a device category keyword and a `new`
expression that names a primitive:

```cascode
fill {
  NMOS M_in = new NMOS_Level1(size(W=2u, L=180n, M=1)) {
    .D--OUT
    .G--IN
    .S--GND
    .B--GND
  }
}
```

The device category keyword (`NMOS`, `PMOS`, etc.) expresses the electrical class of the instance
and determines the legal terminal set for binding (`.D`, `.G`, `.S`, `.B` for MOSFETs, etc.).
The primitive name supplies the backend/model mapping through the corresponding `primitive` block.

The size argument may be:

- A named `size` variable: `new nfet_01v8(InputPair)`
- An inline size expression: `new NMOS_Level1(size(W=2u, L=180n, M=1))`

The binding block uses `.Terminal--Net` syntax and may be written with comma-separated bindings or
one binding per line.

---

## 3.10 Fill Blocks and Connectivity

`fill { ... }` blocks are the structural core of Cascode. They appear in circuits (to construct an
implementation), in benches (to construct a test circuit), and in bench bindings (to specialize a
bench binding).

### 3.10.1 Common Fill Statements

Fill blocks contain a sequence of statements. The most common forms are:

| Construct | Example | Meaning |
|----------|---------|---------|
| Net declaration | `net out : analog` | Declares a local net and its terminal type |
| Size declaration | `size S = size(W=2u, L=180n, M=1)` | Declares a reusable size pack |
| Instance declaration | `DiffPair dp = new DiffPair(...) { ... }` | Instantiates a circuit/bench primitive |
| Existential child request | `Some SensorConditioner frontend { ... }` | Requests some child circuit implementing the named interface |
| Device declaration | `NMOS M1 = new nfet_01v8(S) { ... }` | Instantiates a primitive-backed device |
| Attach | `attach cm to dp via TraitA::TraitB as name` | Applies connector-driven wiring overrides |
| Wire connection | `a--b` | Connects two pin references (joins nets) |

In addition, Cascode includes structural sugar constructs:

- `repeat <id> in [start:stop] { ... }`
- `match <id> { case <id>: { ... } ... }`
- `pair <id> { ... }`

These are intended to reduce boilerplate for repetitive or symmetric wiring patterns.

### 3.10.2 Instance Declarations

Instances use constructor syntax with `new`. Arguments are passed by name:

```cascode
DiffPair dp = new DiffPair(InputPair=size(W=2u, L=180n, M=1), hasTail=true) { ... }
```

Concrete instance declarations must include an explicit declared type, and it must match the
constructor type:

```cascode
VAC ac = new VAC(A=0.5V, phase=0deg) { .N--vcm }
```

### 3.10.3 The `Some` keyword

In ML `fill {}` blocks, the `Some` keyword declares an existential child request:

```cascode
fill {
  Some SensorConditioner frontend {
    .VDD--VDD
    .GND--GND
    .SENSOR_IN--SENSOR
    .SIGNAL_OUT--conditioned
  }
}
```

`Some` always carries an explicit interface name. It means "pick some circuit that implements this
interface." There is no constructor on the right-hand side, because the implementation is still
unresolved at ML.

`Some` is valid only in ML `fill {}` blocks. HL uses bare `slot`. EL must be fully concrete and may
not contain existential child requests.

### 3.10.4 Binding Blocks

Bindings connect instance terminals to nets. Each binding uses the `--` wire operator:

```cascode
DiffPair dp = new DiffPair(...) {
  .IN--IN
  .OUT.P--OUT
  .OUT.N--mirror_gate
}
```

Bindings may also be comma-separated:

```cascode
NMOS M1 = new nfet_01v8(S) { .D--OUT, .G--IN, .S--GND, .B--GND }
```

### 3.10.5 Wire Operator (`--`)

Outside of binding blocks, `--` expresses a direct connection between two pin references:

```cascode
acP.P--sourceP.P
sourceP.N--IN.P
```

The wire operator is also used in connectors (`connectors { to X { A--B } }`) and in bench bindings
(`bench.X--dut.Y`, `dut.X--net`).

---

## 3.11 Constraints, benches sections, environment, and harness (syntax)

Constraints are declared in a `constraints { ... }` block and are organized by verification method:
`bench {}`, `spec {}`, and `physical {}`.

```cascode
constraints {
  bench { c_gbw = transfer_bench::GainBandwidth at net::OUT >= 20MHz }
  spec { c_supply = adc.SupplyVoltage == 3.3V }
}
```

### 3.11.1 Constraint reference form

Informally, a scalar constraint has the shape:

```
<id> = <binding>(<bench-args>)? :: <measurement>(<measurement-args>)? (at <scope>::<pinRef>)? <op> <threshold>
```

Where:

- `<binding>` is a bench binding name introduced by `benches { bind ... as <binding> { ... } }`.
- `<bench-args>` specialize a parameterized bench.
- `<measurement>` is a measurement name declared inside the bench.
- `<measurement-args>` supply parameters for a parameterized measurement.
- `at <scope>::<pinRef>` attaches the constraint to a node reference such as `net::OUT` or `port::IN.P`.
- `<threshold>` is either a quantity literal (for example `20MHz`, `60deg`, `0dB`) or a bare scalar number (for example `1`, `-0.5`).

The grammar-level building blocks are:

```
benchMetricRef = IDENT ( "(" measurementArgList? ")" )? "::" IDENT ( "(" measurementArgList? ")" )?
nodeRef        = (IDENT | "net" | "port") "::" pinRef
numericConstraint = IDENT "=" benchMetricRef ("at" nodeRef)? COMPARISON_OP signedThreshold
signedThreshold  = signedQuantity | ["-"] NUMBER
```

Parameter and call forms are supported both at the bench and measurement level:

```cascode
constraints {
  bench {
    c_int = noise_bench::IntegratedInputNoise(from=10Hz, to=10MHz) <= 1uVrms
    c_swing = tran_bench(stim_freq=1kHz)::OutputSwing() at net::OUT >= 0.4V
  }
}
```

Bench bindings appear in `benches { ... }` blocks inside interfaces and circuits:

```cascode
benches {
  bind DiffToSETransfer as transfer_bench {
    bench.IN--dut.IN
    bench.OUT--dut.OUT
  }
}
```

Harness configuration is declared in a `harness { ... }` block:

```cascode
harness {
  supply VDD = 1.8V
  ground GND = 0V
  bias VTAIL = 0.6V             // pinned value
  bias VTAIL                     // unconstrained synthesis variable
  bias VTAIL in [0.3V:0.9V]     // bounded synthesis variable
  load OUT C=1pF
  sweep InputDCBias [0.3V:1.5V]
}
```

The bias statement grammar is:

```
biasStatement = "bias" IDENT ( "=" quantity | "in" "[" quantity ":" quantity "]" )?
```

All terminals declared on the circuit MUST appear in the harness. Bare and bounded bias forms must
be resolved to concrete values before EL emission.

### 3.11.2 Environment (`env {}`)

Environment values are declared as name/value assignments:

```cascode
env {
  LoadImpedance = 50Ohm || 1pF
  SourceImpedance = 50Ohm
  TempC = 27
}
```

The grammar supports an impedance expression form for `Z || C` style values, as used by standard
benches. See Chapter 2 for the intent-level distinction between `env` and `harness`.

---

## 3.12 Helper Functions (Bench Expressions)

Cascode supports helper `function` declarations at file scope and inside `bench { ... }` bodies.
Functions are intended for measurement expressions and analysis post-processing.

```cascode
function calc_gain_bandwidth(ACAnalysis ac, stim IN, resp OUT) : Frequency {
  TransferFunction H = transfer(ac, IN, OUT)
  GainSpectrum G = db20(H.Mag())
  return G.FindCrossing(0dB, dir=falling, cross=1, from=ac.start, to=ac.stop)
}
```

Parameter types may be physical types (such as `Frequency`), analysis types (such as `ACAnalysis`),
or terminal roles (`stim`, `resp`). A function body consists of variable declarations,
`if/else`, and a `return` statement.
