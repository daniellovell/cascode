# Chapter 4: Bench System

This chapter specifies Cascode’s declarative bench system. A bench defines a simulator-independent
measurement intent: what terminals are stimulated and observed, how the test circuit is constructed,
which analyses are run, and which measurements are produced. Circuits and interfaces bind benches
to their terminals, enabling bench reuse across topology variants.

The bench system replaces the legacy template-driven approach. Instead of selecting a backend-specific
template, benches are executable language constructs that drive emission, simulation, and
post-processing through typed measurement expressions.

Practical authoring patterns and worked examples are collected in the [bench cookbook](../../docs/language/bench-cookbook.md).

---

## 4.0 Summary

A `bench` block contains:

1. Terminal declarations (`stim` / `resp`) that define the bench’s external connection points.
2. A `fill {}` block that constructs the test circuit (instruments, sources, loads, probes).
3. Optional helper `function` declarations (file-level or bench-local).
4. An `analysis {}` block declaring analyses (AC, DC, transient, noise, stability, S-parameter).
5. A `measurements {}` block defining typed measurement outputs.

Bench declarations typically live in libraries (for example, [`lib/std/bench/*.cas`](../../lib/std/bench/)). A circuit or
interface selects and configures benches via `benches { ... }` bindings in circuits and interfaces.

### 4.0.1 Terminology

This chapter uses the following terms:

- **Bench definition**: a `bench Name { ... }` declaration. A bench definition describes a testbench
  topology (stimulus, load, analyses, and measurement computations) but is not connected to a
  particular circuit until it is bound.
- **Bench binding**: a `bind BenchName as binding_name { ... }` block inside an `interface` or
  `circuit`. A binding maps bench terminals onto a specific circuit under test and optionally adds
  binding-scoped wiring and harness primitives.
- **Bench instance**: the specialization of a bench binding with compile-time bench parameters,
  written as `binding_name(param=value, ...)`. Different bench instances produce separate emitted
  testbenches and separate result sets.
