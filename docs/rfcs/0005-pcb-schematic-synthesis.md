# RFC-0005: PCB Schematic Representation and Synthesis

| Field | Value |
|-------|-------|
| Status | Draft |
| Authors | Daniel Lovell |
| Created | 2026-02-06 |
| Last Updated | 2026-02-17 |

---

## Abstract

Cascode's abstractions for IC design (bundles, interfaces, circuits, primitives, benches, constraints, HL/EL levels) map naturally to PCB schematic capture and synthesis. This RFC extends the language with a `part` construct for packaged off-the-shelf components, a metrics system for datasheet-driven and simulation-driven validation, and bus bundles for digital interconnect. Parts use a catalog model: each part defines one or more concrete catalog entries (MPN + footprint + pin map) and may use variant axes as a compact way to generate entries. Part inheritance (`extends`/`abstract`) shares electrical identity across related families. A bracket syntax at the instantiation site (`new Part[Entry](params)` or `new Part[axis=option](params)`) visually distinguishes selection from constructor parameters, ensuring BOM-readiness is explicit. These additions enable Cascode to represent PCB schematics at both high-level (system architecture with constraint-driven part selection) and electrical level (concrete schematic with specific component values and connections).

This RFC defines Tier 1 coverage: schematic capture, synthesis intent, mixed bench/spec verification, and emission targets (SPICE netlists and BOM JSON). Procurement depth and physical-layout intent are intentionally bounded and advanced in follow-on stages.

A unifying insight drives the design: in both IC and PCB flows, the schematic symbol (pin contract and behavioral requirements) is separate from the concrete backing (a PDK device or a sourced component). This RFC formalizes that separation by making both `primitive` and the new `part` declare which interfaces they satisfy via `implements`. Reserved keyword categories (`NMOS`, `Resistor`, etc.) become library-defined interfaces in domain-specific libraries (`lib/ic/`, `lib/pcb/`), making the component taxonomy extensible without grammar changes.

---

## 1. Motivation

PCB schematic capture today uses GUI tools (KiCad, Altium, OrCAD) that, like analog IC schematics, obscure design intent. A sensor frontend board requires specific noise, bandwidth, and accuracy constraints that live in the designer's head and separate notes but are not usually captured alongside the schematic. When a component goes end-of-life, the designer must manually verify whether a replacement part still meets original constraints spread across documents and datasheets.

Cascode's constraint-driven, multi-level abstraction model addresses this gap. An HL design expresses system requirements (for example, the analog frontend must achieve 40 dB gain with less than 1 uVrms integrated noise). An EL design captures concrete schematic structure with explicit components and connections. Bench execution validates simulable blocks (for example, op-amp paths with SPICE models) through `bench` constraints, while datasheet-backed metrics validate non-simulable blocks (for example, ADC and MCU capability constraints) through `spec` constraints. Both share the same constraint language with distinct sub-blocks that make verification provenance explicit.

A secondary motivation is passive-network synthesis. PCB designs contain filters, bias dividers, gain-setting networks, and decoupling structures that involve topology and discrete value selection. These can use the same synthesis framework used for IC circuits, with additional constraints that values snap to standard series and resolvable parts exist.

---

## 2. Goals and Non-Goals

Goals:

1. Define Tier 1 PCB coverage in Cascode: complete schematic capture at HL and EL, constraint-driven synthesis intent, mixed bench/spec verification, and deterministic BOM + SPICE emission.
2. Introduce a `part` construct for packaged components, parallel to `primitive`, both using `implements` to satisfy interface contracts.
3. Unify the component taxonomy: replace reserved keyword categories (`NMOS`, `Resistor`, etc.) with library-defined interfaces in `lib/ic/` and `lib/pcb/`.
4. Enable constraint-driven part selection using guaranteed datasheet metrics (`spec` constraints) alongside existing bench-derived metrics (`bench` constraints), with verification provenance explicit in the syntax.
5. Support standard PCB signal buses (I2C, SPI, UART, SWD) as first-class bundle types.
6. Add mandatory pin-map contracts on concrete parts and unit grouping support for multi-unit packages.
7. Add corner-scoped metric declarations (`min|max|typ` qualifiers + `corners`/`at`) while keeping forwarding and provenance explicit.
8. Provide a worked example (Wheatstone bridge sensor frontend) that stress-tests the proposed constructs.

Non-goals:

1. PCB layout, physical placement, pick-and-place data, or gerber generation. This RFC addresses schematic capture and synthesis intent only. BOM generation is in scope as a first-class emit target alongside SPICE netlists; Cascode replaces KiCad/Altium for schematic capture and does not emit artifacts in their formats.
2. Defining the complete external parts sync pipeline implementation. This RFC defines language-facing contracts and pointers.
3. Solving MCU alternate-function/pin-mux firmware configuration in-language.

Coverage envelope:

- Tier 1 (this RFC): schematic DSL, part modeling, synthesis intent, bench/spec verification, BOM + SPICE emission.
- Tier 2 (follow-on): deeper procurement-aware synthesis policy.
- Tier 3 (follow-on): richer PCB physical-intent handoff to downstream layout flows.

---

## 3. Conceptual Mapping

The table below summarizes how existing IC Cascode concepts translate to PCB design under the unified `implements` model.

| IC Cascode | PCB Cascode | Notes |
|---|---|---|
| `primitive nfet(size s) implements NMOS` | `part OPA2376 implements DualOpAmp` | Both use `implements` to satisfy an interface contract |
| `lib/ic/` interfaces (`NMOS`, `Resistor`, ...) | `lib/pcb/` interfaces (`NMOS`, `DualOpAmp`, `ADC`, ...) | Domain-specific interface libraries; shared interfaces (`SingleEndedOpAmp`, bus bundles) live in `lib/std/` |
| PDK (`pdk scan`) | Parts library + external pricing/availability sources | Source of available components and their operating/procurement attributes |
| `size(W=2u, L=180n, M=1)` | E-series params for passives (`e96 R`, `e12 C`); no value params for fixed-identity ICs | `size` remains reserved for transistor geometry on primitives |
| `device "sky130_fd_pr__nfet_01v8"` | `catalog { entry VSSOP8 { mpn = "OPA2376AIDDBVR" ... } }` | `device` directive for primitives; `catalog` block for parts; parts may use variant axes and `extends` for families |
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

A note on token naming: the existing `PACKAGE_KW` token (bound to the keyword `library`) is renamed to `LIBRARY_KW` to better reflect its purpose. Source files continue to use `library` for library declarations — only the internal token name changes. The word `package` does not appear as a keyword — `catalog` is used for the outer container block on part declarations, and `footprint` names the land-pattern field within entries. No `PACKAGE_KW` token is needed.

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

`Resistor` appears in both IC and PCB libraries with domain-appropriate contracts. The IC version declares only terminals; the PCB version adds metric requirements for tolerance and power rating. These are separate interfaces in separate namespaces. A source file may include `lib.ic`, `lib.pcb`, or both. When both are included, any ambiguous symbol reference must be fully qualified; unresolved or ambiguous references are hard validation errors.

Concrete part declarations live in a separate `lib/parts/` tree, organized by category (`lib.parts.opamp`, `lib.parts.adc`, `lib.parts.res`, etc.). Including `lib.pcb` brings interface definitions into scope without pulling in the entire parts catalog.

### 4.3 Primitive Syntax Change

The existing primitive syntax uses reserved keyword categories:

```cascode
primitive nfet_01v8(size s) implements NMOS { device "sky130_fd_pr__nfet_01v8" params { ... } }
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
  catalog {
    entry MSOP10 {
      mpn = "ADS1115IDGSR"
      ...
    }
  }
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

`Some` directs the solver to infer the interface from the instantiated circuit's own `implements` declaration. A circuit declared as `circuit AnalogFrontend implements SensorConditioner` already states its interface contract; `Some` lets the slot omit the type annotation when the interface is unambiguous from the target.

`Some` is enforced at the grammar level by using separate instance declaration rules for slot and fill blocks.

### 4.6 Selecting by Capability (MCUs with SPI + I2C)

HL selection often needs “and” requirements: an MCU that exposes both SPI and I2C, plus datasheet constraints like minimum core clock and RAM size. Rather than relying on intersection types at the instantiation site, this RFC composes the requirement into a single interface.

Interface inheritance composes smaller capability interfaces:

```cascode
interface MicrocontrollerCore {
  supply VDD
  ground GND
  metrics {
    CoreClock : Hz
    RAMSize : B
  }
}

interface HasI2C {
  io I2C1 : I2C
}

interface HasSPI {
  io SPI1 : SPI
}

interface MCURequired extends MicrocontrollerCore, HasI2C, HasSPI { }
```

A slot block can then request `MCURequired` directly and constrain its metrics:

```cascode
slot {
  net i2c_bus : I2C
  net spi_bus : SPI

  MCURequired mcu = new MCURequired() {
    .VDD--VDD_3V3
    .GND--GND
    .I2C1--i2c_bus
    .SPI1--spi_bus
  }
}

