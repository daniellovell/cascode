# RFC-0005: PCB Schematic Representation and Synthesis

| Field | Value |
|-------|-------|
| Status | Draft |
| Authors | Daniel Lovell |
| Created | 2026-02-06 |
| Last Updated | 2026-02-13 |

---

## Abstract

Cascode's abstractions for IC design (bundles, interfaces, circuits, primitives, benches, constraints, HL/EL levels) map naturally to PCB schematic capture and synthesis. This RFC extends the language with a `part` construct for packaged off-the-shelf components, a metrics system for datasheet-driven and simulation-driven validation, and bus bundles for digital interconnect. Parts support variant blocks for discrete configuration axes (package, tolerance, flash size) and part inheritance (`extends`/`abstract`) for sharing electrical identity across related families. A bracket syntax at the instantiation site (`new Part[axis=option](params)`) visually distinguishes variant selection from constructor parameters, ensuring BOM-readiness is explicit. These additions enable Cascode to represent PCB schematics at both high-level (system architecture with constraint-driven part selection) and electrical level (concrete schematic with specific component values and connections).

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

1. PCB layout, physical placement, pick-and-place data, or gerber generation. This RFC addresses schematic capture and synthesis intent only. BOM generation is in scope as a first-class emit target alongside SPICE netlists; Cascode replaces KiCad/Altium for schematic capture and does not emit artifacts in their formats.
2. Defining the complete external parts sync pipeline implementation. This RFC defines language-facing contracts and pointers.
3. Solving MCU alternate-function/pin-mux firmware configuration in-language.

---

## 3. Conceptual Mapping

The table below summarizes how existing IC Cascode concepts translate to PCB design under the unified `implements` model.

| IC Cascode | PCB Cascode | Notes |
|---|---|---|
| `primitive nfet(size s) implements NMOS` | `part OPA2376 implements DualOpAmp` | Both use `implements` to satisfy an interface contract |
| `lib/ic/` interfaces (`NMOS`, `Resistor`, ...) | `lib/pcb/` interfaces (`NMOS`, `DualOpAmp`, `ADC`, ...) | Domain-specific interface libraries; shared interfaces (`SingleEndedOpAmp`, bus bundles) live in `lib/std/` |
| PDK (`pdk scan`) | Parts library + external pricing/availability sources | Source of available components and their operating/procurement attributes |
| `size(W=2u, L=180n, M=1)` | E-series params for passives (`e96 R`, `e12 C`); no value params for fixed-identity ICs | `size` remains reserved for transistor geometry on primitives |
| `device "sky130_fd_pr__nfet_01v8"` | `catalog { mpn = "OPA2376AIDDBVR" ... }` | `device` directive for primitives; `catalog` block for parts; parts may use variant blocks and `extends` for families |
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

A note on token naming: the existing `PACKAGE_KW` token (bound to the keyword `library`) is renamed to `LIBRARY_KW` to better reflect its purpose. Source files continue to use `library` for library declarations — only the internal token name changes. The word `package` appears only as a field name inside `catalog` blocks on part declarations, so no `PACKAGE_KW` token is needed.

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

