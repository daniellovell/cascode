# Chapter 4: Testbench Templates

> This chapter defines the testbench template system that transforms ACIR circuits into simulator-specific testbenches. Templates enable backend abstraction, allowing the same circuit and bench semantics to target multiple simulators (ngspice, Spectre) while maintaining deterministic, version-controlled test harnesses.

---

## 4.0 Summary

The testbench template system bridges ACIR circuits to simulator execution through a three-part mechanism: bench definitions declare metrics and template references, Scriban-based templates generate simulator netlists from ACIR harness data, and the compliance checker validates simulation results against numeric constraints. This architecture separates circuit design concerns from test harness implementation, enabling backend portability without duplicating bench semantics.

The flow proceeds deterministically: ACIR circuits at EL level contain `harness:` blocks specifying supply values, loads, and source impedances, plus `benches:` blocks naming which tests to run. The `cascode emit` command discovers backend-specific templates, populates them with data extracted from ACIR (including constraint-derived AC sweep parameters), and writes simulator netlists. After simulation, `cascode verify` compares measurement results against numeric constraints using SI-prefix-aware value parsing and reports pass/fail status with exit codes suitable for CI integration.

Template discovery follows upward traversal from the ACIR file location, checking for local `benches/` folders before falling back to the standard library at `lib/std/amp/benches/`. This resolution strategy supports both project-specific custom benches and shared canonical definitions, with backend selection determined by file extension (`.ngspice.tpl` vs `.spectre.tpl`).

---

## 4.1 Design Principles

The testbench template system establishes clear architectural boundaries. Separation of concerns keeps circuit topology and sizing decisions in ACIR while test stimulus, measurements, and simulator-specific syntax live in templates, preventing cross-contamination between design and verification concerns. Backend abstraction ensures that bench semantics—the metrics measured and their interpretation—remain independent of simulator choice, allowing the same ACIR circuit to target ngspice for quick iteration or Spectre for production sign-off without altering bench declarations. Template-based generation produces deterministic output suitable for version control and CI golden tests, with explicit variable substitution replacing fragile string manipulation. Constraint-driven verification automates pass/fail checking through declarative numeric constraints in ACIR, eliminating manual log parsing and ensuring consistent interpretation of simulation results across teams and tool versions.

---

## 4.2 Bench Definition Files

Bench definitions reside in `.cas` files alongside their templates, declaring the bench's identity, available metrics, and backend template references. These metadata files enable compile-time validation of metric names and provide the mapping between abstract bench names (referenced in ACIR `benches:` blocks) and concrete template files.

### 4.2.1 Syntax

```cascode
package lib.std.amp.benches;

bench SEOpAmpACBench {
  spectre_template = "SEOpAmpACBench.spectre.tpl";
  ngspice_template = "SEOpAmpACBench.ngspice.tpl";
  metrics [
    GainBandwidth: Hz,
    PassbandGain: dB,
    PhaseMargin: deg,
    LowpassBandwidth: Hz,
    HighpassBandwidth: Hz,
    BandpassBandwidth: Hz
  ]
}
```

The `bench` block declares:
- Bench name (must match the filename prefix)
- Template file references for each supported backend
- Metrics list with canonical names and units

### 4.2.2 Metrics Declaration

Metrics define the measurable quantities this bench can extract from simulation. Each metric carries:
- A canonical name (e.g., `GainBandwidth`) referenced in ACIR constraints
- A physical unit (e.g., `Hz`, `dB`, `deg`) used for constraint compliance checking

Template implementations MUST emit results with these exact metric names in the output JSON. Units MAY differ in the results (e.g., raw Hz vs MHz), but parsers normalize to the declared base unit during compliance checking.

### 4.2.3 Template References

Backend template references use simple string paths relative to the bench definition file. The paths follow the naming convention `{BenchName}.{backend}.tpl` where backend is `ngspice` or `spectre`. This convention enables automatic discovery and ensures deterministic template selection based on the `--backend` flag to `cascode emit`.

---

## 4.3 Template File Format

Templates use Scriban syntax (a Liquid-like template language) with variables populated from ACIR data by the `ACIRBenchAdapter` and `ACIRTemplateHarness` components. Templates generate complete, standalone simulator netlists ready for execution.

### 4.3.1 File Naming

Template files MUST follow the convention: `{BenchName}.{backend}.tpl`

Examples:
- `SEOpAmpACBench.ngspice.tpl`
- `SEOpAmpACBench.spectre.tpl`
- `SEAmpACBench.ngspice.tpl`

