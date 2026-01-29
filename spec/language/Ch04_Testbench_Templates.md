# Chapter 4: Testbench Templates

> This chapter defines the testbench template system that transforms ACIR circuits into simulator-specific testbenches. Templates enable backend abstraction, allowing the same circuit and bench semantics to target multiple simulators (ngspice, Spectre) while maintaining deterministic, version-controlled test harnesses.

---

## 4.0 Summary

The testbench template system bridges ACIR circuits to simulator execution through a three-part mechanism: bench definitions declare metrics and template references, Scriban-based templates generate simulator netlists from ACIR harness data, and the compliance checker validates simulation results against numeric constraints. This architecture separates circuit design concerns from test harness implementation, enabling backend portability without duplicating bench semantics.

The flow proceeds deterministically: ACIR circuits at EL level contain `harness` blocks specifying supply values, loads, and source impedances, plus document-level `bench` definitions and bench-qualified numeric constraints that select which benches to run. The `cascode emit` command discovers backend-specific templates, populates them with data extracted from ACIR (including constraint-derived AC sweep parameters), and writes simulator netlists. After simulation, `cascode verify` compares measurement results against numeric constraints using SI-prefix-aware value parsing and reports pass/fail status with exit codes suitable for CI integration.

Builtin templates are embedded into the Cascode.Bench assembly at build time. `cascode emit` resolves templates by builtin bench name and backend from these embedded resources, with no filesystem discovery or project overrides. When a scanned PDK workspace is available, emit/bench still populate include lists so templates can pull model decks without extra command-line arguments.

---

## 4.1 Design Principles

The testbench template system establishes clear architectural boundaries. Separation of concerns keeps circuit topology and sizing decisions in ACIR while test stimulus, measurements, and simulator-specific syntax live in templates, preventing cross-contamination between design and verification concerns. Backend abstraction ensures that bench semantics—the metrics measured and their interpretation—remain independent of simulator choice, allowing the same ACIR circuit to target ngspice for quick iteration or Spectre for production sign-off without altering bench declarations. Template-based generation produces deterministic output suitable for version control and CI golden tests, with explicit variable substitution replacing fragile string manipulation. Constraint-driven verification automates pass/fail checking through declarative numeric constraints in ACIR, eliminating manual log parsing and ensuring consistent interpretation of simulation results across teams and tool versions.

---

## 4.2 Bench Definition Files

Bench definitions reside in `.cas` files under `lib/benches` alongside their templates. They declare the bench's identity, available metrics, and backend template filenames. These files are the canonical source of bench metadata; templates are embedded at build time and resolved by builtin name at runtime.

### 4.2.1 Syntax

```cascode
library lib.benches;

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

Backend template references use filenames relative to the bench definition file. The paths follow the naming convention `{BenchName}.{backend}.tpl` where backend is `ngspice` or `spectre`. During build, these files are embedded, and `cascode emit` selects the embedded variant that matches the chosen backend.

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
| `bench_name` | string | Bench name from ACIR bench definition | `"ACBench"` |
| `bench_config` | object | Bench configuration key/value pairs | `{ "points": "100", "sweep": "decade" }` |
| `design_file` | string | Design netlist filename | `"OTA5TSingleEnded.sp"` |
| `port_list` | string | Space-separated port/supply/ground names | `"IN_P IN_N OUT VTAIL VDD GND"` |
| `out_node` | string | Primary output node (first OUT port) | `"OUT"` |
| `generic_models` | boolean | True if circuit uses generic nmos/pmos | `true` |
| `vcm` | double | Common-mode voltage (mid-supply) | `0.9` |
| `bias_v` | double | Input bias voltage (defaults to vcm) | `0.9` |
| `supply_elements` | string | Pre-rendered SPICE netlist for supplies and biases | `"VVDD VDD 0 DC 1.8V\nVVTAIL VTAIL 0 DC 0.6V"` |
| `load_elements` | string | Pre-rendered SPICE netlist for loads | `"COUT_load OUT 0 1pF"` |
| `ac_mag` | double | AC stimulus magnitude | `1.0` |
| `ac_start_hz` | double | AC sweep start frequency (constraint-derived) | `1.0` |
| `ac_stop_hz` | double | AC sweep stop frequency (constraint-derived) | `10e9` |
| `passband_freq_hz` | double | Optimal frequency for passband gain measurement | `100e3` |
| `stb_start_hz` | double | Stability analysis start frequency | `1.0` |
| `stb_stop_hz` | double | Stability analysis stop frequency | `10e9` |
| `includes_with_section` | array | Include files that should be paired with `section` | `["/path/models/sky130.lib.spice"]` |
| `includes_without_section` | array | Include files emitted with `.include` (includes the design file) | `["OTA5TSingleEnded.sp"]` |
| `section` | string or null | Preferred section for `includes_with_section` | `"tt"` |

Templates should iterate over the include lists rather than manually including `design_file`, since the design file is appended to `includes_without_section` and PDK model decks may be present in `includes_with_section` when a workspace database is available.

`bench_name` is the alias used in constraints and results. The builtin bench name selected by the `bench` definition determines which embedded template is used for each backend. `bench_config` is a string-to-string map of bench configuration entries.

The `supply_elements` and `load_elements` variables provide pre-rendered SPICE netlist fragments, which is the recommended approach for most templates. See Section 4.3.4 for details on the structured harness data available for advanced use cases.

### 4.3.4 Harness Data Structures

Recommended Approach: Use the pre-rendered `supply_elements` and `load_elements` strings for most templates. These provide complete, ready-to-use SPICE netlist fragments:

```scriban
* Harness supplies and biases
{{ supply_elements }}

