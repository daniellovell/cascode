# RFC: ACIR Measurement Abstraction System

Status: Draft  
Authors: Daniel Lovell
Created: 2026-01-25  
Last Updated: 2026-01-25  
Target Version: ACIR 3.0

---

## Abstract

This RFC proposes a measurement abstraction system for ACIR that eliminates redundant bench definitions across circuit topologies while maintaining type safety and enabling automatic testbench synthesis. The design introduces:

1. Port abstraction: A two-node pair representing where signals are stimulated or measured, agnostic to voltage/current mode
2. Explicit bench bindings: Circuit-level bindings that map abstract measurement roles to concrete node pairs
3. Reusable measurement definitions: Specifications with typed role requirements and fixed primitive operations
4. Multi-simulation support: Fixed stimulus mode vocabulary for measurements requiring multiple simulation runs
5. Programmatic testbench generation: C# emission (eliminating template maintenance)

---

## 1. Problem Statement

### 1.1 Current State

The existing ACIR bench system requires separate builtin bench definitions for each circuit topology:

```acir
bench SEOpAmpACBench for SingleEndedOpAmp
  builtin SEOpAmpACBench
  outputs: GainBandwidth, PassbandGain, PhaseMargin, LowpassBandwidth

bench FDOpAmpACBench for FullyDifferentialOpAmp
  builtin FDOpAmpACBench
  outputs: GainBandwidth, PassbandGain, PhaseMargin, LowpassBandwidth
```

These benches measure identical specifications but require separate implementations because:

1. Stimulus generation differs (differential vs single-ended input drive)
2. Response probing differs (differential vs single-ended output measurement)
3. Some measurements are topology-dependent (CMRR requires independently-drivable input terminals)

### 1.2 Consequences

1. Code duplication: Measurement logic reimplemented in every bench template
2. Maintenance burden: Bug fixes require changes across multiple templates
3. Template explosion: N topologies x M backends x K measurements = O(N x M x K) templates
4. Manual applicability tracking: No compile-time validation of measurement applicability

### 1.3 Desired Properties

1. Single definition: Each measurement defined exactly once
2. Explicit binding: Measurements bind to circuit nodes through explicit declarations
3. Type-safe applicability: Topology requirements validated at compile time via Port types
4. No implicit behaviors: All bindings explicit; no context-dependent resolution rules
5. Programmatic generation: Testbench generation in C# code, not templates

---

## 2. Background

### 2.1 ACIR Trait System

Traits define interface contracts for circuit composition:

```acir
trait DiffPairLike:
  input IN : Diff
  output OUT : Diff

  connectors:
    to CurrentMirrorLike:
      OUT.P--SENSE
      OUT.N--TAP[0]
```

Traits specify port interfaces and connector rules. This RFC preserves traits for their original purpose (interface contracts, hierarchical composition) but removes measurement binding from traits.

### 2.2 ACIR Bundle System

Bundles group related nets:

```acir
bundle Diff:
  P : analog
  N : analog
```

### 2.3 Two-Port Network Model

A key insight informs this design: measurements operate on ports in the network-analysis sense, two-terminal access points where signals can be applied or observed.

| Circuit Type | Input Port | Output Port |
|--------------|------------|-------------|
| Voltage amplifier | Voltage driven, two terminals | Voltage measured, two terminals |
| TIA | Current driven, two terminals | Voltage measured, two terminals |
| Fully differential amp | Differential voltage, two terminals | Differential voltage, two terminals |

The Port abstraction captures "where" without prescribing "what kind" (voltage/current). A Port is always defined by two nodes: `Port(positive_node, negative_node)`.

### 2.4 Existing Harness System

The harness holds bench-only elements:

```acir
harness:
  supply VDD = 1.8V
  bias VBIAS = 0.7V
  source IN Z=50Ohm
  load OUT C=1pF
  pvt TT@27C, SS@-40C, FF@125C
```

This RFC preserves and extends this system.

---

## 3. Proposal Overview

### 3.1 Core Components

1. Direction keywords: `input`, `output`, `inout` replace the `port` keyword
2. Port abstraction: `Port(node_a, node_b)` defines a two-terminal measurement/stimulus point
3. Port types: `Port` (base), `DifferentialPort` (both nodes independently drivable)
4. Bench bindings: Circuit-level `bench_bindings:` block maps abstract roles to concrete Ports
5. Measurement definitions: Reusable specs with typed role requirements
6. Fixed primitive vocabulary: Well-defined operations for procedure expressions
7. Stimulus modes: Fixed vocabulary for multi-simulation measurements
8. Programmatic emission: C# generates testbenches directly

### 3.2 Design Philosophy

Explicit over implicit: All bindings are explicit. No context-dependent resolution rules.

Port as core abstraction: A Port is two nodes. The measurement system does not know or care whether the underlying circuit uses `Diff` bundles or `analog` scalars, it only sees node pairs.

Type-safe applicability: `DifferentialPort` vs `Port` determines measurement applicability at compile time. CMRR requires `DifferentialPort` for stimulus; a circuit binding `Port(IN, GND)` cannot satisfy this and triggers a compile error.

Fixed primitives, extensible later: The procedure DSL uses a fixed set of primitives with documented type signatures. This can be extended to a full expression language in future versions.

---

## 4. Detailed Design

### 4.1 Direction Keywords

#### 4.1.1 Syntax

```ebnf
portDecl   = direction IDENT ":" typeSpec ;
direction  = "input" | "output" | "inout" ;
typeSpec   = domain | bundleType ;
domain     = "analog" | "bias" ;
bundleType = IDENT ;

supplyDecl = "supply" IDENT ;
groundDecl = "ground" IDENT ;
```

#### 4.1.2 Semantics

| Keyword | Direction | Typical Use |
|---------|-----------|-------------|
| `input` | into circuit | Signal inputs, bias inputs |
| `output` | out of circuit | Signal outputs |
| `inout` | bidirectional | I/O pads, transmission gates |
| `supply` | into circuit | Power rails (VDD, AVDD, etc.) |
| `ground` | into circuit | Ground references (GND, VSS, etc.) |

#### 4.1.3 Examples

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

### 4.2 Port Abstraction

#### 4.2.1 Concept

A Port is a two-terminal access point defined by two nodes. It represents "where" a signal is stimulated or measured, independent of whether the physical quantity is voltage or current.

```
Port(positive_node, negative_node)
```

This maps directly to the network-analysis concept of a port: the voltage across the port is V(positive) - V(negative), and current into the port flows into the positive terminal.

#### 4.2.2 Port Types

| Type | Definition | Use Case |
|------|------------|----------|
| `Port` | Any two-node pair | General stimulus/response point |
| `DifferentialPort` | Two nodes where neither is a fixed reference | Required for differential/common-mode measurements |

DifferentialPort constraint: A `DifferentialPort` requires that both nodes can be independently driven. This excludes ground nodes and supply nodes.

#### 4.2.3 Type Checking Rules

When a measurement requires `DifferentialPort`:

```acir
measurement CMRR:
  requires: stim : DifferentialPort, resp : Port
```

The compiler checks the circuit's bench binding:

```acir
bench_bindings:
  stim = Port(IN.P, IN.N)    # IN.P and IN.N are both signal nodes -> DifferentialPort (ok)
  stim = Port(IN, GND)       # GND is a ground node -> Port only, not DifferentialPort
```

Determining DifferentialPort eligibility:
- If either node is declared via `ground` -> not DifferentialPort
- If either node is declared via `supply` -> not DifferentialPort
- Otherwise -> DifferentialPort

This is a compile-time check based on node declarations.

### 4.3 Bench Bindings

#### 4.3.1 Syntax

```ebnf
benchBindingsBlock = "bench_bindings:" NL INDENT (bindingDecl NL)+ DEDENT ;
bindingDecl = IDENT "=" bindingExpr ;
bindingExpr = portBinding | supplyBinding ;
portBinding = "Port" "(" nodeRef "," nodeRef ")" ;
supplyBinding = IDENT ;  (* References a supply declaration *)
nodeRef = IDENT ("." IDENT)? ;  (* e.g., IN.P, OUT, GND *)
```

#### 4.3.2 Standard Role Names

Measurements use these standard role names:

| Role | Type | Semantics |
|------|------|-----------|
| `stim` | `Port` or `DifferentialPort` | Where stimulus is applied |
| `resp` | `Port` | Where response is measured |
| `supply` | `Supply` | Power supply for rejection measurements |

#### 4.3.3 Examples

Single-ended output OTA:
```acir
circuit OTA5T implements SingleEndedOpAmp
  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog

  bench_bindings:
    stim = Port(IN.P, IN.N)    # Differential input
    resp = Port(OUT, GND)       # Single-ended output
    supply = VDD
```

Fully differential OTA:
```acir
circuit FullyDiffOTA implements FullyDifferentialOpAmp
  supply VDD
  ground GND
  input IN : Diff
  output OUT : Diff

  bench_bindings:
    stim = Port(IN.P, IN.N)
    resp = Port(OUT.P, OUT.N)   # Differential output
    supply = VDD
```

RC lowpass filter (no differential, no supply):
```acir
circuit RCLowpass implements Filter
  ground GND
  input IN : analog
  output OUT : analog

  bench_bindings:
    stim = Port(IN, GND)        # Single-ended input
    resp = Port(OUT, GND)       # Single-ended output
    # No supply binding - PSRR not applicable
```

#### 4.3.4 Binding Validation

At compile time:

1. Completeness: All roles required by referenced measurements must be bound
2. Type compatibility: Bindings must satisfy role type requirements
3. Node existence: Referenced nodes must exist in the circuit

Error example:
```
error[ACIR0042]: measurement CMRR requires DifferentialPort for role 'stim'
  --> RCLowpass.cir:12:5
   |
12 |     c_cmrr = ACBench::CMRR >= 60dB
   |     ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
   |
note: bench_bindings declares stim = Port(IN, GND)
note: Port(IN, GND) is not a DifferentialPort because GND is a ground node
help: CMRR is not applicable to this circuit topology
```

### 4.4 Measurement Definitions

#### 4.4.1 Syntax

```ebnf
measurementDef = "measurement" IDENT ":" NL INDENT measurementBody DEDENT ;

measurementBody = requiresClause
                  analysisClause
                  procedureClause
                  preconditionClause?
                  paramsClause?
                  unitClause ;

requiresClause = "requires:" roleList NL ;
roleList = roleDecl ("," roleDecl)* ;
roleDecl = IDENT ":" roleType ;
roleType = "Port" | "DifferentialPort" | "Supply" ;

analysisClause = "analysis:" analysisSpec NL ;
analysisSpec = analysisType | "multi" "(" analysisType ("," analysisType)* ")" ;
analysisType = "ac" | "dc" | "tran" | "noise" | "stb" ;

procedureClause = "procedure:" NL INDENT (procedureStmt NL)+ DEDENT ;

preconditionClause = "precondition:" boolExpr NL ;

paramsClause = "params:" NL INDENT (paramDecl NL)+ DEDENT ;
paramDecl = IDENT ":" paramType "=" defaultValue ;
paramType = "Frequency" | "Voltage" | "Current" | "Time" ;

unitClause = "unit:" UNIT NL ;
```

#### 4.4.2 Role Semantics

Roles are typed abstract names:

| Role Declaration | Meaning |
|------------------|---------|
| `stim : Port` | Any two-node pair for stimulus |
| `stim : DifferentialPort` | Two independent nodes for stimulus (enables differential/CM drive) |
| `resp : Port` | Any two-node pair for response measurement |
| `supply : Supply` | A supply rail |

#### 4.4.3 Single-Simulation Measurements

```acir
measurement PassbandGain:
  requires: stim : Port, resp : Port
  analysis: ac
  procedure:
    H = transfer(stim, resp)
    gain = mag_dB(H)
    result = eval(gain, f_measure)
  params:
    f_measure : Frequency = 1kHz
  unit: dB

measurement LowpassBandwidth:
  requires: stim : Port, resp : Port
  analysis: ac
  procedure:
    H = transfer(stim, resp)
    gain = mag_dB(H)
    dc_gain = eval(gain, DC)
    result = find_crossing(gain, dc_gain - 3, falling)
  unit: Hz

measurement GainBandwidth:
  requires: stim : Port, resp : Port
  analysis: ac
  procedure:
    H = transfer(stim, resp)
    gain = mag_dB(H)
    result = find_crossing(gain, 0, falling)
  precondition: eval(mag_dB(transfer(stim, resp)), DC) > 0
  unit: Hz

measurement PhaseMargin:
  requires: stim : Port, resp : Port
  analysis: ac
  procedure:
    H = transfer(stim, resp)
    gain = mag_dB(H)
    ph = phase(H)
    f_ugf = find_crossing(gain, 0, falling)
    result = 180 + eval(ph, f_ugf)
  precondition: eval(mag_dB(transfer(stim, resp)), DC) > 0
  unit: deg

measurement QuiescentPower:
  requires: supply : Supply
  analysis: dc
  procedure:
    result = abs(supply_voltage(supply) * supply_current(supply))
  unit: W
```

#### 4.4.4 Multi-Simulation Measurements

Some measurements require multiple simulation runs with different stimulus configurations. The `analysis: multi(...)` clause declares this, and the procedure uses `in <mode>` syntax to reference data from each simulation.

Stimulus Modes (Fixed Vocabulary):

| Mode | Semantics | Applicable To |
|------|-----------|---------------|
| `differential` | V+ = +0.5, V- = -0.5 (normalized) | DifferentialPort |
| `common_mode` | V+ = V- = 1 (normalized) | DifferentialPort |
| `signal` | Standard stimulus (default) | Port |
| `supply_perturb` | AC perturbation on supply rail | Supply |

```acir
measurement CMRR:
  requires: stim : DifferentialPort, resp : Port
  stimulus_modes: differential, common_mode
  analysis: multi(ac, ac)
  procedure:
    H_diff = transfer(stim, resp) in differential
    H_cm = transfer(stim, resp) in common_mode
    result = eval(mag_dB(H_diff), f) - eval(mag_dB(H_cm), f)
  params:
    f : Frequency = 1kHz
  unit: dB

measurement PSRR:
  requires: stim : Port, resp : Port, supply : Supply
  stimulus_modes: signal, supply_perturb
  analysis: multi(ac, ac)
  procedure:
    H_sig = transfer(stim, resp) in signal
    H_sup = transfer_from_supply(supply, resp) in supply_perturb
    result = eval(mag_dB(H_sig), f) - eval(mag_dB(H_sup), f)
  params:
    f : Frequency = 1kHz
  unit: dB
```

