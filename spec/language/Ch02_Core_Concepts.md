# Chapter 2: Core Concepts

> This chapter defines the **semantic scaffolding** of *cascode*: the building blocks, how they relate, and the invariants the compiler and tools rely upon. Syntax is shown informally; the formal grammar appears in Chapter 11.
> Normative keywords **MUST**, **MUST NOT**, **SHOULD**, **MAY** follow [RFC 2119](https://www.ietf.org/rfc/rfc2119.txt).

---

## 2.1 Programs, Packages, and Imports

A **program** comprises one or more `.cas` files organized under a library namespace. The `library` declaration establishes the namespace, while `import` statements bring external names into the current scope. Name resolution follows **lexical scoping** principles with library-qualified fallback, and shadowing rules mirror Java and C# conventions unless explicitly stated otherwise.

---

## 2.2 Modules, Motifs, Interfaces

The cascode type system distinguishes three fundamental entities. A **module** represents a top-level design entity that encompasses **ports**, **parameters**, optional **use** blocks for instantiation, **connect** and **cascade** statements for wiring, **spec**, **env**, and **bench** blocks for behavioral specification, and optional **slot** and **synth** directives for synthesis. A **motif** is a synthesizable structural unit with defined **ports**, **params**, and **contracts**; it encapsulates internal structure and is eligible for topology selection when it **implements** an interface. Motifs may be authored natively within cascode or integrated via **`wrap spice`** constructs. An **interface** may be either:

1) a **spec-only interface** that declares canonical metric names and contains no port definitions, or
2) an **interface** that extends a spec-only interface, adding ports and mapping metrics to concrete bench outputs (see §2.11.3).

This separation enables substitution during synthesis - for instance, any entity implementing `SingleEndedOpAmp` becomes eligible to fill `slot Core: SingleEndedOpAmp` while sharing the same metric names as `Amplifier`.

#### Normative Requirements

* An entity implementing an interface **MUST** expose a **superset** of the interface's ports/bundles and satisfy its declared **contracts** (2.10).
* A module is **instantiable** only when all required ports are bound and all declared `slot`s are **filled** (structurally or via `synth`).
* A motif **MUST NOT** contain `spec {}` or `bench {}` blocks.
* A slot **MUST** be typed by an interface that declares ports. Typing a slot by a spec-only interface is an error.

Motifs represent synthesizable circuit topologies and must remain pure structural definitions. Behavioral specifications and verification benches belong at the module level, where they guide synthesis decisions, or within enclosing harnesses that validate composed designs. Similarly, slots function as structural placeholders in the wiring graph and require interfaces that provide the port definitions necessary for establishing connections. Spec-only interfaces, which define behavioral contracts without physical port structure, cannot satisfy this requirement.

#### Library Placement

The standard primitive interfaces used by connectors - such as `DiffPairLike`, `CascodePairLike`, and `CurrentMirrorLike` - reside in the primitive library namespace (`lib/std/prim`). Primitives and their interfaces co‑locate to maintain proximity between wiring semantics and the building blocks they compose.

#### Trait Extension

* Use `extend` to define an interface from a spec-only interface:
  `interface SingleEndedOpAmp extend Amplifier { … }`.
* Extension composes metric sets: the child inherits all canonical metric names from the parent. Child interfaces may add metric mappings and additional ports but MUST NOT remove or rename metrics inherited from the parent.
* Interfaces MAY declare parameters that influence their port shape (for example, `taps:int` on `CurrentMirrorLike`). A motif that implements such an interface **MUST** declare parameters with the same names and compatible domains. The realized port set of the implementing motif **MUST** be a superset of the interface's port family evaluated at the same parameter values.

---

## 2.3 Port Kinds, Roles, and Net Types

#### Port kinds (non-exhaustive)

* `supply`, `ground` - special; **MUST NOT** short to signal types.
* `signal` - general-purpose signal net (replaces the former `electrical` domain).
* `analog` - continuous-valued signals (amplifier I/O, bias references); subtype of `signal`.
* `digital` - discrete-valued signals (stdcell ports, logic nets); subtype of `signal`.
* `mixed` - explicit domain boundary signals (comparator outputs, ADC/DAC interfaces); subtype of `signal`.
* `diff` - differential bundle abstraction (has fields `.P`/`.N`).
* `bias` - bias/control nets (typed for headroom/legality checks); resolved to specific DC voltage values during ML→EL elaboration (see §3.6.1).
* `rf`, `clock` - specialized kinds with additional contracts (impedance, phase/timing).

#### Signal Type Hierarchy

The signal types form a subtype hierarchy: `analog`, `digital`, and `mixed` are all subtypes of `signal`. Connection compatibility follows these rules:

* A `signal`-typed port accepts connections from `signal`, `analog`, `digital`, or `mixed` nets.
* An `analog`-typed port accepts only `analog` or `signal` nets.
* A `digital`-typed port accepts only `digital` or `signal` nets.
* A `mixed`-typed port accepts any signal subtype, marking intentional domain crossings.

When no domain is explicitly specified, nets default to `signal`.

#### Roles

Ports and nets may carry semantic **roles** such as `stage1_out`, `ota_out`, or `cmfb_ctrl` that provide semantic context beyond basic connectivity. These role annotations guide automated **pattern recognition**, enable targeted **contract** enforcement, and inform **benchmark generation** strategies.

#### Normative

* Port-kind **compatibility** is enforced at connect time (e.g., `bias→gate` inside motifs is allowed; `bias→out` is forbidden unless a motif explicitly exposes this).
* Each `supply`/`ground` port **MUST** connect to exactly one global net per instantiation context.

### 2.3.1 Port Naming Conventions

To improve readability and keep interfaces visually distinct from internal nets, the specification adopts the following convention:

- External ports exported by modules and motifs use ALL_CAPS_WITH_UNDERSCORES (e.g., `IN_P`, `IN_N`, `OUT`, `OUT_L`, `OUT_R`).
- Rails remain `VDD`/`GND` (already ALL CAPS).
- Internal nets and instance locals inside `use {}` blocks use lowerCamelCase.

This convention is normative for the standard libraries and examples and SHOULD be followed by user code for consistency. Bundles default to PascalCase unless they mirror normative ALL_CAPS ports; the standard `Diff` bundle fixes its fields to `P` and `N`.

---

## 2.4 Bundles (Structured Port Groups)

A **Bundle** is a typed, named group of ports (e.g., a differential pair or an amplifier I/O interface).

```cas
bundle Diff   { P: analog; N: analog; }
bundle AmpIO  { IN: Diff; OUT: analog; }
```

The `Diff` bundle is normative and its fields **MUST** be named `P` and `N`. Bundles that wrap normative ports (such as `AmpIO`) adopt the ALL_CAPS naming used for external ports so that binding syntax aligns with Chapter 2.3.1. Custom bundles that introduce ad-hoc groupings may select their own field casing, but the chosen style **MUST** remain consistent wherever that bundle appears.

Bundles serve two primary purposes: they **reduce verbosity** while making **binding explicit** without ambiguity. These constructs are valid within module ports, motif ports, `slot` interface definitions, and `bind` statements.

#### Normative

* Binding a bundle **MUST** map all required fields (compile-time error if any are missing).
* Bundle field kinds **MUST** match (structural subtyping is **not** implied).

---

## 2.5 Parameters and Defaults

#### Parameters

Parameters (declared via `param` or `params`) define compile-time tunables for modules and motifs. These parameters may be typed as `bool`, `int`, `real`, `enum`, `polarity`, or **unit-typed** quantities. While default values may be provided, required parameters must be explicitly set at instantiation time.

```cas
param CL = 2pF;              // module parameter
params { enabled: bool=true; m:int=1; }          // motif parameters
```

### 2.5.1 Native Polarity Type and Bulk Policy

In addition to `bool`, `int`, `real`, and enums, the language defines a primitive `polarity` type with literals `NMOS` and `PMOS`. This enables concise, type‑checked parameterization of device polarity and supports the `new MOS(polarity)` constructor sugar in §2.8.1.

Bulk policy: When a primitive MOS is instantiated without an explicit bulk binding, the compiler applies a target‑tech policy (default: tie NMOS bulk to ground domain and PMOS bulk to supply domain in benches; technology adapters may override). This allows reusable motifs (like DiffPair) to avoid hard‑coding rails and remain stackable (e.g., Gilbert cells) while still producing legal SPICE.

Example: "BASE" semantics

Some motifs provide a stable external name for a configuration‑dependent internal junction. For the differential pair above, `BASE` is defined as:

- when `hasTail==false`: the common source of the pair;
- when `hasTail==true`: the bottom of the internal tail device.

The intermediate node between the pair and the tail is internal when the tail is present. This keeps the interface small while remaining composable.

---

## 2.6 Units and Dimensions

Literals may specify **units** including voltage (`V`), current (`A`), capacitance (`F`), inductance (`H`), frequency (`Hz`), gain (`dB`), phase (`deg`), time (`s`, `ps`), power (`mW`), and noise density (`nV/√Hz`). The compiler enforces **dimensional consistency** across all expressions and specifications. SI prefixes undergo automatic conversion, while non-linear units such as decibels are treated as scalars with semantics defined per metric (detailed in Chapter 5).

```cas
supply VDD = 1.2V;
spec { GainBandwidth>=100MHz; PassbandGain>=70dB; PhaseMargin>=60deg; Power<=1mW; }
```

---

## 2.7 Instances and Connections (Explicit Binding)

#### Instances

The `use {}` construct creates motif and module instances through `new` expressions with **inline field binding**. The language mandates that all cross-instance connections be explicit, accomplished through `bind`, `connect`, or `cascade` statements.

#### Explicit binding (mandatory)

The language requires that **all** `slot` bindings and cross-instance connections be explicitly specified. **Auto-binding by name or role is strictly prohibited** to ensure design intent remains unambiguous.

```cas
slot Core: AmplifierStage bind { IN -> IN; OUT -> OUT; }   // bundle-to-bundle binding
connect A.OUT -> B.IN;                                 // explicit net connection
```

Style convention (normative for repo sources): binds, connect statements, and attach mappings are written as `pin -> net`. The grammar continues to accept `<-` for parsing compatibility, but the standard library and documentation adhere to `->`.

#### Attach and connectors (unified)

Connectors are declared on interfaces and define how two instances that implement compatible interfaces wire together. There are two forms:

1) Within‑interface connector (both sides implement the same interface):

```cas
interface AmplifierStage extend Amplifier {
  ports [ IN: Diff, OUT: analog ]
  connector { OUT -> IN; }
}
```

2) Cross‑interface connector (left implements this interface; right implements the target interface):

```cas
interface CurrentMirrorLike {
  params { taps: int = 1; }
  ports [ SENSE: analog ]
  ports { for i in [0:taps] { TAP[i]: analog; } }
  connector to DiffPairLike { SENSE -> OUT.N; TAP[0] -> OUT.P }
}

`TAP[0]` is the primary tap exposed by connectors. Additional taps (`TAP[1]`, `TAP[2]`, …) are optional and are wired explicitly by the author when needed.
```

Semantics (normative):

