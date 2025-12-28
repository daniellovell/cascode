# ACIR: Chapter 3 - Analog Circuit Intermediate Representation

> This chapter defines ACIR as a data model and text-based format that carries circuit connectivity and analysis intent from Cascode ADL to synthesis, sizing, verification, and SPICE emission.

---

## 3.0 Summary

ACIR serves as the single, authoritative handoff between the Cascode front end and the rest of the toolchain. Its role is both simple and critical: representing every connection as a binding from an instance terminal to a net, preserving sufficient structure and metadata to support search operations, rewrites, sizing, and benchmark generation, while maintaining deterministic output that facilitates diff operations and serves as a golden artifact in tests and reviews.

If we do this well, getting from ADL to SPICE is a straight line: parse and elaborate ADL into instances and nets, write ACIR, pick and size implementations, then print SPICE by looking up port ordering in library templates and substituting the already-known node names.

Most compilers follow a familiar arc: lex and parse source into an abstract syntax tree (AST), lower that AST into an intermediate representation (IR), run optimizations on the IR, then lower again to a concrete target before emission. Cascode follows the same shape; the interesting design work lives in the ACIR and optimization stages. This chapter focuses on that IR step in the context of the Cascode front end.

ACIR is produced at three elaboration levels that describe how far the front end has progressed:

- HL (High Level): design slots may remain open (declared with the `slot` keyword), and many parameters can stay symbolic or null, but connectivity through nets and ports is already complete.
- ML (Mid Level): all slots have been bound to concrete motif types and all pins are connected; parameters may still be symbolic, and the representation remains PDK-agnostic.
- EL (Electrical Level): parameters are numeric wherever required by the spec, all pins are connected, and PDK-specific device choices have been recorded so that the document is SPICE-ready.

Rules in the rest of this chapter tighten as you move from HL to ML to EL; §3.7 lists the exact invariants per level.

---

## 3.1 Design Principles

The ACIR design prioritizes connectivity as the primary concern, establishing the terminal-to-net binding within each instance or slot as the sole source of truth for edges, deliberately avoiding duplication in canonical form. The uniform instance model ensures that after desugaring, every ADL structure becomes instances (or slots at HL) with terminals and parameters, with syntactic sugar for constructs like attach, pair, and feedback already expanded. Deterministic text output maintains stability by using consistent ordering and formatting, ensuring diff stability and CI compatibility. Elaboration levels provide flexibility through three distinct modes: HL (High Level, with `slot` declarations and symbolic sizing), ML (Mid Level, concrete motifs with possible symbolic parameters), and EL (Electrical Level, numeric and SPICE-ready), with pin coverage rules becoming more stringent at each level. Line-oriented format ensures that each statement occupies one logical line, facilitating grep operations, LLM comprehension, and unified diffs. Compact inline connections using `terminal->net` syntax within parentheses reduce verbosity while maintaining explicit keyword-argument clarity, avoiding the fragility of positional syntax. Source attribution via `@[file:line]` annotations enables precise error messages and debugging. Finally, the extensible non-leaky architecture places vendor or dialect fields under extension blocks, avoiding special-purpose modifications to the core model.

---

## 3.2 File Structure and Syntax

### 3.2.1 Character Encoding and Line Structure

ACIR files use UTF-8 encoding with LF line endings. Each logical statement occupies one line, with continuation indicated by indentation for nested content. Comments begin with `;` and extend to end of line.

```text
; This is a comment
ACIR 1.0  ; Version declaration with inline comment
```

### 3.2.2 Document Structure

An ACIR document begins with a version declaration, followed by optional bundle type definitions, then one or more circuit blocks.

```text
ACIR <major>.<minor>

[bundle definitions]

circuit <name> ...
  [level, package]
  [supply/ground/port declarations]
  fill:
    [nets, instances/devices]
  [constraints, harness, benches, provenance]

circuit <name> ...
  [circuit body]
```

A single ACIR file may contain multiple circuits, supporting compilation of related motifs together as a single unit.

The circuit body structure separates the declared interface (supplies, grounds, ports) from the synthesized implementation (contained in the `fill:` block at ML and EL levels). At HL level, slots appear at the circuit body level since they represent requirements rather than implementations.

### 3.2.3 Version Semantics

ACIR uses MAJOR.MINOR versioning semantics. Major version increments indicate breaking changes to the format, and readers must reject files with a different major version. Minor version increments indicate additive, backward-compatible additions such as optional fields or new syntax constructs. Readers must accept any minor version within the same major version and silently ignore unknown minor-level constructs. Readers must not contain conditional logic based on minor version.

Current version: `1.0`

### 3.2.4 Lexical Elements

Identifiers follow the pattern `[A-Za-z_][A-Za-z0-9_]*`. Pin paths extend identifiers with dot notation and array indexing: `ident ( "." ident | "[" int "]" )*`.

Numeric literals support integer and floating-point forms with optional SI unit suffixes:

```acir
42          ; integer
3.14        ; float
1.8V        ; voltage
100n        ; 100 nano (100e-9)
2.5u        ; 2.5 micro (2.5e-6)
1.2e-5m     ; explicit scientific with unit
```

The SI prefix table:

| Prefix | Symbol | Factor |
|--------|--------|--------|
| tera   | T      | 10^12  |
| giga   | G      | 10^9   |
| mega   | M      | 10^6   |
| kilo   | k      | 10^3   |
| milli  | m      | 10^-3  |
| micro  | u      | 10^-6  |
| nano   | n      | 10^-9  |
| pico   | p      | 10^-12 |
| femto  | f      | 10^-15 |

### 3.2.4 Source Attribution (Optional)

Statements may optionally include source attribution in the form `@[file:line]` or `@[file:line:column]`. Source attribution is **not required** and should be omitted in most cases. It is primarily useful for debugging, error messages, and tracing elaborated designs back to their ADL source.

```
port OUT : analog @[OTA.cas:7]
nmos dp.M_N (G->IN_P, D->OUT_N, S->tnode) : W=1u L=100n @[DiffPair.cas:12]
inst dp (IN.P->IN_P) : DiffPair @[OTA.cas:9]
  param p = NMOS
```

