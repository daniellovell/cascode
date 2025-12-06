# cascode

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="spec/logos/cascode_banner_dark.png">
  <img alt="Cascode Logo" src="spec/logos/cascode_banner_light.png">
</picture>

![CI](https://github.com/daniellovell/cascode/actions/workflows/dotnet.yml/badge.svg)

*Synthesized analog description language for rapid, portable analog/mixed-signal design*

**cascode** is a concise, object-oriented language for specifying **what** an analog system must do (specs, environment) and **how** it may be built (structural motifs), with an integrated synthesis workflow that turns `.cas` into a canonical IR (`.cir`) and a verified SPICE netlist.

It's designed to be **engineer-friendly** (reads like a schematic), **LLM-friendly** (modules, traits, and clear verbs), and **tool-friendly** (typed units, canonical IR, contracts).

## Getting Started

New to Cascode? Start here to understand the core concepts through a practical OTA example:

- **[Getting Started Guide](docs/GETTING_STARTED.md)**

## Language Specification
- [Chapter 1 – Introduction](spec/language/Ch01_Introduction.md)
- [Chapter 2 – Core Concepts](spec/language/Ch02_Core_Concepts.md)
- [Chapter 3 – CasIR: The Intermediate Representation](spec/language/Ch03_CasIR.md)



## 🚀 Install

- npm (prebuilt binaries, zero .NET runtime required)

  ```sh
  npm install -g @cascode/cascode-cli
  ```

  Notes: The npm wrapper downloads a self-contained `cascode` binary for your
  platform from GitHub Releases. If your network blocks GitHub, set
  `CASCODE_DOWNLOAD_BASE` to a mirror and reinstall.

- .NET global tool (requires .NET 8 SDK)

  ```sh
  dotnet tool install -g Cascode.Cli
  ```

- Standalone release (download and add to PATH)

  Download the archive matching your OS/arch (e.g., `cascode-linux-x64.tar.gz`)
  from the Releases page, extract, and place `cascode` on your PATH.

After install, verify:

```sh
cascode --version
cascode --help
```

### Latest vs pre-release

- Stable (latest):
  - npm: `npm install -g @cascode/cascode-cli`
  - dotnet tool: `dotnet tool install -g Cascode.Cli`

- Pre-release (release candidates, nightly tags):
  - npm: `npm install -g @cascode/cascode-cli@next` (or pin a specific tag, e.g. `@0.2.0-rc.1`)
  - dotnet tool: `dotnet tool install -g Cascode.Cli --version 0.2.0-rc.1`
  - Direct download: grab the matching asset from the GitHub release marked "Pre-release".

---

## 💡 Why cascode?

* **Bridges behavior and structure.** Mix spec-only requests ("meet GainBandwidth/PhaseMargin/PassbandGain") with structural guidance ("choose from {tele-cascode, folded-cascode}").
* **Motif-centric.** Build with well‑named blocks: `DiffPair`, `CurrentMirror`, `MillerRz`, `StrongArmLatch`, etc.
* **Concise structural sugar.** One-liners for mirrors, feedback, symmetry, and topology attachments: `mirror`, `fb`, `pair`, `attach`.
* **Synthesis built-in.** `slot` + `synth` select and size topologies from libraries characterized with SPICE.
* **Typed units and contracts.** Units like `1.2V`, `2pF`, `100MHz` are first-class; contracts (`req`/`ens`) capture headroom and validity.
* **CasIR.** A canonical typed graph that downstream tools and LLMs can reason about far better than raw SPICE.

---

## 📝 Language at a Glance

### Spec-only amplifier

In this example, the amplifier is defined by the specification and the **synthesis will choose the topology** from available topologies.


```java
package analog.amp; import lib.ota.*;

module AmpAuto implements SingleEndedAmplifier {
  supply VDD; ground GND;
  port in IN: Diff;
  port out OUT: analog;

  env {
    vdd   = VDD;
    icmr  in [0.55V..0.75V];
    load  C = CL;     // mandatory bench load
    source Z = 50;    // mandatory bench source impedance
  }

  spec {
    GainBandwidth >= 100MHz;
    PhaseMargin   >= 60deg;
    PassbandGain  >= 70dB;
    OutputSwing(OUT) in [0.2V..1.0V];
    Power         <= 1mW;
  }

  // Stage placeholder; synthesis chooses the actual topology.
  slot Core: AmplifierStage bind { IN -> IN; OUT -> OUT; }

  synth {
    from lib.ota.*;
    fill Core;
    prefer inputPolarity = NMOS;
    objective minimize Power;
  }

  // Testbenches for this topology are inherited from
  // the `SingleEndedAmplifier` trait, but they may 
  // be overriden, as shown below.
  bench { SEAmplifierACBench; UnityUGF; Step; }
}
```

### Guided selection

In this example, the amplifier is defined by the specification and the synthesis will choose the topology **from the allowed topologies**.

```java
module AmpGuided implements SingleEndedAmplifier {
  supply VDD; ground GND;
  port in IN: Diff;
  port out OUT: analog;

  // ... Environment `env` defined same as `AmpAuto` example ...
  // ... Specifications `spec` defined same as `AmpAuto` example ...
  spec { GainBandwidth>=120MHz; PhaseMargin>=60deg; PassbandGain>=72dB; Power<=1mW; }

  slot Core : AmplifierStage; 
  slot Comp : Compensator?;

  synth {
    from lib.ota.*;
    fill Core;

    // Constrain the search space
    allow Core in { TeleCascodeNMOS, FoldedCascodePMOS };
    prefer Comp in { MillerRC, MillerRz };
    forbid GainBoosting;
    objective minimize Power;
  }
}
```

### Manual 5T OTA

Here the topology is manually defined using reusable building blocks, called "motifs" from Cascode's standard library.

```java
package analog.ota; import lib.std.amp.*; import lib.std.prim.*;

module OTA5T implements SingleEndedAmplifier {
  supply VDD; ground GND;
  port in IN: Diff;
  port out OUT: analog;
  bias VTAIL;

  env {
    vdd = VDD;
    icmr in [0.55V..0.75V];
    load C = 1pF;
    source Z = 50;
  }

  use {
    // Differential pair with internal tail device
    dp = new DiffPair { p=NMOS; hasTail=true } {
      IN.P -> IN.P; IN.N -> IN.N; BASE -> GND; BIAS -> VTAIL;
    };

    // PMOS current mirror as active load
    cm = new CurrentMirror { p=PMOS; taps=1 };
    attach cm to dp;   // Uses CurrentMirrorLike → DiffPairLike connector

    // Single-ended output taken from the sensed branch
    connect dp.OUT.N -> OUT;
  }

  spec { GainBandwidth>=50MHz; PassbandGain>=55dB; PhaseMargin>=60deg; OutputSwing(OUT) in [0.2V..1.6V]; Power<=2mW; }
  bench { SEAmplifierACBench; UnityUGF; Step; }
}
```

#### SPICE wrap as a reusable "lego" (wide-swing mirror)

```java
motif WideSwingPMOSMirror {
  ports { SENSE: analog; OUT: analog; VDD: supply; }
  params { m:int=1; Wp=2u; Lp=0.18u; }

  wrap spice """
    .subckt WS_PMOS_MIRROR sense out vdd m=1 Wp=2u Lp=0.18u
    M1 out  sense vdd vdd pch W={Wp*m} L={Lp}
    M2 sense sense vdd vdd pch W={Wp}   L={Lp}   ; diode
    .ends
  """ map { SENSE=sense; OUT=out; VDD=vdd; }
}
```


### System-level sense chain

This example shows a system-level sense chain with a front-end block, a baseband filter, a variable gain amplifier, and an output driver. The synthesis will choose the topology from the available topologies. 

It will make these choices based on the specifications of each block, each of their own `env` and `spec` blocks, and the overall `SenseChainAuto` `env` and `spec` blocks.

```java
module SenseChainAuto {
  supply VDD; ground GND;
  port in VIN;
  port out VOUT;

  env {
    source { Z=10; range=[0V..1V]; }
    load   { C=5pF; }
  }

  spec {
    PassbandGain == 40dB +/- 1dB over [10kHz..2MHz];
    NoiseIn <= 20nV/sqrtHz at 100kHz;
    Settle(out, 1% step(0->1V)) <= 1us;
    Power <= 10mW;
  }

  slot FrontEnd : FrontEndBlock;
  slot Filter   : BasebandFilter?;
  slot VGA      : VariableGainAmp?;
  slot Driver   : OutputDriver;

  synth {
    from lib.sense.*, lib.filters.*, lib.buffers.*;
    fill FrontEnd, Filter, VGA, Driver;
    prefer FrontEnd in { InverterTIA, OTA_TIA };
    objective minimize Power;
  }

  bench { ChainAC; ChainNoise; Step; }
}
```

---

## ⚙️ From `.cas` to `.cir` to SPICE -- The Synthesis/Verification Flow

1. **Parse & Normalize**

   * Read `.cas`, resolve packages, check units and types, expand sugar (`pair`, `mirror`, `fb`).
   * Canonicalize specs and environment into inequalities.

2. **Lower to CasIR (`.cir`)**

   * Emit a **typed graph**: nets, ports, motif instances, edges, roles, constraints, benches, provenance.

3. **Feasibility Guards** (fast checks)

   * Headroom stacks, ICMR, GainBandwidth vs. Power, PhaseMargin (two-stage guards), device/legal limits.

4. **Topology Selection (if `synth {}` present)**

   * Build the **search space** from libraries (`Synthesizable` motifs/modules with `char {}` manifests).
   * **SAT** for structure + **SMT/OMT** for mixed Boolean/real feasibility and objectives (`allow/forbid/prefer/objective`).

5. **Sizing Initialization**

   * gm/Id + LUT-backed fits (convex/GP where possible) to determine $V_{ov}$, currents, $W/L$, compensation values.

6. **SPICE-Level Verification**

   * Auto-generate benches (AC/Noise/Tran, PSS/PNOISE when relevant).
   * Run across PVT and a limited MC budget; aggregate metrics and margins.

7. **Optimization Loop**

   * If misses, run sizing optimization (GP, adjoint-based gradients, or derivative-free).
   * If still infeasible, perform **minimal topological edits** within the chosen family; else re-select topology (bounded).

8. **Artifacts & Reports**

   * Outputs: `.cir` (CasIR), synthesized SPICE netlist(s), bench results, constraints/margins report, and provenance (which library blocks, parameters, and fits were used).

> **Why CasIR?** It's compact, unambiguous, and far easier for downstream tools to analyze than raw SPICE. It preserves intent (roles, traits, benches) and provenance.

**CasIR snippet (for `OTA5T`, illustrative)**:

```json
{
  "nets":[{"id":"VDD","type":"supply"},{"id":"GND","type":"supply"},
          {"id":"vinp"},{"id":"vinn"},{"id":"nL"},{"id":"nR"},{"id":"vout"}],
  "motifs":[
    {"id":"dp","type":"DiffPair",
     "ports":{"IN.P":"vinp","IN.N":"vinn","OUT.N":"nN","OUT.P":"nP","BASE":"GND","BIAS":"vbias_n"}},
    {"id":"cm","type":"CurrentMirror",
     "ports":{"SENSE":"nN","TAP[0]":"nP"}},
    {"id":"cl","type":"Cap","ports":{"p":"vout","n":"GND"}, "params":{"C":1e-12}}
  ],
  "constraints":{
    "numeric":["GainBandwidth>=5.0e7","PhaseMargin>=60deg","PassbandGain>=55","Power<=2e-3",
               "OutputSwing(vout) in [0.2,1.6]"]
  },
  "benches":["SEAmplifierACBench","UnityUGF","Step"],
  "provenance":{"source":"examples/OTA5T.cas"}
}
```

---

## 📁 Repository Layout
```
cascode/
├─ README.md
├─ spec/
│  └─ language/
│     ├─ Ch01_Introduction.md
│     ├─ Ch02_Core_Concepts.md
│     └─ Ch03_CasIR.md
├─ spec/casir-schema/
│  └─ casir-json-1.schema.json
├─ lib/
│  └─ std/
│     ├─ prim/                 # Primitive motifs + interface traits
│     ├─ amp/                  # Amplifier traits and topologies
│     │  └─ ota/
│     └─ refs/                 # Reference circuits (current/voltage references)
├─ tools/
│  ├─ cli/
│  └─ parser/
├─ tests/
│  ├─ golden/              # Canonical Cascode/CasIR/SPICE fixtures (see below)
│  ├─ integration/
│  └─ unit/
├─ editors/
│  └─ vscode/
└─ examples/
   └─ harnesses/
```

### Component Responsibilities

- `tools/parser`: Hosts `Cascode.g4` (ANTLR v4) and parser setup for C#.
- `tools/compiler`: Front end that turns ADL into CasIR (name/units/type checks, trait conformance, desugaring of attach/pair/mirror/fb, IR build with provenance).
- `tools/casir`: CasIR object model, canonical JSON writer (sorted keys/ids, explicit units), and JSON Schema validation.
- `tools/workspace`: Cadence workspace scanning, PDK device/model catalog, and workspace database persistence.
- `tools/bench`: Testbench harness discovery, bench generation, and SPICE backend adapters (Ngspice, Spectre).

### Notes
- Build artifacts go in `build/` (not committed).
- CasIR on disk is JSON only with explicit units; the JSON Schema lives under `spec/casir-schema/`.

---

## 🧪 Quick Start (build & test)

CLI / compiler build

```bash
# Build everything (compiler, CLI, tests)
dotnet build

# Run the CLI
dotnet run --project tools/cli/Cascode.Cli.csproj
```

## ♻️ Golden fixtures

`tests/golden/` is the canonical store for regression assets that tie Cascode
sources to expected CasIR/SPICE outputs.

---

## 💻 CLI (preview)

> Architecture, command modules, and snapshot testing workflow are documented in [tools/README.md](tools/README.md).

```bash
# Compile ADL to CasIR
cascode build tests/golden/cas/ota/OTA5TSingleEndedSimplified.cas
# Output: build/OTA5TSingleEndedSimplified.cir
```

---

## 🎨 Editor Support

Syntax highlighting for `.cas` files is available for VS Code, Cursor, and other editors:

```bash
# Install for VS Code / Cursor / VSCodium (macOS/Linux)
cd editors/vscode && ./install.sh

# Windows (PowerShell)
cd editors\vscode; .\install.ps1
```

Highlights keywords (`module`, `slot`, `synth`, `spec`), typed units (`1.8V`, `15pF`, `50MHz`), connection operators (`->`, `<->`), and more. The style guide standardizes bind/connect arrows as `pin -> net`. See [editors/README.md](editors/README.md) for details and GitHub Linguist integration.

---

## 🤝 Contributing

* See `CONTRIBUTING.md` for coding standards, style, and the language conformance suite.
* Library authors: include a `char { ... }` block with benches, PVT grid, sweeps, and fitted models.
* Please add minimal, runnable examples with each new motif or trait.

---

## 📄 License

BSD-3
