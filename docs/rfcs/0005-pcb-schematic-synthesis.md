# RFC-0005: PCB Schematic Representation and Synthesis

Status: Draft
Authors: Daniel Lovell
Created: 2026-02-06
Last Updated: 2026-02-07

---

## Abstract

Cascode's abstractions for IC design (bundles, interfaces, circuits, primitives, benches, constraints, HL/EL levels) map naturally to PCB schematic capture and synthesis. This RFC extends the language with a `part` construct for packaged off-the-shelf components, a metrics system for datasheet-driven and simulation-driven validation, bus bundles for digital interconnect, and channel sub-interfaces for multi-channel ICs. These additions enable Cascode to represent PCB schematics at both high-level (system architecture with constraint-driven part selection) and electrical level (concrete schematic with specific component values and connections).

A unifying insight drives the design: in both IC and PCB flows, the schematic symbol (pin contract and behavioral requirements) is separate from the concrete backing (a PDK device or a sourced component). This RFC formalizes that separation by making both `primitive` and the new `part` declare which interfaces they satisfy via `implements`. Reserved keyword categories (`NMOS`, `Resistor`, etc.) become library-defined interfaces in domain-specific libraries (`lib/ic/`, `lib/pcb/`), making the component taxonomy extensible without grammar changes.

---

## 1. Motivation

PCB schematic capture today uses GUI tools (KiCad, Altium, OrCAD) that, like analog IC schematics, obscure design intent. A sensor frontend board requires specific noise, bandwidth, and accuracy constraints that live in the designer's head and separate notes but are not usually captured alongside the schematic. When a component goes end-of-life, the designer must manually verify whether a replacement part still meets original constraints spread across documents and datasheets.

Cascode's constraint-driven, multi-level abstraction model addresses this gap. An HL design expresses system requirements (for example, the analog frontend must achieve 40 dB gain with less than 1 uVrms integrated noise). An EL design captures concrete schematic structure with explicit components and connections. Bench execution validates simulable blocks (for example, op-amp paths with SPICE models), while datasheet-backed metrics validate non-simulable blocks (for example, ADC and MCU capability constraints). Both use one constraint language.

A secondary motivation is passive-network synthesis. PCB designs contain filters, bias dividers, gain-setting networks, and decoupling structures that involve topology and discrete value selection. These can use the same synthesis framework used for IC circuits, with additional constraints that values snap to standard series and resolvable parts exist.

---

## 2. Goals and Non-Goals

Goals:

1. Represent complete PCB schematics in Cascode at both HL and EL abstraction levels.
2. Introduce a `part` construct for packaged components, parallel to `primitive`, both using `implements` to satisfy interface contracts.
3. Unify the component taxonomy: replace reserved keyword categories (`NMOS`, `Resistor`, etc.) with library-defined interfaces in `lib/ic/` and `lib/pcb/`.
4. Enable constraint-driven part selection using guaranteed datasheet metrics unified with existing bench-derived metrics.
5. Support standard PCB signal buses (I2C, SPI, UART, SWD) as first-class bundle types.
6. Handle multi-channel ICs (dual op-amps, quad ADCs) through channel sub-interfaces.
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
| `lib/ic/` interfaces (`NMOS`, `Resistor`, ...) | `lib/pcb/` interfaces (`NMOS`, `DualOpAmp`, `ADC`, ...) | Domain-specific interface libraries; same name may appear in both with domain-appropriate contracts |
| PDK (`pdk scan`) | Parts library + external pricing/availability sources | Source of available components and their operating/procurement attributes |
| `size(W=2u, L=180n, M=1)` | `real` scalar params for passives; no value params for fixed-identity ICs | `size` remains reserved for transistor geometry on primitives |
| `device "sky130_fd_pr__nfet_01v8"` | `mpn "OPA2376AIDDBVR"` | Distinct identity fields by declaration kind (`device` for primitive, `mpn` for part) |
| `bench` with SPICE analysis | Bench-derived metrics plus datasheet-valued metrics | Unified constraints; metric source resolved by the evaluator |
| `bundle Diff { P, N }` | `bundle I2C { SDA, SCL }` | Digital bus bundles for PCB interconnect |
| `interface SingleEndedOpAmp` | `interface SensorConditioner` | Functional contracts remain interface-centric; channels reference interfaces |
| `constraints { numeric { ... } }` | Same syntax, constraining metric references from multiple providers | One constraint language, multiple sources |
| HL `slot` + synthesis | Part selection for ICs; topology/value selection for passive networks | `Some` keyword for synthesis-inferred types in slots |

