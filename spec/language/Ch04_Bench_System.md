# Chapter 4: Bench System

This chapter specifies Cascode’s declarative bench system. A bench defines a simulator-independent
measurement intent: what terminals are stimulated and observed, how the test circuit is constructed,
which analyses are run, and which measurements are produced. Circuits and interfaces bind benches
to their terminals, enabling bench reuse across topology variants.

The bench system replaces the legacy template-driven approach. Instead of selecting a backend-specific
template, benches are executable language constructs that drive emission, simulation, and
post-processing through typed measurement expressions.

---

## 4.0 Summary

A `bench` block contains:

1. Terminal declarations (`stim` / `resp`) that define the bench’s external connection points.
2. A `fill {}` block that constructs the test circuit (instruments, sources, loads, probes).
3. Optional helper `function` declarations (file-level or bench-local).
4. An `analysis {}` block declaring analyses (AC, DC, transient, noise, stability).
5. A `measurements {}` block defining typed measurement outputs.

Bench declarations typically live in libraries (for example, `lib/std/bench/*.cas`). A circuit or
interface selects and configures benches via `benches { ... }` bindings in circuits and interfaces.

---

## 4.1 Bench Declarations

### 4.1.1 Basic Structure

Benches are declared with a name and a body:

```cascode
bench DiffToSETransfer {
  stim IN : Diff
  resp OUT : analog

  fill {
    // Test circuit construction.
  }

  analysis {
    // Analysis declarations.
  }

  measurements {
    // Measurement definitions.
  }
}
```

The body ordering is:

- Zero or more terminal declarations.
- Optional `fill {}`.
- Zero or more `function` declarations.
- Optional `analysis {}`.
- Optional `measurements {}`.

### 4.1.2 Bench Parameters

A bench may declare parameters, each with an explicit physical type and an optional default:

```cascode
bench GainSweep(Frequency start = 1Hz, Frequency stop = 10GHz) {
  // ...
}
```

Bench parameters are compile-time values and are available in the bench scope alongside `env`,
`constraints`, and `harness` (see Section 4.1.3).

### 4.1.3 Scope and Availability

The following names are available throughout a bench:

- `env.<Name>`: resolved environment values from the bound circuit.
- `constraints.<Name>`: resolved constraint values from the bound circuit (absent/null if unconstrained).
- `harness.<Name>`: resolved harness values used by emission and bench execution.

Analyses declared in `analysis {}` are in scope inside `measurements {}`. Measurements are declared
using `measurement Name : Unit { ... }`; the declared unit determines the measurement’s required
return type and the units reported to downstream consumers.

---

## 4.2 Bench Terminals

### 4.2.1 Declaration Syntax

Bench terminals are declared with a role and a type:

```cascode
stim IN : Diff
resp OUT : analog
```

The declaration introduces a named terminal value into the bench scope. If the terminal type is a
bundle (for example, `Diff`), the terminal has a bundle structure and is addressed by field name
inside the bench (for example, `IN.P`, `IN.N`).

### 4.2.2 Roles: `stim` and `resp`

`stim` and `resp` express the intended role of a terminal in a bench. Many measurement primitives
operate on an explicit stimulus terminal and response terminal. For example, the standard transfer
measurement takes `(analysis, stim, resp)`:

```cascode
TransferFunction H = transfer(ac, IN, OUT)
```

Roles are part of the bench’s public contract and are used during bench binding validation. A bench
may declare multiple `stim` and/or `resp` terminals.

### 4.2.3 Terminal Types

Terminal types are either a built-in domain keyword or a user-defined bundle name.

Built-in terminal domains:

| Type | Meaning | Typical use |
|------|---------|-------------|
| `analog` | Scalar analog node | signal pins, single-ended outputs |
| `digital` | Scalar digital node | enables, mode pins |
| `mixed` | Mixed-signal node | ADC/DAC interfaces, mixed nets |
| `clock` | Clock node | clocked mixed-signal benches |
| `rf` | RF node | RF inputs/outputs |
| `bias` | Bias node | bias pins (currents/voltages) |
| `supply` | Supply rail | power rails (for PSRR or power benches) |
| `ground` | Return rail | explicit return terminal for power benches |

User-defined bundle types:

- Any identifier can name a bundle type (for example, `Diff`, `Quad`, `SupplyPair`).
- Role and binding operate on the bundle’s leaf terminals. For example, a `Diff` terminal introduces
  two leaves (`.P`, `.N`), each of which must be mapped during binding.