#### 4.4.5 Preconditions (Runtime Validity)

The `precondition:` clause specifies a runtime condition that must hold for the measurement to be valid:

```acir
measurement GainBandwidth:
  requires: stim : Port, resp : Port
  analysis: ac
  procedure:
    # ...
  precondition: eval(mag_dB(transfer(stim, resp)), DC) > 0
  unit: Hz
```

Semantics:
1. Simulation runs normally
2. Precondition is evaluated
3. If false: measurement result is `NaN`, warning logged
4. Constraint evaluation treats `NaN` as failure

Distinction from `requires:`:
- `requires:` - compile-time structural requirements (Port types)
- `precondition:` - runtime behavioral requirements (circuit must have gain > 0dB)

### 4.5 Procedure Primitives

#### 4.5.1 Design Principle

The procedure DSL uses a fixed set of primitives with documented type signatures. This enables:
- Unambiguous semantics
- Straightforward code generation
- Future extension without breaking changes

#### 4.5.2 Primitive Definitions

Transfer Function Primitives:

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `transfer(p1, p2)` | `(Port, Port) -> TransferFunction` | Complex voltage transfer function V(p2)/V(p1) |
| `transfer_from_supply(s, p)` | `(Supply, Port) -> TransferFunction` | Transfer function from supply perturbation to port |

Function Transformation Primitives:

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `mag(H)` | `TransferFunction -> MagnitudeFunction` | Linear magnitude |H(f)| |
| `mag_dB(H)` | `TransferFunction -> MagnitudeFunction` | Magnitude in dB: 20*log10(|H(f)|) |
| `phase(H)` | `TransferFunction -> PhaseFunction` | Phase in degrees |

Evaluation Primitives:

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `eval(F, f)` | `(Function, Frequency) -> Scalar` | Evaluate function at frequency f |
| `eval(F, DC)` | `(Function, DC) -> Scalar` | Evaluate at DC (f -> 0) |

Search Primitives:

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `find_crossing(F, threshold, direction)` | `(Function, Scalar, Direction) -> Frequency` | Find frequency where F crosses threshold |

Where `direction` is `rising` or `falling`.

Search failure: If no crossing exists, result is `NaN` and a warning is logged.

DC Measurement Primitives:

| Primitive | Signature | Semantics |
|-----------|-----------|-----------|
| `supply_voltage(s)` | `Supply -> Voltage` | DC voltage of supply |
| `supply_current(s)` | `Supply -> Current` | DC current into supply (positive = into circuit) |

Arithmetic:

Standard arithmetic operators (`+`, `-`, `*`, `/`) and `abs()` are available for `Scalar` values.

#### 4.5.3 Operator Precedence

For arithmetic expressions:

| Precedence | Operators | Associativity |
|------------|-----------|---------------|
| 1 (highest) | unary `-` | right |
| 2 | `*`, `/` | left |
| 3 (lowest) | `+`, `-` | left |

Parentheses override precedence.

#### 4.5.4 Type Checking

Procedures are type-checked at compile time:

```acir
procedure:
  H = transfer(stim, resp)      # H : TransferFunction
  gain = mag_dB(H)              # gain : MagnitudeFunction  
  dc_gain = eval(gain, DC)      # dc_gain : Scalar
  result = dc_gain + 3          # Scalar + Scalar -> Scalar (ok)
```

Type errors:
```acir
procedure:
  H = transfer(stim, resp)
  result = H + 3                # ERROR: TransferFunction + Scalar undefined
```

### 4.6 Bench Definitions

#### 4.6.1 Syntax

```ebnf
benchDef = "bench" IDENT ":" NL INDENT benchBody DEDENT ;
benchBody = measurementsBlock ;
measurementsBlock = "measurements:" NL INDENT (IDENT NL)+ DEDENT ;
```

#### 4.6.2 Semantics

A bench groups related measurements:

```acir
bench ACBench:
  measurements:
    PassbandGain
    LowpassBandwidth
    HighpassBandwidth
    GainBandwidth
    PhaseMargin
    GainMargin
    CMRR
    PSRR

bench DCBench:
  measurements:
    QuiescentPower
    TotalQuiescentPower

bench NoiseBench:
  measurements:
    InputReferredNoise
    SpotNoise
```

Note: Benches no longer have a `for Trait` clause. Applicability is determined entirely by whether the circuit's `bench_bindings` satisfy each measurement's `requires:` clause.

#### 4.6.3 Measurement Filtering

When a circuit references a bench in constraints:

1. Applicable measurements: Role requirements satisfied by `bench_bindings` -> included
2. Inapplicable, not in constraints: Silently excluded from testbench
3. Inapplicable, referenced in constraint: Compile error

### 4.7 Constraints

#### 4.7.1 Syntax

```ebnf
constraintsBlock = "constraints:" NL INDENT (constraint NL)+ DEDENT ;
constraint = IDENT "=" constraintExpr ;
constraintExpr = measurementRef comparator value ;
measurementRef = IDENT "::" IDENT paramOverrides? ;
paramOverrides = "(" paramAssign ("," paramAssign)* ")" ;
paramAssign = IDENT "=" value ;
comparator = ">=" | "<=" | ">" | "<" | "==" ;
```

#### 4.7.2 Examples

```acir
constraints:
  c_gain = ACBench::PassbandGain >= 40dB
  c_gbw = ACBench::GainBandwidth >= 100MHz
  c_pm = ACBench::PhaseMargin >= 60deg
  c_cmrr = ACBench::CMRR >= 60dB
  c_cmrr_hf = ACBench::CMRR(f=1MHz) >= 40dB
  c_psrr = ACBench::PSRR >= 60dB
  c_power = DCBench::QuiescentPower <= 100uW
```

#### 4.7.3 Parameter Overrides

Measurement parameters can be overridden at the constraint level:

```acir
c_cmrr_1k : ACBench::CMRR(f=1kHz) >= 60dB
c_cmrr_1M : ACBench::CMRR(f=1MHz) >= 40dB
```

### 4.8 Harness

#### 4.8.1 Syntax

```ebnf
harnessBlock = "harness:" NL INDENT (harnessEntry NL)+ DEDENT ;
harnessEntry = supplyEntry | biasEntry | sourceEntry | loadEntry | pvtEntry ;

supplyEntry = "supply" IDENT "=" value ;
biasEntry = "bias" IDENT "=" value ;
sourceEntry = "source" IDENT impedanceSpec ;
loadEntry = "load" IDENT loadSpec ;
impedanceSpec = "Z" "=" value ;
loadSpec = ("C" "=" value)? ("R" "=" value)? ("L" "=" value)? ;
pvtEntry = "pvt" pvtList ;
pvtList = pvtPoint ("," pvtPoint)* ;
pvtPoint = IDENT "@" TEMPERATURE ;
```