### 4.3.2 Scriban Syntax

Templates support:
- Variable substitution: `{{ variable_name }}`
- Conditionals: `{{ if condition }} ... {{ end }}`
- Loops: `{{ for item in collection }} ... {{ end }}`
- Arithmetic: `{{ value / 2 }}`
- String concatenation: embedded in expressions

### 4.3.3 Common Template Variables

All templates receive these base variables from `ACIRTemplateHarness`:

| Variable | Type | Description | Example |
|----------|------|-------------|---------|
| `circuit_name` | string | Circuit name from ACIR | `"OTA5TSingleEnded"` |
| `bench_name` | string | Bench name from ACIR benches block | `"SEOpAmpACBench"` |
| `design_file` | string | Design netlist filename | `"OTA5TSingleEnded.sp"` |
| `port_list` | string | Space-separated port/supply/ground names | `"IN_P IN_N OUT VTAIL VDD GND"` |
| `out_node` | string | Primary output node (first OUT port) | `"OUT"` |
| `generic_models` | boolean | True if circuit uses generic nmos/pmos | `true` |
| `vcm` | double | Common-mode voltage (mid-supply) | `0.9` |
| `bias_v` | double | Input bias voltage | `0.9` |

### 4.3.4 Harness Data Structures

The `harness` object contains lists extracted from ACIR `harness:` blocks:

**harness.supplies** (array of objects):

```scriban
{{ for supply in harness.supplies }}
V{{ supply.net }} {{ supply.net }} 0 DC {{ supply.value }}
{{ end }}
```

- `supply.net`: net name (e.g., `"VDD"`)
- `supply.value`: voltage value (e.g., `"1.8V"`)

**harness.loads** (array of objects):

```scriban
{{ for load in harness.loads }}
C{{ load.net }}_load {{ load.net }} 0 {{ load.c }}
{{ end }}
```

- `load.net`: net name (e.g., `"OUT"`)
- `load.c`: capacitance value (e.g., `"1p"`)

### 4.3.5 Backend-Specific Variables (Spectre)

Spectre templates receive additional environment parameters in the `env` object, intelligently derived from ACIR by `ACIRBenchAdapter`:

| Variable | Type | Description | Derivation |
|----------|------|-------------|------------|
| `env.source_ohms` | double | Source impedance (Ω) | From `harness: source IN Z=50 ohm`, default 50Ω |
| `env.cload_f` | double | Load capacitance (F) | From `harness: load OUT C=1p F` |
| `env.rload_ohms` | double | Load resistance (Ω) | Default 1GΩ (high-Z) |

**AC Sweep Parameters** (derived from constraints):

| Variable | Type | Description | Derivation |
|----------|------|-------------|------------|
| `ac_start_hz` | double | AC sweep start frequency | Constraint-derived: max(1, GBW/1000) |
| `ac_stop_hz` | double | AC sweep stop frequency | Constraint-derived: max(GBW*10, 1G) |
| `ac_mag` | double | AC stimulus magnitude | Default 1.0 |

The AC sweep derivation examines ACIR `constraints: numeric:` for GainBandwidth, GBW, UnityGainFrequency, or Bandwidth constraints. For example, a constraint `c_gbw : GainBandwidth @ OUT >= 100M Hz` yields `ac_start_hz = 100kHz` and `ac_stop_hz = 1GHz`, ensuring the sweep covers the expected circuit behavior without manual tuning.

**DC Bias Sweep Parameters**:

|| Variable | Type | Description | Example |
||----------|------|-------------|---------|
|| `sweep.<ConditionName>` | object or null | Sweep condition if present in harness | `sweep.InputDCCommonMode` |
|| `sweep.<ConditionName>.start` | double | Sweep start value | `0.3` (for 0.3V) |
|| `sweep.<ConditionName>.stop` | double | Sweep stop value | `1.5` (for 1.5V) |
|| `sweep.<ConditionName>.step` | double | Sweep step value | `0.1` (for 100mV) |

Templates should check for the presence of sweep conditions using `{{ if sweep.<ConditionName> }}` and adapt their analysis accordingly. When a sweep is present, benches must execute analyses at each sweep point and report worst-case values.

Templates do not interpret `Auto`. When a design requests `sweep <ConditionName> [Auto]` at earlier elaboration levels, the synthesis/lowering pipeline must resolve it to a concrete numeric sweep in ACIR-EL before template rendering.

**Example usage in templates:**

