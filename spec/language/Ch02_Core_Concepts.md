# **Chapter 2: Core Concepts**

> This chapter defines the **semantic scaffolding** of *cascode*: the building blocks, how they relate, and the invariants the compiler and tools rely upon. Syntax is shown informally; the formal grammar appears in Chapter 11.
> Normative keywords **MUST**, **MUST NOT**, **SHOULD**, **MAY** follow [RFC 2119](https://www.ietf.org/rfc/rfc2119.txt).

---

## 2.1 Programs, Packages, and Imports

A **program** comprises one or more `.cas` files organized under a package namespace. The `package` declaration establishes the namespace, while `import` statements bring external names into the current scope. Name resolution follows **lexical scoping** principles with package-qualified fallback, and shadowing rules mirror Java and C# conventions unless explicitly stated otherwise.

---

## 2.2 Modules, Motifs, Traits

The cascode type system distinguishes three fundamental entities. A **module** represents a top-level design entity that encompasses **ports**, **parameters**, optional **use** blocks for instantiation, **connect** and **cascade** statements for wiring, **spec**, **env**, and **bench** blocks for behavioral specification, and optional **slot** and **synth** directives for synthesis. A **motif** serves as a reusable building block with defined **ports**, **params**, and **contracts** while maintaining encapsulated internal structure. Motifs may be authored natively within cascode or integrated via **`wrap spice`** constructs. A **trait** functions as an interface that defines contracts through **ports**, **bundles**, **roles**, and **behavioral expectations** that modules and motifs can **implement**. This abstraction enables substitution during synthesis - for instance, any entity implementing `AmplifierStage` becomes eligible to fill `slot Core: AmplifierStage`.

**Normative**

* An entity implementing a trait **MUST** expose a **superset** of the trait’s ports/bundles and satisfy its declared **contracts** (2.10).
* A module is **instantiable** only when all required ports are bound and all declared `slot`s are **filled** (structurally or via `synth`).

---

## 2.3 Port Kinds, Roles, and Net Types

**Port kinds** (non-exhaustive):

* `supply`, `ground` - special; **MUST NOT** short to `electrical`.
* `electrical` - general (single-ended).
* `diff` - differential bundle abstraction (has fields `.p`/`.n`).
* `bias` - bias/control nets (typed for headroom/legality checks).
* `rf`, `clk` - specialized kinds with additional contracts (impedance, phase/timing).

**Roles**

Ports and nets may carry semantic **roles** such as `stage1_out`, `ota_out`, or `cmfb_ctrl` that provide semantic context beyond basic electrical connectivity. These role annotations guide automated **pattern recognition**, enable targeted **contract** enforcement, and inform **benchmark generation** strategies.

**Normative**

* Port-kind **compatibility** is enforced at connect time (e.g., `bias→gate` inside motifs is allowed; `bias→out` is forbidden unless a motif explicitly exposes this).
* Each `supply`/`ground` port **MUST** connect to exactly one global net per instantiation context.

### 2.3.1 Port Naming Conventions

To improve readability and keep interfaces visually distinct from internal nets, the specification adopts the following convention:

- External ports exported by modules and motifs use ALL_CAPS_WITH_UNDERSCORES (e.g., `IN_P`, `IN_N`, `OUT`, `OUT_L`, `OUT_R`).
- Rails remain `VDD`/`GND` (already ALL CAPS).
- Internal nets and instance locals inside `use {}` blocks use lowerCamelCase.

This convention is normative for the standard libraries and examples and SHOULD be followed by user code for consistency.

---

## 2.4 Bundles (Structured Port Groups)

A **Bundle** is a typed, named group of ports (e.g., a differential pair or an amplifier I/O interface).

```cas
bundle Diff   { P: electrical; N: electrical; }
bundle AmpIO  { in: Diff; out: electrical; }
```

Bundles serve two primary purposes: they **reduce verbosity** while making **binding explicit** without ambiguity. These constructs are valid within module ports, motif ports, `slot` trait definitions, and `bind` statements.

**Normative**

* Binding a bundle **MUST** map all required fields (compile-time error if any are missing).
* Bundle field kinds **MUST** match (structural subtyping is **not** implied).

---

## 2.5 Parameters and Defaults

**Parameters** (declared via `param` or `params`) define compile-time tunables for modules and motifs. These parameters may be typed as `bool`, `int`, `real`, `enum`, `polarity`, or **unit-typed** quantities. While default values may be provided, required parameters must be explicitly set at instantiation time.

```cas
param CL = 2pF;              // module parameter
params { enabled: bool=true; m:int=1; }          // motif parameters
```

---

## 2.6 Units and Dimensions

Literals may specify **units** including voltage (`V`), current (`A`), capacitance (`F`), inductance (`H`), frequency (`Hz`), gain (`dB`), phase (`deg`), time (`s`, `ps`), power (`mW`), and noise density (`nV/√Hz`). The compiler enforces **dimensional consistency** across all expressions and specifications. SI prefixes undergo automatic conversion, while non-linear units such as decibels are treated as scalars with semantics defined per metric (detailed in Chapter 5).

```cas
supply VDD = 1.2V;
spec { gbw>=100MHz; gain>=70dB; pm>=60deg; power<=1mW; }
```

---

## 2.7 Instances and Connections (Explicit Binding)

**Instances**

The `use {}` construct creates motif and module instances through `new` expressions with **inline field binding**. The language mandates that all cross-instance connections be explicit, accomplished through `bind`, `connect`, or `cascade` statements.

**Explicit binding (mandatory)**

The language requires that **all** `slot` bindings and cross-instance connections be explicitly specified. **Auto-binding by name or role is strictly prohibited** to ensure design intent remains unambiguous.

```cas
slot Core: AmplifierStage bind { in<-IN; out->OUT; }   // bundle-to-bundle binding
connect A.out -> B.in;                                 // explicit net connection
```

**Cascade sugar**

The `cascade { A -> B -> C; }` syntax provides a concise representation for sequential connections, but is permitted **only** when the underlying trait defines a canonical **connector** (such as `AmplifierStage: out→in`). The compiler expands cascade expressions into equivalent explicit `connect` statements.

**Alias**

The `alias` construct may expose internal nets as top-level ports to improve design clarity, but aliases do not introduce auto-binding behavior.

**Normative**

* If a connector is not defined for a trait, `cascade` **MUST** be rejected (use explicit `connect`).
* Binding a bundle **MUST** bind all fields; partial binding is an error.

---

## 2.8 Structural Composition Primitives

**Schematic-like sugar** (all expand to primitives in CasIR):

* `attach` - bind a structural motif to a target instance with explicit port mapping.

  ```cas
  // Example: attach a cascode pair atop a differential pair using bundles.
  attach CascodePair on dp { IN <- OUT; BIAS <- VB_CASC }
  ```

* `pair` - instantiate symmetric **left/right** branches with `.l`/`.r` handles. (Omitted here; see the Grammar for details.)

* `CurrentMirror` - general mirror motif with `SENSE` and `TAP` ports. Rails are inferred from polarity unless overridden.

  ```cas
  cm = new CurrentMirror { p=PMOS; taps=1 };
  attach cm on dp { SENSE <- OUT.N; TAP <- OUT.P }
  ```

### 2.8.3 Attach Name Resolution

Within an `attach X on target { … }` block, identifiers on the left‑hand side of bindings refer to the attached motif `X` and must name its ports. Identifiers on the right‑hand side resolve in two steps: first against the public ports of `target` (including bundle fields like `OUT.N`/`OUT.P`), then against names in the surrounding module scope (nets and ports). If a right‑hand identifier would resolve in both places, the reference must be qualified (for example, `target.OUT.N`). Names that cannot be resolved after this search are errors.

Example (attaching a current mirror to a differential pair):

```cas
cm = new CurrentMirror { p=PMOS; taps=1; };
dp = new DiffPair     { p=NMOS;  hasTail=true; } { IN_P <- VINP; IN_N <- VINN; BASE <- GND; BIAS <- VTAIL; };
attach cm on dp { SENSE <- OUT.N; TAP <- OUT.P };
```

Here `SENSE` and `TAP` are ports of `cm`, while `OUT.N` and `OUT.P` resolve to `dp.OUT.N` and `dp.OUT.P` by the target‑first rule. If the surrounding scope also declares `OUT.P`, the binding must be written as `TAP <- dp.OUT.P` to disambiguate.

#### 2.8.a Scoped Orientation on Pair‑Like Targets

Orientation sugar is permitted only when the attachment is unambiguously “pair‑like.” Two cases qualify:

1) Bundle match. Both the attached motif and the target expose a common pair bundle (for example, `Diff { P, N }`). Writing a single bundle binding implies field‑wise mapping:

```cas
// Both sides declare Diff bundles.
motif CascodePair { ports [ IN: Diff, OUT: Diff, BIAS: bias ] }
attach CascodePair on dp { IN <- OUT }   // expands to IN.P<-OUT.P; IN.N<-OUT.N
```

Reversed orientation in this case requires explicit field mapping (for example, `IN.p <- OUT.n; IN.n <- OUT.p`).

2) Name match. The attached motif and the target share identical port names and compatible kinds for all required connections. To avoid surprises, orientation‑by‑name is opt‑in using an explicit directive:

```cas
motif Probe { ports [ OUT: Diff ] }
attach Probe on dp by name;   // binds OUT.P<-dp.OUT.P; OUT.N<-dp.OUT.N
```

Outside these cases, users MUST bind complementary ports explicitly. For example, attaching a `CurrentMirror` (ports `SENSE`, `TAP`) to a `DiffPair` (port `OUT: Diff`) requires explicit mapping:

```cas
attach cm on dp { SENSE <- OUT.N; TAP <- OUT.P }
```

Name resolution inside `attach` follows §2.8.3 (left‑hand identifiers refer to the attached motif; right‑hand identifiers resolve to the target first, then the surrounding scope, with qualification required on ambiguity).

### 2.8.1 Param‑Variants (sugar to avoid nested conditionals)

