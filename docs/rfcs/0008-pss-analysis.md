# RFC-0008: Periodic Steady-State (PSS) Analysis

Status: Draft
Authors: Claude (proposed), Titan Yuan (review)
Created: 2026-03-04
Target Version: Cascode 4.x

---

## Abstract

This RFC proposes periodic steady-state (PSS) analysis within Cascode's bench system. The design introduces `PSSAnalysis` as a new analysis type that integrates with the existing `voltage()`, `current()`, and waveform infrastructure — the same accessors used for `TranAnalysis`. New built-ins `duration`, `harmonic_power`, and `thd` support waveform time-span extraction, per-harmonic power computation, and distortion analysis from periodic waveforms.

PSS analysis finds the periodic steady-state response of a nonlinear circuit driven at (or near) a specified frequency. The solver produces one period of the steady-state waveform, from which bench measurements can extract the solved oscillation frequency, harmonic voltages/currents, and harmonic power. This enables characterization of power amplifiers, oscillators, mixers, and other large-signal periodic circuits.

PSS benches follow the `TranBenches` harness pattern: explicit `VSIN` sources and `Impedor` terminations in the fill block, with no `Port` primitives. The oscillating node is always the output terminal, resolved at emission time.

---

## 1. PSSAnalysis

### 1.1 Declaration

`PSSAnalysis` is declared in `analysis {}` like other analysis types:

```cascode
analysis {
  PSSAnalysis pss = new PSSAnalysis(
    fguess=1GHz,
    tstab=10ns,
    harmonics=10)
}
```

