

## **Chapter 2 - Core Concepts**

> This chapter defines the **semantic scaffolding** of cascode: the building blocks, how they relate, and the invariants the compiler and tools rely upon. Syntax appears informally; the formal grammar is in Chapter 11.

---

### 2.1 Programs, Packages, and Imports

* A **program** comprises one or more `.cas` files under a package namespace.
* `package` declares a namespace; `import` brings names into scope.
* Names are resolved by **lexical scope** with package-qualified fallback. Shadowing rules are identical to Java/C# unless otherwise stated.

---

### 2.2 Modules, Motifs, Traits

* A **module** is a top-level *design entity* with **ports**, **(optional) parameters**, optional **use** and **wire** sections (structural), **spec**/**env**/**bench** (behavioral), and optionally **slot**/**synth** (synthesis).
* A **motif** is a *reusable circuit building block* with **ports**, **params**, and **contracts**; it encapsulates internal structure. Motifs MAY be authored natively or via **`wrap spice`**.
* A **trait** (interface) defines a contract (a set of **ports**, **roles**, and **behavioral expectations**) that modules/motifs **implement**. Traits enable substitution during synthesis (e.g., anything implementing `AmplifierStage` may fill `slot Core : AmplifierStage`).

**Normative**

* An entity **implementing** a trait **MUST** declare a superset of the trait's ports and satisfy its declared **contracts** (Section 2.7).
* A module is **instantiable** if all required ports are bound and all required `slot`s are **filled** (statically or via `synth`).

---

### 2.3 Ports, Roles, and Net Types

**Port kinds** (non-exhaustive):

* `supply`, `ground` - special; **MUST NOT** short to `electrical`.
* `electrical` - general (single-ended).
* `diff` - differential pair abstraction (provides `.p`/`.n`).
* `bias` - bias/control nets (typed for headroom constraints).
* `rf`, `clk` - specialized kinds with additional contracts (impedance, phase).

**Roles**

* Ports and internal nets MAY be annotated with **roles** (e.g., `stage1_out`, `ota_out`, `cmfb_ctrl`).
* Roles guide **pattern recognition**, **contracts**, and **bench generation**.

**Normative**

* Port kind **compatibility** is enforced at connect time (e.g., `bias -> gate` allowed within motifs; `bias -> out` forbidden unless a motif explicitly exposes it).
* `supply`/`ground` ports **MUST** connect to exactly one global net each per instantiation context.

---

### 2.4 Parameters and Defaults

* **Parameters** (`param`) define compile-time tunables for modules/motifs (e.g., `CL=2pF`).
* Parameters MAY be **typed** (`int`, `real`, `enum`, `unit-typed`).
* Default values MAY be provided; required parameters **MUST** be set at instantiation.

**Example**

```cas
param CL = 2pF;       // module parameter with default
params { m:int=1; }   // motif parameters inside a motif
```

---

### 2.5 Units and Dimensions

* Numeric literals MAY specify **units**: `V, A, Ohm, F, H, Hz, dB, deg, s, ps, mW, nV/sqrt(Hz)`, etc.
* The compiler **MUST** enforce **dimensional consistency** across expressions and spec relations.
* Conversions between SI prefixes are automatic; non-linear units (e.g., dB) are **scalars with semantics** defined per metric (Chapter 5).

**Examples**

```cas
supply VDD = 1.2V;
spec { gbw>=100MHz; gain>=70dB; pm>=60deg; power<=1mW; }
```

---

### 2.6 Structural Composition (Conceptual)

**Objects and wiring**

* `use {}` creates **instances** of motifs/modules using `new` and **inline binding** (object initializers).
* `wire { A >> B >> C; }` provides **pipeline** wiring by role.
* `connect` (explicit) wiring is allowed for fine control.

**Schematic-like sugar**

* `attach` - bind a **structural motif** to a target (e.g., `attach FiveTLoadPMOS on dp { vdd=VDD; out=vout; }`).
* `pair` - instantiate symmetric **left/right** branches with `.l`/`.r` handles.
* `mirror.PMOS/NMOS` - create **multi-tap** mirrors with an implied diode at the **sense** tap.
* `fb R(...)`, `fb C(...)` - **feedback** primitives; `type=Auto` MAY select pseudo-resistors where appropriate.
* `alias` - name an internal net as an externally visible port.

**Normative**

* Sugar constructs **MUST** expand into equivalent primitive motifs and connections in CasIR.
* Structural composition **MUST** be acyclic unless the motif explicitly defines a legal loop (e.g., cross-coupled latch).

---

### 2.7 Contracts and Patterns

* **Contracts** capture **assumptions** (`req`) and **guarantees** (`ens`) at the motif boundary, enabling compositional reasoning and synthesis pruning.

  * Examples: *headroom >= 0.35 V*, *gain_min_db >= 20*, *valid ICMR in [0.4,0.9] V*.
* **Patterns** specify **recognizers and binders** for common subgraphs (e.g., 5T mirror load), allowing ingestion from SPICE and canonicalization to motifs.

**Normative**

* Tools **MUST** enforce `req` at instantiation and MAY reject designs that cannot satisfy them under the declared `env`.
* `ens` are **promises** used for feasibility and search; violations discovered in verification **MUST** be reported.

---

### 2.8 Behavioral Description: `spec`, `env`, `bench`

* **`spec {}`** declares **required metrics** and relations (e.g., `gbw`, `pm`, `gain`, `noise_in`, `sr`, `settle`, `zt`, `swing(node)`, `power`).
* **`env {}`** declares **operating conditions**: source impedance and range, load, ICMR, supply, temperature, corner set.
* **`bench {}`** selects measurement benches (AC/Noise/Tran; domain-specific such as `LatchDecision`).

**Normative**

* Metric names map to **measurement intents** in CasIR; the tool **MUST** generate benches consistent with these intents.
* Specs are **hard constraints** unless marked as **objectives** in `synth`.

---

### 2.9 Synthesis: `slot` and `synth`

* A **`slot`** is a typed placeholder to be **filled** by synthesis (e.g., `slot Core : AmplifierStage;`).
* **`synth {}`** defines the **search policy**:

  * `from` - libraries (packages) to search (entities marked `Synthesizable` and providing `char {}`).
  * `fill` - which slots to decide.
  * `allow`/`forbid`/`prefer` - **structural constraints** over candidate sets.
  * `objective` - scalar objective (e.g., `minimize power + 0.2*area`).

**Normative**

* If a slot is declared and not bound in `use`, the program **MUST** include a `synth` that can fill it, or compilation **MUST** fail.
* Synthesis **MUST** respect `allow/forbid` as hard constraints and `prefer` as soft objectives.
* Only entities with **characterization manifests** (`char {}`) are eligible for synthesis (Section 2.10).

---

### 2.10 Characterization (`char`) for Synthesizable Libraries

Library authors **MUST** provide, per **Synthesizable** motif/module:

* **`char {}` manifest** with:

  * `benches` - the set of characterization benches (e.g., `ac_openloop`, `noise_in`, `step`).
  * `pvt` - the PVT grid used for characterization.
  * `sweep` - parameter domains (e.g., `CL`, `VDD`, `gmId`).
  * `fit` - surrogate models (GP, PWL, affine) for metrics consumed by synthesis (GBW, gain, PM, noise, power).
  * `validity` - operating domains (ICMR, swing) where fits apply.

**Normative**

* Synthesis engines **MUST** consult `char` fits for coarse feasibility and ranking before running SPICE.
* Final acceptance **MUST** rely on SPICE verification; fit errors **MUST** be surfaced in diagnostics.

---

### 2.11 SPICE Interoperability: `wrap spice`

* `wrap spice """ ... """ map { ... }` turns a SPICE subckt into a **motif** with declared **ports**, **params**, and optional **contracts**.
* Wrapped motifs **MAY** be marked **Synthesizable** if accompanied by a `char` manifest.

**Normative**

* The `map {}` section **MUST** bind SPICE subckt pins to cascode ports bijectively.
* Any parameter interpolation semantics **MUST** be documented by the library.

---

### 2.12 Clocks and Phases

* `clk` ports carry timing semantics; `phase {}` can declare frequency, duty, rise/fall targets.
* Clocked motifs (e.g., `StrongArmLatch`) **MUST** expose a `clk` and document timing contracts.

**Example**

```cas
clk phi; phase { phi: 500MHz, duty=50%, t_rise<=50ps; }
```

---

### 2.13 Diagnostics and Provenance

* Tools **SHOULD** report which constraints are **binding**, which blocks dominate **power/noise/headroom**, and suggested **edits**.
* CasIR **MUST** record **provenance**: which library entity and parameter set were chosen, and from which `.cas` lines.

---

### 2.14 Minimal Examples (Concept Recap)

**Slots + Synthesis**

```cas
slot Core: AmplifierStage; slot Comp: Compensator?;
synth { from lib.ota.*; fill Core, Comp; allow Core in {TeleCascodeNMOS, FoldedCascodePMOS}; }
```

**Contracts & Patterns**

```cas
motif DiffPairNMOS implements AmplifierStage {
  ports { in_p, in_n, out_l, out_r: electrical; tail: bias; gnd: ground; }
  contract { req headroom>=0.35V; ens gm_min>=GmHint; }
}
pattern FiveT_MirrorLoad { /* recognizer binding a diode+mirror pair */ }
```

**Mirrors & Feedback**

```cas
pm = mirror.PMOS(sense=dp.out_l, vdd=VDD, taps={ n2:1, nmir:2, vout:2 });
fb R(vout -> vin, 20MOhm) { type=Auto; }   // pseudo-res MOS if needed
```

---

### 2.15 Summary

* **Modules, motifs, traits** define structure and substitutability.
* **Ports, roles, units** make intent explicit and type-safe.
* **Contracts, patterns** provide legality and recognition.
* **Specs, env, benches** define behavior and measurement.
* **Slots + synth + char** connect specification to realizations.
* **CasIR** preserves all of the above for tools and verification.

---

> **Next chapters** (not included here) specify syntax/semantics in detail (Chapter 3-6), CasIR (Chapter 7), the toolchain (Chapter 8), the standard library (Chapter 9), interoperability and ingestion (Chapter 10), and formal grammar and diagnostics (Chapter 11).