---

## 4. Interface Libraries and the `implements` Model

### 4.1 Unified Abstraction

In both IC and PCB design, the schematic symbol — the pin contract and behavioral requirements of a component — is separate from its concrete backing. An op-amp symbol declares differential inputs, a single-ended output, and supply rails. Whether the backing is a PDK device model or a sourced part with an MPN is an implementation detail.

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

`lib/pcb/Interfaces.cas` defines PCB-domain component interfaces with PCB-standard terminal sets and metric contracts:

```cascode
library lib.pcb

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

interface SingleEndedOpAmp {
  input INP : analog
  input INN : analog
  output OUT : analog
}

interface DualOpAmp {
  channel A : SingleEndedOpAmp
  channel B : SingleEndedOpAmp
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

`Resistor` appears in both libraries with domain-appropriate contracts. The IC version declares only terminals; the PCB version adds metric requirements for tolerance and power rating. These are separate interfaces in separate namespaces. `include lib.ic` or `include lib.pcb` brings the appropriate definitions into scope. Mixed IC+PCB projects can include both; the linker resolves by fully-qualified namespace.

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

### 4.4 Channels as Interface References

Channels on multi-unit components reference interfaces, not bundles. A channel of a dual op-amp IS a single-ended op-amp — it is a functional sub-unit with its own pin contract, not a connectable wire group.

```cascode
interface DualOpAmp {
  channel A : SingleEndedOpAmp
  channel B : SingleEndedOpAmp
  supply VDD
  ground GND
}
```

The `channel` declaration syntax is `channel <name> : <interfaceType>`. Shared supply and ground remain part-level declarations.

### 4.5 Multiple Interface Implementation

Parts and circuits may implement multiple interfaces, following the same model as C# multiple interface implementation:

```cascode
part ADS1115 implements ADCSubsystem, I2CDevice {
  mpn "ADS1115IDGSR"
  ...
}
```

The implementation must satisfy the port and metric contracts of all declared interfaces. Missing ports or required metrics from any interface are hard validation errors.

### 4.6 The `Some` Keyword

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
  <channel declarations>           // for multi-channel parts

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
- terminal/channel declarations: physical connectivity.
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

  channel A : SingleEndedOpAmp
  channel B : SingleEndedOpAmp
  supply VDD
  ground GND

  metrics {
    GBW = 5.5MHz
    InputOffsetVoltage = 25uV
    CMRR = 114dB
    InputBiasCurrent = 0.2pA
    SlewRate = 2V/us
    SupplyCurrentMax = 285uA
  }
}
```

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
part R_0402(real R) implements Resistor {
  mpn "RES-0402-1PCT"
  package "0402"

  pricing {
    option { provider = "DigiKey" sku = "RES-0402-1PCT" priority = 10 }
    option { provider = "Mouser" sku = "RES-0402-1PCT" priority = 20 }
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
  // parameterized passive — Resistor is an interface from lib.pcb
  Resistor R1 = new R_0402(R=10k) {
    .P--IN
    .N--OUT
  }

  // fixed-identity IC part — DualOpAmp is an interface from lib.pcb
  DualOpAmp U1 = new OPA2376() {
    .A.INP--sensor_p
    .A.INN--ref
    .A.OUT--stage1_out
    .VDD--VDD
    .GND--GND
  }
}
```

IC primitive instantiation follows the same pattern — the declared type is the interface, not a reserved keyword:

```cascode
fill {
  NMOS M1 = new nfet_01v8(size(W=2u, L=180n, M=1, NF=1)) { ... }
  PMOS M2 = new pfet_01v8(size(W=2u, L=180n, M=1, NF=1)) { ... }
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
  CMRR = 114dB
  SupplyVoltageMin = 2.2V
  SupplyVoltageMax = 5.5V
  SupplyCurrentMax = 285uA
}
```

Each entry is `<Identifier> = <quantity>`.

### 6.3 Constraint References

Constraints reference metrics on instances using the same `instance::Metric` form used for bench bindings.

```cascode
constraints {
  numeric {
    c_gbw = frontend::PassbandGain >= 40dB
    c_resolution = adc::Resolution >= 16 bits
    c_supply_min = adc::SupplyVoltageMin <= 3.3V
    c_supply_max = adc::SupplyVoltageMax >= 3.3V
  }
}
```

The left side of `::` names the source (a bench binding or a slot/instance). The resolver determines whether the metric comes from a bench or a declared `metrics {}` block on the target. Bench-native references remain valid where needed.

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
  Resolution = U_ADC::Resolution
  MaxSampleRate = U_ADC::MaxSampleRate
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

## 8. Channels

### 8.1 Motivation

Many PCB components contain multiple functionally similar channels in one package. A dual op-amp contains two independent amplifier channels; a quad ADC contains four independent conversion channels. Channels allow explicit per-channel wiring while preserving one physical part identity.

### 8.2 Syntax

Channels reference interfaces, not bundles. Each channel of a multi-unit component is a functional sub-unit that satisfies its own interface contract.

```cascode
part OPA2376 implements DualOpAmp {
  mpn "OPA2376AIDDBVR"
  package "VSSOP-8"
  spice "OPA2376"

  channel A : SingleEndedOpAmp
  channel B : SingleEndedOpAmp
  supply VDD
  ground GND

  metrics {
    GBW = 5.5MHz
    InputOffsetVoltage = 25uV
    CMRR = 114dB
    SupplyCurrentMax = 285uA
  }
}
```

In fill bindings, channel terminals are accessed via dot notation:

```cascode
fill {
  DualOpAmp U1 = new OPA2376() {
    .A.INP--sensor_p
    .A.INN--ref
    .A.OUT--stage1_out
    .B.INP--stage1_out
    .B.INN--fb_node
    .B.OUT--final_out
    .VDD--VDD
    .GND--GND
  }
}
```

### 8.3 Semantics

Channels are independent signal namespaces. Shared supply and ground remain part-level declarations outside any channel.

---

## 9. Constraint Unification

The constraint system supports multiple metric sources through one numeric constraint form.

1. Bench-derived metrics: values computed from simulation.
2. Declared part/circuit metrics: values sourced from datasheet-backed declarations or forwarded aliases.

Example HL composition:

```cascode
circuit SensorBoard {
  level HL

  slot {
    SensorConditioner frontend = new AnalogFrontend() { ... }
    ADCSubsystem adc = new ADCStage() { ... }
    ControllerSubsystem mcu = new Controller() { ... }
  }

  constraints {
    numeric {
      c_bw = frontend::LowpassBandwidth >= 10kHz
      c_gain = frontend::PassbandGain >= 40dB
      c_noise = frontend::IntegratedInputNoise <= 1uVrms

      c_res = adc::Resolution >= 16 bits
      c_rate = adc::MaxSampleRate >= 128 SPS

      c_flash = mcu::FlashSize >= 64kB
    }
  }
}
```

This unifies user intent in one place while preserving explicit source paths.

---

## 10. Parts Database and Pricing Pointers

This section defines language-facing expectations. Full external sync architecture remains out of scope.

### 10.1 Role

A parts ecosystem for PCB design plays the same role that `pdk.db` plays for PDK-backed primitive flows: candidate discovery and resolution against constraints.

### 10.2 Library Organization

Interface definitions and part declarations are organized under domain-specific library trees:

- `lib/ic/` — IC-domain interfaces (`NMOS`, `PMOS`, `Resistor`, `Capacitor`, `Inductor`, `Diode`).
- `lib/pcb/` — PCB-domain interfaces (`NMOS`, `Resistor`, `SingleEndedOpAmp`, `DualOpAmp`, `ADC`, etc.) and bus bundles (`I2C`, `SPI`, `UART`, `SWD`).
- `lib/pcb/parts/` — concrete part declarations organized by category (`lib.pcb.parts.opamp`, `lib.pcb.parts.adc`, `lib.pcb.parts.res.0402`).
- `lib/std/` — shared constructs (bundles like `Diff`, benches, etc.).

### 10.3 Pricing Pointer Contract

Each part declaration carries checked-in pricing options. Population may come from manufacturer/distributor APIs, distributor CSV exports, or curated internal catalogs, but the language-facing pointer contract is the same.

Required fields per option:

- `provider`
- `sku`
- `priority`

Optional:

- `url`

This allows deterministic fallback and sourcing without brittle URL parsing.

### 10.4 Passive Resolution

Parameterized passives represent families. Concrete sourceable part resolution occurs during synthesis/selection, based on value/package/tolerance constraints and available pricing options.

---

## 11. PCB Synthesis Model

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

## 12. Grammar Changes

This section describes expected grammar shape and semantic policy updates.

### 12.1 New and Modified Lexer Tokens

New tokens:

```antlr
PART_KW     : 'part' ;
METRICS_KW  : 'metrics' ;
CHANNEL_KW  : 'channel' ;
MPN_KW      : 'mpn' ;
PRICING_KW  : 'pricing' ;
OPTION_KW   : 'option' ;
SOME_KW     : 'Some' ;
```

Removed token:

```antlr
// DEVICE_TYPE is removed. NMOS, PMOS, Resistor, etc. are now ordinary
// identifiers resolved from included interface libraries.
```

`IMPLEMENTS_KW` already exists for circuit declarations and is now shared with primitive and part declarations.

### 12.2 Top-Level Declaration

Add `partDef` to top-level declarations.

```antlr
topLevelDecl
    : ...
    | partDef
    | ...
    ;
```

### 12.3 Primitive Declaration (Modified)

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

### 12.4 Part Declaration

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
    | channelDecl                                             # PartChannel
    ;
```

Passive `part` declarations use scalar parameters (`real R`, `real C`, `real L`). `size` remains reserved for primitive geometry.

### 12.5 Metrics Blocks

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

### 12.6 Channel Declaration

Channels reference interfaces, not bundles:

```antlr
channelDecl
    : CHANNEL_KW name=IDENT COLON interfaceType=IDENT
    ;
```

### 12.7 Slot Instance Declaration with `Some`

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

### 12.8 Device Instantiation (Modified)

With `DEVICE_TYPE` removed, the existing `deviceDecl` rule is unified with `instanceDecl`. The declared type is an interface name (e.g., `NMOS`, `Resistor`) resolved from scope rather than a reserved keyword:

```antlr
// Before:
deviceDecl
    : DEVICE_TYPE deviceId EQ NEW_KW primitiveName=IDENT LPAREN sizeArg RPAREN bindingBlock
    ;

// After: unified into instanceDecl (see above).
```

### 12.9 Resolution Policy

There are no reserved keyword categories. All instantiation targets — whether for primitives, parts, or circuits — resolve semantically against declarations in scope. The `include` directives determine which interfaces are available. Ambiguous or unresolved targets are hard validation errors.

---

## 13. Worked Example: Sensor Frontend PCB

A complete worked example accompanies this RFC at `tests/golden/cas/pcb/SensorFrontendPCB.cas`. The example includes:

- Wheatstone-bridge sensor frontend topology.
- Dual op-amp analog path with bench-derived metrics.
- 16-bit ADC and MCU wrappers with forwarded metrics.
- `part` declarations with `implements` and `mpn`.
- pricing option pointers with provider/sku/priority.
- channel sub-interfaces referencing `SingleEndedOpAmp`.
- unified metric-based constraints with `instance::Metric` references.
- metric-driven env propagation at simulation boundaries.

The example intentionally exercises both simulable and non-simulable paths within one HL composition.

---

## 14. Implementation Plan

Implementation is split into phases.

Phase 1: Grammar and AST

- Remove `DEVICE_TYPE` from grammar; replace with `implements` on `primitiveDef`.
- Add `part`, `mpn`, `metrics`, `pricing`, `channel`, `Some` grammar support.
- Add `implementsList` rule shared by `primitiveDef`, `partDef`, and `circuitDef`.
- Add separate `slotInstanceDecl` with `Some` support.
- Add AST types for part declarations, pricing options, metric declarations/assignments.
- Add reader/writer support and tests.

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

- Extend evaluators to consume unified metric references (`instance::Metric`).
- Support bench-provider and declaration-provider metric resolution.

Phase 5: Parts ecosystem integration

- Wire pricing pointers to provider adapters/cache.
- Implement deterministic option ordering/fallback via `priority`.

Phase 6: Emission and synthesis expansion

- Evolve PCB-oriented emission contracts.
- Implement passive-series snapping policy details.

---

## 15. Open Questions

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