constraints {
  spec {
    c_clock = mcu.CoreClock >= 25MHz
    c_ram = mcu.RAMSize >= 256kB
  }
}
```

The EL boundary remains explicit. HL wires logical bus bundles; EL wrappers around concrete part entries are where pin assignment and pin-mux decisions are expressed.

---

## 5. The `part` Construct

### 5.1 Syntax

A `part` declaration is a new top-level construct, parallel to `primitive`. It declares a packaged component that implements one or more interfaces and can be resolved to one or more concrete orderables.

A part has two layers of identity:

- Electrical identity: the schematic symbol contract (terminals, directions, and required metrics) expressed through `implements`.
- Physical/procurement identity: a set of concrete catalog entries, each of which corresponds to a BOM-resolvable configuration (MPN, footprint, procurement pointers) and carries a pin-map contract.

This RFC uses the term entry deliberately. Variant axes do not introduce a competing notion of identity. They are a compact authoring form that generates entries; after validation, tools expand variants into an explicit entry set.

```
abstract? part <Name> (<params>?)
    (extends <Parent> (<parent_args>)?)?
    (implements <Interface>(, <Interface>)*)?
{
  <terminal declarations>

  params { ... }?

  catalog {
    defaults { <entry_member>* }?

    // Explicit entries (small enumerations like package options):
    entry <EntryName> { <entry_member>* }+

    // Or variant axes (shorthand that generates entries via a Cartesian product):
    variant <axis> { <option> { <entry_member>* } ... }+
  }
}
```

A part must have either `extends`, `implements`, or both. Parts with `extends` implicitly inherit the `implements` conformance of their base. The `abstract` keyword marks a base part that cannot be directly instantiated.

Three mechanisms address three separate concerns:

1. Constructor parameters (`e96 R`, `e12 C`): discrete passive component values constrained to a preferred number series. Continuous parameters (`real`) remain available for non-discrete values. Passed in parentheses at instantiation.
2. Catalog entries (explicit `entry` blocks or generated via `variant` axes): discrete, BOM-resolvable configurations. Selected in square brackets at instantiation.
3. Part inheritance (`extends`): shared electrical identity and shared entry defaults across related families. Declared at the part level.

Passive `part` declarations use E-series parameters (`e96 R`, `e12 C`, `e24 L`). The series constrains the parameter to values in the named IEC 60063 preferred number set. Continuous `real` parameters remain valid for non-discrete values. `size` remains reserved for primitive geometry.

Terminal declarations are part identity; catalog entries are the backing. The entry layer is introduced by the `catalog {}` block.

A `catalog {}` block contains:

- `defaults {}`: shared entry members (for example, a SPICE model reference, a shared pin map, or shared metrics).
- `entry <Name> { ... }`: an explicit, named entry used for small enumerations (for example, the same IC in two packages).
- `variant <axis> { ... }`: an axis definition used to generate entries. Tools expand the Cartesian product of axis options into concrete entries.

Entry members describe the concrete backing. Sourcing fields (`mpn`, `footprint`, `spice`) and procurement `option` entries are direct members of an entry (or defaults) — there is no inner grouping block. The full set of entry members:

- `mpn`: part lookup and traceability, may be a template string with `{...}` references.
- `footprint`: physical land pattern.
- `spice` (optional): model reference for simulable parts.
- `option { ... }`: zero or more procurement pointer entries. Each `option` must contain `provider`, `sku`, and `priority` fields; `url` is optional.
- `pins {}`: terminal-to-pad mapping contract for netlist/symbol correctness.
- `units {}` (optional): named groups for multi-unit packages. A unit defines the subset of pads and terminals that form one logical sub-unit (for example, op-amp A and op-amp B in a dual package).
- `metrics {}`: guaranteed datasheet values and part attributes (including entry-dependent overrides).
- `mechanical {}` (optional): physical geometry and assembly data. Contains `step` (path or URL to a STEP 3D model file), `dimensions` (W × L × H tuple, e.g., `dimensions = (5.0mm, 3.2mm, 1.5mm)`), `mass` (component mass, e.g., `mass = 0.5g`), and `mating_force` (insertion force for connectors, e.g., `mating_force = 30N`). All fields are optional. Keep-out zones are layout concerns and are out of scope.

The catalog field previously named `package` is renamed to `footprint`. This avoids confusion with variant axis naming and better describes the field's purpose — it identifies the physical land pattern, not a software package.

Pin mapping requirements:

- Every concrete (non-abstract) part MUST resolve to at least one concrete entry.
- Every concrete entry MUST include an effective `pins {}` mapping (either directly or via inherited/merged defaults).
- Every declared terminal leaf MUST map to one or more pads in the effective entry.
- A pad MUST NOT be mapped to conflicting terminal leaves in the same effective entry.
- Inherited or defaulted pin maps may be extended or overridden only when the resulting mapping remains complete and conflict-free.

Pin maps support a shorthand for contiguous array terminals:

```cascode
pins {
  PA[0:15] = P6..P21
  PB[0:9]  = P22..P31
}
```

This form expands to the obvious element-wise mapping and is valid only when both ranges are increasing and of equal length. Pad ranges are defined only for pad references with a shared prefix and an integer suffix (for example, `P1` through `P48`).

Unit grouping requirements:

- `units {}` is optional for single-unit packages.
- For multi-unit packages, `units {}` SHOULD be present and SHOULD partition logical sub-units explicitly.
- If `units {}` is present, every listed terminal/pad reference MUST be valid in the effective entry and MUST agree with `pins {}`.

### 5.2 Catalog Entries and Variant Axes

A catalog entry is the unit of BOM identity. It is what the resolver groups by when emitting a BOM (after MPN template resolution). A part describes its entry set inside `catalog {}`.

For small enumerations (for example, "this IC in two packages"), use explicit `entry <Name> { ... }` blocks. Shared backing (metrics, a pin map, a SPICE model reference) belongs in `defaults {}`.

```cascode
part OPA2376 implements DualOpAmp {
  input A_INP : analog
  input A_INN : analog
  output A_OUT : analog
  input B_INP : analog
  input B_INN : analog
  output B_OUT : analog
  supply VDD
  ground GND

  catalog {
    defaults {
      spice = "OPA2376"
      metrics {
        GBW = 5.5MHz
        InputOffsetVoltage = 25uV
        CMRR = 90dB
      }
      pins { /* omitted */ }
    }

    entry VSSOP8 {
      mpn = "OPA2376AIDDBVR"
      footprint = "VSSOP-8"
      option { provider = "DigiKey" sku = "296-28003-1-ND" priority = 10 }
      option { provider = "Mouser" sku = "595-OPA2376AIDDBVR" priority = 20 }
    }

    entry SOIC8 {
      mpn = "OPA2376AIDGKR"
      footprint = "SOIC-8"
      option { provider = "DigiKey" sku = "296-28004-1-ND" priority = 10 }
      option { provider = "Mouser" sku = "595-OPA2376AIDGKR" priority = 20 }
    }
  }
}
```

When a part has more than one entry, fill-block instantiation must select one explicitly (Section 5.7). In slot blocks, selection may be omitted and deferred to synthesis.

For structured search spaces, use `variant <axis> { ... }` blocks. Each axis option may provide sourcing field overrides (`mpn`, `footprint`, `spice`), procurement entries (`option { ... }`), metric overrides (`metrics { ... }`), arbitrary metadata fields (`key = value` pairs accessible via `{axis.key}` in MPN templates), and exclusion directives.

Not all combinations of variant options across axes produce valid entries. An option may declare `exclude <axis>=<option>` to mark a specific cross-axis combination as invalid. The validator rejects instantiations and synthesis candidates that select excluded combinations.

```cascode
variant body {
  _0402 {
    footprint = "0402"
    metrics { PowerRating = 63mW }
  }
  _0805 {
    footprint = "0805"
    metrics { PowerRating = 125mW }
    exclude grade=J
  }
}
```

In this example, the combination `[body=_0805, grade=J]` is invalid. A fill block selecting that combination is a validation error; during synthesis, the combination is excluded from the candidate search space.

The effective entry backing is computed by merging `defaults` with all selected axis option overrides. Variant fields override defaults of the same name. If two selected options both provide the same scalar sourcing field (e.g., both provide `mpn`), it is a validation error — the field should be a template in shared defaults that references both axes.

Multi-axis parts declare multiple independent variant blocks. The following passive family has body size and tolerance grade as separate axes:

```cascode
part YageoRC(e96 R) implements Resistor {
  io P : analog
  io N : analog
  params { R = R }

  catalog {
    defaults {
      mpn = "RC{body.footprint}{grade.code}{R}L"
    }

    variant body {
      _0402 {
        footprint = "0402"
        metrics { PowerRating = 63mW  VoltageRating = 50V }
        option { provider = "DigiKey" sku = "311-{R}LRCT-ND" priority = 10 }
      }
      _0603 {
        footprint = "0603"
        metrics { PowerRating = 100mW  VoltageRating = 75V }
        option { provider = "DigiKey" sku = "311-{R}HRCT-ND" priority = 10 }
      }
      _0805 {
        footprint = "0805"
        metrics { PowerRating = 125mW  VoltageRating = 150V }
        option { provider = "DigiKey" sku = "311-{R}GRCT-ND" priority = 10 }
      }
    }

    variant grade {
      F { code = "FR-07"  metrics { Tolerance = 1pct } }
      J { code = "JR-05"  metrics { Tolerance = 5pct } }
    }
  }
}
```

### 5.3 E-Series Parameter Types

Passive component values (resistance, capacitance, inductance) are not continuous — they come from discrete preferred number series defined by IEC 60063. E-series types make this constraint explicit in the parameter type system, catching invalid values at compile time rather than deferring to the parts resolver.

Six E-series types are supported:

| Type | Values per decade | Typical tolerance | Example values (one decade) |
|------|------------------|-------------------|-----------------------------|
| `e6` | 6 | 20% | 1.0, 1.5, 2.2, 3.3, 4.7, 6.8 |
| `e12` | 12 | 10% | adds 1.2, 1.8, 2.7, 3.9, 5.6, 8.2 |
| `e24` | 24 | 5% | adds 1.1, 1.3, 1.6, 2.0, 2.4, 3.0, 3.6, 4.3, 5.1, 6.2, 7.5, 9.1 |
| `e48` | 48 | 2% | finer subdivision |
| `e96` | 96 | 1% | finer subdivision |
| `e192` | 192 | 0.5% / 0.25% / 0.1% | finest standard subdivision |

The types form a subtype hierarchy: e6 ⊂ e12 ⊂ e24 ⊂ e48 ⊂ e96 ⊂ e192. Every value in a coarser series is also a member of every finer series. A 10kΩ value is E6 and therefore valid as E12, E24, E96, or any finer series.

A value of type `eN` must be a member of the E-N series at some decade. The validator rejects non-member values as compile-time errors. For instance, declaring `e12 C` and passing `C=15n` is valid (15 is an E12 value), while passing `C=13n` would be rejected (13 is not in E12).

By convention, the type on a part's constructor parameter reflects the finest series the part family supports. A resistor family available in 1% tolerance uses `e96 R`; an MLCC family with standard E12 values uses `e12 C`. Coarser-series values are always valid when passed to a finer-series parameter — passing an E24 value to an `e96` parameter is accepted, since e24 ⊂ e96.

E-series types apply uniformly to resistance, capacitance, and inductance. The series defines preferred numbers, not component type; the same `e24` type constrains 10kΩ resistors and 100nF capacitors alike. The `real` type remains valid for circuit-level parameters that are not discrete component values (e.g., `real load_cap=10pF` on a circuit, which is an environmental parameter, not an orderable value).

Tolerance-grade entries may further narrow the available series at resolution time. A part declared with `e96 R` supports values up to E96, but a tolerance-grade entry selecting 5% tolerance may only manufacture E24 values. This series-tolerance interaction is a resolver concern: the language-level type catches the broadest class of errors (non-standard values), while the parts resolver validates that the specific value exists at the selected tolerance grade.

### 5.4 Part Inheritance

Parts support `extends` for sharing electrical identity and shared entry defaults. The `abstract` keyword marks a base part that cannot be directly instantiated. Concrete parts inherit terminals, constructor parameters, interface conformance, and the `catalog.defaults` backing layer from their base.

```cascode
abstract part STM32G0 implements IMicrocontroller {
  supply VDD
  supply VDDA
  ground VSS
  input NRST : digital
  input BOOT0 : digital

  catalog {
    defaults {
      metrics {
        SupplyVoltage min = 1.7V
        SupplyVoltage max = 3.6V
      }
    }
  }
}