The special `ground` type is most commonly used as a return terminal when a bench needs to reason
about supply current (for example, `QuiescentPower`). Grounds within the test circuit itself are
usually modeled as internal nets connected via a `GND()` element inside `fill {}`.

### 4.2.4 `stim` / `resp` as Value Types

Within bench helper functions and measurement parameters, `stim` and `resp` may also appear as
parameter types to indicate that a value is a bench terminal:

```cascode
function calc_gain_bandwidth(ACAnalysis ac, stim IN, resp OUT) : Frequency {
  TransferFunction H = transfer(ac, IN, OUT)
  GainSpectrum G = db20(H.Mag())
  return G.FindCrossing(0dB, dir=falling, cross=1, from=ac.start, to=ac.stop)
}
```

This use of `stim`/`resp` is distinct from the domain types (`analog`, `digital`, etc.). It describes
the value category passed to measurement primitives (a terminal reference), not the electrical domain
of a terminal leaf.

---

## 4.3 Fill Blocks (Test Circuit Construction)

The `fill {}` block constructs the bench’s test circuit. It uses the same structural building
operations as a circuit `fill {}` block: declaring nets, instantiating circuits/devices, attaching
motifs, and wiring pins with the `--` operator.

In addition, benches commonly instantiate a small set of “harness primitives” such as voltage
sources and impedances. These primitives are recognized by the bench runtime and emitted as
simulator elements during `cascode emit`.

### 4.3.1 Common Fill Statements

Net declaration:

```cascode
net gnd : ground
net vcm : analog
```

Instance construction with a binding block:

```cascode
VAC ac = new VAC(A=0.5V, phase=0deg) { .N--vcm }
Impedor z = new Impedor(Z=env.SourceImpedance) { }
```

Connectivity using `--`:

```cascode
ac.P--z.P
z.N--IN.P
```

### 4.3.2 Harness Primitives

The following instance types are treated as harness primitives by the current toolchain.
Other instances are treated as normal structural instances in the test circuit.

| Primitive type | Parameters | Pins | Notes |
|----------------|------------|------|-------|
| `GND` | none | `.GND` | Ties a local ground net to simulator node 0 |
| `VDC` | `V=Voltage` | `.P`, `.N` | DC voltage source |
| `VAC` | `A=Voltage`, `phase=Phase` | `.P`, `.N` | Small-signal AC source (AC magnitude and phase) |
| `VSIN` | `DC=Voltage`, `A=Voltage`, `freq=Frequency`, `phase=Phase` | `.P`, `.N` | Time-domain sinusoidal source for transient benches |
| `Impedor` / `Impedance` | `Z=Impedance` | `.P`, `.N` | Impedance element; emits as R/C/L or a parallel combination |

Notes:

- `Impedor` and `Impedance` are synonymous at emission time; both normalize to the same harness
  element kind.
- An impedance value may be a numeric impedance, a numeric capacitance/inductance, or a parallel
  composite expressed with `||` (for example, `1GOhm || 15pF`).

### 4.3.3 Example: Differential AC Stimulus with Source/Load Impedances

The standard library’s transfer benches use `VAC` sources, `Impedor` source impedances, and an
output load impedance:

```cascode
fill {
  net vcm : analog
  net gnd : ground

  GND _ = new GND() { .GND--gnd }
  VDC commonModeVDC = new VDC(V=env.InputCommonModeRange) { .P--vcm, .N--gnd }

  VAC acP = new VAC(A=0.5V, phase=0deg) { .N--vcm }
  VAC acN = new VAC(A=0.5V, phase=180deg) { .N--vcm }

  Impedor sourceP = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }
  Impedor sourceN = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }

  acP.P--sourceP.P
  sourceP.N--IN.P

  acN.P--sourceN.P
  sourceN.N--IN.N

  Impedor loadZ = new Impedor(Z=env.LoadImpedance) { .P--OUT, .N--gnd }
}
```

---

## 4.4 Analysis Blocks

The `analysis {}` block declares typed analyses that the bench runtime executes during simulation.
Analyses are declared as variables initialized with `new`, with named parameters:

```cascode
analysis {
  ACAnalysis ac = new ACAnalysis(space=Log, samples=100, start=1Hz, stop=10GHz)
}
```

### 4.4.1 Supported Analysis Types

The current grammar defines the following analysis types:

| Type | Purpose |
|------|---------|
| `ACAnalysis` | Small-signal frequency sweep |
| `DCAnalysis` | Operating-point / DC sweep (backend-dependent) |
| `TranAnalysis` | Time-domain transient simulation |
| `NoiseAnalysis` | Noise analysis driven by an input source |
| `STBAnalysis` | Stability analysis (backend-dependent) |