Parameter‑driven structural choices are common (e.g., a device `p∈{NMOS,PMOS}` or a compensator style). To avoid verbose nested `if` blocks, the language introduces two pieces of sugar that desugar prior to CasIR emission:

1) Polarity‑polymorphic constructors (native type)

   ```cas
   // new MOS(p) chooses NMOS or PMOS primitive at elaboration
   T = new MOS(p) { gate<-vin; drain->vout; source<-ref; bulk<-ref; };
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
  ports [ IN: Diff, OUT: Diff, BASE: electrical ]
  ports { if (hasTail) { BIAS: bias; } }
}
```

Normative

- Declarations inside `ports { … }` are evaluated at elaboration time. Names declared in `ports[…]` are always present.
- Conditional ports are compile‑time; call sites MUST bind all ports that exist after evaluation; names not declared MUST NOT be referenced.
- CasIR contains only the realized port set.

### 2.5.1 Native Polarity Type and Bulk Policy

In addition to `bool`, `int`, `real`, and enums, the language defines a primitive `polarity` type with literals `NMOS` and `PMOS`. This enables concise, type‑checked parameterization of device polarity and supports the `new MOS(polarity)` constructor sugar in §2.8.1.

Bulk policy: When a primitive MOS is instantiated without an explicit bulk binding, the compiler applies a target‑tech policy (default: tie NMOS bulk to ground domain and PMOS bulk to supply domain in benches; technology adapters may override). This allows reusable motifs (like DiffPair) to avoid hard‑coding rails and remain stackable (e.g., Gilbert cells) while still producing legal SPICE.

Example: “BASE” semantics

Some motifs provide a stable external name for a configuration‑dependent internal junction. For the differential pair above, `BASE` is defined as:

