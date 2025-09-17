# **cascode Language Specification - Chapters 1 & 2 (Draft v0.1)**

## **Chapter 1 - Introduction**

### 1.1 Purpose and Scope

**cascode** is a concise, object-oriented analog description language for **mixed structural-behavioral** circuit design. It enables designers to:

* express **intent** (specifications, operating environment) and **structure** (motifs, interconnect) in one source file (`.cas`), and
* synthesize and verify implementations through a canonical intermediate representation, **CasIR** (`.cir`), and **SPICE** netlists.

cascode is meant for:

* Analog/RF IC designers who want to work **behaviorally** (spec-first) or **structurally** (schematic-style), or a hybrid.
* Library authors who provide **reusable, characterized motifs** (native or wrapped SPICE) for the synthesis engine.
* Tooling that performs **topology selection**, **gm/Id sizing**, and **SPICE verification**.

This specification defines the language surface, core semantics, and the artifacts required by the toolchain. Detailed algorithms for topology search and sizing are out of scope; their **contracts and interfaces** are in scope.

---

### 1.2 Motivation

Analog and mixed-signal (A/MS) IP underpins high-performance systems (clocking, power management, sensing, high-speed I/O). Yet analog design automation lags RTL flows because design **intent** is captured at the wrong level (GUI schematics and raw SPICE), which obscures **structure**, **roles**, and **constraints**. As highlighted in the accompanying research proposal, this **representation gap** hinders:

* **scalable synthesis** and systematic optimization,
* **reuse** of proven building blocks across technologies,
* and **LLM-assisted** planning and diagnostics.

**cascode** addresses this by:

* making **intent first-class** (explicit specs, environment, benches),
* making **structure canonical** (named motifs, typed ports, roles, patterns),
* and providing **CasIR**, a normalized graph that is both tool- and LLM-friendly.

---

### 1.3 Design Goals and Non-Goals

**Goals**

* **Mixed abstraction**: support *spec-only*, *guided*, and *fully structural* descriptions.
* **Concise & familiar**: Java/C#-style classes, interfaces, object initializers; schematic-like verbs.
* **Motif-centric**: circuits are composed from **motifs** with typed ports and **contracts**.
* **Typed units**: `1.2V`, `2pF`, `100MHz`, `60deg`, `1mW` with compile-time unit checking.
* **Synthesis built-in**: `slot` + `synth` directives choose, size, and verify implementations.
* **Interoperability**: `wrap spice` turns SPICE subcircuits into first-class motifs.
* **Traceability**: CasIR preserves provenance, constraints, and bench intents.

**Non-Goals**

* Replace SPICE device models or analog simulation semantics.
* Guarantee unique optimality of chosen topologies (the engine may use heuristics/OMT).
* Mandate a particular PDK, simulator, or gm/Id table format.

---

### 1.4 Source Artifacts and File Types

* **`.cas`** - *cascode* source (modules, motifs, traits, specs, synth directives).
* **`.cir`** - **CasIR** intermediate representation (typed graph; machine-readable JSON/YAML/CBOR; schema in Chapter 7).
* **SPICE netlists** - generated for verification (simulator-specific, e.g., Spectre).

---

### 1.5 Language in One Page (Informative)

**Spec-only (engine picks topology)**

```cas
package analog.amp; import lib.ota.*;

class AmpAuto implements Amplifier {
  supply VDD=1.2V; ground GND; port in_p vip, in_n vin; port out vout; param CL=2pF;
  env  { icmr in [0.55V..0.75V]; load C=CL; }
  spec { gbw>=100MHz; pm>=60deg; gain>=70dB; swing(vout) in [0.2V..1.0V]; power<=1mW; }
  slot Core: AmplifierStage; slot Comp: Compensator?;
  synth { from lib.ota.*; fill Core, Comp; prefer inputPolarity=NMOS; objective minimize power; }
  bench { AC_OpenLoop; UnityUGF; Step; }
}
```

**Structural (5T OTA, concise)**

```cas
package analog.ota; import lib.motifs.*;
class OTA5T implements Amplifier {
  supply VDD=1.8V; ground GND; port in_p vinp, in_n vinn; port out vout; bias vbias_n;
  use {
    dp = new DiffPairNMOS(vinp, vinn) { gnd=GND; tail=vbias_n; };
    attach FiveTLoadPMOS on dp { vdd=VDD; out=vout; };
    C(vout, GND, 1pF);
  }
  spec { gbw>=50MHz; gain>=55dB; pm>=60deg; swing(vout) in [0.2V..1.6V]; power<=2mW; }
}
```

**SPICE wrap (wide-swing mirror motif)**

```cas
motif WideSwingPMOSMirror implements CurrentMirror {
  ports { sense, out: electrical; vdd: supply; }
  params { m:int=1; Wp=2u; Lp=0.18u; }
  wrap spice """
    .subckt WS_PMOS_MIRROR sense out vdd m=1 Wp=2u Lp=0.18u
    M1 out  sense vdd vdd pch W={Wp*m} L={Lp}
    M2 sense sense vdd vdd pch W={Wp}   L={Lp}
    .ends
  """ map { sense=sense; out=out; vdd=vdd; }
}
```

---

### 1.6 CasIR and the Toolchain (Overview)

The **compiler** takes `.cas` and produces **CasIR** (`.cir`), then drives a **synthesis/verification** pipeline:

1. **Parsing & Normalization** -> units, roles, constraints, sugar expansion (`mirror`, `fb`, `pair`).
2. **CasIR Emit** -> typed graph: nets, ports, motif instances, role tags, numeric/graph constraints, bench intents.
3. **Feasibility Guards** -> headroom stacks, ICMR, GBW vs power, PM heuristics.
4. **Topology Selection** (if `synth {}` present) -> SAT/SMT/OMT over a library of **Synthesizable** motifs/modules using their **char** manifests.
5. **Sizing Initialization** -> gm/Id LUTs + convex/GP fits for currents, $V_{ov}$, $W/L$, compensation.
6. **SPICE Verification** -> auto benches (AC/noise/transient; PVT/MC), metric aggregation.
7. **Optimization & Minimal Edits** -> sizing tweaks; bounded structural edits within the chosen family.

Outputs: **CasIR**, **SPICE**, and a **diagnostics report** mapping spec margins to responsible blocks.

---

### 1.7 Intended Audience

* **Designers**: analog/RF/mixed-signal IC engineers.
* **Library builders**: motif authors, technology integrators, PDK adapters.
* **Tool developers**: synthesis and verification backends; IDEs.

---

### 1.8 Normative Keywords

The terms **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are to be interpreted as in RFC 2119.

---

### 1.9 Conformance

A *cascode* implementation is conformant if it:

* accepts syntactically valid `.cas` programs (Chapter 11),
* performs unit/type checks and emits diagnostic codes specified herein,
* produces CasIR conforming to the Chapter 7 schema,
* respects the semantics of `slot`/`synth`/`char` and bench intents,
* and enforces contracts and structural typing rules in this specification.

---