Each analysis type has its own constructor parameters. Parameters are expressions, and may reference
`env`, `constraints`, and bench parameters. The analysis parameter value syntax also supports an
inline conditional expression:

```cascode
start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz })
```

### 4.4.2 Example: Constraint-Driven AC Sweep

```cascode
analysis {
  ACAnalysis ac = new ACAnalysis(
    space=Log,
    samples=100,
    start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
    stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }))
}
```

---

## 4.5 Measurements

The `measurements {}` block defines the outputs produced by a bench. Each measurement has:

- A name.
- An optional parameter list (each parameter has an explicit type and name).
- A declared output unit.
- A body consisting of typed local variable declarations, `if`/`else`, and `return`.

### 4.5.1 Declaration Syntax

```cascode
measurements {
  measurement PassbandGain : dB {
    TransferFunction H = transfer(ac, IN, OUT)
    GainSpectrum G = db20(H.Mag())
    return G.ValueAt(1kHz)
  }

  measurement IntegratedInputNoise(Frequency from, Frequency to) : Vrms {
    NoiseSpectrum n_in = input_referred_noise(noise_ac, ac, IN, OUT)
    return n_in.Integrate(from, to)
  }
}
```

The declared unit constrains the measurement’s return type (for example, `: dB` returns a
`VoltageRatio`-typed value in dB; `: Hz` returns `Frequency`; `: Vrms` returns integrated noise).

### 4.5.2 Statements and Control Flow

Measurement bodies support:

- Typed local variables: `Frequency f = 1kHz`
- `if` / `else` blocks: `if constraints.GainBandwidth { ... } else { ... }`
- Explicit `return` expressions

Multi-line expressions must be parenthesized, and conditional expressions inside argument lists use
the `(if ... { ... } else { ... })` form (shown in Section 4.4).

### 4.5.3 Calling Other Measurements

Within a bench, other measurements can be invoked as functions using call syntax:

```cascode
measurement BandpassBandwidth : Hz {
  return abs(LowpassBandwidth() - HighpassBandwidth())
}
```

If a measurement declares parameters, it must be invoked with matching arguments by name or by
position.

---

## 4.6 Measurement Value Types

Bench expressions are statically typed. Types fall into two broad categories:

- Physical scalar types such as `Frequency` or `Voltage`.
- Structured result types such as spectra, waveforms, and transfer functions.

### 4.6.1 Physical Scalar Types

The following scalar types are used throughout analyses, measurements, and bench parameters:

| Type | Dimension | Example literal |
|------|-----------|-----------------|
| `Frequency` | Hz | `10MHz` |
| `VoltageRatio` | linear or dB | `0dB`, `70dB` |
| `Voltage` | V | `1.2V` |
| `Current` | A | `50uA` |
| `Time` | s | `1ns` |
| `Phase` | deg | `60deg` |
| `Impedance` | Ω (and derived forms) | `50Ohm`, `1GOhm || 15pF` |
| `Capacitance` | F | `1pF` |
| `Inductance` | H | `10nH` |
| `Scalar` | unitless | `0.5` |

### 4.6.2 Structured Types

Common structured result types produced by measurement primitives:

| Type | Produced by | Notes |
|------|-------------|-------|
| `TransferFunction` | `transfer(ac, stim, resp)` | Complex frequency response |
| `GainSpectrum` | `TransferFunction.Mag()`, `db20(...)`, `db10(...)` | Magnitude vs frequency (linear or dB) |
| `PhaseSpectrum` | `TransferFunction.Phase()` | Phase vs frequency (degrees) |
| `NoiseSpectrum` | `noise(noise_analysis, node)`, `input_referred_noise(...)` | Noise density vs frequency |
| `VoltageSpectrum` | `voltage(ac, node)` | Complex voltage vs frequency |
| `CurrentSpectrum` | `current(ac, harness_pin)` | Current vs frequency (A) |
| `VoltageWaveform` | `voltage(tran, node)` | Voltage vs time (V) |
| `CurrentWaveform` | `current(tran, harness_pin)` | Current vs time (A) |

---

## 4.7 Measurement Primitives and Methods

### 4.7.1 Constructors and Conversions

Built-in functions construct or transform measurement values. The following table lists the most
commonly used primitives in the standard library.