* Output loads
{{ load_elements }}
```

This approach is more elegant and matches the pattern used in all standard library templates.

Advanced Use: For templates requiring custom load splitting or conditional logic, the structured `harness` object is available:

harness.supplies (array of objects):

```scriban
{{ for supply in harness.supplies }}
V{{ supply.net }} {{ supply.net }} 0 DC {{ supply.value }}
{{ end }}
```

- `supply.net`: net name (e.g., `"VDD"`)
- `supply.value`: voltage value (e.g., `"1.8V"`)

harness.loads (array of objects):

```scriban
{{ for load in harness.loads }}
{{ for c in load.cs }}C{{ load.net }}_load {{ load.net }} 0 {{ c }}
{{ end }}{{ for r in load.rs }}R{{ load.net }}_load {{ load.net }} 0 {{ r }}
{{ end }}{{ end }}
```

- `load.net`: net name (e.g., `"OUT"`)
- `load.cs`: array of capacitance values (e.g., `["1pF"]`)
- `load.rs`: array of resistance values (e.g., `["1MOhm"]`)
- `load.cs_half`: array of halved capacitance values for differential load splitting
- `load.rs_half`: array of halved resistance values for differential load splitting

Note that loads support multiple parallel elements, hence the array structure.

### 4.3.5 Backend-Specific Variables (Spectre)

Spectre templates receive additional environment parameters in the `env` object, intelligently derived from ACIR by `ACIRBenchAdapter`:

| Variable | Type | Description | Derivation |
|----------|------|-------------|------------|
| `env.source_ohms` | double | Source impedance (Ω) | From `harness { source IN Z=50ohm }`, default 50Ω |
| `env.cload_f` | double | Load capacitance (F) | From `harness { load OUT C=1pF }` |
| `env.rload_ohms` | double | Load resistance (Ω) | Default 1GΩ (high-Z) |

**AC Sweep Parameters** (derived from constraints):

| Variable | Type | Description | Derivation |
|----------|------|-------------|------------|
| `ac_start_hz` | double | AC sweep start frequency | Constraint-derived: max(1, GBW/1000) |
| `ac_stop_hz` | double | AC sweep stop frequency | Constraint-derived: max(GBW*10, 1G) |
| `ac_mag` | double | AC stimulus magnitude | Default 1.0 |

The AC sweep derivation examines ACIR `constraints { numeric { ... } }` for GainBandwidth, GBW, UnityGainFrequency, or Bandwidth constraints. For example, a constraint `c_gbw = ACBench::GainBandwidth at net::OUT >= 100MHz` yields `ac_start_hz = 100kHz` and `ac_stop_hz = 1GHz`, ensuring the sweep covers the expected circuit behavior without manual tuning.

Passband Frequency Derivation:

The `passband_freq_hz` variable provides the optimal frequency for measuring passband gain, ensuring measurements occur in the flat passband region rather than in rolloff regions. The derivation algorithm proceeds as follows:

1. Determine HP corner (low-frequency bound of passband):
   - If a `HighpassBandwidth` constraint exists, use that value
   - Otherwise assume DC-coupled: use 1 Hz as the effective HP corner

2. Determine LP corner (high-frequency bound of passband):
   - If a `LowpassBandwidth` constraint exists, use that value
   - Otherwise infer from GBW and gain: `f_3dB = GBW / 10^(gain_dB/20)`
   - If only GBW is available, assume typical 40dB gain: `LP = GBW / 100`

3. Compute passband measurement frequency as the geometric mean of the corners:
   ```
   passband_freq_hz = sqrt(HP_corner * LP_corner)
   ```

4. Clamp to AC sweep range: The result is clamped to `[ac_start_hz, ac_stop_hz]`

This intelligent derivation ensures that gain measurements capture the true passband value regardless of circuit topology (DC-coupled vs AC-coupled, lowpass vs bandpass).

DC Bias Sweep Parameters:

| Variable | Type | Description | Example |
|----------|------|-------------|---------|
| `sweep.<ConditionName>` | object or null | Sweep condition if present in harness | `sweep.InputDCCommonMode` |
| `sweep.<ConditionName>.Start` | double | Sweep start value | `0.3` (for 0.3V) |
| `sweep.<ConditionName>.Stop` | double | Sweep stop value | `1.5` (for 1.5V) |
| `sweep.<ConditionName>.Step` | double | Sweep step value | `0.1` (for 100mV) |

Templates should check for the presence of sweep conditions using `{{ if sweep.<ConditionName> }}` and adapt their analysis accordingly. When a sweep is present, benches must execute analyses at each sweep point and report worst-case values.

Templates do not interpret `Auto`. When a design requests `sweep <ConditionName> [Auto]` at earlier elaboration levels, the synthesis/lowering pipeline must resolve it to a concrete numeric sweep in ACIR-EL before template rendering.

Example usage in templates:

```spectre
{{ if sweep.InputDCCommonMode }}
VCM (vcm vss) vsource dc={{ sweep.InputDCCommonMode.Start }}

