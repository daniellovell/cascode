# RFC: ACIR Measurement Abstraction System

Status: Draft  
Authors: Daniel Lovell
Created: 2026-01-25  
Last Updated: 2026-01-25  
Target Version: ACIR 3.0

---

## Abstract

This RFC proposes a bench and measurement model for ACIR grounded in network theory. A bench is treated as the explicit adapter layer: it connects a circuit to test equipment and defines which measurements are physically meaningful. To avoid topology-driven template duplication (single-ended vs fully-differential, presence/absence of supply ports, etc.), circuits expose named two-terminal network ports (e.g., `Port(a, b)`) as part of the interface they claim to implement.

The design separates “what a circuit is” from “what connection points it exposes”:

- `class`: a taxonomic interface for circuits, with single inheritance
- `trait`: an orthogonal capability marker (e.g., “has a supply port”), composable
- `circuit`: a concrete implementation that belongs to a `class`

Classes carry a set of traits. When a circuit belongs to a class, it inherits that class’s traits and therefore becomes compatible with the benches that require them. This keeps circuit authoring non-verbose: you typically implement the one class that matches your intent, and all the common bench compatibility “comes along for free.”

Benches are written against the capabilities they require, not against a proliferation of topology variants. The toolchain emits concrete simulator testbenches programmatically (C#), keeping measurement definitions reusable while making the bench hookup explicit and type-checked.

---

## 1. Problem Statement

### 1.1 Current State

The existing ACIR bench system encodes hookup topology in builtin benches, so the same measurement intent must be reimplemented for each circuit interface variant:

```acir
bench SEOpAmpACBench for SingleEndedOpAmp
  builtin SEOpAmpACBench
  outputs: GainBandwidth, PassbandGain, PhaseMargin, LowpassBandwidth

bench FDOpAmpACBench for FullyDifferentialOpAmp
  builtin FDOpAmpACBench
  outputs: GainBandwidth, PassbandGain, PhaseMargin, LowpassBandwidth
```

These benches share measurement intent but differ in what can be connected and how:

- A single-ended output is measured against a reference; a fully-differential output is measured between two driven nodes.
- Some measurements require ports whose terminals are independently drivable (e.g., common-mode excitation for CMRR).
- Some benches require additional physical connection points that a circuit may not have (e.g., PSRR requires a supply port; a passive filter has none).

### 1.2 Consequences

This leads to:

1. Duplicated measurement logic across bench templates.
2. Fixes and improvements replicated across topology variants and simulator backends.
3. Combinatorial growth: device category × interface variant × simulator backend.
4. Late incompatibility discovery: benches and measurements can be requested even when they cannot physically connect.

### 1.3 Desired Properties

The design should provide:

1. Bench as adapter: benches define hookup; circuits should not restate bench-specific bindings.
2. Network ports as the common currency: benches and measurements operate on two-terminal ports.
3. Compile-time compatibility: a bench should be accepted only when the circuit exposes the required ports/capabilities.
4. Reuse without “SE/DIFF infection”: avoid topology-variant benches where network-port modeling should unify them.
5. Programmatic emission: testbenches are generated in code rather than maintained as a template matrix.

### 1.4 Questions This RFC Must Answer

To avoid drifting into an elegant but incomplete abstraction, this RFC is considered incomplete until it answers (with concrete syntax, semantics, and examples) at least the following questions:

1. Reuse across circuit categories: how do two-port benches and measurements apply uniformly to both filters and amplifiers, while still allowing specialized benches for specific classes?
2. Differential and common-mode behavior: how are differential drive, common-mode drive, and mixed-mode excitation represented in the bench adapter, and how does a measurement request a specific mode without reintroducing “SE/DIFF variant” benches?
3. Impedances: how are input and output impedance measurements defined for both differential and single-ended ports, in a way that matches the network-theory decomposition designers expect?
4. I/O aliasing: how can circuits with non-standard terminal names (or multiple supplies/grounds) conform to a class’s required port bindings without reintroducing per-circuit bench glue?
5. Taxonomy and trait coverage: does the proposed `class` hierarchy and trait vocabulary cover the following representative families without special cases?
   - Passive filters: a differential RC filter and a single-ended RC filter (same network-port model, different reference choices).
   - Powered filters: a fully-differential Sallen–Key filter (powered, not a passive two-port; contains an amplifier core and therefore has input common-mode constraints).
   - Amplifiers: `FullyDifferentialOpAmp`, `SingleEndedOpAmp`, and `SingleEndedAmplifier` (shared benches where appropriate, specialized benches where needed).
   - References: a bandgap voltage reference and a current reference (both have supplies and outputs, but no signal inputs; PSRR must still be measurable).
   - Multiport extensions: quadrature IQ ports and polyphase filters with many inputs/outputs (the approach must extend beyond “exactly two ports” without exploding into a new family of bespoke benches).

### 1.5 Measurement and Constraint Coverage (initial)

This section is intentionally exhaustive and will grow. It is a catalog of the measurements and constraint forms this RFC intends to support, and how each is interpreted across common circuit families.

Each measurement is defined against named network ports and traits exposed by a circuit’s `class`. The default expectation is that a class defines at least an output port when any output-referenced measurement is desired; reference generators are not special-cased, they simply omit input ports and therefore only support measurements that do not require an input port.

| Measurement / constraint family | Meaning (network-theory interpretation) | Required ports / traits (selected view) | Typical bench | Notes on topology interpretation |
|---|---|---|---|---|
| `PassbandGain` | Small-signal transfer magnitude from input port to output port at a specified frequency (default DC) | `in`, `out` | `ACBench` | For passive filters: \(abs(V(out)/V(in))\). For amplifiers: small-signal gain. Not applicable to references (no `in`). |
| `LowpassBandwidth` | Upper frequency where transfer magnitude drops to a defined threshold relative to low-frequency gain (e.g., −3 dB) | `in`, `out` | `ACBench` | For amplifiers: closed-loop or open-loop bandwidth depends on bench hookup. For lowpass filters: cutoff. |
| `HighpassBandwidth` | Lower frequency where transfer magnitude rises to a defined threshold relative to midband gain | `in`, `out` | `ACBench` | Primarily for highpass/bandpass filters; for amplifiers this is typically a coupling/servo pole and may be irrelevant unless bench defines it. |
| `GainBandwidth` | Unity-gain frequency (or gain-bandwidth product as defined by the bench) | `in`, `out` | `ACBench` | For op-amps: usually UGF (0 dB crossing). For filters: may not be meaningful unless the bench defines a normalization (often omitted). |
| `PhaseMargin` | Phase margin at unity loop gain (as defined by bench) | `in`, `out` and a loop-breaking topology | `ACBench` | Meaning depends on whether the bench measures loop gain (requires a defined loop break) or closed-loop phase at UGF; must be bench-defined to avoid ambiguity. |
| `GainMargin` | Gain margin at phase crossover (as defined by bench) | `in`, `out` and a loop-breaking topology | `ACBench` | Same caveat as phase margin: this is a stability measurement and is only meaningful when the bench specifies the loop transfer being measured. |
| `CMRR` | Ratio of differential transfer to common-mode transfer (bench-defined excitation and probe) | `in`, `out`, `BalancedInput` | `ACBench` / `CMRRBench` | Only meaningful when the class explicitly declares `BalancedInput`. For `BalancedOutput`, the bench measures differential output by default; for `UnbalancedOutput`, the bench measures output relative to the reference terminal. |
| `PSRR` | Output sensitivity to supply perturbation (bench-defined perturbation port and probe) | `out`, `supply` | `ACBench` / `PSRRBench` | Applies to powered filters, amplifiers, voltage references, and current references. Not applicable to passive filters (no `supply`). |
| `QuiescentPower` | Total DC power at the operating point under the bench’s bias/harness conditions | `supply` (and an operating point) | `DCBench` | For references: this is often a primary metric. For multi-supply systems, the bench must define whether “total” means sum over all supplies or a selected supply. |
| `TotalQuiescentPower` | Sum of quiescent power over all supplies | one or more `supply` ports | `DCBench` | Only meaningful when multiple supplies exist; otherwise identical to `QuiescentPower`. |
| `InputReferredNoise` | Output noise referred back to an equivalent input quantity under the bench’s excitation model | `in`, `out` | `NoiseBench` | For differential ports, “input-referred” must specify whether it is differential-mode, common-mode, or a specific linear combination. |
| `SpotNoise` | Noise spectral density at a specific frequency | `out` (and optionally `in` for referring) | `NoiseBench` | Applies broadly; for references this is often output-referred noise. |
| `SlewRate` | Max time-derivative of output under a bench-defined large-signal step | `out` (and a defined stimulus) | `TranBench` | Requires a transient bench hookup; for differential outputs the bench must specify which output quantity is observed (e.g., differential output vs single-ended-to-reference). |
| `InputImpedance` (`Zin`) | Small-signal impedance looking into the input port under a bench-defined termination of other ports | `in` | `ZinBench` | For differential input ports, impedance decomposes into differential and common-mode components; the bench must define which is being measured. For single-ended ports, this is \(V/I\) to the reference terminal. |
| `OutputImpedance` (`Zout`) | Small-signal impedance looking into the output port under a bench-defined termination of other ports | `out` | `ZoutBench` | For filters and amplifiers, termination conditions matter (load, feedback). For differential outputs, bench must define differential vs common-mode impedance. |
| Input bias / common-mode range (ICMR as a sweep axis) | Range of input bias values over which constraints must hold | `in`, and exactly one of `BalancedInput` / `UnbalancedInput` | (bench-defined) | For `BalancedInput`, this is a sweep of input common-mode \(Vcm\). For `UnbalancedInput`, this is a sweep of the input DC bias relative to the reference terminal. |
| Constraint domains: PVT, sweeps, yield | Constraints are evaluated over an environment/sweep domain and aggregated | harness `pvt`, plus sweep axes and optional aggregators | (all benches) | See §1.6 for the default “must hold for all points” interpretation and the explicit aggregation forms. |
| Multiport extensions (IQ, polyphase, N-port) | Generalization from two named ports to a set of named ports and port-to-port transfer/impedance matrices | named port set | (future) | The model must generalize to transfer matrices (e.g., \(S\)-parameters) and port-selective measurements without reintroducing topology-variant benches. |

### 1.6 Constraint Evaluation Under Sweeps (PVT, bias, and operating ranges)

Constraints are not evaluated at a single point. The intent is that a constraint is evaluated over a domain of environment and sweep points, and passes only when it holds over that domain.

Unless a constraint explicitly chooses another aggregation, the default interpretation is:

- The domain includes all PVT points in the `harness:` `pvt` list (if present).
- The domain includes any explicit sweep ranges stated in the constraint (frequency bands, common-mode ranges, load sweeps, etc.).
- The constraint must hold for every point in the Cartesian product of those sets.

Examples of the intended expressivity (syntax is illustrative at this stage):

- Maintain gain across input common-mode and PVT:
  `ACBench::PassbandGain >= 40dB over Vcm in InputCommonModeRange across PVT`
- Maintain a minimum rejection over a frequency band:
  `ACBench::CMRR >= 50dB for f in [1kHz:1MHz]`
- Statistical constraints:
  `yield(ACBench::GainBandwidth >= 80MHz) >= 99%`

---

## 2. Construct System

This RFC introduces a small construct system that makes the bench adapter model explicit. The key idea is to separate taxonomy from capabilities, and to attach named network ports to the taxonomic interface so individual circuits do not need to restate them.

The terminology is intentionally close to common programming language usage: circuits “belong to a class” and “have traits.”

### 2.1 `circuit` (concrete definition)

A `circuit` is a concrete definition at some elaboration level. At HL, a circuit may contain `slot` declarations that specify requirements for components that will be synthesized later. At ML and EL, a circuit typically contains a `fill:` block that holds the instantiated implementation (instances, devices, and internal nets). A circuit may also include `constraints:` and `harness:` blocks.

A circuit declares which `class` it belongs to; it must satisfy the class’s required terminals and any invariants the type checker enforces.

### 2.2 `class` (taxonomy)

A `class` describes a family of circuits and may extend a single parent class. Classes exist to capture taxonomy (e.g., Amplifier vs Filter vs Reference) and to define the stable external interface of that family.

Crucially, a class is also where **named network ports** are defined. A class provides a `ports:` block that binds bench-facing port names to concrete node pairs using `Port(a, b)`. All circuits of that class inherit these bindings, eliminating per-circuit repetition.

Classes also define a `traits:` set. Traits are inherited transitively through the class hierarchy, so a circuit that belongs to a specialized class automatically carries the full bundle of capabilities of its parent classes. This is the mechanism that makes “write the circuit you mean” align with “get the benches you expect.”

### 2.3 `trait` (capability)

A `trait` is an orthogonal capability marker used for bench compatibility checks. Traits are intentionally small and composable (a class can list many traits). Examples include “has a supply port,” “has an output port,” or “input port supports differential drive.” A trait is not a taxonomy node; it exists so a bench can state what it needs without naming every concrete class it can attach to.

### 2.4 `Port(a, b)` (network port)

`Port(a, b)` is a two-terminal network access point. It captures “where” a bench connects without prescribing whether the stimulus is voltage-mode or current-mode. Single-ended and differential interfaces are both expressed as two-terminal ports; the only difference is whether either terminal is a fixed reference node.

In particular, a “single-ended” interface is represented by choosing a reference node as one terminal (for example, `Port(OUT, GND)`), while a “fully-differential” interface is represented by binding the port across two signal terminals (for example, `Port(OUT.P, OUT.N)`). Benches that require additional degrees of freedom (such as common-mode excitation) express those requirements through explicit traits (for example `BalancedInput`) and compatibility checks, not by splitting ports into separate kinds.

### 2.5 Benches, measurements, and harnesses

A `bench` adapts a circuit’s named network ports to a concrete test topology (sources, loads, perturbations) and enumerates the measurements it can produce. A bench declares the traits/ports it requires; if the circuit’s class does not provide them, the bench cannot be used.

The existing `harness:` block remains the place for numeric parameters (PVT points, source impedances, default loads, supply values). Benches define structure; harnesses provide parameterization.

Most reusable measurements are defined purely in terms of across-port quantities. For a port \(p = Port(a, b)\), define \(v(p) = V(a) - V(b)\), and (by convention) \(i(p)\) as current entering terminal \(a\). Transfer and impedance measurements can then be defined once:
\(H_v = v(out) / v(in)\), \(Z_{in} = v(in) / i(in)\), \(Z_{out} = v(out) / i(out)\).

These expressions do not care whether a port is “single-ended” or “fully differential”; that distinction is entirely captured by the port binding (for example `out = Port(OUT, GND)` versus `out = Port(OUT.P, OUT.N)`). This is how the design avoids topology-driven bench duplication: the measurement intent is stable, and only the binding changes.

Port-shape traits (`BalancedInput`, `UnbalancedOutput`, etc.) exist to make degrees of freedom explicit for benches that need them (common-mode drive, mixed-mode impedance, output common-mode measurement). They should not force “one bench per topology variant.” Instead, a bench provides a single measurement suite with multiple stimulation/sense modes, and the emitter selects realizable modes based on the selected view’s declared traits. In practice, this branching is localized to a small set of port-driver adapters (e.g., “drive across a port” versus “drive common-mode”), while measurement extraction stays shared and expressed in terms of \(v(p)\) and \(i(p)\).

The following sketch illustrates the intended authoring style:

```acir
trait HasSupplyPort
trait TransferTwoPort

class Amplifier:
  traits:
    TransferTwoPort
    HasSupplyPort

class SingleEndedOpAmp extends Amplifier:
  input IN : Diff
  output OUT : analog
  supply VDD
  ground GND

  ports:
    in = Port(IN.P, IN.N)
    out = Port(OUT, GND)
    supply = Port(VDD, GND)

bench ACBench:
  requires:
    TransferTwoPort
  # ... hookups and measurement suite ...
```

---

## 3. Proposal Overview

### 3.1 Summary of the Proposal

This RFC makes benches the adapter layer and makes network ports the common currency for applicability and reuse.

At a high level, the proposal consists of:

1. Named network ports: classes bind bench-facing port names to concrete node pairs using `ports:` and `Port(a, b)`.
2. A split between taxonomy and capability:
   - `class` defines a circuit family (single inheritance) and carries inherited `traits`.
   - `trait` is a capability marker used for bench compatibility checks.
3. Bench compatibility by requirement: benches declare the traits (and named ports) they require; no per-circuit bench glue is required when a circuit belongs to a well-defined class.
4. Measurements defined once: measurement logic is reusable and parameterized by the bench hookup; constraints evaluate those measurements over sweep domains (PVT and explicit sweeps such as frequency bands or input common-mode ranges).
5. Programmatic emission: testbench generation is implemented in C# against a normalized intermediate model, avoiding an explosion of handwritten templates.

### 3.2 Canonical Taxonomy and Trait Vocabulary

The following vocabulary is the starting point. It is intentionally small, but it must be sufficient to cover the representative families listed in §1.4(5).

Traits (capabilities) are used to express what benches can assume about a class.

| Trait | Meaning | Typical benches enabled |
|---|---|---|
| `HasInputPort` | The class defines an input network port named `in` | transfer- and impedance-related benches that require an input |
| `HasOutputPort` | The class defines an output network port named `out` | output-referenced benches (most) |
| `TransferTwoPort` | The class defines both `in` and `out` ports and intends a transfer interpretation between them | `ACBench`, `ZinBench`, `ZoutBench` |
| `BalancedInput` | The input port `in` is balanced (both terminals are signal nets, neither is a reference node) | common-mode and mixed-mode input benches (e.g., CMRR, differential/common-mode impedance) |
| `UnbalancedInput` | The input port `in` is unbalanced (one terminal is a reference node) | single-ended input benches; input bias sweeps are interpreted as DC bias relative to the reference |
| `BalancedOutput` | The output port `out` is balanced | differential output benches; output common-mode measurements when defined by a bench |
| `UnbalancedOutput` | The output port `out` is unbalanced | single-ended output benches; output quantities are interpreted relative to the reference |
| `HasSupplyPort` | The class defines a supply network port named `supply` (a two-terminal supply perturbation point) | `PSRRBench`, power measurements |
| `Multiport` | The class defines more than one signal input and/or output port and expects matrix-style measurements (transfer between multiple ports) | future multiport benches (IQ, polyphase, N-port) |

The balanced/unbalanced traits above are intentionally explicit. They are a reference vocabulary that standard benches can use to describe applicability without forcing inference. Implementations are still expected to validate obvious inconsistencies (for example, a class that declares `BalancedInput` must bind `in` to two non-reference terminals in its `ports:` block), but the trait itself is a declared part of the class contract.

#### Views: multiple instances of a port/trait bundle

A single circuit may expose more than one “measurement interface” at once. Examples include:

- A macro that contains two independent amplifier channels (two transfers to measure).
- A multi-supply design where PSRR must be measured against multiple supplies.
- A multiport network (IQ, polyphase) where multiple port groupings are meaningful.

To support this without inventing new trait names for every channel, this RFC introduces a named **view** concept. A view is a mapping from bench-facing names (`in`, `out`, `supply`, etc.) to concrete `Port(a, b)` bindings together with a declared set of traits describing the shape of those bindings.

The existing `ports:` block is treated as a shorthand for a default view (named `main`). A class or circuit may additionally define a `views:` block to export multiple named views. When a `main` view exists in the same scope, a view may override only the bindings it needs; any unspecified bindings are inherited from `main`.

To reduce repetition, a view may be declared as conforming to an existing class (for example `SingleEndedOpAmp`). In that form, the view inherits the class’s trait bundle and canonical port roles, and the view’s `ports:` block provides the concrete `Port(a, b)` bindings for that instance of the interface.

```acir
class DualChannelAmplifier:
  views:
    ch_a : SingleEndedOpAmp
      ports:
        in = Port(IN_A.P, IN_A.N)
        out = Port(OUT_A, GND)
        supply = Port(VDD, GND)
    ch_b : SingleEndedOpAmp
      ports:
        in = Port(IN_B.P, IN_B.N)
        out = Port(OUT_B, GND)
        supply = Port(VDD, GND)
```

Constraints and benches select a view when a class exports more than one applicable view. The exact syntax is specified later, but the intent is that “two passband gains on the same circuit” is a normal, first-class use case.

Multi-supply PSRR is handled the same way: export one view per supply perturbation point, then write one constraint per view (syntax illustrative):

```acir
circuit MixedSupplyOTA implements FullyDifferentialOpAmp
  level EL

  supply AVDD
  supply DVDD
  ground VSS
  input IN : Diff
  output OUT : Diff

  ports:
    in = Port(IN.P, IN.N)
    out = Port(OUT.P, OUT.N)
    supply = Port(AVDD, VSS)         # default supply for the `main` view

  views:
    psrr_avdd : FullyDifferentialOpAmp
      ports:
        supply = Port(AVDD, VSS)
    psrr_dvdd : FullyDifferentialOpAmp
      ports:
        supply = Port(DVDD, VSS)

  constraints:
    c_psrr_a = PSRRBench[psrr_avdd]::PSRR >= 70dB
    c_psrr_d = PSRRBench[psrr_dvdd]::PSRR >= 50dB
```

The taxonomy (classes) organizes circuit families and carries the trait bundle so circuits inherit bench compatibility by implementing a single class.

Unless otherwise noted, the class table below describes the trait bundle and port shape of the default `main` view exported by each class.

| Class | Extends | Carries traits (minimum) | Notes |
|---|---|---|---|
| `Filter` | (none) | `TransferTwoPort` | Base taxonomy for filters (passive and powered). |
| `SingleEndedFilter` | `Filter` | `UnbalancedInput`, `UnbalancedOutput` | Transfer two-port with single-ended I/O against a reference node. |
| `FullyDifferentialFilter` | `Filter` | `BalancedInput`, `BalancedOutput` | Transfer two-port with differential I/O. |
| `FullyDifferentialSallenKeyFilter` | `FullyDifferentialFilter` | `HasSupplyPort` | Powered filter with active core; often evaluated over an input bias/common-mode sweep domain. |
| `Amplifier` | (none) | `TransferTwoPort`, `HasSupplyPort` | Base for op-amps and general amplifiers; often evaluated over an input bias/common-mode sweep domain. |
| `SingleEndedAmplifier` | `Amplifier` | `UnbalancedInput`, `UnbalancedOutput` | Single-ended I/O. |
| `SingleEndedOpAmp` | `Amplifier` | `BalancedInput`, `UnbalancedOutput` | Differential input, single-ended output. |
| `FullyDifferentialOpAmp` | `Amplifier` | `BalancedInput`, `BalancedOutput` | Fully differential I/O. |
| `VoltageReference` | (none) | `HasSupplyPort`, `HasOutputPort`, `UnbalancedOutput` | No signal input; supports PSRR and output noise/power measurements. |
| `CurrentReference` | (none) | `HasSupplyPort`, `HasOutputPort`, `UnbalancedOutput` | No signal input; supports PSRR and output noise/power measurements (bench defines load/compliance). |

This vocabulary is not the final library. The point of listing it here is to make the intended coverage explicit early and to keep later sections honest: if a proposed bench or measurement cannot be expressed using this system without reintroducing topology variants, either the vocabulary must expand or the design must change.

### 3.3 Commitment to Complete Examples

Section 7 will include complete, end-to-end examples that explicitly hit each family in §1.4(5), including bench usage, constraint domains (PVT and sweeps), and at least one non-standard naming/aliasing scenario. At minimum:

- Passive filters: single-ended RC and differential RC, both using the same transfer and impedance benches.
- Powered filters: fully-differential Sallen–Key, showing supply-dependent measurements (PSRR) and constraints that must hold across input common-mode and PVT.
- Amplifiers: `SingleEndedAmplifier`, `SingleEndedOpAmp`, and a fully differential OTA, showing how a shared `ACBench` applies while mixed-mode benches are enabled only when the appropriate balanced/unbalanced traits are present.
- References: a bandgap voltage reference and a current reference (no inputs), showing PSRR and noise/power constraints.
- Multiport: an IQ or polyphase-style example demonstrating how the model extends beyond a single transfer two-port without creating a new template family.

---

## 4. Detailed Design

### 4.1 Direction Keywords

#### 4.1.1 Syntax

```ebnf
portDecl   = direction IDENT ":" typeSpec ;
direction  = "input" | "output" | "inout" ;
typeSpec   = domain | bundleType ;
domain     = "analog" | "bias" ;
bundleType = IDENT ;

supplyDecl = "supply" IDENT ;
groundDecl = "ground" IDENT ;
```

#### 4.1.2 Semantics

| Keyword | Direction | Typical Use |
|---------|-----------|-------------|
| `input` | into circuit | Signal inputs, bias inputs |
| `output` | out of circuit | Signal outputs |
| `inout` | bidirectional | I/O pads, transmission gates |
| `supply` | into circuit | Power rails (VDD, AVDD, etc.) |
| `ground` | into circuit | Ground references (GND, VSS, etc.) |

#### 4.1.3 Examples

```acir
circuit OTA5T implements SingleEndedOpAmp
  level EL

  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog
  input VTAIL : bias
```

```acir
circuit FullyDiffOTA implements FullyDifferentialOpAmp
  level EL

  supply AVDD
  supply DVDD
  ground AVSS
  input IN : Diff
  output OUT : Diff
```

### 4.2 Port Abstraction

#### 4.2.1 Concept

A port is a two-terminal access point defined by two nodes. It represents “where” a signal is stimulated or measured, independent of whether the physical quantity is voltage or current.

```
Port(positive_node, negative_node)
```

This maps directly to the network-analysis concept of a port: the voltage across the port is \(v(p) = V(positive) - V(negative)\), and (by convention) current \(i(p)\) flows into the positive terminal.

#### 4.2.2 Across-port quantities are the stable measurement API

Benches and measurement procedures should be written in terms of across-port quantities \(v(p)\) and \(i(p)\), not in terms of whether a circuit’s pins are single-ended or differential. This is the central mechanism that prevents “SE/DIFF variant infection.”

For example, the same transfer measurement applies to both of the following output bindings:

```acir
out = Port(OUT, GND)         # single-ended output
out = Port(OUT.P, OUT.N)     # differential output
```

In both cases, a transfer function primitive such as `transfer(in, out)` is defined as \(v(out)/v(in)\). The bench hookup differs only in which concrete nodes those port terminals map to; measurement extraction remains shared.

#### 4.2.3 Port-shape traits (balanced vs unbalanced)

Some benches need additional degrees of freedom: common-mode excitation, mixed-mode impedance, or output common-mode measurement. Rather than introducing multiple port kinds, the reference vocabulary uses explicit traits that describe the *shape* of the named ports in the selected view:

| Trait | Contract (selected view) |
|---|---|
| `BalancedInput` | The `in` port is bound across two non-reference terminals. |
| `UnbalancedInput` | The `in` port is bound to a reference terminal on one side (typically a `ground`). |
| `BalancedOutput` | The `out` port is bound across two non-reference terminals. |
| `UnbalancedOutput` | The `out` port is bound to a reference terminal on one side. |

These traits are declared and inherited as part of the class/view contract, and implementations are expected to validate obvious inconsistencies at compile time. For example, a view that declares `UnbalancedOutput` must bind `out` to include a declared `ground` terminal, while a view that declares `BalancedOutput` must not bind either side of `out` to a declared `ground` terminal.

#### 4.2.4 Mixed-mode drive and sensing uses adapters, not new benches

For balanced ports, benches may define mixed-mode quantities (differential-mode and common-mode) by specifying a reference node as part of the bench hookup. This does not require separate bench definitions for “single-ended” vs “differential” devices. Instead, the emitter uses a small set of port-driver adapters:

- Drive across a port (works for any `Port(a, b)`; becomes single-ended when \(b\) is a reference).
- Drive common-mode (requires a balanced port; realized by equal drive of both terminals relative to the bench’s reference).

Measurements such as transfer, PSRR, and many impedance forms remain expressed in terms of \(v(p)\) and \(i(p)\). Port-shape traits only gate which stimulation/sense modes are meaningful, and therefore which measurements a bench can legally request.

### 4.3 Port Bindings and Views

#### 4.3.1 Syntax (illustrative)

```ebnf
portsBlock = "ports:" NL INDENT (bindingDecl NL)+ DEDENT ;
bindingDecl = IDENT "=" "Port" "(" nodeRef "," nodeRef ")" ;
nodeRef = IDENT ("." IDENT)? ;  (* e.g., IN.P, OUT, GND *)

viewsBlock = "views:" NL INDENT (viewDecl NL)+ DEDENT ;
viewDecl = IDENT (":" IDENT)? NL INDENT viewBody DEDENT ;
viewBody = traitsBlock? portsBlock? ;
```

#### 4.3.2 Standard port role names

Standard benches use a small set of conventional port role names:

| Role | Meaning |
|---|---|
| `in` | Signal input network port (two-terminal). |
| `out` | Signal output network port (two-terminal). |
| `supply` | Supply perturbation port (two-terminal, typically `Port(VDD, GND)`). |

#### 4.3.3 Examples

A single-ended op-amp class defines its canonical ports once:

```acir
class SingleEndedOpAmp extends Amplifier:
  input IN : Diff
  output OUT : analog
  supply VDD
  ground GND

  ports:
    in = Port(IN.P, IN.N)
    out = Port(OUT, GND)
    supply = Port(VDD, GND)
```

A fully differential class chooses a different binding for `out`:

```acir
class FullyDifferentialOpAmp extends Amplifier:
  traits:
    BalancedInput
    BalancedOutput
    HasSupplyPort

  supply VDD
  ground GND
  input IN : Diff
  output OUT : Diff

  ports:
    in = Port(IN.P, IN.N)
    out = Port(OUT.P, OUT.N)
    supply = Port(VDD, GND)
```

Views provide multiple independent instances of a port/trait bundle:

```acir
class DualChannelAmplifier:
  views:
    ch_a : SingleEndedOpAmp
      ports:
        in = Port(IN_A.P, IN_A.N)
        out = Port(OUT_A, GND)
        supply = Port(VDD, GND)
    ch_b : SingleEndedOpAmp
      ports:
        in = Port(IN_B.P, IN_B.N)
        out = Port(OUT_B, GND)
        supply = Port(VDD, GND)
```

#### 4.3.4 Binding Validation

At compile time:

1. Completeness: if a bench requires a named port role (e.g., `in`), the selected view must bind it.
2. Trait consistency: if a view declares `BalancedInput`/`UnbalancedInput` (etc.), its bindings must match the trait contract.
3. Node existence: referenced terminals must exist and be well-typed for the containing class or circuit.

### 4.4 Measurement Definitions

#### 4.4.1 Syntax

```ebnf
measurementDef = "measurement" IDENT ":" NL INDENT measurementBody DEDENT ;

measurementBody = requiresClause
                  requiresTraitsClause?
                  analysisClause
                  procedureClause
                  preconditionClause?
                  paramsClause?
                  unitClause ;

requiresClause = "requires:" roleList NL ;
roleList = roleDecl ("," roleDecl)* ;
roleDecl = IDENT ":" roleType ;
roleType = "Port" ;

requiresTraitsClause = "requires_traits:" traitList NL ;
traitList = IDENT ("," IDENT)* ;

analysisClause = "analysis:" analysisSpec NL ;
analysisSpec = analysisType | "multi" "(" analysisType ("," analysisType)* ")" ;
analysisType = "ac" | "dc" | "tran" | "noise" | "stb" ;

procedureClause = "procedure:" NL INDENT (procedureStmt NL)+ DEDENT ;

preconditionClause = "precondition:" boolExpr NL ;

paramsClause = "params:" NL INDENT (paramDecl NL)+ DEDENT ;
paramDecl = IDENT ":" paramType "=" defaultValue ;
paramType = "Frequency" | "Voltage" | "Current" | "Time" ;

unitClause = "unit:" UNIT NL ;
```

#### 4.4.2 Role Semantics

Roles are typed abstract names:

| Role Declaration | Meaning |
|------------------|---------|
| `in : Port` | Input network port role for stimulus (two-terminal) |
| `out : Port` | Output network port role for response measurement (two-terminal) |
| `supply : Port` | Supply perturbation port role (two-terminal) |

#### 4.4.3 Single-Simulation Measurements

```acir
measurement PassbandGain:
  requires: in : Port, out : Port
  analysis: ac
  procedure:
    H = transfer(in, out)
    gain = mag_dB(H)
    result = eval(gain, f_measure)
  params:
    f_measure : Frequency = 1kHz
  unit: dB

measurement LowpassBandwidth:
  requires: in : Port, out : Port
  analysis: ac
  procedure:
    H = transfer(in, out)
    gain = mag_dB(H)
    dc_gain = eval(gain, DC)
    result = find_crossing(gain, dc_gain - 3, falling)
  unit: Hz

measurement GainBandwidth:
  requires: in : Port, out : Port
  analysis: ac
  procedure:
    H = transfer(in, out)
    gain = mag_dB(H)
    result = find_crossing(gain, 0, falling)
  precondition: eval(mag_dB(transfer(in, out)), DC) > 0
  unit: Hz

measurement PhaseMargin:
  requires: in : Port, out : Port
  analysis: ac
  procedure:
    H = transfer(in, out)
    gain = mag_dB(H)
    ph = phase(H)
    f_ugf = find_crossing(gain, 0, falling)
    result = 180 + eval(ph, f_ugf)
  precondition: eval(mag_dB(transfer(in, out)), DC) > 0
  unit: deg

measurement QuiescentPower:
  requires: supply : Port
  analysis: dc
  procedure:
    result = abs(port_voltage(supply) * port_current(supply))
  unit: W
```

#### 4.4.4 Multi-Simulation Measurements

Some measurements require multiple simulation runs with different stimulus configurations. The `analysis: multi(...)` clause declares this, and the procedure uses `in <mode>` syntax to reference data from each simulation.

Stimulus Modes (Fixed Vocabulary):

| Mode | Semantics | Applicable To |
|------|-----------|---------------|
| `differential` | Drive across a port such that \(v(port)=1\) (normalized) | `Port` |
| `common_mode` | Equal drive of both terminals relative to the bench reference (normalized) | balanced ports (e.g., `BalancedInput`) |
| `signal` | Alias for `differential` for the signal-path transfer run | `Port` |
| `supply_perturb` | Drive across the `supply` port such that \(v(supply)=1\) (normalized) | `supply : Port` |

```acir
measurement CMRR:
  requires: in : Port, out : Port
  requires_traits: BalancedInput
  stimulus_modes: differential, common_mode
  analysis: multi(ac, ac)
  procedure:
    H_diff = transfer(in, out) in differential
    H_cm = transfer(in, out) in common_mode
    result = eval(mag_dB(H_diff), f) - eval(mag_dB(H_cm), f)
  params:
    f : Frequency = 1kHz
  unit: dB

measurement PSRR:
  requires: supply : Port, out : Port
  stimulus_modes: supply_perturb
  analysis: ac
  procedure:
    H_sup = transfer(supply, out)
    result = -eval(mag_dB(H_sup), f)
  params:
    f : Frequency = 1kHz
  unit: dB
```

#### 4.4.5 Preconditions (Runtime Validity)

The `precondition:` clause specifies a runtime condition that must hold for the measurement to be valid:

```acir
measurement GainBandwidth:
  requires: in : Port, out : Port
  analysis: ac
  procedure:
    # ...
  precondition: eval(mag_dB(transfer(in, out)), DC) > 0
  unit: Hz
```

Semantics:
1. Simulation runs normally
2. Precondition is evaluated
3. If false: measurement result is `NaN`, warning logged
4. Constraint evaluation treats `NaN` as failure

Distinction from `requires:`:
- `requires:` - compile-time structural requirements (ports and declared traits)
- `precondition:` - runtime behavioral requirements (circuit must have gain > 0dB)

### 4.5 Procedure Primitives

#### 4.5.1 Design Principle

The procedure DSL uses a fixed set of primitives with documented type signatures. This enables:
- Unambiguous semantics
- Straightforward code generation
- Future extension without breaking changes

#### 4.5.2 Primitive Definitions

Transfer Function Primitives:

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `transfer(p1, p2)` | `(Port, Port) -> TransferFunction` | Complex voltage transfer function V(p2)/V(p1) |
 
Supply transfer is expressed using the same primitive (for example, `transfer(supply, out)`), with the supply perturbation realized by the bench’s stimulus mode selection.

Function Transformation Primitives:

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `mag(H)` | `TransferFunction -> MagnitudeFunction` | Linear magnitude |H(f)| |
| `mag_dB(H)` | `TransferFunction -> MagnitudeFunction` | Magnitude in dB: 20*log10(|H(f)|) |
| `phase(H)` | `TransferFunction -> PhaseFunction` | Phase in degrees |

Evaluation Primitives:

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `eval(F, f)` | `(Function, Frequency) -> Scalar` | Evaluate function at frequency f |
| `eval(F, DC)` | `(Function, DC) -> Scalar` | Evaluate at DC (f -> 0) |

Search Primitives:

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `find_crossing(F, threshold, direction)` | `(Function, Scalar, Direction) -> Frequency` | Find frequency where F crosses threshold |

Where `direction` is `rising` or `falling`.

Search failure: If no crossing exists, result is `NaN` and a warning is logged.

DC Measurement Primitives:

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `port_voltage(p)` | `Port -> Voltage` | DC voltage across the port: \(v(p)\) |
| `port_current(p)` | `Port -> Current` | DC current into the port’s positive terminal |

Arithmetic:

Standard arithmetic operators (`+`, `-`, `*`, `/`) and `abs()` are available for `Scalar` values.

#### 4.5.3 Operator Precedence

For arithmetic expressions:

| Precedence | Operators | Associativity |
|------------|-----------|---------------|
| 1 (highest) | unary `-` | right |
| 2 | `*`, `/` | left |
| 3 (lowest) | `+`, `-` | left |

Parentheses override precedence.

#### 4.5.4 Type Checking

Procedures are type-checked at compile time:

```acir
procedure:
  H = transfer(in, out)         # H : TransferFunction
  gain = mag_dB(H)              # gain : MagnitudeFunction  
  dc_gain = eval(gain, DC)      # dc_gain : Scalar
  result = dc_gain + 3          # Scalar + Scalar -> Scalar (ok)
```

Type errors:
```acir
procedure:
  H = transfer(in, out)
  result = H + 3                # ERROR: TransferFunction + Scalar undefined
```

### 4.6 Bench Definitions

#### 4.6.1 Syntax

```ebnf
benchDef = "bench" IDENT ":" NL INDENT benchBody DEDENT ;
benchBody = measurementsBlock ;
measurementsBlock = "measurements:" NL INDENT (IDENT NL)+ DEDENT ;
```

#### 4.6.2 Semantics

A bench groups related measurements:

```acir
bench ACBench:
  measurements:
    PassbandGain
    LowpassBandwidth
    HighpassBandwidth
    GainBandwidth
    PhaseMargin
    GainMargin
    CMRR
    PSRR

bench DCBench:
  measurements:
    QuiescentPower
    TotalQuiescentPower

bench NoiseBench:
  measurements:
    InputReferredNoise
    SpotNoise
```

Note: Benches no longer have a `for Trait` clause. Applicability is determined by whether the selected view provides the named ports in `requires:` and the declared capabilities in `requires_traits:` for each referenced measurement.

#### 4.6.3 Measurement Filtering

When a circuit references a bench in constraints:

1. Applicable measurements: Requirements satisfied by the selected view -> included
2. Not referenced by constraints: may be excluded from emission (even if applicable)
3. Referenced in a constraint but inapplicable: compile error

### 4.7 Constraints

#### 4.7.1 Syntax

```ebnf
constraintsBlock = "constraints:" NL INDENT (constraint NL)+ DEDENT ;
constraint = IDENT "=" constraintExpr ;
constraintExpr = measurementRef comparator value ;
measurementRef = IDENT viewSelector? "::" IDENT paramOverrides? ;
viewSelector = "[" IDENT "]" ;
paramOverrides = "(" paramAssign ("," paramAssign)* ")" ;
paramAssign = IDENT "=" value ;
comparator = ">=" | "<=" | ">" | "<" | "==" ;
```

#### 4.7.2 Examples

```acir
constraints:
  c_gain = ACBench::PassbandGain >= 40dB
  c_gain_a = ACBench[ch_a]::PassbandGain >= 40dB
  c_gbw = ACBench::GainBandwidth >= 100MHz
  c_pm = ACBench::PhaseMargin >= 60deg
  c_cmrr = ACBench::CMRR >= 60dB
  c_cmrr_hf = ACBench::CMRR(f=1MHz) >= 40dB
  c_psrr = ACBench::PSRR >= 60dB
  c_power = DCBench::QuiescentPower <= 100uW
```

#### 4.7.3 Parameter Overrides

Measurement parameters can be overridden at the constraint level:

```acir
c_cmrr_1k = ACBench::CMRR(f=1kHz) >= 60dB
c_cmrr_1M = ACBench::CMRR(f=1MHz) >= 40dB
```

### 4.8 Harness

#### 4.8.1 Syntax

```ebnf
harnessBlock = "harness:" NL INDENT (harnessEntry NL)+ DEDENT ;
harnessEntry = supplyEntry | biasEntry | sourceEntry | loadEntry | pvtEntry ;

supplyEntry = "supply" IDENT "=" value ;
biasEntry = "bias" IDENT "=" value ;
sourceEntry = "source" IDENT impedanceSpec ;
loadEntry = "load" IDENT loadSpec ;
impedanceSpec = "Z" "=" value ;
loadSpec = ("C" "=" value)? ("R" "=" value)? ("L" "=" value)? ;
pvtEntry = "pvt" pvtList ;
pvtList = pvtPoint ("," pvtPoint)* ;
pvtPoint = IDENT "@" TEMPERATURE ;
```

#### 4.8.2 Examples

```acir
harness:
  supply VDD = 1.8V
  bias VTAIL = 0.6V
  source IN Z=50Ohm
  load OUT C=1pF
  pvt TT@27C, SS@-40C, FF@125C
```

```acir
harness:
  supply AVDD = 1.8V
  supply DVDD = 1.8V
  load OUT C=500fF R=10k
```

### 4.9 Applicability Resolution

#### 4.9.1 Algorithm

```python
def resolve_applicability(circuit, bench, constraints, *, view_name="main"):
    """
    Determine which measurements apply for a selected view and validate constraints.

    In practice, applicability is resolved per unique (bench, view) pair referenced by
    constraints such as `ACBench[ch_a]::PassbandGain`.
    """
    view = circuit.views.get(view_name, circuit.views["main"])
    ports = view.ports          # dict: role name -> Port(a, b)
    traits = view.traits        # set: declared traits for this view

    applicable = {}
    errors = []

    for measurement in bench.measurements:
        # Port-role requirements
        if any(role_name not in ports for role_name in measurement.requires_ports):
            applicable[measurement.name] = False
            continue

        # Trait requirements
        if not measurement.requires_traits.issubset(traits):
            applicable[measurement.name] = False
            continue

        applicable[measurement.name] = True

    # Validate constraints reference applicable measurements on the chosen view
    for constraint in constraints:
        meas_name = constraint.measurement_name
        if meas_name not in applicable:
            errors.append(f"Unknown measurement: {meas_name}")
        elif not applicable[meas_name]:
            errors.append(
                f"Measurement {meas_name} is not applicable on view '{view.name}'."
            )

    return applicable, errors
```

#### 4.9.2 Error Messages

Clear diagnostics for inapplicable measurements:

```
error[ACIR0042]: measurement CMRR is not applicable to circuit RCLowpass
  --> RCLowpass.cir:15:5
   |
15 |     c_cmrr : ACBench::CMRR >= 60dB
   |     ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
   |
note: CMRR requires traits: BalancedInput
note: selected view 'main' declares: UnbalancedInput
help: CMRR requires a balanced input port; choose a different view/class or remove the constraint
```

### 4.10 Failure Handling

#### 4.10.1 Failure Modes

| Failure | When | Result | Behavior |
|---------|------|--------|----------|
| Compile-time type error | Role type mismatch | N/A | Compile error, no simulation |
| Missing binding | Required port role not bound in selected view | N/A | Compile error |
| Precondition failure | Runtime condition false | `NaN` | Warning logged, constraint fails |
| Search failure | No crossing found | `NaN` | Warning logged, constraint fails |
| Simulation error | Simulator fails | `NaN` | Error logged, constraint fails |

#### 4.10.2 Partial Results

When a testbench contains multiple measurements:

1. All measurements are attempted
2. Failures in one measurement do not prevent others from running
3. Results are reported per-measurement with status (success/failure/skipped)
4. Overall constraint satisfaction requires all referenced measurements to succeed

#### 4.10.3 Result Structure

```
TestbenchResults:
  circuit: OTA5T
  bench: ACBench
  measurements:
    PassbandGain: 52.3 dB [SUCCESS]
    GainBandwidth: 156 MHz [SUCCESS]
    PhaseMargin: 62.1 deg [SUCCESS]
    CMRR: 68.4 dB [SUCCESS]
    PSRR: NaN [PRECONDITION_FAILED: "supply not perturbed"]
  constraints:
    c_gain: PASS (52.3 >= 40)
    c_gbw: PASS (156 >= 100)
    c_pm: PASS (62.1 >= 60)
    c_cmrr: PASS (68.4 >= 60)
```

---

## 5. Emission Pipeline

### 5.1 Design Decision: Programmatic Generation

This RFC mandates programmatic testbench generation in C#, not templates.

Rationale:
1. Templates with conditionals become unmaintainable at scale
2. C# provides type safety, testability, and refactoring support
3. Testbench generation logic can share code with measurement calculation
4. New backends require only a new emitter class, not a template language port

### 5.2 Architecture


![Emission Pipeline](../../resources/0000/0000-emission-flow.svg)




```
direction: right

ACIR_Document: "ACIR Document"
Binding_Resolution: "Binding Resolution"
TestbenchModel: "TestbenchModel"

ITestbenchEmitter: "ITestbenchEmitter\n(per backend)"
ngspice: "ngspice\nEmitter"
Spectre: "Spectre\nEmitter"
Xyce: "Xyce\nEmitter"

ACIR_Document -> Binding_Resolution -> TestbenchModel
TestbenchModel -> ITestbenchEmitter
ITestbenchEmitter -> ngspice
ITestbenchEmitter -> Spectre
ITestbenchEmitter -> Xyce
```

### 5.3 TestbenchModel

```csharp
public class TestbenchModel
{
    public string CircuitName { get; }
    public string BenchName { get; }
    public string ViewName { get; }
    
    // From the selected view (`ports:`): role name -> Port(a, b)
    public IReadOnlyDictionary<string, PortBinding> Ports { get; }

    // Declared capabilities of the selected view
    public IReadOnlySet<string> Traits { get; }
    
    // Measurements to run (filtered by applicability)
    public List<ResolvedMeasurement> Measurements { get; }
    
    // From harness block
    public HarnessConfig Harness { get; }
}

public class PortBinding
{
    public string PositiveNode { get; }
    public string NegativeNode { get; }
}

public class ResolvedMeasurement
{
    public MeasurementDefinition Definition { get; }
    public Dictionary<string, object> Parameters { get; }
    public List<StimulusMode> RequiredModes { get; }
}

public enum StimulusMode
{
    Differential,
    CommonMode,
    SupplyPerturb
}
```

### 5.4 ITestbenchEmitter Interface

```csharp
public interface ITestbenchEmitter
{
    /// <summary>
    /// Emit testbench file(s) for the given model.
    /// May emit multiple files for multi-simulation measurements.
    /// </summary>
    EmittedTestbench Emit(TestbenchModel model);
    
    /// <summary>
    /// Parse simulation results from the backend's output.
    /// </summary>
    SimulationResults ParseResults(string outputPath, TestbenchModel model);
}

public class EmittedTestbench
{
    public Dictionary<StimulusMode, string> NetlistsByMode { get; }
    public string ControlScript { get; }  // For simulators that support scripting
}
```

### 5.5 NgspiceEmitter (Sketch)

```csharp
public class NgspiceEmitter : ITestbenchEmitter
{
    public EmittedTestbench Emit(TestbenchModel model)
    {
        var result = new EmittedTestbench();
        
        // Determine which stimulus modes are needed
        var modes = model.Measurements
            .SelectMany(m => m.RequiredModes)
            .Distinct()
            .ToList();
        
        foreach (var mode in modes)
        {
            var netlist = EmitNetlistForMode(model, mode);
            result.NetlistsByMode[mode] = netlist;
        }
        
        return result;
    }
    
    private string EmitNetlistForMode(TestbenchModel model, StimulusMode mode)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"* Testbench: {model.BenchName} for {model.CircuitName}");
        sb.AppendLine($"* Stimulus mode: {mode}");
        sb.AppendLine();
        
        EmitIncludes(sb, model);
        EmitSupplies(sb, model);
        EmitStimulus(sb, model, mode);
        EmitLoad(sb, model);
        EmitDutInstance(sb, model);
        EmitAnalysis(sb, model);
        EmitMeasurements(sb, model, mode);
        
        sb.AppendLine(".end");
        return sb.ToString();
    }
    
    private void EmitStimulus(StringBuilder sb, TestbenchModel model, StimulusMode mode)
    {
        var stim = model.Stim;
        
        switch (mode)
        {
            case StimulusMode.Differential:
                sb.AppendLine($"* Differential stimulus");
                sb.AppendLine($"V_STIM_P {stim.PositiveNode} v_cm DC {{v_cm}} AC 0.5");
                sb.AppendLine($"V_STIM_N {stim.NegativeNode} v_cm DC {{v_cm}} AC -0.5");
                sb.AppendLine($"V_CM v_cm 0 DC {{v_cm}}");
                break;
                
            case StimulusMode.CommonMode:
                sb.AppendLine($"* Common-mode stimulus");
                sb.AppendLine($"V_STIM_P {stim.PositiveNode} 0 DC {{v_cm}} AC 1");
                sb.AppendLine($"V_STIM_N {stim.NegativeNode} 0 DC {{v_cm}} AC 1");
                break;
                
            case StimulusMode.Signal:
            default:
                if (stim.IsDifferential)
                {
                    sb.AppendLine($"* Differential stimulus (signal mode)");
                    sb.AppendLine($"V_STIM_P {stim.PositiveNode} v_cm DC {{v_cm}} AC 0.5");
                    sb.AppendLine($"V_STIM_N {stim.NegativeNode} v_cm DC {{v_cm}} AC -0.5");
                    sb.AppendLine($"V_CM v_cm 0 DC {{v_cm}}");
                }
                else
                {
                    sb.AppendLine($"* Single-ended stimulus");
                    sb.AppendLine($"V_STIM {stim.PositiveNode} {stim.NegativeNode} DC {{v_cm}} AC 1");
                }
                break;
        }
    }
    
    private void EmitMeasurements(StringBuilder sb, TestbenchModel model, StimulusMode mode)
    {
        var resp = model.Resp;
        string respExpr = resp.IsDifferential 
            ? $"{{v({resp.PositiveNode})-v({resp.NegativeNode})}}"
            : resp.PositiveNode;
        
        foreach (var meas in model.Measurements.Where(m => m.RequiredModes.Contains(mode)))
        {
            EmitMeasurement(sb, meas, respExpr);
        }
    }
    
    private void EmitMeasurement(StringBuilder sb, ResolvedMeasurement meas, string respExpr)
    {
        // Emit simulator-specific measurement commands based on primitives
        // This is where procedure primitives map to ngspice .meas statements
        
        switch (meas.Definition.Name)
        {
            case "PassbandGain":
                var f = meas.Parameters.GetValueOrDefault("f_measure", 1e3);
                sb.AppendLine($".meas ac PassbandGain find vdb({respExpr}) at={f}");
                break;
                
            case "GainBandwidth":
                sb.AppendLine($".meas ac GainBandwidth when vdb({respExpr})=0 fall=1");
                break;
                
            case "PhaseMargin":
                sb.AppendLine($".meas ac f_ugf when vdb({respExpr})=0 fall=1");
                sb.AppendLine($".meas ac PhaseMargin find par('180+vp({respExpr})') at=f_ugf");
                break;
                
            // Additional measurements...
        }
    }
}
```

### 5.6 Multi-Simulation Orchestration

```csharp
public class MeasurementRunner
{
    private readonly ITestbenchEmitter _emitter;
    private readonly ISimulatorInvoker _simulator;
    
    public async Task<TestbenchResults> RunAsync(TestbenchModel model)
    {
        var results = new TestbenchResults(model.CircuitName, model.BenchName);
        var emitted = _emitter.Emit(model);
        
        // Run each stimulus mode
        var simDataByMode = new Dictionary<StimulusMode, SimulationData>();
        
        foreach (var (mode, netlist) in emitted.NetlistsByMode)
        {
            try
            {
                var output = await _simulator.RunAsync(netlist);
                simDataByMode[mode] = _emitter.ParseResults(output, model);
            }
            catch (SimulationException ex)
            {
                results.AddError(mode, ex.Message);
            }
        }
        
        // Compute measurements
        foreach (var meas in model.Measurements)
        {
            var calculator = GetCalculator(meas.Definition);
            try
            {
                var value = calculator.Calculate(simDataByMode, meas.Parameters);
                results.AddMeasurement(meas.Definition.Name, value, MeasurementStatus.Success);
            }
            catch (PreconditionFailedException ex)
            {
                results.AddMeasurement(meas.Definition.Name, double.NaN, 
                    MeasurementStatus.PreconditionFailed, ex.Message);
            }
            catch (SearchFailedException ex)
            {
                results.AddMeasurement(meas.Definition.Name, double.NaN,
                    MeasurementStatus.SearchFailed, ex.Message);
            }
        }
        
        return results;
    }
}
```

---

## 6. Standard Library

### 6.1 Library Structure

```
lib/
|-- std.acir                     # Meta-include
|-- interfaces/
|   `-- standard.acir            # Standard traits + classes (taxonomy + ports)
|-- measurements/
|   `-- standard.acir            # Standard measurement definitions
`-- benches/
    `-- standard.acir            # Standard bench definitions
```

The `lib/std.acir` file includes all standard definitions:

```acir
include lib/interfaces/standard
include lib/measurements/standard
include lib/benches/standard
```

### 6.2 Standard Interfaces (Traits and Classes)

File: `lib/interfaces/standard.acir`

```acir
// ============================================================
// Traits (capabilities)
// ============================================================

trait HasInputPort
trait HasOutputPort
trait TransferTwoPort

trait BalancedInput
trait UnbalancedInput
trait BalancedOutput
trait UnbalancedOutput

trait HasSupplyPort
trait Multiport

// ============================================================
// Classes (taxonomy + canonical port bindings)
// ============================================================

class Filter:
  traits:
    HasInputPort
    HasOutputPort
    TransferTwoPort

class SingleEndedFilter extends Filter:
  traits:
    UnbalancedInput
    UnbalancedOutput

  ground GND
  input IN : analog
  output OUT : analog

  ports:
    in = Port(IN, GND)
    out = Port(OUT, GND)

class FullyDifferentialFilter extends Filter:
  traits:
    BalancedInput
    BalancedOutput

  ground GND
  input IN : Diff
  output OUT : Diff

  ports:
    in = Port(IN.P, IN.N)
    out = Port(OUT.P, OUT.N)

class FullyDifferentialSallenKeyFilter extends FullyDifferentialFilter:
  traits:
    HasSupplyPort

  supply VDD

  ports:
    supply = Port(VDD, GND)

class Amplifier:
  traits:
    HasInputPort
    HasOutputPort
    TransferTwoPort
    HasSupplyPort

class SingleEndedAmplifier extends Amplifier:
  traits:
    UnbalancedInput
    UnbalancedOutput

  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  ports:
    in = Port(IN, GND)
    out = Port(OUT, GND)
    supply = Port(VDD, GND)

class SingleEndedOpAmp extends Amplifier:
  traits:
    BalancedInput
    UnbalancedOutput

  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog

  ports:
    in = Port(IN.P, IN.N)
    out = Port(OUT, GND)
    supply = Port(VDD, GND)

class FullyDifferentialOpAmp extends Amplifier:
  traits:
    BalancedInput
    BalancedOutput

  supply VDD
  ground GND
  input IN : Diff
  output OUT : Diff

  ports:
    in = Port(IN.P, IN.N)
    out = Port(OUT.P, OUT.N)
    supply = Port(VDD, GND)

class VoltageReference:
  traits:
    HasOutputPort
    HasSupplyPort
    UnbalancedOutput

  supply VDD
  ground GND
  output VREF : analog

  ports:
    out = Port(VREF, GND)
    supply = Port(VDD, GND)

class CurrentReference:
  traits:
    HasOutputPort
    HasSupplyPort
    UnbalancedOutput

  supply VDD
  ground GND
  output IOUT : analog

  ports:
    out = Port(IOUT, GND)
    supply = Port(VDD, GND)
```

### 6.3 Standard Measurements

File: `lib/measurements/standard.acir`

```acir
// ============================================================
// Transfer Function Measurements
// ============================================================

measurement PassbandGain:
  requires: in : Port, out : Port
  analysis: ac
  procedure:
    H = transfer(in, out)
    gain = mag_dB(H)
    result = eval(gain, f_measure)
  params:
    f_measure : Frequency = 1kHz
  unit: dB

measurement LowpassBandwidth:
  requires: in : Port, out : Port
  analysis: ac
  procedure:
    H = transfer(in, out)
    gain = mag_dB(H)
    dc_gain = eval(gain, DC)
    result = find_crossing(gain, dc_gain - 3, falling)
  unit: Hz

measurement HighpassBandwidth:
  requires: in : Port, out : Port
  analysis: ac
  procedure:
    H = transfer(in, out)
    gain = mag_dB(H)
    hf_gain = eval(gain, f_high)
    result = find_crossing(gain, hf_gain - 3, rising)
  params:
    f_high : Frequency = 1GHz
  unit: Hz

measurement GainBandwidth:
  requires: in : Port, out : Port
  analysis: ac
  procedure:
    H = transfer(in, out)
    gain = mag_dB(H)
    result = find_crossing(gain, 0, falling)
  precondition: eval(mag_dB(transfer(in, out)), DC) > 0
  unit: Hz

measurement PhaseMargin:
  requires: in : Port, out : Port
  analysis: ac
  procedure:
    H = transfer(in, out)
    gain = mag_dB(H)
    ph = phase(H)
    f_ugf = find_crossing(gain, 0, falling)
    result = 180 + eval(ph, f_ugf)
  precondition: eval(mag_dB(transfer(in, out)), DC) > 0
  unit: deg

measurement GainMargin:
  requires: in : Port, out : Port
  analysis: ac
  procedure:
    H = transfer(in, out)
    gain = mag_dB(H)
    ph = phase(H)
    f_180 = find_crossing(ph, -180, falling)
    result = 0 - eval(gain, f_180)
  precondition: eval(mag_dB(transfer(in, out)), DC) > 0
  unit: dB

// ============================================================
// Rejection Measurements (Multi-Simulation)
// ============================================================

measurement CMRR:
  requires: in : Port, out : Port
  requires_traits: BalancedInput
  stimulus_modes: differential, common_mode
  analysis: multi(ac, ac)
  procedure:
    H_diff = transfer(in, out) in differential
    H_cm = transfer(in, out) in common_mode
    gain_diff = mag_dB(H_diff)
    gain_cm = mag_dB(H_cm)
    result = eval(gain_diff, f) - eval(gain_cm, f)
  params:
    f : Frequency = 1kHz
  unit: dB

measurement PSRR:
  requires: supply : Port, out : Port
  stimulus_modes: supply_perturb
  analysis: ac
  procedure:
    H_sup = transfer(supply, out) in supply_perturb
    gain_sup = mag_dB(H_sup)
    result = 0 - eval(gain_sup, f)
  params:
    f : Frequency = 1kHz
  unit: dB

// ============================================================
// Power Measurements
// ============================================================

measurement QuiescentPower:
  requires: supply : Port
  analysis: dc
  procedure:
    result = abs(port_voltage(supply) * port_current(supply))
  unit: W
```

### 6.4 Standard Benches

File: `lib/benches/standard.acir`

```acir
bench ACBench:
  measurements:
    PassbandGain
    LowpassBandwidth
    HighpassBandwidth
    GainBandwidth
    PhaseMargin
    GainMargin
    CMRR
    PSRR

bench DCBench:
  measurements:
    QuiescentPower
```

---

## 7. Complete Examples

### 7.1 Passive filters: single-ended RC and differential RC (same benches)

```acir
ACIR 3.0

include lib/std

// ------------------------------------------------------------
// Single-ended RC lowpass
// - Uses `SingleEndedFilter` (UnbalancedInput/UnbalancedOutput)
// - Reuses the same transfer measurements as amplifiers
// ------------------------------------------------------------

circuit SE_RCLowpass implements SingleEndedFilter
  level EL

  ground GND
  input IN : analog
  output OUT : analog

  fill:
    resistor R1 (.P--IN, .N--OUT) : resistor
      R = 10k
    capacitor C1 (.P--OUT, .N--GND) : capacitor
      C = 1n

  constraints:
    c_gain = ACBench::PassbandGain >= -1dB
    c_bw = ACBench::LowpassBandwidth >= 10kHz
    // CMRR is not meaningful here because `SingleEndedFilter` declares UnbalancedInput.

  harness:
    source IN Z=50Ohm

// ------------------------------------------------------------
// Differential RC lowpass (balanced I/O)
// - Uses `FullyDifferentialFilter` (BalancedInput/BalancedOutput)
// - Uses the same ACBench transfer logic; only the port bindings differ
// ------------------------------------------------------------

circuit FD_RCLowpass implements FullyDifferentialFilter
  level EL

  ground GND
  input IN : Diff
  output OUT : Diff

  fill:
    // A simple differential lowpass realized as two symmetric RC halves to ground.
    // The bench observes the differential output across OUT.P/OUT.N.
    resistor RP (.P--IN.P, .N--OUT.P) : resistor
      R = 10k
    resistor RN (.P--IN.N, .N--OUT.N) : resistor
      R = 10k
    capacitor CP (.P--OUT.P, .N--GND) : capacitor
      C = 1n
    capacitor CN (.P--OUT.N, .N--GND) : capacitor
      C = 1n

  constraints:
    c_gain = ACBench::PassbandGain >= -1dB
    c_bw = ACBench::LowpassBandwidth >= 10kHz
    c_cmrr = ACBench::CMRR(f=10kHz) >= 60dB

  harness:
    source IN Z=50Ohm
```

### 7.2 Powered filter: fully-differential Sallen–Key (powered + ICMR domain)

```acir
ACIR 3.0

include lib/std

// A powered filter is still a transfer two-port, but it is also supply-dependent.
// The filter contains an amplifier core, so constraints are commonly evaluated over
// an input common-mode domain (ICMR) and PVT.

circuit FD_SallenKeyLP implements FullyDifferentialSallenKeyFilter
  level HL

  supply VDD
  ground GND
  input IN : Diff
  output OUT : Diff

  // The active core is left as a slot at HL and must be bound to a fully
  // differential op-amp (or another class carrying the same trait bundle).
  slot core (.IN--IN, .OUT--OUT, .VDD--VDD, .GND--GND) : FullyDifferentialOpAmp

  // Passive network components are concrete even at HL in many flows.
  slot R1 (.P--IN.P, .N--OUT.P) : [Resistor]
  slot R2 (.P--IN.N, .N--OUT.N) : [Resistor]
  slot C1 (.P--OUT.P, .N--GND) : [Capacitor]
  slot C2 (.P--OUT.N, .N--GND) : [Capacitor]

  constraints:
    c_gain = ACBench::PassbandGain(f_measure=10kHz) >= -1dB
    c_bw = ACBench::LowpassBandwidth >= 1MHz
    c_psrr = ACBench::PSRR(f=100kHz) >= 50dB

  harness:
    supply VDD = 1.8V
    load OUT C=500fF R=10k
    pvt TT@27C, SS@-40C, FF@125C
    // Evaluated over input common-mode domain (ICMR):
    // sweep IN.Vcm in [0.6V:1.2V]
```

### 7.3 Amplifiers: SE amplifier, SE op-amp, and fully-differential op-amp (shared benches)

```acir
ACIR 3.0

include lib/std

// ------------------------------------------------------------
// Single-ended amplifier: UnbalancedInput + UnbalancedOutput
// ------------------------------------------------------------

circuit SE_Amp implements SingleEndedAmplifier
  level HL

  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  constraints:
    c_gain = ACBench::PassbandGain(f_measure=1kHz) >= 20dB
    c_bw = ACBench::LowpassBandwidth >= 10MHz
    c_psrr = ACBench::PSRR(f=10kHz) >= 40dB

  harness:
    supply VDD = 1.8V
    load OUT C=1pF R=10k

// ------------------------------------------------------------
// Single-ended op-amp: BalancedInput + UnbalancedOutput
// - CMRR is applicable because the class declares BalancedInput
// ------------------------------------------------------------

circuit OTA5T implements SingleEndedOpAmp
  level EL

  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog
  input VTAIL : bias

  fill:
    net mirror_gate : analog
    net tnode : analog

    nmos dp.M_N (.G--IN.P, .D--mirror_gate, .S--tnode, .B--GND) : nfet_01v8
      size (W=2u, L=180n, M=1)
    nmos dp.M_P (.G--IN.N, .D--OUT, .S--tnode, .B--GND) : nfet_01v8
      size (W=2u, L=180n, M=1)
    nmos dp.M_TAIL (.G--VTAIL, .D--tnode, .S--GND, .B--GND) : nfet_01v8
      size (W=4u, L=180n, M=1)
    pmos cm.M_SENSE (.G--mirror_gate, .D--mirror_gate, .S--VDD, .B--VDD) : pfet_01v8
      size (W=2u, L=180n, M=1)
    pmos cm.M_TAP0 (.G--mirror_gate, .D--OUT, .S--VDD, .B--VDD) : pfet_01v8
      size (W=2u, L=180n, M=1)

  constraints:
    c_gbw = ACBench::GainBandwidth >= 100MHz
    c_gain = ACBench::PassbandGain >= 50dB
    c_pm = ACBench::PhaseMargin >= 60deg
    c_cmrr = ACBench::CMRR >= 60dB
    c_psrr = ACBench::PSRR >= 60dB
    c_power = DCBench::QuiescentPower <= 100uW

  harness:
    supply VDD = 1.8V
    bias VTAIL = 0.6V
    load OUT C=1pF
    pvt TT@27C
    // Evaluated over input common-mode domain (ICMR):
    // sweep IN.Vcm in [0.6V:1.2V]

// ------------------------------------------------------------
// Fully differential op-amp with multi-supply PSRR via views
// - A single design exports multiple PSRR measurement interfaces (views),
//   each overriding only the `supply` binding.
// ------------------------------------------------------------

circuit MixedSupplyOTA implements FullyDifferentialOpAmp
  level EL

  supply AVDD
  supply DVDD
  ground VSS
  input IN : Diff
  output OUT : Diff

  // Default view uses AVDD.
  ports:
    supply = Port(AVDD, VSS)

  views:
    psrr_avdd : FullyDifferentialOpAmp
      ports:
        supply = Port(AVDD, VSS)
    psrr_dvdd : FullyDifferentialOpAmp
      ports:
        supply = Port(DVDD, VSS)

  fill:
    // ... device instantiations ...

  constraints:
    c_psrr_a = ACBench[psrr_avdd]::PSRR(f=10kHz) >= 70dB
    c_psrr_d = ACBench[psrr_dvdd]::PSRR(f=10kHz) >= 50dB

  harness:
    supply AVDD = 1.8V
    supply DVDD = 1.8V
    load OUT C=500fF
```

### 7.4 References: bandgap voltage reference and current reference (PSRR without inputs)

```acir
ACIR 3.0

include lib/std

// Voltage reference: no signal input, but has `out` and `supply`.
circuit BandgapRef implements VoltageReference
  level HL

  supply VDD
  ground GND
  output VREF : analog

  constraints:
    c_psrr = ACBench::PSRR(f=1kHz) >= 60dB
    c_power = DCBench::QuiescentPower <= 200uW

  harness:
    supply VDD = 1.8V
    load VREF R=1Meg C=1pF
    pvt TT@27C, SS@-40C, FF@125C

// Current reference: no signal input. The bench provides a compliance/load condition
// via the harness (e.g., Rload to ground) so output current can be interpreted.
circuit CurrentRef implements CurrentReference
  level HL

  supply VDD
  ground GND
  output IOUT : analog

  constraints:
    c_psrr = ACBench::PSRR(f=1kHz) >= 50dB
    c_power = DCBench::QuiescentPower <= 200uW

  harness:
    supply VDD = 1.8V
    load IOUT R=10k
    pvt TT@27C, SS@-40C, FF@125C
```

### 7.5 Multiport extension: IQ example using views as projections

```acir
ACIR 3.0

include lib/std

class IQPolyphaseFilter:
  traits:
    Multiport

  ground GND
  input IN_I : Diff
  input IN_Q : Diff
  output OUT_I : Diff
  output OUT_Q : Diff

  // The class is multiport, but it exports two two-port projections as views so
  // existing two-port benches can be reused without introducing a new template family.
  views:
    i_path : FullyDifferentialFilter
      ports:
        in = Port(IN_I.P, IN_I.N)
        out = Port(OUT_I.P, OUT_I.N)
    q_path : FullyDifferentialFilter
      ports:
        in = Port(IN_Q.P, IN_Q.N)
        out = Port(OUT_Q.P, OUT_Q.N)

circuit IQ_Filter implements IQPolyphaseFilter
  level HL

  ground GND
  input IN_I : Diff
  input IN_Q : Diff
  output OUT_I : Diff
  output OUT_Q : Diff

  constraints:
    c_i_gain = ACBench[i_path]::PassbandGain(f_measure=10kHz) >= -1dB
    c_q_gain = ACBench[q_path]::PassbandGain(f_measure=10kHz) >= -1dB

  harness:
    source IN_I Z=50Ohm
    source IN_Q Z=50Ohm
```

### 7.6 Non-standard terminal names: adapter circuit (no per-bench glue)

```acir
ACIR 3.0

include lib/std

// A hardmacro-like circuit with fixed, non-canonical pin names.
circuit LegacyAmpHardmacro
  level HL

  supply VCC
  ground VSS
  input VINP : analog
  input VINN : analog
  output VOUT : analog

  slot core (.VCC--VCC, .VSS--VSS, .VINP--VINP, .VINN--VINN, .VOUT--VOUT) : [BlackBox]

// Adapter: implements the canonical class and maps pins in its body.
circuit LegacyAmpAdapter implements SingleEndedOpAmp
  level EL

  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog

  fill:
    inst U1 (.VCC--VDD, .VSS--GND, .VINP--IN.P, .VINN--IN.N, .VOUT--OUT) : LegacyAmpHardmacro

  constraints:
    c_gain = ACBench::PassbandGain >= 30dB
    c_cmrr = ACBench::CMRR(f=10kHz) >= 50dB
    c_psrr = ACBench::PSRR >= 40dB

  harness:
    supply VDD = 3.3V
    load OUT C=5pF R=10k
```

---

## 8. Grammar Specification

```ebnf
(* ============================================================ *)
(* Top-level *)
(* ============================================================ *)

document = header (include)* (definition)* ;
header = "ACIR" VERSION NL ;
include = "include" PATH NL ;
definition = traitDef | classDef | measurementDef | benchDef | circuitDef ;

(* ============================================================ *)
(* Traits *)
(* ============================================================ *)

traitDef = "trait" IDENT NL ;
extendsClause = "extends" IDENT ;

(* ============================================================ *)
(* Classes *)
(* ============================================================ *)

classDef = "class" IDENT extendsClause? ":" NL INDENT classBody DEDENT ;
classBody = traitsBlock? (classMember NL)* ;
classMember = supplyDecl | groundDecl | portDecl | portsBlock | viewsBlock | "pass" ;

traitsBlock = "traits:" NL INDENT (IDENT NL)+ DEDENT ;
portsBlock = "ports:" NL INDENT (bindingDecl NL)+ DEDENT ;
bindingDecl = IDENT "=" portBinding ;
portBinding = "Port" "(" nodeRef "," nodeRef ")" ;

viewsBlock = "views:" NL INDENT (viewDecl NL)+ DEDENT ;
viewDecl = IDENT (":" IDENT)? NL INDENT viewBody DEDENT ;
viewBody = traitsBlock? portsBlock? ;

connectorsBlock = "connectors:" NL INDENT (connectorEntry NL)+ DEDENT ;
connectorEntry = "to" IDENT ":" NL INDENT (connectionStmt NL)+ DEDENT ;
connectionStmt = nodeRef "--" nodeRef ;

(* ============================================================ *)
(* Port Declarations *)
(* ============================================================ *)

portDecl = direction IDENT ":" typeSpec ;
direction = "input" | "output" | "inout" ;
typeSpec = domain | bundleType ;
domain = "analog" | "bias" ;
bundleType = IDENT ;

supplyDecl = "supply" IDENT ;
groundDecl = "ground" IDENT ;

(* ============================================================ *)
(* Measurements *)
(* ============================================================ *)

measurementDef = "measurement" IDENT ":" NL INDENT measurementBody DEDENT ;

measurementBody = requiresClause
                  requiresTraitsClause?
                  stimulusModesClause?
                  analysisClause
                  procedureClause
                  preconditionClause?
                  paramsClause?
                  unitClause ;

requiresClause = "requires:" roleList NL ;
roleList = roleDecl ("," roleDecl)* ;
roleDecl = IDENT ":" roleType ;
roleType = "Port" ;

requiresTraitsClause = "requires_traits:" traitList NL ;
traitList = IDENT ("," IDENT)* ;

stimulusModesClause = "stimulus_modes:" modeList NL ;
modeList = IDENT ("," IDENT)* ;

analysisClause = "analysis:" analysisSpec NL ;
analysisSpec = analysisType | multiAnalysis ;
analysisType = "ac" | "dc" | "tran" | "noise" | "stb" ;
multiAnalysis = "multi" "(" analysisType ("," analysisType)* ")" ;

procedureClause = "procedure:" NL INDENT (procedureStmt NL)+ DEDENT ;
procedureStmt = assignment | resultStmt ;
assignment = IDENT "=" expr ;
resultStmt = "result" "=" expr ;

preconditionClause = "precondition:" boolExpr NL ;

paramsClause = "params:" NL INDENT (paramDecl NL)+ DEDENT ;
paramDecl = IDENT ":" paramType "=" defaultValue ;
paramType = "Frequency" | "Voltage" | "Current" | "Time" ;

unitClause = "unit:" UNIT NL ;

(* ============================================================ *)
(* Procedure Expressions *)
(* ============================================================ *)

expr = additiveExpr ;
additiveExpr = multiplicativeExpr (("+"|"-") multiplicativeExpr)* ;
multiplicativeExpr = unaryExpr (("*"|"/") unaryExpr)* ;
unaryExpr = "-"? primaryExpr ;
primaryExpr = functionCall | IDENT | NUMBER | "(" expr ")" ;

functionCall = IDENT "(" argList? ")" inClause? ;
argList = expr ("," expr)* ;
inClause = "in" IDENT ;

boolExpr = expr comparator expr ;
comparator = ">" | ">=" | "<" | "<=" | "==" ;

(* ============================================================ *)
(* Benches *)
(* ============================================================ *)

benchDef = "bench" IDENT ":" NL INDENT benchBody DEDENT ;
benchBody = measurementsBlock ;
measurementsBlock = "measurements:" NL INDENT (IDENT NL)+ DEDENT ;

(* ============================================================ *)
(* Circuits *)
(* ============================================================ *)

circuitDef = "circuit" IDENT implementsClause? NL INDENT circuitBody DEDENT ;
implementsClause = "implements" IDENT ;

circuitBody = levelDecl (circuitMember NL)* ;
levelDecl = "level" ("EL" | "ML" | "HL") NL ;
circuitMember = supplyDecl | groundDecl | portDecl 
              | portsBlock | viewsBlock | fillBlock | constraintsBlock | harnessBlock ;

(* ============================================================ *)
(* Constraints *)
(* ============================================================ *)

constraintsBlock = "constraints:" NL INDENT (constraint NL)+ DEDENT ;
constraint = IDENT "=" constraintExpr ;
constraintExpr = measurementRef comparator value ;
measurementRef = IDENT viewSelector? "::" IDENT paramOverrides? ;
viewSelector = "[" IDENT "]" ;
paramOverrides = "(" paramAssign ("," paramAssign)* ")" ;
paramAssign = IDENT "=" value ;

(* ============================================================ *)
(* Harness *)
(* ============================================================ *)

harnessBlock = "harness:" NL INDENT (harnessEntry NL)+ DEDENT ;
harnessEntry = supplyEntry | biasEntry | sourceEntry | loadEntry | pvtEntry ;

supplyEntry = "supply" IDENT "=" value ;
biasEntry = "bias" IDENT "=" value ;
sourceEntry = "source" IDENT impedanceSpec ;
loadEntry = "load" IDENT loadSpec ;
impedanceSpec = "Z" "=" value ;
loadSpec = ("C" "=" value)? ("R" "=" value)? ("L" "=" value)? ;
pvtEntry = "pvt" pvtList ;
pvtList = pvtPoint ("," pvtPoint)* ;
pvtPoint = IDENT "@" TEMPERATURE ;

(* ============================================================ *)
(* Fill Block (unchanged from ACIR 2.x) *)
(* ============================================================ *)

fillBlock = "fill:" NL INDENT (fillEntry NL)+ DEDENT ;
(* ... fill syntax unchanged ... *)

(* ============================================================ *)
(* Terminals *)
(* ============================================================ *)

IDENT = [a-zA-Z_][a-zA-Z0-9_]* ;
NUMBER = [0-9]+ ("." [0-9]+)? exponent? ;
exponent = [eE] [+-]? [0-9]+ ;
value = NUMBER UNIT? ;
UNIT = "V" | "A" | "W" | "Hz" | "Ohm" | "F" | "H" | "dB" | "deg" | "%"
     | "mV" | "uV" | "nV" | "mA" | "uA" | "nA" | "pA"
     | "kHz" | "MHz" | "GHz" | "kOhm" | "MOhm"
     | "pF" | "nF" | "uF" | "fF"
     | "ns" | "us" | "ms" | "ps"
     | "mW" | "uW" | "nW" ;
VERSION = [0-9]+ "." [0-9]+ ;
PATH = [a-zA-Z0-9_/.-]+ ;
TEMPERATURE = "-"? [0-9]+ "C" ;

NL = "\n" ;
INDENT = (* indentation increase *) ;
DEDENT = (* indentation decrease *) ;
```

---

## 9. Migration from ACIR 2.x

### 9.1 Breaking Changes

| ACIR 2.x | ACIR 3.0 | Migration |
|----------|----------|-----------|
| `port IN : Diff` | `input IN : Diff` | Change keyword |
| `builtin SEOpAmpACBench` | Removed | Use standard benches (`include lib/std`) |
| Topology-specific bench templates | Removed | Use class-defined `ports:` + trait requirements to select applicable measurements |

### 9.2 Migration Steps

1. Replace `port` keyword:
   ```
   # Before
   port IN : Diff
   port OUT : analog
   
   # After
   input IN : Diff
   output OUT : analog
   ```

2. Choose a standard class (or define one) with canonical `ports:`:
   - For example, `SingleEndedOpAmp` defines `in`, `out`, and `supply` once using `Port(a, b)`.
   - Your circuits then simply `implements SingleEndedOpAmp` and inherit the port bindings; no per-circuit bench glue is required.

3. Update constraints to use standard benches:
   ```
   # Before
   constraints:
     c_gbw = SEOpAmpACBench::GainBandwidth >= 100MHz
   
   # After
   constraints:
     c_gbw = ACBench::GainBandwidth >= 100MHz
   ```

4. Remove builtin bench references:
   - Delete any `bench ... builtin ...` declarations
   - Use `include lib/std` to access standard benches

### 9.3 Automated Migration Tool

A migration tool will be provided:

```bash
acir-migrate --from 2.x --to 3.0 circuit.acir
```

The tool will:
- Replace `port` with appropriate direction keyword
- Suggest or insert a standard class conformance (`implements <class>`) and, when pin names are non-canonical, generate a small adapter circuit skeleton
- Update constraint bench references
- Report any manual changes required

---

## 10. Implementation Plan

### 10.1 Phase 1: Grammar and Parser

1. Update lexer with direction keywords (`input`, `output`, `inout`)
2. Remove `port` keyword
3. Add `class` parsing with `traits:`, `ports:`, and `views:`
4. Add `measurement` definition blocks with `stimulus_modes` and `in <mode>` clause
5. Add `precondition:` clause
6. Remove `builtin` keyword
7. Add view selection in constraints: `Bench[view]::Measurement(...)`

### 10.2 Phase 2: Semantic Analysis 

1. Implement class inheritance and trait inheritance
2. Implement port binding validation for `ports:` and `views:` (including trait/binding consistency)
3. Implement measurement applicability checking against selected view (`requires:` ports + `requires_traits:`)
4. Implement procedure primitive type checking
5. Emit diagnostics for inapplicable measurements in constraints

### 10.3 Phase 3: Emission Pipeline 

1. Define `TestbenchModel`, `PortBinding`, `ResolvedMeasurement` structures
2. Implement `ITestbenchEmitter` interface
3. Implement `NgspiceEmitter` with programmatic generation
4. Implement `SpectreEmitter`
5. Implement multi-simulation orchestration for stimulus modes
6. Implement measurement calculators for multi-sim measurements

### 10.4 Phase 4: Standard Library 

1. Create `lib/interfaces/standard.acir` (traits + classes)
2. Create `lib/measurements/standard.acir`
3. Create `lib/benches/standard.acir`
4. Create `lib/std.acir` meta-include
5. Implement C# calculators: `CMRRCalculator`, `PSRRCalculator`

### 10.5 Phase 5: Migration and Testing

1. Implement `acir-migrate` tool
2. Unit tests for binding resolution and type checking
3. Integration tests for each circuit topology
4. Golden file tests for emitted testbenches
5. Migrate existing example circuits

---

## 11. Future Work

### 11.1 Range and Sweep Constraints

Support for constraints that must hold across a parameter range:

```acir
constraints:
  c_cmrr_band = ACBench::CMRR for f in [1kHz:1MHz] >= 50dB
```

Requires:
- Range syntax in grammar
- Sampling strategy specification
- Multi-point simulation orchestration

### 11.2 Current-Mode Measurements

Extend harness to specify stimulus mode:

```acir
harness:
  source IN mode=current Z=1MOhm    # Current stimulus for TIA
```

Requires:
- Harness syntax extension
- Emitter changes for current source generation

### 11.3 Noise Measurements

```acir
measurement InputReferredNoise:
  requires: in : Port, out : Port
  analysis: noise
  procedure:
    Sn = input_noise_density(in, out)
    result = integrate_sqrt(Sn, f_min, f_max)
  params:
    f_min : Frequency = 1Hz
    f_max : Frequency = 1MHz
  unit: V
```

Requires:
- Noise analysis primitives
- Spectral density integration primitive

### 11.4 Transient Measurements

```acir
measurement SlewRate:
  requires: in : Port, out : Port
  analysis: tran
  procedure:
    result = max(derivative(voltage(out)))
  unit: V/s
```

Requires:
- Transient analysis primitives
- Time-domain evaluation primitives

### 11.5 Statistical Constraints

```acir
constraints:
  c_gbw_yield : yield(ACBench::GainBandwidth >= 80MHz) >= 99%
```

Requires:
- Monte Carlo simulation support
- Statistical aggregation primitives

### 11.6 Full Expression Language

Extend procedure DSL to a complete typed expression language with:
- User-defined functions
- Conditional expressions
- Loop constructs for parameter sweeps

---

## 12. References

1. ACIR Specification, Chapters 1-3
2. Razavi, B. "Design of Analog CMOS Integrated Circuits"
3. Gonzalez, G. "Microwave Transistor Amplifiers" (two-port network theory)
4. ngspice User Manual
5. Cadence Spectre User Guide