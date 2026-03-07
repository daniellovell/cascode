# RFC-0007: S-Parameter Analysis

Status: Draft
Authors: Claude (proposed), Titan Yuan (review)
Created: 2026-02-25
Last Updated: 2026-03-07
Target Version: Cascode 4.x

---

## Abstract

This RFC proposes S-parameter support within Cascode's bench system. The design introduces `SPAnalysis`, `SParameterMatrix`, and a `Port` harness primitive that is instantiated in bench wiring just like `VDC` or `Impedor`.

Ports are single-ended by definition. Each `Port` provides an S-parameter source/termination point with explicit port number, reference impedance, and DC bias voltage.

---

## 1. Port Harness Primitive

### 1.1 Primitive Form

S-parameter reference planes are modeled as harness primitive instances in `fill {}` (or bench `bind {}`), not as a new terminal role.

```cascode
Port <instance> = new Port(N=<integer>, Z=<Impedance>, V=<Voltage>) {
  .P--<signal_net>
  .N--<reference_net>
}
```

Parameters are required:

| Parameter | Type | Meaning |
| --- | --- | --- |
| `N` | integer | S-parameter port index (`1..Nports`) |
| `Z` | `Impedance` | Port reference impedance (`z0`) |
| `V` | `Voltage` | DC source value for the port source |

`Z` is collapsed to its resistive component at emission time. If the impedance is a parallel
composite (for example `1GOhm || 15pF`), only the resistive terms contribute to `z0`; reactive
elements are discarded.

Pins:

| Pin | Meaning |
| --- | --- |
| `.P` | Signal side of the port |
| `.N` | Reference side of the port |

### 1.2 Port Numbering and Validation

Port numbers must be positive, unique within a bench, and sequential starting at 1. For example, a two-port bench must declare `N=1` and `N=2`.

The numbering determines matrix indexing, so `S.S(2, 1)` is the response at port 2 due to excitation at port 1.

### 1.3 Single-Ended Semantics

`Port` is intentionally single-ended. The typical usage ties `.N` to a ground net through `GND` and connects `.P` to a bench terminal net that is bound to the DUT.

```cascode
fill {
  net gnd : ground
  GND g = new GND() { .GND--gnd }

  Port p1 = new Port(N=1, Z=50Ohm, V=1V) {
    .P--P1
    .N--gnd
  }
}
```

### 1.4 Interaction with `stim` and `resp`

Bench interface terminals remain `stim` and `resp`. `Port` instances connect to those terminal nets in `fill {}` and provide the S-parameter behavior at simulation time.

---

## 2. SPAnalysis

### 2.1 Declaration

`SPAnalysis` is declared in `analysis {}` like other analysis types:

```cascode
analysis {
  SPAnalysis sp = new SPAnalysis(
    space=Log,
    samples=200,
    start=100MHz,
    stop=10GHz,
    noise=1)
}
```

### 2.2 Parameters

| Parameter | Type           | Required | Default | Description                  |
| --------- | -------------- | -------- | ------- | ---------------------------- |
| `start`   | `Frequency`    | yes      | —       | Start frequency of the sweep |
| `stop`    | `Frequency`    | yes      | —       | Stop frequency of the sweep  |
| `space`   | `Log` or `Lin` | no       | `Log`   | Frequency spacing            |
| `samples` | integer        | no       | 100     | Number of frequency points   |
| `noise`   | `0` or `1`     | no       | `0`     | Enable correlated noise parameter computation during the sweep, including `NF` extraction |

Like other analyses, arguments may use expressions over `constraints` and `env`.

### 2.3 Port Discovery

`SPAnalysis` consumes `Port` primitive instances from the compiled bench harness used for emission.
There is no additional analysis argument for selecting ports.

Current semantic validation requires at least one `Port` instance in the bench `fill {}` block.
For each discovered port, the runtime reads `N`, `Z`, and `V`, validates numbering, and configures
the simulator.

### 2.4 Simulation Semantics

For an N-port bench, the simulator performs N single-port excitations. On each run, one port is excited while all ports are terminated at their declared reference impedances, and the resulting wave quantities are assembled into `S(i,j)`.

Because ports are explicit harness components, the bench definition is responsible for providing them; `SPAnalysis` does not synthesize hidden port sources.

---

## 3. SParameterMatrix

### 3.1 Constructor

The `sparam` constructor has the following signature:

```
sparam(SPAnalysis) → SParameterMatrix
```

`sparam` extracts matrix results from a completed `SPAnalysis`:

```cascode
SParameterMatrix S = sparam(sp)
```

It is a semantic error if the argument does not reference a declared `SPAnalysis`.

### 3.2 Element Access

Each `S.S(i, j)` element is a `TransferFunction` over frequency:

```cascode
TransferFunction s21 = S.S(2, 1)
TransferFunction s11 = S.S(1, 1)

GainSpectrum magS21 = db20(s21.Mag())
Phase phaseAt1g = s21.Phase().ValueAt(1GHz)
```