When present, source attribution enables error messages to reference original source locations. However, canonical ACIR output omits source attribution by default to improve readability and reduce noise.

---

## 3.3 Graph Model

ACIR models the circuit as a bipartite graph. Instance terminals connect to nets. The authoritative mapping is:

```
f: (instanceId, terminalPath) -> netId
```

### 3.3.1 Net Declarations

Nets represent electrical nodes in the circuit. Each net has a unique identifier within the circuit and a domain classification.

```
net <id> : <domain> [<attributes>]
```

The domain field specifies one of:

| Domain | Description | Usage |
|--------|-------------|-------|
| `supply` | Power supply rail | VDD, VDDIO |
| `ground` | Ground reference | GND, VSS |
| `analog` | General analog signal | Internal nodes |
| `bias` | Bias voltage/current | Gate biases |
| `digital` | Logic signal | Enable pins |
| `clock` | Clock signal | Sampling clocks |
| `rf` | Radio frequency signal | High-frequency paths |

**Net placement:**

- Nets created as part of port expansion (e.g., `IN_P`, `IN_N` from `port IN : Diff`) are implicit and do not require explicit declaration.
- Internal nets created during elaboration (e.g., `tnode`, `mirror_gate`) are declared within the `fill:` block at ML and EL levels.
- At HL level, internal nets may appear at the circuit body level if needed for slot connectivity.

Examples:

```
net VDD : supply
net GND : ground
net tnode : analog  ; internal tail node
net VTAIL : bias
net EN : digital
```

**Invariants:**

- A net id is unique within the circuit.
- Supply and ground nets referenced by instances must correspond to exactly one canonical net per name within the circuit.

### 3.3.2 Supply and Ground Declarations

Supplies and grounds are specialized net declarations that serve as power rails. Voltage values are specified in the harness, not in the circuit definition.

```
supply <id>
ground <id>
```

Examples:

```
supply VDD
supply VDDIO
ground GND
ground GNDA  ; analog ground
```

Supply declarations implicitly create nets with domain `supply`. Ground declarations implicitly create nets with domain `ground`. The actual voltage values for supplies are specified in the harness section, allowing the same circuit to be tested under different supply conditions.

### 3.3.3 Bundle Type Definitions

Bundles group related nets for convenience, most commonly differential pairs. Bundle types are declared at the file level before circuits.

```
bundle <TypeName>:
  <field> : <domain>
  <field> : <domain>
  ...
```

Example:

```
bundle Diff:
  P : analog
  N : analog

bundle QuadIQ:
  IP : analog
  IN : analog
  QP : analog
  QN : analog
```

**Built-in bundle type:** The `Diff` bundle is predefined with fields `P` and `N`, both of domain `analog`.

### 3.3.4 Port Declarations

Ports declare the external interface of a circuit. Each port has a name, a domain or bundle type, and optional source attribution.

```
port <name> : <domain|BundleType>
```

Examples:

```
port VIN : analog
port IN : Diff
port OUT : analog
port EN : digital
port VTAIL : bias
```

**Bundle port expansion:** A port declared with a bundle type expands to multiple underlying nets. For `port IN : Diff`, the nets `IN_P` and `IN_N` are created, accessible as `IN.P` and `IN.N` in terminal bindings.

### 3.3.5 Slot Declarations (HL)

At HL (High Level), slots represent placeholders for circuit components that will be resolved during synthesis. A slot declares the interface contract (terminal connections) and the behavioral requirements (traits) without specifying a concrete implementation.

**Syntax:**

```
slot <id> [(<connections>)] : <Trait>
  param <key> = <value>
  ...

slot <id> [(<connections>)] : [<Trait1>, <Trait2>, ...]
  param <key> = <value>
  ...
```

When a single trait is required, it appears directly after the colon. When multiple traits are required, they are enclosed in square brackets as a comma-separated list.

**Examples:**

```
slot load (node->vout, bias->vb1, vref->VDD) : LoadDevice

slot amp (IN->IN, OUT->OUT, VDD->VDD, VSS->VSS) : SingleEndedOpAmp
  param maxPower = 1m

slot driver (IN->sig, OUT->pad) : [BufferLike, HighDrive]
```

**Slot-to-Instance Resolution:**

During the HL->ML transition, the synthesis engine resolves each slot to a concrete motif type that satisfies all required traits. The slot becomes a regular `inst` declaration:

```
; HL
slot amp (IN->IN, OUT->OUT, VDD->VDD, VSS->VSS) : SingleEndedOpAmp

; ML (after synthesis resolves the slot)
inst amp (IN->IN, OUT->OUT, VDD->VDD, VSS->VSS) : OTA5TSingleEnded
  param p = NMOS
  param W = $Auto
```

The identifier is preserved, maintaining traceability from the original slot to its concrete implementation.

### 3.3.6 Instance Declarations (ML)

At ML (Mid Level), instances represent motif instantiations with type, parameters, and terminal bindings. Instance declarations appear within the `fill:` block.

**Syntax:**

```
fill:
  inst <id> [(<connections>)] : <MotifType>
    param <key> = <value>
    ...
    <terminal> -> <net>
    ...
```

The terminal bindings use arrow syntax (`terminal -> net`) to show the mapping from instance terminal to net. Connections may be specified inline in parentheses immediately following the instance identifier, or in the indented body, or both.

**Inline Connections:**

When an instance has few connections or they fit naturally on one line, use inline syntax:

```
fill:
  inst cm (RAIL->VDD, SENSE->mirror_gate, TAP[0]->OUT) : CurrentMirror
    param p = PMOS
    param taps = 1
```

**Multiline Connections:**

For readability with many connections or when combined with parameters, break across lines:

```
fill:
  inst dp : DiffPair
    param p = NMOS
    param hasTail = true
    IN.P -> IN_P
    IN.N -> IN_N
    OUT.P -> mirror_gate
    OUT.N -> OUT
    BASE -> GND
    BIAS -> VTAIL
```

**Bundle Connections:**