```spectre
{{ if sweep.InputDCCommonMode }}
VCM (vcm vss) vsource dc={{ sweep.InputDCCommonMode.start }}

sweepDC sweep param=VCM.dc start={{ sweep.InputDCCommonMode.start }} \
    stop={{ sweep.InputDCCommonMode.stop }} step={{ sweep.InputDCCommonMode.step }} {
  dcOp dc
  ac ac start={{ ac_start_hz }} stop={{ ac_stop_hz }} dec=100
}
{{ else }}
VCM (vcm vss) vsource dc={{ vcm }}
dcOp dc
ac ac start={{ ac_start_hz }} stop={{ ac_stop_hz }} dec=100
{{ end }}
```

**Spectre-Specific Objects**:

| Variable | Type | Description |
|----------|------|-------------|
| `spec.temperature_c` | double | Simulation temperature (°C) |
| `includes_with_section` | array | Include files with section parameter |
| `includes_without_section` | array | Simple include files (e.g., design .sp) |

### 4.3.6 Example: Ngspice Template

```spice
* {{ circuit_name }}_{{ bench_name }} - Generated from ACIR EL
.title {{ circuit_name }}_{{ bench_name }}

{{ if generic_models }}
* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05
{{ end }}

.include "{{ design_file }}"

* Harness
{{ for supply in harness.supplies }}
V{{ supply.net }} {{ supply.net }} 0 DC {{ supply.value }}
{{ end }}
* Differential input: common-mode bias with AC on positive input
VIN_P IN_P 0 DC {{ vcm }} AC 1
VIN_N IN_N 0 DC {{ vcm }}
{{ for load in harness.loads }}
C{{ load.net }}_load {{ load.net }} 0 {{ load.c }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
op
ac dec 100 1 10G

* Measurements
meas ac gain_dc find vdb({{ out_node }}) at=1
meas ac gbw when vdb({{ out_node }})=0 cross=1
meas ac pm_raw find vp({{ out_node }}) at=gbw
let pm = 180 + pm_raw

* Results output
echo "RESULT: PassbandGain = " gain_dc " dB"
echo "RESULT: GainBandwidth = " gbw " Hz"
echo "RESULT: PhaseMargin = " pm " deg"

quit
.endc
.end
```

### 4.3.7 Example: Spectre Template Fragment

```spectre
// Source impedance split across each leg
RINP (IN_P in_p_drv) resistor r={{ env.source_ohms/2 }}
RINN (IN_N in_n_drv) resistor r={{ env.source_ohms/2 }}

// Output load on single-ended OUT
CLOAD (OUT vss) capacitor c={{ env.cload_f }}
{{ if env.rload_ohms && env.rload_ohms > 0 }}
RLOAD (OUT vss) resistor r={{ env.rload_ohms }}
{{ end }}

// Small-signal AC sweep (ranges inferred upstream from spec)
ac ac start={{ ac_start_hz }} stop={{ ac_stop_hz }} annotate=status
```

---

## 4.4 Template Discovery

The `TemplateDiscovery` service locates template files through a deterministic search strategy that supports both project-local customization and fallback to standard library definitions.

### 4.4.1 Resolution Order

Given a bench name (e.g., `SEOpAmpACBench`) and backend type (ngspice or spectre), discovery proceeds:

1. **Upward traversal**: Starting from the ACIR file's directory, traverse parent directories looking for a `benches/` subdirectory containing the target template file (`{BenchName}.{backend}.tpl`)

2. **Standard library fallback**: If upward traversal finds no match, check `lib/std/amp/benches/` relative to the workspace root

3. **Return null**: If neither search succeeds, return null (the CLI will report an error)

### 4.4.2 Backend Selection

The backend flag (`--backend ngspice` or `--backend spectre`) determines the template filename suffix:
- Ngspice: `{BenchName}.ngspice.tpl`
- Spectre: `{BenchName}.spectre.tpl`

This enables a single bench definition to support multiple simulators with different netlist syntax, while sharing the same bench semantics (metrics, measurement intent).

### 4.4.3 Custom Bench Placement

To override a standard library bench or define project-specific benches:

1. Create a `benches/` folder in your project (at any level above the ACIR files)
2. Place `.cas` and `.tpl` files in this folder
3. Template discovery will find local definitions before falling back to `lib/std/amp/benches/`

This strategy enables gradual customization: start with standard benches, then selectively override specific templates as needed for project requirements.

---

## 4.5 ACIR Integration

