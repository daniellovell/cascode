# RFC-0007: S-Parameter Analysis

Status: Draft
Authors: Claude (proposed), Titan Yuan (review)
Created: 2026-02-25
Last Updated: 2026-02-25
Target Version: Cascode 4.x

---

## Abstract

This RFC proposes S-parameter support within Cascode's bench system. The design introduces a new terminal role (`port`), a new analysis type (`SPAnalysis`), and a new compound result type (`SParameterMatrix`) with methods for accessing raw and derived S-parameter data.

The design builds on the bench constructs defined in the [bench system spec](../../spec/language/Ch04_Bench_System.md). Familiarity with bench declarations, fill blocks, analysis blocks, and measurement expressions is assumed.

---

## 1. Port Terminals

### 1.1 The `port` Role

S-parameter analysis requires bidirectional terminals: each port is simultaneously a potential stimulus and response. The existing `stim` and `resp` roles express unidirectional intent and are not appropriate for this purpose. The `port` role captures the bidirectional semantics of an S-parameter reference plane.

A port declaration specifies three required elements and one optional element:

```cascode
port <number> <name> : <type>
port <number> <name> : <type> = <impedance>
```

The port number is a positive integer that determines the S-parameter index. Port 1 corresponds to the first index in S-parameter notation, so `S.S(2, 1)` denotes the transmission from port 1 to port 2. Port numbers need not be contiguous but must be unique within a bench.

The port name follows the same scoping rules as `stim`/`resp` terminals: it is available by name in the bench's fill block, helper functions, and measurement bodies. If the port type is a bundle, the name exposes the bundle's leaf terminals (for example, `RF_IN.P`, `RF_IN.N`).

Examples:

```cascode
port 1 P1 : analog
port 2 P2 : analog = 75Ohm
port 1 RF_IN : Diff
port 2 RF_OUT : Diff = 150Ohm
```

### 1.2 Port Types

Port terminals accept the same type system as `stim`/`resp` terminals: any built-in domain keyword or user-defined bundle name. The port's type determines its default reference impedance and its mixed-mode behavior.


| Type category                       | Default Z₀ | Mixed-mode                                      |
| ----------------------------------- | ---------- | ----------------------------------------------- |
| Single-ended (`analog`, `rf`, etc.) | 50Ω        | No — produces scalar Sij                        |
| Differential bundle (`Diff`)        | 100Ω       | Yes — automatically produces Sdd, Sdc, Scd, Scc |


The default impedance applies when no explicit impedance is given in the port declaration. A port with a `Diff` type defaults to 100Ω (the standard differential impedance, representing two 50Ω single-ended legs).

### 1.3 Port Impedance Override

An explicit impedance value after `=` overrides the type-driven default:

```cascode
port 1 P1 : analog = 75Ohm       // 75Ω instead of the default 50Ω
port 2 P2 : Diff = 200Ohm        // 200Ω differential instead of 100Ω
```

The impedance must be a real-valued resistance. Complex or frequency-dependent port impedances are not supported; such cases require manual termination in the fill block.

### 1.4 Interaction with `stim` and `resp`

A bench may declare both `port` terminals and `stim`/`resp` terminals. The `port` terminals participate in S-parameter analysis; the `stim`/`resp` terminals are available for other analyses within the same bench (for example, a noise analysis that requires an explicit stimulus terminal). Port terminals may also be passed to non-S-parameter measurement primitives where a terminal reference is expected.

---

## 2. SPAnalysis

### 2.1 Declaration

`SPAnalysis` is declared in the `analysis {}` block, following the same constructor syntax as other analysis types:

```cascode
analysis {
  SPAnalysis sp = new SPAnalysis(
    space=Log,
    samples=200,
    start=100MHz,
    stop=10GHz)
}
```

### 2.2 Parameters


| Parameter | Type           | Required | Default | Description                  |
| --------- | -------------- | -------- | ------- | ---------------------------- |
| `start`   | `Frequency`    | yes      | —       | Start frequency of the sweep |
| `stop`    | `Frequency`    | yes      | —       | Stop frequency of the sweep  |
| `space`   | `Log` or `Lin` | no       | `Log`   | Frequency spacing            |
| `samples` | integer        | no       | 100     | Number of frequency points   |