part STM32G031K extends STM32G0 {
  io PA[0:15] : digital
  io PB[0:9] : digital

  catalog {
    defaults {
      mpn = "STM32G031K{flash.code}{pkg.suffix}"
      option { provider = "DigiKey" sku = "497-STM32G031K{flash.code}{pkg.suffix}-ND" priority = 10 }

      metrics {
        CoreClock = 64MHz
        SupplyCurrent max = 10mA
      }
    }

    variant flash {
      _8 { code = "8"  metrics { FlashSize = 64kB  RAMSize = 8kB } }
      B  { code = "B"  metrics { FlashSize = 128kB  RAMSize = 16kB } }
    }

    variant pkg {
      LQFP32   { footprint = "LQFP-32"  suffix = "T6" }
      UFQFPN32 { footprint = "UFQFPN-32"  suffix = "U6" }
    }
  }
}
```

Instantiation: `new STM32G031K[flash=_8, pkg=LQFP32]()` resolves `mpn` to `"STM32G031K8T6"`.

Inheritance rules:

- A part with `extends` inherits terminals, constructor parameters, `implements` conformance, and the `catalog.defaults` layer from the base chain.
- An extending part may add terminals, entry members, explicit entries, and variant axes.
- An extending part may override inherited entry members by providing the same member kind in its own defaults, entries, or selected variant option.
- An extending part may add interface conformance: `part Foo extends Bar implements ExtraInterface`.
- `abstract` parts cannot appear in `fill {}` blocks or be instantiated directly.
- The effective entry backing is: inherited defaults, overridden by own defaults, overridden by the selected entry or selected variant options.

The designer chooses the boundary between inheritance and variants based on what the synthesizer should search. Variant axes are synthesis degrees of freedom: a single declaration with `variant body { _0402, _0603 }` lets the synthesizer explore both body sizes within one candidate. Separate declarations via `extends` are independent synthesis candidates: they appear independently, and the synthesizer does not assume they share a base. Both patterns produce the same BOM output (fully resolved entries); the difference is in search-space structure.

### 5.5 MPN Template Strings

The `mpn` field (and `sku` fields in option entries) can contain `{...}` interpolation references. Parsing remains string-literal based, but reference validation is split across compile time and resolution time:

- Compile-time validation MUST verify that every placeholder reference is well-formed and resolvable by name (`axis`, `axis.field`, or `param` exists in the entry-generation context).
- Resolution-time logic computes encoded values and concrete strings for a selected configuration.

The parts resolver interprets placeholders as:

- `{axis}` resolves to the selected option's name as a string (e.g., `{flash}` → `"_8"` or `"B"`).
- `{axis.field}` resolves to a named field on the selected option (e.g., `{pkg.suffix}` → `"T6"`, `{grade.code}` → `"FR-07"`).
- `{param}` resolves to the constructor parameter value, with encoding handled by the parts resolver (e.g., `{R}` for 10 kOhm → resolver applies RKM encoding to produce `"10K"`).

No language-level expression evaluation is supported inside templates. Placeholder payloads are identifiers only, not expressions.

Example resolution:

```
Instance: rDiv1 = new YageoRC[body=_0402, grade=F](R=100k)

  mpn template: "RC{body.footprint}{grade.code}{R}L"
    body.footprint → "0402"
    grade.code     → "FR-07"
    R              → "100K"  (tooling RKM encoding)
  Resolved MPN: "RC0402FR-07100KL"

  footprint: "0402" (from body._0402)
  PowerRating: 63mW (from body._0402.metrics)
  Tolerance: 1pct (from grade.F.metrics)

BOM line: RC0402FR-07100KL | 0402 | 1 | 100kΩ 1% | 311-100KLRCT-ND
```

### 5.6 Examples

The examples in this section focus on catalog/variant/metric shape and may omit `pins {}` and `units {}` blocks for brevity. Normative requirements in Section 5.1 still apply to concrete part declarations.

A dual op-amp with two explicit entries (two package options sharing all other attributes):

```cascode
part OPA2376 implements DualOpAmp {
  input A_INP : analog
  input A_INN : analog
  output A_OUT : analog
  input B_INP : analog
  input B_INN : analog
  output B_OUT : analog
  supply VDD
  ground GND

  catalog {
    defaults {
      spice = "OPA2376"
      metrics {
        GBW = 5.5MHz
        InputOffsetVoltage = 25uV
        CMRR = 90dB
        InputBiasCurrent = 10pA
        SlewRate = 2V/us
        SupplyVoltage min = 2.2V
        SupplyVoltage max = 5.5V
        SupplyCurrent max = 950uA
        RecommendedLoadCapacitance = 100pF
      }
    }

    entry VSSOP8 {
      mpn = "OPA2376AIDDBVR"
      footprint = "VSSOP-8"
      option { provider = "DigiKey" sku = "296-28003-1-ND" priority = 10 }
      option { provider = "Mouser" sku = "595-OPA2376AIDDBVR" priority = 20 }
    }

    entry SOIC8 {
      mpn = "OPA2376AIDGKR"
      footprint = "SOIC-8"
      option { provider = "DigiKey" sku = "296-28004-1-ND" priority = 10 }
      option { provider = "Mouser" sku = "595-OPA2376AIDGKR" priority = 20 }
    }
  }
}
```

Multi-unit ICs like dual op-amps use flat port naming with per-port direction qualifiers. Each port carries its own `input`, `output`, or `io` direction, preserving signal flow information that a bundle grouping cannot express. The entries carry package-dependent MPN/footprint/procurement pointers; shared metrics and simulation metadata live in `defaults`.

A 16-bit I2C ADC with a single entry:

```cascode
part ADS1115 implements ADCSubsystem {
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

  catalog {
    entry MSOP10 {
      mpn = "ADS1115IDGSR"
      footprint = "MSOP-10"
      option { provider = "DigiKey" sku = "296-38714-1-ND" priority = 10 }
      option { provider = "Mouser" sku = "595-ADS1115IDGSR" priority = 20 }

      metrics {
        Resolution = 16 bits
        MaxSampleRate = 860 SPS
        INL = 0.5 LSB
        FullScaleRange = 6.144V
        SupplyVoltage min = 2.0V
        SupplyVoltage max = 5.5V
        SupplyCurrent max = 200uA
        InputCapacitance = 14pF
      }
    }
  }
}
```

An MCU family using inheritance and variants together:

```cascode
abstract part STM32G0 implements IMicrocontroller {
  supply VDD
  supply VDDA
  ground VSS
  input NRST : digital
  input BOOT0 : digital
  catalog {
    defaults {
      metrics {
        SupplyVoltage min = 1.7V
        SupplyVoltage max = 3.6V
      }
    }
  }
}

part STM32G031K extends STM32G0 {
  io PA[0:15] : digital
  io PB[0:9] : digital

  catalog {
    defaults {
      mpn = "STM32G031K{flash.code}{pkg.suffix}"
      option { provider = "DigiKey" sku = "497-STM32G031K{flash.code}{pkg.suffix}-ND" priority = 10 }
      metrics {
        CoreClock = 64MHz
        SupplyCurrent max = 10mA
      }
    }
    variant flash {
      _8 { code = "8"  metrics { FlashSize = 64kB  RAMSize = 8kB } }
      B  { code = "B"  metrics { FlashSize = 128kB  RAMSize = 16kB } }
    }
    variant pkg {
      LQFP32   { footprint = "LQFP-32"  suffix = "T6" }
      UFQFPN32 { footprint = "UFQFPN-32"  suffix = "U6" }
    }
  }
}
```

A USB Type-C connector with mechanical metadata:

```cascode
part USB_C_Receptacle implements USBConnector {
  io VBUS : power
  io CC1 : analog
  io CC2 : analog
  io DP1 : digital
  io DN1 : digital
  io DP2 : digital
  io DN2 : digital
  io SBU1 : analog
  io SBU2 : analog
  ground GND

  catalog {
    entry USB4105 {
      mpn = "USB4105-GF-A"
      footprint = "USB-C-SMD-16P"
      option { provider = "DigiKey" sku = "2073-USB4105-GF-ACT-ND" priority = 10 }
      option { provider = "Mouser" sku = "640-USB4105-GF-A" priority = 20 }

      mechanical {
        step = "models/USB4105-GF-A.step"
        dimensions = (8.94mm, 7.30mm, 3.26mm)
        mass = 1.2g
        mating_force = 8N
      }
    }
  }
}
```

### 5.7 Instantiation

Parts are instantiated using a three-delimiter syntax that distinguishes selection from constructor parameters from connectivity:

```
<Interface> <refdes> = new <Part>[<selection>](<params>) { <bindings> }
```

Square brackets for configuration, parentheses for values, braces for connectivity:

```cascode
fill {
  // Explicit entry: positional selection by entry name
  DualOpAmp u1 = new OPA2376[VSSOP8]() {
    .A_INP--sensor_p
    .A_INN--ref
    .A_OUT--stage1_out
    .VDD--VDD
    .GND--GND
  }

  // Variant axes: named selection + value param
  Resistor r1 = new YageoRC[body=_0402, grade=F](R=10k) {
    .P--VDD
    .N--vref
  }

  // Single-entry part: brackets omitted
  ADCSubsystem uAdc = new ADS1115() { ... }

  // Inherited part with variants: all axes selected
  IMicrocontroller uMcu = new STM32G031K[flash=_8, pkg=LQFP32]() { ... }
}
```

Bare positional `[X]` selects an explicit `entry` by name only. Variant axes always require named form `[axis=option]`, even for single-axis parts. If a part declares both explicit entries and variant axes, positional selection resolves against entry names; a positional argument that does not match any declared entry name is a validation error.

In `fill {}` blocks, selection must be complete — this is the BOM-readiness guarantee. For explicit-entry parts with more than one entry, the entry must be specified. For variant-generated entries, all axes must be explicitly selected. Omitting a required selection in a fill block is a validation error. In `slot {}` blocks, selection may be omitted entirely; omitted axes are deferred to synthesis.

IC primitive instantiation follows the same pattern — the declared type is the interface, not a reserved keyword:

```cascode
fill {
  NMOS m1 = new nfet_01v8(size(W=2u, L=180n, M=1, NF=1)) { ... }
  PMOS m2 = new pfet_01v8(size(W=2u, L=180n, M=1, NF=1)) { ... }
}
```

### 5.8 Relationship to `primitive`

`primitive` and `part` are siblings, not parent-child. Both use `implements` to satisfy interface contracts. The difference is in backing:

- `primitive` declarations carry a `device` directive referencing a simulator model from a foundry PDK.
- `part` declarations carry one or more catalog entries with sourcing identity (`mpn`, `footprint`, optional `spice`) and procurement pointers.

The `extends`/`abstract` mechanism and variant blocks apply to parts only. Primitives continue to use `implements` without inheritance — PDK devices do not form part families in the same way as sourced components. A Cascode project may contain both primitives and parts when modeling mixed IC + PCB systems.

### 5.9 Resolution Policy

All instantiation targets resolve semantically against declarations in scope (`circuit`, `interface`, `part`, `primitive`). There are no reserved keyword categories. The `include` directives determine which interfaces are available. Ambiguous or unresolved targets are hard validation errors.

When both IC and PCB domain libraries are in scope, ambiguous short names must be replaced by fully-qualified references. Name ambiguity is never resolved by heuristic preference.

---

## 6. The Metrics System

### 6.1 Semantics

A `metrics` block on `part` or `circuit` declares scalar values with explicit statistic qualifiers and optional corner scoping.

Statistic qualifiers:

- `min` for guaranteed lower bounds.
- `max` for guaranteed upper bounds.
- `typ` for nominal values.

Corner scoping:

- A corner is a named operating context (for example, a supply + temperature point).
- Corners are declared with a `corners { ... }` metadata block.
- A `metrics {}` block may contain `at <CornerName> { ... }` sub-blocks; entries inside apply only at that corner.
- Unscoped entries are interpreted as defaults for all corners.

Alignment with harness PVT:

- `harness { pvt <CornerName> }` selects an active corner for execution.
- Tier 1 tools use the active corner when evaluating `spec {}` constraints against declared metrics. If no corner is selected, tools use unscoped values.

This RFC does not introduce full matrix algebra across all operating dimensions. It introduces named corners and explicit qualifiers so datasheet intent remains machine-readable without requiring a full corner-solver in Tier 1.

### 6.2 Syntax

Within a part declaration:

```cascode
corners {
  Room_3V3_25C { TemperatureC = 25  Supply = 3.3V }
  Hot_3V3_85C  { TemperatureC = 85  Supply = 3.3V }
}

