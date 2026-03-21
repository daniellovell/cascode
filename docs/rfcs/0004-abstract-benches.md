# RFC-0004: Abstract Bench System

Status: Draft
Authors: Snappie (proposed), Daniel Lovell (review)
Created: 2026-02-04
Last Updated: 2026-02-07
Target Version: Cascode 3.x
Related Issue: #94

---

## Abstract

This RFC proposes an `abstract bench` mechanism that allows bench families to share analysis and measurement definitions across topology variants. The motivating case is `TransferBenches.cas`, where five benches share identical analysis configurations and measurement blocks — calling the same file-level helper functions — but each requires its own terminal declarations and fill block for its stimulus/load topology. Abstract benches formalize this pattern by allowing a base bench to declare abstract terminals, an analysis block, and a measurements block, while concrete extending benches supply the terminal types and fill blocks.

---

## 1. Problem Statement

`lib/std/bench/TransferBenches.cas` defines five benches in 411 lines. Three of them — `DiffToSETransfer`, `DiffToDiffTransfer`, and `SEToSETransfer` — share the same structure:

- Identical `analysis` blocks (an `ACAnalysis` with constraint-driven start/stop)
- Identical `measurements` blocks (seven declarations: `Gain`, `PassbandGain`, `GainBandwidth`, `PhaseMargin`, `LowpassBandwidth`, `HighpassBandwidth`, `BandpassBandwidth`)
- Different `stim`/`resp` terminal type declarations
- Different `fill` blocks that construct topology-appropriate stimulus and load circuits

The analysis and measurement blocks are copied verbatim across all three benches. The only variation within measurements is that each bench passes its own `IN` and `OUT` terminals either to the shared file-level helpers (`calc_passband_gain`, `calc_gain_bandwidth`, etc.) or directly to `transfer(ac, IN, OUT)` for spot and band gain. This is exactly the kind of reference that abstract terminals would resolve: the measurement expressions reference terminal names, and the extending bench provides the concrete typed terminals.

Similarly, the two CM rejection benches (`DiffCMRejection` and `DiffToSECMRejection`) share identical analysis and measurement blocks but differ in terminal types and fill topology.

The file already factors measurement logic into file-level functions (lines 5-79), which is the right pattern for sharing computation. But the analysis configuration and measurement declarations themselves — the "which measurements does this bench produce, and how are they wired to terminals?" structure — cannot currently be shared. Each new topology variant requires copying the full bench definition and changing only the terminals and fill block, creating maintenance burden and drift risk.

The existing TODO at line 81 of `TransferBenches.cas` acknowledges this:

```
// TODO: We should support bench templates/abstract benches in the future
```

---

## 2. Goals and Non-Goals

### Goals

This RFC aims to enable bench families to share analysis and measurement definitions across topology variants, complementing the existing function-sharing pattern. The mechanism should use the `extends` keyword for concrete bench inheritance from abstract benches, allow abstract terminal declarations that measurement expressions can reference, and require no changes to the bench binding semantics (`bind ... as ...` in interfaces and circuits).

### Non-Goals

This RFC does not address fill-block topology abstraction (abstracting over the stimulus/load construction itself is future work), multiple inheritance for benches, changes to the `bind` mechanism or constraint system, runtime polymorphism or dynamic bench selection, or automatic generation of bench variants from topology descriptors.

---

## 3. Proposal

### 3.1 Abstract Bench Declaration

An `abstract bench` declares a bench template that cannot be used directly in `bind` statements. It may declare abstract terminals (names without types), an analysis block, a measurements block, and bench-local functions.

```cascode
abstract bench AbstractTransfer {
  abstract stim IN
  abstract resp OUT

  analysis {
    ACAnalysis ac = new ACAnalysis(
      space=Log,
      samples=100,
      start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
      stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }))
  }

  measurements {
    measurement Gain(Frequency f) : dB {
      return db20(transfer(ac, IN, OUT).Mag()).ValueAt(f)
    }

    measurement Gain(Frequency from, Frequency to) : dB {
      return db20(transfer(ac, IN, OUT).Mag()).From(from).To(to)
    }

    measurement PassbandGain : dB {
      return calc_passband_gain(ac, IN, OUT, constraints.LowpassBandwidth, constraints.GainBandwidth)
    }

    measurement GainBandwidth : Hz {
      return calc_gain_bandwidth(ac, IN, OUT)
    }

    measurement PhaseMargin : deg {
      return calc_phase_margin(ac, IN, OUT)
    }

    measurement LowpassBandwidth : Hz {
      return calc_lowpass_bandwidth(ac, IN, OUT, constraints.LowpassBandwidth, constraints.GainBandwidth)
    }

    measurement HighpassBandwidth : Hz {
      return calc_highpass_bandwidth(ac, IN, OUT, constraints.LowpassBandwidth, constraints.GainBandwidth)
    }

    measurement BandpassBandwidth : Hz {
      return abs(LowpassBandwidth() - HighpassBandwidth())
    }
  }
}
```