The testbench system integrates with ACIR through three primary blocks: `harness:`, `constraints:`, and `benches:`.

### 4.5.1 Harness Block

The `harness:` block specifies test-only elements that do not appear in the synthesized design:

```acir
harness:
  supply VDD = 1.8V
  bias VTAIL = 0.6V
  load OUT C=1p F
  source IN Z=50 ohm
  icmr min=0.55V max=0.75V
  pvt TT@27C
```

Template variables derived from harness entries:
- `harness.supplies`: list of supply/bias declarations
- `harness.loads`: list of load capacitances
- `env.source_ohms`: extracted from source impedance declarations
- `env.cload_f`: extracted from first load capacitance
- `env.rload_ohms`: defaults to 1GΩ unless specified

### 4.5.2 Constraints Block

The `constraints:` block defines pass/fail criteria and measurement intents:

```acir
constraints:
  numeric:
    c_gbw : GainBandwidth @ OUT >= 100M Hz
    c_gain : PassbandGain @ OUT >= 40 dB
    c_pm : PhaseMargin @ OUT >= 60 deg
    c_pwr : Power <= 500u W
  tech:
    t_lmin : L >= 180n m on *
  measure:
    m_gbw : SEOpAmpACBench GainBandwidth @ OUT
    m_gain : SEOpAmpACBench PassbandGain @ OUT
    m_pm : SEOpAmpACBench PhaseMargin @ OUT
```

**Numeric constraints** drive both AC sweep parameter derivation and post-simulation compliance checking. The `ACIRBenchAdapter` examines GainBandwidth constraints to set appropriate `ac_start_hz` and `ac_stop_hz` values, ensuring the frequency sweep captures the circuit's expected bandwidth.

**Measurement intents** document which bench produces which metrics, enabling future optimizations like selective bench execution.

### 4.5.3 Benches Block

The `benches:` block lists which tests to run:

```acir
benches:
  SEOpAmpACBench
  SEOpAmpStability
```

During `cascode emit`, each named bench triggers template discovery and netlist generation. The bench name must match a `.cas` definition file discoverable through the template resolution strategy.

---

## 4.6 CLI Workflow

### 4.6.1 Emit Command

Generate simulator netlists from an ACIR circuit:

```bash
cascode emit <acir_file> --out <output_dir> --backend {ngspice|spectre}
```

**Arguments:**
- `<acir_file>`: Path to ACIR file (must be EL-level)
- `--out <output_dir>`: Output directory for generated files
- `--backend {ngspice|spectre}`: Target simulator backend

**Generated Artifacts:**

For an ACIR file `OTA5TSingleEnded.el.cir` with bench `SEOpAmpACBench`:

```bash
<output_dir>/
  OTA5TSingleEnded.sp                    # Design subcircuit
  OTA5TSingleEnded_SEOpAmpACBench.sp     # Ngspice testbench
  spec.json                               # Testbench metadata
```

Or with `--backend spectre`:

```bash
<output_dir>/
  OTA5TSingleEnded.sp                    # Design subcircuit
  OTA5TSingleEnded_SEOpAmpACBench.scs    # Spectre testbench
  spec.json                               # Testbench metadata
```

**Design File Emission:** The design subcircuit (`.sp`) always uses SPICE syntax regardless of backend, as both ngspice and Spectre can include SPICE subcircuits. The testbench file uses backend-specific syntax (`.sp` for ngspice, `.scs` for Spectre).

### 4.6.2 Verify Command

Check simulation results against ACIR constraints:

```bash
cascode verify --acir <acir_file> --results <results_json>
```

**Arguments:**
- `--acir <acir_file>`: ACIR file containing constraints
- `--results <results_json>`: Simulation results in JSON format

**Results JSON Schema:**

```json
{
  "circuit": "OTA5TSingleEnded",
  "bench": "SEOpAmpACBench",
  "measurements": {
    "gain": {
      "metric": "PassbandGain",
      "value": 45.2,
      "unit": "dB",
      "node": "OUT"
    },
    "gbw": {
      "metric": "GainBandwidth",
      "value": 150000000,
      "unit": "Hz",
      "node": "OUT"
    },
    "pm": {
      "metric": "PhaseMargin",
      "value": 65.3,
      "unit": "deg",
      "node": "OUT"
    }
  }
}
```

**Exit Codes:**
- `0`: All constraints satisfied
- Non-zero: One or more constraints failed

**Output Format:**