metrics {
  SupplyVoltage min = 2.2V
  SupplyVoltage max = 5.5V

  at Room_3V3_25C {
    GBW min = 5.5MHz
    GBW = 7MHz
    InputOffsetVoltage max = 25uV
    CMRR min = 90dB
    SupplyCurrent max = 950uA
  }
}
```

Each entry is `<Identifier> <qualifier>? = <metricValue>`. When omitted, the qualifier is `typ`.

Corner declarations are optional metadata blocks on parts/circuits:

```cascode
corners {
  Room_3V3_25C { TemperatureC = 25  Supply = 3.3V }
  Hot_3V3_85C  { TemperatureC = 85  Supply = 3.3V }
}
```

Corner contents are declaration metadata. Tier 1 tools treat corner values as labeled context for resolution and reporting, not as a numeric interpolation space.

### 6.3 Qualifier Resolution in Constraints

When a constraint references a qualified metric, the comparison operator determines which qualifier the evaluator reads. This keeps constraint expressions terse — the designer writes `adc.SupplyVoltage == 3.3V` rather than separately naming the min and max qualifier keys.

Resolution rules:

- `instance.Metric >= X`: the evaluator reads the `min` qualifier. The constraint passes when `min >= X`. This asserts that the guaranteed lower bound clears the threshold.
- `instance.Metric <= X`: the evaluator reads the `max` qualifier. The constraint passes when `max <= X`. This asserts that the guaranteed upper bound does not exceed the threshold.
- `instance.Metric == X`: the evaluator reads both `min` and `max`. The constraint passes when `min <= X <= max`. This is a containment check — it asserts that the value X falls within the declared operating range.
- Bare `instance.Metric` in forwarding or parameter propagation reads `typ`.

These rules apply uniformly to `spec {}` constraints and to synthesis candidate filtering (Section 10). `bench {}` constraints compare against a simulation-produced scalar and do not interact with qualifier resolution — bench measurements produce a single value, not a qualified triple.

When a metric has only unqualified (typ) values and a constraint uses `>=` or `<=`, the evaluator treats the typ value as the sole available bound. When a metric has corner-scoped values, the evaluator uses the active corner (Section 6.1) to select the relevant value before applying qualifier resolution.

If a constraint requires a qualifier that the metric does not provide (for example, `>=` on a metric declared without `min`), it is a validation error. Interface qualifier requirements (Section 6.6) catch this class of error at the `implements` check rather than deferring it to constraint evaluation.

### 6.4 Metric Reference Syntax

Two syntactic forms distinguish metric access by origin:

The dot operator (`instance.Metric`) performs declared metric property lookup on an instance. It is used in constraints, forwarding, and parameter propagation whenever the value comes from a metric declared or bound on a sub-component.

The double-colon operator (`bench::Measurement`) performs bench measurement extraction. It is used exclusively inside metric binding blocks to name a value produced by a bench run. Arguments may follow in parentheses when the measurement requires parameters.

These two forms define the named metric kinds:

A **bench-derived metric binding** has the form `MetricName = benchBinding::Measurement(args?)`. The value is produced by simulation: the bench planner generates a testbench, runs it, and extracts the named measurement. The `::` operator appears only on the right-hand side of a metric assignment inside a `metrics {}` block that sits within a bench bind.

A **forwarded metric alias** has the form `MetricName = instance.Metric`. The value is aliased from a sub-component's declared metric. No simulation runs; the evaluator resolves the value by looking up the named metric on the target instance.

Within a circuit's own constraint block, metrics bound on the same circuit are referenced by bare name (unqualified). The `metrics::` self-reference prefix is not supported.

### 6.5 Constraint References

Constraints reference metrics on instances using the dot operator. Constraints are placed in sub-blocks according to their verification method (see Section 8 for the full taxonomy).

```cascode
constraints {
  bench {
    c_gbw = frontend.PassbandGain >= 40dB
  }
  spec {
    c_resolution = adc.Resolution >= 16 bits
    c_supply = adc.SupplyVoltage == 3.3V
  }
}
```

`bench {}` constraints must trace to bench-derived metrics; `spec {}` constraints must trace to declared metrics. The evaluator validates this mapping.

### 6.6 Interface Metric Declarations

Interfaces may declare metrics as contracts with declaration-only entries. A metric declaration names the metric and its unit. Optionally, the declaration may require specific qualifiers by appending a braced qualifier list:

```cascode
interface ADCSubsystem {
  metrics {
    Resolution : bits
    MaxSampleRate : SPS
    InputCapacitance : F
    SupplyVoltage : V { min, max }
    SupplyCurrent : A { max }
  }
}
```

When a qualifier requirement is present (e.g., `{ min, max }`), the implementing part's effective `metrics {}` must provide at least each listed (metric, qualifier) pair. When no qualifier requirement is stated, the interface requires at least one value for the metric (any qualifier).

Rules:

1. Interface metrics are the minimum required set.
2. Implementations may expose additional metrics, qualifiers, and corner-scoped variants beyond the required set.
3. Missing required metrics are hard validation errors at the `implements` check.
4. Missing required qualifiers are hard validation errors at the `implements` check. For example, if an interface requires `SupplyVoltage : V { min, max }` and an implementing part provides only `SupplyVoltage min = 2.0V` without a `max` entry, validation fails.

### 6.7 Metric Providers and Forwarding

Metric values come from two explicit provider kinds: bench-derived (simulation) and declared/datasheet. The provider kind is determined by how the metric is bound, not where it is referenced.

Forwarding is supported for wrappers and is alias-only in v1. Forwarded metrics use the dot operator:

```cascode
metrics {
  Resolution = uAdc.Resolution
  MaxSampleRate = uAdc.MaxSampleRate
}
```

Transform expressions in forwarding are deferred.

### 6.8 Variant-Dependent Metrics

When a part declares variant blocks, metric values may come from the base `metrics {}` block, from a variant option's `metrics {}` block, or from both. The effective metric set for a given configuration is computed by merging base metrics with all selected variant option metrics. Variant-provided entries override base entries only when `(MetricName, qualifier, corner)` keys match, where corner is either unscoped or a named corner from an `at <Corner> { ... }` block.

For example, in the `YageoRC` passive family, `PowerRating` and `VoltageRating` depend on the `body` axis, while `Tolerance` depends on the `grade` axis. The base `metrics {}` block is empty; all metrics come from variant options. In `OPA2376`, the base `metrics {}` block provides all values (shared across packages) and no variant options carry metric overrides.

Every interface-required metric must be provided either by the base `metrics {}` block, by inherited metrics via `extends`, or by all variant options across all variant axes collectively. Required coverage is checked per required metric key and per required qualifier/corner combination. If a required key is provided by some options of an axis but not others at the same corner/qualifier, it is a validation error — every valid configuration must produce a complete metric set.

The merge order is: inherited metrics (from `extends` chain), overridden by the part's own base metrics, overridden by selected variant option metrics. When constraints reference variant-dependent metrics (`spec { c_pwr = r.PowerRating >= 50mW }`), the evaluator traces through the instance's selected variant options and the active corner (Section 6.1) to look up the concrete value.

### 6.9 PCB-Domain Units

The PCB domain extends the unit system with the following units:

- `pct` — percentage (e.g., `1pct` for 1% tolerance)
- `SPS` — samples per second (e.g., `860 SPS`)
- `bits` — bit count (e.g., `16 bits`)
- `LSB` — least significant bit (e.g., `0.5 LSB`)
- `B` — bytes, with standard SI prefixes (e.g., `64kB`, `8kB`)

These will be formally added to the unit tables in spec chapters Ch02 and Ch03 as part of implementation. Existing units (`Hz`, `V`, `A`, `F`, `Ohm`, `dB`, `W`, `Vrms`, `V/us`, etc.) continue to apply to PCB-domain metrics. E-series parameter types (`e6` through `e192`) are part of the parameter type system defined in Section 5.3, not the unit system.

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

The constraint system uses distinct sub-blocks organized by verification method. Each sub-block carries clear semantics about how the evaluator verifies the constraint and where the metric value originates. Tier 1 defines only `bench {}`, `spec {}`, and `physical {}`.

### 8.1 Sub-Block Types

Three constraint sub-blocks are supported:

`bench {}` constrains scalar metrics verified by bench execution (simulation). The bench planner generates a testbench, runs it, extracts a metric value, and compares the result against the stated bound. This replaces the prior `bench {}` block from the IC-only constraint system.

```cascode
constraints {
  bench {
    c_gain = frontend.PassbandGain >= 40dB
    c_noise = frontend.IntegratedInputNoise <= 1uVrms
  }
}
```

`spec {}` constrains scalar metrics verified against declared or forwarded values. No simulation runs; the evaluator looks up the metric value from a `metrics {}` block on a part or circuit and compares it against the bound. This block is for component selection and datasheet-backed assertions.

```cascode
constraints {
  spec {
    c_res = adc.Resolution >= 16 bits
    c_flash = mcu.FlashSize >= 64kB
  }
}
```

`physical {}` constrains device and component physical parameters against structural rules. This replaces the prior `physical {}` block. In IC designs, physical constraints enforce geometry rules (minimum channel length). In PCB designs, they can enforce package, operating temperature, or other physical attributes.

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
    IMicrocontroller mcu = new Controller() { ... }
  }

  constraints {
    bench {
      c_bw = frontend.LowpassBandwidth >= 10kHz
      c_gain = frontend.PassbandGain >= 40dB
      c_noise = frontend.IntegratedInputNoise <= 1uVrms
    }
    spec {
      c_adc_res = adc.Resolution >= 16 bits
      c_adc_rate = adc.MaxSampleRate >= 128 SPS
      c_mcu_flash = mcu.FlashSize >= 64kB
    }
  }
}
```

