# RFC-0000: Cascode Language Unification and Declarative Bench System

Status: Draft
Authors: Daniel Lovell
Created: 2026-01-25
Last Updated: 2026-01-28
Target Version: Cascode 1.0

---

## Abstract

This RFC proposes the unification of ACIR and Cascode into a single language called Cascode, along with a declarative bench system for measurement abstraction. The motivation remains the same as earlier proposals: avoid topology-driven bench duplication (single-ended vs fully-differential, presence/absence of supply ports, etc.). However, the solution takes a fundamentally different approach.

Rather than introducing network-port theory with `Port(a,b)` abstractions and trait/class taxonomy systems, this RFC introduces a declarative `bench` construct. Benches define terminals with stimulus/response roles, a `fill {}` block for test circuit construction, an `analysis {}` block for typed analysis instantiation, and a `measurements {}` block with typed measurement expressions. Circuits and interfaces bind benches to their terminals and inherit bench definitions through interface implementation.

The key insight is that benches should provide total freedom and flexibility: users can instantiate test instruments, probes, and even complete circuits within a bench. The measurement logic executes as runtime post-processing after simulation, allowing rich expression evaluation over simulation results.

---

## Big Picture Goals

This section captures the architectural decisions that must be realized throughout the specification. Every grammar change, semantic rule, and toolchain command should trace back to one of these goals.

### Unified Language

ACIR and Cascode merge into a single language called Cascode, using the `.cas` file extension exclusively. The separate `.cir` format is eliminated. When the two languages conflict, ACIR features take precedence as the more mature specification. The elaboration level (HL, ML, EL) becomes a property of the content, not a separate format.

### Explicit Elaboration Levels

Every circuit declaration must include an explicit `level` block declaring `HL`, `ML`, or `EL`. There is no level inference. A single file may contain circuits at different levels, but the file's suffix reflects the highest (least elaborated) level present.

### Explicit Compilation Pipeline

The toolchain enforces a three-stage pipeline with clear input/output contracts:

```
mycircuit.cas                        # source (may have includes)
    ↓ [cascode link]
build/mycircuit.hl.cas               # linked (complete dependency graph)
build/mycircuit.synth.yaml           # extracted synthesis guidance
    ↓ [cascode syn]
build/mycircuit.el.cas               # synthesized (all circuits at EL)
    ↓ [cascode emit]
build/mycircuit.sp + benches         # SPICE output
```

**`cascode link`** resolves the dependency graph:
- Input: any `.cas` file
- Follows all `include` directives recursively
- Gathers all referenced interfaces, benches, circuits, functions
- Extracts `synth {}` blocks into a sidecar `.synth.yaml` file
- Output directory: `build/` by default, override with `--out <dir>`
- Output: `.hl.cas`, `.ml.cas`, or `.el.cas` (suffix based on highest level found)
- Guarantee: no unresolved includes, all references satisfied, synth guidance extracted

**`cascode syn`** performs topology selection and sizing:
- Input: `.hl.cas` or `.ml.cas` only (rejects plain `.cas` by suffix)
- Validation: errors if any `include` statements remain
- Output: `.el.cas`
- Guarantee: all circuits at level EL, all sizing resolved
- Note: `cascode syn` is out of scope for this RFC; only the interface contract is defined

**`cascode emit`** generates simulator-ready output:
- Input: `.el.cas` only
- Validation: errors if any circuit is not level EL
- Output: `.sp` subcircuit files and bench testbenches

### Linked File Conventions

Linked files follow strict conventions that enable fast validation:

1. **Suffix convention**: `.hl.cas`, `.ml.cas`, `.el.cas` indicate linked files; plain `.cas` indicates source
2. **No includes**: A linked file must not contain any `include` statements (validation error if found)
3. **Preserved hierarchy**: Linked files contain multiple circuit/interface/bench declarations as separate blocks, not inlined into a single circuit
4. **Self-contained**: The linked file contains everything needed to process the design with no external references

### Three Distinct Specification Concepts

The language maintains clear separation between three related but distinct concepts:

**`env {}`** describes the operating environment and design intent:
- Supply voltage, input common-mode range, load specifications, source impedance
- Temperature and process corners
- Represents "how the circuit will be used"

**`harness {}`** describes the bench test infrastructure:
- Concrete voltage sources, bias sources, load elements
- Materializes `env` into physical test setup
- Represents "how we set up the simulation"

**`constraints {}`** describes the requirements and pass/fail criteria:
- Numeric constraints on metrics (`GainBandwidth >= 100MHz`)
- Technology constraints (`L >= 180nm`)
- Graph constraints (cardinality, path existence)
- Represents "what the circuit must achieve"

### Synthesis Agent Interface

The synthesis agent is treated as a black box. This RFC defines only its input/output contract:
- Input: a linked `.hl.cas` or `.ml.cas` file
- Output: a linked `.el.cas` file

The internal workings of the synthesis agent (topology selection algorithms, sizing optimization, etc.) are out of scope for this specification.

### Synthesis Guidance Extraction

The `synth {}` block remains part of the Cascode language, allowing authors to express synthesis preferences inline with their design. During linking, this block is extracted into a structured sidecar file:

```
mycircuit.cas                        # source (contains synth {} block)
    ↓ [cascode link]
build/mycircuit.hl.cas               # linked (synth block removed)
build/mycircuit.synth.yaml           # extracted synthesis guidance
```

The sidecar file is structured YAML that the synthesis agent consumes. This separation keeps the linked Cascode file purely declarative (what the circuit *is*) while preserving synthesis intent (how it *should be synthesized*) in a machine-readable format.

`cascode syn` auto-discovers the sidecar by convention. The `--guidance <file>` flag overrides for one-off cases.

The `.synth.yaml` schema is out of scope for this RFC and will be specified separately.

### Feature Disposition

The unification adopts ACIR syntax as the baseline with the following feature decisions:

| Feature | Disposition |
|---------|-------------|
| `circuit` keyword | Keep (replaces `module`) |
| `interface` keyword | Keep (replaces `trait`) |
| `motif` keyword | Replace with `circuit inline` |
| `fill {}` block | Keep |
| `env {}` block | Keep (distinct from harness) |
| `harness {}` block | Keep |
| `constraints {}` block | Keep |
| `char {}` manifests | Remove (out of scope) |
| `synth {}` block | Keep (extracted to sidecar during link) |
| `include` directive | Add (new grammar feature) |
| `wrap spice` | Add to grammar |
| `pair` / `repeat` / `match` sugar | Add to grammar |
| Terminal binding syntax (`.terminal--net`) | Keep |
| Size packs (`size(W=2u, L=180n)`) | Keep |
| Monomorphization naming | Keep |
| Diagnostic codes | Rename ACIR0xxx → CAS0xxx |

