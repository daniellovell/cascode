# RFC-0000: Cascode Language Unification and Declarative Bench System

Status: Draft
Authors: Daniel Lovell
Created: 2026-01-25
Last Updated: 2026-01-28
Target Version: Cascode 1.0

---

## Abstract

This RFC proposes the unification of ACIR and Cascode into a single language called Cascode, along with a declarative bench system for measurement abstraction. The motivation remains the same as earlier proposals: avoid topology-driven bench duplication (single-ended vs fully-differential, presence/absence of supply ports, etc.). However, the solution takes a fundamentally different approach.

Rather than introducing network-port theory with `Port(a,b)` abstractions and interface/class taxonomy systems, this RFC introduces a declarative `bench` construct. Benches define terminals with stimulus/response roles, a `fill {}` block for test circuit construction, an `analysis {}` block for typed analysis instantiation, and a `measurements {}` block with typed measurement expressions. Circuits and interfaces bind benches to their terminals and inherit bench definitions through interface implementation.

The key insight is that benches should provide total freedom and flexibility: users can instantiate test instruments, probes, and even complete circuits within a bench. The measurement logic executes as runtime post-processing after simulation, allowing rich expression evaluation over simulation results.

---

## Big Picture Goals

This section captures the architectural decisions that must be realized throughout the specification. Every grammar change, semantic rule, and toolchain command should trace back to one of these goals.

### Unified Language

ACIR and Cascode merge into a single language called Cascode.

Human-authored Cascode source files use the `.cas` extension. Tool-generated, self-contained intermediate artifacts use the `.cai` extension (**Cascode Intermediate**). The separate `.cir` format is eliminated. When the two languages conflict, ACIR features take precedence as the more mature specification. The elaboration level (HL, ML, EL) becomes a property of the content, not a separate format.

### Explicit Elaboration Levels

Every circuit declaration must include an explicit `level` block declaring `HL`, `ML`, or `EL`. There is no level inference. A single file may contain circuits at different levels, but the file's suffix reflects the highest (least elaborated) level present.

### Explicit Compilation Pipeline

The toolchain enforces a three-stage pipeline with clear input/output contracts:

```
mycircuit.cas                        # source (may have includes)
    ↓ [cascode link]
build/mycircuit.hl.cai               # linked (complete dependency graph; self-contained)
build/mycircuit.synth.yaml           # extracted synthesis guidance
    ↓ [cascode syn]
build/mycircuit.el.cai               # synthesized (all circuits at EL; self-contained)
    ↓ [cascode emit]
build/mycircuit.sp + benches         # SPICE output

mycircuit.el.cas                     # source (already EL; may have includes)
    ↓ [cascode link]
build/mycircuit.el.cai               # linked (self-contained)
    ↓ [cascode emit]
build/mycircuit.sp + benches         # SPICE output
```

**`cascode link`** resolves the dependency graph:
- Input: any `.cas` file
- Follows all `include` directives recursively
- Gathers all referenced interfaces, benches, circuits, functions
- Extracts `synth {}` blocks into a sidecar `.synth.yaml` file
- Output directory: `build/` by default, override with `--out <dir>`
- Output: `.hl.cai`, `.ml.cai`, or `.el.cai` (suffix based on highest level found)
- Guarantee: no unresolved includes, all references satisfied, synth guidance extracted
- Guarantee: `.cai` outputs contain no `include` statements (self-contained by construction)
- Note: A source file may already be “effectively emittable” if it contains only `level EL` circuits and primitives. Such a file is still source (it ends in `.cas`), and it may be named `*.el.cas` by convention for readability (not required). In this case, `cascode link` can produce an `.el.cai` without changing the content beyond normalization/validation; the resulting `.el.cai` is directly emittable by definition.

**`cascode syn`** performs topology selection and sizing:
- Input: `.hl.cai` or `.ml.cai` only (rejects plain `.cas` by suffix)
- Validation: errors if any `include` statements remain
- Output: `.el.cai`
- Guarantee: all circuits at level EL, all sizing resolved
- Note: `cascode syn` is out of scope for this RFC; only the interface contract is defined

**`cascode emit`** generates simulator-ready output:
- Input: `.el.cai` or `.el.cas`
- If the input is `.el.cas`, `cascode emit` MUST perform linking (equivalent to running `cascode link` first) to produce an `.el.cai` before emission.
- Validation: errors if any circuit is not level EL
- Output: `.sp` subcircuit files and bench testbenches
- Guarantee: `.el.cai` is directly emittable to SPICE (no further linking or lowering steps required)

### Linked File Conventions

Linked files follow strict conventions that enable fast validation:

1. **Suffix convention**: `.hl.cai`, `.ml.cai`, `.el.cai` indicate linked files; plain `.cas` indicates source
2. **No includes**: A `.cai` file MUST not contain any `include` statements (validation error if found)
3. **Preserved hierarchy**: Linked files contain multiple circuit/interface/bench declarations as separate blocks, not inlined into a single circuit
4. **Self-contained**: The linked file contains everything needed to process the design with no external references

### Why `.cai`

Linked output is intentionally Cascode-shaped (same syntax, same semantics) but it has a different role: it is generated, self-contained, and not intended for manual editing. A distinct extension makes that boundary obvious and gives tooling a reliable way to apply stricter validation rules (e.g., "no includes").

We choose `.cai` (Cascode Intermediate) to keep filenames short and readable (`RcLowpass.el.cai`), avoid multi-suffix naming like `RcLowpass.el.link.cas`, and avoid collisions with established EDA expectations. In particular, `.cir` is widely interpreted as a SPICE circuit file; using it for Cascode-shaped content would be misleading. This also keeps `.cal` available for Cascode layout files.

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
- Input: a linked `.hl.cai` or `.ml.cai` file
- Output: a linked `.el.cai` file

The internal workings of the synthesis agent (topology selection algorithms, sizing optimization, etc.) are out of scope for this specification.

### Synthesis Guidance Extraction

The `synth {}` block remains part of the Cascode language, allowing authors to express synthesis preferences inline with their design. During linking, this block is extracted into a structured sidecar file:

```
mycircuit.cas                        # source (contains synth {} block)
    ↓ [cascode link]
build/mycircuit.hl.cai               # linked (synth block removed)
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
| `interface` keyword | Keep |
| `motif` keyword | Replace with `circuit inline` |
| `port` keyword | Replace with `terminal` (language-wide rename) |
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

Human-authored Cascode source files use the `.cas` file extension. Tool-generated, self-contained intermediate artifacts use the `.cai` file extension.

### 1.3.1 Version Header

Cascode files may include a version header as the first line:

```cascode
VERSION 3.0
```

Version header requirements:
- **Linked files** (`.hl.cai`, `.ml.cai`, `.el.cai`) **MUST** include a VERSION header
- **Source files** (`.cas`) **MAY** include a VERSION header (strongly encouraged)
- The canonical version source is `tools/language/CascodeVersion.cs`
- Version format is `MAJOR.MINOR` where major version changes indicate breaking changes

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
- `cascode emit`: Change input from `.cir` to `.el.cai`
- `cascode verify`: Change input from `.cir` to `.el.cai`
- `cascode bench`: Change input from `.cir` to `.el.cai`
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

`harness` is in scope inside bench bindings. The `harness` scope provides flattened key-value access to resolved environment values, mirroring the fields available in `env`: `harness.SupplyVoltage`, `harness.LoadImpedance`, `harness.SourceImpedance`, `harness.Temperature`, etc.

**A6. Environment Scope**

`env` is in scope throughout the bench. The bound circuit's environment block is accessible as `env.<ParamName>`.

**A7. Explicit Typing**

All variables must be explicitly typed at declaration. Types are semantic categories: `Frequency`, `VoltageRatio`, `TransferFunction`, `GainSpectrum`, `VoltageWaveform`, etc.

```cascode
Frequency f = 1MHz
VoltageRatio g = G.ValueAt(f)
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
stim CLK : clock     // Clock stimulus for mixed-signal benches
stim CTRL : digital  // Digital control input
```

Terminal types follow the standard Cascode terminal type system. Any terminal type (domain or bundle) is valid for terminal declarations, including user-defined bundles. Role restrictions apply based on the terminal's underlying domain:

| Role | Valid Domains | Notes |
|------|---------------|-------|
| `stim` | analog, digital, bias, mixed, clock, rf, supply | Stimulus inputs; supply valid for PSRR-style benches |
| `resp` | analog, digital, bias, mixed, clock, rf | Response outputs; supply/ground not valid |

Bundle types (including user-defined bundles like `Quad { A: analog; B: analog; C: analog; D: analog; }`) are valid for both roles. When a bundle is used, the role restrictions apply to each constituent field based on its domain.

Examples with bundle types:

```cascode
stim INPUT : Quad    // User-defined 4-terminal bundle as stimulus
resp OUTPUTS : Diff  // Standard differential bundle as response
```

The `ground` type is not valid for either role as grounds are handled implicitly through the test harness.

### 4.3 Fill Block

The `fill {}` block constructs the test circuit using standard Cascode circuit-building operations. Users can instantiate test instruments, probes, impedances, and even complete circuits.

#### 4.3.1 Fill Block Elements

The following circuit elements are language builtins available in fill blocks. All elements require named parameters for clarity and self-documentation.

| Element | Parameters | Terminals | Description |
|---------|------------|-----------|-------------|
| `VDC(V=Voltage)` | V: DC voltage value | `.P`, `.N` | DC voltage source |
| `VAC(A=Voltage, phase=Phase)` | A: AC amplitude, phase: angle (default 0deg) | `.P`, `.N` | AC voltage source for small-signal analysis |
| `IDC(I=Current)` | I: DC current value | `.P`, `.N` | DC current source |
| `IAC(A=Current, phase=Phase)` | A: AC amplitude, phase: angle (default 0deg) | `.P`, `.N` | AC current source for small-signal analysis |
| `GND()` | None | `.GND` | Ground reference node |
| `Impedor(Z=Impedance)` | Z: impedance value or expression | `.P`, `.N` | Frequency-dependent impedance element |
| `Resistor(R=Resistance)` | R: resistance value | `.P`, `.N` | Pure resistance element |
| `Capacitor(C=Capacitance)` | C: capacitance value | `.P`, `.N` | Pure capacitance element |
| `Inductor(L=Inductance)` | L: inductance value | `.P`, `.N` | Pure inductance element |
| `VProbe()` | None | `.P`, `.N` | Voltage probe (measures V(P) - V(N)) |
| `IProbe()` | None | `.P`, `.N` | Current probe (zero-impedance ammeter) |

Note the distinction between circuit elements and types: `Impedor` is a circuit element that accepts an `Impedance` type as its constructor parameter, just as `Capacitor` accepts `Capacitance` and `Resistor` accepts `Resistance`. See Section 12.7 for the complete element-type mapping.

Examples:
```cascode
VDC bias = new VDC(V=0.9V) { .P--node; .N--gnd }
VAC stim = new VAC(A=0.5V, phase=180deg) { .P--inp; .N--vcm }
GND _ = new GND() { .GND--gnd }
Impedor load = new Impedor(Z=1MOhm || 1pF) { .P--out; .N--gnd }
```

```cascode
fill {
  net vcm : analog
  net gnd : ground

  GND _ = new GND() {
    .GND--gnd
  }

  VDC commonModeVDC = new VDC(V=env.InputCommonModeRange) {
    .P--vcm
    .N--gnd
  }

  VAC acP = new VAC(A=0.5V, phase=0deg) {
    .N--vcm
  }

  Impedor sourceP = new Impedor(Z=env.SourceImpedance / 2) { }

  acP.P--sourceP.P, sourceP.N--IN.P
}
```

Key features:
- Component values can reference `env` for compile-time resolution
- Component values can reference `constraints` for constraint-aware configuration
- Anonymous instantiation using `_` as the instance name
- Standard net declarations and connectivity syntax

### 4.4 Helper Functions

Helper functions can be declared at two scopes:

**File-level functions** are declared outside bench blocks and can be shared across multiple benches in the same file:

```cascode
library lib.std.bench

// File-level function - shared across all benches in this file
function calc_passband_freq(ACAnalysis ac, Frequency hp, Frequency lp) : Frequency {
  Frequency f = sqrt(hp * lp)
  if f < ac.start { return ac.start }
  if f > ac.stop { return ac.stop }
  return f
}

bench DiffToSETransfer {
  // Can call calc_passband_freq here
}

