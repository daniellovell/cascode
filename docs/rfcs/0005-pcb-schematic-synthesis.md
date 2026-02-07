# RFC-0005: PCB Schematic Representation and Synthesis

Status: Draft
Authors: Daniel Lovell
Created: 2026-02-06
Last Updated: 2026-02-06
Target Version: Cascode 1.x

---

## Abstract

Cascode's abstractions for IC design (bundles, interfaces, circuits, primitives, benches, constraints, HL/EL levels) map naturally to PCB schematic capture and synthesis. This RFC proposes extending the language with a **`part`** construct for packaged off-the-shelf components, a **`specs`** system for datasheet-driven constraints, bus bundles for digital interconnect, and channel sub-bundles for multi-channel ICs. These additions enable Cascode to represent PCB schematics at both high-level (system architecture with constraint-driven part selection) and electrical level (concrete schematic with specific component values and connections).

The core shift from IC to PCB design: instead of choosing transistor-level primitives from a foundry PDK, you choose components from a parts database. The composition model, constraint system, and multi-level abstraction carry over directly.

---

## 1. Motivation

PCB schematic capture today uses GUI tools (KiCad, Altium, OrCAD) that, like analog IC schematics, obscure design intent. A sensor frontend board requires specific noise, bandwidth, and accuracy constraints that live in the designer's head and a separate spreadsheet but are never captured alongside the schematic. When a component goes end-of-life, the designer must manually verify whether a replacement part meets all the original constraints scattered across emails and datasheets.

Cascode's constraint-driven, multi-level abstraction model addresses this gap. An HL design expresses system requirements ("the analog frontend must achieve 40dB gain with less than 1uVrms integrated noise"). An EL design captures the concrete schematic with specific part numbers and connections. The bench system validates simulable blocks (opamp circuits with manufacturer SPICE models), while the specs system validates non-simulable blocks (ADCs, MCUs) against guaranteed datasheet parameters. Both constraint sources use the same syntax.

A secondary motivation: PCB designs, like IC designs, contain passive networks (filters, bias dividers, gain-setting resistors) that involve real topology selection and value sizing. These are amenable to the same synthesis approach used for IC circuits, with the added constraint that component values must land on standard series (E96, E24) and that parts must be physically available.

---

## 2. Goals and Non-Goals

Goals:

1. Represent complete PCB schematics in Cascode at both HL and EL abstraction levels.
2. Introduce a `part` construct for packaged components, cleanly distinguished from IC-level `primitive` declarations.
3. Enable constraint-driven part selection using guaranteed datasheet specifications, unified with the existing bench-based constraint syntax.
4. Support standard PCB signal buses (I2C, SPI, UART, SWD) as first-class bundle types.
5. Handle multi-channel ICs (dual opamps, quad ADCs) through channel sub-bundles.
6. Maintain backward compatibility: all existing IC-design Cascode source remains valid.
7. Provide a worked example (Wheatstone bridge sensor frontend) that stress-tests the proposed constructs.

Non-goals:

1. PCB layout or physical placement. This RFC addresses schematic capture only.
2. Specifying output formats (KiCad, Altium, BOM generation). Emission targets are future work.
3. Defining the parts database schema or import pipeline. This RFC sketches the concept; implementation is separate.
4. Automatic propagation of specs across circuit boundaries. Designers manually declare circuit-level specs for now.
5. Modeling pin alternate functions for MCUs. The language captures physical connectivity; pin mux configuration is external tooling.

---

## 3. Conceptual Mapping

The table below summarizes how existing IC Cascode concepts translate to PCB design. Items marked with "(new)" require language changes proposed in this RFC; all others work as-is.

