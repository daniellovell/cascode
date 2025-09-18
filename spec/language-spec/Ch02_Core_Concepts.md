# **Chapter 2 — Core Concepts (Revised, v0.2)**

> This chapter defines the **semantic scaffolding** of *cascode*: the building blocks, how they relate, and the invariants the compiler and tools rely upon. Syntax is shown informally; the formal grammar appears in Chapter 11.
> Normative keywords **MUST**, **MUST NOT**, **SHOULD**, **MAY** follow RFC 2119.

---

## 2.1 Programs, Packages, and Imports

A **program** comprises one or more `.cas` files organized under a package namespace. The `package` declaration establishes the namespace, while `import` statements bring external names into the current scope. Name resolution follows **lexical scoping** principles with package-qualified fallback, and shadowing rules mirror Java and C# conventions unless explicitly stated otherwise.

---

## 2.2 Modules, Motifs, Traits

The cascode type system distinguishes three fundamental entities. A **module** represents a top-level design entity that encompasses **ports**, **parameters**, optional **use** blocks for instantiation, **connect** and **cascade** statements for wiring, **spec**, **env**, and **bench** blocks for behavioral specification, and optional **slot** and **synth** directives for synthesis. A **motif** serves as a reusable building block with defined **ports**, **params**, and **contracts** while maintaining encapsulated internal structure. Motifs may be authored natively within cascode or integrated via **`wrap spice`** constructs. A **trait** functions as an interface that defines contracts through **ports**, **bundles**, **roles**, and **behavioral expectations** that modules and motifs can **implement**. This abstraction enables substitution during synthesis—for instance, any entity implementing `AmplifierStage` becomes eligible to fill `slot Core: AmplifierStage`.

**Normative**

* An entity implementing a trait **MUST** expose a **superset** of the trait’s ports/bundles and satisfy its declared **contracts** (2.10).
* A module is **instantiable** only when all required ports are bound and all declared `slot`s are **filled** (structurally or via `synth`).

---

## 2.3 Port Kinds, Roles, and Net Types

**Port kinds** (non-exhaustive):

* `supply`, `ground` — special; **MUST NOT** short to `electrical`.
* `electrical` — general (single-ended).
* `diff` — differential bundle abstraction (has fields `.p`/`.n`).
* `bias` — bias/control nets (typed for headroom/legality checks).
* `rf`, `clk` — specialized kinds with additional contracts (impedance, phase/timing).

**Roles**

Ports and nets may carry semantic **roles** such as `stage1_out`, `ota_out`, or `cmfb_ctrl` that provide semantic context beyond basic electrical connectivity. These role annotations guide automated **pattern recognition**, enable targeted **contract** enforcement, and inform **benchmark generation** strategies.

**Normative**

* Port-kind **compatibility** is enforced at connect time (e.g., `bias→gate` inside motifs is allowed; `bias→out` is forbidden unless a motif explicitly exposes this).
* Each `supply`/`ground` port **MUST** connect to exactly one global net per instantiation context.

---

## 2.4 Bundles (Structured Port Groups)

A **Bundle** is a typed, named group of ports (e.g., a differential pair or an amplifier I/O interface).

```cas
bundle Diff   { p: electrical; n: electrical; }
bundle AmpIO  { in: Diff; out: electrical; }
```

Bundles serve two primary purposes: they **reduce verbosity** while making **binding explicit** without ambiguity. These constructs are valid within module ports, motif ports, `slot` trait definitions, and `bind` statements.

**Normative**

* Binding a bundle **MUST** map all required fields (compile-time error if any are missing).
* Bundle field kinds **MUST** match (structural subtyping is **not** implied).

---

## 2.5 Parameters and Defaults

**Parameters** (declared via `param` or `params`) define compile-time tunables for modules and motifs. These parameters may be typed as `int`, `real`, `enum`, or **unit-typed** quantities. While default values may be provided, required parameters must be explicitly set at instantiation time.