When a terminal and a net both share the same bundle type, a single binding connects all constituent fields recursively:

```
fill:
  net sig_in : Diff
  net sig_out : Diff

  ; Implicitly connects IN.P->sig_in.P, IN.N->sig_in.N
  inst dp (IN->sig_in, OUT->sig_out) : DiffPair
    param p = NMOS
```

**Terminal path grammar:**

```
terminalPath = ident ( "." ident | "[" int "]" )*
ident = [A-Za-z_][A-Za-z0-9_]*
int = [0-9]+
```

**Guidance:** External connectivity should prefer stable, named sub-terminals over numeric indices when a natural name exists (for example, `OUT.P` rather than `OUT[0]`). When a motif legitimately produces an ordered family, indices appear as `name[index]` and become part of the schema contract. Readers MUST treat `TAP[0]` as a single logical terminal path; bracket segments are not array lookups but syntactic components of the path.

**Inline vs. Multiline Guidance:** Use inline connections when they fit naturally on one line (typically 4 or fewer simple connections). Use multiline format when connections are numerous, complex, or need alignment for clarity. Both syntaxes may be mixed within the same instance.

### 3.3.7 Device Declarations (EL)

At EL (Electrical Level), primitive devices replace motif instances. Device declarations specify the device type, sizing parameters, and terminal connections. Device declarations appear within the `fill:` block.

**Transistors:**

```
fill:
  nmos <id> [(<connections>)] : <parameters>
    <terminal> -> <net>
    ...

  pmos <id> [(<connections>)] : <parameters>
    <terminal> -> <net>
    ...
```

Transistor parameters include `W` (width), `L` (length), `M` (multiplicity), and optionally the PDK device name.

Example:

```
fill:
  nmos dp.M_N (G->IN_P, D->mirror_gate, S->tnode) : W=1u L=100n M=1

  pmos cm.M_SENSE (G->mirror_gate, D->mirror_gate, S->VDD, B->VDD) : W=2u L=100n M=1 sky130_fd_pr__pfet_01v8
```

**Passives:**

```
fill:
  resistor <id> [(<connections>)] : R=<value>
    P -> <net>
    N -> <net>

  capacitor <id> [(<connections>)] : C=<value>
    P -> <net>
    N -> <net>

  inductor <id> [(<connections>)] : L=<value>
    P -> <net>
    N -> <net>
```

Example:

```
fill:
  capacitor Cc (P->comp_out, N->stage2_in) : C=1p

  resistor Rz (P->comp_out, N->stage2_in) : R=10k
```

**Diodes:**

```
diode <id> [(<connections>)] : <model>
  A -> <net>
  K -> <net>
```

### 3.3.8 Connection Statements (ML)

Explicit connection statements declare net-to-net or terminal-to-net connections that are not captured by instance bindings. Connection statements appear within the `fill:` block.

```
fill:
  connect <source> -> <dest>
```

Example:

```
fill:
  connect dp.OUT.N -> OUT
```

### 3.3.9 Elaboration of Attach and Connectors

ACIR does not serialize connectors or attach chains. The front-end elaborates all `attach …` and `attach … to … to …` chains by resolving connectors declared on interface traits (§2.8), then emitting explicit terminal bindings into instance declarations.

**Normative:**

- Connector resolution and attach chains are elaboration-time sugar. ACIR always contains explicit terminal->net bindings only.
- When a connector maps unnamed bundles, field-wise expansion uses identical field names (PascalCase; `Diff` uses `P`/`N`).
- If no connector applies or multiple apply without explicit disambiguation, the front-end must reject the source before ACIR emission.

**Example (after elaboration):**

The ADL statement `attach cm to dp` with a `CurrentMirrorLike -> DiffPairLike` connector elaborates to explicit terminal bindings where `cm.SENSE` connects to the same net as `dp.OUT.P`, and `cm.TAP[0]` connects to the same net as `dp.OUT.N`.

### 3.3.10 The `fill:` Block

The `fill:` block groups all synthesized and elaborated content, separating the circuit's **declared interface** (ports, supplies, grounds) from its **implementation** (instances, devices, internal nets).

**Syntax:**

```
fill:
  <net declarations>
  <instance declarations>
  <device declarations>
  <connection statements>
```

**Semantics:**

- At **ML level**, the `fill:` block contains internal `net` declarations and `inst` declarations resulting from slot resolution and elaboration.  
- **EL level** uses the `fill:` block for internal `net` declarations and primitive device declarations (`nmos`, `pmos`, `resistor`, `capacitor`, `inductor`, `diode`).  
- **HL level** does not use the `fill:` block; instead, slots remain at the circuit body level, representing requirements and contracts rather than synthesized implementations.

**Net placement:**

- Nets created as part of port expansion (e.g., `IN_P`, `IN_N` from `port IN : Diff`) are implicit and do not appear in the `fill:` block.
- Internal nets created during elaboration (e.g., `tnode`, `mirror_gate`) are declared within the `fill:` block.

**Example:**

```
circuit SimpleAmp
  level EL

  supply VDD
  ground VSS
  port IN : analog
  port OUT : analog

  fill:
    net tnode : analog
    nmos M_in (G->IN, D->OUT, S->VSS, B->VSS) : W=8u L=180n M=2 sky130_fd_pr__nfet_01v8
    pmos M_load (G->OUT, D->OUT, S->VDD, B->VDD) : W=2u L=180n M=2 sky130_fd_pr__pfet_01v8
```

The `fill:` block creates a clear structural separation between what the circuit promises (its interface) and how it is implemented (the synthesized content).

---

## 3.4 Derived Indices (Optional)

Tools routinely need fast graph queries. ACIR allows serializing derived views in an optional indices block. They are informative only and must match terminal bindings exactly.

```
indices:
  hash sha256:abc123...
  pin_to_net dp.IN.P -> VINP
  pin_to_net dp.OUT.N -> N1
  net_to_pins VINP <- dp.IN.P
  net_to_pins N1 <- dp.OUT.N, cm.SENSE
  adjacent dp -> cm, tail
```