#### 4.8.2 Examples

```acir
harness:
  supply VDD = 1.8V
  bias VTAIL = 0.6V
  source IN Z=50Ohm
  load OUT C=1pF
  pvt TT@27C, SS@-40C, FF@125C
```

```acir
harness:
  supply AVDD = 1.8V
  supply DVDD = 1.8V
  load OUT C=500fF R=10k
```

### 4.9 Applicability Resolution

#### 4.9.1 Algorithm

```python
def resolve_applicability(circuit, bench, constraints):
    """
    Determine which measurements apply and validate constraints.
    """
    bindings = circuit.bench_bindings
    applicable = {}
    errors = []
    
    for measurement in bench.measurements:
        can_apply = True
        
        for role in measurement.requires:
            # Check if role is bound
            if role.name not in bindings:
                can_apply = False
                break
            
            binding = bindings[role.name]
            
            # Check type compatibility
            if role.type == 'DifferentialPort':
                if not is_differential_port(circuit, binding):
                    can_apply = False
                    break
            elif role.type == 'Supply':
                if not is_supply(circuit, binding):
                    can_apply = False
                    break
            # Port type always matches any Port binding
        
        applicable[measurement.name] = can_apply
    
    # Validate constraints reference applicable measurements
    for constraint in constraints:
        meas_name = constraint.measurement_name
        if meas_name not in applicable:
            errors.append(f"Unknown measurement: {meas_name}")
        elif not applicable[meas_name]:
            errors.append(
                f"Measurement {meas_name} is not applicable: "
                f"{explain_inapplicability(measurement, circuit, bindings)}"
            )
    
    return applicable, errors


def is_differential_port(circuit, binding):
    """
    A Port is a DifferentialPort if neither node is a ground or supply.
    """
    pos_node, neg_node = binding.positive, binding.negative
    
    # Check if either node is a ground
    for ground in circuit.grounds:
        if pos_node == ground.name or neg_node == ground.name:
            return False
    
    # Check if either node is a supply
    for supply in circuit.supplies:
        if pos_node == supply.name or neg_node == supply.name:
            return False
    
    return True
```

#### 4.9.2 Error Messages

Clear diagnostics for inapplicable measurements:

```
error[ACIR0042]: measurement CMRR is not applicable to circuit RCLowpass
  --> RCLowpass.cir:15:5
   |
15 |     c_cmrr : ACBench::CMRR >= 60dB
   |     ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
   |
note: CMRR requires 'stim : DifferentialPort'
note: bench_bindings declares: stim = Port(IN, GND)
note: Port(IN, GND) is not a DifferentialPort because GND is a ground node
```

### 4.10 Failure Handling

#### 4.10.1 Failure Modes

| Failure | When | Result | Behavior |
|---------|------|--------|----------|
| Compile-time type error | Role type mismatch | N/A | Compile error, no simulation |
| Missing binding | Required role not in bench_bindings | N/A | Compile error |
| Precondition failure | Runtime condition false | `NaN` | Warning logged, constraint fails |
| Search failure | No crossing found | `NaN` | Warning logged, constraint fails |
| Simulation error | Simulator fails | `NaN` | Error logged, constraint fails |

#### 4.10.2 Partial Results

When a testbench contains multiple measurements:

1. All measurements are attempted
2. Failures in one measurement do not prevent others from running
3. Results are reported per-measurement with status (success/failure/skipped)
4. Overall constraint satisfaction requires all referenced measurements to succeed

#### 4.10.3 Result Structure

```
TestbenchResults:
  circuit: OTA5T
  bench: ACBench
  measurements:
    PassbandGain: 52.3 dB [SUCCESS]
    GainBandwidth: 156 MHz [SUCCESS]
    PhaseMargin: 62.1 deg [SUCCESS]
    CMRR: 68.4 dB [SUCCESS]
    PSRR: NaN [PRECONDITION_FAILED: "supply not perturbed"]
  constraints:
    c_gain: PASS (52.3 >= 40)
    c_gbw: PASS (156 >= 100)
    c_pm: PASS (62.1 >= 60)
    c_cmrr: PASS (68.4 >= 60)
```

---

## 5. Emission Pipeline

### 5.1 Design Decision: Programmatic Generation

This RFC mandates programmatic testbench generation in C#, not templates.

Rationale:
1. Templates with conditionals become unmaintainable at scale
2. C# provides type safety, testability, and refactoring support
3. Testbench generation logic can share code with measurement calculation
4. New backends require only a new emitter class, not a template language port

### 5.2 Architecture


![Emission Pipeline](../../resources/0000/0000-emission-flow.svg)




```
direction: right

ACIR_Document: "ACIR Document"
Binding_Resolution: "Binding Resolution"
TestbenchModel: "TestbenchModel"

ITestbenchEmitter: "ITestbenchEmitter\n(per backend)"
ngspice: "ngspice\nEmitter"
Spectre: "Spectre\nEmitter"
Xyce: "Xyce\nEmitter"

ACIR_Document -> Binding_Resolution -> TestbenchModel
TestbenchModel -> ITestbenchEmitter
ITestbenchEmitter -> ngspice
ITestbenchEmitter -> Spectre
ITestbenchEmitter -> Xyce
```

### 5.3 TestbenchModel

```csharp
public class TestbenchModel
{
    public string CircuitName { get; }
    public string BenchName { get; }
    
    // Resolved port bindings
    public PortBinding Stim { get; }
    public PortBinding Resp { get; }
    public SupplyBinding? Supply { get; }
    
    // Measurements to run (filtered by applicability)
    public List<ResolvedMeasurement> Measurements { get; }
    
    // From harness block
    public HarnessConfig Harness { get; }
}

public class PortBinding
{
    public string PositiveNode { get; }
    public string NegativeNode { get; }
    public bool IsDifferential { get; }  // True if DifferentialPort
}

public class ResolvedMeasurement
{
    public MeasurementDefinition Definition { get; }
    public Dictionary<string, object> Parameters { get; }
    public List<StimulusMode> RequiredModes { get; }
}

public enum StimulusMode
{
    Signal,
    Differential,
    CommonMode,
    SupplyPerturb
}
```

### 5.4 ITestbenchEmitter Interface

```csharp
public interface ITestbenchEmitter
{
    /// <summary>
    /// Emit testbench file(s) for the given model.
    /// May emit multiple files for multi-simulation measurements.
    /// </summary>
    EmittedTestbench Emit(TestbenchModel model);
    
    /// <summary>
    /// Parse simulation results from the backend's output.
    /// </summary>
    SimulationResults ParseResults(string outputPath, TestbenchModel model);
}

public class EmittedTestbench
{
    public Dictionary<StimulusMode, string> NetlistsByMode { get; }
    public string ControlScript { get; }  // For simulators that support scripting
}
```

### 5.5 NgspiceEmitter (Sketch)

