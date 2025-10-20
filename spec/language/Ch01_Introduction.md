

## **Chapter 1 - Introduction**

### 1.1 Purpose and Scope

**cascode** is a concise, object-oriented analog description language designed for **mixed structural-behavioral** circuit design. The language empowers designers to express both **intent** (specifications and operating environment) and **structure** (motifs and interconnect) within a unified source file (`.cas`), enabling seamless synthesis and verification through a canonical intermediate representation (**CasIR**, `.cir`) and standard **SPICE** netlists.

The language addresses the needs of three primary constituencies. Analog and RF IC designers benefit from the flexibility to work behaviorally with specification-driven design, structurally with schematic-style composition, or through hybrid approaches that combine both paradigms. Library authors can contribute **reusable, characterized motifs** - whether authored natively or wrapped from existing SPICE subcircuits - that integrate directly with the synthesis engine. Finally, automated tooling leverages cascode's structured representation to perform **topology selection**, **gm/Id sizing**, and comprehensive **SPICE verification**.

This specification defines the language surface, core semantics, and the artifacts required by the toolchain. Detailed algorithms for topology search and sizing are out of scope; their **contracts and interfaces** are in scope.

---

### 1.2 Motivation

Analog and mixed-signal (A/MS) IP forms the foundation of high-performance systems, enabling critical functions such as clocking, power management, sensing, and high-speed I/O. Despite this importance, analog design automation significantly lags behind RTL flows due to a fundamental issue: design **intent** is captured at inappropriate abstraction levels - through GUI schematics and raw SPICE netlists - that obscure essential **structure**, **roles**, and **constraints**. This **representation gap** systematically undermines **scalable synthesis** and optimization, prevents effective **reuse** of proven building blocks across different technologies, and inhibits **LLM-assisted** planning and diagnostics.

**cascode** directly addresses these limitations through three architectural principles. First, the language elevates **intent to first-class status** by requiring explicit specifications, operating environments, and benchmark definitions. Second, it establishes **canonical structural representation** through named motifs, typed ports, well-defined roles, and recognizable patterns. Finally, it provides **CasIR**, a normalized graph representation designed for both automated tooling and machine learning applications.

---

### 1.3 Design Goals and Non-Goals

**Goals**

The language design prioritizes **mixed abstraction** capabilities, supporting specification-only, guided, and fully structural design methodologies within a single framework. Syntactic familiarity draws from Java and C# conventions, employing classes, interfaces, and object initializers alongside schematic-inspired verbs that resonate with analog designers. The architecture centers on a **motif-centric** approach where circuits compose from reusable **motifs** that expose typed ports and well-defined **contracts**.

Type safety extends to physical dimensions through **typed units** (`1.2V`, `2pF`, `100MHz`, `60deg`, `1mW`) with comprehensive compile-time checking. The language incorporates **synthesis as a native construct** via `slot` and `synth` directives that automatically choose, size, and verify implementations. **Interoperability** with existing workflows leverages `wrap spice` constructs that elevate SPICE subcircuits to first-class motifs. Throughout the design flow, **traceability** ensures that CasIR preserves complete provenance, constraints, and benchmark intents.

**Non-Goals**

Several capabilities remain explicitly outside the language scope. cascode does not replace SPICE device models or analog simulation semantics, instead leveraging these established foundations. The synthesis engine may employ heuristics and optimization modulo theories (OMT) without guaranteeing unique optimality of chosen topologies. Finally, the language avoids mandating specific PDK formats, simulators, or gm/Id table structures, maintaining flexibility across tool ecosystems.

---

### 1.4 Source Artifacts and File Types

The cascode toolchain operates on three primary file types. **`.cas`** files contain cascode source code, encompassing modules, motifs, traits, specifications, and synthesis directives. **`.cir`** files represent the **CasIR** intermediate representation as typed graphs serialized in machine-readable formats (JSON, YAML, or CBOR) according to the schema defined in Chapter 7. Finally, **SPICE netlists** are generated for verification purposes, formatted according to simulator-specific requirements (such as Spectre).