---

## 1. Language Unification

### 1.1 Background

The Cascode toolchain currently maintains two separate language specifications:
- The original Cascode language (`spec/language/Ch02_Cascode.md`)
- ACIR, the Analog Circuit Intermediate Representation (`spec/language/Ch03_ACIR.md`)

These languages have converged to the point where maintaining them separately creates unnecessary complexity. The redefinition of ACIR proposed below is practically identical to the original Cascode language.

### 1.2 Decision

ACIR and Cascode merge into a single language: **Cascode**.

This unification supersedes both the Cascode Language Spec and the ACIR Language Spec. There is now one language with one grammar, one compiler, and one specification.

### 1.3 File Extension

The unified language uses the `.cas` file extension.

### 1.4 Implementation Impact

The following changes implement language unification:

**Files to Delete:**
- `tools/compiler/SimpleCascodeCompiler.cs` - the existing Cascode compiler
- `tools/parser/Cascode.g4` - the existing Cascode grammar

**Files to Rename/Rebrand:**
- `tools/acir/ACIR.g4` → `tools/parser/Cascode.g4`
- `tools/acir/ACIRDocument.cs` → `CascodeDocument.cs`
- `tools/acir/ACIRReader.cs` → `CascodeReader.cs`
- `tools/acir/ACIRWriter.cs` → `CascodeWriter.cs`
- All `ACIR`-prefixed classes → `Cascode`-prefixed
- Namespace `Cascode.ACIR` → `Cascode.Language`
- Diagnostic codes `ACIR0xxx` → `CAS0xxx`

**Grammar Extensions (add to renamed Cascode.g4):**

The ACIR grammar serves as the base. The following features must be added after unification:
- `include` directive for dependency resolution
- `synth {}` block for synthesis guidance
- `pair` / `repeat` / `match` sugar constructs
- `wrap spice` for SPICE subcircuit wrapping
- Declarative bench grammar (see Appendix A)

**CLI Updates:**
- `cascode emit`: Change input from `.cir` to `.el.cas`
- `cascode verify`: Change input from `.cir` to `.el.cas`
- `cascode bench`: Change input from `.cir` to `.el.cas`
- `cascode link`: New command (see Big Picture Goals)

**Test Migration:**
- `tests/golden/acir/**/*.cir` → `tests/golden/**/*.cas`
- All existing golden ACIR tests become golden Cascode tests

**Spec Documents:**
- `spec/language/Ch03_ACIR.md` → merged into `Ch02_Cascode.md` or removed
- `spec/language/Ch04_Testbench_Templates.md` → `Ch04_Benches.md`

---

## 2. Problem Statement

### 2.1 Current State

The existing bench system encodes hookup topology in builtin benches, requiring the same measurement intent to be reimplemented for each circuit interface variant. This leads to duplicated measurement logic, fixes replicated across topology variants, and combinatorial growth across device categories, interface variants, and simulator backends.

### 2.2 Desired Properties

The bench system should provide:
1. Total freedom and flexibility in bench construction
2. Compile-time resolution of environment and constraint values
3. Rich measurement expressions that execute after simulation
4. Bench reuse through interface inheritance
5. Clear separation between bench definition, binding, and constraint specification

---

## 3. Bench System Axioms

The following axioms define the bench system semantics. Implementations must adhere to these axioms.

**A1. Strongly Typed Function Arguments**

Every function parameter must have an explicit name and type. Types are semantic categories representing physical quantities.

```cascode
function foo(Frequency hp, Frequency lp) : Frequency { ... }
```

**A2. Analysis Scope in Measurements**

All declared analyses are in scope within `measurements {}`. If an analysis is declared as `ACAnalysis ac = new ACAnalysis(...)`, then `ac` is directly accessible in every measurement body and helper function within `measurements {}`.

**A3. Constraints Scope**

`constraints` is in scope throughout the bench. The bound circuit's constraints block is accessible as `constraints.<MetricName>`, returning the constraint value or null/absent if not constrained.

**A4. Harness Scope in Bench**

`harness` is in scope throughout the bench.

**A5. Harness Scope in Bench Bindings**

`harness` is in scope inside bench bindings.

**A6. Environment Scope**

`env` is in scope throughout the bench. The bound circuit's environment block is accessible as `env.<ParamName>`.

**A7. Explicit Typing**

All variables must be explicitly typed at declaration. Types are semantic categories: `Frequency`, `VoltageRatio`, `TransferFunction`, `RealFunction`, etc.

```cascode
Frequency f = 1MHz
VoltageRatio g = eval(G, f)
```

**A8. Multi-line Expression Parenthesization**

Multi-line expressions must be parenthesized.

**A9. Control Flow**

Control flow uses `if`/`else` blocks with explicit `return`. Ternary operators are not supported.

**A10. Measurement Declaration Syntax**

Measurement declarations use `Name : Unit` syntax to specify the output unit.

```cascode
PassbandGain : dB { ... }
GainBandwidth : Hz { ... }
```

**A11. Reserved for Future Use**

A `Bandwidth` type may be introduced to enable provenance-based type checking, representing the difference or sum of `Frequency` values distinct from an absolute `Frequency`.

**A12. Constraint-Driven Bench Emission**

Only emit and execute benches for which constraints are defined.

**A13. Whole-Bench Simulation**

If even one measurement from a bench is used as a constraint, the whole bench is simulated.

---

## 4. Bench Construct Specification

### 4.1 Bench Definition Syntax

A bench defines terminals, constructs a test circuit, declares analyses, and specifies measurements.

```cascode
bench BenchName {
  stim TERMINAL_NAME : TerminalType
  resp TERMINAL_NAME : TerminalType

  fill {
    // Test circuit construction
  }

  // Optional helper functions (bench-local scope)
  function helper_name(Type param) : ReturnType {
    // Function body
  }

  analysis {
    // Analysis declarations
  }

  measurements {
    // Measurement definitions
  }
}
```

### 4.2 Terminal Declarations

Terminals are declared with stimulus (`stim`) or response (`resp`) roles:

```cascode
stim IN : Diff       // Differential stimulus input
resp OUT : analog    // Single-ended response output
```