Like other analysis types, parameters may include conditional expressions referencing `constraints` or `env`.

### 2.3 Port Discovery

The `SPAnalysis` operates on all `port` terminals declared in the bench. There is no explicit parameter linking the analysis to specific ports. The runtime discovers all port declarations, assigns each port its declared (or default) reference impedance, and configures the simulation accordingly.

### 2.4 Simulation Semantics

S-parameter simulation follows the standard multiport formulation. For an N-port bench, the simulator performs N independent analyses, one per port. In each analysis, the excited port is driven by its port source while all other ports are terminated in their reference impedances. The incident and reflected wave amplitudes at each port are collected and assembled into the S-parameter matrix.

The bench fill block provides DC bias, coupling networks, and any other circuit elements required for the operating point. The fill block does not need to provide port excitation sources or termination impedances — the `SPAnalysis` runtime handles those based on the port declarations.

---

## 3. SParameterMatrix

### 3.1 Constructor

The `sparam` constructor function extracts the full S-parameter matrix from a completed `SPAnalysis`:

```cascode
SParameterMatrix S = sparam(sp)
```

The result is a frequency-indexed matrix of complex-valued S-parameters. All subsequent access methods operate on this object.

### 3.2 Element Access

Individual S-parameters are accessed by port number. Each element is a `TransferFunction` (a complex-valued function of frequency) supporting `.Mag()` and `.Phase()`. Converting to a real-valued spectrum first makes the full set of spectrum methods available — `.ValueAt()`, `.FindCrossing()`, and so on.

```cascode
TransferFunction s21 = S.S(2, 1)    // forward transmission
TransferFunction s11 = S.S(1, 1)    // input reflection

GainSpectrum mag_s21 = db20(s21.Mag())
Phase phase_s21_at_1g = s21.Phase().ValueAt(1GHz)
```

The index pair follows standard S-parameter convention: `S.S(i, j)` is the response at port *i* due to excitation at port *j*.

### 3.3 Mixed-Mode S-Parameters

When a port is declared with a `Diff` type, the runtime automatically computes mixed-mode (differential/common) S-parameters from the underlying single-ended data. Four mixed-mode accessors are provided:


| Accessor      | Meaning                                            |
| ------------- | -------------------------------------------------- |
| `S.Sdd(i, j)` | Differential-mode to differential-mode             |
| `S.Sdc(i, j)` | Common-mode to differential-mode (mode conversion) |
| `S.Scd(i, j)` | Differential-mode to common-mode (mode conversion) |
| `S.Scc(i, j)` | Common-mode to common-mode                         |


Each accessor returns a `TransferFunction`. The indices refer to port numbers, not individual single-ended nodes.

```cascode
port 1 RF_IN : Diff
port 2 RF_OUT : Diff

// ...

measurements {
  measurement DiffGain(Frequency f) : dB {
    SParameterMatrix S = sparam(sp)
    return db20(S.Sdd(2, 1).Mag()).ValueAt(f)
  }

  measurement ModeConversion(Frequency f) : dB {
    SParameterMatrix S = sparam(sp)
    return db20(S.Sdc(2, 1).Mag()).ValueAt(f)
  }
}
```

Mixed-mode accessors are available only when both the response port *i* and the excitation port *j* are differential. Calling `S.Sdd(i, j)` when either port is single-ended is a semantic error. For benches mixing single-ended and differential ports, use `S.S(i, j)` for the single-ended parameters and the mixed-mode accessors for the differential pairs.

### 3.4 Mixed-Mode Conversion

The mixed-mode S-parameters are computed from the single-ended S-parameters using the standard transformation. For a differential port with single-ended legs *a* and *b*:

$$S_{dd} = \tfrac{1}{2}(S_{aa} - S_{ab} - S_{ba} + S_{bb})$$
$$S_{dc} = \tfrac{1}{2}(S_{aa} + S_{ab} - S_{ba} - S_{bb})$$
$$S_{cd} = \tfrac{1}{2}(S_{aa} - S_{ab} + S_{ba} - S_{bb})$$
$$S_{cc} = \tfrac{1}{2}(S_{aa} + S_{ab} + S_{ba} + S_{bb})$$