The hash is computed from a canonical serialization of terminal bindings only. Readers must recompute and compare when indices are present. Writers should not serialize indices by default, reserving them for debugging scenarios or heavy-duty solvers that benefit from a warm cache.

---

## 3.5 Constraints and Measurement Intents

Constraints live alongside the graph and come in four main kinds. They are evaluated during synthesis, sizing, and verification.

```
constraints:
  numeric:
    c_gbw : GainBandwidth @ OUT >= 100M Hz
    c_gain : PassbandGain @ OUT >= 55 dB
    c_pm : PhaseMargin @ OUT >= 60 deg
    c_pwr : Power <= 2m W

  tech:
    t_lmin : L >= 180n m on *

  graph:
    g_card_tail : cardinality type:CurrentMirror in [1, 1]
    g_path : path_exists IN.P -> OUT through CurrentMirror

  measure:
    m_gbw : SEOpAmpACBench GainBandwidth @ OUT
    m_rise : StepToggle RiseTime @ PAD
```

### 3.5.1 Numeric Constraints

Numeric constraints express inequalities over metrics with explicit units and scope.

```
<id> : <metric> @ <node> <op> <value> <unit>
```

Operators: `>=`, `<=`, `==`, `>`, `<`

### 3.5.2 Technology Constraints

Technology constraints express limits on device parameters.

```
<id> : <param> <op> <value> <unit> on <scope>
```

Scope may be `*` (all devices), a type selector, or an instance id.

### 3.5.3 Graph Constraints

Graph constraints express structural properties of the circuit graph.

```
<id> : cardinality <selector> in [<min>, <max>]
<id> : path_exists <from> -> <to> [through <type>]
<id> : fanout <net> in [<min>, <max>]
```

### 3.5.4 Measurement Intents

Measurement intents specify what metrics should be extracted from simulation.

```
<id> : <bench> <metric> @ <node>
```

**Guidance:** Graph constraints operate on the derived incidence graph, leveraging the fact that explicit edges eliminate the need for wiring inference. Numeric constraints and measurement intents carry explicit units, with sizing tools responsible for conversion to internal SI base units.

---

## 3.6 Harness: Environment for Benches

The harness holds bench-only elements derived from ADL env blocks: supply values, bias voltages, source impedances, loads, and PVT selections. Harness elements are not part of the design graph and should not affect layout or LVS.

```
harness:
  supply VDD = 1.8V
  supply VDDIO = 3.3V
  bias VBIAS = 0.7V
  sweep InputDCCommonMode [0.3V:100mV:1.5V]
  source IN Z=50Ohm
  load OUT C=1pF
  icmr min=0.55V max=0.75V
  pvt TT@27C, SS@-40C, FF@125C
```

### Sweep Conditions

The `sweep` directive specifies DC bias conditions that vary across a range during bench execution. When present, all benches execute their analyses at each sweep point and report worst-case values.

**Syntax:**

```
sweep <ConditionName> [<start>:<step>:<stop>]     ; explicit step
sweep <ConditionName> [<start>:<stop>]            ; automatic step
sweep <ConditionName> [Auto]                      ; synthesis chooses range (HL/ML only)
```

**Examples:**

```
sweep InputDCBias [0.3V:100mV:1.5V]           ; SEAmp: sweep input bias with explicit step
sweep InputDCCommonMode [0.3V:1.5V]           ; SEOpAmp: sweep ICMR with auto step
sweep OutputDCCommonMode [0.5V:50mV:1.3V]     ; FDOpAmp: sweep OCMR with explicit step
```

**Automatic step sizing:** When the step parameter is omitted, the toolchain computes `step = (stop - start) / 20` clamped to the range [10mV, 100mV].

**Semantics:**

- The condition name (`InputDCBias`, `InputDCCommonMode`, `OutputDCCommonMode`) is topology-specific and must match the swept condition declared in the design's specification
- All benches listed in the `benches:` block must respect the sweep and execute analyses at each point
- Benches report worst-case values according to constraint directionality (minimum for `>=` constraints, maximum for `<=` constraints)
- For range constraints `in [X..Y]`, benches report both `_min` and `_max` metric values

**Resolution level (normative):**

- At EL, sweep ranges must be fully concrete (numeric start/stop/step). `sweep <ConditionName> [Auto]` must not appear in ACIR-EL.
- At HL (and optionally ML), `sweep <ConditionName> [Auto]` is permitted only as an explicit request for synthesis to choose an execution envelope. During lowering to EL, synthesis must resolve `[Auto]` to a concrete range and record that range in the EL harness for reproducibility.

**Example (underconstrained but explicit):**

```acir
; HL or ML: author requests that synthesis choose a sweep envelope
harness:
  sweep InputDCBias [Auto]
```

```acir
; EL: synthesis materializes the chosen envelope (example values)
harness:
  sweep InputDCBias [0.42V:50mV:1.07V]
```

### 3.6.1 Bias Resolution

Ports declared with domain `bias` represent DC operating points that must be resolved to specific voltage values before simulation. During the ML→EL transition, the sizing and biasing engine determines appropriate bias voltages based on the circuit topology and performance requirements. These resolved values appear in the harness block as `bias NET = VALUE` entries.

For example, a common-source amplifier with a PMOS active load requires a gate bias voltage to set the load device's operating point. The biasing engine selects a voltage that places the output near mid-rail while maintaining adequate headroom for signal swing. This value is recorded in the harness and emitted as an ideal DC voltage source during SPICE testbench generation.

### 3.6.2 Bench Configuration

ACIR lists selected benches and their configurations for reproducibility.

```
benches:
  SEOpAmpACBench
  StepToggle:
    node = COMP_OUT
    freq = 50M Hz
    duty = 0.5
    cycles = 3
```

Readers that do not understand a given bench must ignore its configuration block.

---

## 3.7 Elaboration Levels

ACIR files declare a level in the circuit header: HL, ML, or EL. Pin coverage and parameter rules depend on the level.

### 3.7.1 HL - High Level

Slots are declared using the `slot` keyword followed by an identifier, connections, and required traits. When a slot requires a single trait, the trait name appears directly after the colon. When multiple traits are required, they are enclosed in square brackets.