Terminal types and their valid roles:

| Type | Description | Valid for `stim` | Valid for `resp` |
|------|-------------|------------------|------------------|
| `Diff` | Differential signal pair with `.P` and `.N` sub-terminals | Yes | Yes |
| `analog` | Single-ended analog signal | Yes | Yes |
| `supply` | Power supply terminal | Yes | No |
| `ground` | Reference ground terminal | No | No |

The `supply` type is valid only for stimulus terminals (e.g., PSRR benches that inject signals on supply rails). The `ground` type is not valid for either role as grounds are handled implicitly through the test harness.

### 4.3 Fill Block

The `fill {}` block constructs the test circuit using standard Cascode circuit-building operations. Users can instantiate test instruments, probes, impedances, and even complete circuits.

```cascode
fill {
  net vcm : analog
  net gnd : ground

  GND _ = new GND() {
    .GND--gnd
  }

  VDC commonModeVDC = new VDC(env.InputCommonModeRange) {
    .P--vcm
    .N--gnd
  }

  VAC acP = new VAC(0.5, phase=0deg) {
    .N--vcm
  }

  Impedance sourceP = new Impedance(env.SourceImpedance / 2)

  acP.P--sourceP.P, sourceP.N--IN.P
}
```

Key features:
- Component values can reference `env` for compile-time resolution
- Component values can reference `constraints` for constraint-aware configuration
- Anonymous instantiation using `_` as the instance name
- Standard net declarations and connectivity syntax

### 4.4 Helper Functions

Benches can declare helper functions with file-level or bench-local scope:

```cascode
function infer_hp_corner(Frequency fallback) : Frequency {
  if constraints.HighpassBandwidth {
    return constraints.HighpassBandwidth
  }
  return fallback
}
```

Functions have access to:
- `constraints` - the bound circuit's constraint values
- `env` - the bound circuit's environment values
- `ac`, `dc`, etc. - declared analyses (within measurements scope)

### 4.5 Analysis Block

The `analysis {}` block declares typed analysis instances:

```cascode
analysis {
  ACAnalysis ac = new ACAnalysis(
    space=Log,
    samples=100,
    start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
    stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }))
}
```

Analysis parameters can include conditional expressions that reference constraint values for adaptive analysis configuration.

### 4.6 Measurements Block

The `measurements {}` block defines typed measurement expressions:

```cascode
measurements {
  measurement PassbandGain : dB {
    TransferFunction H = transfer(ac, IN, OUT)
    RealFunction G = db20(mag(H))

    Frequency hp = infer_hp_corner(1Hz)
    Frequency lp = infer_lp_corner()
    Frequency fpb = calc_passband_freq(hp, lp)

    return eval(G, fpb)
  }

  measurement GainBandwidth : Hz {
    TransferFunction H = transfer(ac, IN, OUT)
    RealFunction G = db20(mag(H))

    return find_crossing(G, 0dB, dir=falling, cross=1, from=ac.start, to=ac.stop)
  }
}
```

Measurement bodies have access to:
- Declared analyses by name
- Helper functions
- `constraints` and `env` scopes
- Measurement primitives (`transfer`, `eval`, `find_crossing`, etc.)

### 4.7 Cross-Measurement References

Measurements can reference other measurements in the same bench:

```cascode
measurement BandpassBandwidth : Hz {
  return abs(LowpassBandwidth - HighpassBandwidth)
}
```

---

## 5. Bench Binding

### 5.1 Interface Bench Binding

Interfaces specify bench bindings in a `benches {}` block:

```cascode
interface SingleEndedOpAmp {
  supply VDD
  ground VSS
  input IN : Diff
  output OUT : analog

  benches {
    bind DiffToSETransfer as transfer_bench {
      bench.IN--dut.IN
      bench.OUT--dut.OUT

      GND localGround = new GND()
      VDC dcSource = new VDC(0V) {
        .P--dut.VDD
        .N--localGround
      }
      dut.VSS--localGround
    }
  }
}
```

The binding block:
- Maps bench terminals to DUT terminals using `bench.X--dut.Y` syntax
- Can instantiate additional components needed for the test harness
- Has access to `harness` for parameterization
- Uses `dut` as the reserved keyword referring to the bound circuit

### 5.2 Circuit Bench Binding

Circuits can define additional bench bindings or override inherited bindings:

```cascode
circuit My5TOTA implements SingleEndedOpAmp {
  level EL

  supply VDD
  input IN : Diff
  output OUT : analog
  ground VSS

  fill {
    // 5T OTA implementation
  }

  constraints {
    c_psrr_a = psrr_avdd::PSRR >= 70dB
    c_psrr_d = psrr_dvdd::PSRR >= 60dB
  }

  benches {
    bind PSRRBench as psrr_avdd {
      GND _ = new GND() {
        .GND--dut.VSS
      }
      bench.SUPPLY--dut.AVDD
      bench.OUT--dut.OUT
      dut.DVDD--harness.DVDD
    }
  }
}
```

### 5.3 Bench Inheritance

Circuits inherit interface benches. The circuit's `benches {}` block can:
- Add new bench bindings
- Override inherited bindings (same `as` name)

### 5.4 Multi-Supply Binding

For circuits with multiple supplies, a bench can be bound multiple times with different names:

```cascode
circuit MultiSupplyOTA implements SingleEndedOpAmp {
  level EL

  supply AVDD
  alias VDD=AVDD  // Interface contract satisfaction
  supply DVDD
  input IN : Diff
  output OUT : analog
  ground VSS

  constraints {
    c_psrr_a = psrr_avdd::PSRR >= 70dB
    c_psrr_d = psrr_dvdd::PSRR >= 60dB
  }

  benches {
    bind PSRRBench as psrr_avdd {
      bench.SUPPLY--dut.AVDD
      // ...
    }
    bind PSRRBench as psrr_dvdd {
      bench.SUPPLY--dut.DVDD
      // ...
    }
  }
}
```

---

## 6. Include System

### 6.1 Include Syntax

```cascode
include lib.std           // Includes all files in lib/std/
include lib.std.amp       // Includes all files in lib/std/amp/
include BenchFunctions    // Includes BenchFunctions.cas from same directory
```

### 6.2 Include Resolution

- `lib.X` resolves to the `lib/X/` directory, recursively including all `.cas` files
- Relative names resolve to the same directory as the including file
- Include statements must appear at file level, before any definitions

### 6.3 Standard Library Structure