The separation makes verification intent visible in the source: the analog frontend will be simulation-verified, while the ADC and MCU are verified against their declared specifications.

### 8.4 Variant-Constraint Interaction

Variant selections determine which declared metric values are visible to constraints. For `spec {}` constraints on variant-dependent metrics, the evaluator traces `instance.Metric → part declaration → selected variant option → metric value`. Variant selections act as a lookup key into the metric table, not as a runtime parameter.

For `bench {}` constraints, variant selections affect simulation indirectly through SPICE model selection and component values. The bench-derived metric emerges from simulation, not from declared values. Variant selections primarily determine which model file to load.

When metric values flow between instances (e.g., `load_cap=adc.InputCapacitance`) and the source metric is variant-dependent, the ordering pass must resolve the source instance's variant selections before propagating. In fill blocks this is trivial since variants are fully specified. In slot blocks during synthesis, the synthesizer fixes upstream variants before evaluating downstream metrics.

Variant selections are discrete decision variables in the synthesis search space. For a `Resistor` slot, the synthesizer enumerates each (part × variant combination) as a distinct candidate with its own metric vector:

```
YageoRC[body=_0402, grade=F](R=?) → PowerRating=63mW,  Tolerance=1pct
YageoRC[body=_0402, grade=J](R=?) → PowerRating=63mW,  Tolerance=5pct
YageoRC[body=_0603, grade=F](R=?) → PowerRating=100mW, Tolerance=1pct
...
```

Constraints filter infeasible candidates. Objectives (cost, area) rank the rest.

### 8.5 Metric Propagation Ordering

When constructor parameters on one instance depend on metric values from another instance (e.g., `load_cap=adc.InputCapacitance`), the bench planner must determine a resolution order before generating testbenches.

The algorithm is a topological sort on the constructor-parameter-to-metric dependency graph. Each node represents an instance in the composition. A directed edge from instance B to instance A exists when A's constructor parameter expression references a metric on B. The planner walks the graph in topologically sorted order, resolving each instance's metrics before they are consumed by downstream instances.

Cycles in this graph are hard validation errors reported at design time. A cycle means two or more instances mutually depend on each other's metrics for construction, which has no well-defined resolution order. The error message identifies the participating instances and the metric references forming the cycle.

In fill blocks, all variant selections are explicit, so metric values are immediately available from declared `metrics {}` blocks. In slot blocks during synthesis, the topological order determines the sequence in which the synthesizer fixes upstream candidates (and their variant selections) before evaluating downstream constructor parameters.

### 8.6 Hierarchical Verification

Running `cascode bench run` on a composition walks the entire hierarchy tree and evaluates constraints at every level. Each circuit's constraints are checked independently, ensuring that components pass both standalone and in context.

The execution model for a composition with mixed simulable and non-simulable sub-blocks:

1. Walk the hierarchy. Identify circuits with bench bindings (simulable) and circuits with only declared or forwarded metrics (non-simulable).
2. For each simulable circuit, generate and run testbenches. Extract bench-derived metric values from simulation results.
3. For each non-simulable circuit, collect declared metric values from `metrics {}` blocks.
4. Evaluate `bench {}` constraints against simulated metrics and `spec {}` constraints against declared metrics, at every level of the hierarchy.
5. Report each constraint result with its verification provenance (bench-verified or spec-verified) and hierarchy level.

A child circuit's constraints serve as its own design targets. A parent circuit's constraints serve as system requirements. Both are always evaluated. If a child circuit targets 38 dB passband gain as a design margin and the parent requires 40 dB for the system, the bench runner reports both results independently.

### 8.7 Harness Applicability

Harness requirements are tied to executability:

- A circuit that is bench-executable (declares bench bindings directly or via implemented interfaces, or is transitively required by `bench {}` constraints) MUST satisfy harness requirements for simulation.
- A circuit that is not bench-executable and is verified only through `spec {}` or `physical {}` constraints MAY omit `harness {}`.

This keeps Tier 1 non-simulable compositions concise while preserving explicit harness contracts for simulated paths.

---

## 9. Parts Database and Pricing Pointers

This section defines language-facing expectations. Full external sync architecture remains out of scope.

### 9.1 Role

A parts ecosystem for PCB design plays the same role that `pdk.db` plays for PDK-backed primitive flows: candidate discovery and resolution against constraints.

### 9.2 Library Organization

Interface definitions and part declarations are organized under domain-specific library trees:

- `lib/std/` -- shared constructs: bundles (`Diff`), bus bundles (`I2C`, `SPI`, `UART`, `SWD` under `lib/std/bus/`), shared interfaces (`SingleEndedOpAmp` under `lib/std/amp/`), benches, and primitives.
- `lib/ic/` -- IC-domain component interfaces (`NMOS`, `PMOS`, `Resistor`, `Capacitor`, `Inductor`, `Diode`).
- `lib/pcb/` -- PCB-domain component interfaces (`NMOS`, `Resistor` with metric contracts, `DualOpAmp`, `ADCSubsystem`, `IMicrocontroller`, etc.). Does not contain part declarations.
- `lib/parts/` -- concrete part declarations organized by category (`lib.parts.opamp`, `lib.parts.adc`, `lib.parts.mcu`, `lib.parts.res`, `lib.parts.cap`, `lib.parts.power` for regulators and decoupling patterns, `lib.parts.conn` for connectors).

### 9.3 Catalog Option Contract

Each part declaration carries checked-in procurement pointers inside its `catalog {}` block. Options may live in an explicit entry's sourcing fields, in shared `defaults`, or inside variant axis options that generate entries. Population may come from manufacturer/distributor APIs, distributor CSV exports, or curated internal catalogs, but the language-facing pointer contract is the same.

Required fields per option:

- `provider`
- `sku`
- `priority`

Optional:

- `url`
- `approved`
- additional non-lookup-able project metadata as key/value fields

The resolver performs volatile data lookup (availability, lead time, and pricing) on demand using these pointers. Those volatile fields are not required to be checked into source. Any policy data that cannot be reliably looked up (for example, internal approval status or qualification notes) should be stored in source metadata.

This model preserves deterministic fallback order while avoiding mandatory mirrored distributor caches in the language surface.

When a part uses variant axes to generate entries, procurement options may appear in either shared defaults or inside axis options. Package-specific SKUs naturally live inside the axis option that determines the package (e.g., body size determines the DigiKey suffix). SKUs that depend on multiple axes use template strings in shared defaults that reference axis fields.

### 9.4 MPN Template Resolution

Parts with variant blocks and constructor parameters may use template strings in the `mpn` and `sku` fields. The BOM resolution pipeline processes these in order:

1. Walk the EL hierarchy. For each part instance, collect: reference designator path, part family, constructor params, selection (explicit entry name, or variant axis selections).
2. Resolve the effective entry backing. Merge `catalog.defaults` with the selected entry body (for explicit entries) or with the selected variant option bodies (for variant-generated entries). Resolve all `{...}` template references using params and selections. Produce: concrete MPN, footprint, SPICE model ref, resolved procurement options.
3. Optionally enrich procurement options via provider lookups (availability, lead time, and pricing) at resolution time.
4. Merge shared metrics with entry/variant metric overrides. All interface-required metrics must resolve.
5. Aggregate by resolved MPN. Sum quantities. Collect reference designator lists.
6. Emit BOM as structured JSON alongside SPICE netlists.

BOM output is a first-class emit target. The JSON schema for a BOM line item:

```json
{
  "bom": [
    {
      "mpn": "RC0402FR-07100KL",
      "description": "100kΩ 1% 0402 thick film resistor",
      "footprint": "0402",
      "qty": 4,
      "refdes": ["r1", "r2", "r3", "r4"],
      "value": "100kΩ",
      "distributors": [
        { "provider": "DigiKey", "sku": "311-100KLRCT-ND", "priority": 10 },
        { "provider": "Mouser", "sku": "603-RC0402FR-07100KL", "priority": 20 }
      ]
    }
  ]
}
```

Each entry in the `bom` array represents an aggregated line item: instances sharing the same resolved MPN are grouped. The `refdes` array lists all reference designators for the line item. The `distributors` array carries resolved procurement options sorted by priority. The `value` field is a human-readable value string derived from constructor parameters (e.g., resistance, capacitance). Fields not applicable to a given part (e.g., `value` for an MCU) are omitted.

### 9.5 Passive Resolution

Parameterized passives represent families rather than concrete components. A declaration like `part YageoRC(e96 R) implements Resistor` defines a family whose value domain is constrained to the declared E-series (Section 5.3). Concrete resolution to a sourceable MPN occurs during synthesis or explicit fill-block instantiation. The E-series type ensures that only standard preferred values enter the resolution pipeline. The resolution then considers the validated value, selection (explicit or variant-generated; for example, body size and tolerance grade), and available procurement options. Selection is a discrete decision variable alongside the E-series-constrained value parameter in the synthesis search space.

---

## 10. PCB Synthesis Model