### 1.2 Parameters

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fguess` | `Frequency` | yes | — | Initial frequency estimate for the PSS solver |
| `tstab` | `Time` | yes | — | Stabilization time before shooting iterations begin |
| `harmonics` | integer | yes | — | Number of harmonics to resolve in the output |

Arguments may use expressions over `constraints`, `env`, and bench parameters, consistent with other analysis types.

### 1.3 Oscillating Node

The oscillating node required by the simulator's PSS command is the `resp OUT` terminal of the bench, resolved to a netlist node at emission time. There is no explicit parameter for specifying it; the runtime discovers the output terminal from the bench definition.

For single-ended outputs, the oscillating node is the net connected to `OUT`. For differential outputs, it is the net connected to `OUT.P`.

### 1.4 Simulation Semantics

The PSS solver performs a transient stabilization for `tstab` seconds, then applies a shooting method to converge on the periodic steady-state waveform at (or near) `fguess`. The result is a time-domain waveform spanning exactly one solved period, available for all circuit node voltages.

For driven circuits (where a `VSIN` source provides the excitation), the solver converges to the exact drive frequency. For autonomous oscillators (no external drive), the solver finds the natural oscillation frequency, which may differ from `fguess`.

### 1.5 Harness Topology

PSS benches use standard fill-block primitives rather than `Port` instances. The harness follows the `TranBenches` pattern:

- Input stimulus (driven benches): `VSIN` source through `Impedor` source impedance, biased at `env.InputCommonModeRange`.
- Output termination: `Impedor` load impedance. Driven benches bias the load at `env.OutputCommonModeRange`; oscillator benches leave the DC operating point to the DUT.
- The stimulus amplitude is resolved through `get_input_amplitude(25mV)`, which checks `env.InputPower` first (deriving amplitude from available power and source impedance), then `env.InputAmplitude`, then the fallback. `TranBenches` is updated to use the same helper, retiring the hardcoded amplitude and the unused `env.TranInputAmplitude` reference.

This separation keeps `PSSAnalysis` purely about solver parameters while the fill block owns the stimulus topology.

### 1.6 Input Amplitude Resolution

The `get_input_amplitude` helper resolves the VSIN drive amplitude through a priority chain:

```cascode
function get_input_amplitude(Voltage fallback) : Voltage {
  if env.InputPower { return sqrt(8 * env.SourceImpedance * env.InputPower) }
  if env.InputAmplitude { return env.InputAmplitude }
  return fallback
}
```

When `env.InputPower` is set, the peak amplitude is derived from the maximum available power of a sinusoidal source into a matched load: $A = \sqrt{8\,R_s\,P_{avail}}$, where $R_s$ is `env.SourceImpedance`. This is the standard RF convention — the interface specifies power (e.g. `-10dBm`) and the harness computes the voltage for the given source impedance. Changing `SourceImpedance` automatically adjusts the drive voltage to maintain the same available power.

When `env.InputPower` is not set, the helper falls back to `env.InputAmplitude` (an explicit peak voltage), then to the hardcoded fallback (25 mV for both PSS and transient benches).

Note that available power (what the source can deliver into a matched load) differs from delivered power (what actually enters the DUT). The `InputPower` measurement computes delivered power from the terminal voltage waveform, which accounts for impedance mismatch.

---

## 2. Waveform and Time Accessors

`PSSAnalysis` reuses the same `voltage()` and `current()` accessors as `TranAnalysis`, with the same current-source constraints as transient benches. The PSS waveform covers exactly one solved period, so the resulting `VoltageWaveform` / `CurrentWaveform` objects contain one cycle of the steady-state response.

### 2.1 Voltage and Current

```
voltage(PSSAnalysis, terminal) → VoltageWaveform
current(PSSAnalysis, element_pin) → CurrentWaveform
```

These behave identically to their transient counterparts. `voltage()` takes a terminal reference (node voltage). `current()` uses the same extraction model as transient benches: branch currents are available for harness voltage-source elements written via `i(V...)` vectors (for example `harness.VDD.P`), not as a generic simulator-side element-pin probe for arbitrary devices.

For differential terminals, `voltage(pss, OUT)` produces the differential waveform `V(OUT.P) - V(OUT.N)`, consistent with the existing differential terminal semantics (Section 4.2.5 of the spec).

### 2.2 Duration

A `duration` built-in returns the time span of a waveform:

```
duration(VoltageWaveform) → Time
duration(CurrentWaveform) → Time
```

This computes $t_{end} - t_{start}$ from the waveform's time vector. For PSS waveforms, the duration is exactly one solved period. The solved frequency is its reciprocal: $f = 1 / \text{duration}(w)$. This built-in is general-purpose and applies to any waveform, not just PSS.

### 2.3 Harmonic Power

A new `harmonic_power` built-in computes real power at a specific harmonic from a periodic voltage waveform and a known impedance:

```
harmonic_power(VoltageWaveform, Impedance) → Power (W)
harmonic_power(VoltageWaveform, Impedance, Int k) → Power (W)
```

The two-argument form defaults to the fundamental ($k = 1$). The fundamental frequency is inferred from the waveform's time span ($f = 1 / (t_{end} - t_{start})$), which is exact for PSS waveforms since they cover precisely one solved period. The built-in extracts the $k$-th harmonic peak phasor $V_k$, then computes:

$$P_k = \frac{|V_k|^2}{2\,R}$$

where $R$ is the resistive component of the impedance. This built-in is not PSS-specific — it operates on any `VoltageWaveform`, including those from `TranAnalysis`, though its primary use case is periodic waveforms where the DFT is well-defined.

### 2.4 THD

A `thd` built-in computes total harmonic distortion from a periodic voltage waveform:

```
thd(VoltageWaveform, Int harmonics) → Scalar
```

The fundamental frequency is inferred from the waveform's time span, same as `harmonic_power`. The `harmonics` argument controls how many harmonics above the fundamental to include. The result is the voltage-domain THD:

$$\text{THD} = \frac{\sqrt{\sum_{k=2}^{N} |V_k|^2}}{|V_1|}$$

where $V_k$ is the $k$-th harmonic phasor and $N$ is `harmonics`. The impedance cancels from the ratio, so no impedance argument is needed. Like `harmonic_power`, this built-in works on any `VoltageWaveform` but is primarily intended for periodic waveforms.

### 2.5 Input Power via Voltage and Current

For input power, the measurement can use `voltage(pss, IN)` for the terminal voltage and `current(pss, sourceZ.P)` for the branch current through the source impedance element, computing $P_{in,k} = \tfrac{1}{2}\,\text{Re}\{V_k\,I_k^*\}$ at each harmonic. Alternatively, when the source impedance and drive amplitude are known, input power can be derived from the terminal voltage waveform and impedance alone via `harmonic_power` (see Section 5.3).

---

## 3. Bench Hierarchy

PSS benches are organized in an abstract hierarchy that separates measurement definitions from harness topology.

### 3.1 AbstractOutputPSS

The base abstract bench declares the output terminal, the analysis, and output-side measurements. It is parameterized by `guess_freq`, analogous to `stim_freq` in `AbstractTran`.

```cascode
abstract bench AbstractOutputPSS(Frequency guess_freq = 1GHz) {
  abstract resp OUT

  analysis {
    PSSAnalysis pss = new PSSAnalysis(
      fguess=guess_freq,
      tstab=10ns,
      harmonics=10)
  }

  measurements {
    measurement HarmonicFrequency : Hz {
      VoltageWaveform vout = voltage(pss, OUT)
      return 1 / duration(vout)
    }
    measurement HarmonicPeriod : s {
      VoltageWaveform vout = voltage(pss, OUT)
      return duration(vout)
    }
    measurement OutputPower : W {
      VoltageWaveform vout = voltage(pss, OUT)
      return harmonic_power(vout, env.LoadImpedance)
    }
    measurement OutputPowerHarmonic(Int k) : W {
      VoltageWaveform vout = voltage(pss, OUT)
      return harmonic_power(vout, env.LoadImpedance, k)
    }
  }
}
```

### 3.2 AbstractInputOutputPSS

Extends `AbstractOutputPSS` with an input terminal and input power measurements.

```cascode
abstract bench AbstractInputOutputPSS extends AbstractOutputPSS {
  abstract stim IN
  abstract resp OUT

  measurements {
    measurement InputPower : W {
      VoltageWaveform vin = voltage(pss, IN)
      return harmonic_power(vin, env.SourceImpedance)
    }
    measurement InputPowerHarmonic(Int k) : W {
      VoltageWaveform vin = voltage(pss, IN)
      return harmonic_power(vin, env.SourceImpedance, k)
    }
    measurement Gain : dB {
      VoltageWaveform vout = voltage(pss, OUT)
      VoltageWaveform vin = voltage(pss, IN)
      Power pout = harmonic_power(vout, env.LoadImpedance)
      Power pin = harmonic_power(vin, env.SourceImpedance)
      return 10 * log10(pout / pin)
    }
    measurement THD(Int harmonics) : Scalar {
      VoltageWaveform vout = voltage(pss, OUT)
      return thd(vout, harmonics)
    }
    measurement DrainEfficiency(Power dcPower) : Scalar {
      VoltageWaveform vout = voltage(pss, OUT)
      Power pout = harmonic_power(vout, env.LoadImpedance)
      return pout / dcPower
    }
    measurement PAE(Power dcPower) : Scalar {
      VoltageWaveform vout = voltage(pss, OUT)
      VoltageWaveform vin = voltage(pss, IN)
      Power pout = harmonic_power(vout, env.LoadImpedance)
      Power pin = harmonic_power(vin, env.SourceImpedance)
      return (pout - pin) / dcPower
    }
  }
}
```

### 3.3 Concrete Benches

Four concrete benches cover the common oscillator and driven topologies.

#### 3.3.1 SEOscPSS — Single-Ended Autonomous Oscillator

Provides a resistive load on the output. The DUT sets its own DC operating point.

```cascode
bench SEOscPSS extends AbstractOutputPSS {
  resp OUT : analog

  fill {
    net gnd : ground
    GND _ = new GND() { .GND--gnd }

    Impedor loadZ = new Impedor(Z=env.LoadImpedance) {
      .P--OUT
      .N--gnd
    }
  }
}
```

#### 3.3.2 DiffOscPSS — Differential Autonomous Oscillator

Split shunt loads on each leg of the differential output.

```cascode
bench DiffOscPSS extends AbstractOutputPSS {
  resp OUT : Diff

  fill {
    net gnd : ground
    GND _ = new GND() { .GND--gnd }

    Impedor loadP = new Impedor(Z=env.LoadImpedance.DiffToShunt()) {
      .P--OUT.P
      .N--gnd
    }
    Impedor loadN = new Impedor(Z=env.LoadImpedance.DiffToShunt()) {
      .P--OUT.N
      .N--gnd
    }
  }
}
```

#### 3.3.3 SEToSEPSS — Single-Ended Driven

Large-signal sinusoidal drive through source impedance, with input and output common-mode biasing.

```cascode
bench SEToSEPSS extends AbstractInputOutputPSS {
  stim IN : analog
  resp OUT : analog

  fill {
    net vcm_in : analog
    net gnd : ground

    GND _ = new GND() { .GND--gnd }

    VDC inputCommonModeDC = new VDC(V=env.InputCommonModeRange) {
      .P--vcm_in
      .N--gnd
    }

    VSIN vin = new VSIN(A=get_input_amplitude(25mV), freq=guess_freq, phase=0deg) {
      .N--vcm_in
    }

    Impedor sourceZ = new Impedor(Z=env.SourceImpedance) { }
    vin.P--sourceZ.P
    sourceZ.N--IN

    Impedor loadZ = new Impedor(Z=env.LoadImpedance) {
      .P--OUT
      .N--gnd
    }
  }
}
```

#### 3.3.4 DiffToDiffPSS — Differential Driven

Anti-phase `VSIN` pair through split source impedances, with differential load termination.

```cascode
bench DiffToDiffPSS extends AbstractInputOutputPSS {
  stim IN : Diff
  resp OUT : Diff

  fill {
    net vcm_in : analog
    net vcm_out : analog
    net gnd : ground

    GND _ = new GND() { .GND--gnd }

    VDC inputCommonModeVDC = new VDC(V=env.InputCommonModeRange) {
      .P--vcm_in
      .N--gnd
    }

    VDC outputCommonModeVDC = new VDC(V=env.OutputCommonModeRange) {
      .P--vcm_out
      .N--gnd
    }

    VSIN inP = new VSIN(A=get_input_amplitude(25mV), freq=guess_freq, phase=0deg) {
      .N--vcm_in
    }
    VSIN inN = new VSIN(A=get_input_amplitude(25mV), freq=guess_freq, phase=180deg) {
      .N--vcm_in
    }

    Impedor sourceP = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }
    Impedor sourceN = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }

    inP.P--sourceP.P
    sourceP.N--IN.P

    inN.P--sourceN.P
    sourceN.N--IN.N

    Impedor loadP = new Impedor(Z=env.LoadImpedance.DiffToShunt()) {
      .P--OUT.P
      .N--vcm_out
    }
    Impedor loadN = new Impedor(Z=env.LoadImpedance.DiffToShunt()) {
      .P--OUT.N
      .N--vcm_out
    }
  }
}
```

---

## 4. Interface Binding

PSS benches are bound like any other bench. Bench terminals are mapped to DUT terminals; the fill block's sources and impedances sit on those terminal nets.

```cascode
interface SingleEndedPA {
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  env {
    InputPower = 0dBm
    InputCommonModeRange = 0V
    SourceImpedance = 50Ohm
    LoadImpedance = 50Ohm
  }