In summary:

* **`.cas`** - *cascode* source (modules, motifs, traits, specs, synth directives).
* **`.cir`** - **CasIR** intermediate representation (typed graph; machine-readable JSON/YAML/CBOR; schema in Chapter 7).
* **SPICE netlists** - generated for verification (simulator-specific, e.g., Spectre).

---

### 1.5 Cascode in a few examples

**Spec-only definition of an amplifier (engine picks topology)**

```java
package analog.amp; import lib.ota.*;

bundle Diff { P: electrical; N: electrical; }

module AmpAuto implements SingleEndedAmplifier {
  supply VDD=1.2V; ground GND;
  port in IN: Diff; port out OUT: electrical;
  param CL=2pF;

  env  {
    vdd = VDD;
    icmr in [0.55V..0.75V];
    load C = CL;           // mandatory bench load
    source Z = 50;        // mandatory bench source impedance
  }

  spec {
    GainBandwidth>=100MHz; PhaseMargin>=60deg; PassbandGain>=70dB;
    OutputSwing(OUT) in [0.2V..1.0V];
    Power<=1mW;
  }

  slot Core: AmplifierStage bind { IN<-IN; OUT->OUT; }

  // Choose topology via synthesis and **enable** comp (or disable with 'None')
  synth {
    from lib.ota.*;
    fill Core;
    prefer inputPolarity = NMOS;
    // Optional compensation policy on the chosen Core
    Core.comp { style=MillerRC; Cc=Auto; Rz=Auto; }   // or: Core.comp None;
    objective minimize Power;
  }

  bench { SEAmplifierACBench; UnityUGF; Step; }
}
```

**Structural definition of a 5T OTA**

```java
package analog.ota; import lib.motifs.*;

bundle Diff { P: electrical; N: electrical; }

module OTA5T implements SingleEndedAmplifier {
  supply VDD=1.8V; ground GND;
  port in IN: Diff; port out OUT: electrical;

  env  { vdd = VDD; load C = 1pF; source Z = 50; }

  use {
    dp = new DiffPair { p=NMOS; hasTail=true } {
      IN.P <- IN.P; IN.N <- IN.N; BASE <- GND; BIAS <- vbias_n;
    };

    cm = new CurrentMirror { p=PMOS; taps=1 };
    attach cm on dp { SENSE <- OUT.N; TAP <- OUT.P };
    OUT <- dp.OUT.P;  // Single‑ended pickoff for illustration.
  }

  spec { GainBandwidth>=50MHz; PassbandGain>=55dB; PhaseMargin>=60deg; OutputSwing(OUT) in [0.2V..1.6V]; Power<=2mW; }
}

```

**Structural single-ended CS amplifier with primitive transistor**

```java
package analog.ota; import lib.motifs.*;

module CommonSourceAmp implements SingleEndedAmplifier {
  supply VDD=1.8V; ground GND;
  port in vin; port out vout;
  bias vb1;

  env  {
    vdd = VDD;
    load C = 1pF;
    source Z = 50;
  }

  spec { GainBandwidth>=50MHz; PassbandGain>=40dB; PhaseMargin>=60deg; Power<=5mW; }

  use {
    // Primitive NMOS input transistor (synthesis sizes W/L from specs)
    M_in = new NMOS() { gate<-vin; drain<-vout; source<-GND; bulk<-GND; };

    // Generic active load motif with polarity
    load = new ActiveLoad(polarity=PMOS) { node<-vout; bias<-vb1; vref<-VDD; };
  }
}

```

**Two stages in cascade with slots**

Option 1: Synthesis fills both slots