All terminals are connected to nets, but many parameters and some values may remain symbolic or null while connectivity is complete.

```
circuit OTA : SingleEndedOpAmp
  level HL
  ...
  slot load (node->vout, bias->vb1, vref->VDD) : LoadDevice
  slot amp (IN->IN, OUT->OUT, VDD->VDD, VSS->VSS) : [SingleEndedOpAmp, LowPower]
```

**Syntax:**

```
slot <id> [(<connections>)] : <Trait>
slot <id> [(<connections>)] : [<Trait1>, <Trait2>, ...]
```

The slot declaration captures the interface contract (connections) and the behavioral requirements (traits) that any concrete implementation must satisfy. During synthesis, slots are resolved to concrete motif types that implement the required traits.

### 3.7.2 ML - Mid Level

Slots are resolved to concrete motif types and become regular `inst` declarations. All terminals are connected to nets. Parameters may still be symbolic, and the representation remains PDK-agnostic. Instances and internal nets appear within the `fill:` block.

```
circuit OTA : SingleEndedOpAmp
  level ML
  ...
  fill:
    inst load (node->vout, bias->vb1, vref->VDD) : ActiveLoad
      param p = PMOS
      param W = $Auto
      param L = $Auto

    inst dp : DiffPair
      param p = NMOS
      param W = $Auto
      param L = $Auto
      ...
```

At ML, what was a `slot load : LoadDevice` at HL becomes `inst load : ActiveLoad` once the synthesis engine selects a concrete motif that satisfies the `LoadDevice` trait.

Symbolic parameters use the `$` prefix: `$Auto`, `$ratio`, `$W_input`.

### 3.7.3 EL - Electrical Level

Parameters are numeric wherever required by this specification. All terminals are connected, PDK-specific device choices have been recorded, and the document is ready for SPICE emission. Devices and internal nets appear within the `fill:` block.

```
circuit OTA
  level EL
  ...
  fill:
    nmos dp.M_N (G->IN_P, D->mirror_gate, S->tnode, B->GND) : W=2u L=180n M=1 sky130_fd_pr__nfet_01v8
```

At EL, the selected PDK device appears inline with device parameters. Hierarchical device names (e.g., `dp.M_N`) preserve the origin of each device from the ML elaboration.

---

## 3.8 Provenance and Diagnostics

Provenance links IR elements back to ADL source and records transformation steps. This enables precise diagnostics and reproducibility.

```
provenance:
  sources:
    examples/OTA5T.cas [1:120]
  transforms:
    desugar.attach
    slot.fill
    sizing.geometric
  aliases:
    nN = dp.OUT.N
    nP = dp.OUT.P
```

---

## 3.9 Validation Rules

ACIR validation executes after build completion and before consumption by downstream passes, enforcing several invariants:

- **Terminal coverage:** every required terminal path for every instance appears exactly once at ML and EL.
- **Bundle completeness:** any referenced bundle field resolves to a concrete net id at ML and EL.
- **Domain compatibility:** terminal kind and net domain are compatible according to the library schema.
- **Device selection at EL:** primitive device declarations MUST include the PDK device name.
- **Rail uniqueness:** each named rail such as VDD or GND maps to one net id across the circuit.
- **No dangling nets:** any net with zero incident terminals is pruned unless referenced by harness.
- **Indices consistency:** when indices are present, the hash matches a recomputed hash from terminal bindings.
- **Allowed loops:** cycles are allowed unless explicitly forbidden by rule or library schema. Algebraic loops of ideal passives without controlled sources may be rejected.

Diagnostics leverage source attribution via `@[file:line]` annotations, ensuring error messages point to the specific ADL construct that introduced the problematic edge or parameter.

---

## 3.10 Diagnostics

The ACIR reader emits structured diagnostics when parsing fails or encounters malformed input. Each diagnostic includes a code, severity, message, file path, and line/column location. Diagnostics follow the same pattern as the Cascode compiler's `Diagnostic` type.

### Diagnostic Codes

| Code | Severity | Description |
|------|----------|-------------|
| ACIR0001 | Error | General parse failure (e.g., I/O error, unexpected exception) |
| ACIR0002 | Error/Warning | Invalid or missing version declaration; expects `ACIR <number>` |
| ACIR0003 | Error | Malformed circuit or bundle declaration |
| ACIR0004 | Error | Invalid device declaration syntax |
| ACIR0005 | Warning | Malformed binding syntax; expects `TERMINAL->NET` |
| ACIR0006 | Error | Invalid sweep range specification; expects `[start:stop]` or `[start:step:stop]` |
| ACIR0007 | Error | ACIR major version mismatch; reader rejects different major versions |
| ACIR0010 | Error | Parallel load specification missing parentheses; expects `(C=... \|\| R=...)` |
| ACIR0011 | Error | Parallel load specification missing `\|\|` operator between elements |
| ACIR0012 | Error | Parallel load specification missing first element (before `\|\|`) |
| ACIR0013 | Error | Parallel load specification missing second element (after `\|\|`) |
| ACIR0014 | Error | Parallel load element missing value; expects `C=<value>` or `R=<value>` |

### Programmatic Access

Use `ACIRReader.TryRead()` or `ACIRReader.TryParse()` to obtain an `ACIRReadResult` containing the parsed document and any diagnostics:

```csharp
var result = ACIRReader.TryRead(reader, "path/to/file.cir");
if (!result.Success)
{
    foreach (var diag in result.Diagnostics)
        Console.WriteLine($"{diag.FilePath}:{diag.Line}: {diag.Message}");
}
```

`ACIRReader.Read()` throws `ACIRParseException` on fatal errors. For structured error handling in tooling, use the `TryRead` variants which return diagnostics without throwing.

---

## 3.11 Core IR Operations

The synthesis and optimization engine modifies the graph through a constrained set of operations that update terminal bindings and mark indices dirty:

- `add_instance(type, id, bindings, params)`
- `bind(inst.terminalPath, netId)`
- `unbind(inst.terminalPath)`
- `new_net(id, domain)`
- `merge_nets(a, b)`
- `split_net(n, partition)`
- `replace_subgraph(patternId, binder)`
- `set_param(inst, name, value)`

