# Bench cookbook

This cookbook is a practical companion to the normative bench specification in
[Chapter 4: Bench System](../../spec/language/Ch04_Bench_System.md). It collects patterns used in the
[standard library](../../lib/std/bench/) and in the [golden examples](../../tests/golden/cas/).

Benches are executed when at least one of their measurements is referenced by a numeric constraint.
When developing a new bench or binding, add a constraint that forces execution and inspect the
emitted testbench.

## Quick references

- Standard benches: [`TransferBenches.cas`](../../lib/std/bench/TransferBenches.cas), [`NoiseBenches.cas`](../../lib/std/bench/NoiseBenches.cas), [`TranBenches.cas`](../../lib/std/bench/TranBenches.cas), [`PSSBenches.cas`](../../lib/std/bench/PSSBenches.cas), [`PowerBenches.cas`](../../lib/std/bench/PowerBenches.cas), [`SParamBenches.cas`](../../lib/std/bench/SParamBenches.cas)
- Standard interface bindings: [`SingleEndedOpAmp.cas`](../../lib/std/amp/SingleEndedOpAmp.cas), [`FullyDifferentialOpAmp.cas`](../../lib/std/amp/FullyDifferentialOpAmp.cas), [`SingleEndedAmp.cas`](../../lib/std/amp/SingleEndedAmp.cas), [`SingleEndedPassiveFilter.cas`](../../lib/std/filters/SingleEndedPassiveFilter.cas), [`DifferentialPassiveFilter.cas`](../../lib/std/filters/DifferentialPassiveFilter.cas)
- Short, complete example: [`RcLowpass.el.cai`](../../tests/golden/cas/bench/RcLowpass.el.cai)
- Coverage stress cases: [`tests/golden/cas/stress/`](../../tests/golden/cas/stress/)

## Harness primitives (what the runtime recognizes)

