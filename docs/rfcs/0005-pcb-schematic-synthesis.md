# RFC-0005: PCB Schematic Representation and Synthesis

| Field | Value |
|-------|-------|
| Status | Draft |
| Authors | Daniel Lovell |
| Created | 2026-02-06 |
| Last Updated | 2026-02-07 |

---

## Abstract

Cascode's abstractions for IC design (bundles, interfaces, circuits, primitives, benches, constraints, HL/EL levels) map naturally to PCB schematic capture and synthesis. This RFC extends the language with a `part` construct for packaged off-the-shelf components, a metrics system for datasheet-driven and simulation-driven validation, and bus bundles for digital interconnect. These additions enable Cascode to represent PCB schematics at both high-level (system architecture with constraint-driven part selection) and electrical level (concrete schematic with specific component values and connections).

A unifying insight drives the design: in both IC and PCB flows, the schematic symbol (pin contract and behavioral requirements) is separate from the concrete backing (a PDK device or a sourced component). This RFC formalizes that separation by making both `primitive` and the new `part` declare which interfaces they satisfy via `implements`. Reserved keyword categories (`NMOS`, `Resistor`, etc.) become library-defined interfaces in domain-specific libraries (`lib/ic/`, `lib/pcb/`), making the component taxonomy extensible without grammar changes.

---

## 1. Motivation

PCB schematic capture today uses GUI tools (KiCad, Altium, OrCAD) that, like analog IC schematics, obscure design intent. A sensor frontend board requires specific noise, bandwidth, and accuracy constraints that live in the designer's head and separate notes but are not usually captured alongside the schematic. When a component goes end-of-life, the designer must manually verify whether a replacement part still meets original constraints spread across documents and datasheets.

Cascode's constraint-driven, multi-level abstraction model addresses this gap. An HL design expresses system requirements (for example, the analog frontend must achieve 40 dB gain with less than 1 uVrms integrated noise). An EL design captures concrete schematic structure with explicit components and connections. Bench execution validates simulable blocks (for example, op-amp paths with SPICE models) through `bench` constraints, while datasheet-backed metrics validate non-simulable blocks (for example, ADC and MCU capability constraints) through `spec` constraints. Both share the same constraint language with distinct sub-blocks that make verification provenance explicit.

A secondary motivation is passive-network synthesis. PCB designs contain filters, bias dividers, gain-setting networks, and decoupling structures that involve topology and discrete value selection. These can use the same synthesis framework used for IC circuits, with additional constraints that values snap to standard series and resolvable parts exist.

---

## 2. Goals and Non-Goals

Goals:

1. Represent complete PCB schematics in Cascode at both HL and EL abstraction levels.
2. Introduce a `part` construct for packaged components, parallel to `primitive`, both using `implements` to satisfy interface contracts.
3. Unify the component taxonomy: replace reserved keyword categories (`NMOS`, `Resistor`, etc.) with library-defined interfaces in `lib/ic/` and `lib/pcb/`.
4. Enable constraint-driven part selection using guaranteed datasheet metrics (`spec` constraints) alongside existing bench-derived metrics (`bench` constraints), with verification provenance explicit in the syntax.
5. Support standard PCB signal buses (I2C, SPI, UART, SWD) as first-class bundle types.
6. Handle multi-unit ICs (dual op-amps, quad ADCs) through flat port naming with per-port directionality.
7. Introduce a `Some` keyword for synthesis-inferred slot types.
8. Provide a worked example (Wheatstone bridge sensor frontend) that stress-tests the proposed constructs.

Non-goals:

1. PCB layout or physical placement. This RFC addresses schematic capture and synthesis intent only.
2. Fully specifying output formats (KiCad, Altium, BOM generation). Target contracts remain future work.
3. Defining the complete external parts sync pipeline implementation. This RFC defines language-facing contracts and pointers.
4. Solving MCU alternate-function/pin-mux firmware configuration in-language.

---

## 3. Conceptual Mapping

The table below summarizes how existing IC Cascode concepts translate to PCB design under the unified `implements` model.