Abstract terminals are declared with the `abstract` modifier and a role (`stim`/`resp`) but no type. The terminal names (`IN`, `OUT`) are in scope within the analysis and measurements blocks, allowing measurement expressions to reference them. The extending bench provides concrete typed terminals that bind these names to actual terminal types.

### 3.2 Concrete Bench Extension

A concrete bench uses `extends` to inherit from an abstract bench. It must provide typed terminal declarations matching each abstract terminal, and a fill block:

```cascode
bench DiffToSETransfer extends AbstractTransfer {
  stim IN : Diff
  resp OUT : analog

  fill {
    net vcm : analog
    net gnd : ground

    GND g = new GND() {
      .GND--gnd
    }

    VDC commonModeVDC = new VDC(V=env.InputCommonModeRange) {
      .P--vcm
      .N--gnd
    }

    VAC acP = new VAC(A=0.5V, phase=0deg) {
      .N--vcm
    }
    VAC acN = new VAC(A=0.5V, phase=180deg) {
      .N--vcm
    }

    Impedor sourceP = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }
    Impedor sourceN = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }

    acP.P--sourceP.P
    sourceP.N--IN.P

    acN.P--sourceN.P
    sourceN.N--IN.N

    Impedor loadZ = new Impedor(Z=env.LoadImpedance) {
      .P--OUT
      .N--gnd
    }
  }
}
```

The extending bench inherits the analysis and measurements blocks from `AbstractTransfer`. The terminal declarations `stim IN : Diff` and `resp OUT : analog` provide concrete types for the abstract terminals `IN` and `OUT`. The fill block is provided entirely by the extending bench.

### 3.3 New Keywords

Three keywords are introduced:

`extends` indicates bench inheritance (`bench X extends Y`). This is distinct from the existing `extend` keyword used for bench binding extensions in circuits (`extend binding_name { ... }`). The `extends` keyword was selected because it is universally understood, scales to multi-level hierarchies, and has precedent in hardware description contexts (SystemVerilog uses `extends` for class inheritance).

`abstract` marks a bench definition or terminal declaration as abstract. An `abstract bench` cannot be bound directly. An `abstract stim` or `abstract resp` declares a terminal placeholder without a type.

`override` marks an analysis block or measurement definition as intentionally replacing an inherited one. Without `override`, a name collision with an inherited member is an error.

### 3.4 Mixed Terminals

An abstract bench may declare both abstract terminals (untyped placeholders) and concrete terminals (with types). This is needed when a bench family shares a common terminal across all variants but varies others. For example, the PSRR bench family shares `stim PWR : supply` across all variants while varying the input and output topologies:

```cascode
abstract bench AbstractPSRR {
  abstract stim IN
  stim PWR : supply
  abstract resp OUT

  analysis { ... }
  measurements { ... }
}
```

The extending bench must redeclare all terminals — both abstract and concrete. Abstract terminals gain their types; concrete terminals are restated with matching name, role, and type:

```cascode
bench SupplyToSERejection extends AbstractPSRR {
  stim IN : Diff
  stim PWR : supply
  resp OUT : analog

  fill { ... }
}
```

This makes each concrete bench self-documenting: its full terminal list is visible without consulting the base bench.

### 3.5 Parameter Inheritance

Abstract benches may declare parameters using the standard `benchParamList` syntax. Extending benches inherit these parameters. An extending bench may additionally declare its own parameters (appended to the inherited set) or override default values of inherited parameters by redeclaring a parameter with the same name and type but a different default:

```cascode
abstract bench AbstractTran(Frequency stim_freq = 1kHz) {
  abstract stim IN
  abstract resp OUT

  analysis {
    TranAnalysis tran = new TranAnalysis(
      step=period(stim_freq) / 200,
      start=9 * period(stim_freq),
      stop=10 * period(stim_freq))
  }

  measurements {
    measurement OutputSwing : V {
      VoltageWaveform vout = voltage(tran, OUT)
      return vout.Max() - vout.Min()
    }
  }
}

bench DiffToSETran extends AbstractTran {
  stim IN : Diff
  resp OUT : analog

  fill { ... }
}
```

The concrete bench `DiffToSETran` inherits the `stim_freq` parameter with its `1kHz` default. Users bind it the same way as before: `bind DiffToSETran as tran_bench { ... }`.

### 3.6 Override Mechanism

An extending bench may override inherited analysis or individual measurements using the `override` keyword.

To replace the inherited analysis block entirely:

```cascode
bench CustomTransfer extends AbstractTransfer {
  stim IN : Diff
  resp OUT : analog

  override analysis {
    ACAnalysis ac = new ACAnalysis(
      space=Log,
      samples=200,
      start=100Hz,
      stop=1GHz)
  }

  fill { ... }
}
```

To replace an individual inherited measurement while keeping the rest:

```cascode
bench CustomTransfer extends AbstractTransfer {
  stim IN : Diff
  resp OUT : analog

  override measurement PassbandGain : dB {
    return custom_gain_calculation(ac, IN, OUT)
  }

  fill { ... }
}
```

Without the `override` keyword, a measurement name collision with an inherited measurement is an error. This makes accidental shadowing impossible while still allowing intentional replacement.

### 3.7 Inheritance Rules

The following rules govern what is inherited and what must be provided.

Inherited from the abstract bench by default:
- The `analysis` block (unless the extending bench declares `override analysis`)
- All `measurement` definitions (unless individually replaced by `override measurement`)
- Bench-local `function` definitions (extending bench may shadow by name)
- Parameters from `benchParamList`

Must be provided by the extending bench:
- All terminal declarations matching those in the abstract bench — abstract terminals gain types, concrete terminals are restated with matching type
- A `fill` block

May be provided by the extending bench:
- Additional measurements (appended to the inherited set)
- Additional bench-local functions
- Additional parameters (appended to the inherited set)
- `override analysis` to replace the inherited analysis block
- `override measurement X` to replace individual inherited measurements
- Redeclared inherited parameters with different default values

An abstract bench cannot appear in `bind` statements in interfaces or circuits. Only concrete (non-abstract) benches — whether defined directly or extending an abstract bench — are valid bind targets.

### 3.8 Abstract Terminal Semantics

Abstract terminals serve as named placeholders that the abstract bench body can reference before the concrete terminal type is known. They carry a name and a role (`stim` or `resp`) but no type. Abstract terminal names are in scope everywhere within the abstract bench body: analysis blocks, measurement blocks, and bench-local functions.

This full-scope visibility is necessary for benches like `AbstractNoise`, where the analysis block itself references a terminal:

```cascode
abstract bench AbstractNoise {
  abstract stim IN
  abstract resp OUT

  analysis {
    ACAnalysis ac = new ACAnalysis(...)
    NoiseAnalysis noise_ac = new NoiseAnalysis(..., output=OUT)
  }

  measurements { ... }
}
```

When the extending bench declares `stim IN : Diff`, the abstract terminal `IN` resolves to a `Diff`-typed terminal. Measurement primitives like `transfer(ac, IN, OUT)` then compute the appropriate voltage based on the resolved terminal type (e.g., `V(IN.P) - V(IN.N)` for a `Diff` input), following the standard terminal voltage computation rules from RFC-0000 Section 11.7.

This means the same measurement expression `calc_passband_gain(ac, IN, OUT, ...)` correctly handles differential-to-single-ended, differential-to-differential, and single-ended-to-single-ended topologies — the terminal type determines the voltage computation, not the measurement expression itself.

### 3.9 Chained Inheritance

An abstract bench may extend another abstract bench, forming an inheritance chain. The chain must terminate at a concrete bench that provides terminals and a fill block. Circular inheritance is an error.

```cascode
abstract bench AbstractAC {
  abstract stim IN
  abstract resp OUT

  analysis {
    ACAnalysis ac = new ACAnalysis(...)
  }
}

abstract bench AbstractTransfer extends AbstractAC {
  measurements {
    measurement PassbandGain : dB {
      return calc_passband_gain(ac, IN, OUT, ...)
    }
    // ... remaining shared measurements
  }
}

bench DiffToSETransfer extends AbstractTransfer {
  stim IN : Diff
  resp OUT : analog
  fill { ... }
}
```