Bench `fill {}` blocks and binding bodies may instantiate a small set of harness primitives that the
bench runtime emits as backend elements (see [Chapter 4, Section 4.3.2](../../spec/language/Ch04_Bench_System.md#432-harness-primitives)):

- `GND`, `VDC`, `VAC`, `VSIN`, `Kick`, `Impedor` / `Impedance`

Prefer these primitives over backend-specific netlist syntax.

## Recipe: AC transfer benches (gain, bandwidth, phase margin)

### When to use

Use an AC transfer bench when you need small-signal metrics derived from an `ACAnalysis`, such as
passband gain, \(-3\) dB bandwidth, gain-bandwidth, or phase margin.

### Minimal pattern

Transfer benches typically include an explicit input bias (common-mode), so the DUT is not floating,
a stimulus source plus source impedance, and a load impedance. Measurements are built from a
transfer function and spectrum post-processing:

```cascode
TransferFunction H = transfer(ac, IN, OUT)
GainSpectrum G = db20(H.Mag())
Frequency fg = G.FindCrossing(0dB, dir=falling, cross=1, from=ac.start, to=ac.stop)
Frequency f10 = G.Range(to=1MHz, from=100Hz).ValueAt(f=10kHz)
Time tclk = period(f=1MHz)
```

Reference implementations live in [`lib/std/bench/TransferBenches.cas`](../../lib/std/bench/TransferBenches.cas):

- `DiffToSETransfer` (Diff → analog)
- `DiffToDiffTransfer` (Diff → Diff)
- `SEToSETransfer` (analog → analog)

### Common pitfalls

- Floating inputs commonly produce singular matrices in AC. Bias inputs and include source impedance (see the `VDC` and `Impedor` patterns in the [standard benches](../../lib/std/bench/TransferBenches.cas)).
- Differential outputs should split the target load per leg using `env.LoadImpedance.DiffToShunt()` (see [`DiffToDiffTransfer`](../../lib/std/bench/TransferBenches.cas)).

## Recipe: Noise benches (output noise and input-referred noise)

### When to use

Use a noise bench when you need spot noise density, integrated noise over a band, or input-referred
noise.

### Minimal pattern

Noise benches typically pair a `NoiseAnalysis` with an `ACAnalysis`, so they can compute input-referred
noise by dividing output noise density by the transfer magnitude:

```cascode
NoiseSpectrum n_in = input_referred_noise(noise_ac, ac, IN, OUT)
return n_in.Integrate(from, to)
```

Reference implementations live in [`lib/std/bench/NoiseBenches.cas`](../../lib/std/bench/NoiseBenches.cas) (`DiffToSENoise`, `DiffToDiffNoise`,
and `SEToSENoise`).

### Common pitfalls

- `NoiseAnalysis` requires an independent source in the bench/binding fill to act as the noise input
  reference. Standard patterns use `VAC` sources.
- For differential outputs, `output=OUT` refers to \(V(OUT.P) - V(OUT.N)\) (see the spec’s
  Section 4.2.5, “Differential terminal semantics”).

## Recipe: Transient benches (swing and time-domain metrics)

### When to use

Use a transient bench when you need time-domain metrics such as output swing, settling, or supply
current transients.

### Minimal pattern

Transient benches usually bias the input, apply a time-domain source such as `VSIN`, run a
`TranAnalysis`, and compute metrics from waveforms (for example `vout.Max() - vout.Min()`).

Bench parameters are commonly used to create multiple bench instances without duplicating the bench
definition. The standard library transient benches accept a `stim_freq` parameter:

```cascode
c_swing = tran_bench(stim_freq=1kHz)::OutputSwing() at net::OUT >= 0.4V
```

Reference implementations live in [`lib/std/bench/TranBenches.cas`](../../lib/std/bench/TranBenches.cas) (`DiffToSETran`, `DiffToDiffTran`,
and `SEToSETran`).

### Common pitfalls

- Choose a simulation window that excludes startup transients. A common pattern is “simulate 10
  cycles, evaluate on the last cycle”.
- For differential outputs, `voltage(tran, OUT)` yields a waveform for \(V(OUT.P) - V(OUT.N)\).

## Recipe: PSRR benches (supply rejection)

### When to use

Use PSRR benches when you want to measure how supply ripple couples to the output.

### Minimal pattern

PSRR benches inject ripple by placing a `VAC` source in series with a `VDC` supply bias, then measure
the supply-to-output transfer. The standard library provides:

- `SupplyToSERejection` (Diff input, analog output)
- `SupplyToDiffRejection` (Diff input, Diff output)
- `SupplyToSERejectionSEInput` (analog input, analog output)

All three are defined in [`lib/std/bench/PowerBenches.cas`](../../lib/std/bench/PowerBenches.cas).

## Recipe: PSS benches (periodic steady-state metrics)

### When to use

Use a PSS bench when you need large-signal periodic metrics such as harmonic output power, THD, and
efficiency under driven or autonomous oscillation.

### Minimal pattern

PSS benches run a `PSSAnalysis`, read one solved period as a waveform, and derive metrics from
`duration`, `mean`, `harmonic_power`, and `thd`:

```cascode
analysis {
  PSSAnalysis pss = new PSSAnalysis(guess_frequency=1GHz, stabilization_time=10ns, harmonics=10)
}

measurement OutputPower : W {
  VoltageWaveform vout = voltage(pss, OUT)
  Impedance loadImp = env.LoadImpedance
  return harmonic_power(vout, loadImp)
}
```

Reference implementations are in [`lib/std/bench/PSSBenches.cas`](../../lib/std/bench/PSSBenches.cas):
`SEOscPSS`, `DiffOscPSS`, `SEToSEPSS`, and `DiffToDiffPSS`.

### Common pitfalls

- PSS bench fill blocks do not provide input or output common-mode biasing — `VSIN` sources are
  referenced to ground. The interface binding is responsible for establishing the DUT's DC operating
  point, following the same pattern as `QuiescentPower` and `DCBias` bindings (see
  [bench/binding design contract](../../AGENTS.md)). Without binding-side bias, amplifier inputs
  may float and the PSS solver will see incorrect or degenerate operating points.
- For input/output benches, drive amplitude is resolved by `get_input_amplitude(25mV)`: first
  `env.InputPower`, then `env.InputAmplitude`, then the fallback.
- For output power, `harmonic_power` uses the `VoltageWaveform + Impedance` form; assign
  `env.LoadImpedance` to an `Impedance` local before calling it in measurements. For delivered
  input power under mismatch, use the `VoltageWaveform + CurrentWaveform` form with
  `current(pss, harness.<source>.P)`.
- Supply power under drive should be computed from a supply branch current waveform in the binding,
  for example `mean(current(pss, supplyDC.P))`, then forwarded to `SupplyPower`.
- PSS requires at least one `resp` terminal, so the runtime can resolve the oscillating node.

### Output-referred vs input-referred PSRR

Input-referred PSRR is gain-normalized. Standard interfaces export a no-argument
`InputReferredPSRR` by forwarding to the underlying bench measurement with a default `dmGain` source:

```cascode
measurement InputReferredPSRR : dB =
  base::InputReferredPSRR(dmGain=transfer_bench::PassbandGain)
```

See [`SingleEndedOpAmp.cas`](../../lib/std/amp/SingleEndedOpAmp.cas), [`FullyDifferentialOpAmp.cas`](../../lib/std/amp/FullyDifferentialOpAmp.cas), and [`SingleEndedAmp.cas`](../../lib/std/amp/SingleEndedAmp.cas).

## Recipe: Quiescent power

### When to use

Use quiescent power when you want a deterministic operating-point power number derived from the
harness-applied supply.

### Minimal pattern

The standard library’s `QuiescentPower` bench declares a supply terminal and a return terminal and
computes static power from the harness-injected source:

```cascode
measurement QuiescentPower : W {
  return quiescent_power(PWR, RET)
}
```

See `QuiescentPower` in [`lib/std/bench/PowerBenches.cas`](../../lib/std/bench/PowerBenches.cas) and its bindings in the [standard amplifier
interfaces](../../lib/std/amp/).

### Common pitfalls

- Ensure the circuit’s `harness { ... }` provides the referenced supply and return rails (for example
  `supply VDD = 1.8V` and `ground GND = 0V`) and that the bench is bound to those terminals.
- The `QuiescentPower` bench is intentionally topology-agnostic: it only declares supply and return
  terminals. If the DUT has analog inputs (gates), the binding must bias them to avoid floating nodes.
  Without bias, transistors remain OFF and the bench reports 0 W. The standard amplifier interfaces
  solve this with binding-scoped instances that apply a common-mode VDC and source impedance:

```cascode
bind QuiescentPower as vdd_pwr {
  bench.PWR--dut.VDD
  bench.RET--dut.GND

  GND g = new GND() { .GND--gnd }
  VDC commonModeVDC = new VDC(V=env.InputCommonModeRange) { .P--vcm, .N--gnd }
  Impedor sourceP = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { .P--vcm, .N--dut.IN.P }
  Impedor sourceN = new Impedor(Z=env.SourceImpedance.DiffToShunt()) { .P--vcm, .N--dut.IN.N }
}
```

  The same pattern applies to `SEDCBias` and `DiffDCBias` bindings, which also omit input terminals
  from their bench definitions.

## Recipe: S-parameter benches (forward gain, return loss, stability)

### When to use

Use an S-parameter bench when you need RF metrics derived from an `SPAnalysis`, such as forward
gain, return loss, VSWR, isolation, stability factor, or group delay.

### Minimal pattern

S-parameter benches place `Port` harness primitives on the bench's response terminals. Each port
declares a sequential index, a reference impedance, and a DC bias. The standard library's
`TwoPortSParam` uses env-backed helpers to allow per-circuit impedance overrides with a `50Ohm`
fallback:

```cascode
Port port1 = new Port(N=1, Z=get_source_impedance(50Ohm), V=env.InputCommonModeRange) {
  .P--P1
  .N--gnd
}

Port port2 = new Port(N=2, Z=get_load_impedance(50Ohm), V=env.OutputCommonModeRange) {
  .P--P2
  .N--gnd
}
```

Measurements are built from an `SParameterMatrix` extracted from the analysis:

```cascode
SParameterMatrix S = sparam(sp)
return db20(S.S(2, 1).Mag()).ValueAt(f)
```

To constrain a full frequency band, return a sliced spectrum and apply a numeric constraint directly
to that measurement. Numeric constraints on spectrums and waveforms are evaluated element-wise, so
every sample in the selected band must satisfy the bound:

```cascode
measurement ForwardGain(Frequency from, Frequency to) : dB {
  SParameterMatrix S = sparam(sp)
  return db20(S.S(2, 1).Mag()).From(from).To(to)
}
```

```cascode
constraints {
  numeric {
    c_forward_gain = sparam_bench::ForwardGain(from=100kHz, to=10MHz) >= 10dB
  }
}
```

Reference implementation: [`lib/std/bench/SParamBenches.cas`](../../lib/std/bench/SParamBenches.cas).

> [!IMPORTANT]
> Port reference impedances (`z0`) are real-valued. When the `Z` parameter resolves to a parallel
> impedance expression such as `1GOhm || 15pF`, only the resistive terms contribute to `z0`;
> reactive components (capacitance or inductance) are discarded. A purely reactive impedance with
> no resistive term produces `z0=0`, which is invalid for simulator RF ports.

- Port numbers must be sequential starting at 1. Gaps or duplicates are rejected at compile time.

## Recipe: Probing internal nodes and measuring harness currents

### When to use

Probe internal nodes when a metric depends on an internal operating point (for example, a midpoint
voltage) or when you want to sanity-check bench wiring. Measure harness currents when you need rail
current/power behavior during AC or transient analysis.

### Minimal pattern

Internal DUT nets are accessed through `dut.<Name>` when the circuit declares the net:

```cascode
ComplexVoltageSpectrum v = voltage(ac, dut.mid)
return v.Mag().ValueAt(0Hz)
```

Harness-injected rail currents are accessed through the harness element pin:

```cascode
CurrentWaveform i = current(tr, harness.VDD.P)
return i.Max()
```

### Reference implementations

Two golden examples show the pattern end-to-end:

- [`tests/golden/cas/bench/DcInternalNode.cas`](../../tests/golden/cas/bench/DcInternalNode.cas)
- [`tests/golden/cas/bench/TranInternalCurrent.cas`](../../tests/golden/cas/bench/TranInternalCurrent.cas)

## Recipe: Binding benches in interfaces

### When to use

Bind benches in interfaces when you want a family of circuits to expose the same constraint surface
(`transfer_bench::...`, `noise_bench::...`, and so on) without repeating bindings in every circuit.

### Minimal pattern

Interfaces bind benches with stable names and wire bench terminals onto DUT terminals:

```cascode
bind DiffToSETransfer as transfer_bench {
  bench.IN--dut.IN
  bench.OUT--dut.OUT
}
```

Interfaces may also export derived measurements (for example, gain-normalized PSRR) using
binding-local `measurements { ... }` blocks. See [Chapter 4, Section 4.8.5](../../spec/language/Ch04_Bench_System.md#485-binding-measurement-exports).

### Reference implementations

Reference interfaces:

- [`lib/std/amp/SingleEndedOpAmp.cas`](../../lib/std/amp/SingleEndedOpAmp.cas) (Diff in, analog out)
- [`lib/std/amp/FullyDifferentialOpAmp.cas`](../../lib/std/amp/FullyDifferentialOpAmp.cas) (Diff in, Diff out)
- [`lib/std/amp/SingleEndedAmp.cas`](../../lib/std/amp/SingleEndedAmp.cas) (analog in, analog out)
- [`lib/std/filters/SingleEndedPassiveFilter.cas`](../../lib/std/filters/SingleEndedPassiveFilter.cas)
- [`lib/std/filters/DifferentialPassiveFilter.cas`](../../lib/std/filters/DifferentialPassiveFilter.cas)

## Workflow: authoring and debugging benches

1. Start from the closest standard bench in [`lib/std/bench/**`](../../lib/std/bench/).
2. Bind the bench in an interface or circuit with `benches { bind ... }`.
3. Add a numeric constraint referencing one measurement from the binding to force execution.
4. Run `cascode emit` to inspect the emitted testbench netlist.
5. Run `cascode bench run` to execute benches and produce `results.json`.
6. Run `cascode verify` (or rely on constraint checking in tests) to ensure constraints resolve to
   measured values.

The canonical complete example for this loop is [`tests/golden/cas/bench/RcLowpass.el.cai`](../../tests/golden/cas/bench/RcLowpass.el.cai). The root
[`README.md`](../../README.md) and [`docs/GETTING_STARTED.md`](../GETTING_STARTED.md) include end-to-end commands using that file.