| IC Cascode | PCB Cascode | Notes |
|---|---|---|
| `primitive nfet(size s) implements NMOS` | `part OPA2376 implements DualOpAmp` | Both use `implements` to satisfy an interface contract |
| `lib/ic/` interfaces (`NMOS`, `Resistor`, ...) | `lib/pcb/` interfaces (`NMOS`, `DualOpAmp`, `ADC`, ...) | Domain-specific interface libraries; shared interfaces (`SingleEndedOpAmp`, bus bundles) live in `lib/std/` |
| PDK (`pdk scan`) | Parts library + external pricing/availability sources | Source of available components and their operating/procurement attributes |
| `size(W=2u, L=180n, M=1)` | `real` scalar params for passives; no value params for fixed-identity ICs | `size` remains reserved for transistor geometry on primitives |
| `device "sky130_fd_pr__nfet_01v8"` | `mpn "OPA2376AIDDBVR"` | Distinct identity fields by declaration kind (`device` for primitive, `mpn` for part) |
| `bench` with SPICE analysis | Bench-derived metrics plus datasheet-valued metrics | `bench` for simulation-verified, `spec` for declaration-verified; provenance explicit in syntax |
| `bundle Diff { P, N }` | `bundle I2C { SDA, SCL }` | Bus bundles shared in `lib/std/bus/`; domain-agnostic |
| `interface SingleEndedOpAmp` | `interface SensorConditioner` | Functional contracts remain interface-centric |
| `constraints { bench { ... } }` | `constraints { bench { ... } spec { ... } }` | `bench` for simulation-verified, `spec` for declaration-verified; `physical` replaces `tech` |
| HL `slot` + synthesis | Part selection for ICs; topology/value selection for passive networks | `Some` keyword for synthesis-inferred types in slots |

---

## 4. Interface Libraries and the `implements` Model

### 4.1 Unified Abstraction

In both IC and PCB design, the schematic symbol -- the pin contract and behavioral requirements of a component -- is separate from its concrete backing. An op-amp symbol declares differential inputs, a single-ended output, and supply rails. Whether the backing is a PDK device model or a sourced part with an MPN is an implementation detail.

Cascode formalizes this with a single mechanism: the `implements` keyword. Circuits already use it (`circuit OTA5T implements SingleEndedOpAmp`). This RFC extends `implements` to both `primitive` and the new `part` construct, making all three declaration kinds conform to the same interface system.

```
interface (schematic symbol / pin contract)
    ↑ implements
primitive (IC backing: PDK device)
part      (PCB backing: sourced MPN)
circuit   (composed implementation)
```

### 4.2 Domain-Specific Interface Libraries

Component interfaces live in domain-specific libraries. A Cascode source file declares its target domain through its includes.

`lib/ic/Interfaces.cas` defines IC-domain component interfaces with IC-standard terminal sets:

```cascode
library lib.ic

interface NMOS {
  input G : analog
  output D : analog
  io S : analog
  io B : analog
}

interface PMOS {
  input G : analog
  output D : analog
  io S : analog
  io B : analog
}

interface Resistor {
  io P : analog
  io N : analog
}

interface Capacitor {
  io P : analog
  io N : analog
}

// Inductor, Diode similarly.
```

Interfaces that are shared across IC and PCB domains live in `lib/std/`. The `SingleEndedOpAmp` interface already exists at `lib/std/amp/SingleEndedOpAmp.cas`, and bus bundles (`I2C`, `SPI`, `UART`, `SWD`) live under `lib/std/bus/`:

```cascode
library lib.std.bus

bundle I2C {
  SDA : digital
  SCL : clock
}

bundle SPI {
  MOSI : digital
  MISO : digital
  SCLK : clock
  CS   : digital
}

bundle UART {
  TX : digital
  RX : digital
}

bundle SWD {
  SWDIO : digital
  SWCLK : clock
}
```

`lib/pcb/Interfaces.cas` defines PCB-domain component interfaces with PCB-standard terminal sets and metric contracts. It imports shared interfaces as needed:

```cascode
library lib.pcb
include lib.std.amp

interface NMOS {
  input G : analog
  output D : analog
  io S : analog
}

interface Resistor {
  io P : analog
  io N : analog
  metrics {
    Tolerance : pct
    PowerRating : W
  }
}

interface DualOpAmp {
  input A_INP : analog
  input A_INN : analog
  output A_OUT : analog
  input B_INP : analog
  input B_INN : analog
  output B_OUT : analog
  supply VDD
  ground GND
  metrics {
    GBW : Hz
    InputOffsetVoltage : V
    CMRR : dB
  }
}

// ADC, MCU, Connector interfaces similarly.
```

`Resistor` appears in both IC and PCB libraries with domain-appropriate contracts. The IC version declares only terminals; the PCB version adds metric requirements for tolerance and power rating. These are separate interfaces in separate namespaces. `include lib.ic` or `include lib.pcb` brings the appropriate definitions into scope. Mixed IC+PCB projects can include both; the linker resolves by fully-qualified namespace.