The index order follows standard convention: response index first, excitation index second.

---

## 4. Derived Metric Methods

`SParameterMatrix` exposes derived RF metrics as frequency-domain results.

### 4.1 Return Loss and VSWR

```
S.ReturnLoss(port) → GainSpectrum
S.VSWR(port) → ScalarSpectrum
```

Return loss at port `n` is `-20*log10(|Snn|)`. VSWR uses `Gamma = Snn` and `(1 + |Gamma|)/(1 - |Gamma|)`.

### 4.2 Insertion Loss and Isolation

```
S.InsertionLoss(to, from) → GainSpectrum
S.Isolation(to, from) → GainSpectrum
```

Both methods use `-20*log10(|Sij|)` with response-first argument ordering.

### 4.3 Stability Factors

```
S.StabilityK() → ScalarSpectrum
S.MuFactor() → ScalarSpectrum
```

These are defined only for two-port networks.

### 4.4 Maximum Gain

```
S.MSG() → GainSpectrum
S.MAG() → GainSpectrum
```

`MAG` uses the standard two-port expression and falls back to `MSG` where `K < 1`.
The output is given in linear units.

### 4.5 Group Delay

```
S.GroupDelay(to, from) → TimeSpectrum
```

Group delay uses the phase derivative of `Sij` with respect to angular frequency.

### 4.6 Noise Figure

```
S.NF() → GainSpectrum
S.NFmin() → GainSpectrum
S.Rn() → ImpedanceSpectrum
```

When `SPAnalysis(noise=1)` is enabled, the following noise parameters are available:
- `S.NF()` returns the sampled noise figure values in dB.
- `S.NFmin()` returns the minimum noise figure in dB.
- `S.Rn()` returns unnormalized input noise resistance in Ohms.

---

## 5. Complete Examples

### 5. Two-Port S-Parameter Bench

```cascode
library lib.std.bench

bench TwoPortSParamNoise {
  resp P1 : analog
  resp P2 : analog

  fill {
    net gnd : ground

    GND _ = new GND() {
      .GND--gnd
    }

    Port port1 = new Port(N=1, Z=50Ohm, V=env.InputCommonModeRange) {
      .P--P1
      .N--gnd
    }

    Port port2 = new Port(N=2, Z=50Ohm, V=env.OutputCommonModeRange) {
      .P--P2
      .N--gnd
    }
  }

  analysis {
    SPAnalysis sp = new SPAnalysis(
      space=Log,
      samples=100,
      start=(if constraints.HighpassBandwidth { constraints.HighpassBandwidth * 0.1 } else { 1Hz }),
      stop=(if constraints.GainBandwidth { constraints.GainBandwidth * 10 } else { 10GHz }),
      noise=1)
  }

  measurements {
    measurement S21(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, 1).Mag()).ValueAt(f)
    }

    measurement S21(Frequency from, Frequency to) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, 1).Mag()).From(from).To(to)
    }

    measurement ForwardGain(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, 1).Mag()).ValueAt(f)
    }

    measurement ForwardGain(Frequency from, Frequency to) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, 1).Mag()).From(from).To(to)
    }

    measurement S11(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.S(1, 1).Mag()).ValueAt(f)
    }

    measurement S11(Frequency from, Frequency to) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.S(1, 1).Mag()).From(from).To(to)
    }

    measurement InputReturnLoss(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return S.ReturnLoss(1).ValueAt(f)
    }

    measurement InputReturnLoss(Frequency from, Frequency to) : dB {
      SParameterMatrix S = sparam(sp)
      return S.ReturnLoss(1).From(from).To(to)
    }

    measurement S22(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, 2).Mag()).ValueAt(f)
    }

    measurement S22(Frequency from, Frequency to) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, 2).Mag()).From(from).To(to)
    }

    measurement OutputReturnLoss(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return S.ReturnLoss(2).ValueAt(f)
    }

    measurement OutputReturnLoss(Frequency from, Frequency to) : dB {
      SParameterMatrix S = sparam(sp)
      return S.ReturnLoss(2).From(from).To(to)
    }

    measurement S12(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.S(1, 2).Mag()).ValueAt(f)
    }

    measurement S12(Frequency from, Frequency to) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.S(1, 2).Mag()).From(from).To(to)
    }

    measurement ReverseIsolation(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return S.Isolation(1, 2).ValueAt(f)
    }

    measurement ReverseIsolation(Frequency from, Frequency to) : dB {
      SParameterMatrix S = sparam(sp)
      return S.Isolation(1, 2).From(from).To(to)
    }

    measurement InputVSWR(Frequency f) : Scalar {
      SParameterMatrix S = sparam(sp)
      return S.VSWR(1).ValueAt(f)
    }

    measurement InputVSWR(Frequency from, Frequency to) : Scalar {
      SParameterMatrix S = sparam(sp)
      return S.VSWR(1).From(from).To(to)
    }

    measurement StabilityK(Frequency f) : Scalar {
      SParameterMatrix S = sparam(sp)
      return S.StabilityK().ValueAt(f)
    }

    measurement MaxAvailableGain(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.MAG()).ValueAt(f)
    }

    measurement ForwardGroupDelay(Frequency f) : s {
      SParameterMatrix S = sparam(sp)
      return S.GroupDelay(2, 1).ValueAt(f)
    }

    measurement NoiseFigure(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return S.NF().ValueAt(f)
    }

    measurement NoiseFigure(Frequency from, Frequency to) : dB {
      SParameterMatrix S = sparam(sp)
      return S.NF().From(from).To(to)
    }

    measurement MinNoiseFigure(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return S.NFmin().ValueAt(f)
    }

    measurement NoiseResistance(Frequency f) : Ohm {
      SParameterMatrix S = sparam(sp)
      return S.Rn().ValueAt(f)
    }
  }
}
```