```java
module TwoStageAmp implements SingleEndedAmplifier {
  supply VDD=1.2V; ground GND;
  port in IN: Diff; port out OUT: electrical;
  net N1: electrical;

  slot S1: AmplifierStage bind { IN<-IN; OUT->N1; }
  slot S2: AmplifierStage bind { IN<-N1; OUT->OUT; }

  synth {
    from lib.ota.*;
    fill S1, S2;
    S1.comp { style=MillerRC; Cc=Auto; Rz=Auto; }   // enable comp on first stage
    S2.comp None;                                   // no comp on second
    objective minimize Power;
  }
}
```

Option 2: Structural fill (no `synth` needed)

```java
module TwoStageAmp_Manual implements SingleEndedAmplifier {
  supply VDD=1.2V; ground GND;
  port in IN: Diff; port out OUT: electrical;
  net N1: electrical;

  slot S1: AmplifierStage bind { IN<-IN; OUT->N1; }
  slot S2: AmplifierStage bind { IN<-N1; OUT->OUT; }

  use {
    fill S1 with FoldedCascodePMOS { /* params… */ };
    S1.comp { style=MillerRC; Cc=Auto; }

    fill S2 with CSStageNMOS       { /* params… */ };
    S2.comp None;
  }
}

```

**SPICE wrap (wide-swing NMOS mirror motif)**

```java
motif WideSwingNMOSMirror implements CurrentMirror {
  ports {
    sense, out, ibias: electrical;
    vss: supply;
  }

  params {
    n: int = 1;     // mirror ratio parameter
    Wn = 2u;        // base width
    Ln = 0.18u;     // length
  }

  wrap spice """
    .subckt WS_NMOS_MIRROR sense out ibias vss n=1 Wn=2u Ln=0.18u
    * M5 sized (W/L)/(n+1)^2
    M5 ibias ibias vss vss nch   W={Wn/((n+1)*(n+1))} L={Ln}
    * M1 and M4 sized (W/L)/n^2
    M1 out  ibias N002 N002 nch  W={Wn/(n*n)}       L={Ln}
    M4 sense ibias N001 N001 nch W={Wn/(n*n)}       L={Ln}
    * M2 and M3 sized W/L
    M2 N002 sense vss vss nch W={Wn} L={Ln}
    M3 N001 sense vss vss nch W={Wn} L={Ln}
    .ends
  """ map {
    sense = sense;
    out = out;
    ibias = ibias;
    vss = vss;
  }
}
```

---

### 1.6 CasIR and the Toolchain (Overview)

The **compiler** transforms `.cas` source files into **CasIR** (`.cir`) intermediate representation, then orchestrates a comprehensive **synthesis and verification** pipeline. The compilation process encompasses seven distinct phases:

1. **Parsing & Normalization** processes units, roles, and constraints while expanding syntactic sugar for constructs like `mirror`, `fb`, and `pair`. Primitive transistors (`NMOS`, `PMOS`) are recognized and emit as topology-only motif instances.
2. **CasIR Emission** generates a typed graph containing nets, ports, motif instances (including primitives), role tags, numeric and graph constraints, and benchmark intents. All representation remains process-agnostic.
3. **Feasibility Guards** validate headroom stacks, input common-mode range (ICMR), gain-bandwidth versus power tradeoffs, and phase margin heuristics.
4. **Topology Selection** (when `synth {}` directives are present) employs SAT/SMT/OMT solvers over libraries of **Synthesizable** motifs and modules, guided by their **char** manifests.
5. **Sizing Initialization** leverages gm/Id lookup tables from the active PDK and convex/geometric programming fits to determine currents, overdrive voltages ($V_{ov}$), transistor aspect ratios ($W/L$), and compensation parameters. Primitive transistors are sized using the same methodology as structured motifs.
6. **SPICE Verification** executes automated benchmarks across AC, noise, and transient analyses with process/voltage/temperature and Monte Carlo variations, aggregating performance metrics. By EL, primitives in CasIR carry the selected `impl.pdk_device`, which the SPICE emitter uses directly.
7. **Optimization & Minimal Edits** performs sizing refinements and bounded structural modifications within the selected topology family.