This conversion is applied pointwise across all frequencies in the sweep.

---

## 4. Derived Metric Methods

`SParameterMatrix` exposes methods for commonly used derived RF metrics. Each method returns a frequency-domain result (a spectrum or transfer function), allowing the caller to evaluate at a specific frequency, find crossings, or extract extrema using the standard spectrum methods.

All of these derived metric methods assume single-ended ports, and calling these derived metric methods on differential ports is a semantic error.

### 4.1 Return Loss and VSWR

```
S.ReturnLoss(port) → GainSpectrum
S.VSWR(port) → GainSpectrum
```

Return loss at port *n* is computed as $-20 \log_{10} |S_{nn}|$ (a positive quantity in dB for a passive, well-matched port). VSWR is $(1 + |\Gamma|) / (1 - |\Gamma|)$ where $\Gamma = S_{nn}$.

```cascode
measurement InputReturnLoss(Frequency f) : dB {
  SParameterMatrix S = sparam(sp)
  return S.ReturnLoss(1).ValueAt(f)
}

measurement InputVSWR(Frequency f) : Scalar {
  SParameterMatrix S = sparam(sp)
  return S.VSWR(1).ValueAt(f)
}
```

### 4.2 Insertion Loss and Isolation

```
S.InsertionLoss(to, from) → GainSpectrum
S.Isolation(to, from) → GainSpectrum
```

Insertion loss from port *j* to port *i* is $-20 \log_{10} |S_{ij}|$. Isolation uses the same formula but conventionally refers to the reverse path (for example, output to input in an amplifier). Both return positive dB values for attenuating paths. The argument order matches the S-parameter convention: response port first, excitation port second.

### 4.3 Stability Factors

```
S.RolletK() → GainSpectrum
S.MuFactor() → GainSpectrum
```

The Rollet stability factor *K* and the Edwards-Sinsky *μ* factor are defined for 2-port networks. Calling these methods on an `SParameterMatrix` with more than two ports is a semantic error.

Rollet *K*:
$$K = \frac{1 - |S_{11}|^2 - |S_{22}|^2 + |\Delta|^2}{2|S_{12}||S_{21}|}$$

where $\Delta = S_{11}S_{22} - S_{12}S_{21}$.

Unconditional stability requires $K > 1$ and $|\Delta| < 1$.

Edwards-Sinsky *μ*:
$$\mu = \frac{1 - |S_{11}|^2}{|S_{22} - \Delta S_{11}^*| + |S_{12}S_{21}|}$$

Unconditional stability requires $\mu > 1$.

```cascode
measurement StabilityK(Frequency f) : Scalar {
  SParameterMatrix S = sparam(sp)
  return S.RolletK().ValueAt(f)
}
```

### 4.4 Maximum Gain

```
S.MSG() → GainSpectrum
S.MAG() → GainSpectrum
```

Maximum stable gain (MSG) and maximum available gain (MAG) are 2-port quantities.

$$\text{MSG} = \frac{|S_{21}|}{|S_{12}|}$$

$$\text{MAG} = \frac{|S_{21}|}{|S_{12}|} \left( K - \sqrt{K^2 - 1} \right)$$

MAG is defined only where $K \geq 1$. At frequencies where the device is potentially unstable ($K < 1$), `MAG` evaluates to MSG (the conventional gain boundary on a gain-frequency plot).

### 4.5 Group Delay

```
S.GroupDelay(to, from) → GainSpectrum
```

Group delay from port *j* to port *i* is the negative derivative of the phase of $S_{ij}$ with respect to angular frequency:

$$\tau_g = -\frac{d\phi_{ij}}{d\omega}$$

The result is a spectrum with time-valued samples. Despite being time-valued, the return type is `GainSpectrum` (a frequency-domain curve) so that `.ValueAt(f)` returns the group delay at a specific frequency.

```cascode
measurement ForwardGroupDelay(Frequency f) : Time {
  SParameterMatrix S = sparam(sp)
  return S.GroupDelay(2, 1).ValueAt(f)
}
```

---

## 5. Complete Examples