- Arrows map a source port/bundle field on the left to a sink on the right. Unnamed bundle arrows expand field‑wise by identical field names (PascalCase; Diff uses P/N).
- Only interfaces may declare connectors; spec‑only interfaces MUST NOT. An interface declares at most one connector block per target interface (including the within‑interface case).
- attach A to B resolves exactly one applicable connector from interfaces implemented by A and B. If none match, an explicit attach block is required. If more than one matches, the binding MUST be disambiguated using `attach using InterfaceName A to B` or by providing an explicit attach block.
- `attach A to B to C` chains pairwise: (A,B), then (B,C). There is no transitive propagation.
- Connectors expand to explicit `connect` statements; they never create new nets.

#### Alias

The `alias` construct may expose internal nets as top-level ports to improve design clarity, but aliases do not introduce auto-binding behavior.

#### Normative

* If no connector applies for a pair, `attach` without a block **MUST** be rejected (use explicit `attach { … }` or add a connector).
* Binding a bundle **MUST** bind all fields; partial binding is an error.

---

## 2.8 Structural Composition Primitives

### Schematic-like sugar (all expand to primitives in ACIR)

- `attach` - bind one instance to another using either a connector or an explicit mapping block. Chains are allowed via `attach A to B to C`.

  ```cas
  // Connector-driven attach (no explicit mapping needed):
  cm = new CurrentMirror { p=PMOS; taps=1; };
  attach cm to dp;

  // Explicit mapping remains available:
  attach CascodePair to dp { SOURCE -> OUT; BIAS -> VB_CASC }
  ```

- `pair` - instantiate symmetric left/right branches with `.l`/`.r` handles. (Omitted here; see Grammar.)

### 2.8.1 Param‑Variants (sugar to avoid nested conditionals)

Parameter‑driven structural choices are common (e.g., a device `p∈{NMOS,PMOS}` or a compensator style). To avoid verbose nested `if` blocks, the language introduces two pieces of sugar that desugar prior to ACIR emission:

1) Polarity‑polymorphic constructors (native type)

   ```cas
   // new MOS(p) chooses NMOS or PMOS primitive at elaboration
   T = new MOS(p) { gate -> vin; drain -> vout; source -> ref; bulk -> ref; };
   ```

2) Variant blocks over enum parameters

   ```cas
   match style {
     case A: { /* variant A */ }
     case B: { /* variant B */ }
   }
   ```

Both forms expand to ordinary motif and primitive instantiations; they introduce no new runtime semantics.

### 2.8.2 Computed Ports (ports[…] and ports{…})

To keep interfaces concise yet expressive, ports are declared in two blocks:

- `ports[…]` lists mandatory ports that are always present.
- `ports { … }` is an evaluable block where parameter‑dependent logic may declare additional ports.

Example:

```cas
motif DiffPair {
  params { p: polarity = NMOS; hasTail: bool = true; }
  ports [ IN: Diff, OUT: Diff, BASE: analog ]
  ports { if (hasTail) { BIAS: bias; } }
}
```

Normative

- Declarations inside `ports { … }` are evaluated at elaboration time. Names declared in `ports[…]` are always present.
- Conditional ports are compile‑time; call sites MUST bind all ports that exist after evaluation; names not declared MUST NOT be referenced.
- ACIR contains only the realized port set.

### 2.8.3 Repeat Blocks and Indexed Ports

Structural sugar includes two loop forms that elaborate to explicit instances and ports:

- `for i in [start:end] { port[i]: … }` inside `ports { … }` declares a family of ports indexed from `start` to `end-1`. Indices must be compile-time integers.
- `repeat idx in [start:end] { … }` inside `use { … }` clones the enclosed block for each index in the same range. The desugared instances are named by appending `_[idx]`.

Example (multi-tap current mirror excerpt):

```cas
ports {
  RAIL: supply;
  for i in [0:taps] { TAP[i]: analog; }
}

use {
  M_SENSE = new MOS(p) { drain -> SENSE; gate -> SENSE; source -> RAIL; };
  repeat tap in [0:taps] {
    M_TAP[tap] = new MOS(p) { mult = ratio } { drain -> TAP[tap]; gate -> SENSE; source -> RAIL; };
  }
}
```

Normative

- Loop bounds MUST evaluate to integers during elaboration; negative or zero-length intervals are rejected.
- Port families declared with `for` create deterministic names `name[index]`. The implementing entity MUST bind every generated port.
- `repeat` clones emit distinct instances; statements inside the block behave as if written explicitly for each index after substituting the loop variable.
- Parameters that drive loop bounds (such as `taps` on `CurrentMirror`) MUST be ≥1. The total drive strength of a mirror is the per-tap `ratio` multiplied by the number of generated taps; additional taps beyond index `0` require explicit wiring.

### 2.8.4 Attach Name Resolution

Within an `attach X to target { … }` block, identifiers on the left‑hand side of bindings refer to the attached motif `X` and must name its ports. Identifiers on the right‑hand side resolve in two steps: first against the public ports of `target` (including bundle fields like `OUT.N`/`OUT.P`), then against names in the surrounding module scope (nets and ports). If a right‑hand identifier would resolve in both places, the reference must be qualified (for example, `target.OUT.N`). Names that cannot be resolved after this search are errors.

Example (attaching a current mirror to a differential pair):

```cas
cm = new CurrentMirror { p=PMOS; taps=2; ratio=2; };
dp = new DiffPair     { p=NMOS;  hasTail=true; } { IN.P -> VINP; IN.N -> VINN; BASE -> GND; BIAS -> VTAIL; };
attach cm to dp { SENSE -> OUT.N; TAP[0] -> OUT.P };
connect cm.TAP[1] -> cascodeBias;
```

Here `SENSE` and `TAP[0]` are ports of `cm`, while `OUT.N` and `OUT.P` resolve to `dp.OUT.N` and `dp.OUT.P` by the target‑first rule. Additional taps appear as `TAP[1]`, `TAP[2]`, … and must be wired explicitly. If the surrounding scope also declares `OUT.P`, the binding must be written as `TAP[0] -> dp.OUT.P` to disambiguate.