sweepDC sweep param=VCM.dc start={{ sweep.InputDCCommonMode.Start }} \
    stop={{ sweep.InputDCCommonMode.Stop }} step={{ sweep.InputDCCommonMode.Step }} {
  dcOp dc
  ac ac start={{ ac_start_hz }} stop={{ ac_stop_hz }} dec=100
}
{{ else }}
VCM (vcm vss) vsource dc={{ vcm }}
dcOp dc
ac ac start={{ ac_start_hz }} stop={{ ac_stop_hz }} dec=100
{{ end }}
```

Spectre-Specific Objects:

| Variable | Type | Description |
|----------|------|-------------|
| `spec.temperature_c` | double | Simulation temperature (°C) |

Include lists are provided for all backends; see the common template variables for details.

### 4.3.6 Example: Ngspice Template

```spice
* {{ circuit_name }}_{{ bench_name }} - Generated from ACIR EL
.title {{ circuit_name }}_{{ bench_name }}

{{ if generic_models }}
* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05
{{ end }}

{{ for inc in includes_with_section }}
{{ if section }}.lib "{{ inc }}" {{ section }}{{ else }}.include "{{ inc }}"{{ end }}
{{ end }}
{{ for inc in includes_without_section }}
.include "{{ inc }}"
{{ end }}

* Harness supplies and biases
{{ supply_elements }}

* Differential input: common-mode bias with AC on positive input
VIN_P IN_P 0 DC {{ vcm }} AC 1
VIN_N IN_N 0 DC {{ vcm }}