```
lib/
├── std/
│   ├── BenchFunctions.cas    // Common helper functions
│   ├── DiffToSE.cas          // Differential-to-single-ended benches
│   └── amp/
│       ├── SingleEndedOpAmp.cas
│       └── FullyDifferentialOpAmp.cas
└── benches/
    └── ...
```

---

## 7. Execution Model

### 7.1 Runtime Post-Processing

Measurement expressions execute as runtime post-processing after simulation completes. The execution flow is:

1. Bench binding resolves terminal connections
2. Fill block constructs the test circuit
3. Analysis parameters are resolved (potentially using constraint values)
4. Simulation executes
5. Measurement expressions evaluate over simulation results

### 7.2 DUT Keyword

`dut` is a reserved keyword (lowercase) within bench bindings that refers to the circuit being tested. It provides access to the circuit's terminals for binding and internal nodes for measurement (see Section 15).

The keyword is always lowercase `dut`, not `DUT` or `Dut`.

### 7.3 Constraint-Driven Analysis

Analysis parameters can adapt based on constraint values:

```cascode
start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz })
```

This enables benches to focus simulation resources on the frequency ranges relevant to the specified constraints.

---

## 8. Complete Example

### 8.1 Reusable Helper Functions

`lib/std/BenchFunctions.cas`:
```cascode
// File-level function available wherever this file is included
function calc_passband_freq(Frequency hp, Frequency lp) : Frequency {
  Frequency f = sqrt(hp * lp)

  if f < ac.start { return ac.start }
  if f > ac.stop  { return ac.stop  }
  return f
}
```

### 8.2 Differential-to-Single-Ended Transfer Bench

`lib/std/DiffToSE.cas`:
```cascode
include BenchFunctions

bench DiffToSETransfer {
  stim IN : Diff
  resp OUT : analog

  fill {
    net vcm : analog
    net gnd : ground

    GND _ = new GND() {
      .GND--gnd
    }

    VDC commonModeVDC = new VDC(env.InputCommonModeRange) {
      .P--vcm
      .N--gnd
    }

    VAC acP = new VAC(0.5, phase=0deg) {
      .N--vcm
    }
    VAC acN = new VAC(0.5, phase=180deg) {
      .N--vcm
    }

    Impedance sourceP = new Impedance(env.SourceImpedance / 2)
    Impedance sourceN = new Impedance(env.SourceImpedance / 2)

    acP.P--sourceP.P, sourceP.N--IN.P
    acN.P--sourceN.P, sourceN.N--IN.N

    Impedance load = new Impedance(env.LoadImpedance) {
      OUT--.P
      .N--gnd
    }
  }

  function infer_hp_corner(Frequency fallback) : Frequency {
    if constraints.HighpassBandwidth {
      return constraints.HighpassBandwidth
    }
    return fallback
  }

  function infer_lp_corner() : Frequency {
    if constraints.LowpassBandwidth {
      return constraints.LowpassBandwidth
    }
    if constraints.GainBandwidth {
      return constraints.GainBandwidth / 100
    }
    return ac.stop
  }

  analysis {
    ACAnalysis ac = new ACAnalysis(
      space=Log,
      samples=100,
      start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
      stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }))
  }

  measurements {
    measurement PassbandGain : dB {
      TransferFunction H = transfer(ac, IN, OUT)
      RealFunction G = db20(mag(H))

      Frequency hp = infer_hp_corner(1Hz)
      Frequency lp = infer_lp_corner()
      Frequency fpb = calc_passband_freq(hp, lp)

      return eval(G, fpb)
    }

    measurement GainBandwidth : Hz {
      TransferFunction H = transfer(ac, IN, OUT)
      RealFunction G = db20(mag(H))

      return find_crossing(G, 0dB, dir=falling, cross=1, from=ac.start, to=ac.stop)
    }

    measurement LowpassBandwidth : Hz {
      TransferFunction H = transfer(ac, IN, OUT)
      RealFunction G = db20(mag(H))

      Frequency hp = infer_hp_corner(1Hz)
      Frequency lp = infer_lp_corner()
      Frequency fpb = calc_passband_freq(hp, lp)

      VoltageRatio gpb = eval(G, fpb)
      VoltageRatio thr = gpb - 3dB

      return find_crossing(G, thr, dir=falling, cross=1, from=fpb, to=ac.stop)
    }

    measurement HighpassBandwidth : Hz {
      TransferFunction H = transfer(ac, IN, OUT)
      RealFunction G = db20(mag(H))

      Frequency hp = infer_hp_corner(1Hz)
      Frequency lp = infer_lp_corner()
      Frequency fpb = calc_passband_freq(hp, lp)

      VoltageRatio gpb = eval(G, fpb)
      VoltageRatio thr = gpb - 3dB

      return find_crossing(G, thr, dir=rising, cross=1, from=ac.start, to=fpb)
    }

    measurement BandpassBandwidth : Hz {
      return abs(LowpassBandwidth - HighpassBandwidth)
    }
  }
}
```

### 8.3 Interface with Bench Binding

`lib/std/amp/SingleEndedOpAmp.cas`:
```cascode
include lib.std

interface SingleEndedOpAmp {
  supply VDD
  ground VSS
  input IN : Diff
  output OUT : analog

  benches {
    bind DiffToSETransfer as transfer_bench {
      bench.IN--dut.IN
      bench.OUT--dut.OUT

      GND localGround = new GND()
      VDC dcSource = new VDC(0V) {
        .P--dut.VDD
        .N--localGround
      }
      dut.VSS--localGround
    }
  }
}
```

### 8.4 Circuit Implementation

`MyOTA.cas`:
```cascode
include lib.std

circuit My5TOTA implements SingleEndedOpAmp {
  level EL

  supply VDD
  input IN : Diff
  output OUT : analog
  ground VSS

  fill {
    // 5T OTA implementation
  }

  constraints {
    c_gbw = transfer_bench::GainBandwidth >= 100MHz
    c_gain = transfer_bench::PassbandGain >= 50dB
  }
}
```

---

## 9. Design Decisions

### 9.1 Confirmed Decisions

| Decision | Resolution | Rationale |
|----------|------------|-----------|
| Execution model | Runtime post-processing | Expressions execute after simulation for maximum flexibility |
| DUT keyword | Reserved keyword | Clear, unambiguous reference to the bound circuit |
| Bench inheritance | Circuits inherit interface benches with override | Reduces duplication while allowing customization |
| Include system | Directory-based with recursive resolution | Supports modular library organization |
| Backward compatibility | None - `builtin` removed entirely | Clean break enables simpler implementation |