#### 2.8.a Scoped Orientation on Pair‑Like Targets

Orientation sugar is permitted only when the attachment is unambiguously "pair‑like." Two cases qualify:

1) Bundle match. Both the attached motif and the target expose a common pair bundle (for example, `Diff { P, N }`). Writing a single bundle binding implies field‑wise mapping:

```cas
// Both sides declare Diff bundles.
motif CascodePair { ports [ DRAIN: Diff, SOURCE: Diff, BIAS: bias ] }
attach CascodePair to dp { SOURCE -> OUT }   // expands to SOURCE.P->OUT.P; SOURCE.N->OUT.N
```

Reversed orientation in this case requires explicit field mapping (for example, `IN.P -> OUT.N; IN.N -> OUT.P`).

2) Name match. The attached motif and the target share identical port names and compatible kinds for all required connections. To avoid surprises, orientation‑by‑name is opt‑in using an explicit directive:

```cas
motif Probe { ports [ OUT: Diff ] }
attach Probe to dp by name;   // binds OUT.P->dp.OUT.P; OUT.N->dp.OUT.N
```

Outside these cases, users MUST bind complementary ports explicitly. For example, attaching a `CurrentMirror` (ports `SENSE`, `TAP[0]`) to a `DiffPair` (port `OUT: Diff`) requires explicit mapping:

```cas
attach cm to dp { SENSE -> OUT.N; TAP[0] -> OUT.P }
```

Name resolution inside `attach` follows §2.8.4 (left‑hand identifiers refer to the attached motif; right‑hand identifiers resolve to the target first, then the surrounding scope, with qualification required on ambiguity).

### 2.8.5 Family Mapping in `attach` (Wildcards, Ranges, Lists)

Attach blocks may bind families of pins concisely using wildcards, ranges, and ordered lists. All forms elaborate to explicit `connect` statements.

Syntax (informal)

- Family selectors on the left‑hand side (LHS):
  - `TAP[*]` — all indices in the realized family.
  - `TAP[a..b]` — indices `a, a+1, …, b-1` (half‑open range; `b>a`).
  - `TAP[{i0,i1,…}]` — explicit ordered index list.

- Right‑hand side (RHS) forms:
  - Single pin path (e.g., `OUT.P`).
  - Family selector (e.g., `OUTS[*]`, `OUTS[a..b]`).
  - Bundle field list: `OUT.{P,N}` — ordered list of bundle fields.

Semantics (normative)

- Fan‑in: `TAP[family] -> SINK` (RHS single pin) expands to `connect TAP[i] -> SINK` for each index `i` selected on the LHS. Multiple identical connects to the same pair are deduplicated in ACIR.
- Elementwise: `LHS family -> RHS family/field list` requires equal cardinality after evaluation. Expansion pairs items in order (first‑to‑first, second‑to‑second, …).
- Family selectors are evaluated after parameter evaluation (for example, `taps`). Out‑of‑range indices and negative bounds are errors. Zero‑length families are errors.
- Bundle field lists MUST reference existing fields and the count MUST match the LHS family size.
- Bracketed indices (e.g., `TAP[0]`) are part of the pin path (see §3.3 Pin Path Grammar) and introduce no array semantics at runtime.

Examples

```cas
// Fan‑in: all taps feed a single node.
attach cm to dp { SENSE -> OUT.N; TAP[*] -> OUT.P }

// Elementwise: tap i to sink i (requires OUTS[i] ports).
attach cm to arr { TAP[*] -> OUTS[*] }

// Elementwise: two taps to bundle fields, ordered.
attach cm2 to dp { TAP[0..2] -> OUT.{N, P} }

// Reordered mapping.
attach cm2 to dp { TAP[{1,0}] -> OUT.{P, N} }
```

#### Acyclicity

Structural nets maintain an **acyclic** topology unless a motif explicitly permits legal loops (such as cross-coupled latches). The compiler enforces acyclicity constraints during elaboration.

---

## 2.9 Compensation as a Stage Property

Compensation is a **configuration of the stage**, not a separate module.

```cas
// Stages implementing AmplifierStage expose:
Core.comp { style=MillerRC | MillerRz | Ahuja | None | Auto;
            Cc: capacitance = Auto; Rz: resistance = Auto; }
```

The compiler **realizes** compensation **internally** within the stage through dedicated devices or by selecting compensated variants from the library. From an external perspective, compensation manifests solely as a *property* of the stage.

#### Normative

* If `Core.comp None;` is set, no compensation circuitry may be realized.
* Supported styles and parameter semantics **MUST** be documented by the chosen stage’s library entry.

---

## 2.10 Contracts and Patterns

#### Contracts

Contracts encapsulate boundary assumptions (`req`) and guarantees (`ens`) for motifs and modules. Examples include `req Headroom>=0.35V`, `ens PassbandGain>=20`, and `ens ICMR in [0.4V..0.9V]`.

#### Patterns

Patterns define recognizers and binders for canonical subgraphs (such as 5T current mirrors), enabling automated ingestion from SPICE netlists and canonicalization into structured motifs.

#### Normative

* Tools **MUST** enforce `req` at instantiation given `env{}`; violations are compile-time errors.
* `ens` are used for feasibility/search; violations found during verification **MUST** be reported.

---

## 2.11 Behavioral Description: `spec`, `env`, `bench` and the **Harness**

The **`spec {}`** block enumerates **required metrics** including GainBandwidth, PhaseMargin, PassbandGain, input‑referred noise (NoiseIn), slew rate (SlewRate), settling time (Settle), zero‑tau frequency (ZeroTau), output swing (OutputSwing(node)), and power consumption (Power). The **`env {}`** block characterizes the **operating environment** through supply voltage (`vdd`), input common‑mode range (ICMR), mandatory load specifications, mandatory source impedance, temperature, and process corners. The **`bench {}`** block selects additional measurement benches beyond those implied by the specification.