Mixed-mode S-parameters for differential ports can be derived from single-ended S-parameters.
This is now implemented in the standard library as `TwoPortMixedModeSParam`, which computes
`Sdd`, `Sdc`, `Scd`, and `Scc` terms from four single-ended `Port` instances.

### 5.2 Interface Binding

S-parameter benches are bound like other benches. Bench terminals are mapped to DUT terminals; the bench's `Port` instances already sit on those terminal nets.

```cascode
interface SingleEndedAmp {
  supply VDD
  ground GND
  input IN : analog
  output OUT : analog

  ...

  benches {
    bind TwoPortSParamNoise as sparam_bench {
      bench.P1--dut.IN
      bench.P2--dut.OUT
    }
  }
}
```

Constraints reference measurements through the bind name:

```cascode
constraints {
  numeric {
    c_s21 = sparam_bench::S21(f=2.4GHz) >= 15dB
    c_s11 = sparam_bench::S11(f=2.4GHz) <= -10dB
    c_k   = sparam_bench::StabilityK(f=2.4GHz) >= 1
  }
}
```

---

## 6. Grammar and Recognition Changes

### 6.1 SPAnalysis Type

`SPAnalysis` is added to analysis type alternatives:

```antlr
analysisType
    : AC_ANALYSIS_TYPE
    | DC_ANALYSIS_TYPE
    | TRAN_ANALYSIS_TYPE
    | NOISE_ANALYSIS_TYPE
    | STB_ANALYSIS_TYPE
    | SP_ANALYSIS_TYPE
    ;

SP_ANALYSIS_TYPE : 'SPAnalysis' ;
```

### 6.2 SParameterMatrix Type

`SParameterMatrix` is added to the physical type alternatives:

```antlr
physicalType
    : // ... existing types ...
    | S_PARAMETER_MATRIX_TYPE
    ;

S_PARAMETER_MATRIX_TYPE : 'SParameterMatrix' ;
```

### 6.3 Harness Primitive Recognition

No new terminal declaration grammar is needed. `Port` is recognized as a harness primitive type name, consistent with `GND`, `VDC`, `VAC`, `VSIN`, and `Impedance`/`Impedor`.

Runtime and linker primitive lists must therefore include `Port` (for example in `IsHarnessPrimitive` and in bench harness element compilation).

---

## 7. Error Conditions

### 7.1 Semantic Errors

| Condition | Error |
| --- | --- |
| `sparam()` called with wrong arity | `CAS2010: sparam requires exactly 1 argument, got {count}.` |
| `sparam()` argument is not `SPAnalysis` | `CAS2011: sparam first argument must be an SPAnalysis, got '{type}'.` |
| Non-real-valued port impedance | `Port impedance must be real-valued: invalid port impedance on port {n}` |
| Duplicate port number in a bench | `Duplicate port number {n}: '{nameA}' and '{nameB}'` |
| Port number <= 0 | `Port number must be a positive integer; got {n}` |
| Non-sequential port numbering | `Incorrect port ordering, ports must be numbered sequentially from 1` |
| `SPAnalysis` with no `Port` in bench `fill {}` | `SPAnalysis requires at least one Port instance.` |
| Two-port-only S-parameter method on N != 2 ports | `{method} is defined for 2-port networks only; bench declares {n} ports.` |

### 7.2 Runtime Errors

| Condition | Behavior |
| --- | --- |
| `MAG` where `K < 1` | Falls back to `MSG` numerically (no separate diagnostic required) |
| `NF` / `NFmin` / `Rn` requested without `SPAnalysis(noise=1)` | Throws runtime error indicating SP noise data is unavailable |

## 8. Implementation Plan

1. Update language/runtime recognition, so `Port` is treated as a harness primitive in bench compilation and linking.
2. Extend bench harness element compilation and testbench emission to map `Port(N, Z, V)` to ngspice `portnum`/`z0` source cards.
3. Implement `SPAnalysis` execution and result extraction to consume discovered `Port` instances.
4. Add and update unit and integration coverage, including stress golden outputs (for example `CSAmp_Resistive_Sky130_sparam_bench.sp`) to verify emitted port cards and matrix data extraction.