```text
Constraint Compliance Report for OTA5TSingleEnded
--------------------------------------------------
c_gbw    GainBandwidth @ OUT >= 100M Hz      PASS (measured: 150M Hz)
c_gain   PassbandGain @ OUT >= 40 dB        PASS (measured: 45.2 dB)
c_pm     PhaseMargin @ OUT >= 60 deg       PASS (measured: 65.3 deg)
c_pwr    Power <= 500u W       PASS (measured: 350u W)
--------------------------------------------------
Result: 4/4 constraints satisfied
```

---

## 4.7 Constraint Verification

The `ComplianceChecker` component validates simulation results against numeric constraints declared in ACIR, providing automated pass/fail reporting suitable for CI integration.

### 4.7.1 Supported Operators

Numeric constraints support five comparison operators:

| Operator | Meaning | Example |
|----------|---------|---------|
| `>=` | Greater than or equal | `GainBandwidth @ OUT >= 100M Hz` |
| `<=` | Less than or equal | `Power <= 500u W` |
| `==` | Equal (with 1e-9 tolerance) | `Gain @ OUT == 40 dB` |
| `>` | Strictly greater than | `PhaseMargin @ OUT > 45 deg` |
| `<` | Strictly less than | `RiseTime @ OUT < 10n s` |

### 4.7.2 Value Parsing with SI Prefixes

The parser handles numeric values with SI prefix multipliers:

| Prefix | Symbol | Multiplier | Example |
|--------|--------|------------|---------|
| tera | T | 10^12 | `1T` → 1e12 |
| giga | G | 10^9 | `2.5G` → 2.5e9 |
| mega | M | 10^6 | `100M` → 100e6 |
| kilo | k | 10^3 | `50k` → 50e3 |
| milli | m | 10^-3 | `500m` → 0.5 |
| micro | u | 10^-6 | `10u` → 10e-6 |
| nano | n | 10^-9 | `180n` → 180e-9 |
| pico | p | 10^-12 | `1p` → 1e-12 |
| femto | f | 10^-15 | `15f` → 15e-15 |

Values in constraints and results may use different prefixes; the parser normalizes both to base units before comparison. For example, a constraint `>= 100M Hz` matches a result `value: 150000000` (raw Hz) or `value: 150` with `unit: MHz`.

### 4.7.3 Metric and Node Matching

Constraints specify which metric to check and optionally which node:

```acir
c_gain : PassbandGain @ OUT >= 40 dB
```

The compliance checker matches this constraint to a measurement result by:
1. Case-insensitive metric name comparison (`PassbandGain` matches `passbandgain`)
2. Node name matching if specified (`@ OUT` requires result to have `"node": "OUT"`)
3. If multiple measurements have the same metric but different nodes, the node selector disambiguates

### 4.7.4 Missing Measurements

If a constraint references a metric not present in the results JSON, the checker reports:

```text
c_gain   PassbandGain @ OUT >= 40 dB        FAIL (not measured)
```

This situation indicates either:
- The bench template does not measure this metric
- The simulation failed to produce results
- A mismatch between constraint metric names and bench definition

---

## 4.8 Standard Library Benches

The standard library at `lib/std/amp/benches/` provides canonical bench definitions for common analog circuit tests. The following table shows current backend support status:

| Bench | Ngspice | Spectre | Description |
|-------|---------|---------|-------------|
| `SEOpAmpACBench` | Yes | Yes | Single-ended output operational amplifier AC analysis (differential inputs) |
| `SEAmpACBench` | Yes | Yes | Single-ended amplifier AC analysis (single input, single output) |
| `FDOpAmpACBench` | No | No | Fully-differential operational amplifier AC analysis |
| `FDOpAmpStability` | No | No | Stability analysis for fully-differential operational amplifiers |
| `SEOpAmpSettle` | No | No | Settling time analysis for operational amplifiers |
| `SEOpAmpSlew` | No | No | Slew rate measurement for operational amplifiers |
| `SEOpAmpStability` | No | No | Stability analysis (gain/phase margins) for op-amps |

Benches marked "No" have `.cas` definitions and may have legacy `.tpl` files but lack complete `.ngspice.tpl` and `.spectre.tpl` implementations. Contributions to expand backend coverage are welcome.

### 4.8.1 SEOpAmpACBench

**Purpose:** AC analysis for single-ended output operational amplifiers with differential inputs.