### 5.1 Two-Port LNA Bench

```cascode
library lib.std.bench

bench TwoPortSParam {
  port 1 P1 : analog
  port 2 P2 : analog

  fill {
    net gnd : ground
    GND _ = new GND() { .GND--gnd }
  }

  analysis {
    SPAnalysis sp = new SPAnalysis(
      space=Log,
      samples=200,
      start=100MHz,
      stop=10GHz)
  }

  measurements {
    measurement S21(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.S(2, 1).Mag()).ValueAt(f)
    }

    measurement InputReturnLoss(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return S.ReturnLoss(1).ValueAt(f)
    }

    measurement OutputReturnLoss(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return S.ReturnLoss(2).ValueAt(f)
    }

    measurement ReverseIsolation(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return S.Isolation(1, 2).ValueAt(f)
    }

    measurement InputVSWR(Frequency f) : Scalar {
      SParameterMatrix S = sparam(sp)
      return S.VSWR(1).ValueAt(f)
    }

    measurement StabilityK(Frequency f) : Scalar {
      SParameterMatrix S = sparam(sp)
      return S.RolletK().ValueAt(f)
    }

    measurement MaxAvailableGain(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.MAG()).ValueAt(f)
    }

    measurement ForwardGroupDelay(Frequency f) : Time {
      SParameterMatrix S = sparam(sp)
      return S.GroupDelay(2, 1).ValueAt(f)
    }
  }
}
```

### 5.2 Differential Amplifier Bench (Mixed-Mode)

```cascode
library lib.std.bench

bench DiffSParam {
  port 1 RF_IN : Diff
  port 2 RF_OUT : Diff

  fill {
    net gnd : ground
    net vcm_in : analog
    net vcm_out : analog

    GND _ = new GND() { .GND--gnd }

    VDC biasIn = new VDC(V=env.InputCommonModeRange) {
      .P--vcm_in
      .N--gnd
    }
    VDC biasOut = new VDC(V=env.OutputCommonModeRange) {
      .P--vcm_out
      .N--gnd
    }

    RF_IN.P--vcm_in
    RF_IN.N--vcm_in
    RF_OUT.P--vcm_out
    RF_OUT.N--vcm_out
  }

  analysis {
    SPAnalysis sp = new SPAnalysis(
      space=Log,
      samples=200,
      start=100MHz,
      stop=10GHz)
  }

  measurements {
    measurement DiffForwardGain(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.Sdd(2, 1).Mag()).ValueAt(f)
    }

    measurement DiffReturnLossIn(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.Sdd(1, 1).Mag()).ValueAt(f)
    }

    measurement ModeConversion(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.Sdc(2, 1).Mag()).ValueAt(f)
    }

    measurement CommonModeRejection(Frequency f) : dB {
      SParameterMatrix S = sparam(sp)
      return db20(S.Sdd(2, 1).Mag()).ValueAt(f) - db20(S.Sdc(2, 1).Mag()).ValueAt(f)
    }
  }
}
```

### 5.3 Interface Binding

S-parameter benches are bound to circuits and interfaces using the same `bind` syntax as other benches. Port terminals are mapped to DUT terminals with `bench.<port_name>--dut.<terminal>`:

```cascode
interface LNA {
  supply VDD
  ground VSS
  input RF_IN : analog
  output RF_OUT : analog

  benches {
    bind TwoPortSParam as sparam_bench {
      bench.P1--dut.RF_IN
      bench.P2--dut.RF_OUT

      GND localGnd = new GND()
      VDC dc = new VDC(V=harness.VDD) {
        .P--dut.VDD
        .N--localGnd
      }
      dut.VSS--localGnd
    }
  }
}
```

Constraints reference measurements through the binding name, supplying the frequency argument:

```cascode
circuit MyLNA implements LNA {
  level EL

  // ...

  constraints {
    numeric {
      c_gain = sparam_bench::ForwardGain(f=2.4GHz) >= 15dB
      c_s11  = sparam_bench::InputReturnLoss(f=2.4GHz) >= 10dB
      c_s22  = sparam_bench::OutputReturnLoss(f=2.4GHz) >= 10dB
      c_k    = sparam_bench::StabilityK(f=2.4GHz) >= 1
    }
  }
}
```