* Output loads
{{ load_elements }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
op
ac dec 100 {{ ac_start_hz }} {{ ac_stop_hz }}

* Measurements
* Passband gain measured at optimal frequency (computed in C#)
meas ac gain_passband find vdb({{ out_node }}) at={{ passband_freq_hz }}
meas ac gbw when vdb({{ out_node }})=0 cross=1
meas ac pm_raw find vp({{ out_node }}) at=gbw
let pm = 180 + pm_raw

* Per-point report for cascode bench runner
echo CASCODE_POINT point_index=0 PassbandGain_dB=$&gain_passband GainBandwidth_Hz=$&gbw PhaseMargin_deg=$&pm

* Results output
echo "RESULT: PassbandGain = " $&gain_passband " dB"
echo "RESULT: GainBandwidth = " $&gbw " Hz"
echo "RESULT: PhaseMargin = " $&pm " deg"

quit
.endc
.end
```

### 4.3.7 Simulation Trace Output (JSONL)

`cascode bench run` runs the simulator for the benches referenced by numeric constraints in the design and writes artifacts into the job directory. When a bench name is provided, it runs only that bench.

For each executed bench, it writes:

- `{Circuit}_{Bench}_trace.jsonl`: append-only trace capturing per-point sweep data and the final summary.
- `{Circuit}_{Bench}_results.json`: consolidated measurement values intended for constraint verification.

When multiple benches are executed in one run, it also writes `{Circuit}_results.json`, which merges consolidated measurements across benches so that `verify` can evaluate the full constraint set from a single file.

The intended CLI shape is concise:

```bash
cascode bench run <cascode_file> [<bench>] [-o <output_dir>] [-b <bench>] [--backend ngspice]
```

If `<bench>` is omitted, `cascode bench run` executes all benches referenced by numeric constraints in the ACIR document. To run a single bench (for faster iteration and debugging), pass the bench name as either the second positional argument or `-b/--bench`.

Templates must emit two kinds of lines to stdout when running under ngspice:

1) One `CASCODE_POINT` line per executed sweep point. Each line is a flat set of `key=value` tokens. Keys should include `point_index` and may include sweep axes (e.g., `InputDCCommonMode_V=...`) and measured metrics (e.g., `GainBandwidth_Hz=...`). These lines are parsed into per-point records in the JSONL trace.

2) One or more `RESULT:` lines that contain the bench-level spec-compliance values (scalar or vector). Values printed under `RESULT:` must be reduced across the sweep (for example, QuiescentPower must be the worst-case scalar across points), because `verify` evaluates constraints against these consolidated values.

CASCODE_POINT Format Specification:

The `CASCODE_POINT` line must follow this format:

```
CASCODE_POINT point_index=<index> [AxisName_Unit=<value>]* [MetricName_Unit=<value>]*
```

- Required: `point_index=<N>` where N is the zero-based sweep point index
- Optional: Sweep axis values with format `AxisName_Unit=<value>` (e.g., `InputDCCommonMode_V=0.9`)
- Optional: Measured metric values with format `MetricName_Unit=<value>` (e.g., `GainBandwidth_Hz=150e6`)
- All keys use underscore-separated PascalCase with unit suffix
- Values must use ngspice variable expansion syntax `$&variable_name` for numeric variables

Example from SEOpAmpACBench:

```spice
echo CASCODE_POINT point_index=0 PassbandGain_dB=$&gain_passband GainBandwidth_Hz=$&gbw PhaseMargin_deg=$&pm
```

Example with sweep axis from SEOpAmpDCBench:

```spice
echo CASCODE_POINT point_index=$&point_index InputDCCommonMode_V=$&cm_val OutputDCBias_V=$&out_dc QuiescentPower_W=$&pwr_total
```

The JSONL file is a sequence of independent JSON objects with a stable envelope:

| Record `type` | Purpose | Required fields |
|--------------|---------|-----------------|
| `meta` | Run context | `schema`, `version`, `type`, `run_id`, `ts_utc`, `circuit`, `bench`, `backend` |
| `axes` | Declared sweep axes | `schema`, `version`, `type`, `run_id`, `ts_utc`, `axes[]` |
| `point` | One executed point | `schema`, `version`, `type`, `run_id`, `ts_utc`, `point.index`, `point.axis_values`, `measurements[]` |
| `summary` | Consolidated outputs | `schema`, `version`, `type`, `run_id`, `ts_utc`, `results` |

The `summary.results` object is the canonical bridge to `verify`; it matches the `BenchResult` JSON shape used by `verify --results`.

### 4.3.8 Example: Spectre Template Fragment

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

## 4.4 Builtin Template Resolution

Bench templates are embedded resources packaged with Cascode.Bench. Resolution is name- and backend-based: a bench definition that declares `builtin SEOpAmpACBench` and a `ngspice` backend resolves `SEOpAmpACBench.ngspice.tpl` from the embedded library. If the embedded resource is missing, `cascode emit` fails with an error that lists the available builtin benches. ACIR presently does not support filesystem discovery or project-local overrides.

Backend selection follows filename suffixes (`.ngspice.tpl` or `.spectre.tpl`) so a single bench definition can target multiple simulators while preserving consistent metrics.

---

## 4.5 ACIR Integration

The testbench system integrates with ACIR through three primary blocks: document-level `bench` definitions, `constraints`, and `harness`.

### 4.5.1 Harness Block

The `harness` block specifies test-only elements that do not appear in the synthesized design:

```acir
harness {
  supply VDD = 1.8V
  bias VTAIL = 0.6V
  load OUT C=1pF
  source IN Z=50ohm
  icmr min=0.55V max=0.75V
  pvt TT@27C
}
```

Note the compact notation for values: no space between numeric value and unit (e.g., `1pF` not `1p F`, `50ohm` not `50 ohm`).

Template variables derived from harness entries:
- `supply_elements`: pre-rendered SPICE netlist for supplies and biases (recommended)
- `load_elements`: pre-rendered SPICE netlist for loads (recommended)
- `harness.supplies`: list of supply/bias declarations (advanced use)
- `harness.loads`: list of load capacitances (advanced use)
- `env.source_ohms`: extracted from source impedance declarations
- `env.cload_f`: extracted from first load capacitance
- `env.rload_ohms`: defaults to 1GΩ unless specified

### 4.5.2 Constraints Block

The `constraints` block defines pass/fail criteria tied to specific benches:

```acir
constraints {
  numeric {
    c_gbw = ACBench::GainBandwidth at net::OUT >= 100MHz
    c_gain = ACBench::PassbandGain at net::OUT >= 40dB
    c_pm = ACBench::PhaseMargin at net::OUT >= 60deg
    c_pwr = DCBench::QuiescentPower <= 500uW
  }
  tech {
    t_lmin : L >= 180nm on *
  }
}
```

**Numeric constraints** drive both AC sweep parameter derivation and post-simulation compliance checking. The `ACIRBenchAdapter` examines GainBandwidth constraints to set appropriate `ac_start_hz` and `ac_stop_hz` values, ensuring the frequency sweep captures the circuit's expected bandwidth.

### 4.5.3 Bench Definitions

Bench definitions live at document scope and bind a bench name to either a builtin bench definition or an explicit template. The `outputs` list declares which metrics the bench will emit for circuits that implement the specified interface.

```acir
bench ACBench for SingleEndedOpAmp {
  builtin SEOpAmpACBench
  outputs {
    GainBandwidth
    PassbandGain
    PhaseMargin
  }
}
```

During `cascode emit`, each bench referenced by numeric constraints triggers builtin template resolution and netlist generation.

---

## 4.6 CLI Workflow

### 4.6.1 Emit Command

Generate simulator netlists from an ACIR circuit:

```bash
cascode emit <cascode_file> --out <output_dir> --backend {ngspice|spectre}
```

**Arguments:**
- `<cascode_file>`: Path to Cascode file (must be EL-level)
- `--out <output_dir>`: Output directory for generated files
- `--backend {ngspice|spectre}`: Target simulator backend

**Generated Artifacts:**

For a Cascode file `OTA5TSingleEnded.el.cir` with bench `ACBench`:

```bash
<output_dir>/
  OTA5TSingleEnded.sp                    # Design subcircuit
  OTA5TSingleEnded_ACBench.sp            # Ngspice testbench
  spec.json                               # Testbench metadata
```

Or with `--backend spectre`:

```bash
<output_dir>/
  OTA5TSingleEnded.sp                    # Design subcircuit
  OTA5TSingleEnded_ACBench.scs           # Spectre testbench
  spec.json                               # Testbench metadata
```

**Design File Emission:** The design subcircuit (`.sp`) always uses SPICE syntax regardless of backend, as both ngspice and Spectre can include SPICE subcircuits. The testbench file uses backend-specific syntax (`.sp` for ngspice, `.scs` for Spectre).

### 4.6.2 Verify Command

Check simulation results against ACIR constraints:

```bash
cascode verify <cascode_file> <results_json|trace_jsonl>
```

`trace_jsonl` is the output produced by `cascode bench run`. When a trace is supplied, `verify` reads the `summary` record and evaluates constraints against the consolidated measurement values.

**Results JSON Schema:**

```json
{
  "circuit": "OTA5TSingleEnded",
  "bench": "ACBench",
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
c_gbw    ACBench::GainBandwidth at net::OUT >= 100MHz     PASS (measured: 150MHz)
c_gain   ACBench::PassbandGain at net::OUT >= 40dB       PASS (measured: 45.2dB)
c_pm     ACBench::PhaseMargin at net::OUT >= 60deg       PASS (measured: 65.3deg)
c_pwr    DCBench::QuiescentPower <= 500uW                PASS (measured: 350uW)
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
| `>=` | Greater than or equal | `ACBench::GainBandwidth at net::OUT >= 100MHz` |
| `<=` | Less than or equal | `DCBench::QuiescentPower <= 500uW` |
| `==` | Equal (with 1e-9 tolerance) | `ACBench::PassbandGain at net::OUT == 40dB` |
| `>` | Strictly greater than | `ACBench::PhaseMargin at net::OUT > 45deg` |
| `<` | Strictly less than | `StepToggle::RiseTime at net::OUT < 10ns` |

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

Constraints specify which bench metric to check and optionally which node:

```acir
c_gain = ACBench::PassbandGain at net::OUT >= 40dB
```

The compliance checker matches this constraint to a measurement result by:
1. Case-insensitive bench and metric name comparison (`ACBench::PassbandGain` matches `acbench::passbandgain`)
2. Node name matching if specified (`at net::OUT` requires result to have `"node": "OUT"`)
3. If multiple measurements have the same metric but different nodes, the node selector disambiguates

### 4.7.4 Missing Measurements

If a constraint references a metric not present in the results JSON, the checker reports:

```text
c_gain   ACBench::PassbandGain at net::OUT >= 40dB        FAIL (not measured)
```

This situation indicates either:
- The bench template does not measure this metric
- The simulation failed to produce results
- A mismatch between constraint metric names and bench definition

---

## 4.8 Standard Library Benches

The standard library at `lib/benches/` provides canonical bench definitions for common analog circuit tests. These templates are embedded at build time and are the only benches currently available:

| Circuit Type | Bench | Analysis Type | Spectre | ngspice |
|-------------|-------|--------------|---------|---------|
| Single-Ended Amplifier | `SEAmpACBench` | AC | ✓ | ✓ |
| Single-Ended Amplifier | `SEAmpDCBench` | DC sweep | ✓ | ✓ |
| Single-Ended Op Amp | `SEOpAmpACBench` | AC | ✓ | ✓ |
| Single-Ended Op Amp | `SEOpAmpDCBench` | DC sweep | ✓ | ✓ |
| Single-Ended Op Amp | `SEOpAmpStability` | Stability | ? | ? |
| Single-Ended Op Amp | `SEOpAmpSettle` | Transient settling | ? | ? |
| Single-Ended Op Amp | `SEOpAmpSlew` | Transient slew | ? | ? |
| Fully Differential Op Amp | `FDOpAmpACBench` | AC | ✓ | ✓ |
| Fully Differential Op Amp | `FDOpAmpDCBench` | DC sweep | ✓ | ✓ |
| Fully Differential Op Amp | `FDOpAmpStability` | Stability | ? | |

The specific metrics, circuit requirements, and harness configurations for key benches are documented in the subsections below.

### 4.8.1 SEOpAmpACBench

Purpose: AC analysis for single-ended output operational amplifiers with differential inputs.

Metrics:
- `GainBandwidth` (Hz): Frequency where gain crosses 0dB
- `PassbandGain` (dB): Low-frequency gain magnitude
- `PhaseMargin` (deg): Phase margin at unity-gain frequency
- `LowpassBandwidth` (Hz): -3dB bandwidth for lowpass response
- `HighpassBandwidth` (Hz): -3dB bandwidth for highpass response
- `BandpassBandwidth` (Hz): -3dB bandwidth for bandpass response

Circuit Requirements:
- Differential inputs (`IN_P`, `IN_N`)
- Single-ended output (`OUT`)
- Power supplies and grounds as declared in ACIR

Harness Configuration:
The ngspice template applies a common-mode bias at both inputs and superimposes AC stimulus on `IN_P`, creating a differential AC signal. The Spectre template uses an ideal balun to generate differential drive from a single AC source.

### 4.8.2 SEAmpACBench

Purpose: AC analysis for single-ended amplifiers with single input and single output.

Metrics:
- `GainBandwidth` (Hz): Unity-gain frequency
- `PassbandGain` (dB): Low-frequency gain magnitude

Circuit Requirements:
- Single input (`IN`)
- Single-ended output (`OUT`)
- Power supplies and grounds as declared in ACIR

Harness Configuration:
Input receives DC bias (mid-supply by default) with AC stimulus. Simpler than `SEOpAmpACBench` as it requires no differential drive or balun structures.

### 4.8.3 SEOpAmpDCBench

Purpose: DC characterization for single-ended output operational amplifiers with differential inputs, measuring output DC bias and quiescent power across the input common-mode range (ICMR).

Metrics:
- `InputDCCommonMode` (V): ICMR sweep condition (echoed for traceability)
- `OutputDCBias` (V): Output DC level at each ICMR point
- `OutputDCBias_min` (V): Minimum output bias across ICMR sweep
- `OutputDCBias_max` (V): Maximum output bias across ICMR sweep
- `QuiescentPower` (W): Maximum static power consumption across ICMR sweep

Circuit Requirements:
- Differential inputs (`IN_P`, `IN_N`)
- Single-ended output (`OUT`)
- Power supplies and grounds as declared in ACIR

Harness Configuration:
Applies common-mode voltage to both inputs while sweeping across the ICMR range specified in the harness. Measures DC operating points and supply current at each sweep point. When no sweep is specified, performs single-point DC analysis at mid-supply.

Sweep Support:
This bench respects `sweep InputDCCommonMode [start:step:stop]` in the harness. When present, executes DC analysis at each ICMR point and reports worst-case values (max power, output bias range).

### 4.8.4 SEAmpDCBench

Purpose: DC characterization for single-ended amplifiers (single input, single output), measuring output DC bias and quiescent power across the input bias range.

Metrics:
- `InputDCBias` (V): Input bias sweep condition (echoed for traceability)
- `OutputDCBias` (V): Output DC level at each input bias point
- `OutputDCBias_min` (V): Minimum output bias across input bias sweep
- `OutputDCBias_max` (V): Maximum output bias across input bias sweep
- `QuiescentPower` (W): Maximum static power consumption across input bias sweep

Circuit Requirements:
- Single input (`IN`)
- Single-ended output (`OUT`)
- Power supplies and grounds as declared in ACIR

Harness Configuration:
Sweeps the input DC bias voltage across the specified range. Measures DC operating points and supply current at each sweep point. Simpler than `SEOpAmpDCBench` as it requires no differential input structure.

Sweep Support:
This bench respects `sweep InputDCBias [start:step:stop]` in the harness. When present, executes DC analysis at each bias point and reports worst-case values (max power, output bias range).

### 4.8.5 FDOpAmpDCBench

Purpose: DC characterization for fully differential operational amplifiers, measuring output common-mode and quiescent power across the input common-mode range (ICMR).

Metrics:
- `InputDCCommonMode` (V): ICMR sweep condition (echoed for traceability)
- `OutputDCCommonMode` (V): Output common-mode DC level at each ICMR point
- `OutputDCCommonMode_min` (V): Minimum output common-mode across ICMR sweep
- `OutputDCCommonMode_max` (V): Maximum output common-mode across ICMR sweep
- `QuiescentPower` (W): Maximum static power consumption across ICMR sweep

Circuit Requirements:
- Differential inputs (`IN_P`, `IN_N`)
- Differential outputs (`OUT_P`, `OUT_N`)
- Power supplies and grounds as declared in ACIR

Harness Configuration:
Applies common-mode voltage to both inputs while sweeping across the ICMR range specified in the harness. Measures DC operating points and supply current at each sweep point. When no sweep is specified, performs single-point DC analysis at mid-supply.

Sweep Support:
This bench respects `sweep InputDCCommonMode [start:step:stop]` in the harness. When present, executes DC analysis at each ICMR point and reports worst-case values (max power, output common-mode range).

---

## 4.9 Extending the Builtin Bench Library

ACIR presently resolves only builtin benches. To add a new builtin bench, place the definition and templates under `lib/benches` and rebuild Cascode so the templates are embedded into the assembly.

### 4.9.1 Bench Definition

Create `{BenchName}.cas` in `lib/benches`:

```cascode
library lib.benches;

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

{{ for inc in includes_with_section }}
{{ if section }}.lib "{{ inc }}" {{ section }}{{ else }}.include "{{ inc }}"{{ end }}
{{ end }}
{{ for inc in includes_without_section }}
.include "{{ inc }}"
{{ end }}

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

In your ACIR document, add a bench definition and reference it from constraints:

```acir
bench MyCustomBench for SingleEndedOpAmp {
  builtin MyCustomBench
  outputs {
    Metric1
    Metric2
  }
}

constraints {
  numeric {
    c_metric1 = MyCustomBench::Metric1 at net::OUT >= 1.0V
  }
}
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
ACIR 3.0

primitive nmos Level1_NMOS(size primSize) {
  device "level1_nmos"
  params {
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }
}

primitive pmos Level1_PMOS(size primSize) {
  device "level1_pmos"
  params {
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }
}

bench ACBench for SingleEndedOpAmp {
  builtin SEOpAmpACBench
  outputs {
    GainBandwidth
    PassbandGain
    PhaseMargin
  }
}

bench DCBench for SingleEndedOpAmp {
  builtin SEOpAmpDCBench
  outputs {
    QuiescentPower
  }
}

circuit OTA5TSingleEnded implements SingleEndedOpAmp {
  level EL
  supply VDD
  ground GND
  input IN_P : analog
  input IN_N : analog
  output OUT : analog
  input VTAIL : bias

  fill {
    net mirror_gate : analog
    net tnode : analog
    nmos dp.M_N = new Level1_NMOS(size(W=2u, L=180n, M=1)) {
      .G--IN_P
      .D--mirror_gate
      .S--tnode
      .B--GND
    }
    nmos dp.M_P = new Level1_NMOS(size(W=2u, L=180n, M=1)) {
      .G--IN_N
      .D--OUT
      .S--tnode
      .B--GND
    }
    nmos dp.M_TAIL = new Level1_NMOS(size(W=4u, L=180n, M=1)) {
      .G--VTAIL
      .D--tnode
      .S--GND
      .B--GND
    }
    pmos cm.M_SENSE = new Level1_PMOS(size(W=2u, L=180n, M=1)) {
      .G--mirror_gate
      .D--mirror_gate
      .S--VDD
      .B--VDD
    }
    pmos cm.M_TAP0 = new Level1_PMOS(size(W=2u, L=180n, M=1)) {
      .G--mirror_gate
      .D--OUT
      .S--VDD
      .B--VDD
    }
  }

  constraints {
    numeric {
      c_gbw = ACBench::GainBandwidth at net::OUT >= 100MHz
      c_gain = ACBench::PassbandGain at net::OUT >= 40dB
      c_pm = ACBench::PhaseMargin at net::OUT >= 60deg
      c_pwr = DCBench::QuiescentPower <= 500uW
    }
    tech {
      t_lmin : L >= 180nm on *
    }
  }

  harness {
    supply VDD = 1.8V
    bias VTAIL = 0.6V
    load OUT C=1pF
  }
}
```

### 4.10.2 Emit Command

```bash
$ cascode emit tests/golden/acir/ota/OTA5TSingleEnded.el.cir \
    --out /tmp/ota-test --backend ngspice

Design netlist: /tmp/ota-test/OTA5TSingleEnded.sp
Testbench: /tmp/ota-test/OTA5TSingleEnded_ACBench.sp
Emitted 1 design(s) and 1 testbench(es).
```

### 4.10.3 Generated Testbench (excerpt)

```spice
* OTA5TSingleEnded_ACBench - Generated from ACIR EL
.title OTA5TSingleEnded_ACBench

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
  "bench": "ACBench",
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
    "quiescent_power": {
      "metric": "QuiescentPower",
      "value": 0.00035,
      "unit": "W",
      "node": null
    }
  }
}
```

### 4.10.5 Verify Command

```bash
$ cascode verify tests/golden/acir/ota/OTA5TSingleEnded.el.cir /tmp/ota-test/OTA5TSingleEnded_ACBench_results.json

Constraint Compliance Report for OTA5TSingleEnded
--------------------------------------------------
c_gbw    ACBench::GainBandwidth at net::OUT >= 100MHz     PASS (measured: 150MHz)
c_gain   ACBench::PassbandGain at net::OUT >= 40dB       PASS (measured: 45.2dB)
c_pm     ACBench::PhaseMargin at net::OUT >= 60deg       PASS (measured: 65.3deg)
c_pwr    DCBench::QuiescentPower <= 500uW                PASS (measured: 350uW)
--------------------------------------------------
Result: 4/4 constraints satisfied
```

**Exit code:** 0 (success)

---

## 4.11 Implementation Notes

The testbench template system comprises several C# components in the Cascode toolchain:

- **`BenchTemplateLibrary`** (`tools/bench/BenchTemplateLibrary.cs`): Loads embedded builtin templates and provides lookup by bench name and backend
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

- **Richer bench execution support**: Extend `cascode bench run` across more benches/backends and capture additional intermediate artifacts for debugging
- **Monte Carlo support**: Template extensions for statistical analysis with multiple runs
- **Corner analysis**: Systematic PVT corner sweeps with aggregated results
- **Batch execution**: Parallel simulation of multiple benches or circuits
- **Custom measurement calculators**: Extensible metric computation beyond basic `.meas` statements
- **Template inheritance**: Shared base templates with bench-specific overrides to reduce duplication