```csharp
public class NgspiceEmitter : ITestbenchEmitter
{
    public EmittedTestbench Emit(TestbenchModel model)
    {
        var result = new EmittedTestbench();
        
        // Determine which stimulus modes are needed
        var modes = model.Measurements
            .SelectMany(m => m.RequiredModes)
            .Distinct()
            .ToList();
        
        foreach (var mode in modes)
        {
            var netlist = EmitNetlistForMode(model, mode);
            result.NetlistsByMode[mode] = netlist;
        }
        
        return result;
    }
    
    private string EmitNetlistForMode(TestbenchModel model, StimulusMode mode)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"* Testbench: {model.BenchName} for {model.CircuitName}");
        sb.AppendLine($"* Stimulus mode: {mode}");
        sb.AppendLine();
        
        EmitIncludes(sb, model);
        EmitSupplies(sb, model);
        EmitStimulus(sb, model, mode);
        EmitLoad(sb, model);
        EmitDutInstance(sb, model);
        EmitAnalysis(sb, model);
        EmitMeasurements(sb, model, mode);
        
        sb.AppendLine(".end");
        return sb.ToString();
    }
    
    private void EmitStimulus(StringBuilder sb, TestbenchModel model, StimulusMode mode)
    {
        var stim = model.Stim;
        
        switch (mode)
        {
            case StimulusMode.Differential:
                sb.AppendLine($"* Differential stimulus");
                sb.AppendLine($"V_STIM_P {stim.PositiveNode} v_cm DC {{v_cm}} AC 0.5");
                sb.AppendLine($"V_STIM_N {stim.NegativeNode} v_cm DC {{v_cm}} AC -0.5");
                sb.AppendLine($"V_CM v_cm 0 DC {{v_cm}}");
                break;
                
            case StimulusMode.CommonMode:
                sb.AppendLine($"* Common-mode stimulus");
                sb.AppendLine($"V_STIM_P {stim.PositiveNode} 0 DC {{v_cm}} AC 1");
                sb.AppendLine($"V_STIM_N {stim.NegativeNode} 0 DC {{v_cm}} AC 1");
                break;
                
            case StimulusMode.Signal:
            default:
                if (stim.IsDifferential)
                {
                    sb.AppendLine($"* Differential stimulus (signal mode)");
                    sb.AppendLine($"V_STIM_P {stim.PositiveNode} v_cm DC {{v_cm}} AC 0.5");
                    sb.AppendLine($"V_STIM_N {stim.NegativeNode} v_cm DC {{v_cm}} AC -0.5");
                    sb.AppendLine($"V_CM v_cm 0 DC {{v_cm}}");
                }
                else
                {
                    sb.AppendLine($"* Single-ended stimulus");
                    sb.AppendLine($"V_STIM {stim.PositiveNode} {stim.NegativeNode} DC {{v_cm}} AC 1");
                }
                break;
        }
    }
    
    private void EmitMeasurements(StringBuilder sb, TestbenchModel model, StimulusMode mode)
    {
        var resp = model.Resp;
        string respExpr = resp.IsDifferential 
            ? $"{{v({resp.PositiveNode})-v({resp.NegativeNode})}}"
            : resp.PositiveNode;
        
        foreach (var meas in model.Measurements.Where(m => m.RequiredModes.Contains(mode)))
        {
            EmitMeasurement(sb, meas, respExpr);
        }
    }
    
    private void EmitMeasurement(StringBuilder sb, ResolvedMeasurement meas, string respExpr)
    {
        // Emit simulator-specific measurement commands based on primitives
        // This is where procedure primitives map to ngspice .meas statements
        
        switch (meas.Definition.Name)
        {
            case "PassbandGain":
                var f = meas.Parameters.GetValueOrDefault("f_measure", 1e3);
                sb.AppendLine($".meas ac PassbandGain find vdb({respExpr}) at={f}");
                break;
                
            case "GainBandwidth":
                sb.AppendLine($".meas ac GainBandwidth when vdb({respExpr})=0 fall=1");
                break;
                
            case "PhaseMargin":
                sb.AppendLine($".meas ac f_ugf when vdb({respExpr})=0 fall=1");
                sb.AppendLine($".meas ac PhaseMargin find par('180+vp({respExpr})') at=f_ugf");
                break;
                
            // Additional measurements...
        }
    }
}
```

### 5.6 Multi-Simulation Orchestration

```csharp
public class MeasurementRunner
{
    private readonly ITestbenchEmitter _emitter;
    private readonly ISimulatorInvoker _simulator;
    
    public async Task<TestbenchResults> RunAsync(TestbenchModel model)
    {
        var results = new TestbenchResults(model.CircuitName, model.BenchName);
        var emitted = _emitter.Emit(model);
        
        // Run each stimulus mode
        var simDataByMode = new Dictionary<StimulusMode, SimulationData>();
        
        foreach (var (mode, netlist) in emitted.NetlistsByMode)
        {
            try
            {
                var output = await _simulator.RunAsync(netlist);
                simDataByMode[mode] = _emitter.ParseResults(output, model);
            }
            catch (SimulationException ex)
            {
                results.AddError(mode, ex.Message);
            }
        }
        
        // Compute measurements
        foreach (var meas in model.Measurements)
        {
            var calculator = GetCalculator(meas.Definition);
            try
            {
                var value = calculator.Calculate(simDataByMode, meas.Parameters);
                results.AddMeasurement(meas.Definition.Name, value, MeasurementStatus.Success);
            }
            catch (PreconditionFailedException ex)
            {
                results.AddMeasurement(meas.Definition.Name, double.NaN, 
                    MeasurementStatus.PreconditionFailed, ex.Message);
            }
            catch (SearchFailedException ex)
            {
                results.AddMeasurement(meas.Definition.Name, double.NaN,
                    MeasurementStatus.SearchFailed, ex.Message);
            }
        }
        
        return results;
    }
}
```

---

## 6. Standard Library

### 6.1 Library Structure

```
lib/
|-- std.acir                     # Meta-include
|-- measurements/
|   `-- standard.acir            # Standard measurement definitions
`-- benches/
    `-- standard.acir            # Standard bench definitions
```

The `lib/std.acir` file includes all standard definitions:

```acir
include lib/measurements/standard
include lib/benches/standard
```

### 6.2 Standard Measurements

File: `lib/measurements/standard.acir`

```acir
// ============================================================
// Transfer Function Measurements
// ============================================================

measurement PassbandGain:
  requires: stim : Port, resp : Port
  analysis: ac
  procedure:
    H = transfer(stim, resp)
    gain = mag_dB(H)
    result = eval(gain, f_measure)
  params:
    f_measure : Frequency = 1kHz
  unit: dB

measurement LowpassBandwidth:
  requires: stim : Port, resp : Port
  analysis: ac
  procedure:
    H = transfer(stim, resp)
    gain = mag_dB(H)
    dc_gain = eval(gain, DC)
    result = find_crossing(gain, dc_gain - 3, falling)
  unit: Hz

measurement HighpassBandwidth:
  requires: stim : Port, resp : Port
  analysis: ac
  procedure:
    H = transfer(stim, resp)
    gain = mag_dB(H)
    hf_gain = eval(gain, f_high)
    result = find_crossing(gain, hf_gain - 3, rising)
  params:
    f_high : Frequency = 1GHz
  unit: Hz