**Metrics:**
- `GainBandwidth` (Hz): Frequency where gain crosses 0dB
- `PassbandGain` (dB): Low-frequency gain magnitude
- `PhaseMargin` (deg): Phase margin at unity-gain frequency
- `LowpassBandwidth` (Hz): -3dB bandwidth for lowpass response
- `HighpassBandwidth` (Hz): -3dB bandwidth for highpass response
- `BandpassBandwidth` (Hz): -3dB bandwidth for bandpass response

**Circuit Requirements:**
- Differential inputs (`IN_P`, `IN_N`)
- Single-ended output (`OUT`)
- Power supplies and grounds as declared in ACIR

**Harness Configuration:**
The ngspice template applies a common-mode bias at both inputs and superimposes AC stimulus on `IN_P`, creating a differential AC signal. The Spectre template uses an ideal balun to generate differential drive from a single AC source.

### 4.8.2 SEAmpACBench

**Purpose:** AC analysis for single-ended amplifiers with single input and single output.

**Metrics:**
- `GainBandwidth` (Hz): Unity-gain frequency
- `PassbandGain` (dB): Low-frequency gain magnitude

**Circuit Requirements:**
- Single input (`IN`)
- Single-ended output (`OUT`)
- Power supplies and grounds as declared in ACIR

**Harness Configuration:**
Input receives DC bias (mid-supply by default) with AC stimulus. Simpler than `SEOpAmpACBench` as it requires no differential drive or balun structures.

### 4.8.3 SEOpAmpDCBench

**Purpose:** DC characterization for single-ended output operational amplifiers with differential inputs, measuring output DC bias and quiescent power across the input common-mode range (ICMR).

**Metrics:**
- `InputDCCommonMode` (V): ICMR sweep condition (echoed for traceability)
- `OutputDCBias` (V): Output DC level at each ICMR point
- `OutputDCBias_min` (V): Minimum output bias across ICMR sweep
- `OutputDCBias_max` (V): Maximum output bias across ICMR sweep
- `QuiescentPower` (W): Maximum static power consumption across ICMR sweep

**Circuit Requirements:**
- Differential inputs (`IN_P`, `IN_N`)
- Single-ended output (`OUT`)
- Power supplies and grounds as declared in ACIR

**Harness Configuration:**
Applies common-mode voltage to both inputs while sweeping across the ICMR range specified in the harness. Measures DC operating points and supply current at each sweep point. When no sweep is specified, performs single-point DC analysis at mid-supply.

**Sweep Support:**
This bench respects `sweep InputDCCommonMode [start:step:stop]` in the harness. When present, executes DC analysis at each ICMR point and reports worst-case values (max power, output bias range).

### 4.8.4 SEAmpDCBench

**Purpose:** DC characterization for single-ended amplifiers (single input, single output), measuring output DC bias and quiescent power across the input bias range.

**Metrics:**
- `InputDCBias` (V): Input bias sweep condition (echoed for traceability)
- `OutputDCBias` (V): Output DC level at each input bias point
- `OutputDCBias_min` (V): Minimum output bias across input bias sweep
- `OutputDCBias_max` (V): Maximum output bias across input bias sweep
- `QuiescentPower` (W): Maximum static power consumption across input bias sweep

**Circuit Requirements:**
- Single input (`IN`)
- Single-ended output (`OUT`)
- Power supplies and grounds as declared in ACIR

**Harness Configuration:**
Sweeps the input DC bias voltage across the specified range. Measures DC operating points and supply current at each sweep point. Simpler than `SEOpAmpDCBench` as it requires no differential input structure.

**Sweep Support:**
This bench respects `sweep InputDCBias [start:step:stop]` in the harness. When present, executes DC analysis at each bias point and reports worst-case values (max power, output bias range).

---

## 4.9 Authoring Custom Benches

To create a new bench definition and templates:

### 4.9.1 Step 1: Create Bench Definition

Create `{BenchName}.cas` in a `benches/` folder (either project-local or in `lib/std/amp/benches/`):

```cascode
package lib.std.amp.benches;

bench MyCustomBench {
  ngspice_template = "MyCustomBench.ngspice.tpl";
  spectre_template = "MyCustomBench.spectre.tpl";
  metrics [
    Metric1: Unit1,
    Metric2: Unit2
  ]
}
```

### 4.9.2 Step 2: Create Ngspice Template

Create `{BenchName}.ngspice.tpl` in the same directory:

```spice
* {{ circuit_name }}_{{ bench_name }} - Generated from ACIR EL
.title {{ circuit_name }}_{{ bench_name }}

{{ if generic_models }}
* Generic MOSFET models
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05
{{ end }}

.include "{{ design_file }}"

* Harness elements
{{ for supply in harness.supplies }}
V{{ supply.net }} {{ supply.net }} 0 DC {{ supply.value }}
{{ end }}

* Test stimulus and loads
* (Add your custom stimulus here)

* DUT instantiation
XDUT {{ port_list }} {{ circuit_name }}

.control
* Analysis commands
* (Add your analysis here)

* Measurements
* (Add your measurements here)

* Results output (MUST match metrics declared in .cas)
* echo "RESULT: Metric1 = " value1 " Unit1"
* echo "RESULT: Metric2 = " value2 " Unit2"

quit
.endc
.end
```

### 4.9.3 Step 3: Create Spectre Template (Optional)

Create `{BenchName}.spectre.tpl` if Spectre support is required. Use the standard library Spectre templates as reference for syntax and structure.

### 4.9.4 Step 4: Reference from ACIR

In your ACIR circuit, add the bench name to the `benches:` block:

```acir
benches:
  MyCustomBench
```

### 4.9.5 Template Authoring Guidelines

**Variable Access:** Use `{{ variable_name }}` for simple substitution. For object properties, use dot notation: `{{ harness.supplies }}`, `{{ env.source_ohms }}`.

**Conditionals:** Wrap optional content in `{{ if condition }}...{{ end }}`. Example: `{{ if generic_models }}` includes generic model cards only when the circuit uses generic devices.

**Loops:** Iterate over harness supplies, loads, or other collections using `{{ for item in collection }}...{{ end }}`. Each iteration binds `item` as the loop variable.

**Arithmetic:** Scriban supports basic arithmetic in expressions. Example: `{{ env.source_ohms/2 }}` divides source impedance across differential legs.

**Result Format:** The results parser expects measurement output in the format:

```text
RESULT: MetricName = <value> <unit>
```

For ngspice, use `echo` commands. For Spectre, use appropriate output directives or post-processing scripts to generate the JSON results file.

---

## 4.10 Complete Example: OTA with AC Bench

### 4.10.1 ACIR Circuit

```acir
ACIR 1

circuit OTA5TSingleEnded
  level EL
  supply VDD
  ground GND
  port IN_P : analog
  port IN_N : analog
  port OUT : analog
  port VTAIL : bias
  fill:
    net mirror_gate : analog
    net tnode : analog
    nmos dp.M_N (B->GND, D->mirror_gate, G->IN_P, S->tnode) : L=180n M=1 W=2u nmos
    nmos dp.M_P (B->GND, D->OUT, G->IN_N, S->tnode) : L=180n M=1 W=2u nmos
    nmos dp.M_TAIL (B->GND, D->tnode, G->VTAIL, S->GND) : L=180n M=1 W=4u nmos
    pmos cm.M_SENSE (B->VDD, D->mirror_gate, G->mirror_gate, S->VDD) : L=180n M=1 W=2u pmos
    pmos cm.M_TAP0 (B->VDD, D->OUT, G->mirror_gate, S->VDD) : L=180n M=1 W=2u pmos
  constraints:
    numeric:
      c_gbw : GainBandwidth @ OUT >= 100M Hz
      c_gain : PassbandGain @ OUT >= 40 dB
      c_pm : PhaseMargin @ OUT >= 60 deg
      c_pwr : Power <= 500u W
    tech:
      t_lmin : L >= 180n m on *
    measure:
      m_gbw : SEOpAmpACBench GainBandwidth @ OUT
      m_gain : SEOpAmpACBench PassbandGain @ OUT
      m_pm : SEOpAmpACBench PhaseMargin @ OUT
  harness:
    supply VDD = 1.8V
    bias VTAIL = 0.6V
    load OUT C=1p F
  benches:
    SEOpAmpACBench
```

### 4.10.2 Emit Command

```bash
$ cascode emit tests/golden/acir/ota/OTA5TSingleEnded.el.cir \
    --out /tmp/ota-test --backend ngspice

Design netlist: /tmp/ota-test/OTA5TSingleEnded.sp
Testbench: /tmp/ota-test/OTA5TSingleEnded_SEOpAmpACBench.sp
Emitted 1 design(s) and 1 testbench(es).
```

### 4.10.3 Generated Testbench (excerpt)