- when `hasTail==false`: the common source of the pair;
- when `hasTail==true`: the bottom of the internal tail device.

The intermediate node between the pair and the tail is internal when the tail is present. This keeps the interface small while remaining composable.

* `ActiveLoad` - **general** active load motif with polarity parameter (wraps primitive transistor with semantic interface).

  ```cas
  load = new ActiveLoad(polarity=PMOS) {
    node <- vout;             // node being loaded
    bias <- vb1;              // gate bias voltage
    vref <- VDD;              // reference rail (VDD for PMOS, GND for NMOS)
  };
  ```

* `fb R(...)`, `fb C(...)` - feedback creators (expand to `Res`/`Cap` instances with direction metadata). See 2.14 for passive kinds/scope.

**Acyclicity**

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

**Normative**

* If `Core.comp None;` is set, no compensation circuitry may be realized.
* Supported styles and parameter semantics **MUST** be documented by the chosen stage’s library entry.

---

## 2.10 Contracts and Patterns

**Contracts** encapsulate boundary assumptions (`req`) and guarantees (`ens`) for motifs and modules. Examples include `req headroom>=0.35V`, `ens gain_min_db>=20`, and `ens icmr in [0.4V..0.9V]`.

**Patterns** define recognizers and binders for canonical subgraphs (such as 5T current mirrors), enabling automated ingestion from SPICE netlists and canonicalization into structured motifs.

**Normative**

* Tools **MUST** enforce `req` at instantiation given `env{}`; violations are compile-time errors.
* `ens` are used for feasibility/search; violations found during verification **MUST** be reported.

---

## 2.11 Behavioral Description: `spec`, `env`, `bench` and the **Harness**

The **`spec {}`** block enumerates **required metrics** including gain-bandwidth (`gbw`), phase margin (`pm`), gain, input-referred noise (`noise_in`), slew rate (`sr`), settling time (`settle`), zero-tau frequency (`zt`), output swing (`swing(node)`), and power consumption (`power`). The **`env {}`** block characterizes the **operating environment** through supply voltage (`vdd`), input common-mode range (ICMR), **mandatory** load specifications, **mandatory** source impedance, temperature, and process corners. The **`bench {}`** block selects appropriate measurement benches spanning AC, noise, and transient analyses, as well as domain-specific benches such as `LatchDecision`.

**Harness semantics (normative)**

* `env` **MUST** synthesize a **bench harness**:

  * `load C = …` → shunt capacitor(s) on designated output node(s).
  * `source Z = …` → source resistance on the designated input(s).
  * `vdd`, `icmr`, temperature, corners → bench operating conditions.
* Harness elements **do not** enter layout/LVS; they are bench-only by definition.

**Spec↔Env merge (normative)**

When `env.icmr` is present but `spec.icmr` is absent, the compiler automatically injects `spec.icmr ⊇ env.icmr`. When both specifications exist, the constraint `spec.icmr ⊇ env.icmr` must hold.

### 2.11.1 Edge and Level Metrics for Digital‑Style Use

Mixed‑signal flows commonly require timing/level checks on electrical nodes driven by stdcells. The following metrics are defined for use in `spec {}` and in `constraints.measure` (Chapter 3):

* `rise_time(node, v_lo, v_hi)` - time for the node to rise from `v_lo` to `v_hi` once, measured by the first threshold crossing after the input toggles. Units: time. If either bound is a percentage of `VDD`, it binds to `env.vdd` for the active rail. Defaults: `0.1*VDD`, `0.9*VDD` when omitted.
* `fall_time(node, v_hi, v_lo)` - analogous definition for falling transitions.
* `voh(node)` / `vol(node)` - steady‑state high/low levels measured at the node under the bench's toggling pattern. Units: voltage. `voh` is the plateau following a rising transition; `vol` is analogous for falling.
* `toggle_power(node, freq, duty)` - average dynamic power attributable to toggling a designated driver/input. Units: power. Computed from rail current integration over whole cycles.

Normative

