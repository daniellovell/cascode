# cascode

*Computer-Aided Synthesis Code for analog & mixed-signal design.*

* **Language files:** `*.cas`
* **Intermediate representation (IR):** `*.cir` (CasIR)

**cascode** is a concise, object-oriented language for specifying **what** an analog system must do (specs, environment) and **how** it may be built (structural motifs), with an integrated synthesis workflow that turns `.cas` into a canonical IR (`.cir`) and a verified SPICE netlist.

It's designed to be **engineer-friendly** (reads like a schematic), **LLM-friendly** (classes, interfaces, and clear verbs), and **tool-friendly** (typed units, canonical IR, contracts).


## Why cascode?

* **Bridges behavior and structure.** Mix spec-only requests ("meet GBW/PM/gain") with structural guidance ("choose from {tele-cascode, folded-cascode}").
* **Motif-centric.** Build with well-named blocks: `DiffPairNMOS`, `PMOSCascodeLoad`, `MillerRz`, `StrongArmLatch`, etc.
* **Concise structural sugar.** One-liners for mirrors, feedback, symmetry, and topology attachments: `mirror`, `fb`, `pair`, `attach`.
* **Synthesis built-in.** `slot` + `synth` select and size topologies from libraries characterized with SPICE.
* **Typed units and contracts.** Units like `1.2V`, `2pF`, `100MHz` are first-class; contracts (`req`/`ens`) capture headroom and validity.
* **CasIR.** A canonical typed graph that downstream tools and LLMs can reason about far better than raw SPICE.

---

## Language at a Glance

### Spec-only amplifier (you pick the topology)

```cas
package analog.amp; import lib.ota.*;

class AmpAuto implements Amplifier {
  supply VDD = 1.2V; ground GND;
  port in_p vip, in_n vin; port out vout;
  param CL = 2pF;

  env  { icmr in [0.55V..0.75V]; load C = CL; }
  spec { gbw>=100MHz; pm>=60deg; gain>=70dB; swing(vout) in [0.2V..1.0V]; power<=1mW; }

  slot Core : AmplifierStage;      // choose a core
  slot Comp : Compensator?;        // optional compensation

  synth {
    from lib.ota.*;                // search space
    fill Core, Comp;               // decide these slots
    prefer inputPolarity = NMOS;
    objective minimize power + 0.2*area;
  }

  bench { AC_OpenLoop; UnityUGF; Step; NoiseIn; }
}
```

### Guided selection (whitelist topologies)

```cas
class AmpGuided implements Amplifier {
  supply VDD=1.2V; ground GND;
  port in_p vip, in_n vin; port out vout; param CL=3pF;

  env  { load C=CL; icmr in [0.5V..0.8V]; }
  spec { gbw>=120MHz; pm>=60deg; gain>=72dB; power<=1mW; }

  slot Core : AmplifierStage; slot Comp : Compensator?;

  synth {
    from lib.ota.*;
    allow Core in { TeleCascodeNMOS, FoldedCascodePMOS };
    prefer Comp in { MillerRC, MillerRz };
    forbid GainBoosting;
    objective minimize power;
  }
}
```

### Structural 5T OTA (concise)

```cas
package analog.ota; import lib.motifs.*;

class OTA5T implements Amplifier {
  supply VDD=1.8V; ground GND;
  port in_p vinp, in_n vinn; port out vout; bias vbias_n;

  use {
    dp = new DiffPairNMOS(vinp, vinn) { gnd=GND; tail=vbias_n; };
    attach FiveTLoadPMOS on dp { vdd=VDD; out=vout; };  // diode+mirror load in 1 line
    C(vout, GND, 1pF);
  }

  spec { gbw>=50MHz; gain>=55dB; pm>=60deg; swing(vout) in [0.2V..1.6V]; power<=2mW; }
  bench { AC_OpenLoop; UnityUGF; Step; }
}
```

### Structural 5T OTA (explicit mirrors)

```cas
use {
  dp = new DiffPairNMOS(vinp, vinn) { gnd=GND; tail=vbias_n; };

  pm = mirror.PMOS(sense=dp.out_l, vdd=VDD,
                   taps={ n2:1, nmir:2, vout:2 });   // auto diode at sense
  nm = mirror.NMOS(sense=pm.nmir, gnd=GND, taps={ vout:1 }); // auto diode at sense

  C(vout, GND, 1pF);
}
```

#### SPICE wrap as a reusable "lego" (wide-swing mirror)

```cas
motif WideSwingPMOSMirror implements CurrentMirror {
  ports { sense, out: electrical; vdd: supply; }
  params { m:int=1; Wp=2u; Lp=0.18u; }

  wrap spice """
    .subckt WS_PMOS_MIRROR sense out vdd m=1 Wp=2u Lp=0.18u
    M1 out  sense vdd vdd pch W={Wp*m} L={Lp}
    M2 sense sense vdd vdd pch W={Wp}   L={Lp}   ; diode
    .ends
  """ map { sense=sense; out=out; vdd=vdd; }
}
```

#### Self-biased inverter OTA / TIA (feedback sugar)

```cas
class InverterOTA implements Amplifier {
  supply VDD=1.2V; ground GND; port in vin; port out vout;

  use {
    inv = new InverterGm(vdd=VDD, gnd=GND);
    inv.in <- vin; inv.out -> vout;
    fb R(vout -> vin, 20MOhm) { type=Auto; }  // MOS pseudo-res if needed
    C(vout, GND, 0.5pF);
  }

  spec { gbw>=50MHz; pm>=60deg; gain>=35dB; power<=500uW; }
}
```