| IC Cascode | PCB Cascode | Notes |
|---|---|---|
| `primitive NMOS nfet(size s)` | `part OpAmp OPA2376` (new) | Maps category to a concrete component backed by a parts database |
| PDK (`pdk scan`) | Parts database (new) | Source of available components and their specifications |
| `size(W=2u, L=180n, M=1)` | `real` scalar params for passives; no params for ICs | `size` is reserved for transistor geometry. Passives take scalar value parameters (e.g., `real R`). ICs are selected, not sized. |
| `device "sky130_fd_pr__nfet_01v8"` | `device "OPA2376AIDDBVR"` | Both reference a backing database entry by identifier |
| Device classes: NMOS, PMOS, R, C, L, D | Extended: OpAmp, ADC, MCU, LDO, LED, Connector, ... (new) | Broader taxonomy of component categories |
| `bench` with SPICE analysis | `bench` for parts with SPICE models; `specs` for the rest (new) | Opamps often have SPICE models; MCUs and ADCs typically do not |
| `bundle Diff { P, N }` | `bundle I2C { SDA, SCL }` (new library content) | Digital bus bundles for PCB interconnect |
| `interface SingleEndedOpAmp` | `interface SensorConditioner` | Functional block contracts, unchanged semantics |
| `circuit` with `fill {}` | Same; explicit connectivity is schematic capture | No change needed |
| `constraints { numeric { ... } }` | Same syntax for both bench and spec constraints (new) | `bench::Metric >= X` alongside `specs::Property >= Y` |
| HL `slot` + synthesis | Part selection for ICs; topology + value selection for passive networks | Synthesis scope is broader than just catalog lookup |

---

## 4. The `part` Construct

### 4.1 Syntax

A `part` declaration is a new top-level construct, parallel to `primitive`. It maps a component category to a specific packaged device, with declared pins and datasheet specifications.

```
part <Category> <Name> (<params>?) {
  device "<backing_identifier>"
  package "<footprint>"
  spice "<model_file>"            // optional: present when manufacturer provides a SPICE model

  <terminal declarations>         // input, output, io, supply, ground
  <channel declarations>          // for multi-channel parts

  specs {
    <Property> = <guaranteed_value>
    ...
  }
}
```

The header mirrors `primitive`: a keyword, a category identifier, a name, and an optional parameter signature. The body contains:

- `device`: the identifier used to look up this component in the parts database (typically a manufacturer part number). Same field name as `primitive` to reinforce the parallel: both reference a backing store entry.
- `package`: the physical footprint (e.g., "SOT-23-5", "LQFP-32", "0402").
- `spice`: an optional reference to a manufacturer SPICE model file. Parts with this directive can participate in bench simulations. Parts without it are constrained only through `specs`.
- Terminal declarations: physical pins of the component, using the same `input`/`output`/`io`/`supply`/`ground` syntax as circuits.
- `specs {}`: guaranteed datasheet specifications (Section 5).
- `channel` blocks: for multi-channel parts (Section 7).

### 4.2 Examples

A single-channel opamp with a SPICE model:

```cascode
part OpAmp OPA376 {
  device "OPA376AIDCKR"
  package "SC70-5"
  spice "OPA376"

  input INP : analog
  input INN : analog
  output OUT : analog
  supply VDD
  ground GND

  specs {
    GBW = 5.5MHz
    InputOffsetVoltage = 25uV
    CMRR = 114dB
    InputBiasCurrent = 0.2pA
    SlewRate = 2V/us
    SupplyCurrentMax = 285uA
  }
}
```

A 16-bit I2C ADC without a SPICE model:

```cascode
part ADC ADS1115 {
  device "ADS1115IDGSR"
  package "MSOP-10"

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

  specs {
    Resolution = 16 bits
    MaxSampleRate = 860 SPS
    INL = 0.5 LSB
    FullScaleRange = 6.144V
    SupplyVoltageMin = 2.0V
    SupplyVoltageMax = 5.5V
    SupplyCurrentMax = 200uA
  }
}
```

A parameterized passive (resistor family):

```cascode
part Resistor R_0402(real R) {
  device "generic_0402_1pct"
  package "0402"

  io P : analog
  io N : analog

  params {
    R = R
  }

  specs {
    Tolerance = 1pct
    PowerRating = 63mW
    VoltageRating = 50V
  }
}
```

### 4.3 Instantiation

Parts are instantiated in `fill` blocks using the same syntax as primitives. The device category prefix distinguishes device instantiation from circuit instantiation at the grammar level:

```cascode
fill {
  // Parameterized passive: real-valued scalar parameter
  Resistor R1 = new R_0402(R=10k) {
    .P--IN
    .N--OUT
  }

  // Fixed-identity IC: zero parameters
  OpAmp U1 = new OPA376() {
    .INP--signal_in
    .INN--fb_node
    .OUT--signal_out
    .VDD--VDD
    .GND--GND
  }

  // Multi-channel IC (Section 7): dot notation into channels
  OpAmp U2 = new OPA2376() {
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

The unified instantiation syntax means zero-parameter parts (`new OPA376()`) and parameterized parts (`new R_0402(R=10k)`) use the same grammar rule. The presence or absence of arguments is the only difference.

### 4.4 Relationship to `primitive`

`primitive` and `part` are siblings, not parent-child. Both are leaf-node device definitions that back instantiations in `fill` blocks. The distinction is the design domain:

- `primitive` declarations are backed by a foundry PDK. Their `device` strings resolve against `pdk.db`. They model on-die structures (transistors, on-chip passives) with physical dimension parameters.
- `part` declarations are backed by a parts database. Their `device` strings resolve against the parts DB. They model packaged off-the-shelf components with value parameters (passives) or no parameters (ICs).

A single Cascode project may contain both `primitive` and `part` declarations if the design spans IC and PCB (e.g., a custom ASIC and its evaluation board).

### 4.5 Device Category Extension

The existing `DEVICE_TYPE` grammar token recognizes six categories: `NMOS`, `PMOS`, `Resistor`, `Capacitor`, `Inductor`, `Diode`. PCB design requires additional categories. This RFC proposes extending the token set with: `OpAmp`, `ADC`, `DAC`, `MCU`, `LDO`, `LED`, `Connector`. Shared categories (`Resistor`, `Capacitor`, etc.) work for both `primitive` and `part` declarations. Category-specific validation (e.g., expected pins per category) is a semantic concern, not a grammar concern.

---

## 5. The `specs` System

### 5.1 Semantics

A `specs` block declares guaranteed datasheet specifications as named scalar quantities. Every value represents the manufacturer's worst-case guarantee, not a typical or nominal value.

This means:
- For "at least" specifications (GBW, CMRR, slew rate): the value is the guaranteed minimum.
- For "at most" specifications (offset voltage, supply current, noise): the value is the guaranteed maximum.
- For range boundaries (supply voltage min/max): separate entries capture each bound.

The designer is responsible for extracting guaranteed values from the datasheet and entering them in the `specs` block. The language does not model typical, minimum, and maximum as separate tags; it stores one number per spec, and that number must be the guaranteed bound.

This simplicity is deliberate. Datasheet specifications are tested under specific conditions (temperature, supply voltage, load) that vary by manufacturer. Modeling the full condition matrix in the language would add complexity without proportional benefit. If a designer needs to validate a spec under different conditions, they can define multiple specs (e.g., `GBW_25C = 5.5MHz`, `GBW_85C = 4MHz`) or rely on the parts database for condition-aware lookups.

### 5.2 Syntax

Within a `part` declaration:

```cascode
specs {
  GBW = 5.5MHz
  InputOffsetVoltage = 25uV
  CMRR = 114dB
  SupplyVoltageMin = 2.2V
  SupplyVoltageMax = 5.5V
  SupplyCurrentMax = 285uA
}
```

Each entry has the form `<Identifier> = <quantity>`. The identifier is a free-form name (PascalCase by convention). The quantity uses the standard Cascode unit system (`V`, `A`, `Hz`, `Ohm`, `dB`, `F`, `H`, `W`, `deg`, `bits`, `SPS`, `LSB`, `pct`).

### 5.3 Constraint References

Constraints reference specs using the same `::` accessor syntax as bench measurements:

```cascode
constraints {
  numeric {
    // Bench-derived (from SPICE simulation)
    c_gbw = transfer_bench::GainBandwidth >= 1MHz

    // Spec-derived (from datasheet)
    c_resolution = specs::Resolution >= 16 bits
    c_supply_min = specs::SupplyVoltageMin <= 3.3V
    c_supply_max = specs::SupplyVoltageMax >= 3.3V
  }
}
```

In HL compositions where constraints reference sub-block metrics, dot notation reaches into the sub-block:

```cascode
constraints {
  numeric {
    c_frontend_bw = frontend.transfer_bench::LowpassBandwidth >= 10kHz
    c_adc_res     = adc.specs::Resolution >= 16 bits
    c_mcu_flash   = mcu.specs::FlashSize >= 64kB
  }
}
```

Here `frontend`, `adc`, and `mcu` are instance names from the slot's fill block. The `specs::Property` path resolves against the specs declared on the circuit that fills that slot.

### 5.4 Circuit-Level Specs

When a circuit wraps a `part` (e.g., an ADC stage that instantiates an ADS1115 plus supporting passives), the circuit declares its own specs. These are the specs visible to parent compositions:

```cascode
circuit ADCStage {
  level EL
  // ...

  specs {
    Resolution = 16 bits
    MaxSampleRate = 860 SPS
  }
}
```

The circuit author is responsible for ensuring these match the internal part's capabilities. Automatic propagation from part specs to circuit specs is a potential future enhancement but is not proposed here.

---

## 6. Bus Bundles

Standard PCB buses are represented as bundles. These are ordinary Cascode bundles with no special semantics; they follow the existing bundle system exactly. A standard library file (`lib/pcb/Buses.cas`) would provide:

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

These bundles are used as terminal types on circuits and parts:

```cascode
circuit ADCStage {
  io BUS : I2C
  // ...
}
```

Connection syntax is the same as existing bundle connections:

```cascode
fill {
  ADC U_ADC = new ADS1115() {
    .SDA--BUS.SDA
    .SCL--BUS.SCL
    // ...
  }
}
```

---

## 7. Channel Sub-Bundles

### 7.1 Motivation

Many PCB components contain multiple identical functional units in one package: dual opamps, quad ADCs, multi-channel DACs. These share power pins but have independent signal pins per channel. The language must represent this structure so that the fill block can address individual channels while the BOM sees one physical part.

### 7.2 Syntax

A `channel` declaration within a `part` groups signal pins into a named bundle interface:

```cascode
bundle OpAmpChannel {
  INP : analog
  INN : analog
  OUT : analog
}