* Threshold crossings use linear interpolation between simulator timesteps.
* Overshoot/undershoot are ignored for `rise_time`/`fall_time`; use the first monotone crossing after the input toggle.
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
* Measurements permitted: `rise_time`, `fall_time`, `voh`, `vol`, `toggle_power`.

---

## 2.12 Synthesis: `slot` and `synth` (Mandatory Fill)

A **`slot`** is a typed placeholder to be filled either by **synthesis** or by an explicit **structural fill**.

```cas
slot Core: AmplifierStage bind { in<-IN; out->OUT; }    // binding is mandatory
```

**Filling a slot (choose one, normative)**

Slots must be filled through one of two mechanisms:

1. **Synthesis**

```cas
synth {
  from lib.ota.*;                   // search space (entities marked Synthesizable with char{})
  fill Core;                        // which slots to decide
  allow Core in { TeleCascodeNMOS, FoldedCascodePMOS };  // optional structural limits
  Core.comp { style=MillerRC; Cc=Auto; Rz=Auto; }        // stage property
  objective minimize power;
}
```

2. **Structural fill**

```cas
use {
  fill Core with TeleCascodeNMOS { /* params… */ }
    bind { in<-IN; out->OUT; };
  Core.comp None;
}
```

**Normative**

* Declaring a `slot` without a corresponding **synthesis fill** or **structural fill** is a compile error.
* `allow/forbid` are hard constraints; `prefer` is a soft objective.
* Only entities with **`char{}`** manifests (2.15) are eligible for synthesis.

---

## 2.13 Digital‑Style Motifs (Stdcell Integration)

PDK digital standard cells are modeled as ordinary motifs with electrical pins and explicit rails. No new logic net type is introduced; intent is conveyed via traits and contracts.

### 2.13.1 Traits for Eligibility