Each level in the chain can add terminals, parameters, analysis, measurements, and functions. The concrete bench at the end must satisfy all abstract terminals accumulated from the full chain.

---

## 4. Examples

### 4.1 Transfer Bench Family (Motivating Example)

The following shows how `TransferBenches.cas` would be restructured using abstract benches. File-level helper functions remain unchanged.

```cascode
VERSION 4.0

library lib.std.bench

function calc_passband_freq(ACAnalysis ac, Frequency hp, Frequency lp) : Frequency {
  Frequency f = sqrt(hp * lp)
  if f < ac.start { return ac.start }
  if f > ac.stop { return ac.stop }
  return f
}

function calc_passband_gain(ACAnalysis ac, stim IN, resp OUT, Frequency lpConstraint, Frequency gbwConstraint) : VoltageRatio {
  TransferFunction H = transfer(ac, IN, OUT)
  GainSpectrum G = db20(H.Mag())
  Frequency hp = infer_hp_corner(1Hz)
  Frequency lp = infer_lp_corner(ac, lpConstraint, gbwConstraint)
  Frequency fpb = calc_passband_freq(ac, hp, lp)
  return G.ValueAt(fpb)
}

// ... remaining file-level helpers (calc_gain_bandwidth, calc_phase_margin, etc.)

abstract bench AbstractTransfer {
  abstract stim IN
  abstract resp OUT

  analysis {
    ACAnalysis ac = new ACAnalysis(
      space=Log,
      samples=100,
      start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
      stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }))
  }

  measurements {
    measurement Gain(Frequency f) : dB {
      return db20(transfer(ac, IN, OUT).Mag()).ValueAt(f)
    }

    measurement Gain(Frequency from, Frequency to) : dB {
      return db20(transfer(ac, IN, OUT).Mag()).From(from).To(to)
    }

    measurement PassbandGain : dB {
      return calc_passband_gain(ac, IN, OUT, constraints.LowpassBandwidth, constraints.GainBandwidth)
    }

    measurement GainBandwidth : Hz {
      return calc_gain_bandwidth(ac, IN, OUT)
    }

    measurement PhaseMargin : deg {
      return calc_phase_margin(ac, IN, OUT)
    }

    measurement LowpassBandwidth : Hz {
      return calc_lowpass_bandwidth(ac, IN, OUT, constraints.LowpassBandwidth, constraints.GainBandwidth)
    }

    measurement HighpassBandwidth : Hz {
      return calc_highpass_bandwidth(ac, IN, OUT, constraints.LowpassBandwidth, constraints.GainBandwidth)
    }

    measurement BandpassBandwidth : Hz {
      return abs(LowpassBandwidth() - HighpassBandwidth())
    }
  }
}

bench DiffToSETransfer extends AbstractTransfer {
  stim IN : Diff
  resp OUT : analog

  fill {
    net vcm : analog
    net gnd : ground

    GND g = new GND() { .GND--gnd }

    VDC commonModeVDC = new VDC(V=env.InputCommonModeRange) {
      .P--vcm
      .N--gnd
    }

    VAC acP = new VAC(A=0.5V, phase=0deg) { .N--vcm }
    VAC acN = new VAC(A=0.5V, phase=180deg) { .N--vcm }

    Impedor sourceP = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }
    Impedor sourceN = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }

    acP.P--sourceP.P
    sourceP.N--IN.P

    acN.P--sourceN.P
    sourceN.N--IN.N

    Impedor loadZ = new Impedor(Z=env.LoadImpedance) {
      .P--OUT
      .N--gnd
    }
  }
}

bench DiffToDiffTransfer extends AbstractTransfer {
  stim IN : Diff
  resp OUT : Diff

  fill {
    net vcm : analog
    net gnd : ground

    GND g = new GND() { .GND--gnd }

    VDC commonModeVDC = new VDC(V=env.InputCommonModeRange) {
      .P--vcm
      .N--gnd
    }

    VAC acP = new VAC(A=0.5V, phase=0deg) { .N--vcm }
    VAC acN = new VAC(A=0.5V, phase=180deg) { .N--vcm }

    Impedor sourceP = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }
    Impedor sourceN = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }

    acP.P--sourceP.P
    sourceP.N--IN.P

    acN.P--sourceN.P
    sourceN.N--IN.N

    Impedor loadP = new Impedor(Z=env.LoadImpedance.DiffToShunt()) {
      .P--OUT.P
      .N--gnd
    }
    Impedor loadN = new Impedor(Z=env.LoadImpedance.DiffToShunt()) {
      .P--OUT.N
      .N--gnd
    }
  }
}

bench SEToSETransfer extends AbstractTransfer {
  stim IN : analog
  resp OUT : analog

  fill {
    net vcm : analog
    net gnd : ground

    GND g = new GND() { .GND--gnd }

    VDC biasDC = new VDC(V=env.InputCommonModeRange) {
      .P--vcm
      .N--gnd
    }

    VAC ac = new VAC(A=1V, phase=0deg) { .N--vcm }

    Impedor sourceZ = new Impedor(Z=env.SourceImpedance) { }

    ac.P--sourceZ.P
    sourceZ.N--IN

    Impedor loadZ = new Impedor(Z=env.LoadImpedance) {
      .P--OUT
      .N--gnd
    }
  }
}
```