- **Harness primitive**: a special instance kind (for example `VAC`, `VDC`, `VSIN`, `Impedor`) that
  the bench runtime recognizes and emits as backend elements during `cascode emit`.

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
`constraints`, and `harness` (see [Section 4.1.3](#413-scope-and-availability)).

### 4.1.3 Scope and Availability

Benches and bindings execute in a shared testbench context. This section describes the names that are
available when authoring benches and bindings. (See [Chapter 2, Section 2.7](Ch02_Core_Concepts.md#27-constraints-harness-and-environment)
for the definitions of `env`, `constraints`, and `harness`.)

Within a bench definition:

- Bench parameters are available by name.
- Bench terminals declared with `stim`/`resp` are available by name (for example, `IN.P`).
- `env.<Name>`: resolved environment values from the bound circuit.
- `constraints.<Name>`: resolved constraint values from the bound circuit (absent if unconstrained).
- `harness.<Name>`: resolved harness values used by emission and bench execution.
- `dut.<Name>`: the circuit under test. `dut` exposes the DUT’s declared terminals and any named
  internal nets; benches may use this to probe internal nodes (for example,
  `voltage(tran, dut.mid)`).

Analyses declared in `analysis {}` are in scope inside `measurements {}`. Measurements are declared
using `measurement Name : Unit { ... }`; the declared unit determines the measurement’s required
return type and the units reported to downstream consumers.

Within a bench binding body:

- `bench.<Terminal>` refers to a bench terminal being mapped (for example, `bench.IN--dut.IN`).
- `dut.<Name>` refers to a DUT terminal or named internal net.
- Binding bodies may declare additional nets and instances and wire them. The binding elaborates into
  the same emitted testbench netlist as the bench’s `fill {}` block, so binding statements may
  reference nets created by either the bench or the binding.

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
about supply current (for example, [`QuiescentPower`](../../lib/std/bench/PowerBenches.cas)). Grounds within the test circuit itself are
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

### 4.2.5 Differential terminal semantics

Measurement primitives operate on *terminal values*. For scalar terminals (one leaf), the terminal’s
voltage is the node voltage at that leaf. For two-leaf terminals (for example, `Diff`), the terminal
is treated as a differential quantity \(V(P) - V(N)\).

This differential interpretation applies consistently to:

- `transfer(ac, stim, resp)` (both the stimulus and response terminals)
- `voltage(analysis, terminal)` for AC spectra and transient waveforms
- `NoiseAnalysis(..., output=terminal)` and `noise(noise, terminal)`

Example (differential response):

```cascode
bench DiffToDiffTransfer {
  stim IN : Diff
  resp OUT : Diff

  analysis { ACAnalysis ac = new ACAnalysis(space=Log, samples=100, start=1Hz, stop=10GHz) }

  measurements {
    measurement PassbandGain : dB {
      TransferFunction H = transfer(ac, IN, OUT)  // OUT is interpreted as V(OUT.P) - V(OUT.N)
      return db20(H.Mag()).ValueAt(1kHz)
    }
  }
}
```

For bundle types with more than two leaves, benches should reference the desired leaves explicitly
(for example, `OUT.P`).

### 4.2.6 Port harness primitives (S-parameter benches)

S-parameter reference planes are modeled as harness primitive instances in bench `fill {}`, not as
a terminal role. A port instance declares a positive integer index `N`, a reference impedance `Z`,
and a DC bias source value `V`:

```cascode
Port p1 = new Port(N=1, Z=50Ohm, V=0V) {
  .P--P1
  .N--gnd
}
```

`Port` is single-ended by definition. Port numbers must be unique and sequential starting at 1.
The index order in `S.S(i, j)` follows standard convention: response index first, excitation index
second.

S-parameter reference impedances are real-valued by convention. When the `Z` parameter resolves to
a parallel impedance expression (for example `1GOhm || 15pF`), the emitter extracts only the
resistive terms and discards any reactive components (capacitance or inductance). A purely reactive
impedance with no resistive term produces `z0=0`, which is invalid for simulator RF ports.

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

The following instance types are treated as harness primitives by the current toolchain (see
[Section 4.0.1](#401-terminology) for the definition of harness primitive).
Other instances are treated as normal structural instances in the test circuit.

| Primitive type | Parameters | Pins | Notes |
|----------------|------------|------|-------|
| `GND` | none | `.GND` | Ties a local ground net to simulator node 0 |
| `VDC` | `V=Voltage` | `.P`, `.N` | DC voltage source |
| `VAC` | `A=Voltage`, `phase=Phase` | `.P`, `.N` | Small-signal AC source (AC magnitude and phase) |
| `VSIN` | `DC=Voltage`, `A=Voltage`, `freq=Frequency`, `phase=Phase` | `.P`, `.N` | Time-domain sinusoidal source for transient benches |
| `Impedor` / `Impedance` | `Z=Impedance` | `.P`, `.N` | Impedance element; emits as R/C/L or a parallel combination |
| `Port` | `N=Integer`, `Z=Impedance`, `V=Voltage` | `.P`, `.N` | S-parameter reference plane (see [Section 4.2.6](#426-port-harness-primitives-s-parameter-benches)) |

Notes:

- `Impedor` and `Impedance` are synonymous at emission time; both normalize to the same harness
  element kind.
- An impedance value may be a numeric impedance, a numeric capacitance/inductance, or a parallel
  composite expressed with `||` (for example, `1GOhm || 15pF`).
- `Port` declares an S-parameter reference plane with a unique sequential index `N` starting at 1,
  a reference impedance `Z`, and a DC bias voltage `V`. Port instances are discovered by
  `SPAnalysis` at runtime, and current semantic validation requires at least one `Port` in bench
  `fill {}` when `SPAnalysis` is declared.

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
| `NoiseAnalysis` | Noise analysis driven by an input source (see [Section 4.4.3](#443-noise-analysis-contract)) |
| `STBAnalysis` | Stability analysis (backend-dependent) |
| `SPAnalysis` | S-parameter analysis over a frequency sweep (see [Section 4.4.4](#444-s-parameter-analysis-contract)) |

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

### 4.4.3 Noise analysis contract

Noise benches use `NoiseAnalysis` to request an output-referred noise spectrum over frequency. A
portable noise bench must satisfy the following contract:

- `NoiseAnalysis` must provide `start`, `stop`, and `output` parameters.
- `output` must evaluate to a terminal reference (scalar or differential). For a two-leaf terminal,
  the toolchain treats the output as a differential quantity $V(P) - V(N)$ (see [Section 4.2.5](#425-differential-terminal-semantics)).
- The testbench netlist must include at least one independent source that can act as the noise input
  reference. Standard benches (see [`lib/std/bench/NoiseBenches.cas`](../../lib/std/bench/NoiseBenches.cas)) typically include `VAC` sources (preferred) or a `VDC` source if a
  bench is noise-only.

In most cases, `NoiseAnalysis` is paired with an `ACAnalysis` so the bench can compute input-referred
noise by dividing the output noise density by the transfer magnitude:

```cascode
NoiseSpectrum n_in = input_referred_noise(noise_ac, ac, IN, OUT)
```

### 4.4.4 S-parameter analysis contract

`SPAnalysis` requests a multiport S-parameter sweep over frequency. It accepts the same
frequency-sweep parameters as `ACAnalysis` (`start`, `stop`, `space`, `samples`) plus an optional
`noise` flag (`0` or `1`) and operates on `Port` instances declared in bench `fill {}`. There is no
explicit parameter linking the analysis to specific ports; the runtime discovers ports and configures
the simulation accordingly. When `noise=1`, the simulator computes correlated noise parameters
together with the S-parameter sweep, and `S.NF()` becomes available to read noise figure in dB.

```cascode
analysis {
  SPAnalysis sp = new SPAnalysis(space=Log, samples=200, start=100MHz, stop=10GHz, noise=0)
}
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `start` | `Frequency` | yes | — | Start frequency of the sweep |
| `stop` | `Frequency` | yes | — | Stop frequency of the sweep |
| `space` | `Log` or `Lin` | no | `Log` | Frequency spacing |
| `samples` | integer | no | 100 | Number of frequency points |
| `noise` | `0` or `1` | no | `0` | Enable noise computation during S-parameter analysis |

The bench fill block provides DC bias, coupling networks, and any other circuit elements required
for the operating point. The fill block does not need to provide port excitation sources or
termination impedances — `SPAnalysis` handles those based on the port declarations.

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
the `(if ... { ... } else { ... })` form (shown in [Section 4.4](#44-analysis-blocks)).

### 4.5.3 Calling Other Measurements

Within a bench, other measurements can be invoked as functions using call syntax:

```cascode
measurement BandpassBandwidth : Hz {
  return abs(LowpassBandwidth() - HighpassBandwidth())
}
```

If a measurement declares parameters, it must be invoked with matching arguments by name or by
position.

Measurements may be overloaded by arity within the same bench. Two measurements with the same
name are valid only when they declare different parameter counts. Resolution is by name and argument
count at the call site.

```cascode
measurements {
  measurement ForwardGain(Frequency f) : dB {
    SParameterMatrix S = sparam(sp)
    return db20(S.S(2, 1).Mag()).ValueAt(f)
  }

  measurement ForwardGain(Frequency from, Frequency to) : dB {
    SParameterMatrix S = sparam(sp)
    return db20(S.S(2, 1).Mag()).From(from).To(to)
  }
}
```

The standard transfer benches use the same arity-based overloading pattern for `Gain(f)` and
`Gain(from, to)`.

Declaring multiple measurements with the same name and the same parameter count is a semantic error.

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
| `NoiseSpectrum` | `noise(noise, terminal)`, `input_referred_noise(...)` | Noise density vs frequency (V/√Hz) |
| `ComplexVoltageSpectrum` | `voltage(ac, node)` | Complex voltage vs frequency (V) |
| `ComplexCurrentSpectrum` | `current(ac, harness_pin)` | Complex current vs frequency (A) |
| `VoltageWaveform` | `voltage(tran, node)` | Voltage vs time (V) |
| `CurrentWaveform` | `current(tran, harness_pin)` | Current vs time (A) |
| `SParameterMatrix` | `sparam(analysis)` | Frequency-indexed matrix of complex S-parameters |

---

## 4.7 Measurement Primitives and Methods

### 4.7.1 Constructors and Conversions

Built-in functions construct or transform measurement values. The following table lists the most
commonly used primitives in the standard library.

| Function | Result | Notes |
|----------|--------|-------|
| `transfer(ac, stim, resp)` | `TransferFunction` | Computes the complex transfer `V(resp)/V(stim)` over an AC sweep |
| `voltage(analysis, terminal)` | `ComplexVoltageSpectrum` or `VoltageWaveform` | AC yields a spectrum; transient yields a waveform |
| `current(analysis, element_pin)` | `ComplexCurrentSpectrum` or `CurrentWaveform` | Reads current through a harness-injected source pin |
| `noise(noise, terminal)` | `NoiseSpectrum` | Output noise spectral density for the analysis output |
| `input_referred_noise(noise, ac, stim, resp)` | `NoiseSpectrum` | Divides output noise density by the gain |
| `sparam(analysis)` | `SParameterMatrix` | Extracts the full S-parameter matrix from a completed `SPAnalysis` |
| `db20(GainSpectrum)` | `GainSpectrum` | 20·log10(magnitude) |
| `db10(GainSpectrum)` | `GainSpectrum` | 10·log10(magnitude) |
| `quiescent_power(pwr, ret)` | `W` | Computes DC rail power from the applied supply source |
| `period(f)` | `Time` | Returns `1/f` |
| `abs(x)` | scalar type | Absolute value (numeric) |
| `sqrt(x)` | scalar type | Square root (numeric) |

`current(...)` requires a harness element pin reference such as `harness.VDD.P`. The bench runtime
maps `harness.<SupplyName>.P` / `.N` to the injected supply source that applies the rail.

Built-in function arguments support positional and named forms. Runtime validation rejects missing
required arguments, excess positional arguments, and unexpected named arguments.

### 4.7.2 Methods on Structured Values

Structured values expose methods for common post-processing operations.

Transfer function methods:

- `H.Mag()` → `GainSpectrum` (linear magnitude)
- `H.Phase()` → `PhaseSpectrum` (degrees)

Spectrum methods:

- `S.ValueAt(f)` → interpolated real-valued or complex-valued scalar at frequency `f`
- `S.From(f)` / `S.To(f)` → truncated spectrum with frequency range `>= f` or `<= f` (same spectrum type)
- `S.Range(from, to)` → equivalent to `S.From(from).To(to)` (same spectrum type)
- `S.FindCrossing(threshold, dir=falling|rising, cross=1, from=..., to=...)` → crossing frequency
- `S.Integrate(from, to)` → (noise spectra only) integrated RMS noise over a band

Waveform methods:

- `W.ValueAt(t)` → interpolated scalar at time `t`
- `W.From(t)` / `W.To(t)` → truncated waveform with time range `>= t` or `<= t` (same waveform type)
- `W.Range(from, to)` → equivalent to `W.From(from).To(to)` (same waveform type)

For complex AC spectra (`ComplexVoltageSpectrum`, `ComplexCurrentSpectrum`), `ValueAt(f)` returns a complex point
interpolated in magnitude/phase space. Phase interpolation uses the shortest angular path between
neighboring points; if one endpoint has near-zero magnitude, the phase is taken from the nearest
non-zero endpoint. Magnitude-sensitive operations remain explicit, either by converting the
spectrum first (`voltage(ac, OUT).Mag().ValueAt(f)`) or by converting the sampled point
(`voltage(ac, OUT).ValueAt(f).Mag()`). These two forms are equivalent for magnitude interpolation.
`From`, `To`, and `Range` are chainable with each other and with `ValueAt`, for example
`voltage(ac, OUT).Range(100Hz, 1MHz).ValueAt(500kHz)`.
Method arguments can be passed positionally or by name (including mixed usage), and named
arguments are validated against each method's declared parameter names.

The [standard library](../../lib/std/bench/) uses these methods to implement measurements such as gain-bandwidth and phase
margin ([transfer benches](../../lib/std/bench/TransferBenches.cas)) and spot/integrated noise ([noise benches](../../lib/std/bench/NoiseBenches.cas)).

### 4.7.3 S-Parameter Matrix Methods

`SParameterMatrix` exposes element accessors and derived RF metric methods. All element accessors
return `TransferFunction` (a complex-valued function of frequency). Derived metric methods return
typed spectra (`GainSpectrum`, `ScalarSpectrum`, or `TimeSpectrum`) based on the metric.

Element access by port number follows the standard S-parameter convention: `S.S(i, j)` is the
response at port *i* due to excitation at port *j*.

| Method | Result | Notes |
|---|---|---|
| `S.S(i, j)` | `TransferFunction` | Raw S-parameter element Sij |
| `S.S11()` | `GainSpectrum` | Magnitude in dB for the input reflection term, `db20(|S11|)` (2-port only) |
| `S.S21()` | `GainSpectrum` | Magnitude in dB for the forward gain term, `db20(|S21|)` (2-port only) |
| `S.S12()` | `GainSpectrum` | Magnitude in dB for the reverse gain term, `db20(|S12|)` (2-port only) |
| `S.S22()` | `GainSpectrum` | Magnitude in dB for the output reflection term, `db20(|S22|)` (2-port only) |

Derived metric methods:

| Method | Result | Notes |
|---|---|---|
| `S.ReturnLoss(port)` | `GainSpectrum` | −20 log₁₀ \|Snn\| (positive dB for well-matched port) |
| `S.VSWR(port)` | `ScalarSpectrum` | (1 + \|Γ\|) / (1 − \|Γ\|) where Γ = Snn |
| `S.InsertionLoss(to, from)` | `GainSpectrum` | −20 log₁₀ \|Sij\|, refers to forward-path loss |
| `S.Isolation(to, from)` | `GainSpectrum` | Same formula as insertion loss, but refers to the reverse-path leakage |
| `S.StabilityK()` | `ScalarSpectrum` | Stability factor (2-port only) |
| `S.MuFactor()` | `ScalarSpectrum` | Edwards-Sinsky μ factor (2-port only) |
| `S.MSG()` | `GainSpectrum` | Maximum stable gain in linear units (2-port only) |
| `S.MAG()` | `GainSpectrum` | Maximum available gain in linear units; falls back to MSG where K < 1 (2-port only) |
| `S.GroupDelay(to, from)` | `TimeSpectrum` | −dφij/dω (time-valued samples indexed by frequency) |
| `S.NF()` | `GainSpectrum` | Noise figure in dB; requires `SPAnalysis(noise=1)` |
| `S.NFmin()` | `GainSpectrum` | Minimum noise figure in dB; requires `SPAnalysis(noise=1)` |
| `S.Rn()` | `ImpedanceSpectrum` | Noise resistance in Ω; requires `SPAnalysis(noise=1)` |

The 2-port-only methods (`S11`, `S21`, `S12`, `S22`, `StabilityK`, `MuFactor`, `MSG`, `MAG`, `NF`,
`NFmin`, `Rn`) produce a semantic error when called on an `SParameterMatrix` from a bench with more
than two ports.

Mixed-mode S-parameters can be derived in bench measurements from single-ended matrix elements.
The standard library bench `TwoPortMixedModeSParam` demonstrates this using four single-ended ports
to compute `Sdd`, `Sdc`, `Scd`, and `Scc` terms.

Example usage:

```cascode
measurements {
  measurement ForwardGain(Frequency f) : dB {
    SParameterMatrix S = sparam(sp)
    return db20(S.S(2, 1).Mag()).ValueAt(f)
  }

  measurement InputReturnLoss(Frequency f) : dB {
    SParameterMatrix S = sparam(sp)
    return S.ReturnLoss(1).ValueAt(f)
  }

  measurement StabilityK(Frequency f) : Scalar {
    SParameterMatrix S = sparam(sp)
    return S.StabilityK().ValueAt(f)
  }
}
```

---

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

Bindings declared on an interface are part of that interface's contract, not just inherited
boilerplate. A complete document is ill-formed if an interface binding refers to a terminal shape
that the interface itself does not declare, or if it maps to a terminal shape whose leaf types are
incompatible with the interface's declaration; [Chapter 2 terminal-contract rules](Ch02_Core_Concepts.md#24-interfaces-connectors-and-attach) define the exact terminal-contract
compatibility semantics. When a circuit uses `implements`, the interface terminal set is a minimum
contract: the circuit must provide every terminal declared by the interface, but it MAY expose
additional public terminals beyond that set. After inheritance and extension are applied, every
terminal mapping named by the binding must still satisfy both shape and leaf-type compatibility, so
same-shape mappings with incompatible leaf types are still rejected.

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
| Instance declaration | `Type name = new Type(...) { ... }` | Adds binding-scoped instances (for specialization) |

Mappings and connections use the same `--` wiring operator as `fill {}` blocks. `pinRef` supports
bundle field access and indices (for example, `IN.P`, `TAP[0]`).

Binding-scoped instances serve a specific design role: they adapt topology-agnostic benches to
circuits whose terminal structures require additional bias or stimulus context. Standard library
benches such as `QuiescentPower` and `SEDCBias` intentionally declare only the terminals they
directly measure, leaving input biasing to the binding. When a bench omits terminals that the
DUT exposes as analog inputs, the binding must provide a DC path for those terminals; otherwise,
floating nodes produce incorrect simulation results (zero current through OFF devices, wrong DC
operating points, or singular matrices during AC analysis).

The standard amplifier interfaces use this pattern to bind topology-agnostic power and DC bias
benches with common-mode bias and source impedance:

```cascode
bind QuiescentPower as vdd_pwr {
  bench.PWR--dut.VDD
  bench.RET--dut.GND

  GND g = new GND() { .GND--gnd }
  VDC commonModeVDC = new VDC(V=env.InputCommonModeRange) { .P--vcm, .N--gnd }
  Impedor sourceP = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { .P--vcm, .N--dut.IN.P }
  Impedor sourceN = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { .P--vcm, .N--dut.IN.N }
}
```

The implicit nets `gnd` and `vcm` and the three harness primitive instances elaborate into the same
testbench netlist as the bench's `fill {}` block (see [Section 4.1.3](#413-scope-and-availability)).
The bench definition remains reusable across input topologies; the binding adapts it to the specific
interface contract.

### 4.8.3 Referencing Measurements from Constraints

Numeric constraints reference bench measurements via the binding name:

```cascode
constraints {
  numeric {
    c_gbw = transfer_bench::GainBandwidth at net::OUT >= 20MHz
    c_gain = transfer_bench::PassbandGain at net::OUT >= 40dB
    c_gain_at_1mhz = transfer_bench::Gain(f=1MHz) at net::OUT >= 35dB
    c_gain_band = transfer_bench::Gain(from=100kHz, to=10MHz) at net::OUT >= 30dB
    c_pm = transfer_bench::PhaseMargin at net::OUT >= 60deg

    c_int = noise_bench::IntegratedInputNoise(from=10Hz, to=10MHz) <= 1uVrms
  }
}
```

The general form is:

```
<binding>(<bench-args>)? :: <measurement>(<measurement-args>)?
```

Bench arguments specialize a parameterized bench binding (see [Section 4.1.2](#412-bench-parameters)). Measurement arguments invoke a parameterized
measurement within the selected bench (see [Section 4.5.3](#453-calling-other-measurements)).

When a constrained measurement returns a scalar value, the operator applies to that scalar directly.
When a constrained measurement returns a spectrum or waveform, the operator applies element-wise to
all returned samples. The constraint passes only if every sample satisfies the comparison. Compliance
reports expose a single `Actual` value for these constraints as a worst-case sample:

- `>=` / `>` reports the minimum sample.
- `<=` / `<` reports the maximum sample.
- `==` reports the sample with the largest absolute error from the expected value.

An empty spectrum or waveform result fails unconditionally because there are no samples to validate.

### 4.8.4 Emission and Execution Model

Bench simulation is constraint-driven: benches are emitted and executed when at least one of their
measurements is referenced by a constraint. If any measurement from a bench is constrained, the whole
bench is simulated and all of its measurements are produced.

### 4.8.5 Binding measurement exports

A binding may include a `measurements { ... }` block. Measurements declared in this block are *binding
exports*: they behave like additional measurements on the bound bench instance and may be referenced
from constraints using the binding name.

Binding exports are commonly used to:

- Provide default arguments for parameterized bench measurements.
- Define derived metrics that depend on other benches (for example, gain-normalized PSRR).

Within a binding export expression, `base::Name(...)` refers to a measurement defined by the bound
bench itself. See [standard amplifier interfaces](../../lib/std/amp/) for examples in practice.

```cascode
bind SupplyToSERejection as psrr_bench {
  bench.IN--dut.IN
  bench.PWR--dut.VDD
  bench.OUT--dut.OUT

  measurements {
    measurement InputReferredPSRR : dB =
      base::InputReferredPSRR(dmGain=transfer_bench::PassbandGain)
  }
}
```