part OpAmp OPA2376 {
  device "OPA2376AIDDBVR"
  package "VSSOP-8"
  spice "OPA2376"

  channel A : OpAmpChannel
  channel B : OpAmpChannel
  supply VDD
  ground GND

  specs {
    GBW = 5.5MHz
    InputOffsetVoltage = 25uV
    CMRR = 114dB
    SupplyCurrentMax = 285uA
  }
}
```

The `channel <Name> : <BundleType>` declaration is syntactically parallel to a bundle-typed terminal. The channel name becomes a namespace prefix in binding blocks:

```cascode
fill {
  OpAmp U1 = new OPA2376() {
    .A.INP--sensor_p        // Channel A, positive input
    .A.INN--ref             // Channel A, negative input
    .A.OUT--stage1_out      // Channel A, output
    .B.INP--vref_in         // Channel B, positive input
    .B.INN--vref_fb         // Channel B, negative input
    .B.OUT--vref_buffered   // Channel B, output
    .VDD--VDD
    .GND--GND
  }
}
```

### 7.3 Semantics

Each channel is an independent functional unit. The bundle type determines the channel's pin names and types. Power pins (`supply`, `ground`) are shared across channels and declared at the part level, not within channels.

Channel specs, if needed per-channel, can be supported as a future extension. For the initial proposal, specs are part-level (all channels share the same specifications, which matches reality for multi-channel parts from a single die).

---

## 8. Constraint Unification

The constraint system now supports two sources of truth through identical syntax:

1. **Bench measurements**: metrics computed from SPICE simulation of parts with models. Referenced as `bench_name::Measurement`.
2. **Part specifications**: guaranteed values from datasheets. Referenced as `specs::Property`.

Both use the `::` accessor and participate in the same `constraints { numeric { ... } }` block. The constraint evaluator checks bench measurements by running simulations and checks spec constraints by looking up the declared values.

In an HL composition:

```cascode
circuit SensorBoard {
  level HL

  slot {
    frontend = new AnalogFrontend() { ... }
    adc = new ADCStage() { ... }
    mcu = new Controller() { ... }
  }

  constraints {
    numeric {
      // Simulable: opamp frontend has SPICE model, bench produces these
      c_bw    = frontend.transfer_bench::LowpassBandwidth >= 10kHz
      c_gain  = frontend.transfer_bench::PassbandGain >= 40dB
      c_noise = frontend.noise_bench::IntegratedInputNoise(from=1Hz, to=10kHz) <= 1uVrms

      // Non-simulable: ADC specs from datasheet
      c_res  = adc.specs::Resolution >= 16 bits
      c_rate = adc.specs::MaxSampleRate >= 128 SPS

      // Non-simulable: MCU specs from datasheet
      c_flash = mcu.specs::FlashSize >= 64kB
    }
  }
}
```

This unification means the designer expresses all performance requirements in one place, regardless of whether they are validated by simulation or by datasheet lookup. The synthesis engine (for HL designs) uses both constraint types to guide part and topology selection.

---

## 9. Parts Database

This section sketches the parts database concept. The full schema and import pipeline are future work.

### 9.1 Role

The parts database is to PCB design what `pdk.db` is to IC design. It stores available components with their specifications, packages, pin mappings, SPICE model availability, cost, and stock information.

### 9.2 Population

A `part` library file (e.g., `lib/pcb/ti/opamps.cas`) declares part families using the `part` construct. The parts database backs these declarations with additional metadata not expressed in the language: current pricing, distributor stock levels, lifecycle status, alternative/equivalent parts, and full parametric data.

The database can be populated from multiple sources: manufacturer parametric search APIs (DigiKey, Mouser), distributor CSV exports (LCSC/JLCPCB), KiCad symbol libraries, or a curated internal database.

### 9.3 Passive Resolution

For parameterized passives like `part Resistor R_0402(real R)`, the declaration covers an entire family. The specific manufacturer part number (e.g., RC0402FR-0710KL for a 10kOhm 0402 1% resistor) is resolved by the toolchain at synthesis or emit time, querying the parts database for the cheapest available part matching the value, package, and tolerance constraints.

### 9.4 Part Selection During Synthesis

When an HL slot has spec constraints, the synthesis engine queries the parts database for components that satisfy all constraints. For ICs, this is a parametric search (find all ADCs with Resolution >= 16 bits, SupplyVoltageMin <= 3.3V, ...). For passive networks, synthesis selects topology and snaps values to standard series (E96 for 1% resistors, E24 for 5%), then resolves to specific part numbers.

---

## 10. PCB Synthesis Model

HL-to-EL synthesis for PCB design involves three distinct activities, depending on the block type:

**IC selection** (opamps, ADCs, MCUs, LDOs): the HL slot declares an interface with spec constraints. Synthesis queries the parts database for components meeting all constraints, then instantiates the selected part with its standard application circuit (bypass caps, pull-ups, etc.).

**Passive network design** (filters, gain stages, bias dividers): the HL slot declares bench-based constraints (bandwidth, gain, noise). Synthesis selects a topology (e.g., Sallen-Key vs. first-order RC for a lowpass filter), sizes component values to meet constraints, and snaps values to standard series. This is structurally similar to IC synthesis but with a discrete (non-continuous) parameter space.

**Mixed blocks** (opamp + feedback network): synthesis selects the active IC from the parts database based on spec constraints, then designs the surrounding passive network based on bench constraints. The SPICE model of the selected IC is used for bench simulation of the combined circuit.

The `synth {}` block, already defined for IC-level HL circuits, extends naturally to PCB:

```cascode
synth {
  seed = 42
  objective = minimize_cost
  passive_series = E96
}
```

The `passive_series` directive (new) tells the synthesis engine which standard value series to use when snapping passive component values.

---

## 11. Grammar Additions

The following ANTLR grammar rules are needed. They follow the existing grammar's conventions for labeled alternatives and rule structure.

### 11.1 New Lexer Tokens

```antlr
PART_KW     : 'part' ;
SPECS_KW    : 'specs' ;
CHANNEL_KW  : 'channel' ;
```

These are added to the keyword section and to the `idPart` rule so they can appear as identifiers in contexts where keywords are used as names.

### 11.2 Top-Level Declaration

Add `partDef` as an alternative in `topLevelDecl`:

```antlr
topLevelDecl
    : ...
    | partDef
    | ...
    ;