Bench inference from `spec` (normative)

When a design declares a `spec {}` block, the toolchain infers the minimal set of benches required to determine each declared metric. This set is part of the compilation contract and is independent of any explicit `bench {}` block. Authors may add an explicit `bench {}` block to request extra benches (for example, characterization or debugging). In that case, the executed benches are the union of the spec‑implied set and the explicitly requested set. Explicit benches do not remove or replace benches inferred from `spec {}` unless the toolchain provides a documented override.

### 2.11.3 Metrics From Benches (Interface‑Anchored Mapping)

Benches produce named metrics. Interfaces map canonical metric names from a spec‑only interface to concrete bench metrics for their wiring style. This allows a single set of metric names (for example, GainBandwidth, PassbandGain, PhaseMargin) to be realized via different benches for single‑ended versus fully differential interfaces.

Informal syntax:

```
// Spec‑only interface (no ports): declares canonical metric names.
interface Amplifier { metrics { GainBandwidth; PassbandGain; PhaseMargin; ICMR; Swing; Power; NoiseIn; } }

// Interfaces refine Amplifier by adding ports and mapping metrics.
interface SingleEndedOpAmp extend Amplifier {
  ports [ IN: Diff, OUT: analog ]; supply VDD; ground GND;
  metrics {
    GainBandwidth from SEOpAmpACBench.GainBandwidth;
    PassbandGain  from SEOpAmpACBench.PassbandGain;
    PhaseMargin   from SEOpAmpACBench.PhaseMargin;
  }
}

bench SEOpAmpACBench {
  spectre_template = "SEOpAmpACBench.tpl";
  metrics [ GainBandwidth: Hz, PassbandGain: dB, PhaseMargin: deg ]
}
```

Bench inference uses the active interface(s) on the design or candidate motif to determine which benches to run for each metric that appears in `spec {}`. A single bench may provide multiple metrics. Authors may override a specific mapping where supported by tooling.

#### Harness semantics (normative)

* `env` **MUST** synthesize a **bench harness**:

  * `load C = …` → shunt capacitor(s) on designated output node(s).
  * `source Z = …` → source resistance on the designated input(s).
  * `vdd`, `icmr`, temperature, corners → bench operating conditions.
* Harness elements **do not** enter layout/LVS; they are bench-only by definition.

#### Spec↔Env merge (normative)

When `env.icmr` is present but `spec.icmr` is absent, the compiler automatically injects `spec.icmr ⊇ env.icmr`. When both specifications exist, the constraint `spec.icmr ⊇ env.icmr` must hold.

### 2.11.1 Edge and Level Metrics for Digital‑Style Use

Mixed‑signal flows commonly require timing/level checks on digital nodes driven by stdcells. The following metrics are defined for use in `spec {}` and in bench-qualified numeric constraints (Chapter 3):

* `RiseTime(node, v_lo, v_hi)` - time for the node to rise from `v_lo` to `v_hi` once, measured by the first threshold crossing after the input toggles. Units: time. If either bound is a percentage of `VDD`, it binds to `env.vdd` for the active rail. Defaults: `0.1*VDD`, `0.9*VDD` when omitted.
* `FallTime(node, v_hi, v_lo)` - analogous definition for falling transitions.
* `VOH(node)` / `VOL(node)` - steady‑state high/low levels measured at the node under the bench's toggling pattern. Units: voltage. `VOH` is the plateau following a rising transition; `VOL` is analogous for falling.
* `TogglePower(node, freq, duty)` - average dynamic power attributable to toggling a designated driver/input. Units: power. Computed from rail current integration over whole cycles.

Normative

* Threshold crossings use linear interpolation between simulator timesteps.
* Overshoot/undershoot are ignored for `RiseTime`/`FallTime`; use the first monotone crossing after the input toggle.
* When `VDD` varies in time, percentage thresholds use the instantaneous rail value at the start of the measured edge.

### 2.11.2 Bench: `StepToggle`

`StepToggle` is a parameterized transient bench that toggles a designated node or bundle field using an ideal voltage source to exercise downstream drivers and measure edge/level metrics.

Syntax (informal):

```cas
bench { StepToggle { node=IDENT; freq=FREQ; duty=PCT; slew=Auto|time; cycles=3; } }
```

Semantics (normative)

* The bench injects a rail‑to‑rail pulse at `node` (or at the unique upstream driver input if resolvable) with `freq`/`duty` and optional finite `slew`. If `slew=Auto`, the source transitions are ideal.
* `cycles` selects the number of toggles before measurement; default `3` with measurements on the last cycle.
* Measurements permitted: `RiseTime`, `FallTime`, `VOH`, `VOL`, `TogglePower`.

### 2.11.4 Swept Conditions and DC Bias Characterization

Amplifier topologies require DC bias characterization across operating ranges. A requirement envelope declared in `spec {}` establishes that all specifications must hold throughout the stated range. An execution envelope declared in `env {}` defines the set of operating points at which benches run. These two envelopes serve different purposes: requirement envelopes define the user's contract (analogous to datasheet ICMR/OCMR), while execution envelopes control what gets simulated.

When a requirement envelope such as `InputDCBias in [a..b]` or `InputDCCommonMode in [a..b]` appears in `spec {}`, numeric constraints (GainBandwidth, PassbandGain, PhaseMargin, OutputDCBias, QuiescentPower) must be interpreted as holding across the full envelope. The execution envelope must be a subset of or equal to the requirement envelope. When a requirement envelope is omitted, the toolchain must not invent one. If multi-point characterization is still desired, the author opts in via `env {}` (either an explicit range or `[Auto]`). Constraints are then evaluated over the executed sweep points, and the chosen range is recorded for reproducibility but does not become a requirement.