  benches {
    bind QuiescentPower as vdd_pwr {
      bench.PWR--dut.VDD
      bench.RET--dut.GND

      GND g = new GND() { .GND--gnd }
      Impedor sourceZ = new Impedor(Z=env.SourceImpedance) { .P--gnd, .N--dut.IN }
    }

    bind SEToSEPSS as pss_bench {
      bench.IN--dut.IN
      bench.OUT--dut.OUT

      measurements {
        measurement DrainEfficiency : Scalar = base::DrainEfficiency(dcPower=vdd_pwr::QuiescentPower)
        measurement PAE : Scalar = base::PAE(dcPower=vdd_pwr::QuiescentPower)
      }
    }
  }
}
```

The `DrainEfficiency` and `PAE` measurements on `AbstractInputOutputPSS` take a `dcPower` parameter. The binding exports no-arg versions that wire in the DC supply power from the `QuiescentPower` bench, following the same cross-bench forwarding pattern used for CMRR and PSRR (see `FullyDifferentialOpAmp`).

Constraints reference measurements through the bind name:

```cascode
constraints {
  numeric {
    c_osc_freq = pss_bench(guess_freq=2.4GHz)::HarmonicFrequency >= 2.3GHz
    c_output_pwr = pss_bench(guess_freq=2.4GHz)::OutputPower >= 10mW
    c_pae = pss_bench(guess_freq=2.4GHz)::PAE >= 0.3
  }
}
```

---

## 5. Power Computation Details

### 5.1 Data Extraction

After the PSS solver converges, the emitter saves one-period time-domain vectors from the `pss1` plot using `wrdata`: node voltages for relevant terminals, and source branch currents using the same `i(V...)` extraction pattern as transient benches. The runner reads these vectors and applies a discrete Fourier transform to obtain complex harmonic phasors $V_k$ for each terminal.

### 5.2 Output Power

For a resistive load impedance $R_{load}$, output power at harmonic $k$ is:

$$P_{out,k} = \frac{|V_{OUT,k}|^2}{2\,R_{load}}$$

The current implementation of `OutputPower` does not require simulator branch-current vectors.

### 5.3 Input Power

The VSIN source is a known pure sinusoid. Its harmonic decomposition is trivial: $V_{src,1}$ is the declared amplitude (peak) and $V_{src,k} = 0$ for $k > 1$. The current into the DUT at each harmonic is derived from Ohm's law across the source impedance:

$$I_{in,k} = \frac{V_{src,k} - V_{IN,k}}{Z_{source}}$$

The real power delivered to the DUT is:

$$P_{in,k} = \tfrac{1}{2}\,\text{Re}\{V_{IN,k}\,I_{in,k}^*\}$$

This requires only `v(in_node)` from the PSS waveform combined with the source parameters already present in the bench plan.

### 5.4 Differential Power

For differential terminals, the voltage used in power calculations is the differential quantity `V(P) - V(N)`. The impedance used is the full differential impedance (not the per-leg shunt value). Concretely, if the fill block uses `DiffToShunt()` to split the load, the power calculation reassembles the differential impedance from the declared `env.LoadImpedance`.

### 5.5 Limitations

The initial implementation assumes purely resistive source and load impedances. Complex impedances (for example `1GOhm || 15pF`) will use only the resistive component for power calculation, consistent with how `Port` reference impedances are handled in S-parameter analysis. Support for reactive impedance power calculation may be added in a future revision.

---

## 6. Future Work

Under-signal average supply power measurement (as opposed to quiescent DC power) requires a robust mapping from supply intent to the extracted PSS `i(V...)` source-current vectors across bench/interface bindings. That measurement remains deferred. The current approach uses the quiescent DC power from a separate `QuiescentPower` bench, which is exact for class-A and a reasonable approximation for moderate compression.

---

## 7. Grammar and Recognition Changes

### 7.1 PSSAnalysis Type

`PSSAnalysis` is added to the analysis type alternatives:

```antlr
analysisType
    : AC_ANALYSIS_TYPE
    | DC_ANALYSIS_TYPE
    | TRAN_ANALYSIS_TYPE
    | NOISE_ANALYSIS_TYPE
    | STB_ANALYSIS_TYPE
    | SP_ANALYSIS_TYPE
    | PSS_ANALYSIS_TYPE
    ;