### 9.2 Removed Features

The `builtin` keyword and all builtin bench references are removed. All benches are now defined in Cascode source files using the declarative bench construct.

---

## 10. Implementation Plan

Note: `cascode syn` (the synthesis agent) is out of scope for this RFC. Only the interface contract is defined; implementation is separate.

### 10.1 Phase 0: Language Unification (~150 LOC deleted, ~200 LOC renamed)

**PR 0.1: Delete Redundant Compiler**
- Delete `tools/compiler/SimpleCascodeCompiler.cs`
- Delete `tools/parser/Cascode.g4`
- Update any references

**PR 0.2: Rename ACIR to Cascode**
- Rename `tools/acir/ACIR.g4` → `tools/parser/Cascode.g4`
- Rename all `ACIR*` classes to `Cascode*`
- Update namespace from `Cascode.ACIR` to `Cascode.Language`
- Rename diagnostic codes `ACIR0xxx` → `CAS0xxx`
- Update imports and references throughout

**PR 0.3: Migrate Golden Tests and CLI**
- Rename `tests/golden/acir/**/*.cir` → `tests/golden/**/*.cas`
- Update test infrastructure to use `.cas` extension
- Update `cascode emit`, `cascode verify`, `cascode bench` to accept `.cas` instead of `.cir`
- Verify all tests pass

### 10.2 Phase 1: Grammar Extensions (~300 LOC)

**PR 1.1: Include Directive and Link Command**
- Add `include` directive grammar
- Implement `cascode link` command
- Implement include resolution (recursive)
- Implement linked file output to `build/` directory

**PR 1.2: Synth Block and Sidecar Extraction**
- Add `synth {}` block grammar
- Implement extraction to `.synth.yaml` sidecar during link
- Implement `--guidance` flag for `cascode syn` interface

**PR 1.3: Sugar Constructs**
- Add `pair` construct grammar
- Add `repeat idx in [start:end]` grammar
- Add `match` / `case` grammar
- Add `wrap spice` grammar

**PR 1.4: Bench Definition Grammar**
- Add `bench` keyword and block structure
- Add `stim`/`resp` terminal declarations
- Add `fill {}` block parsing
- Add `analysis {}` block parsing
- Add `measurements {}` block parsing

**PR 1.5: Helper Function Grammar**
- Add function declaration syntax with typed parameters
- Add function body parsing with control flow
- Add return type declarations

**PR 1.6: Bench Binding Grammar**
- Add `benches {}` block in interfaces and circuits
- Add `bind ... as ...` syntax
- Add bench-to-DUT connection syntax

### 10.3 Phase 2: Semantic Type System (~400 LOC)

**PR 2.1: Physical Quantity Types**
- Implement `Frequency`, `VoltageRatio`, `TransferFunction`, `RealFunction` types
- Implement type checking for variable declarations
- Implement type inference for expressions

**PR 2.2: Analysis Types**
- Implement `ACAnalysis`, `DCAnalysis`, `TranAnalysis` types
- Implement analysis parameter validation
- Implement analysis scope resolution in measurements

**PR 2.3: Measurement Type Checking**
- Validate measurement return types against declared units
- Validate primitive function signatures
- Validate cross-measurement references

### 10.4 Phase 3: Bench Binding and Scoping (~350 LOC)

**PR 3.1: Scope Resolution**
- Implement `constraints` scope access
- Implement `env` scope access
- Implement `harness` scope access
- Implement analysis scope in measurement bodies

**PR 3.2: Bench Inheritance**
- Implement interface bench collection
- Implement circuit bench inheritance
- Implement bench override semantics

**PR 3.3: Terminal Binding Validation**
- Validate bench terminal to DUT terminal mappings
- Validate terminal type compatibility
- Generate binding diagnostics

### 10.5 Phase 4: Runtime Execution Infrastructure (~500 LOC)

**PR 4.1: Expression Evaluator**
- Implement measurement expression interpreter
- Implement primitive functions (`transfer`, `eval`, `find_crossing`, etc.)
- Implement arithmetic and comparison operators

**PR 4.2: Analysis Execution**
- Implement analysis parameter resolution
- Generate simulator-specific analysis commands
- Parse simulation results into typed structures

**PR 4.3: Measurement Orchestration**
- Implement measurement dependency resolution
- Execute measurements in dependency order
- Aggregate results for constraint evaluation

### 10.6 Phase 5: Migration and Documentation (~200 LOC + documentation)

**PR 5.1: Standard Library**
- Create `lib/std/BenchFunctions.cas`
- Create common bench definitions
- Create standard interface definitions

**PR 5.2: Spec Documentation**
- Update `spec/language/Ch02_Cascode.md` for unified language
- Create `spec/language/Ch04_Benches.md` with bench system specification
- Remove or redirect obsolete spec documents

**PR 5.3: Example Migration**
- Migrate `lib/benches/` to declarative syntax
- Update examples to use new bench system
- Add integration tests for migrated examples

---

## 11. Measurement Primitives

### 11.1 Transfer Function Primitives

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `transfer(analysis, stim, resp)` | `(Analysis, Terminal, Terminal) -> TransferFunction` | Complex voltage transfer function |
| `mag(H)` | `TransferFunction -> RealFunction` | Linear magnitude |
| `db20(F)` | `RealFunction -> RealFunction` | 20*log10 conversion |
| `phase(H)` | `TransferFunction -> RealFunction` | Phase in degrees |

### 11.2 Evaluation Primitives

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `eval(F, freq)` | `(RealFunction, Frequency) -> Scalar` | Evaluate at frequency |
| `find_crossing(F, threshold, dir, cross, from, to)` | `(...) -> Frequency` | Find crossing frequency |

### 11.3 Arithmetic

Standard operators (`+`, `-`, `*`, `/`) and functions (`abs`, `sqrt`) are available for scalar values with appropriate unit propagation.

---

## 12. Type System Specification

This section specifies the type system for measurement expressions, including the type algebra, implicit unit conversions, and error handling.

### 12.1 Physical Quantity Types

The measurement system uses semantic types representing physical quantities. All type checking occurs statically at parse time.