Concrete part declarations live in a separate `lib/parts/` tree, organized by category (`lib.parts.opamp`, `lib.parts.adc`, `lib.parts.res`, etc.). Including `lib.pcb` brings interface definitions into scope without pulling in the entire parts catalog.

### 4.3 Primitive Syntax Change

The existing primitive syntax uses reserved keyword categories:

```cascode
primitive NMOS nfet_01v8(size s) { device "sky130_fd_pr__nfet_01v8" params { ... } }
```

Under the unified model, primitives use `implements` to reference a library-defined interface:

```cascode
primitive nfet_01v8(size s) implements NMOS { device "sky130_fd_pr__nfet_01v8" params { ... } }
```

The reserved `DEVICE_TYPE` token (`NMOS`, `PMOS`, `Resistor`, `Capacitor`, `Inductor`, `Diode`) is removed from the grammar. These names become ordinary identifiers resolved from the included interface library. Terminal sets are validated against the interface contract rather than hard-coded per keyword.

### 4.4 Multiple Interface Implementation

Parts and circuits may implement multiple interfaces, following the same model as C# multiple interface implementation:

```cascode
part ADS1115 implements ADCSubsystem, I2CDevice {
  mpn "ADS1115IDGSR"
  ...
}
```

The implementation must satisfy the port and metric contracts of all declared interfaces. Missing ports or required metrics from any interface are hard validation errors.

### 4.5 The `Some` Keyword

In `slot {}` blocks, the `Some` keyword declares an instance whose type will be inferred or resolved by synthesis. It is only valid in slot blocks; use in `fill` blocks is a grammar-level error.

```cascode
slot {
  Some frontend = new AnalogFrontend() { ... }
  Some adc = new ADCStage() { ... }
}
```

When an explicit interface type is known, it should be used instead:

```cascode
slot {
  SensorConditioner frontend = new WheatstoneAmplifier(...) { ... }
  ADCSubsystem adc = new ADCStage() { ... }
}
```

`Some` is enforced at the grammar level by using separate instance declaration rules for slot and fill blocks.

---

## 5. The `part` Construct

### 5.1 Syntax

A `part` declaration is a new top-level construct, parallel to `primitive`. It declares a packaged component that implements one or more interfaces and carries sourcing metadata.

```
part <Name> (<params>?) implements <Interface>(, <Interface>)* {
  mpn "<manufacturer_part_number_or_family_id>"
  package "<footprint>"
  spice "<model_ref>"            // optional: present when manufacturer provides a usable model

  pricing {
    option { provider = "<provider>" sku = "<sku>" priority = <int> url = "<optional-url>" }
    ...
  }

  <terminal declarations>          // input, output, io, supply, ground

  metrics {
    <Metric> = <value>
    ...
  }
}
```

The body contains:

- `mpn`: identity for part lookup and traceability in PCB contexts.
- `package`: physical footprint package.
- `spice`: optional model reference for simulable parts.
- `pricing`: checked-in external procurement pointers.
- terminal declarations: physical connectivity.
- `metrics {}`: guaranteed datasheet values and part attributes.

For pricing options, `provider`, `sku`, and `priority` are required. `url` is optional.

### 5.2 Examples

A dual op-amp with SPICE model:

```cascode
part OPA2376 implements DualOpAmp {
  mpn "OPA2376AIDDBVR"
  package "VSSOP-8"
  spice "OPA2376"

  pricing {
    option { provider = "DigiKey" sku = "296-28003-1-ND" priority = 10 }
    option { provider = "Mouser" sku = "595-OPA2376AIDDBVR" priority = 20 }
  }

  input A_INP : analog
  input A_INN : analog
  output A_OUT : analog
  input B_INP : analog
  input B_INN : analog
  output B_OUT : analog
  supply VDD
  ground GND

  metrics {
    GBW = 5.5MHz
    InputOffsetVoltage = 25uV
    CMRR = 90dB
    InputBiasCurrent = 10pA
    SlewRate = 2V/us
    SupplyCurrentMax = 950uA
  }
}
```

Multi-unit ICs like dual op-amps use flat port naming with per-port direction qualifiers. Each port carries its own `input`, `output`, or `io` direction, preserving signal flow information that a bundle grouping cannot express.

A 16-bit I2C ADC without SPICE model:

```cascode
part ADS1115 implements ADCSubsystem {
  mpn "ADS1115IDGSR"
  package "MSOP-10"

  pricing {
    option { provider = "DigiKey" sku = "296-38714-1-ND" priority = 10 }
    option { provider = "Mouser" sku = "595-ADS1115IDGSR" priority = 20 }
  }

  input AIN0 : analog
  input AIN1 : analog
  input AIN2 : analog
  input AIN3 : analog
  input ADDR : digital
  io SDA : digital
  io SCL : clock
  output ALERT : digital
  supply VDD
  ground GND

  metrics {
    Resolution = 16 bits
    MaxSampleRate = 860 SPS
    INL = 0.5 LSB
    FullScaleRange = 6.144V
    SupplyVoltageMin = 2.0V
    SupplyVoltageMax = 5.5V
    SupplyCurrentMax = 200uA
    InputCapacitance = 14pF
  }
}
```

A parameterized passive family:

```cascode
part RC0402FR(real R) implements Resistor {
  mpn "RC0402FR-07"
  package "0402"

  pricing {
    option { provider = "DigiKey" sku = "311-{R}LRCT-ND" priority = 10 }
    option { provider = "Mouser" sku = "603-RC0402FR-07{R}L" priority = 20 }
  }

  io P : analog
  io N : analog

  params {
    R = R
  }

  metrics {
    Tolerance = 1pct
    PowerRating = 63mW
    VoltageRating = 50V
  }
}
```

### 5.3 Instantiation

Parts are instantiated in `fill` blocks using unified instantiation syntax. The declared type on the left is the interface; the `new` target on the right is the concrete part or circuit.

```cascode
fill {
  // parameterized passive -- Resistor is an interface from lib.pcb
  Resistor r1 = new RC0402FR(R=10k) {
    .P--IN
    .N--OUT
  }

  // fixed-identity IC part -- DualOpAmp is an interface from lib.pcb
  DualOpAmp u1 = new OPA2376() {
    .A_INP--sensor_p
    .A_INN--ref
    .A_OUT--stage1_out
    .VDD--VDD
    .GND--GND
  }
}
```

IC primitive instantiation follows the same pattern -- the declared type is the interface, not a reserved keyword:

```cascode
fill {
  NMOS m1 = new nfet_01v8(size(W=2u, L=180n, M=1, NF=1)) { ... }
  PMOS m2 = new pfet_01v8(size(W=2u, L=180n, M=1, NF=1)) { ... }
}
```

### 5.4 Relationship to `primitive`

`primitive` and `part` are siblings, not parent-child. Both use `implements` to satisfy interface contracts. The difference is in backing:

- `primitive` declarations carry a `device` directive referencing a simulator model from a foundry PDK.
- `part` declarations carry an `mpn` field referencing a sourced component from a parts ecosystem.

A Cascode project may contain both when modeling mixed IC + PCB systems.

### 5.5 Resolution Policy

All instantiation targets resolve semantically against declarations in scope (`circuit`, `interface`, `part`, `primitive`). There are no reserved keyword categories. The `include` directives determine which interfaces are available. Ambiguous or unresolved targets are hard validation errors.

---

## 6. The Metrics System

### 6.1 Semantics

A `metrics` block on `part` or `circuit` declares guaranteed scalar values. For datasheet-derived metrics, values represent worst-case guarantees, not typical values.

This means:

- For “at least” specs (GBW, CMRR, slew rate), store guaranteed minimums.
- For “at most” specs (offset voltage, supply current, noise), store guaranteed maximums.
- For ranges (supply voltage), use separate min/max metrics.

The language intentionally avoids full condition-matrix modeling (temperature/load/supply axes) in v1. It also avoids storing separate typical, minimum, and maximum tags for each entry. When needed, designers can define multiple explicitly named metrics per condition.

### 6.2 Syntax

Within a part declaration:

```cascode
metrics {
  GBW = 5.5MHz
  InputOffsetVoltage = 25uV
  CMRR = 90dB
  SupplyVoltageMin = 2.2V
  SupplyVoltageMax = 5.5V
  SupplyCurrentMax = 950uA
}
```

Each entry is `<Identifier> = <quantity>`.

### 6.3 Constraint References

Constraints reference metrics on instances using the same `instance::Metric` form used for bench bindings. Constraints are placed in sub-blocks according to their verification method (see Section 9 for the full taxonomy).

```cascode
constraints {
  bench {
    c_gbw = frontend::PassbandGain >= 40dB
  }
  spec {
    c_resolution = adc::Resolution >= 16 bits
    c_supply_min = adc::SupplyVoltageMin <= 3.3V
    c_supply_max = adc::SupplyVoltageMax >= 3.3V
  }
}
```

The left side of `::` names the source (a bench binding or a slot/instance). `bench {}` constraints must trace to bench-derived metrics; `spec {}` constraints must trace to declared metrics. The evaluator validates this mapping.

### 6.4 Interface Metric Declarations