HL-to-EL synthesis for PCB design includes at least three activities.

IC selection:

- Query parts ecosystem against metric constraints.
- Instantiate selected part and associated support circuitry.

Passive network design:

- Choose topology.
- Size values within the declared E-series parameter type.

Mixed-block synthesis:

- Combine selected active parts with synthesized passive networks.

Entry selection is a synthesis degree of freedom alongside part selection and value sizing. When a slot block omits selection, the synthesizer explores all valid entries for each candidate part. For variant-generated entries, this means exploring all valid variant combinations. Each (part, entry) candidate yields a metric vector; constraints filter infeasible configurations and objectives rank the rest. For a `Resistor` slot, the search space is the Cartesian product of candidate part families, their entry space (including body size and tolerance grade axes), and the continuous value parameter.

Synthesis and resolution should be deterministic for identical inputs. This RFC requires stable outcomes but does not prescribe a single global tie-break algorithm.

The existing `synth {}` block remains the synthesis guidance carrier:

```cascode
synth {
  seed = 42
  objective = minimize_cost
}
```

Additional synthesis directives (e.g., passive value series preferences) can be added to the `synth {}` block as the synthesis framework matures.

---

## 11. Grammar Changes

This section describes expected grammar shape and semantic policy updates.

### 11.1 New and Modified Lexer Tokens

New tokens:

```antlr
PART_KW     : 'part' ;
METRICS_KW  : 'metrics' ;
CATALOG_KW  : 'catalog' ;
ENTRY_KW    : 'entry' ;
OPTION_KW   : 'option' ;
PINS_KW     : 'pins' ;
UNITS_KW    : 'units' ;
DEFAULTS_KW : 'defaults' ;
CORNERS_KW  : 'corners' ;
AT_KW       : 'at' ;
MIN_KW      : 'min' ;
MAX_KW      : 'max' ;
TYP_KW      : 'typ' ;
SOME_KW     : 'Some' ;
BENCH_KW    : 'bench' ;     // replaces NUMERIC_KW in constraint blocks
SPEC_KW     : 'spec' ;      // new constraint sub-block
PHYSICAL_KW : 'physical' ;  // replaces TECH_KW in constraint blocks
LIBRARY_KW  : 'library' ;   // renamed from PACKAGE_KW
BENCHES_KW  : 'benches' ;   // bench binding block on interfaces/circuits
VARIANT_KW  : 'variant' ;
ABSTRACT_KW : 'abstract' ;
EXTENDS_KW  : 'extends' ;
E6_KW       : 'e6' ;
E12_KW      : 'e12' ;
E24_KW      : 'e24' ;
E48_KW      : 'e48' ;
E96_KW      : 'e96' ;
E192_KW     : 'e192' ;
```

Removed tokens:

```antlr
// DEVICE_TYPE is removed. NMOS, PMOS, Resistor, etc. are now ordinary
// identifiers resolved from included interface libraries.
// NUMERIC_KW is replaced by BENCH_KW.
// TECH_KW is replaced by PHYSICAL_KW.
// GRAPH_KW is removed in this taxonomy.
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
    : ABSTRACT_KW? PART_KW name=IDENT (LPAREN paramList? RPAREN)?
      (EXTENDS_KW parentPart=IDENT (LPAREN argList? RPAREN)?)?
      (IMPLEMENTS_KW implementsList)?
      LBRACE partMember* catalogBlock RBRACE
    ;

implementsList
    : IDENT (COMMA IDENT)*
    ;

partMember
    : paramsBlock                                             # PartParams
    | direction portName COLON portType                       # PartPort
    | SUPPLY_KW IDENT                                         # PartSupply
    | GROUND_KW IDENT                                         # PartGround
    | cornersBlock                                            # PartCorners
    ;

catalogBlock
    : CATALOG_KW LBRACE catalogMember* RBRACE
    ;

catalogMember
    : defaultsBlock
    | entryDef
    | variantBlock
    ;

defaultsBlock
    : DEFAULTS_KW LBRACE entryMember* RBRACE
    ;

entryDef
    : ENTRY_KW entryName=IDENT LBRACE entryMember* RBRACE
    ;

entryMember
    : catalogOption                                           # EntryCatalogOption
    | pinsBlock                                               # EntryPins
    | unitsBlock                                              # EntryUnits
    | metricsValueBlock                                       # EntryMetrics
    | mechanicalBlock                                         # EntryMechanical
    | IDENT EQ (STRING | signedQuantity | tupleLiteral)        # EntryField
    ;

mechanicalBlock
    : 'mechanical' LBRACE mechanicalField* RBRACE
    ;

mechanicalField
    : IDENT EQ (STRING | signedQuantity | tupleLiteral)
    ;

pinsBlock
    : PINS_KW LBRACE pinMapEntry+ RBRACE
    ;

pinMapEntry
    : pinRef EQ padMap
    ;

padMap
    : padRef (COMMA padRef)*
    | padRange
    ;

padRange
    : padRef DOTDOT padRef
    ;

padRef
    : IDENT (LBRACKET INT_LITERAL RBRACKET)?
    ;

unitsBlock
    : UNITS_KW LBRACE unitDef+ RBRACE
    ;

unitDef
    : IDENT LBRACE unitField+ RBRACE
    ;

unitField
    : IDENT EQ tupleLiteral
    ;

cornersBlock
    : CORNERS_KW LBRACE cornerDef+ RBRACE
    ;

cornerDef
    : IDENT LBRACE cornerField+ RBRACE
    ;

cornerField
    : IDENT EQ (signedQuantity | NUMBER | STRING)
    ;
```

A part must have either `extends`, `implements`, or both. Abstract parts must have `implements` (they define the contract). Concrete parts must include a `catalogBlock` that yields at least one concrete entry, and every concrete entry must include an effective `pinsBlock` (directly or via merged defaults) such that the effective entry has complete terminal-leaf coverage. Passive `part` declarations use E-series parameters (`e96 R`, `e12 C`, `e24 L`) to constrain values to a preferred number series. `size` remains reserved for primitive geometry.

The `paramType` rule in parameter lists accepts E-series types alongside the existing scalar types:

```antlr
paramType
    : REAL_KW
    | INT_KW
    | BOOL_KW
    | eSeriesType
    ;

eSeriesType
    : E6_KW | E12_KW | E24_KW | E48_KW | E96_KW | E192_KW
    ;
```

### 11.5 Variant Block

```antlr
variantBlock
    : VARIANT_KW axisName=IDENT LBRACE variantOption+ RBRACE
    ;

variantOption
    : optionName=(IDENT | STRING) LBRACE variantOptionMember* RBRACE
    ;

variantOptionMember
    : entryMember
    | excludeDirective
    ;

excludeDirective
    : 'exclude' IDENT EQ (IDENT | STRING)
    ;
```

Variant option bodies can contain entry members (sourcing fields, options, pins, units, metrics, mechanical metadata) and arbitrary metadata fields (`key = value` pairs), plus exclusion directives.

### 11.6 Procurement Option Entries

Sourcing fields (`mpn`, `footprint`, `spice`) are direct `entryMember` alternatives (matched by the `EntryField` rule). Procurement option entries use the following grammar:

```antlr
catalogOption
    : OPTION_KW LBRACE catalogOptionField+ RBRACE
    ;

catalogOptionField
    : IDENT EQ (STRING | NUMBER)
    ;
```

Each `catalogOption` must contain at minimum `provider`, `sku`, and `priority` fields. Optional fields include `url`, `approved`, and project-specific non-lookup-able metadata keys. The `package` field is renamed to `footprint` in all entry contexts.

### 11.7 Array Port Syntax

Array ports support ranged declarations and indexed references for multi-pin components (MCUs, FPGAs, connectors):

```antlr
portDecl
    : direction portName (LBRACKET range RBRACKET)? COLON portType
    ;

range
    : INT_LITERAL COLON INT_LITERAL
    ;

portIndexRef
    : IDENT LBRACKET INT_LITERAL RBRACKET
    ;
```

Array ports are declared with an inclusive range (`io PA[0:15] : digital`) and indexed in connection bindings (`.PA[2]--DEBUG.TX`). The range is inclusive on both ends.

### 11.8 Metrics Blocks

```antlr
metricsValueBlock
    : METRICS_KW LBRACE metricsEntry* RBRACE
    ;

metricsEntry
    : metricAssign
    | cornerMetricsBlock
    ;

cornerMetricsBlock
    : AT_KW cornerName=IDENT LBRACE metricAssign* RBRACE
    ;

metricAssign
    : IDENT metricQualifier? EQ metricValue
    ;

metricValue
    : signedQuantity
    | metricSource
    ;

metricQualifier
    : MIN_KW
    | MAX_KW
    | TYP_KW
    ;
```

Interface contracts use declaration-only metrics with optional qualifier requirements:

```antlr
interfaceMetricsBlock
    : METRICS_KW LBRACE metricDecl* RBRACE
    ;

metricDecl
    : IDENT COLON unitType qualifierRequirement?
    ;

qualifierRequirement
    : LBRACE metricQualifier (COMMA metricQualifier)* RBRACE
    ;
```

When `qualifierRequirement` is present, the implementing part must provide at least those qualifiers for the named metric. When absent, any single value suffices.

Bench bind blocks may contain their own metrics block to bind measurement results to named metrics:

```antlr
benchBindingMetrics
    : METRICS_KW LBRACE benchMetricsEntry* RBRACE
    ;

benchMetricsEntry
    : metricBind
    | cornerBenchMetricsBlock
    ;

cornerBenchMetricsBlock
    : AT_KW cornerName=IDENT LBRACE metricBind* RBRACE
    ;

metricBind
    : IDENT metricQualifier? EQ metricSource
    ;

metricSource
    : benchMetricRef       // bench-derived: benchName::Measurement(args?)
    | instanceMetricRef    // forwarded: instance.Metric
    ;
```

### 11.9 Metric Reference Grammar

Two distinct reference forms distinguish property lookup from bench extraction:

```antlr
instanceMetricRef
    : IDENT DOT IDENT                                    // instance.Metric (property lookup)
    ;

benchMetricRef
    : IDENT COLONCOLON IDENT (LPAREN argList? RPAREN)?   // bench::Measurement (extraction)
    ;
```

The dot form (`instance.Metric`) is used in constraints, forwarding, and parameter propagation. The double-colon form (`bench::Measurement`) is used exclusively in bench-derived metric bindings. The evaluator enforces that `benchMetricRef` appears only inside `benchBindingMetrics` blocks or in bench-derived metric assignments.

### 11.10 Instance Declaration with Selection