The topology determines which DC conditions are swept:

| Topology | Input Structure | Output Structure | Swept Conditions |
|----------|----------------|------------------|------------------|
| SEAmp | Single-ended | Single-ended | `InputDCBias` |
| SEOpAmp | Differential | Single-ended | `InputDCCommonMode` |
| FDOpAmp | Differential | Differential | `InputDCCommonMode`, `OutputDCCommonMode` |

When no sweep or requirement envelope is present, benches run a single-point characterization at mid-supply. Mid-supply is defined topology-specifically: for `SEAmp`, `InputDCBias = VDD/2`; for `SEOpAmp`, `InputDCCommonMode = VDD/2`; for `FDOpAmp`, `InputDCCommonMode = VDD/2` and `OutputDCCommonMode = VDD/2`.

#### Sweep Semantics (normative)

When a `sweep <ConditionName>` directive appears in the harness, all benches must execute their analyses at each sweep point and report worst-case values:

* Metrics with `>= X` constraints report the minimum value across the sweep.
* Metrics with `<= X` constraints report the maximum value across the sweep.
* Metrics with range constraints `in [X..Y]` report both `_min` and `_max` values.

If `spec.<ConditionName>` is specified but `env.sweep <ConditionName>` is omitted, the sweep defaults to the specification range with automatic step sizing `(stop - start)/20` clamped to `[10mV, 100mV]`.

#### Explicit `Auto` Sweeps (normative)

A design may request that the toolchain choose a sweep range by writing `[Auto]` in `env {}`:

```cas
env {
  sweep InputDCBias [Auto];
}
```

`[Auto]` requests that synthesis select a concrete execution envelope during lowering and record it in the ACIR-EL artifact. It does not declare a requirement envelope. The toolchain must treat the resolved sweep range as part of the generated harness; repeated runs of `cascode emit` and `cascode verify` must use the resolved range from ACIR-EL.

#### Examples

SEAmp with requirement envelope:

```cas
module CSAmp implements SingleEndedAmp {
  supply VDD=1.8V; ground GND;
  ports [ IN: analog, OUT: analog ]
  bias VB1;

  spec {
    InputDCBias in [0.3V..1.5V];     // All specs must hold across this range
    GainBandwidth >= 100MHz;
    PassbandGain >= 40dB;
    OutputDCBias in [0.4V..1.4V];
    QuiescentPower <= 500uW;
  }

  env {
    sweep InputDCBias [0.3V:1.5V];   // Auto step: (1.5-0.3)/20 = 60mV
    load OUT C=2pF;
  }
}
```

SEAmp with execution envelope only:

```cas
module CSAmp implements SingleEndedAmp {
  supply VDD=1.8V; ground GND;
  ports [ IN: analog, OUT: analog ]

  spec {
    OutputDCBias in [0.4V..1.4V];
    QuiescentPower <= 500uW;
  }

  env {
    sweep InputDCBias [Auto];        // Synthesis chooses range; recorded in ACIR-EL
    load OUT C=2pF;
  }
}
```

SEOpAmp with ICMR requirement:

```cas
module OTA implements SingleEndedOpAmp {
  supply VDD=1.8V; ground GND;
  ports [ IN: Diff, OUT: analog ]

  spec {
    InputDCCommonMode in [0.3V..1.5V];
    GainBandwidth >= 100MHz;
    PassbandGain >= 55dB;
    PhaseMargin >= 60deg;
    OutputDCBias in [0.4V..1.4V];
    QuiescentPower <= 1mW;
  }

  env {
    sweep InputDCCommonMode [0.3V:1.5V];
    load OUT C=1pF;
  }
}
```

FDOpAmp with ICMR and OCMR (future):

```cas
module FDOpAmp implements FullyDiffOpAmp {
  ports [ IN: Diff, OUT: Diff ]
  
  spec {
    InputDCCommonMode in [0.3V..1.5V];
    OutputDCCommonMode in [0.5V..1.3V];
    GainBandwidth >= 200MHz;
  }
  
  env {
    sweep InputDCCommonMode [0.3V:1.5V];
    sweep OutputDCCommonMode [0.5V:1.3V];  // 2D sweep (implementation-dependent)
  }
}
```

#### Bench Responsibilities (normative)

Benches that support swept conditions must detect `sweep.<ConditionName>` in harness template variables, configure the simulator to iterate across the sweep range, aggregate results and report worst‑case values according to metric directionality, and for range‑constrained metrics report both `_min` and `_max` suffixes.

---

## 2.12 Synthesis: `slot` and `synth` (Mandatory Fill)

A **`slot`** is a typed placeholder to be filled either by **synthesis** or by an explicit **structural fill**.

```cas
slot Core: AmplifierStage bind { in -> IN; out -> OUT; }    // binding is mandatory
```

#### Filling a slot (choose one, normative)

Slots must be filled through one of two mechanisms:

1. **Synthesis**

```cas
synth {
  from lib.ota.*;                   // search space (entities marked Synthesizable with char{})
  fill Core;                        // which slots to decide
  allow Core in { TeleCascodeNMOS, FoldedCascodePMOS };  // optional structural limits
  Core.comp { style=MillerRC; Cc=Auto; Rz=Auto; }        // stage property
  objective minimize Power;
}
```

2. **Structural fill**

```cas
use {
  fill Core with TeleCascodeNMOS { /* params… */ }
    bind { in -> IN; out -> OUT; };
  Core.comp None;
}
```

#### Normative

* Declaring a `slot` without a corresponding **synthesis fill** or **structural fill** is a compile error.
* `allow/forbid` are hard constraints; `prefer` is a soft objective.
* Only entities with **`char{}`** manifests (2.15) are eligible for synthesis.

---

## 2.13 Digital‑Style Motifs (Stdcell Integration)