```cas
param CL = 2pF;              // module parameter
params { m:int=1; }          // motif parameters
```

---

## 2.6 Units and Dimensions

Literals may specify **units** including voltage (`V`), current (`A`), resistance (`Ω`), capacitance (`F`), inductance (`H`), frequency (`Hz`), gain (`dB`), phase (`deg`), time (`s`, `ps`), power (`mW`), and noise density (`nV/√Hz`). The compiler enforces **dimensional consistency** across all expressions and specifications. SI prefixes undergo automatic conversion, while non-linear units such as decibels are treated as scalars with semantics defined per metric (detailed in Chapter 5).

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

* `attach` — bind a structural motif to a target instance with explicit port mapping.

  ```cas
  attach Cascode on dp { in<-dp.drain_l; bias<-vb_casc; vref<-VDD; }
  ```

* `pair` — instantiate symmetric **left/right** branches with `.l`/`.r` handles.

  ```cas
  pair casN = NMOSCascode(dp.drain_l, dp.drain_r) { bias=vb_casc; ref<-GND; };
  ```

* `CurrentMirror` — **general** mirror motif (preferred over specialized variants).

  ```cas
  mirP = new CurrentMirror(polarity=PMOS) {
    sense <- dp.drain_l;      // diode device at sense node
    vref  <- VDD;             // PMOS reference rail
    taps  { n2:1, OUT:2; }    // multi-tap with ratios
  };
  ```

* `fb R(...)`, `fb C(...)` — feedback creators (expand to `Res`/`Cap` instances with direction metadata). See 2.13 for passive kinds/scope.

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
* Only entities with **`char{}`** manifests (2.13) are eligible for synthesis.

---

## 2.13 Passive Devices: **Kinds** and **Scope**

The cascode language recognizes that not all passive elements serve equivalent purposes, distinguishing between **physical** and **notional** passives based on their role in the design flow.

**Physical passives** (enter layout, DRC/LVS, parasitics):

```cas
C1 = new Cap(OUT, GND) { kind=MIM | MOM | MFC; value=500fF; }
R1 = new Res(A, OUT)   { kind=TFR | Poly | Metal | Pseudo; value=10kΩ; }
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

## 2.14 Characterization (`char`) for Synthesizable Libraries

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

## 2.15 SPICE Interoperability: `wrap spice`

`wrap spice """ … """ map { … }` turns a SPICE subckt into a **motif** with ports/params/contracts.

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

## 2.16 Clocks and Phases

* `clk` ports carry timing semantics; `phase {}` specifies frequency, duty, edge slew.

```cas
clk phi; phase { phi: 500MHz, duty=50%, t_rise<=50ps; }
```

**Normative**

* Clocked motifs (e.g., `StrongArmLatch`) **MUST** expose a `clk` and document timing contracts that benches rely on.

---

## 2.17 Diagnostics and Provenance

Tools should report which constraints are **binding**, identify blocks that dominate **power**, **noise**, or **headroom** budgets, and suggest targeted edits such as "increase `L` on load" or "enable `MillerRz`" compensation.

CasIR must record complete **provenance** information including the library entity chosen, parameter values, compensation style realized, and source `.cas` line ranges.

---

## 2.19 Summary

* **Bindings are mandatory**. No auto-binding. Use **Bundles** and **bind** blocks, `connect`, or `cascade`.
* **Compensation is a stage property** (`comp { … }`), not a separate object.
* **Diff pair tails are separate motifs**; connect `Tail*` to the diff-pair source.
* Prefer **general `CurrentMirror`** over specialized loads.
* **`env{}` defines the bench harness** (load/source/supply/ICMR/corners).
* **Passive kinds** are explicit; sugar is confined to harness/probes.
* **Slots must be filled** by **synth** or an explicit **structural fill**.
* CasIR preserves structure, roles, compensation, harness intents, and provenance for search, sizing, and verification.
