# RFC-0008: Periodic Steady-State (PSS) Analysis

Status: Draft
Authors: Claude (proposed), Titan Yuan (review)
Created: 2026-03-04
Target Version: Cascode 4.x

---

## Abstract

This RFC proposes periodic steady-state (PSS) analysis within Cascode's bench system. The design introduces `PSSAnalysis` as a new analysis type that integrates with the existing `voltage()`, `current()`, and waveform infrastructure — the same accessors used for `TranAnalysis`. New built-ins `duration`, `mean`, `harmonic_power`, and `thd` support waveform time-span extraction, DC-component averaging, per-harmonic power computation, and distortion analysis from periodic waveforms.

PSS analysis finds the periodic steady-state response of a nonlinear circuit driven at (or near) a specified frequency. The solver produces one period of the steady-state waveform, from which bench measurements can extract the solved oscillation frequency, harmonic voltages/currents, and harmonic power. This enables characterization of power amplifiers, oscillators, mixers, and other large-signal periodic circuits.

PSS benches follow the `TranBenches` harness pattern: explicit `VSIN` sources and `Impedor` terminations in the fill block, with no `Port` primitives. The oscillating node is always the output terminal, resolved at emission time.

---

## 1. PSSAnalysis

### 1.1 Declaration

`PSSAnalysis` is declared in `analysis {}` like other analysis types:

```cascode
analysis {
  PSSAnalysis pss = new PSSAnalysis(
    guess_frequency=1GHz,
    stabilization_time=10ns,
    harmonics=10)
}
```

### 1.2 Parameters

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `guess_frequency` | `Frequency` | yes | — | Initial frequency estimate for the PSS solver |
| `stabilization_time` | `Time` | yes | — | Stabilization time before shooting iterations begin |
| `harmonics` | integer | yes | — | Number of harmonics to resolve in the output |

Arguments may use expressions over `constraints`, `env`, and bench parameters, consistent with other analysis types.

### 1.3 Oscillating Node

The oscillating node required by the simulator's PSS command is the `resp OUT` terminal of the bench, resolved to a netlist node at emission time. There is no explicit parameter for specifying it; the runtime discovers the output terminal from the bench definition.

For single-ended outputs, the oscillating node is the net connected to `OUT`. For differential outputs, it is the net connected to `OUT.P`.

### 1.4 Simulation Semantics

The PSS solver performs a transient stabilization for `stabilization_time` seconds, then applies a shooting method to converge on the periodic steady-state waveform at (or near) `guess_frequency`. The result is a time-domain waveform spanning exactly one solved period, available for all circuit node voltages.

For driven circuits (where a `VSIN` source provides the excitation), the solver converges to the exact drive frequency. For autonomous oscillators (no external drive), the solver finds the natural oscillation frequency, which may differ from `guess_frequency`.

### 1.5 Harness Topology

PSS benches use standard fill-block primitives rather than `Port` instances. The harness follows the bench/binding design contract: benches remain topology-agnostic and bindings provide the bias context.

- Input stimulus (driven benches): `VSIN` source through `Impedor` source impedance, referenced to ground. The bench does not provide input common-mode biasing. That responsibility falls to the interface binding, which has topology-specific knowledge of the DUT's DC operating requirements.
- Output termination: `Impedor` load impedance, referenced to ground. Like the input side, any output common-mode biasing is the binding's responsibility.
- The stimulus amplitude is resolved through `get_input_amplitude(25mV)` for single-ended benches and `get_diff_input_amplitude(25mV)` for differential benches. Both helpers check `env.InputPower` first, then `env.InputAmplitude`, then the fallback. `TranBenches` follows the same split so per-leg differential drive is computed consistently in PSS and transient benches.

This separation keeps `PSSAnalysis` purely about solver parameters while the fill block owns the stimulus topology, and the binding owns the DC operating-point context.

### 1.6 Input Amplitude Resolution

The standard library uses separate helpers for single-ended and differential VSIN drive amplitude resolution:

```cascode
function get_input_amplitude(Voltage fallback) : Voltage {
  if env.InputPower { return sqrt(8 * (env.SourceImpedance / 1Ohm) * (env.InputPower / 1W)) * 1V }
  if env.InputAmplitude { return env.InputAmplitude }
  return fallback
}

function get_diff_input_amplitude(Voltage fallback) : Voltage {
  if env.InputPower {
    return sqrt(2 * (env.SourceImpedance / 1Ohm) * (env.InputPower / 1W)) * 1V
  }
  if env.InputAmplitude { return env.InputAmplitude }
  return fallback
}
```

For single-ended benches, when `env.InputPower` is set, the peak amplitude is derived from the maximum available power of a sinusoidal source into a matched load: $A = \sqrt{8\,R_s\,P_{avail}}$, where `R_s` is `env.SourceImpedance`. This is the standard RF convention: the interface specifies power and the harness computes the voltage for the given source impedance.

For differential benches, `env.InputPower` is interpreted as total differential available power. The helper splits that power across the two legs and computes the per-leg VSIN amplitude against the corresponding per-leg source impedance, yielding $A_{leg} = \sqrt{2\,R_{diff}\,P_{avail}} = \sqrt{4\,R_{leg}\,P_{avail}}$ with `R_leg = env.SourceImpedance.DiffToShunt()`. This avoids the previous overdrive case where reusing the single-ended helper on each leg would deliver 4x the intended total power.

When `env.InputPower` is not set, both helpers fall back to `env.InputAmplitude` (an explicit peak voltage), then to the hardcoded fallback (25 mV for both PSS and transient benches).

Note that available power (what the source can deliver into a matched load) differs from delivered power (what actually enters the DUT). `env.InputPower` participates only in drive-amplitude resolution; delivered input power is measured from voltage and current waveforms at the DUT input terminal.

---

## 2. Waveform and Time Accessors

`PSSAnalysis` reuses the same `voltage()` and `current()` accessors as `TranAnalysis`, with the same current-source constraints as transient benches. The PSS waveform covers exactly one solved period, so the resulting `VoltageWaveform` / `CurrentWaveform` objects contain one cycle of the steady-state response.

### 2.1 Voltage and Current

```
voltage(PSSAnalysis, terminal) → VoltageWaveform
current(PSSAnalysis, element_pin) → CurrentWaveform
```

These behave identically to their transient counterparts. `voltage()` takes a terminal reference (node voltage). `current()` uses the same extraction model as transient benches: branch currents are available for harness voltage-source elements written via `i(V...)` vectors (for example `harness.VDD.P`), not as a generic simulator-side element-pin probe for arbitrary devices.