Interfaces may declare metrics as contracts with declaration-only entries.

```cascode
interface ADCSubsystem {
  metrics {
    Resolution : bits
    MaxSampleRate : SPS
    InputCapacitance : F
  }
}
```

Rules:

1. Interface metrics are the minimum required set.
2. Implementations may expose additional metrics.
3. Missing required metrics are hard validation errors.

### 6.5 Metric Providers and Forwarding

Metric values come from explicit providers.

- Bench-derived provider for simulable blocks.
- Datasheet/declared metrics for non-simulable blocks.

Forwarding is supported for wrappers and is alias-only in v1:

```cascode
metrics {
  Resolution = uAdc::Resolution
  MaxSampleRate = uAdc::MaxSampleRate
}
```

Transform expressions in forwarding are deferred.

---

## 7. Bus Bundles

Standard PCB buses are represented as ordinary bundles with no special semantics.

```cascode
bundle I2C {
  SDA : digital
  SCL : clock
}

bundle SPI {
  MOSI : digital
  MISO : digital
  SCLK : clock
  CS   : digital
}

bundle UART {
  TX : digital
  RX : digital
}

bundle SWD {
  SWDIO : digital
  SWCLK : clock
}
```

These bundles can be used directly on parts/circuits and connected with existing bundle connection syntax.

---

## 8. Constraint Taxonomy

The constraint system uses distinct sub-blocks organized by verification method. Each sub-block carries clear semantics about how the evaluator verifies the constraint and where the metric value originates.

### 8.1 Sub-Block Types

Three constraint sub-blocks are supported:

`bench {}` constrains scalar metrics verified by bench execution (simulation). The bench planner generates a testbench, runs it, extracts a metric value, and compares the result against the stated bound. This replaces the prior `numeric {}` block from the IC-only constraint system.

```cascode
constraints {
  bench {
    c_gain = frontend::PassbandGain >= 40dB
    c_noise = frontend::IntegratedInputNoise <= 1uVrms
  }
}
```

`spec {}` constrains scalar metrics verified against declared or forwarded values. No simulation runs; the evaluator looks up the metric value from a `metrics {}` block on a part or circuit and compares it against the bound. This block is for component selection and datasheet-backed assertions.

```cascode
constraints {
  spec {
    c_res = adc::Resolution >= 16 bits
    c_flash = mcu::FlashSize >= 64kB
  }
}
```

`physical {}` constrains device and component physical parameters against structural rules. This replaces the prior `tech {}` block. In IC designs, physical constraints enforce geometry rules (minimum channel length). In PCB designs, they can enforce package, operating temperature, or other physical attributes.

```cascode
constraints {
  physical {
    p_lmin : L >= 180nm on *
  }
}
```

### 8.2 Verification Provenance