High-level patterns and syntactic sugar in ADL-including attach, pair, and feedback constructs-lower to sequences of these primitive operations during the desugaring phase.

---

## 3.12 Complete Examples

### 3.12.1 ML ACIR for OTA5TSingleEnded

This example shows the ML representation of a five-transistor OTA with differential input and single-ended output.

```
ACIR 1.0

bundle Diff:
  P : analog
  N : analog

circuit OTA5TSingleEnded : SingleEndedOpAmp
  level ML
  package analog.ota

  supply VDD
  ground GND

  port IN : Diff
  port OUT : analog
  port VTAIL : bias

  fill:
    net mirror_gate : analog  ; dp.OUT.P = cm.SENSE via attach
    net tnode : analog        ; internal tail node from dp

    inst dp (IN->IN, OUT.N->OUT, BASE->GND, BIAS->VTAIL, OUT.P->mirror_gate) : DiffPair
      param p = NMOS
      param hasTail = true

    inst cm (RAIL->VDD, SENSE->mirror_gate, TAP[0]->OUT) : CurrentMirror
      param p = PMOS
      param taps = 1

    ; attach cm -> dp elaborated into shared mirror_gate net


circuit DiffPair : DiffPairLike
  level ML
  package lib.std.prim

  port IN : Diff
  port OUT : Diff
  port BASE : analog
  port BIAS : bias

  fill:
    net IN_P : analog
    net IN_N : analog
    net OUT_P : analog
    net OUT_N : analog
    net tnode : analog

    inst M_N (G->IN_P, D->OUT_N, S->tnode) : MOS
      param p = $p

    inst M_P (G->IN_N, D->OUT_P, S->tnode) : MOS
      param p = $p

    inst M_TAIL (G->BIAS, D->tnode, S->BASE) : MOS
      param p = $p


circuit CurrentMirror : CurrentMirrorLike
  level ML
  package lib.std.prim

  port RAIL : supply
  port SENSE : analog
  port TAP[0] : analog

  fill:
    inst M_SENSE (G->SENSE, D->SENSE, S->RAIL) : MOS
      param p = $p

    inst M_TAP0 (G->SENSE, D->TAP[0], S->RAIL) : MOS
      param p = $p
```

### 3.12.2 EL ACIR for OTA5TSingleEnded (Fully Flattened)

At EL, all motifs are expanded to primitive devices. The circuit is fully flattened with hierarchical naming preserved for traceability.

```
ACIR 1.0

circuit OTA5TSingleEnded
  level EL

  supply VDD
  ground GND

  port IN_P : analog
  port IN_N : analog
  port OUT : analog
  port VTAIL : bias

  fill:
    net tnode : analog        ; from dp.tnode
    net mirror_gate : analog  ; dp.OUT.P = cm.SENSE

    ; DiffPair (dp) - NMOS differential pair with tail
    nmos dp.M_N (G->IN_P, D->mirror_gate, S->tnode, B->GND) : W=2u L=180n M=1 sky130_fd_pr__nfet_01v8
    nmos dp.M_P (G->IN_N, D->OUT, S->tnode, B->GND) : W=2u L=180n M=1 sky130_fd_pr__nfet_01v8
    nmos dp.M_TAIL (G->VTAIL, D->tnode, S->GND, B->GND) : W=4u L=180n M=1 sky130_fd_pr__nfet_01v8

    ; CurrentMirror (cm) - PMOS current mirror
    pmos cm.M_SENSE (G->mirror_gate, D->mirror_gate, S->VDD, B->VDD) : W=2u L=180n M=1 sky130_fd_pr__pfet_01v8
    pmos cm.M_TAP0 (G->mirror_gate, D->OUT, S->VDD, B->VDD) : W=2u L=180n M=1 sky130_fd_pr__pfet_01v8

  constraints:
    numeric:
      c_gbw : GainBandwidth @ OUT >= 50M Hz
      c_gain : PassbandGain @ OUT >= 55 dB
      c_pm : PhaseMargin @ OUT >= 60 deg
      c_pwr : Power <= 2m W

    tech:
      t_lmin : L >= 180n m on *

    measure:
      m_gbw : SEOpAmpACBench GainBandwidth @ OUT

  harness:
    supply VDD = 1.8V
    load OUT C=1pF
    icmr min=0.55V max=0.75V
    pvt TT@27C

  benches:
    SEOpAmpACBench
    Step
```

### 3.12.3 ML ACIR for Stdcell Buffer

This example demonstrates a stdcell inverter used as an output buffer, showing how digital standard cells integrate with the ACIR format.

```
ACIR 1.0

circuit LatchPadBuffer
  level ML

  supply VDD
  ground GND

  port COMP_OUT : digital
  port PAD : digital

  fill:
    inst Buf (IN->COMP_OUT, OUT->PAD, VDD->VDD, GND->GND, VPB->VDD, VNB->GND) : sky130_fd_sc_hd__inv_4 [InverterLike]

  constraints:
    numeric:
      c_rise : RiseTime @ PAD <= 1.2n s
      c_fall : FallTime @ PAD <= 1.2n s
      c_voh : VOH @ PAD >= 0.9 VDD
      c_vol : VOL @ PAD <= 0.1 VDD

    measure:
      m_rise : StepToggle RiseTime @ PAD
      m_fall : StepToggle FallTime @ PAD

  harness:
    supply VDD = 1.8V
    load PAD C=15pF

  benches:
    StepToggle:
      node = COMP_OUT
      freq = 50M Hz
      duty = 0.5
      cycles = 3
```

### 3.12.4 EL ACIR for CS Amplifier with Primitive Transistor

This example demonstrates a single-ended common-source amplifier using a primitive NMOS input transistor and an ActiveLoad motif.