`Resistor` appears in both IC and PCB libraries with domain-appropriate contracts. The IC version declares only terminals; the PCB version adds metric requirements for tolerance and power rating. These are separate interfaces in separate namespaces. `include lib.ic` or `include lib.pcb` brings the appropriate definitions into scope. A single `.cas` file should include one domain library, not both. Mixed IC+PCB projects use separate files per domain; cross-domain references use fully-qualified names.

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
  catalog {
    mpn = "ADS1115IDGSR"
    ...
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

---

## 5. The `part` Construct

### 5.1 Syntax

A `part` declaration is a new top-level construct, parallel to `primitive`. It declares a packaged component that implements one or more interfaces and carries sourcing metadata. Parts support two composition mechanisms for managing families of related components: variant blocks for discrete configuration axes within a single declaration, and part inheritance (`extends`/`abstract`) for sharing electrical identity across related families.

```
abstract? part <Name> (<params>?)
    (extends <Parent> (<parent_args>)?)?
    (implements <Interface>(, <Interface>)*)?
{
  (variant <axis> { <option> { ... } ... })*

  catalog {
    mpn = "<manufacturer_part_number_or_template>"
    footprint = "<land_pattern>"
    spice = "<model_ref>"

    option { provider = "<provider>" sku = "<sku>" priority = <int> }
    ...
  }

  <terminal declarations>

  metrics {
    <Metric> = <value>
    ...
  }

  mechanical {
    step = "<path_or_url>"
    dimensions = (<W>, <L>, <H>)
    mass = <quantity>
    mating_force = <quantity>
  }
}
```

A part must have either `extends`, `implements`, or both. Parts with `extends` implicitly inherit the `implements` conformance of their base. The `abstract` keyword marks a base part that cannot be directly instantiated.

Three mechanisms address three concerns:

1. Constructor parameters (`e96 R`, `e12 C`): discrete passive component values constrained to a preferred number series. Continuous parameters (`real`) remain available for non-discrete values. Passed in parentheses at instantiation.
2. Variant blocks (`variant body { ... }`): discrete configuration options (package, tolerance grade, flash size). Selected in square brackets at instantiation.
3. Part inheritance (`extends`): shared electrical identity (ports, base metrics, SPICE model, interface conformance). Declared at the part level.

Passive `part` declarations use E-series parameters (`e96 R`, `e12 C`, `e24 L`). The series constrains the parameter to values in the named IEC 60063 preferred number set. Continuous `real` parameters remain valid for non-discrete values. `size` remains reserved for primitive geometry.

The body contains:

- `variant` blocks: discrete configuration axes. Each block declares named options with per-option catalog field overrides, procurement entries, and metric overrides.
- `catalog {}`: sourcing and physical identity. Contains `mpn` (part lookup and traceability, may be a template string with `{...}` references), `footprint` (physical land pattern), optional `spice` (model reference for simulable parts), and zero or more `option` entries for procurement pointers. Each `option` must contain `provider`, `sku`, and `priority` fields; `url` is optional.
- terminal declarations: physical connectivity.
- `metrics {}`: guaranteed datasheet values and part attributes.
- `mechanical {}` (optional): physical geometry and assembly data. Contains `step` (path or URL to a STEP 3D model file), `dimensions` (W × L × H tuple, e.g., `dimensions = (5.0mm, 3.2mm, 1.5mm)`), `mass` (component mass, e.g., `mass = 0.5g`), and `mating_force` (insertion force for connectors, e.g., `mating_force = 30N`). All fields are optional. Keep-out zones are layout concerns and are out of scope.

The catalog field previously named `package` is renamed to `footprint`. This avoids confusion with variant axis naming and better describes the field's purpose — it identifies the physical land pattern, not a software package.

### 5.2 Variant Blocks

A `variant` block declares a named axis of discrete options within a part declaration. Each option carries per-option metadata: catalog field overrides, procurement entries, and metric overrides.

```cascode
part OPA2376 implements DualOpAmp {
  variant form {
    VSSOP8 {
      mpn = "OPA2376AIDDBVR"
      footprint = "VSSOP-8"
      option { provider = "DigiKey" sku = "296-28003-1-ND" priority = 10 }
      option { provider = "Mouser" sku = "595-OPA2376AIDDBVR" priority = 20 }
    }
    SOIC8 {
      mpn = "OPA2376AIDGKR"
      footprint = "SOIC-8"
      option { provider = "DigiKey" sku = "296-28004-1-ND" priority = 10 }
      option { provider = "Mouser" sku = "595-OPA2376AIDGKR" priority = 20 }
    }
  }

  catalog { spice = "OPA2376" }

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
    SupplyVoltageMin = 2.2V
    SupplyVoltageMax = 5.5V
    SupplyCurrentMax = 950uA
    RecommendedLoadCapacitance = 100pF
  }
}
```

Variant option bodies can contain catalog field overrides (`mpn = "..."`, `footprint = "..."`, `spice = "..."`), procurement entries (`option { provider = "..." sku = "..." priority = N }`), metric overrides (`metrics { MetricName = value }`), arbitrary metadata fields (`key = value` pairs accessible via `{axis.key}` in MPN templates), and exclusion directives.

Not all combinations of variant options across axes produce valid parts. An option may declare `exclude <axis>=<option>` to mark a specific cross-axis combination as invalid. The validator rejects instantiations and synthesis candidates that select excluded combinations.

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

The effective catalog/metrics for a selected configuration is computed by merging base declarations with all selected variant option overrides. Variant fields override base fields of the same name. If two variant axes both provide the same catalog field (e.g., both provide `mpn`), it is a validation error — the field should be a template in the base catalog that references both axes.

Multi-axis parts declare multiple independent variant blocks. The following passive family has body size and tolerance grade as separate axes:

```cascode
part YageoRC(e96 R) implements Resistor {
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

  catalog {
    mpn = "RC{body.footprint}{grade.code}{R}L"
  }

  io P : analog
  io N : analog
  params { R = R }
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

Tolerance-grade variants may further narrow the available series at resolution time. A part declared with `e96 R` supports values up to E96, but a tolerance-grade variant selecting 5% tolerance only manufactures E24 values. This series-tolerance interaction is a resolver concern: the language-level type catches the broadest class of errors (non-standard values), while the parts resolver validates that the specific value exists at the selected tolerance grade.

### 5.4 Part Inheritance

Parts support `extends` for sharing electrical identity. The `abstract` keyword marks a base part that cannot be directly instantiated. Concrete parts inherit ports, metrics, catalog fields, and interface conformance from their base.

```cascode
part abstract STM32G0 implements IMicrocontroller {
  supply VDD
  supply VDDA
  ground VSS
  input NRST : digital
  input BOOT0 : digital
  metrics {
    SupplyVoltageMin = 1.7V
    SupplyVoltageMax = 3.6V
  }
}

part STM32G031K extends STM32G0 {
  variant flash {
    _8 { code = "8"  metrics { FlashSize = 64kB  RAMSize = 8kB } }
    B  { code = "B"  metrics { FlashSize = 128kB  RAMSize = 16kB } }
  }
  variant pkg {
    LQFP32   { footprint = "LQFP-32"  suffix = "T6" }
    UFQFPN32 { footprint = "UFQFPN-32"  suffix = "U6" }
  }

  catalog {
    mpn = "STM32G031K{flash.code}{pkg.suffix}"
    option { provider = "DigiKey" sku = "497-STM32G031K{flash.code}{pkg.suffix}-ND" priority = 10 }
  }

  io PA[0:15] : digital
  io PB[0:9] : digital

  metrics {
    CoreClock = 64MHz
    SupplyCurrentMax = 10mA
  }
}
```

Instantiation: `new STM32G031K[flash=_8, pkg=LQFP32]()` resolves `mpn` to `"STM32G031K8T6"`.

Inheritance rules:

- A part with `extends` inherits ports, metrics, catalog fields, `implements` conformance, and constructor parameters from the base chain.
- An extending part may add ports, metrics, catalog fields, variant blocks, and procurement entries.
- An extending part may override inherited metrics and catalog fields.
- An extending part may add interface conformance: `part Foo extends Bar implements ExtraInterface`.
- `abstract` parts cannot appear in `fill {}` blocks or be instantiated directly.
- The effective declaration is: inherited fields, overridden by own fields, overridden by selected variant fields.

The designer chooses the boundary between inheritance and variants based on what the synthesizer should search. Variant axes are synthesis degrees of freedom: a single `YageoRC` with `variant body { _0402, _0603 }` lets the synthesizer explore both body sizes within one candidate. Separate declarations via `extends` are independent synthesis candidates: `YageoRC0402` and `YageoRC0603` appear independently — the synthesizer does not know they share a base. Both patterns produce the same BOM output (fully resolved MPNs). The difference is in search space structure.

### 5.5 MPN Template Strings

The `mpn` field (and `sku` fields in option entries) can contain `{...}` interpolation references. The language parser treats these as opaque string literals. The parts resolver at tooling level interprets them:

- `{axis}` resolves to the selected option's name as a string (e.g., `{flash}` → `"_8"` or `"B"`).
- `{axis.field}` resolves to a named field on the selected option (e.g., `{pkg.suffix}` → `"T6"`, `{grade.code}` → `"FR-07"`).
- `{param}` resolves to the constructor parameter value, with encoding handled by the parts resolver (e.g., `{R}` for 10 kOhm → resolver applies RKM encoding to produce `"10K"`).

No language-level expression evaluation. The compiler treats template strings as string literals; validation of template references is deferred to the parts resolver.

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

A dual op-amp with single-axis variant (two package options sharing all other attributes):

```cascode
part OPA2376 implements DualOpAmp {
  variant form {
    VSSOP8 {
      mpn = "OPA2376AIDDBVR"
      footprint = "VSSOP-8"
      option { provider = "DigiKey" sku = "296-28003-1-ND" priority = 10 }
      option { provider = "Mouser" sku = "595-OPA2376AIDDBVR" priority = 20 }
    }
    SOIC8 {
      mpn = "OPA2376AIDGKR"
      footprint = "SOIC-8"
      option { provider = "DigiKey" sku = "296-28004-1-ND" priority = 10 }
      option { provider = "Mouser" sku = "595-OPA2376AIDGKR" priority = 20 }
    }
  }

  catalog { spice = "OPA2376" }

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
    SupplyVoltageMin = 2.2V
    SupplyVoltageMax = 5.5V
    SupplyCurrentMax = 950uA
    RecommendedLoadCapacitance = 100pF
  }
}
```

Multi-unit ICs like dual op-amps use flat port naming with per-port direction qualifiers. Each port carries its own `input`, `output`, or `io` direction, preserving signal flow information that a bundle grouping cannot express. The `mpn` and `footprint` fields live inside the variant options because they differ per package; the `spice` model reference and all metrics are shared across both packages and live in the base `catalog` and `metrics` blocks.

A 16-bit I2C ADC without variants (single known MPN):

```cascode
part ADS1115 implements ADCSubsystem {
  catalog {
    mpn = "ADS1115IDGSR"
    footprint = "MSOP-10"

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

An MCU family using inheritance and variants together:

```cascode
part abstract STM32G0 implements IMicrocontroller {
  supply VDD
  supply VDDA
  ground VSS
  input NRST : digital
  input BOOT0 : digital
  metrics {
    SupplyVoltageMin = 1.7V
    SupplyVoltageMax = 3.6V
  }
}

part STM32G031K extends STM32G0 {
  variant flash {
    _8 { code = "8"  metrics { FlashSize = 64kB  RAMSize = 8kB } }
    B  { code = "B"  metrics { FlashSize = 128kB  RAMSize = 16kB } }
  }
  variant pkg {
    LQFP32   { footprint = "LQFP-32"  suffix = "T6" }
    UFQFPN32 { footprint = "UFQFPN-32"  suffix = "U6" }
  }

  catalog {
    mpn = "STM32G031K{flash.code}{pkg.suffix}"
    option { provider = "DigiKey" sku = "497-STM32G031K{flash.code}{pkg.suffix}-ND" priority = 10 }
  }

  io PA[0:15] : digital
  io PB[0:9] : digital

  metrics {
    CoreClock = 64MHz
    SupplyCurrentMax = 10mA
  }
}
```

A USB Type-C connector with mechanical metadata:

```cascode
part USB_C_Receptacle implements USBConnector {
  catalog {
    mpn = "USB4105-GF-A"
    footprint = "USB-C-SMD-16P"
    option { provider = "DigiKey" sku = "2073-USB4105-GF-ACT-ND" priority = 10 }
    option { provider = "Mouser" sku = "640-USB4105-GF-A" priority = 20 }
  }

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

  mechanical {
    step = "models/USB4105-GF-A.step"
    dimensions = (8.94mm, 7.30mm, 3.26mm)
    mass = 1.2g
    mating_force = 8N
  }
}
```

### 5.7 Instantiation

Parts are instantiated using a three-delimiter syntax that distinguishes variant selection from constructor parameters from connectivity:

```
<Interface> <refdes> = new <Part>[<variant_selections>](<params>) { <bindings> }
```

Square brackets for configuration, parentheses for values, braces for connectivity:

```cascode
fill {
  // Single-axis variant: positional selection
  DualOpAmp u1 = new OPA2376[VSSOP8]() {
    .A_INP--sensor_p
    .A_INN--ref
    .A_OUT--stage1_out
    .VDD--VDD
    .GND--GND
  }

  // Multi-axis variant: named selection + value param
  Resistor r1 = new YageoRC[body=_0402, grade=F](R=10k) {
    .P--VDD
    .N--vref
  }

  // No-variant part: brackets omitted
  ADCSubsystem uAdc = new ADS1115() { ... }

  // Inherited part with variants: all axes selected
  IMicrocontroller uMcu = new STM32G031K[flash=_8, pkg=LQFP32]() { ... }
}
```

Single-axis parts can use positional selection (`[VSSOP8]`). Multi-axis parts use named selection (`[body=_0402, grade=F]`). In `fill {}` blocks, all variant axes must be explicitly selected — this is the BOM-readiness guarantee. Omitting any axis in a fill block is a validation error. In `slot {}` blocks, variant selection may be omitted entirely; omitted axes are deferred to synthesis.

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
- `part` declarations carry a `catalog` block with sourcing identity (`mpn`, `footprint`, optional `spice`) and procurement pointers.

The `extends`/`abstract` mechanism and variant blocks apply to parts only. Primitives continue to use `implements` without inheritance — PDK devices do not form part families in the same way as sourced components. A Cascode project may contain both primitives and parts when modeling mixed IC + PCB systems.

### 5.9 Resolution Policy

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

### 6.3 Metric Reference Syntax

Two syntactic forms distinguish metric access by origin:

The dot operator (`instance.Metric`) performs declared metric property lookup on an instance. It is used in constraints, forwarding, and parameter propagation whenever the value comes from a metric declared or bound on a sub-component.

The double-colon operator (`bench::Measurement`) performs bench measurement extraction. It is used exclusively inside metric binding blocks to name a value produced by a bench run. Arguments may follow in parentheses when the measurement requires parameters.

These two forms define the named metric kinds:

A **bench-derived metric binding** has the form `MetricName = benchBinding::Measurement(args?)`. The value is produced by simulation: the bench planner generates a testbench, runs it, and extracts the named measurement. The `::` operator appears only on the right-hand side of a metric assignment inside a `metrics {}` block that sits within a bench bind.

A **forwarded metric alias** has the form `MetricName = instance.Metric`. The value is aliased from a sub-component's declared metric. No simulation runs; the evaluator resolves the value by looking up the named metric on the target instance.

Within a circuit's own constraint block, metrics bound on the same circuit are referenced by bare name (unqualified). The `metrics::` self-reference prefix is not supported.

### 6.4 Constraint References

Constraints reference metrics on instances using the dot operator. Constraints are placed in sub-blocks according to their verification method (see Section 8 for the full taxonomy).

```cascode
constraints {
  bench {
    c_gbw = frontend.PassbandGain >= 40dB
  }
  spec {
    c_resolution = adc.Resolution >= 16 bits
    c_supply_min = adc.SupplyVoltageMin <= 3.3V
    c_supply_max = adc.SupplyVoltageMax >= 3.3V
  }
}
```

`bench {}` constraints must trace to bench-derived metrics; `spec {}` constraints must trace to declared metrics. The evaluator validates this mapping.

### 6.5 Interface Metric Declarations

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

### 6.6 Metric Providers and Forwarding

Metric values come from two explicit provider kinds: bench-derived (simulation) and declared/datasheet. The provider kind is determined by how the metric is bound, not where it is referenced.

Forwarding is supported for wrappers and is alias-only in v1. Forwarded metrics use the dot operator:

```cascode
metrics {
  Resolution = uAdc.Resolution
  MaxSampleRate = uAdc.MaxSampleRate
}
```

Transform expressions in forwarding are deferred.

### 6.7 Variant-Dependent Metrics

When a part declares variant blocks, metric values may come from the base `metrics {}` block, from a variant option's `metrics {}` block, or from both. The effective metric set for a given configuration is computed by merging base metrics with all selected variant option metrics. Variant-provided metrics override base metrics of the same name.

For example, in the `YageoRC` passive family, `PowerRating` and `VoltageRating` depend on the `body` axis, while `Tolerance` depends on the `grade` axis. The base `metrics {}` block is empty; all metrics come from variant options. In `OPA2376`, the base `metrics {}` block provides all values (shared across packages) and no variant options carry metric overrides.

Every interface-required metric must be provided either by the base `metrics {}` block, by inherited metrics via `extends`, or by all variant options across all variant axes collectively. If a metric is provided by some options of an axis but not others, it is a validation error — every valid configuration must produce a complete metric set.

The merge order is: inherited metrics (from `extends` chain), overridden by the part's own base metrics, overridden by selected variant option metrics. When constraints reference variant-dependent metrics (`spec { c_pwr = r.PowerRating >= 50mW }`), the evaluator traces through the instance's selected variant to look up the concrete value.

### 6.8 PCB-Domain Units

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

The constraint system uses distinct sub-blocks organized by verification method. Each sub-block carries clear semantics about how the evaluator verifies the constraint and where the metric value originates. Graph constraints (`graph {}` blocks) were considered during design and are explicitly excluded; structural connectivity requirements are expressed through existing connection and interface mechanisms.

### 8.1 Sub-Block Types

Three constraint sub-blocks are supported:

`bench {}` constrains scalar metrics verified by bench execution (simulation). The bench planner generates a testbench, runs it, extracts a metric value, and compares the result against the stated bound. This replaces the prior `numeric {}` block from the IC-only constraint system.

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

Each part declaration carries checked-in procurement options inside its `catalog` block or inside variant option bodies. Population may come from manufacturer/distributor APIs, distributor CSV exports, or curated internal catalogs, but the language-facing pointer contract is the same.

Required fields per option:

- `provider`
- `sku`
- `priority`

Optional:

- `url`

This allows deterministic fallback and sourcing without brittle URL parsing.

When a part has variant blocks, procurement options may appear in either the base catalog or inside variant options. Package-specific SKUs naturally live inside the variant option that determines the package (e.g., body size determines the DigiKey suffix). SKUs that depend on multiple axes use template strings in the base catalog that reference axis fields.

### 9.4 MPN Template Resolution

Parts with variant blocks and constructor parameters may use template strings in the `mpn` and `sku` fields. The BOM resolution pipeline processes these in order:

1. Walk the EL hierarchy. For each part instance, collect: reference designator path, part family, constructor params, variant selections.
2. Merge base catalog with variant option overrides. Resolve all `{...}` template references using params and variant selections. Produce: concrete MPN, footprint, SPICE model ref, resolved procurement options.
3. Merge base metrics with variant metric overrides. All interface-required metrics must resolve.
4. Aggregate by resolved MPN. Sum quantities. Collect reference designator lists.
5. Emit BOM as structured JSON alongside SPICE netlists.

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

Parameterized passives represent families rather than concrete components. A declaration like `part YageoRC(e96 R) implements Resistor` defines a family whose value domain is constrained to the declared E-series (Section 5.3). Concrete resolution to a sourceable MPN occurs during synthesis or explicit fill-block instantiation. The E-series type ensures that only standard preferred values enter the resolution pipeline. The resolution then considers the validated value, variant selections (body size, tolerance grade), and available catalog options. Variant selections are discrete decision variables alongside the E-series-constrained value parameter in the synthesis search space.

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

Variant selections are synthesis degrees of freedom alongside part selection and value sizing. When a slot block omits variant axes, the synthesizer explores all valid variant combinations for each candidate part. Each (part, variant-combination) pair yields a metric vector; constraints filter infeasible configurations and objectives rank the rest. For a `Resistor` slot, the search space is the Cartesian product of candidate part families, their variant axes (body size, tolerance grade), and the continuous value parameter.

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
OPTION_KW   : 'option' ;
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
// GRAPH_KW is removed. Graph constraints are not supported.
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
      LBRACE partMember* RBRACE
    ;

implementsList
    : IDENT (COMMA IDENT)*
    ;

partMember
    : catalogBlock                                            # PartCatalog
    | paramsBlock                                             # PartParams
    | direction portName COLON portType                       # PartPort
    | SUPPLY_KW IDENT                                         # PartSupply
    | GROUND_KW IDENT                                         # PartGround
    | metricsValueBlock                                       # PartMetrics
    | variantBlock                                            # PartVariant
    | mechanicalBlock                                         # PartMechanical
    ;

mechanicalBlock
    : 'mechanical' LBRACE mechanicalField* RBRACE
    ;

mechanicalField
    : IDENT EQ (STRING | signedQuantity | tupleLiteral)
    ;
```

A part must have either `extends`, `implements`, or both. Abstract parts must have `implements` (they define the contract). Passive `part` declarations use E-series parameters (`e96 R`, `e12 C`, `e24 L`) to constrain values to a preferred number series. `size` remains reserved for primitive geometry.

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
    : IDENT EQ (STRING | signedQuantity)                     # VariantField
    | catalogOption                                          # VariantCatalogOption
    | metricsValueBlock                                      # VariantMetrics
    | excludeDirective                                       # VariantExclude
    ;

excludeDirective
    : 'exclude' IDENT EQ (IDENT | STRING)
    ;
```

Variant option bodies can contain catalog field overrides (`mpn = "..."`, `footprint = "..."`), procurement entries (same `catalogOption` rule as in catalog blocks), metric overrides (`metricsValueBlock`), and arbitrary metadata fields (`key = value` pairs).

### 11.6 Catalog Block

The catalog block groups sourcing and physical identity fields using assignment syntax, with zero or more procurement option entries:

```antlr
catalogBlock
    : CATALOG_KW LBRACE catalogEntry* RBRACE
    ;

catalogEntry
    : IDENT EQ (STRING | NUMBER)                              # CatalogField
    | catalogOption                                           # CatalogOptionEntry
    ;

catalogOption
    : OPTION_KW LBRACE catalogOptionField+ RBRACE
    ;

catalogOptionField
    : IDENT EQ (STRING | NUMBER)
    ;
```

Each `catalogOption` must contain at minimum `provider`, `sku`, and `priority` fields. The `url` field is optional. The `package` field is renamed to `footprint` in all catalog contexts.

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
    : METRICS_KW LBRACE metricAssign* RBRACE
    ;

metricAssign
    : IDENT EQ metricValue
    ;

metricValue
    : signedQuantity
    | metricSource
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

Bench bind blocks may contain their own metrics block to bind measurement results to named metrics:

```antlr
benchBindingMetrics
    : METRICS_KW LBRACE metricBind* RBRACE
    ;

metricBind
    : IDENT EQ metricSource
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

### 11.10 Instance Declaration with Variant Selection

Instance declarations now support an optional variant selection in square brackets between the type name and the argument list. `Some` remains valid only in slot blocks:

```antlr
slotBlockStatement
    : NET_KW IDENT COLON portType                                    # SlotNetDecl
    | slotInstanceDecl                                               # SlotInstanceDecl
    | pinRef WIRE_OP pinRef                                          # SlotConnectDecl
    ;

slotInstanceDecl
    : (declaredType=IDENT | SOME_KW) instanceId=IDENT EQ NEW_KW instanceTypeName
      (LBRACKET variantArgList? RBRACKET)?
      (LPAREN argList? RPAREN)? bindingBlock?
    ;
```

In fill blocks, the existing `instanceDecl` requires a declared type (an interface name) and does not accept `Some`:

```antlr
instanceDecl
    : declaredType=IDENT instanceId=IDENT EQ NEW_KW instanceTypeName
      (LBRACKET variantArgList? RBRACKET)?
      (LPAREN argList? RPAREN)? bindingBlock?
    ;

variantArgList
    : variantArg (COMMA variantArg)*
    ;

variantArg
    : (IDENT EQ)? (IDENT | STRING)                           // named or positional
    ;
```

Single-axis parts can use positional selection (`[VSSOP8]`). Multi-axis parts use named selection (`[body=_0402, grade=F]`). In fill blocks, all variant axes must be explicitly selected. In slot blocks, variant selection may be omitted (deferred to synthesis).

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

There are no reserved keyword categories. All instantiation targets -- whether for primitives, parts, or circuits -- resolve semantically against declarations in scope. The `include` directives determine which interfaces are available. Ambiguous or unresolved targets are hard validation errors.

---

## 12. Worked Example: Sensor Frontend PCB

A complete worked example accompanies this RFC at `tests/golden/cas/pcb/SensorFrontendPCB.cas`. The example includes:

- Single-axis variant on OPA2376 (`variant form { VSSOP8, SOIC8 }`) with positional bracket selection (`[VSSOP8]`).
- Multi-axis variant on YageoRC (`variant body`, `variant grade`) with named bracket selection (`[body=_0402, grade=F]`).
- Part inheritance: abstract `STM32G0` base with concrete `STM32G031K extends STM32G0`, carrying `variant flash` and `variant pkg` axes.
- MPN template strings (`"STM32G031K{flash.code}{pkg.suffix}"`, `"RC{body.footprint}{grade.code}{R}L"`) with field and parameter interpolation.
- No-variant part (ADS1115) demonstrating that brackets are optional when no variant axes exist.
- Fill-block variant enforcement: all axes explicitly selected in every fill-block instantiation.
- `footprint` catalog field (renamed from `package`).
- 16-bit ADC and MCU wrappers with forwarded metrics using dot syntax (`uAdc.Resolution`).
- `bench`/`spec` constraint taxonomy separating simulation-verified and declaration-verified constraints, using dot syntax for instance metric references (`frontend.PassbandGain`, `adc.Resolution`, `mcu.FlashSize`).
- Hierarchical constraint verification (EL design targets with bare metric names, HL system requirements with instance-qualified references).
- Metric-driven parameter propagation (`load_cap=adc.InputCapacitance`), resolved during bench planning via an ordering pass.
- Bench-derived metric bindings (`PassbandGain = transfer_bench::PassbandGain`) using `::` exclusively for bench extraction.

The example intentionally exercises both simulable and non-simulable paths within one HL composition, demonstrating variant selection, part inheritance, and the constraint taxonomy across verification methods and hierarchy levels.

---

## 13. Language Specification Deliverables

This section mandates updates to the Cascode language specification (`spec/language/`) for the constructs and conventions introduced by this RFC. The deliverables comprise updates to existing spec chapters for new and renamed constructs, and a new domain-application chapter for PCB design.

### 13.1 Rationale for a Dedicated Chapter

The constructs this RFC introduces — `part`, variant blocks, part inheritance (`extends`/`abstract`), metrics, `spec {}` constraints, array ports, the `implements` migration on primitives — are general-purpose language mechanisms whose normative definitions belong in Chapters 2 and 3. The PCB domain, however, introduces enough workflow, conventions, and domain-specific patterns that scattering the material across general-purpose sections would not serve PCB designers well. A dedicated Chapter 5 provides the domain-application guide: how these constructs compose for PCB schematic capture, PCB-specific conventions, and topics unique to the domain (parts ecosystem, pricing, passive network synthesis, mixed simulable/non-simulable verification).

### 13.2 New Chapter: Ch05_PCB_Design.md

The chapter covers the following topics:

Section 5.0 (Summary) establishes the chapter's purpose and relates Cascode's existing abstractions to PCB schematic capture and synthesis.

Section 5.1 (Conceptual Mapping) expands the IC-to-PCB mapping table from Section 3 of this RFC into prose. Key themes: schematic symbol vs concrete backing, `implements` as the unifying mechanism, and where IC and PCB flows diverge in identity, parameters, and metric sources.

Section 5.2 (Domain Libraries and Namespace Convention) describes the organization of `lib/ic/`, `lib/pcb/`, `lib/std/`, and `lib/parts/`. Covers the single-domain-per-file convention and cross-references Ch02 Section 2.1 for the underlying resolution model.

Section 5.3 (The `part` Construct) gives a domain-focused treatment of the `part` declaration, cross-referencing Ch02 and Ch03 for normative definitions. Covers the relationship to `primitive`, the `mpn`/`footprint`/`spice` fields, variant blocks for discrete configuration axes, part inheritance (`extends`/`abstract`) for part families, MPN template strings, bracket instantiation syntax, parameterized passives vs fixed-identity ICs, multi-unit ICs with flat port naming, array ports on high-pin-count components, and worked examples.

Section 5.4 (The Metrics System in PCB Context) describes how metrics enable datasheet-driven and simulation-driven validation. Covers datasheet metric polarity conventions, interface metric contracts, the two named metric kinds (bench-derived and forwarded), metric-driven parameter propagation, and PCB-domain units.

Section 5.5 (Constraint Taxonomy for Mixed Designs) explains how `bench {}`, `spec {}`, and `physical {}` sub-blocks partition constraints by verification method. Covers the bench-to-spec distinction, verification provenance enforcement, mixed compositions with simulable and non-simulable sub-blocks, hierarchical constraint verification, and self-metric references by bare name.

Section 5.6 (Bus Bundles and Digital Interconnect) treats standard PCB buses (I2C, SPI, UART, SWD) as ordinary bundles. Points to `lib/std/bus/` and shows connection patterns, cross-referencing Ch02 Section 2.3 for the bundle mechanism.

Section 5.7 (Parts Ecosystem and Pricing) describes the parts database role, `lib/parts/` tree structure organized by category (including `lib/parts/power/` for regulators and decoupling, `lib/parts/conn/` for connectors), the `catalog` block's `option` contract (required fields: `provider`, `sku`, `priority`; optional: `url`), the `mechanical {}` block for physical geometry and assembly data, and passive resolution.

Section 5.8 (PCB Synthesis Model) covers HL-to-EL synthesis for PCB designs: IC selection against metric constraints, passive network topology and value sizing, and mixed-block synthesis. Describes the `synth {}` block's `objective` directive and extensibility for future synthesis directives, cross-referencing Ch02 Section 2.12.

Section 5.9 (Worked Example: Sensor Frontend PCB) walks through the `SensorFrontendPCB.cas` golden test section by section, covering file structure and includes, part declarations, interface contracts with metric declarations, HL composition with mixed bench/spec constraints and metric-driven parameter propagation, EL implementations with bench-derived and forwarded metrics, and the hierarchical verification flow.

### 13.3 Updates to Existing Spec Chapters

The specification already covers HL composition slots (2.5.5), the `Some` keyword (3.10.3), bench bindings with measurement exports (4.8.5), `implements` on circuits (2.4), and the `synth {}` block (2.12). The updates below target only what is genuinely new or renamed.

Ch01: add a brief note in Section 1.5 (Cascode in a Few Examples) that PCB design is covered in Ch05, or include a minimal PCB example. In Section 1.6 (Toolchain Pipeline), note that the pipeline extends to PCB schematic capture and constraint-driven part selection.

Ch02 new constructs: add `part` to the Section 2.2 top-level declaration list. Add Section 2.5.3 for E-series parameter types (`e6` through `e192`) in the parameter type system, covering the subtype hierarchy and compile-time validation. Add Section 2.6.2 for parts (`mpn`, `footprint`, `spice`, `catalog` fields, variant blocks, part inheritance, MPN templates, parameterized vs fixed-identity). Add a new section for the metrics system (interface metric declarations, part/circuit metric value blocks, the two named metric kinds, variant-dependent metrics, metric-driven parameter propagation). Add PCB-domain units (`pct`, `SPS`, `bits`, `LSB`, `B`) to Section 2.9.

Ch02 renames and extensions: rewrite Section 2.6 (Primitives) to use `implements` syntax. Rename `numeric {}` → `bench {}` in Section 2.7.1. Add a new Section 2.7.x for the `spec {}` sub-block with dot-operator metric lookup and bare-name self-references. Rename `tech {}` → `physical {}`. Add a note to Section 2.5.5 about metric-driven parameter propagation between slot sub-blocks.

Ch03 new syntax: update Section 3.1 to include `partDef`. Add `eSeriesType` grammar rule in the parameter declarations section. Add new sections for part declarations (`partDef`, `partMember`, `catalogBlock`, `catalogOption`), metrics blocks (`metricsValueBlock`, `interfaceMetricsBlock`, `benchBindingMetrics`), metric references (`instanceMetricRef`, `benchMetricRef`), and array ports (`portDecl` with range, `portIndexRef`).

Ch03 renames: rewrite Section 3.8 primitive header to `implements`. Merge Section 3.9 (Device Declarations) into instance declarations (3.10.2). Update Section 3.11 (Constraints) for `bench`/`spec`/`physical` renames, dot-operator constraint references, `LIBRARY_KW` rename, and `GRAPH_KW` removal.

Ch04: add a note at Section 4.8.x about `metrics {}` blocks inside bench bindings, distinct from the existing `measurements {}` exports (4.8.5). The `metrics {}` block maps interface-level metric names to bench measurements; `measurements {}` defines computed derived measurements; both may appear in a single binding.

`spec/language/README.md`: add Chapter 5 to the chapter listing.

---

## 14. Implementation Plan

Implementation is split into phases. Phase 1 covers grammar and AST changes in three sub-phases: additive (1a), breaking (1b), and core spec updates (1c). Phase 2 covers interface libraries (2a) and the PCB spec chapter (2b).

Phase 1a: Additive grammar and AST

- Add `part`, `catalog`, `metrics`, `Some` grammar support.
- Add `eSeriesType` tokens (`E6_KW` through `E192_KW`) and `eSeriesType` grammar rule; extend `paramType` to accept E-series types.
- Add `implementsList` rule shared by `primitiveDef`, `partDef`, and `circuitDef`.
- Add `variant`, `abstract`, `extends` grammar support: `VARIANT_KW`, `ABSTRACT_KW`, `EXTENDS_KW` tokens; `variantBlock`, `variantOption`, `variantOptionMember` rules (including `excludeDirective`); modified `partDef` with optional `ABSTRACT_KW`, optional `EXTENDS_KW` clause.
- Add bracket variant selection syntax on `instanceDecl` and `slotInstanceDecl`: `variantArgList`, `variantArg` rules with `LBRACKET`/`RBRACKET` delimiters.
- Add `mechanicalBlock` and `mechanicalField` rules as optional `partMember` alternatives.
- Add `spec` constraint sub-block.
- Add `benchBindingMetrics` rule for metrics inside bench bind blocks.
- Add array port declaration and indexing syntax.
- Add `LIBRARY_KW`, `CATALOG_KW`, `OPTION_KW`, `BENCHES_KW` tokens.
- Add AST types for part declarations, catalog entries, metric declarations/assignments, variant blocks, variant options.
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
- Implement variant selection validation: fill blocks require all axes selected; slot blocks allow omission.
- Implement catalog/metric merge: base + variant option overrides with conflict detection (two axes providing the same non-template catalog field).
- Implement variant exclusion validation: reject instantiations and synthesis candidates selecting combinations marked by `exclude` directives.
- Implement `abstract` part enforcement: cannot appear in fill blocks or be instantiated directly.
- Implement inheritance chain resolution: collect ports, metrics, catalog fields, `implements` conformance from base chain.
- Implement MPN template string validation: verify all `{...}` references resolve to known axes, axis fields, or constructor parameters.
- Add interface metric contract validation (including variant-dependent metrics must cover all options).
- Add alias-only forwarding resolution and cycle detection.
- Validate `Some` only appears in slot blocks (grammar-enforced).
- Add E-series value validation: check that a literal value passed to an E-series parameter is a member of the declared series at some decade.

Phase 4: Constraint and runtime evaluation

- Extend evaluators to consume metric references (`instance.Metric` for property lookup, `bench::Measurement` for extraction) from both bench and declared sources.
- Implement `bench` constraint evaluation (simulation-verified) and `spec` constraint evaluation (declaration-verified).
- Implement verification provenance validation (constraints in the correct sub-block for their metric source).
- Implement metric-driven parameter propagation ordering pass during bench planning.
- Implement hierarchical verification: `cascode bench run` walks the composition tree and evaluates constraints at every level.

Phase 5: Parts ecosystem integration

- Implement MPN template resolution: `{axis}`, `{axis.field}`, `{param}` interpolation with value encoding (RKM for resistance, pF codes for capacitance).
- Implement BOM resolution pipeline: instance collection → catalog merge → template resolution → metric merge → aggregation → JSON emission (per schema in Section 9.4).
- Wire catalog option pointers to provider adapters/cache.
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