### Strong-arm latch (clocked comparator)

```cas
class SALatch implements Comparator {
  supply VDD=1.2V; ground GND; port in_p vip, in_n vin; diff out(vop, von); clk phi;

  use { sa = new StrongArmLatch(vip, vin, phi, vop, von) { vdd=VDD; gnd=GND; }; }

  spec { decision_time(phi@posedge, DeltaVin=5mV) <= 300ps; offset <= 2mV; kickback_in <= 30mV; power <= 1mW; }
  bench { LatchDecision; OffsetMC; Kickback; }
  phase { phi: 500MHz, duty=50%, t_rise<=50ps; }
}
```

### System-level sense chain (spec-first pipeline)

```cas
class SenseChainAuto {
  supply VDD=1.2V; ground GND; port in vin; port out vout;

  env {
    source { Z=10Ohm; range=[0V..1V]; }
    load   { C=5pF; }
  }

  spec {
    gain == 40dB +/- 1dB over [10kHz..2MHz];
    in_noise <= 20nV/sqrtHz at 100kHz;
    settle(out, 1% step(0->1V)) <= 1us;
    power <= 10mW;
  }

  slot FrontEnd : FrontEndBlock;
  slot Filter   : BasebandFilter?;
  slot VGA      : VariableGainAmp?;
  slot Driver   : OutputDriver;

  synth {
    from lib.sense.*, lib.filters.*, lib.buffers.*;
    fill FrontEnd, Filter, VGA, Driver;
    prefer FrontEnd in { InverterTIA, OTA_TIA };
    objective minimize power;
  }

  bench { ChainAC; ChainNoise; Step; }
}
```

---

## From `.cas` to `.cir` to SPICE -- The Synthesis/Verification Flow

1. **Parse & Normalize**

   * Read `.cas`, resolve packages, check units and types, expand sugar (`pair`, `mirror`, `fb`).
   * Canonicalize specs and environment into inequalities.

2. **Lower to CasIR (`.cir`)**

   * Emit a **typed graph**: nets, ports, motif instances, edges, roles, constraints, benches, provenance.

3. **Feasibility Guards** (fast checks)

   * Headroom stacks, ICMR, GBW vs. power, PM (two-stage guards), device/legal limits.

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

**CasIR snippet (for `OTA5T`)**:

```json
{
  "nets":[{"id":"VDD","type":"supply"},{"id":"GND","type":"supply"},
          {"id":"vinp"},{"id":"vinn"},{"id":"nL"},{"id":"nR"},{"id":"vout"}],
  "motifs":[
    {"id":"dp","type":"DiffPairNMOS",
     "ports":{"in_p":"vinp","in_n":"vinn","out_l":"nL","out_r":"nR","tail":"vbias_n","gnd":"GND"}},
    {"id":"m5t","type":"FiveTLoadPMOS","ports":{"target":"dp","out":"vout","vdd":"VDD"}},
    {"id":"cl","type":"Cap","ports":{"p":"vout","n":"GND"}, "params":{"C":1e-12}}
  ],
  "constraints":{
    "numeric":["GBW>=5.0e7","PM>=60deg","Gain_dB>=55","Power<=2e-3",
               "Swing(vout) in [0.2,1.6]"]
  },
  "benches":["AC_OpenLoop","UnityUGF","Step"],
  "provenance":{"source":"examples/OTA5T.cas"}
}
```

---

## Repository Layout
```
cascode/
├─ README.md
├─ LICENSE
├─ spec/
│  ├─ LanguageSpec.md
│  ├─ Grammar.ebnf
│  └─ CasIR.md
├─ lib/
│  ├─ motifs/               # standard motif library
│  ├─ traits/
│  ├─ patterns/
│  └─ tech/                 # tech adapters, gm/Id LUTs, fits (placeholders)
├─ examples/
│  ├─ AmpAuto.cas
│  ├─ AmpGuided.cas
│  ├─ OTA5T.cas
│  ├─ OTATelescopic.cas
│  ├─ InverterOTA.cas
│  ├─ InverterTIA.cas
│  ├─ SALatch.cas
│  └─ SenseChainAuto.cas
├─ tools/
│  ├─ cli/                  # cascode CLI
│  └─ parser/
├─ tests/
│  ├─ conformance/
│  └─ golden/
└─ docs/
   ├─ getting-started.md
   └─ benchmarks/AMSGENBench.md
```

---

## CLI (preview)

```bash
# Synthesize topology and emit CasIR
cascode synth examples/AmpAuto.cas -o build/AmpAuto.cir

# Verify with SPICE + benches (tool selection and PDK binding vary by setup)
cascode verify build/AmpAuto.cir --spice spectre --pdk gpdk045

# End-to-end (synth + size + verify + report)
cascode run examples/AmpGuided.cas --pdk gpdk045 --out build/
```

---

## Contributing

* See `CONTRIBUTING.md` for coding standards, style, and the language conformance suite.
* Library authors: include a `char { ... }` block with benches, PVT grid, sweeps, and fitted models.
* Please add minimal, runnable examples with each new motif or trait.

---

## License

BSD-3