```
ACIR 1.0

circuit CSAmplifier
  level EL

  supply VDD
  ground GND

  port vin : analog
  port vout : analog
  port vb1 : bias

  fill:
    nmos M_in (G->vin, D->vout, S->GND, B->GND) : W=12u L=180n M=4 sky130_fd_pr__nfet_01v8

    pmos load.M1 (G->vb1, D->vout, S->VDD, B->VDD) : W=4u L=180n M=2 sky130_fd_pr__pfet_01v8

  constraints:
    numeric:
      c_gbw : GainBandwidth @ vout >= 50M Hz
      c_gain : PassbandGain @ vout >= 40 dB
      c_pm : PhaseMargin @ vout >= 60 deg
      c_pwr : Power <= 5m W

    tech:
      t_lmin : L >= 180n m on *

    measure:
      m_gbw : SEAmpACBench GainBandwidth @ vout

  harness:
    supply VDD = 1.8V
    bias vb1 = 0.7V
    load vout C=1pF
    source vin Z=50Ohm
    pvt TT@27C

  benches:
    SEAmpACBench
    Step
```

The `bias vb1 = 0.7V` entry specifies the DC voltage for the PMOS load's gate bias. This value was determined during ML→EL elaboration to place the output at approximately mid-rail (0.9V) under nominal operating conditions.

---

## 3.13 SPICE Emission

SPICE emission is a direct traversal over device declarations. No connectivity inference is required.

1. For each device declaration, determine the SPICE element type (M for transistors, R for resistors, C for capacitors, etc.).
2. Read the terminal bindings to determine node names in the correct order for SPICE syntax.
3. Print parameter values. For transistors, emit `W=`, `L=`, `m=` parameters.
4. Append harness devices and analysis statements generated from benches and measure intents.

Because terminal bindings hold all edges, node substitution is O(1) per terminal. This keeps the SPICE writer small, predictable, and testable.

**Example SPICE output for OTA5TSingleEnded:**

```spice
* OTA5TSingleEnded - Generated from ACIR EL
* Source: lib/std/amp/ota/OTA5TSingleEnded.cas

.subckt OTA5TSingleEnded IN_P IN_N OUT VTAIL VDD GND

* DiffPair (dp)
M_dp.M_N mirror_gate IN_P tnode GND sky130_fd_pr__nfet_01v8 W=2u L=180n m=1
M_dp.M_P OUT IN_N tnode GND sky130_fd_pr__nfet_01v8 W=2u L=180n m=1
M_dp.M_TAIL tnode VTAIL GND GND sky130_fd_pr__nfet_01v8 W=4u L=180n m=1

* CurrentMirror (cm)
M_cm.M_SENSE mirror_gate mirror_gate VDD VDD sky130_fd_pr__pfet_01v8 W=2u L=180n m=1
M_cm.M_TAP0 OUT mirror_gate VDD VDD sky130_fd_pr__pfet_01v8 W=2u L=180n m=1

.ends OTA5TSingleEnded
```

**Notes:**

- Primitive transistor devices emit as SPICE M-devices directly.
- The PDK device name from the ACIR declaration becomes the SPICE model name.
- Terminal order follows SPICE convention: drain, gate, source, bulk for MOSFETs.
- Hierarchical names from ACIR (e.g., `dp.M_N`) become SPICE instance names (e.g., `M_dp.M_N`).

---

## 3.14 Canonical Writer Rules

To keep diffs and golden tests stable, the canonical writer follows these rules:

- Order circuits by dependency (referenced circuits before referencing circuits).
- Within a circuit, order sections: level, package, supplies, grounds, ports, fill (containing: nets, instances/devices), constraints, harness, benches, provenance.
- Within the `fill:` block, order: nets first, then instances/devices sorted by id lexicographically.
- Sort terminal bindings within an instance alphabetically by terminal path (whether inline or indented).
- Sort constraints by id within each category.
- Use consistent indentation: two spaces per level.
- Use plain numbers; forbid NaN and Infinity.
- Always include units for physical quantities.
- Use UTF-8, LF, and no trailing spaces.
- Prefer inline connection syntax when an instance has 4 or fewer simple connections and no complex terminal paths.
- Use multiline indented format when connections are numerous, complex, or benefit from vertical alignment.
- Within inline connections, maintain alphabetical order of terminal paths.

---

## 3.15 Extensibility

Vendor or dialect additions live under extension blocks. Extensions must not redefine core keywords. If an extension affects connectivity semantics, it must include a versioned schema and a compatibility note.

```
circuit MyCircuit
  level EL
  ...
  extensions:
    vendor.timing:
      setup_time = 100p s
      hold_time = 50p s
    vendor.layout:
      placement_hint = "top_left"
```

---

## 3.16 Conformance and Testing

A conformant ACIR producer must satisfy the following requirements:

- Emits instances/devices with terminal bindings that cover all required terminals at the declared level.
- Emits nets consistent with terminal bindings.
- Emits constraints with explicit units and resolvable node references.
- At EL, emits primitive devices with PDK device names.
- Ensures indices, if present, match terminal bindings according to the hash.

The testing strategy encompasses three complementary approaches:

- Golden ACIR snapshots under `tests/golden/acir/`.
- Connectivity unit tests rebuild incidence from terminal bindings and assert simple graph properties: path_exists, cardinality, and fanout.
- Round-trip tests from ADL to ACIR to SPICE for small examples.

---

## 3.17 Cascode -> ACIR -> SPICE Pipeline

The transformation from ADL to SPICE follows a systematic progression through ACIR. Parsing and desugaring map ADL constructs to instances and nets, expanding high-level constructs like attach, pair, and feedback into concrete motifs and connections. ACIR captures these connections uniformly within terminal bindings, enabling the synthesis engine to perform path queries and edits directly without inferring wiring relationships.

Sizing augments parameter values without modifying connectivity, and once all parameters become numeric, the IR reaches EL status and becomes ready for emission. SPICE writing reads terminal bindings to determine node names and prints devices according to SPICE conventions, with harness elements and bench configurations derived from constraints and harness specifications.

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│  Cascode    │     │   ACIR HL   │     │   ACIR ML   │     │   ACIR EL   │
│    ADL      │────▶│  (slots)    │────▶│  (motifs)   │────▶│  (devices)  │
└─────────────┘     └─────────────┘     └─────────────┘     └─────────────┘
                          │                   │                   │
                          │ slot.fill         │ elaborate         │ emit
                          ▼                   ▼                   ▼
                    ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
                    │  Synthesis  │     │   Sizing    │     │   SPICE     │
                    │   Engine    │     │   Engine    │     │   Output    │
                    └─────────────┘     └─────────────┘     └─────────────┘