Instance declarations now support an optional selection in square brackets between the type name and the argument list. `Some` remains valid only in slot blocks:

```antlr
slotBlockStatement
    : NET_KW IDENT COLON portType                                    # SlotNetDecl
    | slotInstanceDecl                                               # SlotInstanceDecl
    | pinRef WIRE_OP pinRef                                          # SlotConnectDecl
    ;

slotInstanceDecl
    : (declaredType=IDENT | SOME_KW) instanceId=IDENT EQ NEW_KW instanceTypeName
      (LBRACKET selectionArgList? RBRACKET)?
      (LPAREN argList? RPAREN)? bindingBlock?
    ;
```

In fill blocks, the existing `instanceDecl` requires a declared type (an interface name) and does not accept `Some`:

```antlr
instanceDecl
    : declaredType=IDENT instanceId=IDENT EQ NEW_KW instanceTypeName
      (LBRACKET selectionArgList? RBRACKET)?
      (LPAREN argList? RPAREN)? bindingBlock?
    ;

selectionArgList
    : selectionArg (COMMA selectionArg)*
    ;

selectionArg
    : (IDENT EQ)? (IDENT | STRING)                           // named or positional
    ;
```

Bare positional `[X]` selects an explicit `entry` by name only. Variant axes always require the named form `[axis=option]`, even for single-axis parts. A positional argument that does not match any declared entry name is a validation error. This rule eliminates ambiguity when a part declares both explicit entries and variant axes.

In fill blocks, selection must be complete (all entries or all axes specified). In slot blocks, selection may be omitted (deferred to synthesis).

The formerly optional `(declaredType=IDENT)?` pattern is removed. A declared type is always required in both slot and fill blocks.

### 11.11 Device Instantiation (Modified)

With `DEVICE_TYPE` removed, the existing `deviceDecl` rule is unified with `instanceDecl`. The declared type is an interface name (e.g., `NMOS`, `Resistor`) resolved from scope rather than a reserved keyword:

```antlr
// Before:
deviceDecl
    : DEVICE_TYPE deviceId EQ NEW_KW primitiveName=IDENT LPAREN sizeArg RPAREN bindingBlock
    ;

// After: unified into instanceDecl (see above).
```

### 11.12 Constraint Block Changes

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

`constraintEntry` references instance metrics with the dot operator (`instance.Metric`) and bench metrics with the double-colon operator (`bench::Measurement`). Within a circuit's own constraint block, self-metrics are referenced by bare name. `physicalConstraintEntry` retains its existing internal structure.

The `graph {}` sub-block is not supported; no `graphBlock` appears in the constraint grammar.

### 11.13 Resolution Policy

There are no reserved keyword categories. All instantiation targets -- whether for primitives, parts, or circuits -- resolve semantically against declarations in scope. The `include` directives determine which interfaces are available. Ambiguous or unresolved targets are hard validation errors. If both IC and PCB domains are included and a short symbol name is ambiguous, the source must use a fully-qualified name.

---

## 12. Worked Example: Sensor Frontend PCB

A complete worked example accompanies this RFC at `tests/golden/cas/pcb/SensorFrontendPCB.cas` and is updated as implementation phases land. The current example and planned extensions include:

- Two entries on OPA2376 (`catalog { defaults { ... } entry VSSOP8 { ... } entry SOIC8 { ... } }`) with positional bracket selection (`[VSSOP8]`).
- Multi-axis entry generation on YageoRC (`catalog { defaults { ... } variant body { ... } variant grade { ... } }`) with named bracket selection (`[body=_0402, grade=F]`).
- Part inheritance: abstract `STM32G0` base with concrete `STM32G031K extends STM32G0`, carrying `catalog { defaults { ... } variant flash { ... } variant pkg { ... } }`.
- Concrete-entry pin mapping via `pins {}` and multi-unit grouping via `units {}`, including pin-range shorthand (`PA[0:15] = P6..P21`).
- MPN template strings (`"STM32G031K{flash.code}{pkg.suffix}"`, `"RC{body.footprint}{grade.code}{R}L"`) with field and parameter interpolation.
- Single-entry parts (ADS1115, CL05B) demonstrating that brackets are optional when only one entry exists.
- Fill-block selection completeness: multi-entry parts require explicit selection; variant-generated entries require all axes explicitly selected.
- `footprint` catalog field (renamed from `package`).
- 16-bit ADC and MCU wrappers with forwarded metrics using dot syntax (`uAdc.Resolution`).
- Corner-scoped metrics (`min|max|typ` qualifiers plus `metrics { at Corner { ... } }` blocks).
- `bench`/`spec` constraint taxonomy separating simulation-verified and declaration-verified constraints, using dot syntax for instance metric references (`frontend.PassbandGain`, `adc.Resolution`, `mcu.FlashSize`).
- Harness applicability based on bench executability (simulable paths require harness; spec-only paths may omit).
- Hierarchical constraint verification (EL design targets with bare metric names, HL system requirements with instance-qualified references).
- Metric-driven parameter propagation (`load_cap=adc.InputCapacitance`), resolved during bench planning via an ordering pass.
- Bench-derived metric bindings (`PassbandGain = transfer_bench::PassbandGain`) using `::` exclusively for bench extraction.

The example intentionally exercises both simulable and non-simulable paths within one HL composition, demonstrating entry selection, part inheritance, and the constraint taxonomy across verification methods and hierarchy levels.

---

## 13. Language Specification Deliverables

This section mandates updates to the Cascode language specification (`spec/language/`) for the constructs and conventions introduced by this RFC. The deliverables comprise updates to existing spec chapters for new and renamed constructs, and a new domain-application chapter for PCB design.

### 13.1 Rationale for a Dedicated Chapter

The constructs this RFC introduces — `part`, variant blocks, part inheritance (`extends`/`abstract`), metrics, `spec {}` constraints, array ports, the `implements` migration on primitives — are general-purpose language mechanisms whose normative definitions belong in Chapters 2 and 3. The PCB domain, however, introduces enough workflow, conventions, and domain-specific patterns that scattering the material across general-purpose sections would not serve PCB designers well. A dedicated Chapter 5 provides the domain-application guide: how these constructs compose for PCB schematic capture, PCB-specific conventions, and topics unique to the domain (parts ecosystem, pricing, passive network synthesis, mixed simulable/non-simulable verification).

### 13.2 New Chapter: Ch05_PCB_Design.md

The chapter covers the following topics:

Section 5.0 (Summary) establishes the chapter's purpose and relates Cascode's existing abstractions to PCB schematic capture and synthesis.

Section 5.1 (Conceptual Mapping) expands the IC-to-PCB mapping table from Section 3 of this RFC into prose. Key themes: schematic symbol vs concrete backing, `implements` as the unifying mechanism, and where IC and PCB flows diverge in identity, parameters, and metric sources.

Section 5.2 (Domain Libraries and Namespace Convention) describes the organization of `lib/ic/`, `lib/pcb/`, `lib/std/`, and `lib/parts/`. Covers mixed-domain includes with explicit qualification on ambiguity and cross-references Ch02 Section 2.1 for the underlying resolution model.

Section 5.3 (The `part` Construct) gives a domain-focused treatment of the `part` declaration, cross-referencing Ch02 and Ch03 for normative definitions. Covers the relationship to `primitive`, the catalog model (`catalog {}`), the `mpn`/`footprint`/`spice` fields, variant axes for discrete entry generation, part inheritance (`extends`/`abstract`) for part families, mandatory `pins {}` mapping for concrete entries, optional `units {}` grouping for multi-unit packages, MPN template strings, bracket instantiation syntax, parameterized passives vs fixed-identity ICs, array ports on high-pin-count components, and worked examples.

Section 5.4 (The Metrics System in PCB Context) describes how metrics enable datasheet-driven and simulation-driven validation. Covers qualifier semantics (`min|max|typ`), corner scoping (`corners {}` and `metrics { at Corner { ... } }`), interface metric contracts, the two named metric kinds (bench-derived and forwarded), metric-driven parameter propagation, and PCB-domain units.

Section 5.5 (Constraint Taxonomy for Mixed Designs) explains how `bench {}`, `spec {}`, and `physical {}` sub-blocks partition constraints by verification method. Covers the bench-to-spec distinction, verification provenance enforcement, mixed compositions with simulable and non-simulable sub-blocks, hierarchical constraint verification, and self-metric references by bare name.

Section 5.6 (Bus Bundles and Digital Interconnect) treats standard PCB buses (I2C, SPI, UART, SWD) as ordinary bundles. Points to `lib/std/bus/` and shows connection patterns, cross-referencing Ch02 Section 2.3 for the bundle mechanism.

Section 5.7 (Parts Ecosystem and Pricing) describes the parts database role, `lib/parts/` tree structure organized by category (including `lib/parts/power/` for regulators and decoupling, `lib/parts/conn/` for connectors), the `option` pointer contract, on-demand provider lookup for volatile fields (availability/pricing), source-authored metadata for non-lookup-able policy fields, the `mechanical {}` block for physical geometry and assembly data, and passive resolution.

Section 5.8 (PCB Synthesis Model) covers HL-to-EL synthesis for PCB designs: IC selection against metric constraints, passive network topology and value sizing, and mixed-block synthesis. Describes the `synth {}` block's `objective` directive and extensibility for future synthesis directives, cross-referencing Ch02 Section 2.12.

Section 5.9 (Worked Example: Sensor Frontend PCB) walks through the `SensorFrontendPCB.cas` golden test section by section, covering file structure and includes, part declarations, interface contracts with metric declarations, HL composition with mixed bench/spec constraints and metric-driven parameter propagation, EL implementations with bench-derived and forwarded metrics, and the hierarchical verification flow.

### 13.3 Updates to Existing Spec Chapters

The specification already covers HL composition slots (2.5.5), the `Some` keyword (3.10.3), bench bindings with measurement exports (4.8.5), `implements` on circuits (2.4), and the `synth {}` block (2.12). The updates below target only what is genuinely new or renamed.

Ch01: add a brief note in Section 1.5 (Cascode in a Few Examples) that PCB design is covered in Ch05, or include a minimal PCB example. In Section 1.6 (Toolchain Pipeline), note that the pipeline extends to PCB schematic capture and constraint-driven part selection.

