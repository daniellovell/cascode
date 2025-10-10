# Cascode Syntax Highlighting Features

This document summarizes the syntax highlighting capabilities based on the Cascode language specification (Chapters 1-2).

## What's Highlighted

### Core Language Constructs

**Packages and Modules** (purple/keyword):
- `package`, `import` - namespace declarations
- `class`, `motif`, `trait`, `interface` - entity declarations
- `implements`, `extends` - inheritance

**Structured Port Groups** (purple/keyword):
- `bundle` - structured port definitions (e.g., `bundle Diff { p: electrical; n: electrical; }`)

**Port and Net Declarations** (purple/keyword):
- `supply`, `ground` - power rails
- `port`, `ports` - port declarations (singular and plural forms)
- `net` - internal nets
- `param`, `params` - parameters (singular and plural forms)

### Port Kinds (blue/type)
- `electrical` - general single-ended
- `diff` - differential bundle
- `bias` - bias/control nets
- `clk` - clock signals
- `rf` - RF signals
- `digital`, `analog`, `thermal`, `signal` - specialized kinds

### Block Keywords (purple/keyword)
- `env` - operating environment
- `use` - instance creation
- `spec` - specifications
- `bench` - benchmark selection
- `synth` - synthesis directives
- `slot` - typed placeholders
- `phase` - clock phase definitions
- `char` - characterization manifests

### Synthesis Directives (purple/keyword)
- `from` - search space
- `fill` - slot filling
- `allow` - whitelist constraints
- `forbid` - blacklist constraints
- `prefer` - soft preferences
- `objective` - optimization objectives
- `minimize`, `maximize` - objective directions

### Structural Composition (purple/keyword)
- `attach` - bind motif to target
- `pair` - symmetric left/right branches
- `mirror` - current mirror (deprecated, use `CurrentMirror` motif)
- `fb` - feedback
- `new` - instantiation
- `connect` - explicit connections
- `cascade` - sequential connections
- `alias` - port aliasing
- `bind` - port binding

### SPICE Interoperability (purple/keyword)
- `wrap spice` - SPICE subcircuit wrapper
- `map` - pin mapping

### Contracts and Characterization (purple/keyword)
- `req` - requirements (contracts)
- `ens` - ensures (guarantees)
- `char` - characterization block
- `benches` - characterization benches
- `pvt` - process/voltage/temperature
- `sweep` - parameter sweeps
- `fit` - fitted models
- `validity` - validity regions

### Compensation (purple/keyword)
- `comp` - compensation property (e.g., `Core.comp { style=MillerRC; }`)

### Passive Device Properties (purple/keyword)
- `kind` - device kind (MIM, TFR, etc.)
- `value` - device value
- `taps` - mirror tap ratios

### Environment Properties (purple/keyword)
- `vdd` - supply voltage
- `load` - load specification
- `source` - source impedance
- `temp` - temperature
- `icmr` - input common-mode range
- `on` - placement (e.g., `load C on PAD`)

### Bench Parameters (purple/keyword)
- `node` - toggled node
- `freq` - frequency
- `duty` - duty cycle
- `cycles` - number of cycles
- `slew` - slew rate
- `fixtures` - bench fixtures

### Mirror Parameters (purple/keyword)
- `sense` - sense node
- `vref` - reference rail
- `polarity` - NMOS/PMOS

### Typed Units (yellow/orange)

All SI-prefixed physical quantities are highlighted:

- **Voltage**: `1.8V`, `0.9*VDD`, `500mV`, `10µV`
- **Current**: `1mA`, `500µA`, `10nA`
- **Capacitance**: `15pF`, `2pF`, `100nF`
- **Resistance**: `50Ω`, `20MOhm`, `1kΩ`
- **Inductance**: `2nH`, `1µH` (added)
- **Time**: `1.2ns`, `300ps`, `1µs`
- **Frequency**: `50MHz`, `100MHz`, `1GHz`
- **Temperature**: `27C`, `125C`, `300K`
- **Power**: `2mW`, `500µW`, `1W`
- **Angle**: `60deg`, `45deg`
- **Decibels**: `70dB`, `-3dB`
- **Percentage**: `10%`, `50%`
- **Noise**: `20nV/√Hz` (added)

### Connection Operators (pink/red)
- `->` - drives (output)
- `<-` - driven by (input)
- `<->` - bidirectional

### Specification Functions (yellow/gold)

**Performance Metrics**:
- `gbw` - gain-bandwidth product
- `pm` - phase margin
- `gain`, `gain_min_db` - gain specs
- `swing` - output swing
- `power` - power consumption
- `area` - area metric

**Noise and Dynamic**:
- `noise_in`, `in_noise` - input-referred noise
- `sr` - slew rate
- `zt` - zero-tau frequency
- `settle` - settling time
- `headroom` - voltage headroom

**Edge/Level Metrics** (for stdcell integration):
- `rise_time` - rising edge time
- `fall_time` - falling edge time
- `voh` - output high voltage
- `vol` - output low voltage
- `toggle_power`, `dynamic_power` - dynamic power

**Comparator Metrics**:
- `decision_time` - decision time
- `offset` - input offset
- `kickback_in` - kickback noise

### Bench Names (yellow/gold)
- `AC_OpenLoop` - AC open-loop analysis
- `UnityUGF` - unity gain frequency
- `Step` - step response
- `NoiseIn` - input noise
- `StepToggle` - toggle bench
- `LatchDecision` - latch decision bench
- `OffsetMC` - offset Monte Carlo
- `Kickback` - kickback analysis
- `ChainAC`, `ChainNoise` - chain analyses