| Type | Base Unit | Aliases | Description |
|------|-----------|---------|-------------|
| `Frequency` | Hz | kHz, MHz, GHz, THz | Frequency values |
| `VoltageRatio` | linear | dB, V/V | Voltage gain or attenuation |
| `CurrentRatio` | linear | dB, A/A | Current gain or attenuation |
| `Impedance` | Ω | kΩ, MΩ | Resistance/impedance values |
| `Voltage` | V | mV, µV, nV | Voltage values |
| `Current` | A | mA, µA, nA, pA | Current values |
| `Time` | s | ms, µs, ns, ps | Time values |
| `Phase` | deg | rad | Phase angle |
| `Scalar` | (unitless) | - | Dimensionless quantities |

### 12.2 Function Types

| Type | Description |
|------|-------------|
| `TransferFunction` | Complex-valued function of frequency |
| `RealFunction` | Real-valued function of frequency or time |

### 12.3 Implicit Unit Conversion

The type system performs implicit conversion between unit aliases of the same base type. This enables natural expression of measurements without explicit conversion functions.

Conversion rules:
- Implicit conversion is allowed only within the same physical dimension (e.g., Hz to MHz, dB to linear ratio)
- Cross-dimension conversion requires explicit functions (e.g., `db20()` for linear-to-dB)
- Operations between incompatible types produce parse-time errors

Examples:
```cascode
Frequency f1 = 1MHz        // Stored as 1e6 Hz internally
Frequency f2 = 1000kHz     // Also 1e6 Hz
Frequency f3 = f1 + f2     // Valid: 2e6 Hz

VoltageRatio g1 = 20dB     // Stored as 10.0 linear internally
VoltageRatio g2 = 10       // 10 linear (V/V)
VoltageRatio g3 = g1 * g2  // Valid: 100 linear

// Invalid - cross-dimension
Frequency bad = g1         // Parse-time error: cannot convert VoltageRatio to Frequency
```

### 12.4 Operator Type Rules

| Operator | Left Type | Right Type | Result Type |
|----------|-----------|------------|-------------|
| `+`, `-` | T | T | T (same physical type) |
| `*` | T | Scalar | T |
| `*` | Scalar | T | T |
| `/` | T | Scalar | T |
| `/` | T | T | Scalar |
| `<`, `>`, `<=`, `>=`, `==` | T | T | Boolean |

### 12.5 Function Signatures

Transfer function primitives:
```
transfer(Analysis, Terminal, Terminal) -> TransferFunction
mag(TransferFunction) -> RealFunction
phase(TransferFunction) -> RealFunction
db20(RealFunction) -> RealFunction
db10(RealFunction) -> RealFunction
```

Evaluation primitives:
```
eval(RealFunction, Frequency) -> Scalar
eval(TransferFunction, Frequency) -> Complex
find_crossing(RealFunction, threshold, dir, cross, from, to) -> Frequency
```

Mathematical functions:
```
abs(T) -> T where T is numeric
sqrt(Scalar) -> Scalar
sqrt(Frequency * Frequency) -> Frequency
```

---

## 13. Error Handling Specification

This section specifies error behavior for the bench system.

### 13.1 Parse-Time Errors

The following conditions produce errors during document parsing (before simulation):

| Condition | Error Message Pattern |
|-----------|----------------------|
| Type mismatch | `Cannot assign {source_type} to {target_type}` |
| Undefined variable | `Undefined variable '{name}'` |
| Undefined analysis | `Analysis '{name}' not declared in analysis block` |
| Circular measurement dependency | `Circular dependency detected: {cycle_path}` |
| Undefined `env` parameter | `Environment parameter '{name}' not defined` |
| Undefined `constraints` metric | Warning only: `Constraint '{name}' not defined, evaluates to null` |
| Invalid terminal binding | `Terminal '{bench_term}' type incompatible with '{dut_term}'` |
| Missing required terminal | `Required bench terminal '{name}' not bound` |

### 13.2 Runtime Errors

The following conditions produce errors during measurement evaluation (after simulation):

| Condition | Behavior |
|-----------|----------|
| `find_crossing` no result | Measurement marked as failed; error message: `No crossing found for {measurement_name}` |
| `eval` at frequency outside analysis range | Extrapolation with warning: `Frequency {f} outside analysis range [{start}, {stop}]` |
| Division by zero | Measurement marked as failed; error message: `Division by zero in {measurement_name}` |
| Null constraint access without guard | Measurement marked as failed; error message: `Constraint '{name}' is null and not guarded` |

### 13.3 Constraint Guard Pattern

Measurements that reference optional constraints must use guard patterns:

```cascode
function infer_hp_corner(Frequency fallback) : Frequency {
  if constraints.HighpassBandwidth {    // Guard against null
    return constraints.HighpassBandwidth
  }
  return fallback
}
```

Unguarded access to a null constraint produces a runtime error.

### 13.4 Circular Dependency Detection

Circular dependencies between measurements are detected at parse time. The detection algorithm:
1. Build dependency graph from cross-measurement references
2. Perform topological sort
3. If cycle detected, report all measurements in the cycle

Example error:
```
Circular dependency detected: PassbandGain -> BandpassBandwidth -> LowpassBandwidth -> PassbandGain
```

---

## 14. Environment Block Specification

The `env {}` block describes the operating environment and design intent for a circuit. It is distinct from `harness {}` (test infrastructure) and `constraints {}` (requirements).

### 14.1 Standard Environment Parameters

Benches may reference the following standard environment parameters:

| Parameter | Type | Description |
|-----------|------|-------------|
| `InputCommonModeRange` | Voltage | Nominal input common-mode voltage |
| `OutputCommonModeRange` | Voltage | Nominal output common-mode voltage |
| `SourceImpedance` | Impedance | Source impedance driving the input |
| `LoadImpedance` | Impedance | Load impedance at output |
| `SupplyVoltage` | Voltage | Nominal supply voltage |
| `Temperature` | Temperature | Operating temperature |

### 14.2 Environment Block Syntax

```cascode
circuit MyAmplifier implements SingleEndedOpAmp {
  level EL

  env {
    InputCommonModeRange = 0.9V
    OutputCommonModeRange = 0.9V
    SourceImpedance = 50Ω
    LoadImpedance = 1MΩ || 1pF    // Parallel combination
    SupplyVoltage = 1.8V
    Temperature = 27C
  }

  // ... rest of circuit
}
```

### 14.3 Environment Access in Benches

Within bench `fill {}` blocks, environment values are accessed via the `env.` prefix:

```cascode
fill {
  VDC commonModeVDC = new VDC(env.InputCommonModeRange) {
    .P--vcm
    .N--gnd
  }

  Impedance sourceP = new Impedance(env.SourceImpedance / 2)
}
```