```

### 11.3 Part Declaration

```antlr
partDef
    : PART_KW category=IDENT name=IDENT (LPAREN paramList? RPAREN)? LBRACE partMember* RBRACE
    ;

partMember
    : deviceDirective                                       # PartDevice
    | SPICE_KW STRING                                       # PartSpice
    | PACKAGE_KW STRING                                     # PartPackage
    | paramsBlock                                           # PartParams
    | direction portName COLON portType                     # PartPort
    | SUPPLY_KW IDENT                                       # PartSupply
    | GROUND_KW IDENT                                       # PartGround
    | specsBlock                                            # PartSpecs
    | channelDecl                                           # PartChannel
    ;
```

Note: the category uses a generic `IDENT` rather than `DEVICE_TYPE` to support the open-ended set of PCB component categories without grammar changes per category. In device instantiation within `fill` blocks, parts are instantiated using the same `IDENT` path. The disambiguation between device and circuit instantiation at parse time may require a semantic pass (see Open Questions, Section 14).

Passive part declarations use `real` scalar parameters (e.g., `real R`) rather than `size`. The `size` type is reserved for transistor geometry (`W`, `L`, `M`) on IC primitives. The existing `paramList` grammar rule already supports both `size` and `real` parameter types, so no grammar changes are needed for this distinction.

### 11.4 Specs Block

```antlr
specsBlock
    : SPECS_KW LBRACE specEntry* RBRACE
    ;