bench DiffToDiffTransfer {
  // Can also call calc_passband_freq here
}
```

**Bench-local functions** are declared inside a bench block and are scoped to that bench only:

```cascode
bench DiffToSETransfer {
  // Bench-local function - only visible within this bench
  function infer_hp_corner(Frequency fallback) : Frequency {
    if constraints.HighpassBandwidth {
      return constraints.HighpassBandwidth
    }
    return fallback
  }
}
```

Functions have access to:
- `constraints` - the bound circuit's constraint values
- `env` - the bound circuit's environment values
- Analysis instances must be passed as parameters to file-level functions (they don't have implicit access to bench-scoped analysis instances)

### 4.5 Analysis Block

The `analysis {}` block declares typed analysis instances:

```cascode
analysis {
  ACAnalysis ac = new ACAnalysis(
    space=Log,
    samples=100,
    start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
    stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }))

  NoiseAnalysis noise_ac = new NoiseAnalysis(
    space=Log,
    samples=100,
    start=1Hz,
    stop=10GHz,
    output=OUT)
}
```

Analysis parameters can include conditional expressions that reference constraint values for adaptive analysis configuration.

Available analysis types:

| Analysis Type | Required Parameters | Optional Parameters | Description |
|---------------|---------------------|---------------------|-------------|
| `ACAnalysis` | start, stop | space, samples | Small-signal AC frequency sweep |
| `DCAnalysis` | sweep_var, start, stop | step | DC operating point sweep |
| `TranAnalysis` | stop | start, step | Transient time-domain simulation |
| `NoiseAnalysis` | output, start, stop | space, samples | Noise spectral density analysis |
| `STBAnalysis` | probe_node | - | Stability (loop gain) analysis |

The `NoiseAnalysis` type requires an `output` parameter specifying the node at which output noise is measured.

The `STBAnalysis` type performs stability analysis by measuring loop gain and phase margin at the specified `probe_node`. This uses the STB (stability) analysis available in simulators like Spectre, which breaks the feedback loop at the probe node to measure the open-loop transfer function. Frequency sweep parameters (`start`, `stop`, `space`, `samples`) follow the same semantics as `ACAnalysis`.

### 4.6 Measurements Block

The `measurements {}` block defines typed measurement expressions:

```cascode
measurements {
  measurement PassbandGain : dB {
    TransferFunction H = transfer(ac, IN, OUT)
    GainSpectrum G = db20(H.Mag())

    Frequency hp = infer_hp_corner(1Hz)
    Frequency lp = infer_lp_corner()
    Frequency fpb = calc_passband_freq(ac, hp, lp)

    return G.ValueAt(fpb)
  }

  measurement GainBandwidth : Hz {
    TransferFunction H = transfer(ac, IN, OUT)
    GainSpectrum G = db20(H.Mag())

    return G.FindCrossing(0dB, dir=falling, cross=1, from=ac.start, to=ac.stop)
  }
}
```

Measurement bodies have access to:
- Declared analyses by name
- Helper functions
- `constraints` and `env` scopes
- Measurement primitives (`transfer`, `eval`, `find_crossing`, etc.)

### 4.7 Cross-Measurement References

Measurements can reference other measurements in the same bench using explicit call syntax:

```cascode
measurement BandpassBandwidth : Hz {
  return abs(LowpassBandwidth() - HighpassBandwidth())
}
```

Cross-measurement references always use function call syntax (with parentheses) to distinguish them from variable references and to make the dependency explicit.

### 4.8 Parameterized Measurements

Measurements can accept parameters to enable flexible, reusable measurement definitions:

**Declaration syntax:**
```cascode
measurement IntegratedInputNoise(Frequency from, Frequency to) : nVrms {
  NoiseSpectrum n_in = input_referred_noise(noise_ac, ac, IN, OUT)
  return n_in.Integrate(from, to)
}
```

**Key semantics:**
- Parameter names and types are required in declarations
- Supported parameter types: Physical quantity types (`Frequency`, `Voltage`, etc.), `Scalar`, `Boolean`
- Default values are supported: `measurement Foo(Frequency f = 1kHz) : dB { ... }`
- Memoization: Same arguments return cached result; different arguments trigger new evaluation

**Invocation syntax (explicit call syntax always):**
- Non-parameterized: `LowpassBandwidth()` (parentheses required)
- Parameterized: `IntegratedInputNoise(from=1Hz, to=10MHz)` (named arguments)

**In constraints:**
```cascode
constraints {
  bench {
    c_noise = noise_bench::IntegratedInputNoise(from=10Hz, to=10MHz) <= 100nVrms
  }
}
```

**Cross-measurement references:**
Measurements can call other measurements, constructing a dependency tree. The runtime ensures no duplicate simulations—results are shared via dependency resolution.

```cascode
measurement BandpassBandwidth : Hz {
  return abs(LowpassBandwidth() - HighpassBandwidth())
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
    bench {
      c_psrr_a = psrr_avdd::PSRR >= 70dB
      c_psrr_d = psrr_dvdd::PSRR >= 60dB
    }
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
    bench {
      c_psrr_a = psrr_avdd::PSRR >= 70dB
      c_psrr_d = psrr_dvdd::PSRR >= 60dB
    }
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

## 6. Library and Include System

### 6.1 Library Declarations

Every Cascode source file declares its namespace using the `library` keyword:

```cascode
library lib.std.bench
```

The library declaration:
- Declares which namespace this file's symbols belong to
- Must appear at file level, before any definitions (after optional VERSION header)
- Uses dot-separated namespace paths

### 6.2 Namespace Inheritance

Namespaces form a hierarchy. Files automatically inherit all symbols from ancestor namespaces without explicit includes:

- A file in `lib.std.bench` automatically sees symbols from `lib.std` and `lib`
- A file in `lib.std.amp` automatically sees symbols from `lib.std` and `lib`

This enables modular organization while minimizing boilerplate. For example, the `Diff` bundle defined in `lib.std` is automatically available to all files in `lib.std.bench` without explicit import.

### 6.3 Include Syntax

Explicit includes are required only for namespaces that are not ancestors of the current namespace:

```cascode
include lib.std.amp       // Include all files declaring library lib.std.amp
include lib.pdk.sky130    // Include PDK-specific definitions
```

### 6.4 Include Resolution

- `include lib.std.amp` includes all files with `library lib.std.amp` declaration
- Transitive: included files' dependencies are also included
- Include statements must appear at file level, after library declaration
- Circular includes are resolved by the linker (each file included once)

### 6.5 Standard Library Structure

```
lib/
└── std/
    ├── Bundles.cas              // Common bundles (Diff, Quad) - library lib.std
    ├── benches/
    │   ├── TransferBenches.cas  // Transfer function benches - library lib.std.bench
    │   └── NoiseBenches.cas     // Noise analysis benches - library lib.std.bench
    ├── amp/
    │   ├── SingleEndedOpAmp.cas // SE op-amp interface - library lib.std.amp
    │   └── FullyDifferentialOpAmp.cas
    └── prim/
        └── Devices.cas          // Built-in NMOS/PMOS primitives - library lib.std.prim
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
// Note: Analysis must be passed as parameter since file-level functions
// don't have implicit access to bench-scoped analysis instances
function calc_passband_freq(ACAnalysis ac, Frequency hp, Frequency lp) : Frequency {
  Frequency f = sqrt(hp * lp)

  if f < ac.start { return ac.start }
  if f > ac.stop  { return ac.stop  }
  return f
}
```

### 8.2 Differential-to-Single-Ended Transfer Bench

`lib/std/benches/TransferBenches.cas`:
```cascode
library lib.std.bench

bench DiffToSETransfer {
  stim IN : Diff
  resp OUT : analog

  fill {
    net vcm : analog
    net gnd : ground

    GND _ = new GND() {
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

    Impedor sourceP = new Impedor(Z=env.SourceImpedance / 2) { }
    Impedor sourceN = new Impedor(Z=env.SourceImpedance / 2) { }

    acP.P--sourceP.P, sourceP.N--IN.P
    acN.P--sourceN.P, sourceN.N--IN.N

    Impedor load = new Impedor(Z=env.LoadImpedance) {
      .P--OUT
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
      GainSpectrum G = db20(H.Mag())

      Frequency hp = infer_hp_corner(1Hz)
      Frequency lp = infer_lp_corner()
      Frequency fpb = calc_passband_freq(ac, hp, lp)

      return G.ValueAt(fpb)
    }

    measurement GainBandwidth : Hz {
      TransferFunction H = transfer(ac, IN, OUT)
      GainSpectrum G = db20(H.Mag())

      return G.FindCrossing(0dB, dir=falling, cross=1, from=ac.start, to=ac.stop)
    }

    measurement LowpassBandwidth : Hz {
      TransferFunction H = transfer(ac, IN, OUT)
      GainSpectrum G = db20(H.Mag())

      Frequency hp = infer_hp_corner(1Hz)
      Frequency lp = infer_lp_corner()
      Frequency fpb = calc_passband_freq(ac, hp, lp)

      VoltageRatio gpb = G.ValueAt(fpb)
      VoltageRatio thr = gpb - 3dB

      return G.FindCrossing(thr, dir=falling, cross=1, from=fpb, to=ac.stop)
    }

    measurement HighpassBandwidth : Hz {
      TransferFunction H = transfer(ac, IN, OUT)
      GainSpectrum G = db20(H.Mag())

      Frequency hp = infer_hp_corner(1Hz)
      Frequency lp = infer_lp_corner()
      Frequency fpb = calc_passband_freq(ac, hp, lp)

      VoltageRatio gpb = G.ValueAt(fpb)
      VoltageRatio thr = gpb - 3dB

      return G.FindCrossing(thr, dir=rising, cross=1, from=ac.start, to=fpb)
    }

    measurement BandpassBandwidth : Hz {
      return abs(LowpassBandwidth() - HighpassBandwidth())
    }
  }
}
```

### 8.3 Interface with Bench Binding

`lib/std/amp/SingleEndedOpAmp.cas`:
```cascode
library lib.std.amp

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
      VDC dcSource = new VDC(V=harness.VDD) {
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
library my.designs

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
    bench {
      c_gbw = transfer_bench::GainBandwidth >= 100MHz
      c_gain = transfer_bench::PassbandGain >= 50dB
    }
  }
}
```

### 8.5 Input-Referred Noise Bench

`lib/std/benches/NoiseBenches.cas`:
```cascode
library lib.std.bench

bench DiffToSENoise {
  stim IN : Diff
  resp OUT : analog

  fill {
    net vcm : analog
    net gnd : ground

    GND _ = new GND() {
      .GND--gnd
    }

    VDC commonModeVDC = new VDC(V=env.InputCommonModeRange) {
      .P--vcm
      .N--gnd
    }

    // Bias inputs at common mode for noise analysis (no AC stimulus)
    IN.P--vcm
    IN.N--vcm

    Impedor load = new Impedor(Z=env.LoadImpedance) {
      .P--OUT
      .N--gnd
    }
  }

  analysis {
    // ACAnalysis required for input_referred_noise to compute transfer function
    ACAnalysis ac = new ACAnalysis(
      space=Log,
      samples=100,
      start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
      stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }))

    NoiseAnalysis noise_ac = new NoiseAnalysis(
      space=Log,
      samples=100,
      start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
      stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }),
      output=OUT)
  }

  measurements {
    measurement InputReferredNoise : nV/rtHz {
      // input_referred_noise uses paired ACAnalysis to compute transfer function
      NoiseSpectrum n_in = input_referred_noise(noise_ac, ac, IN, OUT)
      Frequency f_spot = (if constraints.SpotNoiseFrequency { constraints.SpotNoiseFrequency } else { 1kHz })
      return n_in.ValueAt(f_spot)
    }

    // Parameterized measurement - integration bounds specified at invocation
    measurement IntegratedInputNoise(Frequency from, Frequency to) : nVrms {
      NoiseSpectrum n_in = input_referred_noise(noise_ac, ac, IN, OUT)
      return n_in.Integrate(from, to)
    }

    measurement OutputNoise : nV/rtHz {
      NoiseSpectrum n_out = noise(noise_ac, OUT)
      Frequency f_spot = (if constraints.SpotNoiseFrequency { constraints.SpotNoiseFrequency } else { 1kHz })
      return n_out.ValueAt(f_spot)
    }
  }
}
```

This bench demonstrates the noise measurement primitives. The `input_referred_noise` function divides the output noise spectral density by the transfer function magnitude to compute the equivalent input noise. The `Integrate` method computes RMS noise over a specified bandwidth, useful for total integrated noise specifications.

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
| Linked artifact extension | `.cai` | Distinguishes generated, self-contained artifacts from source `.cas` without EDA ambiguity (e.g., `.cir`) |

### 9.2 Removed Features

The `builtin` keyword and all builtin bench references are removed. All benches are now defined in Cascode source files using the declarative bench construct.

---

## 10. Implementation Plan

Note: `cascode syn` (the synthesis agent) is out of scope for this RFC. Only the interface contract is defined; implementation is separate.

### 10.0 Implementation Status

The following phases have been completed or are in progress:

| Phase | Status | Notes |
|-------|--------|-------|
| Phase 0: Language Unification | **Complete** | Grammar renamed, tests migrated to `.cas` |
| Phase 1.1-1.3: Include/Link System | Partial | Grammar added, link command not yet implemented |
| Phase 1.4: Bench Definition Grammar | **Complete** | `bench`, `stim`/`resp`, `fill`, `analysis`, `measurements` |
| Phase 1.5: Helper Function Grammar | **Complete** | File-level and bench-local functions |
| Phase 1.6: Bench Binding Grammar | **Complete** | `benches {}`, `bind ... as ...` |
| Phase 2: Semantic Type System | In Progress | Types defined, validation partial |
| Phase 3: Bench Binding/Scoping | Not Started | |
| Phase 4: Runtime Execution | Not Started | |
| Phase 5: Migration/Documentation | In Progress | Standard library created |

### 10.1 Phase 0: Language Unification ✓

**PR 0.1: Delete Redundant Compiler** ✓
- Delete `tools/compiler/SimpleCascodeCompiler.cs`
- Delete `tools/parser/Cascode.g4`
- Update any references

**PR 0.2: Rename ACIR to Cascode** ✓
- Rename `tools/acir/ACIR.g4` → `tools/language/Cascode.g4`
- Rename all `ACIR*` classes to `Cascode*`
- Update namespace from `Cascode.ACIR` to `Cascode.Language`
- Rename diagnostic codes `ACIR0xxx` → `CAS0xxx`
- Update imports and references throughout

**PR 0.3: Migrate Golden Tests and CLI** ✓
- Rename `tests/golden/acir/**/*.cir` → `tests/golden/**/*.cas`
- Update test infrastructure to use `.cas` extension
- Update `cascode emit`, `cascode verify`, `cascode bench` to accept `.cas` instead of `.cir`
- Verify all tests pass

### 10.2 Phase 1: Grammar Extensions (~300 LOC)

**PR 1.1: Include Directive and Link Command** (Partial)
- ✓ Add `include` directive grammar
- ✓ Add `library` declaration grammar
- ☐ Implement `cascode link` command
- ☐ Implement include resolution (recursive)
- ☐ Implement namespace inheritance semantics
- ☐ Implement linked file output to `build/` directory

**PR 1.2: Synth Block and Sidecar Extraction**
- ✓ Add `synth {}` block grammar
- ☐ Implement extraction to `.synth.yaml` sidecar during link
- ☐ Implement `--guidance` flag for `cascode syn` interface

**PR 1.3: Sugar Constructs** ✓
- ✓ Add `pair` construct grammar
- ✓ Add `repeat idx in [start:end]` grammar
- ✓ Add `match` / `case` grammar
- ✓ Add `wrap spice` grammar

**PR 1.4: Bench Definition Grammar** ✓
- ✓ Add `bench` keyword and block structure
- ✓ Add `stim`/`resp` terminal declarations
- ✓ Add `fill {}` block parsing
- ✓ Add `analysis {}` block parsing
- ✓ Add `measurements {}` block parsing

**PR 1.5: Helper Function Grammar** ✓
- ✓ Add function declaration syntax with typed parameters
- ✓ Add function body parsing with control flow
- ✓ Add return type declarations

**PR 1.6: Bench Binding Grammar** ✓
- ✓ Add `benches {}` block in interfaces and circuits
- ✓ Add `bind ... as ...` syntax
- ✓ Add bench-to-DUT connection syntax

### 10.3 Phase 2: Semantic Type System (~400 LOC)

**PR 2.1: Physical Quantity Types**
- Implement scalar types: `Frequency`, `VoltageRatio`, `Voltage`, `Current`, `Time`, `Phase`, `Scalar`
- Implement compound types: `TransferFunction`, `GainSpectrum`, `PhaseSpectrum`, `VoltageSpectrum`, `CurrentSpectrum`, `NoiseSpectrum`, `VoltageWaveform`, `CurrentWaveform`
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

Measurement primitives are divided into two categories: **constructor functions** that create typed data structures from simulation results, and **type methods** that operate on those structures. This separation enables IDE autocomplete on types while keeping construction and conversion as standalone functions.

### 11.1 Constructor Functions

Constructor functions extract typed data from simulation analyses.

| Function | Signature | Semantics |
|----------|-----------|-----------|
| `transfer(analysis, stim, resp)` | `(ACAnalysis, Terminal, Terminal) -> TransferFunction` | Complex voltage transfer function Vout/Vin(f) |
| `voltage(analysis, terminal)` | `(ACAnalysis, Terminal) -> VoltageSpectrum` | Complex voltage spectrum V(f) |
| `voltage(analysis, terminal)` | `(TranAnalysis, Terminal) -> VoltageWaveform` | Voltage waveform V(t) |
| `current(analysis, element)` | `(ACAnalysis, Element) -> CurrentSpectrum` | Complex current spectrum I(f) |
| `current(analysis, element)` | `(TranAnalysis, Element) -> CurrentWaveform` | Current waveform I(t) |
| `noise(analysis, terminal)` | `(NoiseAnalysis, Terminal) -> NoiseSpectrum` | Noise spectral density V/√Hz(f) |
| `input_referred_noise(noise, ac, stim, resp)` | `(NoiseAnalysis, ACAnalysis, Terminal, Terminal) -> NoiseSpectrum` | Input-referred noise from output noise and transfer function |

The `voltage()` and `current()` functions are overloaded by analysis type: AC analysis returns a frequency-domain spectrum, while transient analysis returns a time-domain waveform.

### 11.2 Conversion Functions

Conversion functions transform values between unit systems without changing the underlying data structure.

| Function | Signature | Semantics |
|----------|-----------|-----------|
| `db20(x)` | `GainSpectrum -> GainSpectrum` | 20·log₁₀ conversion (voltage ratio) |
| `db20(x)` | `VoltageRatio -> VoltageRatio` | 20·log₁₀ conversion (scalar) |
| `db10(x)` | `GainSpectrum -> GainSpectrum` | 10·log₁₀ conversion (power ratio) |
| `db10(x)` | `PowerRatio -> PowerRatio` | 10·log₁₀ conversion (scalar) |

These functions act as type casts between linear and logarithmic representations. They apply pointwise when given spectrum arguments.

### 11.3 TransferFunction Methods

Methods on `TransferFunction` extract magnitude and phase components.

| Method | Signature | Semantics |
|--------|-----------|-----------|
| `tf.Mag()` | `TransferFunction -> GainSpectrum` | Linear magnitude \|H(f)\| |
| `tf.Phase()` | `TransferFunction -> PhaseSpectrum` | Phase angle arg(H(f)) in degrees |

### 11.4 Spectrum Methods

Methods available on frequency-domain spectrum types (`GainSpectrum`, `PhaseSpectrum`, `VoltageSpectrum`, `CurrentSpectrum`).

| Method | Signature | Semantics |
|--------|-----------|-----------|
| `spectrum.ValueAt(freq)` | `(Spectrum, Frequency) -> Scalar` | Evaluate spectrum at frequency |
| `spectrum.Max()` | `Spectrum -> Scalar` | Maximum value in spectrum |
| `spectrum.Min()` | `Spectrum -> Scalar` | Minimum value in spectrum |
| `spectrum.FindCrossing(threshold, dir, cross, from?, to?)` | `(...) -> Frequency` | Find frequency where spectrum crosses threshold |

The `FindCrossing` method locates where the spectrum crosses a threshold value. Parameters:
- `threshold`: The value to cross
- `dir`: Direction (`rising` or `falling`)
- `cross`: Which crossing (`first` or `last`)
- `from`, `to`: Optional frequency range to search (defaults to full analysis range)

### 11.5 Waveform Methods

Methods available on time-domain waveform types (`VoltageWaveform`, `CurrentWaveform`).

| Method | Signature | Semantics |
|--------|-----------|-----------|
| `waveform.ValueAt(time)` | `(Waveform, Time) -> Scalar` | Evaluate waveform at time point |
| `waveform.Max()` | `Waveform -> Scalar` | Maximum value in waveform |
| `waveform.Min()` | `Waveform -> Scalar` | Minimum value in waveform |
| `waveform.FindCrossing(threshold, dir, cross, from?, to?)` | `(...) -> Time` | Find time where waveform crosses threshold |

These methods mirror the spectrum methods but operate over time instead of frequency.

### 11.6 NoiseSpectrum Methods

Methods specific to noise spectral density data.

| Method | Signature | Semantics |
|--------|-----------|-----------|
| `noise.ValueAt(freq)` | `(NoiseSpectrum, Frequency) -> NoiseSpectralDensity` | Spot noise at frequency |
| `noise.Integrate(f_lo, f_hi)` | `(NoiseSpectrum, Frequency, Frequency) -> IntegratedNoise` | RMS noise over bandwidth |

The `Integrate` method computes the RMS noise by integrating the power spectral density over the specified frequency range and taking the square root.

### 11.7 Terminal Voltage Computation

Measurement primitives compute terminal voltages based on the structure of the terminal bundle:

| Terminal Type | Leaf Nodes | Voltage Computed |
|---------------|------------|------------------|
| Single-ended (e.g., `analog`) | 1 | `V(node)` relative to ground |
| Differential (e.g., `Diff`) | 2 | `V(P) - V(N)` differential |

This behavior applies to the terminal arguments of `transfer()`, `voltage()`, `noise()`, and `input_referred_noise()`. A single-ended terminal measures the voltage at that node relative to the simulator's ground reference. A differential terminal (with two leaf nodes) computes the voltage difference between the positive and negative nodes.

This allows measurement primitives to work uniformly across single-ended and differential topologies. For example, `transfer(ac, IN, OUT)` correctly computes:
- Single-ended to single-ended: `V(OUT) / V(IN)`
- Differential to single-ended: `V(OUT) / (V(IN.P) - V(IN.N))`
- Differential to differential: `(V(OUT.P) - V(OUT.N)) / (V(IN.P) - V(IN.N))`

### 11.8 Arithmetic

Standard operators (`+`, `-`, `*`, `/`) and functions (`abs`, `sqrt`) are available for scalar values with appropriate unit propagation

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
| `Impedance` | Ohm | kOhm, MOhm | Complex impedance; supports `\|\|` for parallel combinations |
| `Resistance` | Ohm | kOhm, MOhm | Pure resistance (real component of impedance) |
| `Resistance` | Ohm | kOhm, MOhm | Pure resistance (real component of impedance) |
| `Capacitance` | F | pF, fF, nF, uF | Capacitance values |
| `Inductance` | H | nH, uH, mH | Inductance values |
| `Voltage` | V | mV, uV, nV | Voltage values |
| `Current` | A | mA, uA, nA, pA | Current values |
| `Time` | s | ms, us, ns, ps | Time values |
| `Phase` | deg | rad | Phase angle |
| `Scalar` | (unitless) | - | Dimensionless quantities |
| `NoiseSpectralDensity` | V/rtHz | nV/rtHz, uV/rtHz, pV/rtHz, A/rtHz, pA/rtHz, nA/rtHz | Noise spectral density (voltage or current) |
| `IntegratedNoise` | Vrms | nVrms, uVrms, mVrms, Arms, pArms, nArms | RMS noise integrated over bandwidth |

The `rtHz` suffix represents "root Hz" (√Hz) for noise spectral density units. This avoids requiring special characters in source files while remaining unambiguous.

### 12.2 Compound Types

The measurement system provides compound types representing simulation data over frequency or time domains. These types support method syntax for intrinsic operations.

#### Frequency Domain Types

| Type | Domain | Value Type | Description |
|------|--------|------------|-------------|
| `TransferFunction` | Frequency | Complex | Complex voltage ratio Vout/Vin(f) |
| `GainSpectrum` | Frequency | VoltageRatio | Magnitude \|Vout/Vin\|(f) from `tf.Mag()` |
| `PhaseSpectrum` | Frequency | Phase | Phase angle arg(Vout/Vin)(f) from `tf.Phase()` |
| `VoltageSpectrum` | Frequency | Complex Voltage | Voltage V(f) from `voltage(ac, terminal)` |
| `CurrentSpectrum` | Frequency | Complex Current | Current I(f) from `current(ac, element)` |
| `NoiseSpectrum` | Frequency | NoiseSpectralDensity | Noise density V/√Hz(f) from `noise(...)` |



#### Time Domain Types

| Type | Domain | Value Type | Description |
|------|--------|------------|-------------|
| `VoltageWaveform` | Time | Voltage | Voltage V(t) from `voltage(tran, terminal)` |
| `CurrentWaveform` | Time | Current | Current I(t) from `current(tran, element)` |

The compound types replace the earlier `RealFunction` and `NoiseFunction` types with more specific names that indicate both the physical quantity and the domain. This improves code clarity and enables better IDE autocomplete support.

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

### 12.5 Function and Method Signatures

The measurement system distinguishes between **functions** (for construction and conversion) and **methods** (for type-intrinsic operations). This separation enables IDE autocomplete on compound types while keeping transformations like `db20()` as standalone functions.

#### 12.5.1 Constructor Functions

Constructor functions create compound types from simulation analyses:

```
transfer(ACAnalysis, Terminal, Terminal) -> TransferFunction
voltage(ACAnalysis, Terminal) -> VoltageSpectrum
voltage(TranAnalysis, Terminal) -> VoltageWaveform
current(ACAnalysis, Element) -> CurrentSpectrum
current(TranAnalysis, Element) -> CurrentWaveform
noise(NoiseAnalysis, Terminal) -> NoiseSpectrum
input_referred_noise(NoiseAnalysis, ACAnalysis, Terminal, Terminal) -> NoiseSpectrum
```

#### 12.5.2 Conversion Functions

Conversion functions transform between unit representations:

```
db20(GainSpectrum) -> GainSpectrum
db20(VoltageRatio) -> VoltageRatio
db10(GainSpectrum) -> GainSpectrum
db10(PowerRatio) -> PowerRatio
```

#### 12.5.3 Type Methods

Methods are invoked on compound type instances using dot notation.

TransferFunction methods:
```
TransferFunction.Mag() -> GainSpectrum
TransferFunction.Phase() -> PhaseSpectrum
```

Spectrum methods (GainSpectrum, PhaseSpectrum, VoltageSpectrum, CurrentSpectrum):
```
Spectrum.ValueAt(Frequency) -> Scalar
Spectrum.Max() -> Scalar
Spectrum.Min() -> Scalar
Spectrum.FindCrossing(threshold, direction, crossing, from?, to?) -> Frequency
```

Waveform methods (VoltageWaveform, CurrentWaveform):
```
Waveform.ValueAt(Time) -> Scalar
Waveform.Max() -> Scalar
Waveform.Min() -> Scalar
Waveform.FindCrossing(threshold, direction, crossing, from?, to?) -> Time
```

NoiseSpectrum methods:
```
NoiseSpectrum.ValueAt(Frequency) -> NoiseSpectralDensity
NoiseSpectrum.Integrate(Frequency, Frequency) -> IntegratedNoise
```

#### 12.5.4 Mathematical Functions

Mathematical functions operate on scalar values:

```
abs(T) -> T where T is numeric
sqrt(Scalar) -> Scalar
sqrt(Frequency * Frequency) -> Frequency
```

### 12.6 Impedance Expressions

The `Impedance` type represents complex impedance (Z = R + jX) and supports the parallel combination operator `||` for expressing networks of resistive and reactive elements.

Syntax:

```
impedance_expr := element (|| element)*
element        := resistance | capacitance | inductance
resistance     := NUMBER (Ohm | Ohm | kOhm | kOhm | MOhm | MOhm)
capacitance    := NUMBER (F | pF | fF | nF | uF)
inductance     := NUMBER (H | nH | uH | mH)
```

The `||` operator computes frequency-dependent parallel impedance using standard circuit analysis: Z_parallel = 1 / (1/Z₁ + 1/Z₂ + ...). For a Capacitor, Z_C = 1/(jOhmC); for an Inductor, Z_L = jOhmL; for a Resistor, Z_R = R.

Examples:

```cascode
Impedance z1 = 50Ohm                    // Pure resistance
Impedance z2 = 1MOhm || 1pF             // RC parallel: high-impedance with parasitic cap
Impedance z3 = 100kOhm || 10pF || 1nH   // RLC parallel network
```

The parallel combination notation matches the harness `load` syntax used elsewhere in ACIR, ensuring consistency across environment parameters and harness specifications.

### 12.7 Circuit Elements

Circuit elements are distinct from types. An element is a circuit component that can be instantiated in a fill block; a type is a value category used in expressions and parameters. Elements accept types as constructor arguments.

| Element | Parameter Type | Description |
|---------|---------------|-------------|
| `VDC` | `Voltage` | DC voltage source |
| `VAC` | `Voltage`, `Phase` | AC voltage source |
| `IDC` | `Current` | DC current source |
| `IAC` | `Current`, `Phase` | AC current source |
| `GND` | (none) | Ground reference |
| `Impedor` | `Impedance` | Frequency-dependent impedance element |
| `Resistor` | `Resistance` | Pure resistance element |
| `Capacitor` | `Capacitance` | Pure capacitance element |
| `Inductor` | `Inductance` | Pure inductance element |
| `VProbe` | (none) | Voltage measurement probe |
| `IProbe` | (none) | Current measurement probe |

The element-type relationship parallels physical reality: a `Capacitor` is a physical component that has a `Capacitance` value, just as an `Impedor` is a component with an `Impedance` value. This distinction prevents confusion between instantiating components and working with values in expressions.

Element instantiation requires named parameters:

```cascode
VDC supply = new VDC(V=1.8V) { .P--vdd; .N--gnd }
VAC stimulus = new VAC(A=1V, phase=0deg) { .P--in; .N--gnd }
Impedor source = new Impedor(Z=50Ohm) { .P--sig; .N--in }
Resistor load = new Resistor(R=10kOhm) { .P--out; .N--gnd }
Capacitor decap = new Capacitor(C=100pF) { .P--vdd; .N--gnd }
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
| `SourceImpedance` | Impedance | Source impedance driving the input; accepts impedance expressions |
| `LoadImpedance` | Impedance | Load impedance at output; accepts impedance expressions (e.g., `1MOhm \|\| 1pF`) |
| `SupplyVoltage` | Voltage | Nominal supply voltage |
| `Temperature` | Temperature | Operating temperature |

### 14.2 Environment Block Syntax

```cascode
circuit MyAmplifier implements SingleEndedOpAmp {
  level EL

  env {
    InputCommonModeRange = 0.9V
    OutputCommonModeRange = 0.9V
    SourceImpedance = 50Ohm
    LoadImpedance = 1MOhm || 1pF    // Parallel combination
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
  VDC commonModeVDC = new VDC(V=env.InputCommonModeRange) {
    .P--vcm
    .N--gnd
  }

  Impedor sourceP = new Impedor(Z=env.SourceImpedance / 2)
}
```

Note that named parameters are required for all element instantiation.

---

## 15. Internal Node Access

Measurements can access internal nodes of the DUT using the `dut.` prefix. This enables characterization of internal circuit behavior.

### 15.1 Syntax

```cascode
measurements {
  measurement InternalBias : V {
    VoltageSpectrum v_dc = voltage(dc, dut.mirror_gate)  // Access internal node
    return v_dc.ValueAt(0Hz)
  }

  measurement InternalSwing : dB {
    TransferFunction H_internal = transfer(ac, IN, dut.stage1_out)
    GainSpectrum G = db20(H_internal.Mag())
    return G.ValueAt(1MHz)
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
| `NoiseBench` | `DiffToSENoise` | `Diff` → `analog` | Noise analysis; Example in RFC Section 8.5 |
| `TransientBench` | `StepResponse` | `Diff` → `analog` | Transient analysis |

### 16.2 Unification Opportunities

Many builtin benches differ only in terminal topology. The declarative system enables reuse by creating separate bench definitions that share helper functions and measurement logic patterns. For example, `DiffToSETransfer` and `DiffToDiffTransfer` can share the same helper functions for frequency inference while differing only in their terminal declarations and output handling.

### 16.3 Migration Notes

Per the Big Picture Goals, there is no automated migration tooling. Documents using `builtin` syntax will produce parse errors. Users must:
1. Replace `builtin` bench references with declarative bench definitions
2. Add explicit bench bindings in interface/circuit `benches {}` blocks
3. Update constraint references from `BenchName::Metric` to `binding_name::Metric`

---

## 17. Future Work

### 17.1 Transient Analysis Support

Extend the analysis and measurement system to support transient simulations with time-domain primitives.

### 17.2 Monte Carlo and Statistical Constraints

Support statistical constraint forms like `yield(Measurement >= value) >= percentage`.

### 17.3 Multi-Corner Sweep Integration

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
    : IDENT           // Any bundle or domain name (Diff, Quad, etc.)
    | BIAS_KW
    | SUPPLY_KW
    | GROUND_KW
    | ANALOG_KW
    | DIGITAL_KW
    | MIXED_KW
    | CLOCK_KW
    | RF_KW
    ;
```

Terminal types accept any valid domain or bundle name. Role restrictions are enforced semantically based on the terminal's underlying domain:
- `stim` (stimulus) accepts: analog, digital, bias, mixed, clock, rf, supply
- `resp` (response) accepts: analog, digital, bias, mixed, clock, rf
- `supply` and `ground` are not valid for `resp` terminals
- Bundle types inherit restrictions from their constituent field domains

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
    | GAIN_SPECTRUM_TYPE
    | PHASE_SPECTRUM_TYPE
    | VOLTAGE_SPECTRUM_TYPE
    | CURRENT_SPECTRUM_TYPE
    | NOISE_SPECTRUM_TYPE
    | VOLTAGE_WAVEFORM_TYPE
    | CURRENT_WAVEFORM_TYPE
    | NOISE_SPECTRAL_DENSITY_TYPE
    | INTEGRATED_NOISE_TYPE
    | IMPEDANCE_TYPE
    | CAPACITANCE_TYPE
    | INDUCTANCE_TYPE
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
    : MEASUREMENT_KW name=IDENT measurementParams? COLON unitType LBRACE measurementBody RBRACE
    ;

measurementParams
    : LPAREN measurementParamList RPAREN
    ;

measurementParamList
    : measurementParam (COMMA measurementParam)*
    ;

measurementParam
    : physicalType IDENT (EQ defaultValue)?
    ;

defaultValue
    : QUANTITY
    | NUMBER
    ;

unitType
    : IDENT              // Hz, dB, V, A, s, deg, etc.
    | NOISE_DENSITY_UNIT // nV/rtHz, pA/rtHz, etc.
    | INTEGRATED_RMS_UNIT // nVrms, uVrms, pArms, etc.
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
    : measurementAtom DOT methodCall      // Method call: expr.Method(args)
    | LPAREN measurementExpr RPAREN
    | functionCall
    | scopedAccess
    | dutAccess
    | IDENT
    | QUANTITY
    | NUMBER
    ;

// Method call on a compound type (e.g., tf.Mag(), spectrum.ValueAt(1kHz))
methodCall
    : IDENT LPAREN measurementArgList? RPAREN
    ;

// Function call - constructor or conversion function
functionCall
    : IDENT LPAREN measurementArgList? RPAREN
    ;

measurementArgList
    : measurementArg (COMMA measurementArg)*
    ;

measurementArg
    : IDENT EQ measurementExpr    // Named argument
    | measurementExpr             // Positional argument
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

pinRef
    : IDENT (DOT IDENT)*
    ;

argList
    : measurementExpr (COMMA measurementExpr)*
    ;
```

### A.7.1 Constraint Reference Syntax

Constraints reference bench measurements using the `binding_name::Metric` syntax:

```antlr
constraintDecl
    : IDENT EQ benchMetricRef comparisonOp QUANTITY
    ;

benchMetricRef
    : IDENT COLONCOLON IDENT
    ;

comparisonOp
    : GTE | LTE | GT | LT | EQEQ
    ;

COLONCOLON : '::' ;
GTE        : '>=' ;
LTE        : '<=' ;
GT         : '>' ;
LT         : '<' ;
EQEQ       : '==' ;
```

Example:
```cascode
constraints {
  c_gbw = transfer_bench::GainBandwidth >= 100MHz
  c_gain = transfer_bench::PassbandGain >= 50dB
}
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
    : impedanceExpr
    | QUANTITY
    | NUMBER IDENT?
    ;

// Impedance expressions support chained parallel combinations: R || C || L
impedanceExpr
    : impedanceElement (PIPEPIPE impedanceElement)+
    ;

impedanceElement
    : RESISTANCE_QUANTITY    // e.g., 50Ohm, 1MOhm, 100kOhm
    | CAPACITANCE_QUANTITY   // e.g., 1pF, 100fF, 10nF
    | INDUCTANCE_QUANTITY    // e.g., 1nH, 10uH, 1mH
    ;

RESISTANCE_QUANTITY   : NUMBER WS? ('Ohm' | 'Ohm' | 'kOhm' | 'kOhm' | 'MOhm' | 'MOhm') ;
CAPACITANCE_QUANTITY  : NUMBER WS? ('F' | 'pF' | 'fF' | 'nF' | 'uF' | 'uF') ;
INDUCTANCE_QUANTITY   : NUMBER WS? ('H' | 'nH' | 'uH' | 'uH' | 'mH') ;
```

### A.9 New Lexer Tokens

```antlr
// Operators
WIRE_OP         : '--' ;

// New keywords
STIM_KW         : 'stim' ;
RESP_KW         : 'resp' ;
DIFF_KW         : 'Diff' ;
ANALOG_KW       : 'analog' ;
DIGITAL_KW      : 'digital' ;
MIXED_KW        : 'mixed' ;
CLOCK_KW        : 'clock' ;
RF_KW           : 'rf' ;
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

// Type keywords - Physical quantities
FREQUENCY_TYPE              : 'Frequency' ;
VOLTAGE_RATIO_TYPE          : 'VoltageRatio' ;
NOISE_SPECTRAL_DENSITY_TYPE : 'NoiseSpectralDensity' ;
INTEGRATED_NOISE_TYPE       : 'IntegratedNoise' ;
IMPEDANCE_TYPE              : 'Impedance' ;
CAPACITANCE_TYPE            : 'Capacitance' ;
INDUCTANCE_TYPE             : 'Inductance' ;
VOLTAGE_TYPE                : 'Voltage' ;
CURRENT_TYPE                : 'Current' ;
TIME_TYPE                   : 'Time' ;
PHASE_TYPE                  : 'Phase' ;
SCALAR_TYPE                 : 'Scalar' ;

// Type keywords - Compound types (frequency domain)
TRANSFER_FUNCTION_TYPE      : 'TransferFunction' ;
GAIN_SPECTRUM_TYPE          : 'GainSpectrum' ;
PHASE_SPECTRUM_TYPE         : 'PhaseSpectrum' ;
VOLTAGE_SPECTRUM_TYPE       : 'VoltageSpectrum' ;
CURRENT_SPECTRUM_TYPE       : 'CurrentSpectrum' ;
NOISE_SPECTRUM_TYPE         : 'NoiseSpectrum' ;

// Type keywords - Compound types (time domain)
VOLTAGE_WAVEFORM_TYPE       : 'VoltageWaveform' ;
CURRENT_WAVEFORM_TYPE       : 'CurrentWaveform' ;

// Analysis type keywords
AC_ANALYSIS_TYPE    : 'ACAnalysis' ;
DC_ANALYSIS_TYPE    : 'DCAnalysis' ;
TRAN_ANALYSIS_TYPE  : 'TranAnalysis' ;
NOISE_ANALYSIS_TYPE : 'NoiseAnalysis' ;
STB_ANALYSIS_TYPE   : 'STBAnalysis' ;

// Noise unit patterns (rtHz = root Hz, √Hz)
// These are compound units parsed as single tokens
NOISE_DENSITY_UNIT  : ('V' | 'nV' | 'uV' | 'pV' | 'A' | 'nA' | 'pA' | 'uA') '/rtHz' ;
INTEGRATED_RMS_UNIT : ('V' | 'nV' | 'uV' | 'mV' | 'A' | 'nA' | 'pA' | 'uA') 'rms' ;
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

The `FOR_KW` token usage in `benchDef` is also removed (benches no longer specify `for interface`).

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

---

## Appendix C: Standard Library Structure

The standard library (`lib/std/`) provides common definitions for the Cascode language. This appendix documents its organization and the relationship between stdlib-provided primitives and PDK-provided device models.

### C.1 Directory Structure

```
lib/
└── std/
    ├── Bundles.cas              // Common bundles (Diff, Quad)
    │                            // library lib.std
    ├── benches/
    │   ├── TransferBenches.cas  // Transfer function benches (DiffToSETransfer, etc.)
    │   └── NoiseBenches.cas     // Noise analysis benches (DiffToSENoise)
    │                            // library lib.std.bench
    ├── amp/
    │   ├── SingleEndedOpAmp.cas // Single-ended op-amp interface
    │   └── FullyDifferentialOpAmp.cas
    │                            // library lib.std.amp
    └── prim/
        └── Devices.cas          // Built-in NMOS/PMOS primitive definitions
                                 // library lib.std.prim
```

### C.2 Namespace Hierarchy

Files in child namespaces automatically inherit symbols from parent namespaces:

- `lib.std.bench` sees all symbols from `lib.std` (including the `Diff` bundle)
- `lib.std.amp` sees all symbols from `lib.std`
- User designs that include `lib.std.amp` also get `lib.std` transitively

### C.3 Standard Library vs PDK

The standard library provides language-level primitives and interfaces. PDKs provide technology-specific device models.

| Provided By | Contents | Examples |
|-------------|----------|----------|
| **Standard Library** | Bundles, interfaces, benches, built-in primitives | `Diff`, `SingleEndedOpAmp`, `DiffToSETransfer`, `Level1_NMOS` |
| **PDK** | Device models, process-specific primitives | `sky130_fd_pr__nfet_01v8`, `gpdk045_nmos` |

The stdlib `Devices.cas` provides ideal `Level1_NMOS` and `Level1_PMOS` primitives for simulation without a PDK. Real designs should use PDK-provided primitives for accurate modeling.

### C.4 Common Bundles

The `lib/std/Bundles.cas` file defines commonly-used bundles:

```cascode
library lib.std

bundle Diff {
  P : analog
  N : analog
}
```

These bundles are automatically available to all files in `lib.std.*` namespaces due to namespace inheritance.