---

## 15. Internal Node Access

Measurements can access internal nodes of the DUT using the `dut.` prefix. This enables characterization of internal circuit behavior.

### 15.1 Syntax

```cascode
measurements {
  measurement InternalBias : V {
    return eval(dc, dut.mirror_gate)  // Access internal node
  }

  measurement InternalSwing : V {
    TransferFunction H_internal = transfer(ac, IN, dut.stage1_out)
    RealFunction G = db20(mag(H_internal))
    return eval(G, 1MHz)
  }
}
```

### 15.2 Node Resolution

The `dut.` prefix resolves to internal nets declared in the DUT's `fill {}` block:

```cascode
circuit My5TOTA implements SingleEndedOpAmp {
  level EL

  fill {
    net mirror_gate : analog    // Internal node
    net stage1_out : analog     // Internal node

    // ... implementation using these nodes
  }
}
```

When a bench measurement references `dut.mirror_gate`, the bench binding mechanism ensures the simulator can probe that internal node.

### 15.3 Limitations

- Only nets declared in the DUT's `fill {}` block are accessible
- Device internal nodes (e.g., transistor channel) are not directly accessible
- Hierarchical access (e.g., `dut.subcircuit.node`) is not supported in this version

---

## 16. Standard Library Bench Conversion

This section documents how existing builtin benches map to the declarative bench system.

### 16.1 Conversion Table

| Current Builtin | Declarative Bench | Terminal Types | Notes |
|-----------------|-------------------|----------------|-------|
| `SEOpAmpACBench` | `DiffToSETransfer` | `Diff` → `analog` | Example in RFC Section 8 |
| `FDOpAmpACBench` | `DiffToDiffTransfer` | `Diff` → `Diff` | Output becomes differential |
| `SEOpAmpDCBench` | `DiffToSEDC` | `Diff` → `analog` | DC sweep analysis |
| `FDOpAmpDCBench` | `DiffToDiffDC` | `Diff` → `Diff` | DC sweep, differential output |
| `SEOpAmpStability` | `DiffToSEStability` | `Diff` → `analog` | STB analysis for loop gain |
| `FDOpAmpStability` | `DiffToDiffStability` | `Diff` → `Diff` | STB analysis, differential |
| `PSRRBench` | `SupplyRejection` | `supply` → `analog` | Configurable supply input |
| `CMRRBench` | `CommonModeRejection` | `analog` → `analog` | CM input → output |
| `NoiseBench` | `InputReferredNoise` | `Diff` → `analog` | Noise analysis |
| `TransientBench` | `StepResponse` | `Diff` → `analog` | Transient analysis |

### 16.2 Unification Opportunities

Many builtin benches differ only in terminal topology. The declarative system unifies them through terminal type declarations:

```cascode
// Single bench definition handles both SE and FD by terminal type
bench TransferCharacterization {
  stim IN : Diff
  resp OUT : analog | Diff    // Type determined at binding time

  // ... same measurement logic for both cases
}
```

### 16.3 Migration Notes

Per the Big Picture Goals, there is no automated migration tooling. Documents using `builtin` syntax will produce parse errors. Users must:
1. Replace `builtin` bench references with declarative bench definitions
2. Add explicit bench bindings in interface/circuit `benches {}` blocks
3. Update constraint references from `BenchName::Metric` to `binding_name::Metric`

---

## 17. Future Work

### 17.1 Transient Analysis Support

Extend the analysis and measurement system to support transient simulations with time-domain primitives.

### 17.2 Noise Analysis Support

Add noise analysis types and spectral density integration primitives.

### 17.3 Monte Carlo and Statistical Constraints

Support statistical constraint forms like `yield(Measurement >= value) >= percentage`.

### 17.4 Multi-Corner Sweep Integration

Integrate PVT corner sweeps into the constraint evaluation framework.

---

## 18. References

1. Cascode Language Specification, Chapters 1-3
2. ACIR Specification (superseded by this RFC)
3. Razavi, B. "Design of Analog CMOS Integrated Circuits"

---

## Appendix A: Formal Grammar

This appendix provides the complete ANTLR4 grammar rules for the declarative bench system. These rules replace the existing `benchDef` rule in the grammar entirely.

### A.1 Bench Definition

```antlr
// Replace existing benchDef rule entirely
benchDef
    : BENCH_KW name=IDENT LBRACE benchBody RBRACE
    ;

benchBody
    : terminalDecl* fillBlock? helperFunction* analysisBlock? measurementsBlock?
    ;
```

### A.2 Terminal Declarations

```antlr
terminalDecl
    : terminalRole IDENT COLON terminalType
    ;

terminalRole
    : STIM_KW
    | RESP_KW
    ;

terminalType
    : DIFF_KW
    | ANALOG_KW
    | SUPPLY_KW
    | GROUND_KW
    ;
```

Terminal role restrictions:
- `stim` (stimulus) accepts: `Diff`, `analog`, `supply`
- `resp` (response) accepts: `Diff`, `analog`
- `supply` and `ground` are not valid for `resp` terminals

### A.3 Fill Block

The fill block uses the existing `fillStatement` rules with the addition of `env` and `constraints` access:

```antlr
fillBlock
    : FILL_KW LBRACE fillStatement* RBRACE
    ;

// fillStatement unchanged from existing grammar, but expressions
// may now include scopedAccess for env/constraints
scopedAccess
    : ENV_KW DOT IDENT
    | CONSTRAINTS_KW DOT IDENT
    | HARNESS_KW DOT IDENT
    ;
```

### A.4 Helper Functions

```antlr
helperFunction
    : FUNCTION_KW name=IDENT LPAREN typedParamList? RPAREN COLON returnType LBRACE functionBody RBRACE
    ;

typedParamList
    : typedParam (COMMA typedParam)*
    ;

typedParam
    : physicalType IDENT
    ;

returnType
    : physicalType
    | BOOL_KW
    ;

physicalType
    : FREQUENCY_TYPE
    | VOLTAGE_RATIO_TYPE
    | TRANSFER_FUNCTION_TYPE
    | REAL_FUNCTION_TYPE
    | IMPEDANCE_TYPE
    | VOLTAGE_TYPE
    | CURRENT_TYPE
    | TIME_TYPE
    | PHASE_TYPE
    | SCALAR_TYPE
    ;

functionBody
    : statement*
    ;

statement
    : variableDecl
    | ifStatement
    | returnStatement
    ;

variableDecl
    : physicalType IDENT EQ measurementExpr
    ;

ifStatement
    : IF_KW conditionalExpr LBRACE statement* RBRACE (ELSE_KW LBRACE statement* RBRACE)?
    ;

returnStatement
    : RETURN_KW measurementExpr
    ;
```