measurement GainBandwidth:
  requires: stim : Port, resp : Port
  analysis: ac
  procedure:
    H = transfer(stim, resp)
    gain = mag_dB(H)
    result = find_crossing(gain, 0, falling)
  precondition: eval(mag_dB(transfer(stim, resp)), DC) > 0
  unit: Hz

measurement PhaseMargin:
  requires: stim : Port, resp : Port
  analysis: ac
  procedure:
    H = transfer(stim, resp)
    gain = mag_dB(H)
    ph = phase(H)
    f_ugf = find_crossing(gain, 0, falling)
    result = 180 + eval(ph, f_ugf)
  precondition: eval(mag_dB(transfer(stim, resp)), DC) > 0
  unit: deg

measurement GainMargin:
  requires: stim : Port, resp : Port
  analysis: ac
  procedure:
    H = transfer(stim, resp)
    gain = mag_dB(H)
    ph = phase(H)
    f_180 = find_crossing(ph, -180, falling)
    result = 0 - eval(gain, f_180)
  precondition: eval(mag_dB(transfer(stim, resp)), DC) > 0
  unit: dB

// ============================================================
// Rejection Measurements (Multi-Simulation)
// ============================================================

measurement CMRR:
  requires: stim : DifferentialPort, resp : Port
  stimulus_modes: differential, common_mode
  analysis: multi(ac, ac)
  procedure:
    H_diff = transfer(stim, resp) in differential
    H_cm = transfer(stim, resp) in common_mode
    gain_diff = mag_dB(H_diff)
    gain_cm = mag_dB(H_cm)
    result = eval(gain_diff, f) - eval(gain_cm, f)
  params:
    f : Frequency = 1kHz
  unit: dB

measurement PSRR:
  requires: stim : Port, resp : Port, supply : Supply
  stimulus_modes: signal, supply_perturb
  analysis: multi(ac, ac)
  procedure:
    H_sig = transfer(stim, resp) in signal
    H_sup = transfer_from_supply(supply, resp) in supply_perturb
    gain_sig = mag_dB(H_sig)
    gain_sup = mag_dB(H_sup)
    result = eval(gain_sig, f) - eval(gain_sup, f)
  params:
    f : Frequency = 1kHz
  unit: dB

// ============================================================
// Power Measurements
// ============================================================

measurement QuiescentPower:
  requires: supply : Supply
  analysis: dc
  procedure:
    v = supply_voltage(supply)
    i = supply_current(supply)
    result = abs(v * i)
  unit: W
```

### 6.3 Standard Benches

File: `lib/benches/standard.acir`

```acir
bench ACBench:
  measurements:
    PassbandGain
    LowpassBandwidth
    HighpassBandwidth
    GainBandwidth
    PhaseMargin
    GainMargin
    CMRR
    PSRR

bench DCBench:
  measurements:
    QuiescentPower
```

---

## 7. Complete Examples

### 7.1 Single-Ended OTA

```acir
ACIR 3.0

include lib/std

circuit OTA5T implements SingleEndedOpAmp
  level EL

  supply VDD
  ground GND
  input IN : Diff
  output OUT : analog
  input VTAIL : bias

  bench_bindings:
    stim = Port(IN.P, IN.N)      # Differential stimulus
    resp = Port(OUT, GND)         # Single-ended response
    supply = VDD

  fill:
    net mirror_gate : analog
    net tnode : analog

    nmos dp.M_N (.G--IN.P, .D--mirror_gate, .S--tnode, .B--GND) : nfet_01v8
      size (W=2u, L=180n, M=1)
    nmos dp.M_P (.G--IN.N, .D--OUT, .S--tnode, .B--GND) : nfet_01v8
      size (W=2u, L=180n, M=1)
    nmos dp.M_TAIL (.G--VTAIL, .D--tnode, .S--GND, .B--GND) : nfet_01v8
      size (W=4u, L=180n, M=1)
    pmos cm.M_SENSE (.G--mirror_gate, .D--mirror_gate, .S--VDD, .B--VDD) : pfet_01v8
      size (W=2u, L=180n, M=1)
    pmos cm.M_TAP0 (.G--mirror_gate, .D--OUT, .S--VDD, .B--VDD) : pfet_01v8
      size (W=2u, L=180n, M=1)

  constraints:
    c_gbw = ACBench::GainBandwidth >= 100MHz
    c_gain = ACBench::PassbandGain >= 50dB
    c_pm = ACBench::PhaseMargin >= 60deg
    c_cmrr = ACBench::CMRR >= 60dB
    c_psrr = ACBench::PSRR >= 60dB
    c_power = DCBench::QuiescentPower <= 100uW

  harness:
    supply VDD = 1.8V
    bias VTAIL = 0.6V
    load OUT C=1pF
```

Applicability:
- `stim = Port(IN.P, IN.N)` -> both nodes are signals -> `DifferentialPort` (ok)
- CMRR requires `DifferentialPort` -> applicable
- PSRR requires `Supply` -> `supply = VDD` -> applicable

### 7.2 Fully-Differential OTA

```acir
ACIR 3.0

include lib/std

circuit FullyDiffOTA implements FullyDifferentialOpAmp
  level EL

  supply AVDD
  supply DVDD
  ground GND
  input IN : Diff
  output OUT : Diff
  input VCMFB : bias

  bench_bindings:
    stim = Port(IN.P, IN.N)
    resp = Port(OUT.P, OUT.N)    # Differential response
    supply = AVDD                 # Choose which supply for PSRR

  fill:
    # ... device instantiations ...

  constraints:
    c_gbw = ACBench::GainBandwidth >= 500MHz
    c_cmrr = ACBench::CMRR >= 80dB
    c_psrr = ACBench::PSRR >= 70dB

  harness:
    supply AVDD = 1.8V
    supply DVDD = 1.8V
    load OUT C=500fF
```

Note: With multiple supplies, the `bench_bindings` explicitly chooses which supply to use for PSRR. To measure PSRR for both supplies, add separate constraints:

```acir
bench_bindings:
  stim = Port(IN.P, IN.N)
  resp = Port(OUT.P, OUT.N)
  supply = AVDD
  supply_digital = DVDD           # Additional binding

constraints:
  c_psrr_a = ACBench::PSRR >= 70dB                    # Uses 'supply' (AVDD)
  c_psrr_d = ACBench::PSRR(supply=supply_digital) >= 50dB
```

### 7.3 Passive RC Filter

```acir
ACIR 3.0

include lib/std

circuit RCLowpass implements Filter
  level EL

  ground GND
  input IN : analog
  output OUT : analog

  bench_bindings:
    stim = Port(IN, GND)          # Single-ended (NOT DifferentialPort)
    resp = Port(OUT, GND)
    # No supply binding

  fill:
    resistor R1 (.P--IN, .N--OUT) : resistor
      R = 10k
    capacitor C1 (.P--OUT, .N--GND) : capacitor
      C = 1n

  constraints:
    c_bw = ACBench::LowpassBandwidth >= 10kHz
    c_gain = ACBench::PassbandGain >= -1dB
    # Cannot use CMRR - would be compile error (stim is not DifferentialPort)
    # Cannot use PSRR - would be compile error (no supply binding)

  harness:
    source IN Z=50Ohm