### Component Constructors (yellow/gold)
- `C(...)` - capacitor
- `R(...)` - resistor  
- `L(...)` - inductor (added)

### Characterization Functions (yellow/gold)
- `GP(...)` - geometric programming fit
- `PWL(...)` - piecewise linear fit
- `affine(...)` - affine model

### Built-in Functions (yellow/gold)
- `step` - step function
- `posedge`, `negedge` - edge triggers

### Primitive Types (blue/cyan)
- `int`, `float`, `double`, `real`
- `bool`, `string`, `void`
- `enum` - enumeration
- `capacitance`, `resistance`, `inductance` - unit types

### Common Traits (cyan/blue)
- `Amplifier` - amplifier interface
- `AmplifierStage` - amplifier stage
- `Compensator` - compensation
- `CurrentMirror`, `CurrentMirrorLike` - current mirrors
- `InverterLike` - inverter-like (stdcell trait)
- `Comparator` - comparator interface
- `FrontEndBlock` - front-end block
- `BasebandFilter` - baseband filter
- `VariableGainAmp` - VGA
- `OutputDriver` - output driver

### Common Motifs (cyan/blue)

**Differential Pairs**:
- `DiffPairNMOS`, `DiffPairPMOS`
- `TailNMOS`, `TailPMOS`

**Loads**:
- `PMOSCascodeLoad`, `FiveTLoadPMOS`
- `NMOSCascode`

**Compensation**:
- `MillerRC`, `MillerRz`, `Ahuja`

**Amplifiers**:
- `TeleCascodeNMOS`, `FoldedCascodePMOS`
- `CSStageNMOS` - common-source stage
- `InverterGm` - inverter-based Gm

**Comparators**:
- `StrongArmLatch`

**Miscellaneous**:
- `GainBoosting`
- `PadDriver` - composite pad driver
- `WideSwingPMOSMirror`, `WideSwingNMOSMirror`

**Passive Devices**:
- `Cap` - capacitor
- `Res` - resistor
- `Ind` - inductor

### Constants and Special Names (orange)

**Boolean**:
- `true`, `false`

**Null**:
- `null`, `nil`, `None`

**Device Types**:
- `NMOS`, `PMOS` - transistor polarities
- `pch`, `nch` - SPICE model names

**Process Corners**:
- `TT` - typical-typical
- `SS` - slow-slow
- `FF` - fast-fast

**Capacitor Kinds**:
- `MIM` - metal-insulator-metal
- `MOM` - metal-oxide-metal
- `MFC` - metal-fringe capacitor

**Resistor Kinds**:
- `TFR` - thin-film resistor
- `Poly` - polysilicon
- `Metal` - metal resistor
- `Pseudo` - MOS pseudo-resistor

**Inductor Kinds**:
- `Spiral` - spiral inductor
- `MIMStack` - MIM stack

**Strength Hints**:
- `Auto` - automatic selection
- `X1`, `X2`, `X4`, `X8` - drive strength multiples

**Power Rails**:
- `VDD`, `GND` - common rail names

**Compensation Styles** (special highlighting):
- `MillerRC` - Miller with RC
- `MillerRz` - Miller with Rz nulling
- `Ahuja` - Ahuja compensation

## Syntax Features

### Comments
- Line comments: `// comment`
- Block comments: `/* comment */`

### Strings
- Double-quoted: `"string"`
- Single-quoted: `'string'`
- Triple-quoted: `""" SPICE subckt """` (for `wrap spice`)

### Numbers
- Integers: `42`, `1`
- Floats: `3.14`, `1.0`
- Scientific: `1e-12`, `2.5e9`
- Hex: `0xFF`, `0x1A`

### Operators
- Comparison: `==`, `!=`, `<=`, `>=`, `<`, `>`
- Arithmetic: `+`, `-`, `*`, `/`, `%`
- Logical: `&&`, `||`, `!`
- Assignment: `=`, `+=`, `-=`, `*=`, `/=`
- Range: `..` (e.g., `[0.5V..0.8V]`)
- Connection: `->`, `<-`, `<->`

## Language-Specific Features

### Explicit Binding
All connections must be explicit:
```cas
slot Core: AmplifierStage bind { in<-IN; out->OUT; }
connect A.out -> B.in;
```

### Bundles
Structured port groups:
```cas
bundle Diff { p: electrical; n: electrical; }
port in IN: Diff;
```

### Compensation as Property
```cas
Core.comp { style=MillerRC; Cc=Auto; Rz=Auto; }
S2.comp None;
```

### Characterization
```cas
char {
  benches { ac_openloop; noise_in; step; }
  pvt { TT@27C, SS@-40C, FF@125C; }
  sweep { CL:[0.5pF..5pF]; VDD:[1.0V..1.3V]; }
  fit { gbw~GP("fit/gbw.gp"); power~affine(I_total, VDD); }
  validity { icmr:[0.4V..0.9V]; }
}
```

### SPICE Wrapping
```cas
wrap spice """
  .subckt MY_SUBCKT ...
  .ends
""" map { IN=A; OUT=Y; }
```

### Environment as Harness
```cas
env {
  vdd = VDD;
  load C on PAD = 15pF;      // becomes bench harness
  source Z = 50;
  icmr in [0.55V..0.75V];
}
```

## Editor Support

The grammar is implemented as a TextMate grammar (`.tmLanguage.json`) and works with:
- VS Code
- Cursor
- VSCodium
- Sublime Text (with import)
- TextMate
- GitHub (pending Linguist PR)

See [editors/README.md](README.md) for installation instructions.