### A.5 Analysis Block

```antlr
analysisBlock
    : ANALYSIS_KW LBRACE analysisDecl* RBRACE
    ;

analysisDecl
    : analysisType name=IDENT EQ NEW_KW analysisType LPAREN analysisParams RPAREN
    ;

analysisType
    : AC_ANALYSIS_TYPE
    | DC_ANALYSIS_TYPE
    | TRAN_ANALYSIS_TYPE
    | NOISE_ANALYSIS_TYPE
    | STB_ANALYSIS_TYPE
    ;

analysisParams
    : analysisParam (COMMA analysisParam)*
    ;

analysisParam
    : IDENT EQ conditionalExpr
    ;

conditionalExpr
    : LPAREN IF_KW scopedAccess LBRACE measurementExpr RBRACE ELSE_KW LBRACE measurementExpr RBRACE RPAREN
    | measurementExpr
    ;
```

### A.6 Measurements Block

```antlr
measurementsBlock
    : MEASUREMENTS_KW LBRACE measurementDecl* RBRACE
    ;

measurementDecl
    : MEASUREMENT_KW name=IDENT COLON unitType LBRACE measurementBody RBRACE
    ;

unitType
    : IDENT    // Hz, dB, V, A, s, deg, etc.
    ;

measurementBody
    : statement*
    ;

measurementExpr
    : measurementExpr (PLUS | MINUS) mulMeasurementExpr
    | mulMeasurementExpr
    ;

mulMeasurementExpr
    : mulMeasurementExpr (STAR | SLASH) unaryMeasurementExpr
    | unaryMeasurementExpr
    ;

unaryMeasurementExpr
    : MINUS unaryMeasurementExpr
    | measurementAtom
    ;

measurementAtom
    : LPAREN measurementExpr RPAREN
    | functionCall
    | scopedAccess
    | dutAccess
    | IDENT
    | QUANTITY
    | NUMBER
    ;

functionCall
    : IDENT LPAREN argList? RPAREN
    ;

dutAccess
    : DUT_KW DOT pinRef
    ;
```

### A.7 Bench Binding (in Interfaces and Circuits)

```antlr
// Add to circuitMember and interfaceMember
benchesSection
    : BENCHES_KW LBRACE benchBinding* RBRACE
    ;

benchBinding
    : BIND_KW benchName=IDENT AS_KW bindingName=IDENT LBRACE bindingStatement* RBRACE
    ;

bindingStatement
    : terminalMapping
    | instanceDecl
    | dutConnection
    ;

terminalMapping
    : BENCH_KW DOT IDENT WIRE_OP DUT_KW DOT pinRef
    ;

dutConnection
    : DUT_KW DOT pinRef WIRE_OP pinRef
    ;
```

### A.8 Environment Block

```antlr
// Add to circuitMember
envSection
    : ENV_KW LBRACE envStatement* RBRACE
    ;

envStatement
    : IDENT EQ envValue
    ;

envValue
    : QUANTITY
    | parallelCombination
    | NUMBER IDENT?
    ;

parallelCombination
    : envValue PIPEPIPE envValue
    ;
```

### A.9 New Lexer Tokens

```antlr
// New keywords
STIM_KW         : 'stim' ;
RESP_KW         : 'resp' ;
DIFF_KW         : 'Diff' ;
ANALOG_KW       : 'analog' ;
FUNCTION_KW     : 'function' ;
ANALYSIS_KW     : 'analysis' ;
MEASUREMENTS_KW : 'measurements' ;
MEASUREMENT_KW  : 'measurement' ;
BIND_KW         : 'bind' ;
DUT_KW          : 'dut' ;
ENV_KW          : 'env' ;
IF_KW           : 'if' ;
ELSE_KW         : 'else' ;
RETURN_KW       : 'return' ;

// Type keywords
FREQUENCY_TYPE          : 'Frequency' ;
VOLTAGE_RATIO_TYPE      : 'VoltageRatio' ;
TRANSFER_FUNCTION_TYPE  : 'TransferFunction' ;
REAL_FUNCTION_TYPE      : 'RealFunction' ;
IMPEDANCE_TYPE          : 'Impedance' ;
VOLTAGE_TYPE            : 'Voltage' ;
CURRENT_TYPE            : 'Current' ;
TIME_TYPE               : 'Time' ;
PHASE_TYPE              : 'Phase' ;
SCALAR_TYPE             : 'Scalar' ;

// Analysis type keywords
AC_ANALYSIS_TYPE    : 'ACAnalysis' ;
DC_ANALYSIS_TYPE    : 'DCAnalysis' ;
TRAN_ANALYSIS_TYPE  : 'TranAnalysis' ;
NOISE_ANALYSIS_TYPE : 'NoiseAnalysis' ;
STB_ANALYSIS_TYPE   : 'STBAnalysis' ;
```

### A.10 Removed Grammar Rules

The following rules from the existing grammar are removed:

```antlr
// REMOVED - replaced by new benchDef
benchMember
    : BUILTIN_KW IDENT                           // REMOVED
    | CONFIG_KW LBRACE benchConfigEntry* RBRACE  // REMOVED
    | OUTPUTS_KW LBRACE benchOutput* RBRACE      // REMOVED
    ;

benchConfigEntry  // REMOVED
benchOutput       // REMOVED
```

The `FOR_KW` token usage in `benchDef` is also removed (benches no longer specify `for trait`).

---

## Appendix B: Reserved Keywords

The following identifiers are reserved keywords in the bench system and cannot be used as variable names:

| Keyword | Context |
|---------|---------|
| `bench` | Top-level declaration |
| `stim` | Terminal role |
| `resp` | Terminal role |
| `fill` | Bench block |
| `analysis` | Bench block |
| `measurements` | Bench block |
| `measurement` | Measurement declaration |
| `function` | Helper function declaration |
| `bind` | Bench binding |
| `dut` | DUT reference |
| `env` | Environment access |
| `constraints` | Constraint access |
| `harness` | Harness access |
| `if` | Control flow |
| `else` | Control flow |
| `return` | Return statement |

Type names (`Frequency`, `VoltageRatio`, etc.) are reserved in type position but may be used as identifiers in other contexts.