For differential terminals, `voltage(pss, OUT)` produces the differential waveform `V(OUT.P) - V(OUT.N)`, consistent with the existing differential terminal semantics ([Section 4.2.5](../../spec/language/Ch04_Bench_System.md#425-differential-terminal-semantics)).

### 2.2 Duration

A `duration` built-in returns the time span of a waveform:

```
duration(VoltageWaveform) → Time
duration(CurrentWaveform) → Time
```

This computes $t_{end} - t_{start}$ from the waveform's time vector. For PSS waveforms, the duration is exactly one solved period. The solved frequency is its reciprocal: $f = 1 / \text{duration}(w)$. This built-in is general-purpose and applies to any waveform, not just PSS.

### 2.3 Mean

A `mean` built-in returns the time-averaged (DC component) value of a waveform:

```
mean(VoltageWaveform) → Voltage
mean(CurrentWaveform) → Current
```

For a discrete time-domain waveform with $N$ samples, the result is the arithmetic mean of the sample values. For a PSS current waveform through a supply source, `mean(current(pss, supplyDC.P))` gives the average supply current over one period — the quantity needed for supply power computation under large-signal drive.

Like `duration`, this built-in is general-purpose and applies to any waveform, not just PSS.

### 2.4 Harmonic Power

A `harmonic_power` built-in computes real power at a specific harmonic from periodic waveforms:

```
harmonic_power(VoltageWaveform, Impedance) → power-valued scalar (W)
harmonic_power(VoltageWaveform, Impedance, Scalar k) → power-valued scalar (W)
harmonic_power(VoltageWaveform, CurrentWaveform) → power-valued scalar (W)
harmonic_power(VoltageWaveform, CurrentWaveform, Scalar k) → power-valued scalar (W)
```

The two-argument form defaults to the fundamental ($k = 1$). The fundamental frequency is inferred from the waveform's time span ($f = 1 / (t_{end} - t_{start})$), which is exact for PSS waveforms since they cover precisely one solved period.

For the impedance form, the built-in extracts the $k$-th harmonic peak phasor $V_k$, then computes:

$$P_k = \frac{|V_k|^2}{2\,R}$$

where $R$ is the resistive component of the impedance.

For the waveform form, the built-in extracts $V_k$ and $I_k$ and computes delivered power:

$$P_k = \tfrac{1}{2}\,\text{Re}\{V_k\,I_k^*\}$$

The impedance form is used for output-power measurements against known load impedance. The waveform form is used for delivered input-power measurements under mismatch.

### 2.5 THD

A `thd` built-in computes total harmonic distortion from a periodic voltage waveform:

```
thd(VoltageWaveform, Scalar harmonics) → Scalar
```

The fundamental frequency is inferred from the waveform's time span, same as `harmonic_power`. The `harmonics` argument controls how many harmonics above the fundamental to include. The result is the voltage-domain THD:

$$\text{THD} = \frac{\sqrt{\sum_{k=2}^{N} |V_k|^2}}{|V_1|}$$

where $V_k$ is the $k$-th harmonic phasor and $N$ is `harmonics`. The impedance cancels from the ratio, so no impedance argument is needed. Like `harmonic_power`, this built-in works on any `VoltageWaveform` but is primarily intended for periodic waveforms.

### 2.6 Input Power via Voltage and Current

Delivered input power uses the terminal voltage waveform plus the source-branch current waveform at the DUT input:

$$P_{in,k} = \tfrac{1}{2}\,\text{Re}\{V_{IN,k}\,I_{in,k}^*\}$$

In the standard driven benches, this maps directly to `voltage(pss, IN)` and `current(pss, harness.<vsource>.P)`, then `harmonic_power(vin, iin [, k])`.

---

## 3. Bench Hierarchy

PSS benches are organized in an abstract hierarchy that separates measurement definitions from harness topology.

### 3.1 AbstractOutputPSS

The base abstract bench declares the output terminal, the analysis, and output-side measurements. It is parameterized by `guess_frequency`, analogous to `stim_freq` in `AbstractTran`.

```cascode
abstract bench AbstractOutputPSS(Frequency guess_frequency = 1GHz) {
  abstract resp OUT

  analysis {
    PSSAnalysis pss = new PSSAnalysis(
      guess_frequency=guess_frequency,
      stabilization_time=10ns,
      harmonics=10)
  }

  measurements {
    measurement FundamentalFrequency : Hz {
      VoltageWaveform vout = voltage(pss, OUT)
      return 1 / duration(vout)
    }
    measurement FundamentalPeriod : s {
      VoltageWaveform vout = voltage(pss, OUT)
      return duration(vout)
    }
    measurement OutputPower : W {
      VoltageWaveform vout = voltage(pss, OUT)
      Impedance loadImp = env.LoadImpedance
      return harmonic_power(vout, loadImp)
    }
    measurement OutputPowerHarmonic(Scalar k) : W {
      VoltageWaveform vout = voltage(pss, OUT)
      Impedance loadImp = env.LoadImpedance
      return harmonic_power(vout, loadImp, k)
    }
    measurement SupplyPower(Scalar supplyVoltage, Scalar dcCurrent) : W {
      return supplyVoltage * abs(dcCurrent)
    }
    measurement DrainEfficiency(Scalar dcPower) : Scalar {
      VoltageWaveform vout = voltage(pss, OUT)
      Impedance loadImp = env.LoadImpedance
      Scalar pout = harmonic_power(vout, loadImp)
      return pout / dcPower
    }
  }
}
```

`SupplyPower` receives the mean supply current and the corresponding rail voltage from the caller (typically a binding that extracts current from the PSS waveform via `mean(current(pss, supplyDC.P))` and forwards the rail value explicitly). `DrainEfficiency` takes a precomputed scalar `dcPower`; the binding passes the result of `SupplyPower`. Both measurements are defined here rather than in `AbstractInputOutputPSS` because they apply equally to autonomous oscillators and driven circuits without assuming any particular rail name.

### 3.2 AbstractInputOutputPSS

Extends `AbstractOutputPSS` with an input terminal plus shared input/output metrics that do not depend on a specific source instance name.

```cascode
abstract bench AbstractInputOutputPSS extends AbstractOutputPSS {
  abstract stim IN
  abstract resp OUT

  measurements {
    measurement TotalHarmonicDistortion(Scalar harmonics) : Scalar {
      VoltageWaveform vout = voltage(pss, OUT)
      return thd(vout, harmonics)
    }
  }
}
```

`DrainEfficiency` is inherited from `AbstractOutputPSS`. `PAE` takes scalar `dcPower`; the binding passes the result of `SupplyPower`.

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

Large-signal sinusoidal drive through source impedance, with ground-referenced stimulus. The bench does not provide common-mode biasing; the interface binding is responsible for setting the DUT's DC operating point.

```cascode
bench SEToSEPSS extends AbstractInputOutputPSS {
  stim IN : analog
  resp OUT : analog

  measurements {
    measurement InputPower : W {
      VoltageWaveform vin = voltage(pss, IN)
      CurrentWaveform iin = current(pss, harness.vin.P)
      return harmonic_power(vin, iin)
    }
    measurement Gain : dB {
      VoltageWaveform vout = voltage(pss, OUT)
      Impedance loadImp = env.LoadImpedance
      Scalar pout = harmonic_power(vout, loadImp)
      Scalar pin = InputPower()
      return db10(pout / pin)
    }
  }

  fill {
    net gnd : ground

    GND _ = new GND() { .GND--gnd }

    VSIN vin = new VSIN(A=get_input_amplitude(25mV), freq=guess_frequency, phase=0deg) {
      .N--gnd
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

Anti-phase `VSIN` pair through split source impedances, with ground-referenced differential load termination. Like `SEToSEPSS`, the bench does not provide common-mode biasing; the interface binding sets the DUT's DC operating point.

```cascode
bench DiffToDiffPSS extends AbstractInputOutputPSS {
  stim IN : Diff
  resp OUT : Diff

  measurements {
    measurement InputPower : W {
      VoltageWaveform vinP = voltage(pss, IN.P)
      VoltageWaveform vinN = voltage(pss, IN.N)
      CurrentWaveform iinP = current(pss, harness.inP.P)
      CurrentWaveform iinN = current(pss, harness.inN.P)
      return harmonic_power(vinP, iinP) + harmonic_power(vinN, iinN)
    }
    measurement Gain : dB {
      VoltageWaveform vout = voltage(pss, OUT)
      Impedance loadImp = env.LoadImpedance
      Scalar pout = harmonic_power(vout, loadImp)
      Scalar pin = InputPower()
      return db10(pout / pin)
    }
  }

  fill {
    net gnd : ground

    GND _ = new GND() { .GND--gnd }

    VSIN inP = new VSIN(A=get_diff_input_amplitude(25mV), freq=guess_frequency, phase=0deg) {
      .N--gnd
    }
    VSIN inN = new VSIN(A=get_diff_input_amplitude(25mV), freq=guess_frequency, phase=180deg) {
      .N--gnd
    }

    Impedor sourceP = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }
    Impedor sourceN = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { }

    inP.P--sourceP.P
    sourceP.N--IN.P

    inN.P--sourceN.P
    sourceN.N--IN.N

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

---

## 4. Interface Binding

PSS benches are bound like any other bench. Bench terminals are mapped to DUT terminals; the fill block's sources and impedances sit on those terminal nets. Because the bench fill blocks do not provide common-mode biasing, the binding must ensure all DUT analog terminals have a defined DC path — the same responsibility described in the bench/binding design contract.

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
    bind SEToSEPSS as pss_bench {
      bench.IN--dut.IN
      bench.OUT--dut.OUT

      fill {
        net gnd : ground
        GND g = new GND() { .GND--gnd }

        VDC supplyDC = new VDC(V=harness.VDD) {
          .P--dut.VDD
          .N--gnd
        }
      }

      measurements {
        Current idc = mean(current(pss, supplyDC.P))
        measurement SupplyPower : W = base::SupplyPower(supplyVoltage=harness.VDD, dcCurrent=idc)
        measurement DrainEfficiency : Scalar = base::DrainEfficiency(dcPower=SupplyPower())
        measurement PAE : Scalar = base::PAE(dcPower=SupplyPower())
      }
    }
  }
}
```

The binding declares a `VDC` supply source explicitly so that `current(pss, supplyDC.P)` can extract the branch current through it during PSS. The `mean` built-in averages the one-period current waveform to obtain the DC supply current, and the binding forwards both that current and the appropriate rail voltage to the inherited `SupplyPower`, `DrainEfficiency`, and `PAE` measurements. No separate `QuiescentPower` bench is needed — the supply current is measured directly under large-signal periodic drive, which captures class-dependent current draw that a DC operating-point measurement would miss.

Note that this PA example has `InputCommonModeRange = 0V`, so the bench's ground-referenced VSIN matches the intended bias. For amplifier topologies that require a non-zero input common-mode (op-amps, for example), the binding must add a `VDC` bias source, the same pattern used in the standard amplifier interface bindings for `QuiescentPower` and `DCBias`.

Constraints reference measurements through the bind name:

```cascode
constraints {
  numeric {
    c_osc_freq = pss_bench(guess_frequency=2.4GHz)::FundamentalFrequency >= 2.3GHz
    c_output_pwr = pss_bench(guess_frequency=2.4GHz)::OutputPower >= 10mW
    c_pae = pss_bench(guess_frequency=2.4GHz)::PAE >= 0.3
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

The real power delivered to the DUT is computed from the solved voltage and current harmonics at the input terminal:

$$P_{in,k} = \tfrac{1}{2}\,\text{Re}\{V_{IN,k}\,I_{in,k}^*\}$$

For single-ended driven benches, `I_{in,k}` comes from the branch current of `harness.vin.P`. For differential driven benches, delivered input power is the sum of each leg's delivered power using `harness.inP.P` and `harness.inN.P`.

### 5.4 Supply Power

Supply power under periodic drive is computed from the mean (DC component) of the supply branch current over one PSS period:

$$P_{supply} = V_{DD} \cdot |\bar{I}_{DD}|$$

where $\bar{I}_{DD} = \text{mean}(i_{DD}(t))$ is the arithmetic mean of the extracted `i(V...)` current waveform through the binding's VDC supply source. This captures the actual average current draw under large-signal conditions, which differs from the quiescent operating-point current for class-AB, class-B, and class-C amplifiers where conduction angle varies with signal amplitude.

### 5.5 Differential Power

For differential output-power calculations, the voltage used is the differential quantity `V(P) - V(N)` and the impedance is the full differential impedance (not the per-leg shunt value). Concretely, if the fill block uses `DiffToShunt()` to split the load, the output-power calculation reassembles the differential impedance from the declared `env.LoadImpedance`.

For differential input-power calculations, delivered power is summed per leg using the per-leg voltage and current waveforms:

$$P_{in,k}^{diff} = \tfrac{1}{2}\,\text{Re}\{V_{IN.P,k} I_{inP,k}^*\} + \tfrac{1}{2}\,\text{Re}\{V_{IN.N,k} I_{inN,k}^*\}$$

### 5.6 Limitations

The initial implementation assumes purely resistive source and load impedances. Complex impedances (for example `1GOhm || 15pF`) will use only the resistive component for power calculation, consistent with how `Port` reference impedances are handled in S-parameter analysis. Support for reactive impedance power calculation may be added in a future revision.

---

## 6. Grammar and Recognition Changes

### 6.1 PSSAnalysis Type

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

### 6.2 Accessor Integration

No new result type is needed. `voltage()` and `current()` gain a `PSSAnalysis` branch in their analysis-type dispatch, returning `VoltageWaveform` and `CurrentWaveform` respectively (same as `TranAnalysis`).

### 6.3 New Built-ins

Four built-in functions are added to the function registry:

- `duration(VoltageWaveform | CurrentWaveform) → Time` — time span of a waveform. For PSS, this is the solved period.
- `mean(VoltageWaveform | CurrentWaveform) → Voltage | Current` — arithmetic mean of a waveform's sample values (the DC component). Returns a scalar in the waveform's native unit.
- `harmonic_power(VoltageWaveform, Impedance [, Scalar]) → power-valued scalar (W)` — matched-load real power from a periodic voltage waveform across a known impedance.
- `harmonic_power(VoltageWaveform, CurrentWaveform [, Scalar]) → power-valued scalar (W)` — delivered real power from periodic voltage and current waveforms.
- `thd(VoltageWaveform, Scalar) → Scalar` — total harmonic distortion (voltage-domain) of a periodic waveform. Fundamental frequency inferred from waveform time span.

---

## 7. Error Conditions

### 7.1 Semantic Errors

| Condition | Error |
| --- | --- |
| Missing required parameter (`guess_frequency`, `stabilization_time`, `harmonics`) | `PSSAnalysis '{name}' missing required parameter '{param}'` |
| `guess_frequency` is not a `Frequency` | `PSSAnalysis '{name}.guess_frequency' expects 'Frequency' but got '{type}'` |
| `stabilization_time` is not a `Time` | `PSSAnalysis '{name}.stabilization_time' expects 'Time' but got '{type}'` |
| `voltage(pss, ...)` first argument is not a `PSSAnalysis` | Same error as for other analysis types |
| No `resp` terminal for oscillating node resolution | `PSSAnalysis requires at least one resp terminal` |
| `harmonic_power` first argument is not a `VoltageWaveform` | `harmonic_power first argument must be a VoltageWaveform` |
| `harmonic_power` second argument is neither `Impedance` nor `CurrentWaveform` | `harmonic_power second argument must be an Impedance or CurrentWaveform` |
| `mean` argument is not a `VoltageWaveform` or `CurrentWaveform` | `mean argument must be a VoltageWaveform or CurrentWaveform` |
| `thd` first argument is not a `VoltageWaveform` | `thd first argument must be a VoltageWaveform` |
| `thd` second argument is not an integer-valued scalar | `thd second argument must be an integer scalar` |

Declaration typing note: measurement signatures and local declarations currently accept scalar physical types such as `Voltage`, `Current`, and `Impedance`, plus `Scalar`; there is no `Power` declaration keyword, so power-carrying locals/parameters should use `Scalar` and keep `: W` on the measurement return type.

### 7.2 Runtime Errors

| Condition | Behavior |
| --- | --- |
| PSS solver fails to converge | Error: `PSS did not converge for analysis '{name}'` |
| Harmonic index `k` exceeds declared `harmonics` | Error: `Harmonic index {k} exceeds declared harmonics count {max}` |
| Zero or negative load/source impedance | Error: `Invalid impedance for power calculation at terminal '{terminal}'` |

---

## 8. Implementation Plan

1. Add `PSSAnalysis` to the grammar, AST, and semantic validation. Extend `voltage()` and `current()` type dispatch to handle `PSSAnalysis` (returning `VoltageWaveform` / `CurrentWaveform`).
2. Add the `duration`, `mean`, `harmonic_power`, and `thd` built-ins to the semantic checker and measurement runner (waveform time span, DC-component averaging, DFT, power/distortion computation).
3. Extend `BenchPlanAnalysis` with PSS-specific fields (`GuessFrequencyHz`, `TstabS`, `Harmonics`, `OscNode`) and add compilation in `BenchAnalysisCompiler`.
4. Extend `BenchTestbenchEmitter` to emit the ngspice `pss` command and `wrdata` extraction for terminal node voltages and source branch currents.
5. Extend `BenchMeasurementRunner` to evaluate `voltage` / `current` for PSS analysis contexts (parse PSS wrdata, return waveform values).
6. Add `PSSBenches.cas` to the standard library with the bench hierarchy described in Section 3.
7. Add unit tests for validation, plan compilation, DFT correctness, mean computation, and power calculation, plus golden tests for emitted PSS testbenches.