---

## 6. Grammar Extensions

### 6.1 Port Declaration

The port terminal declaration adds a new alternative to the existing terminal declaration rule:

```antlr
terminalDecl
    : terminalRole IDENT COLON terminalType                    // existing stim/resp
    | PORT_KW INTEGER IDENT COLON terminalType portImpedance?  // new port role
    ;

portImpedance
    : EQ QUANTITY    // e.g., = 75Ohm
    ;
```

### 6.2 SPAnalysis Type

`SPAnalysis` is added to the analysis type alternatives:

```antlr
analysisType
    : AC_ANALYSIS_TYPE
    | DC_ANALYSIS_TYPE
    | TRAN_ANALYSIS_TYPE
    | NOISE_ANALYSIS_TYPE
    | STB_ANALYSIS_TYPE
    | SP_ANALYSIS_TYPE        // new
    ;

SP_ANALYSIS_TYPE : 'SPAnalysis' ;
```

### 6.3 SParameterMatrix Type

`SParameterMatrix` is added to the physical type alternatives:

```antlr
physicalType
    : // ... existing types ...
    | S_PARAMETER_MATRIX_TYPE
    ;

S_PARAMETER_MATRIX_TYPE : 'SParameterMatrix' ;
```

### 6.4 New Lexer Token

```antlr
PORT_KW : 'port' ;
```

Note: `port` already exists as a keyword in the Cascode grammar for circuit terminal declarations. The parser distinguishes the bench port role from the circuit terminal keyword by context (bench body vs. circuit body).

---

## 7. Error Conditions

### 7.1 Semantic Errors


| Condition                                  | Error                                                                              |
| ------------------------------------------ | ---------------------------------------------------------------------------------- |
| Non-real-valued port impedance             | `Port impedance must be real-valued: port {n} declares a complex impedance`        |
| Duplicate port number in a bench           | `Duplicate port number {n}: '{name1}' and '{name2}'`                               |
| Mixed-mode accessor on single-ended port   | `S.Sdd({i}, {j}) requires both ports to be differential; port {n} is single-ended` |
| Derived metric method on differential port | `{method} can only be called on single-ended ports: port {n} is differential`      |
| Stability/gain method on N > 2 ports       | `S.RolletK() is defined for 2-port networks only; bench declares {n} ports`        |
| Port number ≤ 0                            | `Port number must be a positive integer; got {n}`                                  |
| SPAnalysis with no port terminals          | `SPAnalysis requires at least one port terminal declaration`                       |


### 7.2 Runtime Errors


| Condition                         | Behavior                                                                |
| --------------------------------- | ----------------------------------------------------------------------- |
| MAG where $K < 1$                 | Falls back to MSG with a diagnostic note                                |
| Group delay numerical instability | Warning: `Group delay computation may be inaccurate near frequency {f}` |


---

## 8. Future Work

Per-port impedance specified through the environment block (enabling runtime configuration of reference impedances without modifying the bench definition) is deferred to a future revision. The current design requires impedance to be a compile-time constant in the port declaration.

Support for noise figure extraction from S-parameter and noise data (combined `SPAnalysis` + `NoiseAnalysis` bench) is a natural extension and may be specified in a future RFC.

---

## 9. Implementation Plan

Since this RFC cuts across multiple components of the system, requiring changes to the grammar, AST, semantic checker, runtime, and backend integration, the implementation is split into two parts to better manage the implementation.

1. In the first phase, the language and bench definition will be updated to reflect the changes in this RFC. This includes updating the parser, the semantics, and the bench definition. The [core concepts spec](../../spec/language/Ch02_Core_Concepts.md) and the [syntax reference](../../spec/language/Ch03_Syntax_Reference.md) will be updated to reflect the new S-parameter analyses and result syntax. Lastly, the [bench system spec](../../spec/language/Ch04_Bench_System.md) will be updated to reflect the new S-parameter benches.
2. The backend will be updated to run S-parameter analysis with ngspice, which is possible according to https://ngspice.sourceforge.io/docs/ngspice-html-manual/manual.xhtml#magicparlabel-23387.
