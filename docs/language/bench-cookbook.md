# Bench cookbook

This cookbook is a practical companion to the normative bench specification in
`spec/language/Ch04_Bench_System.md`. It focuses on patterns used in the standard library.

## Transfer (AC) benches

Transfer benches measure gain, bandwidth, and phase-derived metrics from an `ACAnalysis`.

See `lib/std/bench/TransferBenches.cas` for:
- Differential stimulus construction using paired `VAC` sources and `Impedor` source impedances.
- Output loading via `Impedor(Z=env.LoadImpedance)`.
- Measurements built from `transfer(ac, IN, OUT)`, `db20(H.Mag())`, and `FindCrossing(...)`.

Typical building blocks:

```cascode
TransferFunction H = transfer(ac, IN, OUT)
GainSpectrum G = db20(H.Mag())
Frequency fg = G.FindCrossing(0dB, dir=falling, cross=1, from=ac.start, to=ac.stop)
```

## Noise benches

Noise benches typically pair a `NoiseAnalysis` with an `ACAnalysis` so they can compute input-referred
noise by dividing output noise density by the transfer magnitude.

See `lib/std/bench/NoiseBenches.cas` for:
- `NoiseAnalysis noise_ac = new NoiseAnalysis(..., output=OUT)`
- `NoiseSpectrum n_in = input_referred_noise(noise_ac, ac, IN, OUT)`
- Spot noise via `ValueAt(f)` and integrated noise via `Integrate(from, to)`

```cascode
NoiseSpectrum n_in = input_referred_noise(noise_ac, ac, IN, OUT)
return n_in.Integrate(from, to)
```

## Transient benches

Transient benches use `TranAnalysis` and a time-domain source such as `VSIN` to drive the DUT.
They can return time-domain waveforms (`VoltageWaveform`) or reduced scalar metrics.

See `lib/std/bench/TranBenches.cas` for transient stimulus and waveform-derived measurements.

## Power (rail current) benches

Power benches compute rail power from the applied supply source. The standard pattern is a minimal
bench that declares a supply terminal and a return terminal and then calls `quiescent_power`:

See `lib/std/bench/PowerBenches.cas` and the binding in `lib/std/amp/SingleEndedOpAmp.cas`.

```cascode
measurement QuiescentPower : W {
  return quiescent_power(PWR, RET)
}
```

## Parameterized measurements

Measurements may declare typed parameters. Constraints can call parameterized measurements by name:

```cascode
measurement IntegratedInputNoise(Frequency from, Frequency to) : Vrms {
  NoiseSpectrum n_in = input_referred_noise(noise_ac, ac, IN, OUT)
  return n_in.Integrate(from, to)
}
```

Constraint usage:

```cascode
c_int = noise_bench::IntegratedInputNoise(from=10Hz, to=10MHz) <= 1uVrms
```

## Parameterized benches

Benches may declare parameters. Bindings can specialize a bench by passing arguments before `::`:

```cascode
c_swing = tran_bench(stim_freq=1kHz)::OutputSwing() at net::OUT >= 0.4V
```

This is commonly used for transient benches where the stimulus frequency or amplitude is a bench
parameter rather than a circuit property.

## Harness primitives in bench `fill {}` blocks

The current toolchain recognizes a small set of harness primitives (see Chapter 4):

- `GND`, `VDC`, `VAC`, `VSIN`, `Impedor` / `Impedance`

When writing benches, prefer these primitives rather than trying to encode simulator-specific syntax
directly.

## Worked example: RC lowpass bench and circuit

The shortest complete reference is `tests/golden/cas/bench/RcLowpass.el.cai`. The README includes a
slightly condensed copy of that file to demonstrate end-to-end authoring.