specEntry
    : IDENT EQ specValue
    ;

specValue
    : signedQuantity
    ;
```

The `specValue` is a `signedQuantity` (already defined in the grammar for `MINUS? QUANTITY`). The use of `=` for the separator follows `env` block conventions. Additional value forms (ranges, enumerations) are deferred to future work.

### 11.5 Channel Declaration

```antlr
channelDecl
    : CHANNEL_KW name=IDENT COLON bundleType=IDENT
    ;
```

Channels reference a bundle type by name. The bundle must be defined elsewhere in scope. During instantiation, channel pins are accessed via dot notation (`instance.ChannelName.PinName`), using the existing bundle expansion mechanism.

### 11.6 Device Type Extension

The `DEVICE_TYPE` lexer token is extended with PCB categories:

```antlr
DEVICE_TYPE
    : 'NMOS' | 'PMOS' | 'Resistor' | 'Capacitor' | 'Inductor' | 'Diode'
    | 'OpAmp' | 'ADC' | 'DAC' | 'MCU' | 'LDO' | 'LED' | 'Connector'
    ;
```

This preserves the existing device instantiation grammar (`DEVICE_TYPE name = new ...`) while supporting PCB component instantiation. The list can be extended as new categories are needed.

---

## 12. Worked Example: Sensor Frontend PCB

A complete worked example accompanies this RFC at `tests/golden/cas/pcb/SensorFrontendPCB.cas`. The example describes a small PCB with:

- A Wheatstone bridge pressure sensor (external, modeled as a differential voltage source in benches).
- An analog signal conditioning stage using a dual opamp (OPA2376), demonstrating channel sub-bundles, bench-based constraints, and a SPICE-simulable path.
- A 16-bit I2C ADC (ADS1115) with spec-based constraints.
- An STM32G0 MCU communicating with the ADC via I2C and providing debug output via UART.
- I2C, UART, and SWD bus bundles.
- HL composition with unified bench + spec constraints.
- EL implementations of each functional block.

The example exercises every construct proposed in this RFC.

---

## 13. Implementation Plan

The implementation can proceed in phases. Each phase is independently useful and stays within the 400 LOC limit.

**Phase 1: Grammar and AST** (language layer only)
- Add lexer tokens (`part`, `specs`, `channel`) and parser rules to `Cascode.g4`.
- Add AST types (`PartDefinition`, `SpecsBlock`, `ChannelDeclaration`) to the language AST.
- Add AST builder methods to `CascodeAstBuilder.Core.cs`.
- Add writer methods to `CascodeWriter.cs` for round-trip fidelity.
- Regenerate ANTLR output.
- Tests: parse, round-trip, golden.

**Phase 2: Spec Constraint Evaluation** (language + bench runtime)
- Extend the constraint evaluator to resolve `specs::Property` references against `SpecsBlock` entries.
- Wire `specs` into HL composition constraint checking.
- Tests: constraint evaluation, HL linking with specs.

**Phase 3: Parts Database** (workspace layer)
- Define SQLite schema for parts (parallel to `pdk.db`).
- Implement import pipeline (KiCad symbol libraries, CSV/API sources).
- Implement part resolution for parameterized passives.
- Wire `part` device strings to the parts database for validation.

**Phase 4: Emission** (CLI layer)
- Extend SPICE emission to handle `part` declarations with `spice` directives.
- For parts with SPICE models: include manufacturer model files in emitted netlists.
- For bench generation: wire `part`-backed instances into the bench test circuit.

**Phase 5: Synthesis** (future)
- Part selection from database based on spec constraints.
- Passive network synthesis with standard series snapping.
- Mixed-block synthesis (IC selection + passive sizing).

---

## 14. Open Questions

The following design points require further discussion or prototyping before they can be settled.

**MPN field name.** The `device` field inside a `part` declaration serves the same purpose as in `primitive` (identifier for the backing database entry), but the backing stores are different. Alternatives considered: `mpn` (industry-standard "Manufacturer Part Number", unambiguous but domain-specific), `device` (unified with primitive, simple but overloaded), `partNumber` (explicit but verbose). This RFC uses `device` for consistency with `primitive`. The choice should be finalized during Phase 1 implementation.

**Simulation boundary propagation.** When a simulable block (opamp frontend) connects to a non-simulable block (ADC), the simulable block's bench needs the non-simulable block's input characteristics (impedance, capacitance). Currently, the designer manually sets these in the `env` block. A future extension could allow formal binding: `env { LoadImpedance = adc.specs::InputCapacitance }`. This RFC defers this to future work; manual propagation is sufficient and explicit.

**Category as grammar token vs. free identifier.** This RFC proposes a fixed set of `DEVICE_TYPE` tokens for device instantiation but a free `IDENT` for the `part` declaration header. This asymmetry means a category like `OpAmp` must be added to `DEVICE_TYPE` before parts of that category can be instantiated. An alternative is to make instantiation use free identifiers for the type prefix, with disambiguation handled semantically. This would require refactoring `deviceDecl` and `instanceDecl` into a single `instantiation` rule with semantic resolution.

**Spec composition and forwarding.** When a circuit wraps a part, specs must be re-declared at the circuit level for HL compositions to reference them. This is manual and redundant. A forwarding mechanism (`specs { Resolution = U_ADC.specs::Resolution }` or `specs forward U_ADC`) could reduce duplication. Deferred to a future RFC.

**Interface-level spec declarations.** Interfaces currently declare benches but not specs. Adding `specs` to interfaces would let an interface declare what spec properties a compliant implementation must provide, mirroring how bench bindings work. This is a natural extension but adds complexity. Deferred to a future RFC.

**Output format for PCB designs.** What does `cascode emit` produce for a PCB design? For the simulable analog path, SPICE netlist emission works as-is. For the complete board schematic, the target format (KiCad `.kicad_sch`, netlist, BOM CSV) is future work.

**Standard passive series in synthesis.** The `synth { passive_series = E96 }` directive is sketched but the integration with the synthesis engine is undefined. Value snapping, preferred-value selection, and handling of parallel/series combinations for exact targets are implementation details.

**Power distribution patterns.** Every IC on a PCB needs bypass capacitors, often in standard patterns (100nF ceramic + 10uF bulk per supply pin). Whether the language should provide shorthand for this boilerplate or leave it to manual `fill` block wiring is an open question. A `decoupling` block or bypass capacitor annotation on supply pins could reduce verbosity.

**Connector and mechanical parts.** Physical connectors (pin headers, USB receptacles, barrel jacks) define the board's external interface. They have pins and footprints but no active electrical function. They fit naturally as parts with `Connector` category and `io` terminals. The example in this RFC does not cover connectors; a follow-up should address them.

**Multi-interface composition at slot sites.** The `implements` clause on circuits already accepts comma-separated interfaces (`implements InterfaceA, InterfaceB`), but slot instantiation (`new InterfaceName()`) accepts only a single type name. A PCB part may satisfy multiple interface contracts simultaneously (e.g., an ADC with a fully differential input stage and an SPI digital output). Expressing this requirement at the slot site needs a composition mechanism. Precedents from other languages: Rust uses trait bounds with `+`, Swift and TypeScript use protocol/type composition with `&`, Go embeds interfaces within interfaces, Scala uses `with` for mixin composition, and Haskell uses constraint tuples. The right design for Cascode is deferred to a future RFC.

---

## References

- RFC-0000: Cascode Language Unification and Declarative Bench System
- RFC-0002: ACIR Terminal Directionality
- RFC-0003: ACIR Syntax Overhaul
- `spec/language/Ch01_Introduction.md` through `Ch04_BenchSystem.md`
- `lib/std/prim/Devices.cas`, `lib/std/prim/Passives.cas`: existing primitive patterns. Note: `Passives.cas` currently uses `size` for passive parameters, which is appropriate for IC primitives where on-die passives have physical geometry. PCB `part` declarations should use `real` scalars instead. Additionally, the inductor in that file is declared with category `Capacitor` and device `"capacitor"`, which is a bug to be fixed separately.
- `lib/std/amp/SingleEndedOpAmp.cas`: interface with bench bindings pattern
- `tests/golden/cas/stress/OTA5T_Sky130.cas`: reference for golden file conventions