### 4.2 CM Rejection Bench Family

The same pattern applies to the CM rejection benches:

```cascode
abstract bench AbstractCMRejection {
  abstract stim CM
  abstract resp OUT

  analysis {
    ACAnalysis ac = new ACAnalysis(
      space=Log,
      samples=100,
      start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
      stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }))
  }

  measurements {
    measurement CommonModeGain : dB {
      return calc_passband_gain(ac, CM, OUT, constraints.LowpassBandwidth, constraints.GainBandwidth)
    }

    measurement CMRR(VoltageRatio dmGain) : dB {
      VoltageRatio cmGain = CommonModeGain()
      return (dmGain - cmGain)
    }
  }
}

bench DiffCMRejection extends AbstractCMRejection {
  stim CM : analog
  resp OUT : Diff

  fill {
    // ... (unchanged from current DiffCMRejection)
  }
}

bench DiffToSECMRejection extends AbstractCMRejection {
  stim CM : analog
  resp OUT : analog

  fill {
    // ... (unchanged from current DiffToSECMRejection)
  }
}
```

### 4.3 Noise Bench Family (Abstract Terminal in Analysis Block)

The noise bench family demonstrates abstract terminals referenced within the analysis block itself, not just in measurements. The `NoiseAnalysis` declaration requires an `output` parameter that names a terminal:

```cascode
abstract bench AbstractNoise {
  abstract stim IN
  abstract resp OUT

  analysis {
    ACAnalysis ac = new ACAnalysis(
      space=Log, samples=100,
      start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
      stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }))

    NoiseAnalysis noise_ac = new NoiseAnalysis(
      space=Log, samples=100,
      start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
      stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }),
      output=OUT)
  }

  measurements {
    measurement InputReferredNoise : V/rtHz {
      NoiseSpectrum n_in = input_referred_noise(noise_ac, ac, IN, OUT)
      Frequency f_spot = (if constraints.SpotNoiseFrequency { constraints.SpotNoiseFrequency } else { 1kHz })
      return n_in.ValueAt(f_spot)
    }

    measurement IntegratedInputNoise(Frequency from, Frequency to) : Vrms {
      NoiseSpectrum n_in = input_referred_noise(noise_ac, ac, IN, OUT)
      return n_in.Integrate(from, to)
    }

    measurement OutputNoise : V/rtHz {
      NoiseSpectrum n_out = noise(noise_ac, OUT)
      Frequency f_spot = (if constraints.SpotNoiseFrequency { constraints.SpotNoiseFrequency } else { 1kHz })
      return n_out.ValueAt(f_spot)
    }
  }
}

bench DiffToSENoise extends AbstractNoise {
  stim IN : Diff
  resp OUT : analog
  fill { ... }
}

bench DiffToDiffNoise extends AbstractNoise {
  stim IN : Diff
  resp OUT : Diff
  fill { ... }
}

bench SEToSENoise extends AbstractNoise {
  stim IN : analog
  resp OUT : analog
  fill { ... }
}
```

### 4.4 Tran Bench Family (Parameter Inheritance)

The transient bench family demonstrates parameter inheritance. The abstract bench declares `stim_freq` with a default; each concrete bench inherits it:

```cascode
abstract bench AbstractTran(Frequency stim_freq = 1kHz) {
  abstract stim IN
  abstract resp OUT

  analysis {
    TranAnalysis tran = new TranAnalysis(
      step=period(stim_freq) / 200,
      start=9 * period(stim_freq),
      stop=10 * period(stim_freq))
  }

  measurements {
    measurement OutputSwing : V {
      VoltageWaveform vout = voltage(tran, OUT)
      return vout.Max() - vout.Min()
    }
  }
}

bench DiffToSETran extends AbstractTran {
  stim IN : Diff
  resp OUT : analog
  fill { ... }
}

bench DiffToDiffTran extends AbstractTran {
  stim IN : Diff
  resp OUT : Diff
  fill { ... }
}

bench SEToSETran extends AbstractTran {
  stim IN : analog
  resp OUT : analog
  fill { ... }
}
```

### 4.5 PSRR Bench Family (Mixed Terminals)

The PSRR bench family demonstrates mixed concrete and abstract terminals. All variants share the supply terminal `PWR : supply` while varying input and output topologies:

```cascode
abstract bench AbstractPSRR {
  abstract stim IN
  stim PWR : supply
  abstract resp OUT

  analysis {
    ACAnalysis ac = new ACAnalysis(
      space=Log, samples=100,
      start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
      stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }))
  }

  measurements {
    measurement SupplyGain : dB {
      return calc_passband_gain(ac, PWR, OUT, constraints.LowpassBandwidth, constraints.GainBandwidth)
    }

    measurement PSRR : dB {
      return -SupplyGain()
    }

    measurement InputReferredPSRR(VoltageRatio dmGain) : dB {
      return (dmGain - SupplyGain())
    }
  }
}

bench SupplyToSERejection extends AbstractPSRR {
  stim IN : Diff
  stim PWR : supply
  resp OUT : analog
  fill { ... }
}

bench SupplyToSERejectionSEInput extends AbstractPSRR {
  stim IN : analog
  stim PWR : supply
  resp OUT : analog
  fill { ... }
}

bench SupplyToDiffRejection extends AbstractPSRR {
  stim IN : Diff
  stim PWR : supply
  resp OUT : Diff
  fill { ... }
}
```

### 4.6 Line Count Comparison

Across the four bench files, abstract benches eliminate significant duplication:

| File | Current | With abstract benches | Reduction |
|------|---------|----------------------|-----------|
| `TransferBenches.cas` | 411 | ~290 | ~120 |
| `NoiseBenches.cas` | 235 | ~150 | ~85 |
| `TranBenches.cas` | 192 | ~110 | ~80 |
| `PowerBenches.cas` | 242 | ~180 | ~60 |
| Total | 1080 | ~730 | ~350 |

The larger benefit is that analysis and measurement definitions exist in exactly one place per bench family, eliminating drift risk when adding or modifying measurements.

### 4.7 Bench Binding (Unchanged)

Abstract benches do not affect binding syntax. The extending benches are bound in interfaces exactly as before:

```cascode
interface SingleEndedOpAmp {
  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog

  benches {
    bind DiffToSETransfer as transfer_bench {
      bench.IN--dut.IN
      bench.OUT--dut.OUT
    }
  }
}
```

The `bind` target is the concrete bench `DiffToSETransfer`, not the abstract `AbstractTransfer`. The interface and circuit authors interact only with concrete bench names.

---

## 5. Migration and Compatibility

This is an additive feature. Existing benches continue to work unchanged with no modifications required.

To migrate an existing bench family to use abstract benches, the pattern is:

1. Identify benches that share identical analysis and measurements blocks.
2. Extract the shared analysis and measurements into an `abstract bench` with `abstract` terminal declarations.
3. Replace each original bench with a concrete bench that `extends` the abstract bench, keeping only its terminal declarations and fill block.
4. Verify that `bind` statements in interfaces and circuits continue to reference the concrete bench names (no changes needed if bench names are preserved).

---

## 6. Resolved Design Decisions

The following questions were raised during the draft phase and resolved through design review.

Chained inheritance is supported. An abstract bench may extend another abstract bench, forming multi-level hierarchies. Circular inheritance is detected and reported as an error. No depth limit is imposed.

Parameter inheritance follows class-constructor semantics. An abstract bench declares parameters that extending benches inherit. Extending benches may add new parameters and may override default values of inherited parameters by redeclaring a parameter with the same name and type.