The pipeline produces three primary outputs: **CasIR** intermediate representation, **SPICE** netlists for simulation, and a **diagnostics report** that traces specification margins to responsible circuit blocks.

---

### 1.7 Intended Audience

This specification serves three primary audiences. **Designers** - including analog, RF, and mixed-signal IC engineers - represent the primary users who will author `.cas` source files and interpret synthesis results. **Library builders** encompass motif authors, technology integrators, and PDK adapters who contribute reusable components to the cascode ecosystem. **Tool developers** focus on synthesis and verification backends as well as integrated development environments that support the cascode workflow.

---

### 1.8 Normative Keywords

The terms **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are to be interpreted as in [RFC 2119](https://www.ietf.org/rfc/rfc2119.txt).

---

### 1.9 Conformance

A *cascode* implementation is conformant if it:

* accepts syntactically valid `.cas` programs (Chapter 11),
* performs unit/type checks and emits diagnostic codes specified herein,
* produces CasIR conforming to the Chapter 7 schema,
* respects the semantics of `slot`/`synth`/`char` and bench intents,
* and enforces contracts and structural typing rules in this specification.

---

### 1.10 Digital Standard Cells as Motifs (Overview)

Many analog and mixed‑signal designs leverage digital standard cells (stdcells)
for buffering, phase detection, and simple logic around analog cores. cascode
supports this by treating stdcells as ordinary motifs with electrical ports and
explicit supply pins. Stdcells typically enter the system through `wrap spice`
from PDK‑provided subcircuits and may participate in synthesis when annotated
with `char {}` manifests.

The integration follows three key architectural principles. Stdcells function as first-class motifs with standard `electrical` ports and explicit `supply` and `ground` rails, requiring no specialized net domains. Library traits communicate functional intent (such as `InverterLike`) to enable slots to be filled by either single stdcells or composite drivers interchangeably. Finally, timing and usage metrics for stdcells are expressed through electrical measurements including `RiseTime`, `FallTime`, `VOH`, and `VOL`, with verification performed through standard benches.

Example: Strong‑arm latch to pad with a selectable output inverter:

```cas
package analog.io; import lib.std.sky130.hd.*; import lib.comp.*;

module LatchToPad {
  supply VDD=1.8V; ground GND; port in vip, vin; port out PAD;
  net COMP_OUT: electrical;

  env { vdd=VDD; load C on PAD = 15pF; source Z = 50; }

  use {
    sa = new StrongArmLatch() { vdd=VDD; gnd=GND; };
    sa.in_p <- vip; sa.in_n <- vin; sa.out -> COMP_OUT;
  }

  // Let synthesis choose a stdcell inverter or a composite pad driver
  slot Buf: InverterLike bind { IN<-COMP_OUT; OUT->PAD; }

  spec {
    RiseTime(PAD, 0.1*VDD, 0.9*VDD) <= 1.2ns;
    FallTime(PAD, 0.9*VDD, 0.1*VDD) <= 1.2ns;
    VOH(PAD) >= 0.9*VDD; VOL(PAD) <= 0.1*VDD; Power<=2mW;
  }

  bench { StepToggle { node=COMP_OUT; freq=50MHz; duty=50%; } }

  synth {
    from lib.std.sky130.hd.*;                 // stdcell wrappers
    allow Buf in { INV_*, PadDriver };        // single cell or composite
    prefer minimize DynamicPower;
  }
}
```

In this flow, the heavy pad load (15 pF) appears via `env{}` and the synthesis
engine chooses an implementation of `InverterLike` that meets `RiseTime` /
`FallTime` constraints at `PAD` while respecting power objectives. The choice
may be a single strong INV or a composite `PadDriver` that implements the same
trait.