```

Applicability:
- `stim = Port(IN, GND)` -> GND is a ground node -> not `DifferentialPort`
- CMRR requires `DifferentialPort` -> not applicable
- No supply binding -> PSRR not applicable

### 7.4 TIA (Transimpedance Amplifier)

```acir
ACIR 3.0

include lib/std

circuit SimpleTIA implements SingleEndedOpAmp
  level EL

  supply VDD
  ground GND
  input IN : Diff                 # Photodiode connects differentially
  output OUT : analog

  bench_bindings:
    stim = Port(IN.P, IN.N)       # Current injected here
    resp = Port(OUT, GND)
    supply = VDD

  # ... fill and constraints ...
```

> **Note:**  
> The `Port` abstraction works for TIAs even when the input is current-driven. Testbench harnesses (defined separately or in future extensions) can specify current-mode stimulus as needed. The measurement infrastructure only cares about the node locations for stimulus, not the signal type.

### 7.5 Legacy Circuit with Non-Standard Port Names

```acir
ACIR 3.0

include lib/std

circuit LegacyAmp implements SingleEndedOpAmp
  level EL

  supply VCC
  ground VSS
  input VIN_DIFF : Diff
  output VOUT_SE : analog
  input IBIAS : bias

  bench_bindings:
    stim = Port(VIN_DIFF.P, VIN_DIFF.N)
    resp = Port(VOUT_SE, VSS)
    supply = VCC

  fill:
    # ... device instantiations ...

  constraints:
    c_gain = ACBench::PassbandGain >= 30dB
    c_cmrr = ACBench::CMRR(f=10kHz) >= 50dB
    c_psrr = ACBench::PSRR >= 40dB

  harness:
    supply VCC = 3.3V
    bias IBIAS = 10uA
    load VOUT_SE C=5pF R=10k
```

---

## 8. Grammar Specification

```ebnf
(* ============================================================ *)
(* Top-level *)
(* ============================================================ *)

document = header (include)* (definition)* ;
header = "ACIR" VERSION NL ;
include = "include" PATH NL ;
definition = traitDef | measurementDef | benchDef | circuitDef ;

(* ============================================================ *)
(* Traits *)
(* ============================================================ *)

traitDef = "trait" IDENT extendsClause? ":" NL INDENT traitBody DEDENT ;
extendsClause = "extends" IDENT ;
traitBody = (traitMember NL)+ ;
traitMember = portDecl | supplyDecl | groundDecl | connectorsBlock | "pass" ;

connectorsBlock = "connectors:" NL INDENT (connectorEntry NL)+ DEDENT ;
connectorEntry = "to" IDENT ":" NL INDENT (connectionStmt NL)+ DEDENT ;
connectionStmt = nodeRef "--" nodeRef ;

(* ============================================================ *)
(* Port Declarations *)
(* ============================================================ *)

portDecl = direction IDENT ":" typeSpec ;
direction = "input" | "output" | "inout" ;
typeSpec = domain | bundleType ;
domain = "analog" | "bias" ;
bundleType = IDENT ;

supplyDecl = "supply" IDENT ;
groundDecl = "ground" IDENT ;

(* ============================================================ *)
(* Measurements *)
(* ============================================================ *)

measurementDef = "measurement" IDENT ":" NL INDENT measurementBody DEDENT ;

measurementBody = requiresClause
                  stimulusModesClause?
                  analysisClause
                  procedureClause
                  preconditionClause?
                  paramsClause?
                  unitClause ;

requiresClause = "requires:" roleList NL ;
roleList = roleDecl ("," roleDecl)* ;
roleDecl = IDENT ":" roleType ;
roleType = "Port" | "DifferentialPort" | "Supply" ;

stimulusModesClause = "stimulus_modes:" modeList NL ;
modeList = IDENT ("," IDENT)* ;

analysisClause = "analysis:" analysisSpec NL ;
analysisSpec = analysisType | multiAnalysis ;
analysisType = "ac" | "dc" | "tran" | "noise" | "stb" ;
multiAnalysis = "multi" "(" analysisType ("," analysisType)* ")" ;

procedureClause = "procedure:" NL INDENT (procedureStmt NL)+ DEDENT ;
procedureStmt = assignment | resultStmt ;
assignment = IDENT "=" expr ;
resultStmt = "result" "=" expr ;

preconditionClause = "precondition:" boolExpr NL ;

paramsClause = "params:" NL INDENT (paramDecl NL)+ DEDENT ;
paramDecl = IDENT ":" paramType "=" defaultValue ;
paramType = "Frequency" | "Voltage" | "Current" | "Time" ;

unitClause = "unit:" UNIT NL ;

(* ============================================================ *)
(* Procedure Expressions *)
(* ============================================================ *)

expr = additiveExpr ;
additiveExpr = multiplicativeExpr (("+"|"-") multiplicativeExpr)* ;
multiplicativeExpr = unaryExpr (("*"|"/") unaryExpr)* ;
unaryExpr = "-"? primaryExpr ;
primaryExpr = functionCall | IDENT | NUMBER | "(" expr ")" ;

functionCall = IDENT "(" argList? ")" inClause? ;
argList = expr ("," expr)* ;
inClause = "in" IDENT ;

boolExpr = expr comparator expr ;
comparator = ">" | ">=" | "<" | "<=" | "==" ;

(* ============================================================ *)
(* Benches *)
(* ============================================================ *)

benchDef = "bench" IDENT ":" NL INDENT benchBody DEDENT ;
benchBody = measurementsBlock ;
measurementsBlock = "measurements:" NL INDENT (IDENT NL)+ DEDENT ;

(* ============================================================ *)
(* Circuits *)
(* ============================================================ *)

circuitDef = "circuit" IDENT implementsClause? NL INDENT circuitBody DEDENT ;
implementsClause = "implements" traitRef ("," traitRef)* ;
traitRef = IDENT ;

circuitBody = levelDecl (circuitMember NL)* ;
levelDecl = "level" ("EL" | "ML" | "HL") NL ;
circuitMember = supplyDecl | groundDecl | portDecl 
              | benchBindingsBlock | fillBlock | constraintsBlock | harnessBlock ;

(* ============================================================ *)
(* Bench Bindings *)
(* ============================================================ *)

benchBindingsBlock = "bench_bindings:" NL INDENT (bindingDecl NL)+ DEDENT ;
bindingDecl = IDENT "=" bindingExpr ;
bindingExpr = portBinding | IDENT ;
portBinding = "Port" "(" nodeRef "," nodeRef ")" ;
nodeRef = IDENT ("." IDENT)? ;

(* ============================================================ *)
(* Constraints *)
(* ============================================================ *)

constraintsBlock = "constraints:" NL INDENT (constraint NL)+ DEDENT ;
constraint = IDENT "=" constraintExpr ;
constraintExpr = measurementRef comparator value ;
measurementRef = IDENT "::" IDENT paramOverrides? ;
paramOverrides = "(" paramAssign ("," paramAssign)* ")" ;
paramAssign = IDENT "=" value ;

(* ============================================================ *)
(* Harness *)
(* ============================================================ *)