Measurement override is supported via the `override` keyword. An extending bench may use `override measurement X : unit { ... }` to replace an individual inherited measurement. Without the `override` keyword, a name collision is an error. Analysis override uses `override analysis { ... }` to replace the inherited analysis block entirely.

Inherited bench-local functions are in scope within the extending bench. The extending bench may shadow an inherited function by declaring a function with the same name; the extending bench's version takes precedence.

An extending bench may itself be abstract, requiring further extension. The concrete bench at the end of the chain must satisfy all accumulated abstract terminals.

Mixed terminals are supported. An abstract bench may contain both abstract (untyped) and concrete (typed) terminals. The extending bench must redeclare all terminals: abstract terminals gain types, concrete terminals are restated with matching type.

---

## 7. Future Extensions

Fill-block topology templates could further reduce duplication by abstracting over the stimulus/load construction patterns shared across differential and single-ended topologies. This is significantly more complex than sharing analysis and measurements, and is deferred to future work.

Standard library abstract bench families (`AbstractTransfer`, `AbstractCMRejection`, `AbstractNoise`, `AbstractTran`, `AbstractPSRR`) will be provided as part of the initial implementation, replacing the current duplicated bench definitions in `lib/std/bench/`.

---

## 8. Implementation Plan

### 8.1 Architecture: Flattening Resolver

The implementation uses a flattening approach. A new `BenchInheritanceResolver` pass runs after the linker merges documents and after bundle desugaring, but before bench semantic checking and bench binding checking. This pass replaces abstract-plus-extending bench pairs with fully-resolved concrete `BenchDefinition` AST nodes, so no downstream pass requires modification.

The processing pipeline becomes:

1. Parse (grammar → AST)
2. Link (merge documents, resolve cross-file dependencies)
3. Desugar bundles
4. **Resolve bench inheritance** (new pass — flattens abstract benches into concrete benches)
5. Extend bench bindings
6. Semantic check
7. Binding check
8. Emit / execute

Because the resolver produces fully-typed concrete benches before validation, the `BenchSemanticChecker` type-checks only resolved benches where all terminals have types. Abstract benches are never type-checked directly.

### 8.2 Grammar Changes

Three new lexer tokens: `ABSTRACT_KW` (`abstract`), `EXTENDS_KW` (`extends`), `OVERRIDE_KW` (`override`). All three are added to the `idPart` rule following the existing pattern for contextual identifiers.

Modified rules:

```
benchDef
    : ABSTRACT_KW? BENCH_KW name=IDENT benchParamList? (EXTENDS_KW base=IDENT)? LBRACE benchBody RBRACE
    ;

terminalDecl
    : ABSTRACT_KW? terminalRole IDENT (COLON terminalType)?
    ;

analysisBlock
    : OVERRIDE_KW? ANALYSIS_KW LBRACE analysisDecl* RBRACE
    ;

measurementDecl
    : OVERRIDE_KW? MEASUREMENT_KW name=IDENT (LPAREN typedParamList? RPAREN)? COLON unitType LBRACE measurementBody RBRACE
    ;
```

### 8.3 AST Changes

`BenchDefinition` gains two properties: `bool IsAbstract` and `string? BaseBench`. `BenchTerminal.Type` becomes nullable (`string?`) to represent abstract terminals, and gains `bool IsAbstract`. `BenchDefinition` gains `bool OverrideAnalysis`. `MeasurementDefinition` gains `bool IsOverride`.

Making `BenchTerminal.Type` nullable means the C# compiler (with `TreatWarningsAsErrors` and `Nullable=enable`) will flag every downstream access. For files that only process post-resolver benches (BenchRuntime, BenchSemanticChecker, BenchBindingChecker), add the null-forgiving operator since those paths never see abstract terminals.

### 8.4 Resolver Logic

`BenchInheritanceResolver.Resolve(CascodeDocument, List<Diagnostic>)`:

1. Index all benches by name.
2. Build an inheritance graph from `BaseBench` references and topologically sort it. Report cycles as errors.
3. Process extending benches in topological order (bases resolved before children). For each:
   - Look up the base bench. Validate it is abstract.
   - Match terminals: the extending bench must redeclare every terminal from the base. Abstract base terminals must gain a type. Concrete base terminals must match in name, role, and type.
   - Flatten the bench by merging inherited and local members:
     - Terminals: from the extending bench.
     - Fill: from the extending bench.
     - Parameters: base parameters first, then extending's additions. Extending may override inherited defaults by redeclaring a parameter with the same name and type.
     - Analysis: from the base, unless the extending bench sets `OverrideAnalysis`.
     - Measurements: base measurements first, then extending's non-override measurements appended. `IsOverride` measurements replace the base measurement with matching name.
     - Functions: base functions plus extending's functions. Extending may shadow by name.
   - If the extending bench is itself abstract, produce a flattened abstract bench for the next level.