PDK digital standard cells are modeled as ordinary motifs with `digital` ports and explicit rails. Intent is conveyed via interfaces and contracts.

### 2.13.1 Traits for Eligibility

Traits express functional intent so slots can be filled by either single stdcells or composite drivers. For a complete usage example, see [Chapter 1 §1.10](Ch01_Introduction.md#110-digital-standard-cells-as-motifs-overview).

```cas
interface InverterLike {
  port in  IN : digital;
  port out OUT: digital;
  supply VDD; ground GND;
  ens VOH(OUT) >= 0.9*VDD;  // typical digital guarantees
  ens VOL(OUT) <= 0.1*VDD;
}

// Optional composite that still implements InverterLike
motif PadDriver implements InverterLike {
  ports { IN: digital; OUT: digital; VDD: supply; GND: ground; }
  params { stages:int=2; bank:int=4; strength_hint: enum{Auto,X1,X2,X4,X8}=Auto; }
}
```

Normative

* Motifs implementing `InverterLike` **MUST** expose at least the listed ports and satisfy its contracts within their `char{}` validity region.
* Composite drivers (e.g., `PadDriver`) are eligible for the same slots as single‑cell inverters when they implement the interface.

### 2.13.2 PDK Wrappers and Rails

When wrapping stdcells, map rails and well/bulk pins explicitly and avoid hidden ties.

```cas
motif INV_X4 implements InverterLike {
  ports { IN: digital; OUT: digital; VDD: supply; GND: ground; VPB: supply; VNB: ground; }
  wrap spice """
    .subckt sky130_fd_sc_hd__inv_4 A Y VPWR VGND VPB VNB
    .ends
  """ map { IN=A; OUT=Y; VDD=VPWR; GND=VGND; VPB=VPB; VNB=VNB; }
  char {
    sweep { CL:[0.5pF..30pF]; VDD:[1.6V..1.95V]; }
    validity{ VOH>=0.9*VDD; VOL<=0.1*VDD; }
    fit { RiseTime~PWL("fit/inv4_tr_vs_cl.pwl"); FallTime~PWL("fit/inv4_tf_vs_cl.pwl"); }
  }
}
```

Normative

* If a PDK view exposes VPB/VNB (or equivalent bulk pins), wrappers **MUST** surface them as ports and map them bijectively in `map {}`.
* Stdcells are authored with `digital` ports for I/O signals.

### 2.13.3 Synthesis Guidance

To meet edge‑time and level specs under `env{}` loads, the engine ranks `InverterLike` candidates using `char{}` fits (Rise/Fall vs. `CL`, and VOH/VOL validity). Objectives such as `minimize DynamicPower` may be applied subject to timing/level constraints.

Example:

```cas
slot Buf: InverterLike bind { in -> COMP_OUT; out -> PAD; }
synth {
  from lib.std.sky130.hd.*;
  allow Buf in { INV_*, PadDriver };
  objective minimize DynamicPower;
}
```

---

## 2.14 Passive Devices: Kinds and Scope

The cascode language recognizes that not all passive elements serve equivalent purposes, distinguishing between **physical** and **notional** passives based on their role in the design flow.

#### Physical passives (enter layout, DRC/LVS, parasitics)

```cas
C1 = new Cap(OUT, GND) { kind=MIM | MOM | MFC; value=500fF; }
R1 = new Res(A, OUT)   { kind=TFR | Poly | Metal | Pseudo; value=10k; }
L1 = new Ind(A, B)     { kind=Spiral | MIMStack | Metal; value=2nH; }
```

#### Notional passives (bench support / modeling)

The preferred approach for expressing loads and sources utilizes **`env{}`** declarations, which the toolchain materializes as **harness** elements during bench generation (as detailed in section 2.11). While minimal `bench.fixtures` may accommodate special measurement hooks such as current probe shunts, they must not be used for loads or sources that fall under `env{}` coverage.

#### Sugar constraints (normative)

* `C(a,b,val)`, `R(a,b,val)`, `L(a,b,val)` **sugar** is permitted:

  * **Inside `env{}`** (becomes harness elements), or
  * Inside `bench { fixtures { … } }` for **special probes**.
* Otherwise, the **explicit** `new Cap/Res/Ind { kind=…; value=… }` form **MUST** be used for physical devices.

---

## 2.14.5 Active Device Primitives: **NMOS** and **PMOS**

For schematic-style structural design, cascode provides **primitive transistor types** as first-class constructs alongside passive devices. These primitives enable direct topological specification while maintaining **process-agnostic** representation.

#### Primitive transistor syntax

```cas
M1 = new NMOS() { 
  gate -> vin; drain -> vout; source -> GND; bulk -> GND;
};

M2 = new PMOS() { 
  gate -> vb1; drain -> vout; source -> VDD; bulk -> VDD;
};
```

#### Port structure

* `NMOS`: `{ gate, drain, source, bulk: analog }`
* `PMOS`: `{ gate, drain, source, bulk: analog }`

#### Parameters

* `W` (width): **derived by synthesis from specifications** - never hardcoded in ADL
* `L` (length): **derived by synthesis from specifications** - never hardcoded in ADL
* `mult` (logical multiplicity / finger count): optional, default=1

#### Process-agnostic semantics (normative)

Primitive transistors are **topology-only constructs** in cascode source. They emit to ACIR as device declarations at EL level, carrying terminal connectivity and sizing parameters.

The synthesis engine:
1. Consults the active **PDK database** to access gm/Id tables and technology rules
2. Derives W, L, and `mult` from `spec{}` constraints (gain, bandwidth, power, etc.)
3. Applies PDK-specific constraints (Lmin, discrete widths, finger limits)
4. Emits sized parameters to ACIR at EL (Electrical Level)

At the Electrical Level (EL), the synthesis engine selects the PDK device for each primitive transistor and records it in ACIR device declarations (e.g., `nfet_01v8`). SPICE emission uses this selected device directly.

#### Normative

* Primitive transistors **MUST** have all four ports explicitly connected.
* Dimensional parameters (W, L) **MUST NOT** be specified in ADL; they are synthesis outputs.
* At EL, primitive devices **MUST** carry PDK device names in ACIR; earlier phases remain PDK‑agnostic.
* Primitives appearing in entities marked `Synthesizable` may be characterized with `char{}` manifests that are **process-qualified** (e.g., `char@sky130`).

#### Use cases

* **Hand-crafted topologies**: CS stages, differential pairs, mirrors where explicit transistor-level control is desired
* **Motif internals**: Building blocks like `ActiveLoad`, `CurrentMirror`, and tail bias mirrors wrap primitives with semantic interfaces
* **Non-synthesizable designs**: Test structures, bias generators, reference circuits

---

## 2.15 Characterization (`char`) for Synthesizable Libraries

Library entities intended for synthesis **MUST** declare a **`char {}`** manifest:

```cas
char {
  benches { SEOpAmpACBench; NoiseIn; Step; }          // characterization benches
  pvt     { TT@27C, SS@-40C, FF@125C; }
  sweep   { CL:[0.5pF..5pF]; VDD:[1.0V..1.3V]; gmId:[10..22]V^-1; }
  fit     { GainBandwidth~GP("fit/gbw.gp"); PassbandGain~PWL("fit/gain.pwl");
            PhaseMargin~PWL("fit/pm.pwl"); NoiseIn~GP("fit/noise.gp");
            Power~affine(I_total, VDD); }
  validity{ icmr:[0.4V..0.9V]; swing:[0.2V..1.0V]; }
}
```

#### Normative

* Synthesis **MUST** consult fits for feasibility/ranking before SPICE.
* Final acceptance **MUST** rely on SPICE; fit error bounds **MUST** be surfaced.

---

## 2.16 Motif Composition: ActiveLoad Example

Motifs often wrap primitive transistors with semantic interfaces. The `ActiveLoad` motif provides a clean example. For usage in a complete design, see [Chapter 1 §1.5](Ch01_Introduction.md#15-cascode-in-a-few-examples) (CommonSourceAmp).

```cas
motif ActiveLoad {
  ports {
    NODE: analog;        // the node being loaded
    BIAS: bias;          // gate bias voltage
    VREF: supply;        // reference rail (VDD for PMOS, GND for NMOS)
  }
  
  params {
    polarity: enum{PMOS, NMOS};
    diode_connected: bool = false;
  }
  
  use {
    if (polarity == PMOS) {
      M = new PMOS { 
        drain -> NODE; 
        source -> VREF; 
        bulk -> VREF;
        gate -> (diode_connected ? NODE : BIAS);
      };
    } else {
      M = new NMOS { 
        drain -> NODE; 
        source -> VREF; 
        bulk -> VREF;
        gate -> (diode_connected ? NODE : BIAS);
      };
    }
  }
  
  char {
    sweep { VDD:[1.0V..1.95V]; I_drain:[10uA..1mA]; polarity:{PMOS,NMOS}; }
    fit { r_out~GP("fit/active_load_rout.gp"); }
    validity { headroom >= 0.2V; }
  }
}
```

This pattern demonstrates **abstraction without loss of transparency**: the primitive transistor is directly accessible for sizing, while the motif provides semantic clarity and reusable characterization.

---

## 2.17 SPICE Interoperability: `wrap spice`

`wrap spice """ … """ map { … }` turns a SPICE subckt into a **motif** with ports/params/contracts. For an NMOS variant of this pattern, see [Chapter 1 §1.5](Ch01_Introduction.md#15-cascode-in-a-few-examples).

```cas
motif WideSwingPMOSMirror {
  ports  { SENSE: analog; OUT: analog; VREF: supply; }
  params { m:int=1; Wp=2u; Lp=0.18u; }
  wrap spice """
    .subckt WS_PMOS_MIRROR sense out vref m=1 Wp=2u Lp=0.18u
    M1 out  sense vref vref pch W={Wp*m} L={Lp}
    M2 sense sense vref vref pch W={Wp}   L={Lp}   ; diode
    .ends
  """ map { SENSE=sense; OUT=out; VREF=vref; }
  // char { ... } // required to be Synthesizable
}
```

> [!NOTE]
> **Limitation: SPICE wraps and parameterized interfaces**
>
> This motif does not declare `implements CurrentMirrorLike` because the interface requires a parameterized `TAP[i]` port family. SPICE subcircuits have fixed port lists and cannot directly implement interfaces with parameterized ports. Future language versions may address this through constrained interface implementation (e.g., `implements CurrentMirrorLike where taps=1`) or parameterized `map { }` blocks that generate port bindings from loop expressions.

#### Normative

* `map{}` **MUST** bind subckt pins to *cascode* ports bijectively.
* Wrapped motifs are **Synthesizable** only when accompanied by `char{}`.

---

## 2.18 Clocks and Phases

* `clock` ports carry timing semantics; `phase {}` specifies frequency, duty, edge slew.

```cas
clock phi; phase { phi: 500MHz, duty=50%, t_rise<=50ps; }
```

#### Normative

* Clocked motifs (e.g., `StrongArmLatch`) **MUST** expose a `clock` and document timing contracts that benches rely on.

---

## 2.19 Diagnostics and Provenance

Tools should report which constraints are **binding**, identify blocks that dominate **power**, **noise**, or **headroom** budgets, and suggest targeted edits such as "increase `L` on load" or "enable `MillerRz`" compensation.

ACIR must record complete **provenance** information including the library entity chosen, parameter values, compensation style realized, and source `.cas` line ranges.