```spice
* OTA5TSingleEnded_SEOpAmpACBench - Generated from ACIR EL
.title OTA5TSingleEnded_SEOpAmpACBench

* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05

.include "OTA5TSingleEnded.sp"

* Harness
VVDD VDD 0 DC 1.8V
VTAIL VTAIL 0 DC 0.6V
* Differential input: common-mode bias with AC on positive input
VIN_P IN_P 0 DC 0.9 AC 1
VIN_N IN_N 0 DC 0.9
COUT_load OUT 0 1p

* DUT
XDUT IN_P IN_N OUT VTAIL VDD GND OTA5TSingleEnded

.control
op
ac dec 100 1 10G

* Measurements
meas ac gain_dc find vdb(OUT) at=1
meas ac gbw when vdb(OUT)=0 cross=1
meas ac pm_raw find vp(OUT) at=gbw
let pm = 180 + pm_raw

* Results output
echo "RESULT: PassbandGain = " gain_dc " dB"
echo "RESULT: GainBandwidth = " gbw " Hz"
echo "RESULT: PhaseMargin = " pm " deg"

quit
.endc
.end
```

### 4.10.4 Simulation Results JSON

After running ngspice and post-processing:

```json
{
  "circuit": "OTA5TSingleEnded",
  "bench": "SEOpAmpACBench",
  "measurements": {
    "gain": {
      "metric": "PassbandGain",
      "value": 45.2,
      "unit": "dB",
      "node": "OUT"
    },
    "gbw": {
      "metric": "GainBandwidth",
      "value": 150000000,
      "unit": "Hz",
      "node": "OUT"
    },
    "pm": {
      "metric": "PhaseMargin",
      "value": 65.3,
      "unit": "deg",
      "node": "OUT"
    },
    "power": {
      "metric": "Power",
      "value": 0.00035,
      "unit": "W",
      "node": null
    }
  }
}
```

### 4.10.5 Verify Command

```bash
$ cascode verify \
    --acir tests/golden/acir/ota/OTA5TSingleEnded.el.cir \
    --results /tmp/ota-test/OTA5TSingleEnded_SEOpAmpACBench_results.json

Constraint Compliance Report for OTA5TSingleEnded
--------------------------------------------------
c_gbw    GainBandwidth @ OUT >= 100M Hz      PASS (measured: 150M Hz)
c_gain   PassbandGain @ OUT >= 40 dB        PASS (measured: 45.2 dB)
c_pm     PhaseMargin @ OUT >= 60 deg       PASS (measured: 65.3 deg)
c_pwr    Power <= 500u W       PASS (measured: 350u W)
--------------------------------------------------
Result: 4/4 constraints satisfied
```

**Exit code:** 0 (success)

---

## 4.11 Implementation Notes

The testbench template system comprises several C# components in the Cascode toolchain:

- **`TemplateDiscovery`** (`tools/bench/TemplateDiscovery.cs`): Implements upward traversal and standard library fallback for template file location
- **`TemplateRenderer`** (`tools/bench/TemplateRenderer.cs`): Wraps Scriban template engine for netlist generation
- **`ACIRBenchAdapter`** (`tools/acir/ACIRBenchAdapter.cs`): Extracts harness data from ACIR and derives intelligent defaults (AC sweep from constraints, load impedance from harness, etc.)
- **`ACIRTemplateHarness`** (`tools/acir/ACIRTemplateHarness.cs`): Builds the template model object with all variables and nested structures
- **`ComplianceChecker`** (`tools/acir/ComplianceChecker.cs`): Parses constraint values, matches measurements, evaluates operators, generates reports
- **`EmitCommandModule`** (`tools/cli/Commands/EmitCommandModule.cs`): CLI command for netlist generation
- **`VerifyCommandModule`** (`tools/cli/Commands/VerifyCommandModule.cs`): CLI command for constraint verification

Template rendering uses the Scriban library ([Scriban](https://github.com/scriban/scriban)), a .NET-based template engine with Liquid-compatible syntax.  Scriban provides safe sandboxed execution, preventing templates from accessing the filesystem or executing arbitrary code.

---

## 4.12 Future Extensions

Potential enhancements to the template system include:

- **Automated results extraction**: Post-processing scripts to parse raw simulator output and generate JSON results automatically
- **Monte Carlo support**: Template extensions for statistical analysis with multiple runs
- **Corner analysis**: Systematic PVT corner sweeps with aggregated results
- **Batch execution**: Parallel simulation of multiple benches or circuits
- **Custom measurement calculators**: Extensible metric computation beyond basic `.meas` statements
- **Template inheritance**: Shared base templates with bench-specific overrides to reduce duplication