Traits express functional intent so slots can be filled by either single stdcells or composite drivers. For a complete usage example, see [Chapter 1 §1.10](Ch01_Introduction.md#110-digital-standard-cells-as-motifs-overview).

```cas
trait InverterLike {
  port in  IN : electrical;
  port out OUT: electrical;
  supply VDD; ground GND;
  ens voh(OUT) >= 0.9*VDD;  // typical electrical guarantees
  ens vol(OUT) <= 0.1*VDD;
}

// Optional composite that still implements InverterLike
motif PadDriver implements InverterLike {
  ports { IN: electrical; OUT: electrical; VDD: supply; GND: ground; }
  params { stages:int=2; bank:int=4; strength_hint: enum{Auto,X1,X2,X4,X8}=Auto; }
}
```

Normative

* Motifs implementing `InverterLike` **MUST** expose at least the listed ports and satisfy its contracts within their `char{}` validity region.
* Composite drivers (e.g., `PadDriver`) are eligible for the same slots as single‑cell inverters when they implement the trait.

### 2.13.2 PDK Wrappers and Rails

When wrapping stdcells, map rails and well/bulk pins explicitly and avoid hidden ties.

```cas
motif INV_X4 implements InverterLike {
  ports { IN: electrical; OUT: electrical; VDD: supply; GND: ground; VPB: supply; VNB: ground; }
  wrap spice """
    .subckt sky130_fd_sc_hd__inv_4 A Y VPWR VGND VPB VNB
    .ends
  """ map { IN=A; OUT=Y; VDD=VPWR; GND=VGND; VPB=VPB; VNB=VNB; }
  char {
    sweep { CL:[0.5pF..30pF]; VDD:[1.6V..1.95V]; }
    validity{ voh>=0.9*VDD; vol<=0.1*VDD; }
    fit { rise_time~PWL("fit/inv4_tr_vs_cl.pwl"); fall_time~PWL("fit/inv4_tf_vs_cl.pwl"); }
  }
}
```

Normative

* If a PDK view exposes VPB/VNB (or equivalent bulk pins), wrappers **MUST** surface them as ports and map them bijectively in `map {}`.
* Stdcells are authored with `electrical` ports; no new net kinds are required.

### 2.13.3 Synthesis Guidance

To meet edge‑time and level specs under `env{}` loads, the engine ranks `InverterLike` candidates using `char{}` fits (rise/fall vs. `CL`, and voh/vol validity). Objectives such as `minimize dynamic_power` may be applied subject to timing/level constraints.

Example:

```cas
slot Buf: InverterLike bind { in<-COMP_OUT; out->PAD; }
synth {
  from lib.std.sky130.hd.*;
  allow Buf in { INV_*, PadDriver };
  objective minimize dynamic_power;
}
```

---

## 2.14 Passive Devices: **Kinds** and **Scope**

The cascode language recognizes that not all passive elements serve equivalent purposes, distinguishing between **physical** and **notional** passives based on their role in the design flow.

**Physical passives** (enter layout, DRC/LVS, parasitics):

```cas
C1 = new Cap(OUT, GND) { kind=MIM | MOM | MFC; value=500fF; }
R1 = new Res(A, OUT)   { kind=TFR | Poly | Metal | Pseudo; value=10k; }
L1 = new Ind(A, B)     { kind=Spiral | MIMStack | Metal; value=2nH; }
```

**Notional passives** (bench support / modeling):

The preferred approach for expressing loads and sources utilizes **`env{}`** declarations, which the toolchain materializes as **harness** elements during bench generation (as detailed in section 2.11). While minimal `bench.fixtures` may accommodate special measurement hooks such as current probe shunts, they must not be used for loads or sources that fall under `env{}` coverage.

**Sugar constraints (normative)**

* `C(a,b,val)`, `R(a,b,val)`, `L(a,b,val)` **sugar** is permitted:

  * **Inside `env{}`** (becomes harness elements), or
  * Inside `bench { fixtures { … } }` for **special probes**.
* Otherwise, the **explicit** `new Cap/Res/Ind { kind=…; value=… }` form **MUST** be used for physical devices.

---

## 2.14.5 Active Device Primitives: **NMOS** and **PMOS**

For schematic-style structural design, cascode provides **primitive transistor types** as first-class constructs alongside passive devices. These primitives enable direct topological specification while maintaining **process-agnostic** representation.

**Primitive transistor syntax:**

```cas
M1 = new NMOS() { 
  gate<-vin; drain<-vout; source<-GND; bulk<-GND;
};

M2 = new PMOS() { 
  gate<-vb1; drain<-vout; source<-VDD; bulk<-VDD;
};
```

**Port structure:**

* `NMOS`: `{ gate, drain, source, bulk: electrical }`
* `PMOS`: `{ gate, drain, source, bulk: electrical }`

**Parameters:**

* `W` (width): **derived by synthesis from specifications** - never hardcoded in ADL
* `L` (length): **derived by synthesis from specifications** - never hardcoded in ADL
* `m` (multiplier): optional, default=1

**Process-agnostic semantics (normative):**

Primitive transistors are **topology-only constructs** in cascode source. They emit to CasIR as motif instances with type `"NMOS"` or `"PMOS"`, carrying port connectivity but **no dimensional parameters until synthesis**.

The synthesis engine:
1. Consults the active **PDK database** to access gm/Id tables and technology rules
2. Derives W, L, and m from `spec{}` constraints (gain, bandwidth, power, etc.)
3. Applies PDK-specific constraints (Lmin, discrete widths, finger limits)
4. Emits sized parameters to CasIR at EL (Electrical Level)

At the Electrical Level (EL), the synthesis engine selects the PDK device for each primitive transistor and records it in CasIR as `impl.pdk_device` (e.g., `nfet_01v8`). SPICE emission uses this selected device directly.

**Normative:**

* Primitive transistors **MUST** have all four ports explicitly connected.
* Dimensional parameters (W, L) **MUST NOT** be specified in ADL; they are synthesis outputs.
* At EL, primitive transistors **MUST** carry `impl.pdk_device` in CasIR; earlier phases remain PDK‑agnostic.
* Primitives appearing in entities marked `Synthesizable` may be characterized with `char{}` manifests that are **process-qualified** (e.g., `char@sky130`).

**Use cases:**

* **Hand-crafted topologies**: CS stages, differential pairs, mirrors where explicit transistor-level control is desired
* **Motif internals**: Building blocks like `ActiveLoad`, `CurrentMirror`, `TailNMOS` wrap primitives with semantic interfaces
* **Non-synthesizable designs**: Test structures, bias generators, reference circuits

---

## 2.15 Characterization (`char`) for Synthesizable Libraries

Library entities intended for synthesis **MUST** declare a **`char {}`** manifest:

```cas
char {
  benches { ac_openloop; noise_in; step; }          // characterization benches
  pvt     { TT@27C, SS@-40C, FF@125C; }
  sweep   { CL:[0.5pF..5pF]; VDD:[1.0V..1.3V]; gmId:[10..22]V^-1; }
  fit     { gbw~GP("fit/gbw.gp"); gain_db~PWL("fit/gain.pwl");
            pm_deg~PWL("fit/pm.pwl"); noise_in~GP("fit/noise.gp");
            power~affine(I_total, VDD); }
  validity{ icmr:[0.4V..0.9V]; swing:[0.2V..1.0V]; }
}
```

**Normative**

* Synthesis **MUST** consult fits for feasibility/ranking before SPICE.
* Final acceptance **MUST** rely on SPICE; fit error bounds **MUST** be surfaced.

---

## 2.16 Motif Composition: ActiveLoad Example

Motifs often wrap primitive transistors with semantic interfaces. The `ActiveLoad` motif provides a clean example. For usage in a complete design, see [Chapter 1 §1.5](Ch01_Introduction.md#15-cascode-in-a-few-examples) (CommonSourceAmp).

```cas
motif ActiveLoad {
  ports {
    node: electrical;    // the node being loaded
    bias: bias;          // gate bias voltage
    vref: supply;        // reference rail (VDD for PMOS, GND for NMOS)
  }
  
  params {
    polarity: enum{PMOS, NMOS};
    diode_connected: bool = false;
  }
  
  use {
    if (polarity == PMOS) {
      M = new PMOS() { 
        drain<-node; 
        source<-vref; 
        bulk<-vref;
        gate<-(diode_connected ? node : bias);
      };
    } else {
      M = new NMOS() { 
        drain<-node; 
        source<-vref; 
        bulk<-vref;
        gate<-(diode_connected ? node : bias);
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
motif WideSwingPMOSMirror implements CurrentMirrorLike {
  ports  { sense, out: electrical; vref: supply; }
  params { m:int=1; Wp=2u; Lp=0.18u; }
  wrap spice """
    .subckt WS_PMOS_MIRROR sense out vref m=1 Wp=2u Lp=0.18u
    M1 out  sense vref vref pch W={Wp*m} L={Lp}
    M2 sense sense vref vref pch W={Wp}   L={Lp}   ; diode
    .ends
  """ map { sense=sense; out=out; vref=vref; }
  // char { ... } // required to be Synthesizable
}
```

**Normative**

* `map{}` **MUST** bind subckt pins to *cascode* ports bijectively.
* Wrapped motifs are **Synthesizable** only when accompanied by `char{}`.

---

## 2.18 Clocks and Phases

* `clk` ports carry timing semantics; `phase {}` specifies frequency, duty, edge slew.

```cas
clk phi; phase { phi: 500MHz, duty=50%, t_rise<=50ps; }
```

**Normative**

* Clocked motifs (e.g., `StrongArmLatch`) **MUST** expose a `clk` and document timing contracts that benches rely on.

---

## 2.19 Diagnostics and Provenance

Tools should report which constraints are **binding**, identify blocks that dominate **power**, **noise**, or **headroom** budgets, and suggest targeted edits such as "increase `L` on load" or "enable `MillerRz`" compensation.

CasIR must record complete **provenance** information including the library entity chosen, parameter values, compensation style realized, and source `.cas` line ranges.