harnessBlock = "harness:" NL INDENT (harnessEntry NL)+ DEDENT ;
harnessEntry = supplyEntry | biasEntry | sourceEntry | loadEntry | pvtEntry ;

supplyEntry = "supply" IDENT "=" value ;
biasEntry = "bias" IDENT "=" value ;
sourceEntry = "source" IDENT impedanceSpec ;
loadEntry = "load" IDENT loadSpec ;
impedanceSpec = "Z" "=" value ;
loadSpec = ("C" "=" value)? ("R" "=" value)? ("L" "=" value)? ;
pvtEntry = "pvt" pvtList ;
pvtList = pvtPoint ("," pvtPoint)* ;
pvtPoint = IDENT "@" TEMPERATURE ;

(* ============================================================ *)
(* Fill Block (unchanged from ACIR 2.x) *)
(* ============================================================ *)

fillBlock = "fill:" NL INDENT (fillEntry NL)+ DEDENT ;
(* ... fill syntax unchanged ... *)

(* ============================================================ *)
(* Terminals *)
(* ============================================================ *)

IDENT = [a-zA-Z_][a-zA-Z0-9_]* ;
NUMBER = [0-9]+ ("." [0-9]+)? exponent? ;
exponent = [eE] [+-]? [0-9]+ ;
value = NUMBER UNIT? ;
UNIT = "V" | "A" | "W" | "Hz" | "Ohm" | "F" | "H" | "dB" | "deg" | "%"
     | "mV" | "uV" | "nV" | "mA" | "uA" | "nA" | "pA"
     | "kHz" | "MHz" | "GHz" | "kOhm" | "MOhm"
     | "pF" | "nF" | "uF" | "fF"
     | "ns" | "us" | "ms" | "ps"
     | "mW" | "uW" | "nW" ;
VERSION = [0-9]+ "." [0-9]+ ;
PATH = [a-zA-Z0-9_/.-]+ ;
TEMPERATURE = "-"? [0-9]+ "C" ;

NL = "\n" ;
INDENT = (* indentation increase *) ;
DEDENT = (* indentation decrease *) ;
```

---

## 9. Migration from ACIR 2.x

### 9.1 Breaking Changes

| ACIR 2.x | ACIR 3.0 | Migration |
|----------|----------|-----------|
| `port IN : Diff` | `input IN : Diff` | Change keyword |
| `builtin SEOpAmpACBench` | Removed | Use standard benches with `bench_bindings` |
| Implicit measurement binding | `bench_bindings:` block | Add explicit bindings |

### 9.2 Migration Steps

1. Replace `port` keyword:
   ```
   # Before
   port IN : Diff
   port OUT : analog
   
   # After
   input IN : Diff
   output OUT : analog
   ```

2. Add bench_bindings block:
   ```acir
   bench_bindings:
     stim = Port(IN.P, IN.N)
     resp = Port(OUT, GND)
     supply = VDD
   ```

3. Update constraints to use standard benches:
   ```
   # Before
   constraints:
     c_gbw = SEOpAmpACBench::GainBandwidth >= 100MHz
   
   # After
   constraints:
     c_gbw = ACBench::GainBandwidth >= 100MHz
   ```

4. Remove builtin bench references:
   - Delete any `bench ... builtin ...` declarations
   - Use `include lib/std` to access standard benches

### 9.3 Automated Migration Tool

A migration tool will be provided:

```bash
acir-migrate --from 2.x --to 3.0 circuit.acir
```

The tool will:
- Replace `port` with appropriate direction keyword
- Infer `bench_bindings` from circuit structure and trait
- Update constraint bench references
- Report any manual changes required

---

## 10. Implementation Plan

### 10.1 Phase 1: Grammar and Parser

1. Update lexer with direction keywords (`input`, `output`, `inout`)
2. Remove `port` keyword
3. Add `bench_bindings:` block parsing
4. Add `measurement` definition blocks with `stimulus_modes` and `in` clause
5. Add `precondition:` clause
6. Remove `builtin` keyword
7. Update constraint syntax (`:` separator)

### 10.2 Phase 2: Semantic Analysis 

1. Implement Port type checking (Port vs DifferentialPort)
2. Implement bench binding validation
3. Implement measurement applicability checking
4. Implement procedure primitive type checking
5. Emit diagnostics for inapplicable measurements in constraints

### 10.3 Phase 3: Emission Pipeline 

1. Define `TestbenchModel`, `PortBinding`, `ResolvedMeasurement` structures
2. Implement `ITestbenchEmitter` interface
3. Implement `NgspiceEmitter` with programmatic generation
4. Implement `SpectreEmitter`
5. Implement multi-simulation orchestration for stimulus modes
6. Implement measurement calculators for multi-sim measurements

### 10.4 Phase 4: Standard Library 

1. Create `lib/measurements/standard.acir`
2. Create `lib/benches/standard.acir`
3. Create `lib/std.acir` meta-include
4. Implement C# calculators: `CMRRCalculator`, `PSRRCalculator`

### 10.5 Phase 5: Migration and Testing

1. Implement `acir-migrate` tool
2. Unit tests for binding resolution and type checking
3. Integration tests for each circuit topology
4. Golden file tests for emitted testbenches
5. Migrate existing example circuits

---

## 11. Future Work

### 11.1 Range and Sweep Constraints

Support for constraints that must hold across a parameter range:

```acir
constraints:
  c_cmrr_band = ACBench::CMRR for f in [1kHz:1MHz] >= 50dB
```

Requires:
- Range syntax in grammar
- Sampling strategy specification
- Multi-point simulation orchestration

### 11.2 Current-Mode Measurements

Extend harness to specify stimulus mode:

```acir
harness:
  source IN mode=current Z=1MOhm    # Current stimulus for TIA
```

Requires:
- Harness syntax extension
- Emitter changes for current source generation

### 11.3 Noise Measurements

```acir
measurement InputReferredNoise:
  requires: stim : Port, resp : Port
  analysis: noise
  procedure:
    Sn = input_noise_density(stim, resp)
    result = integrate_sqrt(Sn, f_min, f_max)
  params:
    f_min : Frequency = 1Hz
    f_max : Frequency = 1MHz
  unit: V
```

Requires:
- Noise analysis primitives
- Spectral density integration primitive

### 11.4 Transient Measurements

```acir
measurement SlewRate:
  requires: stim : Port, resp : Port
  analysis: tran
  procedure:
    result = max(derivative(voltage(resp)))
  unit: V/s
```

Requires:
- Transient analysis primitives
- Time-domain evaluation primitives

### 11.5 Statistical Constraints

```acir
constraints:
  c_gbw_yield : yield(ACBench::GainBandwidth >= 80MHz) >= 99%
```

Requires:
- Monte Carlo simulation support
- Statistical aggregation primitives

### 11.6 Full Expression Language

Extend procedure DSL to a complete typed expression language with:
- User-defined functions
- Conditional expressions
- Loop constructs for parameter sweeps

---

## 12. References

1. ACIR Specification, Chapters 1-3
2. Razavi, B. "Design of Analog CMOS Integrated Circuits"
3. Gonzalez, G. "Microwave Transistor Amplifiers" (two-port network theory)
4. ngspice User Manual
5. Cadence Spectre User Guide