```

This architectural separation maintains focus on structure and semantics in the front end, while the back end treats ACIR as a stable, mechanical contract.

---

## 3.18 Comparison with JSON-based CasIR

ACIR replaces the previous JSON-based CasIR format. The following table summarizes the key differences:

| Aspect | JSON CasIR | ACIR |
|--------|-----------|------|
| Format | JSON | Line-oriented text |
| Readability | Moderate (nested, verbose) | High (one concept per line) |
| Diff-friendliness | Poor (reordering breaks diffs) | Good (stable ordering) |
| LLM token efficiency | Low | High |
| Grep-ability | Awkward | Natural |
| Parse complexity | JSON libraries | Simple line parser |
| Source attribution | Separate provenance section | Inline `@[...]` |
| Multiple circuits | Separate files or complex nesting | Single file, sequential |

The text-based format was chosen to maximize:

1. **Human readability** during debugging and review.
2. **LLM comprehension** for AI-assisted circuit design.
3. **Diff stability** for version control and golden tests.
4. **Tool integration** via grep, sed, and other text utilities.

---

## 3.19 Grammar Summary

The following EBNF-style grammar summarizes ACIR syntax:

```ebnf
document     = "ACIR" MAJOR "." MINOR NL (bundleDef)* (circuit)+ ;
MAJOR        = [0-9]+ ;
MINOR        = [0-9]+ ;

bundleDef    = "bundle" IDENT ":" NL (INDENT field NL)+ ;
field        = IDENT ":" domain ;

circuit      = "circuit" IDENT (":" traits)? source? NL circuitBody ;
traits       = IDENT ("," IDENT)* ;
circuitBody  = (INDENT statement NL)* ;

statement    = levelDecl | packageDecl | supplyDecl | groundDecl
             | portDecl | slotDecl | fillBlock | constraintsBlock | harnessBlock
             | benchesBlock | provenanceBlock | extensionsBlock ;

levelDecl    = "level" ("HL" | "ML" | "EL") ;
packageDecl  = "package" qualifiedName ;
supplyDecl   = "supply" IDENT source? ;
groundDecl   = "ground" IDENT source? ;
portDecl     = "port" IDENT ":" (domain | IDENT) source? ;

slotDecl     = "slot" IDENT connectionList? ":" (IDENT | traitList) source? NL (INDENT slotBody NL)* ;
traitList    = "[" IDENT ("," IDENT)* "]" ;
slotBody     = paramDecl ;

fillBlock    = "fill:" NL (INDENT INDENT fillContent NL)* ;
fillContent  = netDecl | instDecl | deviceDecl | connectStmt ;

netDecl      = "net" IDENT ":" domain source? ;

instDecl     = "inst" IDENT connectionList? ":" IDENT traits? source? NL (INDENT instBody NL)* ;
instBody     = paramDecl | binding ;
paramDecl    = "param" IDENT "=" paramValue ;
binding      = terminalPath "->" IDENT ;
connectionList = "(" connection ("," connection)* ")" ;
connection   = terminalPath "->" IDENT ;

deviceDecl   = deviceType IDENT connectionList? ":" deviceParams pdkDevice? source? NL (INDENT binding NL)* ;
deviceType   = "nmos" | "pmos" | "resistor" | "capacitor" | "inductor" | "diode" ;
deviceParams = (IDENT "=" value)+ ;
pdkDevice    = IDENT ;

connectStmt  = "connect" terminalPath "->" IDENT source? ;

harnessBlock = "harness:" NL (INDENT harnessEntry NL)* ;
harnessEntry = supplyAssign | biasAssign | sweepDecl | loadDecl | sourceDecl | icmrDecl | pvtDecl ;
supplyAssign = "supply" IDENT "=" value ;
biasAssign   = "bias" IDENT "=" value ;
sweepDecl    = "sweep" IDENT sweepRange ;
sweepRange   = "[" value ":" value ":" value "]"    ; start:step:stop (explicit step)
             | "[" value ":" value "]" ;             ; start:stop (auto step)
             | "[" "Auto" "]" ;                      ; synthesis-chosen (HL/ML only)
loadDecl     = "load" IDENT loadSpec ;
loadSpec     = "C=" value
             | "(" loadElement "||" loadElement ")" ;
loadElement  = "C=" value | "R=" value ;
sourceDecl   = "source" IDENT "Z=" value ;
icmrDecl     = "icmr" "min=" value "max=" value ;
pvtDecl      = "pvt" cornerList ;
cornerList   = corner ("," corner)* ;
corner       = IDENT "@" value ;

terminalPath = IDENT ("." IDENT | "[" INT "]")* ;
qualifiedName= IDENT ("." IDENT)* ;
domain       = "supply" | "ground" | "analog" | "bias" | "digital" | "clock" | "rf" ;
value        = NUMBER SIUNIT ;
SIUNIT       = SIPREFIX? BASEUNIT ;
SIPREFIX     = "f" | "p" | "n" | "u" | "m" | "k" | "M" | "G" | "T" ;
BASEUNIT     = "V" | "A" | "F" | "Ohm" | "H" | "Hz" | "W" | "s" ;
paramValue   = value | "$" IDENT | IDENT ;
source       = "@[" STRING "]" ;

IDENT        = [A-Za-z_][A-Za-z0-9_]* ;
NUMBER       = [0-9]+ ("." [0-9]*)? ([eE][+-]?[0-9]+)? ;
UNIT         = [A-Za-z]+ ;
INT          = [0-9]+ ;
STRING       = [^\]]+ ;
NL           = "\n" ;
INDENT       = "  " ;
```

This grammar is informative; the normative specification is the prose in this chapter.