PSS_ANALYSIS_TYPE : 'PSSAnalysis' ;
```

### 7.2 Accessor Integration

No new result type is needed. `voltage()` and `current()` gain a `PSSAnalysis` branch in their analysis-type dispatch, returning `VoltageWaveform` and `CurrentWaveform` respectively (same as `TranAnalysis`).

### 7.3 New Built-ins

Three built-in functions are added to the function registry:

- `duration(VoltageWaveform | CurrentWaveform) → Time` — time span of a waveform. For PSS, this is the solved period.
- `harmonic_power(VoltageWaveform, Impedance [, Int]) → Power (W)` — real power at a specific harmonic from a periodic voltage waveform across a known impedance. Fundamental frequency inferred from waveform time span.
- `thd(VoltageWaveform, Int) → Scalar` — total harmonic distortion (voltage-domain) of a periodic waveform. Fundamental frequency inferred from waveform time span.

---

## 8. Error Conditions

### 8.1 Semantic Errors

| Condition | Error |
| --- | --- |
| Missing required parameter (`fguess`, `tstab`, `harmonics`) | `PSSAnalysis '{name}' missing required parameter '{param}'` |
| `fguess` is not a `Frequency` | `PSSAnalysis '{name}.fguess' expects 'Frequency' but got '{type}'` |
| `tstab` is not a `Time` | `PSSAnalysis '{name}.tstab' expects 'Time' but got '{type}'` |
| `voltage(pss, ...)` first argument is not a `PSSAnalysis` | Same error as for other analysis types |
| No `resp` terminal for oscillating node resolution | `PSSAnalysis requires at least one resp terminal for oscillating node` |
| `harmonic_power` first argument is not a `VoltageWaveform` | `harmonic_power first argument must be a VoltageWaveform` |
| `harmonic_power` second argument is not an `Impedance` | `harmonic_power second argument must be an Impedance` |
| `thd` first argument is not a `VoltageWaveform` | `thd first argument must be a VoltageWaveform` |
| `thd` second argument is not an `Int` | `thd second argument must be an Int` |

### 8.2 Runtime Errors

| Condition | Behavior |
| --- | --- |
| PSS solver fails to converge | Error: `PSS did not converge for analysis '{name}'` |
| Harmonic index `k` exceeds declared `harmonics` | Error: `Harmonic index {k} exceeds declared harmonics count {max}` |
| Zero or negative load/source impedance | Error: `Invalid impedance for power calculation at terminal '{terminal}'` |

---

## 9. Implementation Plan

1. Add `PSSAnalysis` to the grammar, AST, and semantic validation. Extend `voltage()` and `current()` type dispatch to handle `PSSAnalysis` (returning `VoltageWaveform` / `CurrentWaveform`).
2. Add the `duration`, `harmonic_power`, and `thd` built-ins to the semantic checker and measurement runner (waveform time span, DFT, power/distortion computation).
3. Extend `BenchPlanAnalysis` with PSS-specific fields (`FguessHz`, `TstabS`, `Harmonics`, `OscNode`) and add compilation in `BenchAnalysisCompiler`.
4. Extend `BenchTestbenchEmitter` to emit the ngspice `pss` command and `wrdata` extraction for terminal node voltages.
5. Extend `BenchMeasurementRunner` to evaluate `voltage` / `current` for PSS analysis contexts (parse PSS wrdata, return waveform values).
6. Add `PSSBenches.cas` to the standard library with the bench hierarchy described in Section 3.
7. Add unit tests for validation, plan compilation, DFT correctness, and power calculation, plus golden tests for emitted PSS testbenches.