Ch02 new constructs: add `part` to the Section 2.2 top-level declaration list. Add Section 2.5.3 for E-series parameter types (`e6` through `e192`) in the parameter type system, covering the subtype hierarchy and compile-time validation. Add Section 2.6.2 for parts (`mpn`, `footprint`, `spice`, `catalog` fields, variant blocks, part inheritance, mandatory concrete `pins {}` mapping, optional `units {}`, MPN templates, parameterized vs fixed-identity). Add a new section for the metrics system (interface metric declarations, part/circuit metric value blocks, qualifiers and corners, the two named metric kinds, variant-dependent metrics, metric-driven parameter propagation). Add PCB-domain units (`pct`, `SPS`, `bits`, `LSB`, `B`) to Section 2.9.

Ch02 renames and extensions: rewrite Section 2.6 (Primitives) to use `implements` syntax. Rename `bench {}` → `bench {}` in Section 2.7.1. Add a new Section 2.7.x for the `spec {}` sub-block with dot-operator metric lookup and bare-name self-references. Rename `physical {}` → `physical {}`. Add a note to Section 2.5.5 about metric-driven parameter propagation between slot sub-blocks.

Ch03 new syntax: update Section 3.1 to include `partDef`. Add `eSeriesType` grammar rule in the parameter declarations section. Add new sections for part declarations (`partDef`, `partMember`, `catalogBlock`, `catalogOption`, `pinsBlock`, `unitsBlock`, `cornersBlock`), metrics blocks (`metricsValueBlock`, `interfaceMetricsBlock`, `benchBindingMetrics` with qualifiers/corners), metric references (`instanceMetricRef`, `benchMetricRef`), and array ports (`portDecl` with range, `portIndexRef`).

Ch03 renames: rewrite Section 3.8 primitive header to `implements`. Merge Section 3.9 (Device Declarations) into instance declarations (3.10.2). Update Section 3.11 (Constraints) for `bench`/`spec`/`physical` renames, dot-operator constraint references, `LIBRARY_KW` rename, and `GRAPH_KW` removal.

Ch04: add a note at Section 4.8.x about `metrics {}` blocks inside bench bindings, distinct from the existing `measurements {}` exports (4.8.5). The `metrics {}` block maps interface-level metric names to bench measurements; `measurements {}` defines computed derived measurements; both may appear in a single binding.

`spec/language/README.md`: add Chapter 5 to the chapter listing.

---

## 14. Implementation Plan

Implementation is split into phases. Phase 1 covers grammar and AST changes in three sub-phases: additive (1a), breaking (1b), and core spec updates (1c). Phase 2 covers interface libraries (2a) and the PCB spec chapter (2b).

Phase 1a: Additive grammar and AST

- Add `part`, `catalog`, `entry`, `metrics`, `Some` grammar support.
- Add `pins {}` and `units {}` grammar support for parts.
- Add `corners {}` and corner-scoped metrics (`metrics { at Corner { ... } }`) alongside metric qualifiers (`min`, `max`, `typ`).
- Add `eSeriesType` tokens (`E6_KW` through `E192_KW`) and `eSeriesType` grammar rule; extend `paramType` to accept E-series types.
- Add `implementsList` rule shared by `primitiveDef`, `partDef`, and `circuitDef`.
- Add `variant`, `abstract`, `extends` grammar support: `VARIANT_KW`, `ABSTRACT_KW`, `EXTENDS_KW` tokens; `variantBlock`, `variantOption`, `variantOptionMember` rules (including `excludeDirective`); modified `partDef` with optional `ABSTRACT_KW`, optional `EXTENDS_KW` clause.
- Add bracket selection syntax on `instanceDecl` and `slotInstanceDecl`: `selectionArgList`, `selectionArg` rules with `LBRACKET`/`RBRACKET` delimiters.
- Add `mechanicalBlock` and `mechanicalField` rules as optional `entryMember` alternatives.
- Add `spec` constraint sub-block.
- Add `benchBindingMetrics` rule for metrics inside bench bind blocks.
- Add array port declaration and indexing syntax.
- Add `LIBRARY_KW`, `CATALOG_KW`, `ENTRY_KW`, `OPTION_KW`, `BENCHES_KW` tokens.
- Add AST types for part declarations, catalog entries, metric declarations/assignments, variant blocks, variant options.
- Add AST types for pin maps, unit groupings, corners, and corner-scoped metric entries.
- Add separate `slotInstanceDecl` with `Some` support.
- Add reader/writer support and tests.

Verification checkpoint: all existing tests pass; new constructs parse and round-trip correctly.

Phase 1b: Breaking grammar changes and library migration

- Remove `DEVICE_TYPE` from grammar; replace with `implements` on `primitiveDef`.
- Rename constraint sub-blocks: `numeric` → `bench`, `tech` → `physical`. Remove `GRAPH_KW`.
- Rename `PACKAGE_KW` (was `'library'`) to `LIBRARY_KW`.
- Rename catalog field `package` → `footprint` in all part declarations and validation logic.
- Update all existing golden tests for the `bench`/`physical` rename.
- Migrate `lib/std/prim/Devices.cas` and `lib/std/prim/Passives.cas` to new `implements` syntax. Note: `Passives.cas` currently has a bug where `Ideal_Inductor` declares `implements Capacitor`; fix in this phase.
- Bump Cascode version to 3.1.

Verification checkpoint: full test suite passes with renamed tokens and migrated libraries.

Phase 1c: Core spec updates

Grammar is stable after Phase 1b. Update existing spec chapters for renames and new constructs:

- Ch02: add `part` to 2.2 declaration list; add Section 2.6.2 (Parts) including variant blocks, part inheritance, and MPN templates; add metrics section including variant-dependent metrics; rewrite 2.6 primitives to `implements`; rename constraint sub-blocks in 2.7; add `spec {}` sub-block; add PCB units to 2.9.
- Ch03: add `partDef` to 3.1; new sections for part declarations (with `abstract`/`extends`/`variant`), variant blocks, catalog blocks, metrics blocks, metric references, array ports, bracket variant selection on instance declarations; rewrite 3.8 primitives; merge 3.9 into 3.10.2; update 3.11 constraints; rename `package` → `footprint`.
- Ch04: add `metrics {}` in bindings note to 4.8.x.
- Ch01: brief PCB mention in 1.5 or 1.6.
- Update `spec/language/README.md` to add Ch05.
- Specify mixed-domain include policy: both `lib.ic` and `lib.pcb` allowed, ambiguity requires qualification.
- Specify harness applicability by bench executability (simulable paths require harness; spec-only paths may omit).

This phase can proceed in parallel with Phase 2a.

Verification checkpoint: existing spec cross-references resolve; new syntax sections align with grammar.

Phase 2a: Interface libraries

- Create `lib/ic/Interfaces.cas` with IC-domain component interfaces.
- Create `lib/pcb/Interfaces.cas` with PCB-domain component interfaces and bus bundles.
- Update all golden tests and examples.

Phase 2b: PCB spec chapter

Interface libraries (`lib/ic/`, `lib/pcb/`, `lib/parts/`) exist after Phase 2a. The worked example can reference real library paths:

- Draft `spec/language/Ch05_PCB_Design.md` with sections 5.0–5.9.
- Ensure Section 5.9 worked example matches `tests/golden/cas/pcb/SensorFrontendPCB.cas`.

Verification checkpoint: spec chapter cross-references resolve end-to-end; worked example walkthrough is accurate against the golden test.

Phase 3: Resolution and validation

- Implement unified semantic instantiation resolution policy (no reserved keyword categories).
- Implement mixed-domain symbol ambiguity reporting and require fully-qualified references when ambiguous.
- Implement variant selection validation: fill blocks require all axes selected; slot blocks allow omission.
- Implement catalog/metric merge: base + variant option overrides with conflict detection (two axes providing the same non-template catalog field).
- Implement variant exclusion validation: reject instantiations and synthesis candidates selecting combinations marked by `exclude` directives.
- Implement `abstract` part enforcement: cannot appear in fill blocks or be instantiated directly.
- Implement inheritance chain resolution: collect ports, metrics, catalog fields, `implements` conformance from base chain.
- Implement MPN template string validation: verify all `{...}` references resolve to known axes, axis fields, or constructor parameters at compile time.
- Add interface metric contract validation (including variant-dependent metrics must cover all options).
- Add alias-only forwarding resolution and cycle detection.
- Validate `Some` only appears in slot blocks (grammar-enforced).
- Add E-series value validation: check that a literal value passed to an E-series parameter is a member of the declared series at some decade.
- Add pin-map validation: concrete parts must provide a complete mapping for every declared terminal leaf; mapping conflicts are errors.
- Add unit-group validation: unit group references must be valid and consistent with pin maps.
- Add corner validation: referenced corners must exist; corner-scoped metric entries must merge deterministically.

Phase 4: Constraint and runtime evaluation

- Extend evaluators to consume metric references (`instance.Metric` for property lookup, `bench::Measurement` for extraction) from both bench and declared sources.
- Implement `bench` constraint evaluation (simulation-verified) and `spec` constraint evaluation (declaration-verified).
- Implement verification provenance validation (constraints in the correct sub-block for their metric source).
- Implement metric-driven parameter propagation ordering pass during bench planning.
- Implement hierarchical verification: `cascode bench run` walks the composition tree and evaluates constraints at every level.
- Implement harness applicability enforcement based on bench executability.

Phase 5: Parts ecosystem integration

- Implement MPN template resolution: `{axis}`, `{axis.field}`, `{param}` interpolation with value encoding (RKM for resistance, pF codes for capacitance).
- Implement BOM resolution pipeline: instance collection → catalog merge → template resolution → metric merge → aggregation → JSON emission (per schema in Section 9.4).
- Wire catalog option pointers to provider adapters. Volatile fields (availability, lead time, pricing) are lookup-on-demand and are not required to be persisted.
- Implement deterministic option ordering/fallback via `priority`.

Phase 6: Emission and synthesis expansion

- Evolve PCB-oriented emission contracts (SPICE netlist and BOM JSON targets).
- Extend `synth {}` block with passive value series preferences when synthesis framework matures.

---

## References

- RFC-0000: Cascode Language Unification and Declarative Bench System
- RFC-0002: ACIR Terminal Directionality
- RFC-0003: ACIR Syntax Overhaul
- `spec/language/Ch01_Introduction.md` through `Ch05_PCB_Design.md`
- `lib/std/prim/Devices.cas`, `lib/std/prim/Passives.cas` (to be migrated to `implements` syntax)
- `lib/std/amp/SingleEndedOpAmp.cas`
- `lib/std/amp/FullyDifferentialOpAmp.cas`
- `tests/golden/cas/stress/OTA5T_Sky130.cas`
- `tests/golden/cas/stress/OTA5TFullyDiff_Ideal.cas`
- `tests/golden/cas/pcb/SensorFrontendPCB.cas`
- `tests/golden/cas/hl/HLComposition.hl.cai`
