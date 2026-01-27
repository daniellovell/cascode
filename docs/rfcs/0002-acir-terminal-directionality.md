# RFC: ACIR Terminal Directionality

Status: Draft  
Authors: Daniel Lovell
Created: 2026-01-27  
Last Updated: 2026-01-27  
Target Version: ACIR 3.0

---

## Abstract

This RFC proposes making ACIR terminal declarations directional by replacing the single `port` keyword with `input`, `output`, and `io`. The goal is to preserve interface intent (already present in Cascode source) into ACIR so tools can perform correctness checks without relying on naming conventions. `supply` and `ground` declarations remain unchanged.

This RFC is intentionally orthogonal to RFC 0000 (ACIR measurement abstraction). RFC 0000 can assume either the legacy `port` form or the directional form defined here.

---

## 1. Problem Statement

ACIR `port` declarations do not encode whether a terminal is intended to be driven by the environment, driven by the circuit, or used bidirectionally. This makes it difficult to write reliable validation rules (and higher-level adapter layers) without relying on heuristics, naming conventions, or external metadata.

As a practical consequence:

- Harness entries such as `source IN ...` and `load OUT ...` can only be validated weakly today (by assuming “IN” is an input, “OUT” is an output, etc.).
- Composition checks (especially for `digital` nets) cannot be expressed cleanly because ACIR does not know which terminals are expected to drive a shared net.

---

## 2. Goals and Non-Goals

Goals:

1. Encode terminal directionality in ACIR in a way that is easy to parse and diff.
2. Enable deterministic validation of harness intent (sources, loads, and bias settings) against a circuit’s declared interface.
3. Provide enough information for downstream tools to detect obvious wiring mistakes (e.g., output-to-output on digital nets) without “guessing by name”.

Non-goals:

1. Change electrical semantics. Directionality is interface intent metadata; it does not model one-way electrical behavior.
2. Encode bench stimulus legality. Benches may drive “outputs” as part of impedance measurements; directionality must not prohibit valid bench topologies.

---

## 3. Proposal

### 3.1 Direction keywords

#### 3.1.1 Syntax

```ebnf
portDecl   = direction IDENT ":" typeSpec ;
direction  = "input" | "output" | "io" ;
typeSpec   = domain | bundleType ;
domain     = "analog" | "bias" ;
bundleType = IDENT ;

supplyDecl = "supply" IDENT ;
groundDecl = "ground" IDENT ;
```

#### 3.1.2 Semantics

| Keyword | Direction | Typical Use |
|---------|-----------|-------------|
| `input` | into circuit | Signal inputs, bias inputs |
| `output` | out of circuit | Signal outputs |
| `io` | bidirectional | I/O pads, transmission gates |
| `supply` | into circuit | Power rails (VDD, AVDD, etc.) |
| `ground` | into circuit | Ground references (GND, VSS, etc.) |

Notes:

- Direction applies to the declared port as a whole. For bundle ports (e.g., `Diff`), the direction applies uniformly to all expanded fields.
- Directionality describes the circuit boundary contract under normal composition. It does not imply “no current may flow” or similar electrical restrictions.
- `io` is intentionally short to avoid visual confusion with `input` in diffs and fixed-width listings (a motivating problem with `inout`).

#### 3.1.3 Examples

```acir
circuit OTA5T implements SingleEndedOpAmp
  level EL

  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog
  input VTAIL : bias
```

```acir
circuit FullyDiffOTA implements FullyDifferentialOpAmp
  level EL

  supply AVDD
  supply DVDD
  ground AVSS
  input IN : Diff
  output OUT : Diff
```

```acir
circuit TransmissionGate
  level EL

  supply VDD
  ground GND
  io A : analog
  io B : analog
  input EN : bias
```

---

## 4. Validation and Enforcement

Directionality is intended to be enforced primarily at ACIR validation boundaries where intent is otherwise ambiguous.

### 4.1 Harness validation (normative)

When a harness entry references a circuit terminal name (for example, `source IN ...`), tools SHOULD validate that the referenced name exists and that its direction is compatible with the harness entry:

- `source <Name> ...` MUST reference an `input` or `io` port.
- `load <Name> ...` MUST reference an `output` or `io` port.
- `bias <Name> = <Value>` MUST reference an `input` or `io` port with domain `bias`, or a `supply`/`ground` declaration (for example, `bias VDD = 1.8` referencing `supply VDD`).

Rationale: these checks catch common mis-wires (“loaded an input”, “drove an output”) without constraining benches that intentionally drive outputs for measurement.

### 4.2 Composition and net-driver checks (informative)

Tools MAY use directionality to detect obviously invalid compositions, especially for `digital` nets. A minimal driver model is:

- `output` ports are drivers.
- `input` ports are sinks.
- `io` ports may be both driver and sink.

Any stricter driver analysis (tri-state, wired-OR, analog multi-drive) is outside the scope of this RFC.

---

## 5. Grammar Changes

In the ACIR grammar, directional terminals replace `port` declarations:

```ebnf
portDecl = direction IDENT ":" typeSpec ;
direction = "input" | "output" | "io" ;
```

`supply` and `ground` declarations remain unchanged.

---

## 6. Migration from ACIR 2.x

This RFC introduces a breaking syntax change for terminal declarations.

| ACIR 2.x | Directional terminals | Migration |
|----------|------------------------|-----------|
| `port IN : Diff` | `input IN : Diff` | Choose the appropriate direction keyword |

Example:

```
# Before (ACIR 2.x)
port IN : Diff
port OUT : analog

# After (directional terminals)
input IN : Diff
output OUT : analog
```

---

## 7. Alternatives Considered

1. Keep `port` and add a `direction:` attribute.

This avoids a breaking change, but it is harder to scan and invites partial adoption (“direction missing on some ports”), which undermines the main value of the feature.

2. Infer direction by name conventions (IN/OUT/VDD/GND).

This is the status quo. It is brittle and fails on non-canonical naming.

---

## 8. Implementation Plan

1. Update the lexer with direction keywords (`input`, `output`, `io`).
2. Remove the `port` keyword in favor of directional declarations.
3. Persist directionality into ACIR (including in pretty-printer output) so roundtrips preserve the choice.
4. Add validation errors for harness-direction mismatches.