| Function | Result | Notes |
|----------|--------|-------|
| `transfer(ac, stim, resp)` | `TransferFunction` | Computes the complex transfer `V(resp)/V(stim)` over an AC sweep |
| `voltage(analysis, terminal)` | `VoltageSpectrum` or `VoltageWaveform` | AC yields a spectrum; transient yields a waveform |
| `current(analysis, element_pin)` | `CurrentSpectrum` or `CurrentWaveform` | Reads current through a harness-injected source pin |
| `noise(noise_analysis, terminal)` | `NoiseSpectrum` | Output noise spectral density for the analysis output |
| `input_referred_noise(noise_analysis, ac_analysis, stim, resp)` | `NoiseSpectrum` | Divides output noise density by |transfer| |
| `db20(GainSpectrum)` | `GainSpectrum` | 20·log10(magnitude) |
| `db10(GainSpectrum)` | `GainSpectrum` | 10·log10(magnitude) |
| `quiescent_power(PWR, RET)` | `W` | Computes DC rail power from the applied supply source |
| `period(f)` | `Time` | Returns `1/f` |
| `abs(x)` | scalar type | Absolute value (numeric) |
| `sqrt(x)` | scalar type | Square root (numeric) |

`current(...)` requires a harness element pin reference such as `harness.VDD.P`. The bench runtime
maps `harness.<SupplyName>.P` / `.N` to the injected supply source that applies the rail.

### 4.7.2 Methods on Structured Values

Structured values expose methods for common post-processing operations.

Transfer function methods:

- `H.Mag()` → `GainSpectrum` (linear magnitude)
- `H.Phase()` → `PhaseSpectrum` (degrees)

Spectrum methods:

- `S.ValueAt(f)` → interpolated scalar at frequency `f`
- `S.FindCrossing(threshold, dir=falling|rising, cross=1, from=..., to=...)` → crossing frequency
- `S.Integrate(from, to)` → (noise spectra only) integrated RMS noise over a band

The standard library uses these methods to implement measurements such as gain-bandwidth and phase
margin (transfer benches) and spot/integrated noise (noise benches).

## 4.8 Bench Binding and Constraint References

Bench definitions are reusable. A circuit does not “contain” benches directly; instead, an interface
or circuit declares bench bindings that map bench terminals onto the circuit under test (the `dut`).
Constraints then reference measurements through the binding name.

### 4.8.1 `benches {}` Sections

`benches { ... }` blocks may appear inside `interface { ... }` declarations (bindings are inherited)
and inside `circuit { ... }` declarations (add or extend bindings).

An interface declares bindings using `bind`:

```cascode
interface SingleEndedOpAmp {
  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog

  benches {
    bind QuiescentPower as vdd_pwr {
      bench.PWR--dut.VDD
      bench.RET--dut.GND
    }

    bind DiffToSETransfer as transfer_bench {
      bench.IN--dut.IN
      bench.OUT--dut.OUT
    }
  }
}
```

Within a circuit, bindings can be added with `bind` or extended with `extend`:

```cascode
circuit MyOpAmp implements SingleEndedOpAmp {
  level EL
  // ...

  benches {
    extend transfer_bench {
      // Optional additional wiring or harness instances.
    }
  }
}
```

### 4.8.2 Binding Statements

A binding body contains zero or more binding statements:

| Statement kind | Form | Purpose |
|----------------|------|---------|
| Terminal mapping | `bench.<Terminal>--dut.<Pin>` | Maps a bench terminal onto a DUT terminal |
| DUT connection | `dut.<Pin>--<pinRef>` | Wires a DUT pin to a local net in the binding scope |
| Instance declaration | `name = new Type(...) { ... }` | Adds binding-scoped instances (for specialization) |

Mappings and connections use the same `--` wiring operator as `fill {}` blocks. `pinRef` supports
bundle field access and indices (for example, `IN.P`, `TAP[0]`).

### 4.8.3 Referencing Measurements from Constraints

Numeric constraints reference bench measurements via the binding name:

```cascode
constraints {
  numeric {
    c_gbw = transfer_bench::GainBandwidth at net::OUT >= 20MHz
    c_gain = transfer_bench::PassbandGain at net::OUT >= 40dB
    c_pm = transfer_bench::PhaseMargin at net::OUT >= 60deg

    c_int = noise_bench::IntegratedInputNoise(from=10Hz, to=10MHz) <= 1uVrms
  }
}
```

The general form is:

```
<binding>(<bench-args>)? :: <measurement>(<measurement-args>)?
```

Bench arguments specialize a parameterized bench binding. Measurement arguments invoke a parameterized
measurement within the selected bench.

### 4.8.4 Emission and Execution Model

Bench simulation is constraint-driven: benches are emitted and executed when at least one of their
measurements is referenced by a constraint. If any measurement from a bench is constrained, the whole
bench is simulated and all of its measurements are produced.