4. Remove abstract benches that are not referenced by any remaining abstract bench.
5. Replace extending benches with their flattened versions.

### 8.5 Linker Changes

`CollectRequiredSymbols` must collect `BaseBench` names as required bench symbols so the linker resolves cross-file inheritance (e.g., an abstract bench in one file, a concrete bench in another). Terminal type resolution must guard against null types on abstract terminals.

### 8.6 Diagnostic Codes

| Code | Message |
|------|---------|
| CAS2020 | `extends` references unknown bench `{name}` |
| CAS2021 | `extends` targets non-abstract bench `{name}` |
| CAS2022 | Abstract bench `{name}` cannot appear in bind statements |
| CAS2023 | Abstract terminal `{name}` in non-abstract bench `{bench}` |
| CAS2024 | Concrete bench `{bench}` has terminal `{name}` without a type |
| CAS2025 | Extending bench `{bench}` missing terminal for abstract terminal `{name}` from `{base}` |
| CAS2026 | Terminal `{name}` role mismatch with base `{base}` |
| CAS2027 | Abstract bench `{bench}` must not have a fill block |
| CAS2028 | Extending bench `{bench}` must have a fill block |
| CAS2029 | Measurement `{name}` duplicates inherited measurement (use `override` to replace) |
| CAS2030 | Inheritance cycle detected: `{chain}` |
| CAS2031 | Concrete terminal `{name}` type mismatch: base has `{baseType}`, extending has `{extType}` |
| CAS2032 | `override measurement {name}` targets nonexistent base measurement |
| CAS2033 | `override analysis` used but base bench has no analysis block |

### 8.7 Patch Sequence

The implementation is split into patches, each within the 400-LOC limit:

1. Grammar and lexer changes, ANTLR regeneration.
2. AST, parser, writer, and linker changes. Version bump (3.0 → 3.1). Parsing and round-trip tests.
3. `BenchInheritanceResolver` implementation and pipeline integration. Resolver and error-case tests.
4. Stdlib migration: `TransferBenches.cas`.
5. Stdlib migration: `NoiseBenches.cas`, `TranBenches.cas`, `PowerBenches.cas`.
6. RFC update to reflect the final implemented design.

### 8.8 Files Modified

| File | Change |
|------|--------|
| `tools/language/Cascode.g4` | Grammar rules for `abstract`, `extends`, `override` |
| `tools/language/BenchAst.cs` | `IsAbstract`, `BaseBench`, nullable `Type`, `OverrideAnalysis`, `IsOverride` |
| `tools/language/CascodeAstBuilder.Core.cs` | Populate new AST fields from parse tree |
| `tools/language/CascodeWriter.cs` | Emit new syntax |
| `tools/language/CascodeLinker.cs` | Pipeline integration, `CollectRequiredSymbols` changes |
| `tools/language/BenchInheritanceResolver.cs` | New file: core flattening logic |
| `tools/language/CascodeVersion.cs` | Minor version bump |
| `tools/language/Validation/BenchSemanticChecker.cs` | Null-forgiving operators |
| `tools/language/Validation/BenchBindingChecker.cs` | Defensive abstract-bench check |
| `lib/std/bench/TransferBenches.cas` | Migration to abstract benches |
| `lib/std/bench/NoiseBenches.cas` | Migration to abstract benches |
| `lib/std/bench/TranBenches.cas` | Migration to abstract benches |
| `lib/std/bench/PowerBenches.cas` | Migration to abstract benches |

---

## References

- Issue #94: Abstract bench system proposal
- RFC-0000: Cascode Language Unification and Declarative Bench System (canonical bench system specification)
- `lib/std/bench/TransferBenches.cas`: motivating example with current duplication
- `tools/language/Cascode.g4`: authoritative grammar (bench rules at lines 94-108)
- Note: RFC-0003 (ACIR Syntax Overhaul) was retired in this PR as a stale pre-RFC-0000 artifact