The evaluator validates that constraints are placed in the correct sub-block. A `bench {}` constraint must trace to a bench-derived metric (one produced by a bench binding's measurement). A `spec {}` constraint must trace to a declared or forwarded metric (one that appears in a `metrics {}` block on a part or circuit without an originating bench). Misplacement is a validation error.

This enforcement means the designer explicitly declares the expected verification method for each constraint. If a sub-block is later swapped for one with a different metric provenance (for example, replacing a simulable frontend with a non-simulable one), the constraint block mismatch surfaces as an error rather than silently changing verification confidence.

### 8.3 Example: Mixed Composition

An HL composition with both simulable and non-simulable sub-blocks uses both `bench` and `spec` blocks:

```cascode
circuit SensorBoard {
  level HL

  slot {
    SensorConditioner frontend = new AnalogFrontend() { ... }
    ADCSubsystem adc = new ADCStage() { ... }
    ControllerSubsystem mcu = new Controller() { ... }
  }

  constraints {
    bench {
      c_bw = frontend::LowpassBandwidth >= 10kHz
      c_gain = frontend::PassbandGain >= 40dB
      c_noise = frontend::IntegratedInputNoise <= 1uVrms
    }
    spec {
      c_adc_res = adc::Resolution >= 16 bits
      c_adc_rate = adc::MaxSampleRate >= 128 SPS
      c_mcu_flash = mcu::FlashSize >= 64kB
    }
  }
}
```

The separation makes verification intent visible in the source: the analog frontend will be simulation-verified, while the ADC and MCU are verified against their declared specifications.

### 8.4 Hierarchical Verification

Running `cascode bench run` on a composition walks the entire hierarchy tree and evaluates constraints at every level. Each circuit's constraints are checked independently, ensuring that components pass both standalone and in context.

The execution model for a composition with mixed simulable and non-simulable sub-blocks:

1. Walk the hierarchy. Identify circuits with bench bindings (simulable) and circuits with only declared or forwarded metrics (non-simulable).
2. For each simulable circuit, generate and run testbenches. Extract bench-derived metric values from simulation results.
3. For each non-simulable circuit, collect declared metric values from `metrics {}` blocks.
4. Evaluate `bench {}` constraints against simulated metrics and `spec {}` constraints against declared metrics, at every level of the hierarchy.
5. Report each constraint result with its verification provenance (bench-verified or spec-verified) and hierarchy level.

A child circuit's constraints serve as its own design targets. A parent circuit's constraints serve as system requirements. Both are always evaluated. If a child circuit targets 38 dB passband gain as a design margin and the parent requires 40 dB for the system, the bench runner reports both results independently.

---

## 9. Parts Database and Pricing Pointers

This section defines language-facing expectations. Full external sync architecture remains out of scope.

### 9.1 Role

A parts ecosystem for PCB design plays the same role that `pdk.db` plays for PDK-backed primitive flows: candidate discovery and resolution against constraints.

### 9.2 Library Organization

Interface definitions and part declarations are organized under domain-specific library trees:

- `lib/std/` -- shared constructs: bundles (`Diff`), bus bundles (`I2C`, `SPI`, `UART`, `SWD` under `lib/std/bus/`), shared interfaces (`SingleEndedOpAmp` under `lib/std/amp/`), benches, and primitives.
- `lib/ic/` -- IC-domain component interfaces (`NMOS`, `PMOS`, `Resistor`, `Capacitor`, `Inductor`, `Diode`).
- `lib/pcb/` -- PCB-domain component interfaces (`NMOS`, `Resistor` with metric contracts, `DualOpAmp`, `ADCSubsystem`, `ControllerSubsystem`, etc.). Does not contain part declarations.
- `lib/parts/` -- concrete part declarations organized by category (`lib.parts.opamp`, `lib.parts.adc`, `lib.parts.mcu`, `lib.parts.res`, `lib.parts.cap`).

### 9.3 Pricing Pointer Contract

Each part declaration carries checked-in pricing options. Population may come from manufacturer/distributor APIs, distributor CSV exports, or curated internal catalogs, but the language-facing pointer contract is the same.

Required fields per option:

- `provider`
- `sku`
- `priority`

Optional:

- `url`

This allows deterministic fallback and sourcing without brittle URL parsing.

### 9.4 Passive Resolution

Parameterized passives represent families. Concrete sourceable part resolution occurs during synthesis/selection, based on value/package/tolerance constraints and available pricing options.

---

## 10. PCB Synthesis Model

HL-to-EL synthesis for PCB design includes at least three activities.

IC selection:

- Query parts ecosystem against metric constraints.
- Instantiate selected part and associated support circuitry.

Passive network design:

- Choose topology.
- Size and snap values to preferred standard series.

Mixed-block synthesis:

- Combine selected active parts with synthesized passive networks.

The existing `synth {}` block remains the synthesis guidance carrier:

```cascode
synth {
  seed = 42
  objective = minimize_cost
  passive_series = E96
}
```

The exact `passive_series` behavior remains an implementation concern (see open questions).

---

## 11. Grammar Changes

This section describes expected grammar shape and semantic policy updates.

### 11.1 New and Modified Lexer Tokens

New tokens:

```antlr
PART_KW     : 'part' ;
METRICS_KW  : 'metrics' ;
MPN_KW      : 'mpn' ;
PRICING_KW  : 'pricing' ;
OPTION_KW   : 'option' ;
SOME_KW     : 'Some' ;
BENCH_KW    : 'bench' ;     // replaces NUMERIC_KW in constraint blocks
SPEC_KW     : 'spec' ;      // new constraint sub-block
PHYSICAL_KW : 'physical' ;  // replaces TECH_KW in constraint blocks
```

Removed tokens:

```antlr
// DEVICE_TYPE is removed. NMOS, PMOS, Resistor, etc. are now ordinary
// identifiers resolved from included interface libraries.
// NUMERIC_KW is replaced by BENCH_KW.
// TECH_KW is replaced by PHYSICAL_KW.
```

`IMPLEMENTS_KW` already exists for circuit declarations and is now shared with primitive and part declarations.

### 11.2 Top-Level Declaration

Add `partDef` to top-level declarations.

```antlr
topLevelDecl
    : ...
    | partDef
    | ...
    ;
```

### 11.3 Primitive Declaration (Modified)

```antlr
// Before:
primitiveDef
    : PRIMITIVE_KW DEVICE_TYPE name=IDENT LPAREN paramList? RPAREN LBRACE primitiveBody RBRACE
    ;

// After:
primitiveDef
    : PRIMITIVE_KW name=IDENT LPAREN paramList? RPAREN IMPLEMENTS_KW implementsList LBRACE primitiveBody RBRACE
    ;
```

### 11.4 Part Declaration

```antlr
partDef
    : PART_KW name=IDENT (LPAREN paramList? RPAREN)? IMPLEMENTS_KW implementsList LBRACE partMember* RBRACE
    ;

implementsList
    : IDENT (COMMA IDENT)*
    ;

partMember
    : MPN_KW STRING                                           # PartMpn
    | SPICE_KW STRING                                         # PartSpice
    | PACKAGE_KW STRING                                       # PartPackage
    | pricingBlock                                            # PartPricing
    | paramsBlock                                             # PartParams
    | direction portName COLON portType                       # PartPort
    | SUPPLY_KW IDENT                                         # PartSupply
    | GROUND_KW IDENT                                         # PartGround
    | metricsValueBlock                                       # PartMetrics
    ;
```

Passive `part` declarations use scalar parameters (`real R`, `real C`, `real L`). `size` remains reserved for primitive geometry.

### 11.5 Metrics Blocks

```antlr
metricsValueBlock
    : METRICS_KW LBRACE metricAssign* RBRACE
    ;

metricAssign
    : IDENT EQ metricValue
    ;

metricValue
    : signedQuantity
    | metricRef
    ;
```

Interface contracts use declaration-only metrics:

```antlr
interfaceMetricsBlock
    : METRICS_KW LBRACE metricDecl* RBRACE
    ;

metricDecl
    : IDENT COLON unitType
    ;
```

### 11.6 Slot Instance Declaration with `Some`

`Some` is only valid in slot instance declarations. This is enforced at the grammar level by using separate rules for slot and fill blocks:

```antlr
slotBlockStatement
    : NET_KW IDENT COLON portType                                    # SlotNetDecl
    | slotInstanceDecl                                               # SlotInstanceDecl
    | pinRef WIRE_OP pinRef                                          # SlotConnectDecl
    ;

slotInstanceDecl
    : (declaredType=IDENT | SOME_KW) instanceId=IDENT EQ NEW_KW instanceTypeName
      (LPAREN argList? RPAREN)? bindingBlock?
    ;
```

In fill blocks, the existing `instanceDecl` requires a declared type (an interface name) and does not accept `Some`:

```antlr
instanceDecl
    : declaredType=IDENT instanceId=IDENT EQ NEW_KW instanceTypeName
      (LPAREN argList? RPAREN)? bindingBlock?
    ;
```

The formerly optional `(declaredType=IDENT)?` pattern is removed. A declared type is always required in both slot and fill blocks.

### 11.7 Device Instantiation (Modified)

With `DEVICE_TYPE` removed, the existing `deviceDecl` rule is unified with `instanceDecl`. The declared type is an interface name (e.g., `NMOS`, `Resistor`) resolved from scope rather than a reserved keyword:

```antlr
// Before:
deviceDecl
    : DEVICE_TYPE deviceId EQ NEW_KW primitiveName=IDENT LPAREN sizeArg RPAREN bindingBlock
    ;

// After: unified into instanceDecl (see above).
```

### 11.8 Constraint Block Changes

The constraint sub-block keywords are renamed and extended:

```antlr
// Before:
constraintsBlock
    : CONSTRAINTS_KW LBRACE numericBlock? techBlock? RBRACE
    ;

// After:
constraintsBlock
    : CONSTRAINTS_KW LBRACE benchBlock? specBlock? physicalBlock? RBRACE
    ;

benchBlock
    : BENCH_KW LBRACE constraintEntry* RBRACE
    ;

specBlock
    : SPEC_KW LBRACE constraintEntry* RBRACE
    ;

physicalBlock
    : PHYSICAL_KW LBRACE physicalConstraintEntry* RBRACE
    ;
```

New and renamed tokens:

```antlr
BENCH_KW    : 'bench' ;     // replaces NUMERIC_KW
SPEC_KW     : 'spec' ;      // new
PHYSICAL_KW : 'physical' ;  // replaces TECH_KW
```

`constraintEntry` and `physicalConstraintEntry` retain their existing internal structure. The change is in the sub-block keywords and the addition of the `spec` block.

### 11.9 Resolution Policy

There are no reserved keyword categories. All instantiation targets -- whether for primitives, parts, or circuits -- resolve semantically against declarations in scope. The `include` directives determine which interfaces are available. Ambiguous or unresolved targets are hard validation errors.

---

## 12. Worked Example: Sensor Frontend PCB

A complete worked example accompanies this RFC at `tests/golden/cas/pcb/SensorFrontendPCB.cas`. The example includes:

- Wheatstone-bridge sensor frontend topology.
- Dual op-amp analog path with bench-derived metrics.
- 16-bit ADC and MCU wrappers with forwarded metrics.
- `part` declarations with `implements` and `mpn`.
- pricing option pointers with provider/sku/priority.
- flat port naming for multi-unit ICs (dual op-amp with per-port directionality).
- `bench`/`spec` constraint taxonomy separating simulation-verified and declaration-verified constraints.
- hierarchical constraint verification (EL design targets and HL system requirements).
- metric-driven env propagation at simulation boundaries.

The example intentionally exercises both simulable and non-simulable paths within one HL composition, demonstrating the constraint taxonomy across verification methods and hierarchy levels.

---

## 13. Implementation Plan

Implementation is split into phases.

Phase 1: Grammar and AST

- Remove `DEVICE_TYPE` from grammar; replace with `implements` on `primitiveDef`.
- Add `part`, `mpn`, `metrics`, `pricing`, `Some` grammar support.
- Add `implementsList` rule shared by `primitiveDef`, `partDef`, and `circuitDef`.
- Add separate `slotInstanceDecl` with `Some` support.
- Rename constraint sub-blocks: `numeric` → `bench`, `tech` → `physical`. Add `spec` sub-block.
- Add AST types for part declarations, pricing options, metric declarations/assignments.
- Add reader/writer support and tests.
- Update all existing golden tests for the `bench`/`physical` rename.

Phase 2: Interface libraries

- Create `lib/ic/Interfaces.cas` with IC-domain component interfaces.
- Create `lib/pcb/Interfaces.cas` with PCB-domain component interfaces and bus bundles.
- Migrate `lib/std/prim/Devices.cas` and `lib/std/prim/Passives.cas` to new `implements` syntax.
- Update all golden tests and examples.

Phase 3: Resolution and validation

- Implement unified semantic instantiation resolution policy (no reserved keyword categories).
- Add interface metric contract validation.
- Add alias-only forwarding resolution and cycle detection.
- Validate `Some` only appears in slot blocks (grammar-enforced).

Phase 4: Constraint and runtime evaluation

- Extend evaluators to consume metric references (`instance::Metric`) from both bench and declared sources.
- Implement `bench` constraint evaluation (simulation-verified) and `spec` constraint evaluation (declaration-verified).
- Implement verification provenance validation (constraints in the correct sub-block for their metric source).
- Implement hierarchical verification: `cascode bench run` walks the composition tree and evaluates constraints at every level.

Phase 5: Parts ecosystem integration

- Wire pricing pointers to provider adapters/cache.
- Implement deterministic option ordering/fallback via `priority`.

Phase 6: Emission and synthesis expansion

- Evolve PCB-oriented emission contracts.
- Implement passive-series snapping policy details.

---

## 14. Open Questions

The following remain open:

1. Output format for full PCB designs (`cascode emit` targets such as KiCad schematic/netlist/BOM).
2. Standard passive-series integration details (`passive_series` snapping policy, decomposition behavior).
3. Power distribution pattern abstractions (language shorthand versus explicit wiring/library motifs).
4. Connector and mechanical modeling depth in language core versus libraries.
5. Whether interfaces should support inheritance for shared contracts across IC/PCB domains (e.g., a base `Resistor` that both `lib.ic.Resistor` and `lib.pcb.Resistor` extend).
6. Slot synthesis semantics when `Some` is used: does the solver infer the required interface set from constraint metric references, or must the designer provide explicit interface hints?

---

## References

- RFC-0000: Cascode Language Unification and Declarative Bench System
- RFC-0002: ACIR Terminal Directionality
- RFC-0003: ACIR Syntax Overhaul
- `spec/language/Ch01_Introduction.md` through `Ch04_BenchSystem.md`
- `lib/std/prim/Devices.cas`, `lib/std/prim/Passives.cas` (to be migrated to `implements` syntax)
- `lib/std/amp/SingleEndedOpAmp.cas`
- `lib/std/amp/FullyDifferentialOpAmp.cas`
- `tests/golden/cas/stress/OTA5T_Sky130.cas`
- `tests/golden/cas/stress/OTA5TFullyDiff_Ideal.cas`
- `tests/golden/cas/pcb/SensorFrontendPCB.cas`
- `tests/golden/cas/hl/HLComposition.hl.cai`
