# ACIR: Chapter 3 - Analog Circuit Intermediate Representation

> This chapter defines ACIR as a data model and text-based format that carries circuit connectivity and analysis intent from Cascode ADL to synthesis, sizing, verification, and SPICE emission.

The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY", and "OPTIONAL" in this document are to be interpreted as described in [RFC 2119](https://www.ietf.org/rfc/rfc2119.txt).

---

## 3.0 Summary

ACIR serves as the single, authoritative handoff between the Cascode front end and the rest of the toolchain. Its role is both simple and critical: representing every connection as a binding from an instance terminal to a net, preserving sufficient structure and metadata to support search operations, rewrites, sizing, and benchmark generation, while maintaining deterministic output that facilitates diff operations and serves as a golden artifact in tests and reviews.

If we do this well, getting from ADL to SPICE is a straight line: parse and elaborate ADL into instances and nets, write ACIR, pick and size implementations, then print SPICE by looking up port ordering in library templates and substituting the already-known node names.

Most compilers follow a familiar arc: lex and parse source into an abstract syntax tree (AST), lower that AST into an intermediate representation (IR), run optimizations on the IR, then lower again to a concrete target before emission. Cascode follows the same shape; the interesting design work lives in the ACIR and optimization stages. This chapter focuses on that IR step in the context of the Cascode front end.

ACIR is produced at three elaboration levels that describe how far the front end has progressed:

- HL (High Level): design slots may remain open (declared with the `slot` keyword), and many parameters can stay symbolic or null, but connectivity through nets and ports is already complete.
- ML (Mid Level): all slots have been bound to concrete motif types and all pins are connected; parameters may still be symbolic, and the representation remains PDK-agnostic.
- EL (Electrical Level): parameters are numeric wherever required by the spec, all pins are connected, and PDK-specific device choices have been recorded so that the document is SPICE-ready. EL supports both fully flattened form (primitive devices only) and hierarchical form (circuit instances with attach-based composition).

Rules in the rest of this chapter tighten as you move from HL to ML to EL; [§3.7](#37-elaboration-levels) lists the exact invariants per level.

---

## 3.1 Design Principles

Connectivity is the primary concern of ACIR. Every electrical relationship is expressed as a mapping from instance terminals to nets, and the resolved form deliberately avoids redundant encodings; a terminal binds to exactly one net, and that binding is the single source of truth for downstream tools.

ACIR optionally carries higher-level connectivity constraints expressed through `attach` and `connect` statements. When present, these constraints must be resolved deterministically into a concrete terminal-to-net mapping before validation, indexing, or SPICE emission proceeds.

Text output is deterministic. Instance and net orderings follow stable rules, and formatting is consistent across serialization runs. This guarantees diff stability and allows CI pipelines to compare golden files directly.

Elaboration proceeds through three levels: HL (High Level) permits open slots and symbolic sizing, ML (Mid Level) requires all slots bound to concrete motifs while allowing symbolic parameters, and EL (Electrical Level) demands numeric values and PDK-specific device choices so that the document is SPICE-ready. Pin-coverage rules tighten at each level, and [§3.7](#37-elaboration-levels) enumerates the exact invariants.

The format is line-oriented. Each statement occupies one logical line, which simplifies grep-based searches, improves LLM comprehension, and produces clean unified diffs.

Inline connection syntax of the form `terminal->net` reduces verbosity while preserving explicit keyword-argument clarity. This avoids the fragility inherent in positional port orderings.

Source attribution annotations (`@[file:line]`) trace each ACIR element back to its origin in the ADL source, enabling precise error messages and efficient debugging.

Extension and dialect fields belong in dedicated extension blocks. Vendor-specific or experimental features must not modify the core model, keeping the base representation stable and portable.

---

## 3.2 File Structure and Syntax

### 3.2.1 Character Encoding and Line Structure

ACIR files use UTF-8 encoding with LF line endings. Each logical statement occupies one line, with continuation indicated by indentation for nested content. Comments begin with `//` and extend to end of line.

```acir
// This is a comment
ACIR 2.0  // Version declaration with inline comment
```

### 3.2.2 Document Structure

An ACIR document begins with a version declaration, followed by optional bundle type definitions, optional trait definitions, then one or more circuit blocks.

```acir
ACIR <major>.<minor>

[bundle definitions]

[trait definitions]

circuit <name> ...
  [level, package]
  [supply/ground/port declarations]
  fill:
    [nets, instances/devices]
  [constraints, harness, benches, provenance]

circuit <name> ...
  [circuit body]
```

A single ACIR file may contain multiple circuits, supporting compilation of related motifs together as a single unit. At EL level, circuits may instantiate other circuits defined in the same document, enabling hierarchical composition while maintaining a single-file representation.

Circuit ordering: When a document contains multiple circuits with instantiation relationships, the top-level circuit (the one not instantiated by any other circuit in the document) appears FIRST in the file, followed by its child circuits. This top-first ordering ensures readers encounter the design's entry point immediately.

The circuit body structure separates the declared interface (supplies, grounds, ports) from the synthesized implementation (contained in the `fill:` block at ML and EL levels). At HL level, slots appear at the circuit body level since they represent requirements rather than implementations.

### 3.2.3 Version Semantics

ACIR uses MAJOR.MINOR versioning semantics. Major version increments indicate breaking changes to the format, and readers must reject files with a different major version. Minor version increments indicate additive, backward-compatible additions such as optional fields, metadata, or other constructs that a reader may safely ignore without changing circuit connectivity or observable behavior. Readers must accept any minor version within the same major version and silently ignore unknown minor-level constructs only when doing so is behavior-preserving. Readers must not contain conditional logic based on minor version.

Additions that affect circuit connectivity (for example, new ways to create or merge nets) are not ignorable and therefore require a MAJOR version bump.

Current version: `2.0`

### 3.2.4 Lexical Elements

Identifiers follow the pattern `[A-Za-z_][A-Za-z0-9_]*`. Pin paths extend identifiers with dot notation and array indexing: `ident ( "." ident | "[" int "]" )*`.

ACIR also uses hierarchical *symbols* (for example, for device ids and internal net ids) in contexts where a dotted name is useful. A symbol is a dot-separated sequence of identifiers: `ident ("." ident)*`. Symbols are distinct from pin paths: pin paths may include bracketed indices such as `TAP[0]`.

Numeric literals support integer and floating-point forms with optional SI unit suffixes:

```acir
42          // integer
3.14        // float
1.8V        // voltage
100n        // 100 nano (100e-9)
2.5u        // 2.5 micro (2.5e-6)
1.2e-5m     // explicit scientific with unit
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

### 3.2.5 Source Attribution (Optional)

Statements may optionally include source attribution in the form `@[file:line]` or `@[file:line:column]`. Source attribution is **not required** and should be omitted in most cases. It is primarily useful for debugging, error messages, and tracing elaborated designs back to their ADL source.

```acir
port OUT : analog @[OTA.cas:7]
nmos dp.M_N (G->IN_P, D->OUT_N, S->tnode) : size=(W=1u, L=100n, M=1) nfet_01v8 @[DiffPair.cas:12]
inst dp (IN.P->IN_P) : DiffPair @[OTA.cas:9]
  size Input = (W=2u, L=180n, M=1)
```

When present, source attribution enables error messages to reference original source locations. However, canonical ACIR output omits source attribution by default to improve readability and reduce noise.

---

## 3.3 Graph Model

ACIR models the circuit as a bipartite graph. Instance terminals connect to nets. The authoritative mapping is:

```acir
f: (instanceId, terminalPath) -> netId
```

### 3.3.1 Net Declarations

Nets represent electrical nodes in the circuit. Each net has a unique identifier within the circuit and a domain classification.

```acir
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

Net placement:

- Nets created as part of port expansion (e.g., `IN_P`, `IN_N` from `port IN : Diff`) are implicit and do not require explicit declaration.
- Internal nets created during elaboration (e.g., `tnode`, `mirror_gate`) are declared within the `fill:` block at ML and EL levels.
- At HL level, internal nets may appear at the circuit body level if needed for slot connectivity.

Examples:

```acir
net VDD : supply
net GND : ground
net tnode : analog  // internal tail node
net VTAIL : bias
net EN : digital
```

Invariants:

- A net id is unique within the circuit.
- Supply and ground nets referenced by instances must correspond to exactly one canonical net per name within the circuit.

### 3.3.2 Supply and Ground Declarations

Supplies and grounds are specialized net declarations that serve as power rails. Voltage values are specified in the harness, not in the circuit definition.

```acir
supply <id>
ground <id>
```

Examples:

```acir
supply VDD
supply VDDIO
ground GND
ground GNDA  // analog ground
```

Supply declarations implicitly create nets with domain `supply`. Ground declarations implicitly create nets with domain `ground`. The actual voltage values for supplies are specified in the harness section, allowing the same circuit to be tested under different supply conditions.

### 3.3.3 Bundle Type Definitions

Bundles group related nets for convenience, most commonly differential pairs. Bundle types are declared at the file level before circuits.

```acir
bundle <TypeName>:
  <field> : <domain>
  <field> : <domain>
  ...
```

Example:

```acir
bundle Diff:
  P : analog
  N : analog

bundle QuadIQ:
  IP : analog
  IN : analog
  QP : analog
  QN : analog
```

Built-in bundle type: The `Diff` bundle is predefined with fields `P` and `N`, both of domain `analog`.

### 3.3.4 Port Declarations

Ports declare the external interface of a circuit. Each port has a name, a domain or bundle type, and optional source attribution.

```acir
port <name> : <domain|BundleType>
```

Examples:

```acir
port VIN : analog
port IN : Diff
port OUT : analog
port EN : digital
port VTAIL : bias
```

Bundle port expansion: A port declared with a bundle type expands to multiple underlying nets. For `port IN : Diff`, the nets `IN_P` and `IN_N` are created, accessible as `IN.P` and `IN.N` in terminal bindings.

### 3.3.5 Slot Declarations (HL)

At HL (High Level), slots represent placeholders for circuit components that will be resolved during synthesis. A slot declares the interface contract (terminal connections) and the behavioral requirements (traits) without specifying a concrete implementation.

Syntax:

```acir
slot <id> [(<connections>)] : <Trait>
  param <key> = <value>
  ...

slot <id> [(<connections>)] : [<Trait1>, <Trait2>, ...]
  param <key> = <value>
  ...
```

When a single trait is required, it appears directly after the colon. When multiple traits are required, they are enclosed in square brackets as a comma-separated list.

Examples:

```acir
slot load (node->vout, bias->vb1, vref->VDD) : LoadDevice

slot amp (IN->IN, OUT->OUT, VDD->VDD, VSS->VSS) : SingleEndedOpAmp
  param maxPower = 1m

slot driver (IN->sig, OUT->pad) : [BufferLike, HighDrive]
```

Slot-to-Instance Resolution:

During the HL->ML transition, the synthesis engine resolves each slot to a concrete motif type that satisfies all required traits. The slot becomes a regular `inst` declaration:

```acir
// HL
slot amp (IN->IN, OUT->OUT, VDD->VDD, VSS->VSS) : SingleEndedOpAmp

// ML (after synthesis resolves the slot)
inst amp (IN->IN, OUT->OUT, VDD->VDD, VSS->VSS) : OTA5TSingleEnded
  param p = NMOS
  param W = $Auto
```

The identifier is preserved, maintaining traceability from the original slot to its concrete implementation.

### 3.3.6 Instance Declarations (ML and EL)

Instances represent circuit or motif instantiations with type, parameters, and terminal bindings. Instance declarations appear within the `fill:` block.

At ML level, instances reference motif types. At EL level, instances may reference other circuits defined in the same ACIR document, enabling hierarchical composition while maintaining explicit connectivity.

Syntax:

```acir
fill:
  inst <id> [(<connections>)] : <CircuitOrMotifType>
    param <key> = <value>
    size <name> = (<key>=<expr>, <key>=<expr>, ...)
    ...
    <terminal> -> <net>
    ...
```

The terminal bindings use arrow syntax (`terminal -> net`) to show the mapping from instance terminal to net. Connections may be specified inline in parentheses immediately following the instance identifier, or in the indented body, or both.

Inline Connections:

When an instance has few connections or they fit naturally on one line, use inline syntax:

```acir
fill:
  inst cm (RAIL->VDD, SENSE->mirror_gate, TAP[0]->OUT) : CurrentMirror
    param p = PMOS
    param taps = 1
```

Multiline Connections:

For readability with many connections or when combined with parameters, break across lines:

```acir
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

Bundle Connections:

When a terminal and a net both share the same bundle type, a single binding connects all constituent fields recursively:

```acir
fill:
  net sig_in : Diff
  net sig_out : Diff

  // Implicitly connects IN.P->sig_in.P, IN.N->sig_in.N
  inst dp (IN->sig_in, OUT->sig_out) : DiffPair
    param p = NMOS
```

Terminal path grammar:

```acir
terminalPath = ident ( "." ident | "[" int "]" )*
ident = [A-Za-z_][A-Za-z0-9_]*
int = [0-9]+
```

Guidance: External connectivity should prefer stable, named sub-terminals over numeric indices when a natural name exists (for example, `OUT.P` rather than `OUT[0]`). When a motif legitimately produces an ordered family, indices appear as `name[index]` and become part of the schema contract. Readers MUST treat `TAP[0]` as a single logical terminal path; bracket segments are not array lookups but syntactic components of the path.

Inline vs. Multiline Guidance: Use inline connections when they fit naturally on one line (typically 4 or fewer simple connections). Use multiline format when connections are numerous, complex, or need alignment for clarity. Both syntaxes may be mixed within the same instance.

#### Instance-Level Connect Statements

Connect statements may appear within an instance body to specify terminal-to-net bindings in a more expressive way than inline bindings. This is particularly useful when connecting bundle ports or when the direction of data flow should be explicit.

Syntax:

```acir
fill:
  inst <id> [(<connections>)] : <CircuitOrMotifType>
    param <key> = <value>
    size <name> = (key=value, ...)
    connect <source> -> <dest>
```

Example:

```acir
fill:
  inst dp (dp.GND->GND, dp.VDD->VDD) : DiffPair
    size InputPair = (W=2u, L=180n, M=1)
    connect dp.IN -> IN
    connect dp.OUT.P -> OUT
    connect VTAIL -> dp.TAIL
```

Semantics:

- At least one endpoint MUST reference the current instance (via `<instId>.` prefix)
- Both `connect dp.X -> Y` and `connect Y -> dp.X` are valid and equivalent
- Instance-level connect statements are normalized to fill-block level connections during parsing
- Connect statements are applied after inline bindings during resolution

Instance Prefix in Inline Connections:

Inline connections may optionally include the instance prefix for consistency with connect statements. Both forms are valid:

```acir
inst dp (GND->GND, VDD->VDD) : DiffPair        // Traditional form
inst dp (dp.GND->GND, dp.VDD->VDD) : DiffPair  // With instance prefix
```

When the instance prefix is present, it is stripped during parsing. The terminal is stored without the prefix in the instance's bindings.

#### Circuit-to-Circuit Instantiation (EL)

At EL level, instances may reference other circuits defined in the same ACIR document. This enables hierarchical composition where a top-level circuit instantiates child circuits, each of which may contain primitive devices or further circuit instances.

```acir
circuit OTA5TSingleEnded
  level EL

  supply VDD
  ground GND
  port IN : Diff
  port OUT : analog
  port VTAIL : bias

  fill:
    inst dp : DiffPair_hasTail_true_p_NMOS
      param W_input = 2u
      param L = 180n
      RAIL -> VDD
      BASE -> GND
      IN.P -> IN_P
      IN.N -> IN_N
      BIAS -> VTAIL

    inst cm : CurrentMirror_taps_1_p_PMOS
      param W_sense = 2u
      param L = 180n
      RAIL -> VDD

    attach cm to dp via CurrentMirrorLike::DiffPairLike as mirror_node

    connect dp.OUT.N -> OUT

circuit DiffPair_hasTail_true_p_NMOS : DiffPairLike
  level EL
  inline
  ...

circuit CurrentMirror_taps_1_p_PMOS : CurrentMirrorLike
  level EL
  inline
  ...
```

All referenced circuit types MUST be defined in the same document. Supplies and grounds MUST be explicitly bound at instantiation; there is no auto-wiring by name.

### 3.3.7 Circuit Parameter Declarations

Circuits may declare parameters that affect device sizing within the circuit. Parameter declarations appear after the level declaration and before port declarations.

Syntax:

```acir
circuit <name>
  level EL

  param <name> : <type> [= <default>]
  ...
```

Supported types are `real` and `int`. Parameters with defaults are optional at instantiation; parameters without defaults are required.

Example:

```acir
circuit DiffPair_hasTail_true_p_NMOS : DiffPairLike
  level EL
  inline

  param W_input : real = 2u
  param L : real = 180n
  param tail_ratio : real = 2

  supply RAIL
  port BASE : analog
  port IN : Diff
  port OUT : Diff
  port BIAS : bias

  size Input
  size Tail

  fill:
    net tnode : analog
    nmos M_N (G->IN.P, D->OUT.N, S->tnode, B->BASE) : size=Input nfet_01v8
    nmos M_P (G->IN.N, D->OUT.P, S->tnode, B->BASE) : size=Input nfet_01v8
    nmos M_TAIL (G->BIAS, D->tnode, S->BASE, B->BASE) : size=Tail nfet_01v8
```

Parameter references in device sizing use the `$` prefix for clarity: `W=$W_input`, `L=$L`, `W=$W_input*$tail_ratio`.

Rationale for sizing parameters in EL: Although EL represents sizing-complete circuits where device dimensions are finalized, some circuits have inherent structural ratios fundamental to their operation. For example, a current mirror may have a fixed 2:1 ratio between sense and tap transistors that is part of the circuit's topological identity, not a sizing decision. These ratio relationships are preserved as parameters even in EL because they represent architectural intent rather than optimization variables.

Topological vs. sizing parameters: Parameters that affect port shape, device topology, or PDK primitive selection (polarity, hasTail, taps) are monomorphized into the circuit name during HL→ML elaboration. Only sizing parameters (W, L, ratios) remain as runtime parameters. See [§3.3.12](#3312-topological-monomorphization) for monomorphization rules.

Definition vs instantiation semantics: Circuit parameter declarations define the interface; instance parameter assignments provide values. This follows class/object semantics:

- Parameters without defaults MUST be provided at instantiation
- Parameters with concrete defaults MAY be omitted at instantiation
- The `??` placeholder MUST appear only at instantiation sites, indicating the sizing engine will determine the value during ML→EL elaboration
- Circuit parameter defaults MUST be concrete values; `??` MUST NOT appear as a default in circuit definitions

At ML level, sizing parameters are typically declared without defaults (required) and assigned `??` at instantiation. At EL level, all sizing values MUST be concrete.

Example (ML):

```acir
circuit DiffPair_hasTail_true_p_NMOS : DiffPairLike
  level ML
  param W_input : real           // required - caller must provide
  param L : real                 // required - caller must provide
  param tail_ratio : real = 2    // optional - architectural default

// At instantiation:
inst dp : DiffPair_hasTail_true_p_NMOS
  param W_input = ??     // auto-size
  param L = ??           // auto-size
  // tail_ratio omitted → uses default 2
```

### 3.3.7.1 Size Declarations (First-class sizing packs)

Many ML and EL motifs repeatedly carry the same small set of sizing values (for example MOS `W`, `L`, and `M`) across multiple devices and across many instantiation sites. Encoding each quantity as an independent circuit parameter leads to verbose, brittle documents.

ACIR therefore supports a first-class **size** construct: a named, reusable key/value pack intended for sizing bundles. A `size` is not a typed scalar parameter; it is a structured map from keys to parameter expressions.

Syntax (circuit body):

```acir
size <name>
size <name> = (<key>=<expr>, <key>=<expr>, ...)
```

Syntax (instance body):

```acir
size <name> = (<key>=<expr>, <key>=<expr>, ...)
```

Semantics:

- A circuit-level `size <name>` declaration introduces a required size pack. If the declaration omits a default (`=` form), callers MUST provide the size at instantiation.
- A circuit-level `size <name> = (...)` declaration introduces an optional size pack with a concrete default. Callers MAY omit it at instantiation.
- An instance-level `size <name> = (...)` assignment provides a concrete pack for that specific instantiation.
- Keys are identifiers. Values are parameter expressions (the same expression grammar used in device parameter lists).
- For deterministic output, writers MUST serialize tuple keys in sorted order.

Using sizes in device declarations:

Device parameter lists MAY include a `size=<name>` entry. When present, the device’s parameter map is computed by:

1. Copy all key/value pairs from the referenced size pack into the device parameter map.
2. Apply explicit device parameters (e.g. `W=...`) as overrides (explicit keys win over size keys).
3. The `size` pseudo-parameter is not emitted to downstream formats; it is an ACIR-level convenience only.

Example (EL, inline leaf):

```acir
circuit DiffPair : DiffPairLike
  level EL
  inline

  size InputPair
  size Tail

  fill:
    nmos M_N (G->IN.P, D->OUT.N, S->tnode, B->BASE) : size=InputPair nfet_01v8
    nmos M_P (G->IN.N, D->OUT.P, S->tnode, B->BASE) : size=InputPair nfet_01v8
    nmos M_TAIL (G->BIAS, D->tnode, S->BASE, B->BASE) : size=Tail nfet_01v8
```

This construct uses `=` for value assignment; in ACIR, `:` introduces a declaration’s type or domain (for example `port OUT : analog` or `param L : real`).

### 3.3.8 The `inline` Annotation

Circuits may be marked with the `inline` annotation to control SPICE emission behavior.

Syntax:

```acir
circuit <name>
  level EL
  inline
  ...
```

Semantics:

- Without `inline`: The circuit becomes a `.subckt` in SPICE, and instances become `X` element calls.
- With `inline`: During SPICE emission, the circuit's devices and internal nets are merged into the parent circuit with hierarchical naming. No `.subckt` is generated.

Inline expansion uniquification:

When inlining, device IDs and internal net IDs are uniquified to avoid collisions:

| Child element | Parent name | SPICE-safe emission |
|---------------|-------------|---------------------|
| device `M_N` under instance `dp` | `dp.M_N` | `M_dp__M_N` |
| net `tnode` under instance `dp` | `dp.tnode` | `dp__tnode` |

Child ports are replaced by their bound nets from the parent. Internal nets are uniquified; external port bindings are substituted.

Top-level handling: If the top-level circuit is marked `inline`, the annotation is ignored (not an error). The top-level circuit always emits as a `.subckt` because there is no parent to inline into.

### 3.3.9 Device Declarations (EL)

At EL (Electrical Level), primitive devices replace motif instances. Device declarations specify the device type, sizing parameters, and terminal connections. Device declarations appear within the `fill:` block.

Transistors:

```acir
fill:
  nmos <id> [(<connections>)] : <parameters>
    <terminal> -> <net>
    ...

  pmos <id> [(<connections>)] : <parameters>
    <terminal> -> <net>
    ...
```

Transistor sizing uses size packs (§3.3.7.1), specified either inline or by reference. The PDK device name is required at EL.

Inline anonymous size (one-off sizing):

```acir
fill:
  nmos M_in (G->IN, D->OUT, S->GND, B->GND) : size=(W=12u, L=180n, M=4) nfet_01v8
  pmos M_load (G->OUT, D->OUT, S->VDD, B->VDD) : size=(W=2u, L=180n, M=2) pfet_01v8
```

Named size reference (reuse or parametrization):

```acir
circuit DiffPair
  level EL
  size Input
  size Tail

  fill:
    nmos M_N (G->IN.P, D->OUT.N, S->tnode, B->GND) : size=Input nfet_01v8
    nmos M_P (G->IN.N, D->OUT.P, S->tnode, B->GND) : size=Input nfet_01v8
    nmos M_TAIL (G->BIAS, D->tnode, S->GND, B->GND) : size=Tail nfet_01v8
```

Transistors MUST use `size=Name` (named reference) or `size=(...)` (inline literal). Device-level `W=`, `L=`, `M=` parameters are not permitted.

Passives:

```acir
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

```acir
fill:
  capacitor Cc (P->comp_out, N->stage2_in) : C=1p

  resistor Rz (P->comp_out, N->stage2_in) : R=10k
```

Diodes:

```acir
diode <id> [(<connections>)] : <model>
  A -> <net>
  K -> <net>
```

### 3.3.10 Connection Statements

Explicit connection statements declare net-to-net or terminal-to-net connections that are not captured by instance bindings. Connection statements appear within the `fill:` block.

```acir
fill:
  connect <source> -> <dest>
```

Example:

```acir
fill:
  connect dp.OUT.N -> OUT
```

### 3.3.11 Attach Statements in ACIR-EL

At EL level, ACIR supports explicit `attach` statements that provide higher-level composition while remaining SPICE-ready. Attach statements express bulk connectivity between circuit instances using trait-scoped connectors defined in the document's trait declarations.

#### 3.3.11.1 Attach Syntax

The `via` clause is **required** in ACIR-EL, ensuring deterministic resolution independent of trait inheritance changes.

Basic syntax:

```acir
attach <inst1> to <inst2> via <TraitName>::<TargetTrait>
```

**With net anchor** (deterministic naming for created nets):

```acir
attach cm to dp via CurrentMirrorLike::DiffPairLike as mirror_node
```

When the connector creates nets, they are named using the anchor: `mirror_node` for a single net, or `mirror_node_0`, `mirror_node_1`, etc. for multiple nets (in connector mapping order).

**Inline override** (modify or extend connector mappings):

```acir
attach cm to dp via CurrentMirrorLike::DiffPairLike {
  SENSE -> OUT.N   // override: use OUT.N instead of OUT.P
}
```

**Combined form** (anchor + overrides):

```acir
attach cm to dp via CurrentMirrorLike::DiffPairLike as mirror_node {
  SENSE -> OUT.N
}
```

#### 3.3.11.2 Trait-Scoped Connectors

Connectors are defined on interface traits in the Cascode language (Chapter 2). For multi-module ACIR documents that use `attach`, all referenced traits and their connectors MUST be present in the same ACIR document so that attach resolution is deterministic and environment-independent.

Circuits implement traits by listing them after the circuit name:

```acir
circuit CurrentMirror_taps_1_p_PMOS : CurrentMirrorLike
  level EL
  ...
```

Attach resolution looks up the referenced connector in the document's trait definitions at validation/emission time.

Connector disambiguation: A circuit may implement multiple traits, and different traits may define connectors to the same target trait. The `via` clause explicitly selects which connector to use, eliminating ambiguity. In ACIR-EL, the `via` clause is required precisely to ensure deterministic connector selection.

If `via` were optional (as it might be in higher-level ADL), and multiple valid connectors existed for a given attach, the result would be ambiguous. ACIR-EL's required `via` clause prevents this situation entirely.

Example:

```acir
trait TraitA:
  port X : analog
  connectors:
    to TraitC:
      X -> Z

trait TraitB:
  port Y : analog
  connectors:
    to TraitC:
      Y -> Z

// Circuit implements both traits
circuit Multi : TraitA, TraitB
  level EL
  ...

// Attach must specify which connector
attach m to c via TraitA::TraitC    // uses TraitA's connector (X -> Z)
attach m to c via TraitB::TraitC    // uses TraitB's connector (Y -> Z)
// Without 'via', ambiguous which connector applies
```

Attach statements are resolved during SPICE emission using a union-find algorithm that computes equivalence classes over nets and terminal endpoints. See [§3.13.3](#3133-connectivity-resolution) for the detailed resolution algorithm and [§3.13.4](#3134-net-unification-semantics) for net unification semantics.

#### 3.3.11.3 Trait Definitions (In-Document)

Traits declare interface contracts and connectors. Trait definitions appear at the document level, after bundle definitions and before circuits.

Syntax:

```acir
trait <TraitName>:
  port <name> : <domain|BundleType>
  ...

  connectors:
    to <TargetTrait>:
      <source_port> -> <target_port>
      ...
```

Trait port declarations may use the family wildcard form `NAME[*]` to indicate an indexed port family (for example, `TAP[*]`). This notation is descriptive and does not create ports on circuits by itself; circuits must still declare their concrete ports (for example, `TAP[0]`, `TAP[1]`) and monomorphize any port-count parameters (see [§3.3.12](#3312-topological-monomorphization)).

Connectors define how instances of one trait connect to instances of another. The connector `to DiffPairLike` on `CurrentMirrorLike` is referenced as `CurrentMirrorLike::DiffPairLike` in attach statements.

#### 3.3.11.4 Error Conditions

Each error condition corresponds to a diagnostic code defined in [§3.10](#310-diagnostics).

- **Named net merge (ACIR0020):** `error: attach would merge distinct named nets 'net_a' and 'net_b'; use explicit 'connect' to unify`
- **Connector not found (ACIR0021):** `error: no connector CurrentMirrorLike::ResistorLoad in document`
- **Missing via (ACIR0022):** `error: attach requires 'via' clause in ACIR-EL`
- **Conflicting binding (ACIR0023):** `error: cannot unify cm.SENSE (bound to net_a) with dp.OUT.P (bound to net_b)`
- **Domain mismatch (ACIR0024):** `error: domain mismatch: cm.SENSE (analog) vs dp.BIAS (bias)`
- **Rail auto-creation (ACIR0025):** `error: cannot auto-create supply/ground net; bind rails explicitly`

### 3.3.12 Topological Monomorphization

ACIR requires fixed circuit signatures; a circuit's ports cannot vary conditionally. Parameters that affect port shape or device topology require **monomorphization** during the HL→ML transition.

When monomorphization occurs: Topological parameters are resolved during HL→ML elaboration ("topology selection"). By the time a circuit reaches ML level, all topology-affecting parameters are baked into the circuit name. The subsequent ML→EL transition ("circuit sizing") resolves only sizing parameters to numeric values.

Port-Shape Specialization Rule: If a parameter can change the set of ports (adding, removing, or changing port count), then each realized port set MUST become a distinct circuit definition at ML.

Two categories of parameters:

1. **Topological parameters** (resolved at HL→ML, baked into circuit name):
   - Boolean flags (e.g., `hasTail`): Adds/removes ports → `DiffPair_hasTail_true`, `DiffPair_hasTail_false`
   - Port-family counts (e.g., `taps`): Changes port count → `CurrentMirror_taps_1`, `CurrentMirror_taps_2`
   - Polarity (`p`): Determines PDK primitive → `_p_NMOS`, `_p_PMOS`

2. **Sizing parameters** (resolved at ML→EL, remain as runtime parameters at ML):
   - Device dimensions (W, L, mult) — use `??` placeholder at ML
   - Architectural ratios (tail_ratio, mirror_ratio)
   - Expression support: `W=$W_input*$tail_ratio`

Naming convention:

```text
<BaseName>_<param1>_<value1>[_<param2>_<value2>...]
```

Topological parameters appear alphabetically: `DiffPair_hasTail_true_p_NMOS`

Instance references MUST use the specialized name:

```acir
// Correct
inst dp : DiffPair_hasTail_true_p_NMOS
  param W_input = 4u

// Error - DiffPair does not exist, only specialized variants
inst dp : DiffPair
  param hasTail = true   // INVALID
```

### 3.3.13 The `fill:` Block

The `fill:` block groups all synthesized and elaborated content, separating the circuit's **declared interface** (ports, supplies, grounds) from its **implementation** (instances, devices, internal nets).

Syntax:

```acir
fill:
  <net declarations>
  <instance declarations>
  <device declarations>
  <attach statements>
  <connection statements>
```

Semantics:

- At **ML level**, the `fill:` block contains internal `net` declarations and `inst` declarations resulting from slot resolution and elaboration.  
- **EL level** uses the `fill:` block for internal `net` declarations and primitive device declarations (`nmos`, `pmos`, `resistor`, `capacitor`, `inductor`, `diode`).  
- **HL level** does not use the `fill:` block; instead, slots remain at the circuit body level, representing requirements and contracts rather than synthesized implementations.

Net placement:

- Nets created as part of port expansion (e.g., `IN_P`, `IN_N` from `port IN : Diff`) are implicit and do not appear in the `fill:` block.
- Internal nets created during elaboration (e.g., `tnode`, `mirror_gate`) are declared within the `fill:` block.

Example:

```acir
circuit SimpleAmp
  level EL

  supply VDD
  ground VSS
  port IN : analog
  port OUT : analog

  fill:
    net tnode : analog
    nmos M_in (G->IN, D->OUT, S->VSS, B->VSS) : size=(W=8u, L=180n, M=2) nfet_01v8
    pmos M_load (G->OUT, D->OUT, S->VDD, B->VDD) : size=(W=2u, L=180n, M=2) pfet_01v8
```

The `fill:` block creates a clear structural separation between what the circuit promises (its interface) and how it is implemented (the synthesized content).

---

## 3.4 Derived Indices (Optional)

Tools routinely need fast graph queries. ACIR allows serializing derived views in an optional indices block. They are informative only and must match resolved connectivity exactly.

```acir
indices:
  hash sha256:abc123...
  pin_to_net dp.IN.P -> VINP
  pin_to_net dp.OUT.N -> N1
  net_to_pins VINP <- dp.IN.P
  net_to_pins N1 <- dp.OUT.N, cm.SENSE
  adjacent dp -> cm, tail
```

The hash is computed from a canonical serialization of the resolved terminal-to-net mapping. When `attach` and `connect` are present, tools must resolve them first, then hash the resulting concrete mapping. Readers must recompute and compare when indices are present. Writers should not serialize indices by default, reserving them for debugging scenarios or heavy-duty solvers that benefit from a warm cache.

---

## 3.5 Constraints and Measurement Intents

Constraints live alongside the graph and come in four main kinds. They are evaluated during synthesis, sizing, and verification.

```acir
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

```acir
<id> : <metric> @ <node> <op> <value> <unit>
```

Operators: `>=`, `<=`, `==`, `>`, `<`

### 3.5.2 Technology Constraints

Technology constraints express limits on device parameters.

```acir
<id> : <param> <op> <value> <unit> on <scope>
```

Scope may be `*` (all devices), a type selector, or an instance id.

### 3.5.3 Graph Constraints

Graph constraints express structural properties of the circuit graph.

```acir
<id> : cardinality <selector> in [<min>, <max>]
<id> : path_exists <from> -> <to> [through <type>]
<id> : fanout <net> in [<min>, <max>]
```

### 3.5.4 Measurement Intents

Measurement intents specify what metrics should be extracted from simulation.

```acir
<id> : <bench> <metric> @ <node>
```

Guidance: Graph constraints operate on the derived incidence graph, leveraging the fact that explicit edges eliminate the need for wiring inference. Numeric constraints and measurement intents carry explicit units, with sizing tools responsible for conversion to internal SI base units.

---

## 3.6 Harness: Environment for Benches

The harness holds bench-only elements derived from ADL env blocks: supply values, bias voltages, source impedances, loads, and PVT selections. Harness elements are not part of the design graph and should not affect layout or LVS.

```acir
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

Syntax:

```acir
sweep <ConditionName> [<start>:<step>:<stop>]     // explicit step
sweep <ConditionName> [<start>:<stop>]            // automatic step
sweep <ConditionName> [Auto]                      // synthesis chooses range (HL/ML only)
```

Examples:

```acir
sweep InputDCBias [0.3V:100mV:1.5V]           // SEAmp: sweep input bias with explicit step
sweep InputDCCommonMode [0.3V:1.5V]           // SEOpAmp: sweep ICMR with auto step
sweep OutputDCCommonMode [0.5V:50mV:1.3V]     // FDOpAmp: sweep OCMR with explicit step
```

Automatic step sizing: When the step parameter is omitted, the toolchain computes `step = (stop - start) / 20` clamped to the range [10mV, 100mV].

Semantics:

- The condition name (`InputDCBias`, `InputDCCommonMode`, `OutputDCCommonMode`) is topology-specific and must match the swept condition declared in the design's specification
- All benches listed in the `benches:` block must respect the sweep and execute analyses at each point
- Benches report worst-case values according to constraint directionality (minimum for `>=` constraints, maximum for `<=` constraints)
- For range constraints `in [X..Y]`, benches report both `_min` and `_max` metric values

Resolution level (normative):

- At EL, sweep ranges must be fully concrete (numeric start/stop/step). `sweep <ConditionName> [Auto]` must not appear in ACIR-EL.
- At HL (and optionally ML), `sweep <ConditionName> [Auto]` is permitted only as an explicit request for synthesis to choose an execution envelope. During lowering to EL, synthesis must resolve `[Auto]` to a concrete range and record that range in the EL harness for reproducibility.

Example (underconstrained but explicit):

```acir
// HL or ML: author requests that synthesis choose a sweep envelope
harness:
  sweep InputDCBias [Auto]
```

```acir
// EL: synthesis materializes the chosen envelope (example values)
harness:
  sweep InputDCBias [0.42V:50mV:1.07V]
```

### 3.6.1 Bias Resolution

Ports declared with domain `bias` represent DC operating points that must be resolved to specific voltage values before simulation. During the ML→EL transition, the sizing and biasing engine determines appropriate bias voltages based on the circuit topology and performance requirements. These resolved values appear in the harness block as `bias NET = VALUE` entries.

For example, a common-source amplifier with a PMOS active load requires a gate bias voltage to set the load device's operating point. The biasing engine selects a voltage that places the output near mid-rail while maintaining adequate headroom for signal swing. This value is recorded in the harness and emitted as an ideal DC voltage source during SPICE testbench generation.

### 3.6.2 Bench Configuration

ACIR lists selected benches and their configurations for reproducibility.

```acir
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

```acir
circuit OTA : SingleEndedOpAmp
  level HL
  ...
  slot load (node->vout, bias->vb1, vref->VDD) : LoadDevice
  slot amp (IN->IN, OUT->OUT, VDD->VDD, VSS->VSS) : [SingleEndedOpAmp, LowPower]
```

Syntax:

```acir
slot <id> [(<connections>)] : <Trait>
slot <id> [(<connections>)] : [<Trait1>, <Trait2>, ...]
```

The slot declaration captures the interface contract (connections) and the behavioral requirements (traits) that any concrete implementation must satisfy. During synthesis, slots are resolved to concrete motif types that implement the required traits.

### 3.7.2 ML - Mid Level

Slots are resolved to concrete motif types and become regular `inst` declarations. Topological parameters (polarity, hasTail, taps) are resolved during HL→ML and baked into monomorphized circuit names. All terminals are connected to nets. Sizing parameters may still be symbolic, and the representation remains PDK-agnostic. Instances and internal nets appear within the `fill:` block.

```acir
circuit OTA : SingleEndedOpAmp
  level ML
  ...
  fill:
    inst load (node->vout, bias->vb1, vref->VDD) : ActiveLoad_p_PMOS
      param W = ??
      param L = ??

    inst dp : DiffPair_hasTail_true_p_NMOS
      param W_input = ??
      param L = ??
      param tail_ratio = 2
      ...
```

At ML, what was a `slot load : LoadDevice` at HL becomes `inst load : ActiveLoad_p_PMOS` once the synthesis engine selects a concrete motif and topology that satisfies the `LoadDevice` trait.

Auto-sizing placeholder (`??`): At ML level, sizing parameters at **instantiation** may use the `??` placeholder to indicate values the sizing engine will determine during ML→EL elaboration. The `??` token appears only at instantiation sites, not in circuit parameter defaults. Circuit definitions declare parameter types and may provide concrete architectural defaults (e.g., `tail_ratio = 2`), but sizing parameters that need auto-determination are left without defaults and assigned `??` at instantiation. See [§3.3.7](#337-circuit-parameter-declarations) for the full parameter semantics.

Symbolic parameters use the `$` prefix for named references: `$ratio`, `$W_input`. The `??` token is reserved and cannot be used as an identifier.

### 3.7.3 EL - Electrical Level

Parameters are numeric wherever required by this specification. All terminals are connected, PDK-specific device choices have been recorded, and the document is ready for SPICE emission.

EL supports two forms:

1. **Fully flattened:** Primitive devices only, with hierarchical naming preserved for traceability.
2. **Hierarchical:** Circuit instances referencing other circuits in the same document, with primitive devices at leaves.

Flattened form:

```acir
circuit OTA
  level EL
  ...
  fill:
    nmos dp.M_N (G->IN_P, D->mirror_gate, S->tnode, B->GND) : W=2u L=180n M=1 nfet_01v8
```

Hierarchical form:

```acir
circuit OTA5TSingleEnded
  level EL
  ...
  fill:
    inst dp : DiffPair_hasTail_true_p_NMOS
      param W_input = 2u
      ...
    inst cm : CurrentMirror_taps_1_p_PMOS
      ...
    attach cm to dp via CurrentMirrorLike::DiffPairLike as mirror_node

circuit DiffPair_hasTail_true_p_NMOS : DiffPairLike
  level EL
  inline
  ...
```

Hierarchical EL documents contain multiple circuits; the top-level circuit appears first. Child circuits marked `inline` are expanded during SPICE emission. See [§3.3.6](#336-instance-declarations-ml-and-el) for circuit instantiation and [§3.3.8](#338-the-inline-annotation) for the `inline` annotation.

---

## 3.8 Provenance and Diagnostics

Provenance links IR elements back to ADL source and records transformation steps. This enables precise diagnostics and reproducibility.

```acir
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

- **Terminal coverage:** every required terminal path for every instance appears exactly once at ML and EL, either via explicit binding or attach resolution.
- **Bundle completeness:** any referenced bundle field resolves to a concrete net id at ML and EL.
- Domain compatibility: terminal kind and net domain are compatible according to the library schema. Attach resolution requires exact domain matching across unified endpoints.
- **Device selection at EL:** primitive device declarations MUST include the PDK device name.
- **Rail uniqueness:** each named rail such as VDD or GND maps to one net id across the circuit.
- **No dangling nets:** any net with zero incident terminals is pruned unless referenced by harness.
- **Indices consistency:** when indices are present, the hash matches a recomputed hash from terminal bindings.
- **Allowed loops:** cycles are allowed unless explicitly forbidden by rule or library schema. Algebraic loops of ideal passives without controlled sources may be rejected.
- **Circuit reference resolution:** at EL, all circuit types referenced by instance declarations MUST be defined in the same document. No external circuit references are permitted.
- **Parameter validation:** required parameters (those without defaults) MUST be provided at instantiation. Parameter types must match declarations.
- **Attach resolution:** attach statements require the `via` clause. The referenced connector MUST exist in the trait definition. Attach must not create conflicting bindings (both sides already bound to different nets).
- **No circular instantiation:** circuits must not directly or indirectly instantiate themselves.

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
| ACIR0008 | Error | Invalid level declaration; expects `HL`, `ML`, or `EL` |
| ACIR0009 | Error | JSON parse error when reading ACIR from JSON format |
| ACIR0010 | Error | Parallel load specification missing parentheses; expects `(C=... \|\| R=...)` |
| ACIR0011 | Error | Parallel load specification missing `\|\|` operator between elements |
| ACIR0012 | Error | Parallel load specification missing first element (before `\|\|`) |
| ACIR0013 | Error | Parallel load specification missing second element (after `\|\|`) |
| ACIR0014 | Error | Parallel load element missing value; expects `C=<value>` or `R=<value>` |
| ACIR0015 | Error | Invalid instance declaration syntax |
| ACIR0016 | Error | Invalid attach statement syntax; includes malformed attach, unterminated override block, or invalid override syntax |

### Semantic Validation Codes

The following codes apply during semantic analysis, particularly attach resolution and net unification.

| Code | Severity | Description |
|------|----------|-------------|
| ACIR0020 | Error | Named net merge via attach; two distinct named nets would be unified without explicit `connect` |
| ACIR0021 | Error | Connector not found in document |
| ACIR0022 | Error | Missing `via` clause in attach statement (required in ACIR-EL) |
| ACIR0023 | Error | Conflicting binding; cannot unify nets already bound to different named nets |
| ACIR0024 | Error | Domain mismatch between terminals being connected |
| ACIR0025 | Error | Cannot auto-create supply/ground net; bind rails explicitly |
| ACIR0026 | Warning | Source trait not found in trait registry; using default domain for port domain resolution |
| ACIR0027 | Warning | Target trait not found in trait registry; using default domain for port domain resolution |
| ACIR0028 | Error | Instance connect statement must reference the instance on at least one side |
| ACIR0029 | Error | Invalid connect statement syntax in instance body |

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
When ACIR-EL contains `attach` or `connect` statements, tools resolve these constraints deterministically into a concrete terminal-to-net mapping before performing graph queries, validation, indexing, or SPICE emission.

---

## 3.12 Complete Examples

### 3.12.1 ML ACIR for OTA5TSingleEnded

This example shows the ML representation of a five-transistor OTA with differential input and single-ended output. At ML, topological parameters (polarity, hasTail, taps) are already resolved into monomorphized circuit names. Sizing parameters use the `??` placeholder.

```acir
ACIR 2.0

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
    net mirror_gate : analog
    net tnode : analog

    inst dp (IN->IN, OUT.N->OUT, BASE->GND, BIAS->VTAIL, OUT.P->mirror_gate) : DiffPair_hasTail_true_p_NMOS
      param W_input = ??
      param L = ??
      param tail_ratio = 2

    inst cm (RAIL->VDD, SENSE->mirror_gate, TAP[0]->OUT) : CurrentMirror_taps_1_p_PMOS
      param W_sense = ??
      param L = ??


circuit DiffPair_hasTail_true_p_NMOS : DiffPairLike
  level ML
  package lib.std.prim

  param W_input : real
  param L : real
  param tail_ratio : real = 2

  port IN : Diff
  port OUT : Diff
  port BASE : analog
  port BIAS : bias

  fill:
    net tnode : analog

    inst M_N (G->IN.P, D->OUT.N, S->tnode) : MOS_NMOS
      param W = $W_input
      param L = $L

    inst M_P (G->IN.N, D->OUT.P, S->tnode) : MOS_NMOS
      param W = $W_input
      param L = $L

    inst M_TAIL (G->BIAS, D->tnode, S->BASE) : MOS_NMOS
      param W = $W_input * $tail_ratio
      param L = $L


circuit CurrentMirror_taps_1_p_PMOS : CurrentMirrorLike
  level ML
  package lib.std.prim

  param W_sense : real
  param L : real

  port RAIL : supply
  port SENSE : analog
  port TAP[0] : analog

  fill:
    inst M_SENSE (G->SENSE, D->SENSE, S->RAIL) : MOS_PMOS
      param W = $W_sense
      param L = $L

    inst M_TAP0 (G->SENSE, D->TAP[0], S->RAIL) : MOS_PMOS
      param W = $W_sense
      param L = $L
```

### 3.12.2 EL ACIR for OTA5TSingleEnded (Fully Flattened)

At EL, all motifs are expanded to primitive devices. The circuit is fully flattened with hierarchical naming preserved for traceability.

```acir
ACIR 2.0

circuit OTA5TSingleEnded
  level EL

  supply VDD
  ground GND

  port IN_P : analog
  port IN_N : analog
  port OUT : analog
  port VTAIL : bias

  fill:
    net tnode : analog        // from dp.tnode
    net mirror_gate : analog  // dp.OUT.P = cm.SENSE

    // DiffPair (dp) - NMOS differential pair with tail
    nmos dp.M_N (G->IN_P, D->mirror_gate, S->tnode, B->GND) : size=(W=2u, L=180n, M=1) nfet_01v8
    nmos dp.M_P (G->IN_N, D->OUT, S->tnode, B->GND) : size=(W=2u, L=180n, M=1) nfet_01v8
    nmos dp.M_TAIL (G->VTAIL, D->tnode, S->GND, B->GND) : size=(W=4u, L=180n, M=1) nfet_01v8

    // CurrentMirror (cm) - PMOS current mirror
    pmos cm.M_SENSE (G->mirror_gate, D->mirror_gate, S->VDD, B->VDD) : size=(W=2u, L=180n, M=1) pfet_01v8
    pmos cm.M_TAP0 (G->mirror_gate, D->OUT, S->VDD, B->VDD) : size=(W=2u, L=180n, M=1) pfet_01v8

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

```acir
ACIR 2.0

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

```acir
ACIR 2.0

circuit CSAmplifier
  level EL

  supply VDD
  ground GND

  port vin : analog
  port vout : analog
  port vb1 : bias

  fill:
    nmos M_in (G->vin, D->vout, S->GND, B->GND) : size=(W=12u, L=180n, M=4) nfet_01v8

    pmos load.M1 (G->vb1, D->vout, S->VDD, B->VDD) : size=(W=4u, L=180n, M=2) pfet_01v8

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

### 3.12.5 Hierarchical EL ACIR for OTA5TSingleEnded

This example demonstrates hierarchical EL with circuit instantiation and attach statements resolved via trait-scoped connectors.

```acir
ACIR 2.0

bundle Diff:
  P : analog
  N : analog

trait DiffPairLike:
  port IN : Diff
  port OUT : Diff

trait CurrentMirrorLike:
  port SENSE : analog
  port TAP[*] : analog

  connectors:
    to DiffPairLike:
      SENSE -> OUT.N
      TAP[0] -> OUT.P

// Top-level circuit appears first
circuit OTA5TSingleEnded
  level EL

  supply VDD
  ground GND
  port IN : Diff
  port OUT : analog
  port VTAIL : bias

  fill:
    inst dp : DiffPair_hasTail_true_p_NMOS
      param W_input = 2u
      param L = 180n
      param tail_ratio = 2
      RAIL -> VDD
      BASE -> GND
      IN.P -> IN_P
      IN.N -> IN_N
      BIAS -> VTAIL

    inst cm : CurrentMirror_taps_1_p_PMOS
      param W_sense = 2u
      param L = 180n
      RAIL -> VDD

    attach cm to dp via CurrentMirrorLike::DiffPairLike as mirror_node

    connect dp.OUT.N -> OUT

  harness:
    supply VDD = 1.8V
    load OUT C=1pF

  benches:
    SEOpAmpACBench

// Child circuits follow
circuit DiffPair_hasTail_true_p_NMOS : DiffPairLike
  level EL
  inline

  param W_input : real = 2u
  param L : real = 180n
  param tail_ratio : real = 2

  supply RAIL
  port BASE : analog
  port IN : Diff
  port OUT : Diff
  port BIAS : bias

  size Input
  size Tail

  fill:
    net tnode : analog
    nmos M_N (G->IN.P, D->OUT.N, S->tnode, B->BASE) : size=Input nfet_01v8
    nmos M_P (G->IN.N, D->OUT.P, S->tnode, B->BASE) : size=Input nfet_01v8
    nmos M_TAIL (G->BIAS, D->tnode, S->BASE, B->BASE) : size=Tail nfet_01v8

circuit CurrentMirror_taps_1_p_PMOS : CurrentMirrorLike
  level EL
  inline

  param W_sense : real = 2u
  param L : real = 180n

  supply RAIL
  port SENSE : analog
  port TAP[0] : analog

  size Sense

  fill:
    pmos M_SENSE (G->SENSE, D->SENSE, S->RAIL, B->RAIL) : size=Sense pfet_01v8
    pmos M_TAP0 (G->SENSE, D->TAP[0], S->RAIL, B->RAIL) : size=Sense pfet_01v8
```

The attach statement `attach cm to dp via CurrentMirrorLike::DiffPairLike as mirror_node` resolves using the referenced connector from the document’s trait definitions. The `as mirror_node` clause names the created nets `mirror_node_0` and `mirror_node_1`. Since both child circuits are marked `inline`, SPICE emission expands them into the top-level circuit with uniquified names.

---

## 3.13 SPICE Emission

SPICE emission transforms EL-level ACIR into simulator-ready netlists. The process handles both flattened and hierarchical documents, resolving attach statements and expanding inline circuits as needed.

### 3.13.1 Flattened Emission

For fully flattened EL documents containing only primitive devices, emission is a direct traversal. Each device declaration maps to a SPICE element: M for transistors, R for resistors, C for capacitors, and so forth. Terminal bindings determine node names in SPICE-correct order, and parameters emit directly. Because terminal bindings hold all edges, node substitution is O(1) per terminal.

```spice
* OTA5TSingleEnded - Generated from ACIR EL

.subckt OTA5TSingleEnded VDD GND IN_P IN_N OUT VTAIL

M_dp__M_N mirror_gate IN_P tnode GND nfet_01v8 W=2u L=180n m=1
M_dp__M_P OUT IN_N tnode GND nfet_01v8 W=2u L=180n m=1
M_dp__M_TAIL tnode VTAIL GND GND nfet_01v8 W=4u L=180n m=1
M_cm__M_SENSE mirror_gate mirror_gate VDD VDD pfet_01v8 W=2u L=180n m=1
M_cm__M_TAP0 OUT mirror_gate VDD VDD pfet_01v8 W=2u L=180n m=1

.ends OTA5TSingleEnded
```

Primitive transistor devices emit as SPICE M-devices. The PDK device name becomes the SPICE model name. Terminal order follows SPICE convention: drain, gate, source, bulk for MOSFETs. Hierarchical names from ACIR (e.g., `dp.M_N`) are sanitized for broad simulator compatibility (for example, `M_dp__M_N`).

### 3.13.2 Hierarchical Emission

For hierarchical EL documents containing circuit instances, the emitter first resolves all attach statements using the union-find algorithm described in [§3.13.3](#3133-connectivity-resolution), then processes circuits according to their `inline` annotation.

Circuits not marked `inline` become separate `.subckt` definitions. The emitter orders these by dependency: leaf circuits (those with no circuit instances) emit first, followed by circuits that instantiate only leaves, continuing up the dependency tree. The top-level circuit emits last. This ordering ensures each `.subckt` is defined before any `X` element references it.

Circuits marked `inline` do not generate `.subckt` definitions. Instead, their devices and internal nets merge into the parent circuit during emission. Device IDs and internal net IDs are uniquified using the instance path: device `M_N` under instance `dp` becomes `M_dp__M_N`, and net `tnode` becomes `dp__tnode`. Child port bindings are substituted with the parent's bound nets.

### 3.13.3 Connectivity Resolution

ACIR-EL connectivity is computed by solving a constraint system over **net atoms** using union-find. This ensures deterministic resolution regardless of statement order.

**Net atoms** include:
- Declared nets (explicit `net` declarations in fill block)
- Port-expansion nets (from bundle expansion, e.g., `IN_P`, `IN_N`)
- Supply/ground nets (from circuit header)
- Unbound terminal endpoints

**Constraints** come from:
1. Explicit terminal bindings (`port -> net`)
2. Connect statements (`connect a -> b`)
3. Attach statements (bulk binding via connector mappings)

Resolution algorithm:
1. Initialize union-find with all net atoms
2. For each explicit binding, union the terminal with its bound net
3. For each connect statement, union the two endpoints
4. For each attach statement:
   - Look up connector from trait
   - For each mapping in connector, union source endpoint with target endpoint
5. Compute equivalence classes
6. Assign representative net to each class

### 3.13.4 Net Unification Semantics

Each connector mapping is treated as **net unification**, not "skip if pre-bound". For each mapping `A -> B`:

1. **One side bound, other unbound:** Bind the unbound side to the same net
2. **Both sides bound to the same net:** No-op (already unified)
3. **Both sides bound to different nets:** Error
4. **Neither side bound:** Create a net using the `as` anchor (or auto-name if no anchor)

Representative net selection follows priority order:
1. Supply/ground nets declared in circuit header
2. Port-expansion nets (from bundle expansion)
3. Explicitly declared nets (`net foo : analog` in fill block)
4. Auto-generated net (when class contains only unbound terminals)

Strict net conflict rule: Attach resolution must not implicitly merge two distinct named nets. If an attach statement would place two explicitly named nets (declared nets, port-expansion nets, or supply/ground nets) into the same equivalence class, this is an error. The error message identifies both nets and suggests using explicit `connect` for intentional unification.

Explicit `connect` statements allow intentional unification of named nets. When multiple named nets are unified via `connect`, representative selection follows the priority order above, with ties within a tier broken by choosing the lexicographically smallest net id.

Unifying distinct supply nets or distinct ground nets remains an error even with explicit `connect`.

Example:

```acir
// Error: attach would merge net_a and net_b (both explicitly named)
inst a : CircuitA
  PORT_X -> net_a
inst b : CircuitB
  PORT_Y -> net_b
attach a to b via TraitA::TraitB  // error if connector maps PORT_X to PORT_Y

// Solution: use explicit connect
connect net_a -> net_b           // intentional unification
attach a to b via TraitA::TraitB // OK: both sides now in same equivalence class
```

Auto-net naming: If no `as` anchor is provided, auto-generate: `_auto_<term1>__<term2>` where terms are lexicographically-sorted terminal paths with `.` replaced by `_`.

Domain compatibility: All endpoints in an equivalence class must have identical domains (exact matching, no supertype inference). Auto-created nets cannot have supply or ground domain; rails must be bound explicitly.

### 3.13.5 Port Ordering

ACIR uses named binding; SPICE requires positional `.subckt` pin order. The canonical pin order follows declaration order in ACIR: supplies first (in declaration order), then grounds (in declaration order), then ports (in declaration order, with bundles expanded field-by-field).

```acir
circuit OTA5TSingleEnded
  supply VDD
  ground GND
  port IN : Diff      // expands to IN_P, IN_N
  port OUT : analog
  port VTAIL : bias

// Emits as:
.subckt OTA5TSingleEnded VDD GND IN_P IN_N OUT VTAIL
```

### 3.13.6 Parameter Substitution

When emitting devices from parameterized circuits, sizing parameter references are substituted with their bound values. The expression `W=$W_input*$tail_ratio` with `W_input=2u` and `tail_ratio=2` emits as `W=4u`. Parameter expressions support multiplication, division, addition, and subtraction.

### 3.13.7 Instance Naming

ACIR instance IDs map to SPICE element IDs with sanitization. The element type prefix is determined by the target: `M` for MOSFETs, `R` for resistors, `C` for capacitors, `X` for subckt instances. The hierarchical separator `.` in ACIR becomes `__` (double underscore) in SPICE. Any SPICE-illegal characters are replaced with `_`.

### 3.13.8 Subckt Naming for Parameterized Circuits

When emitting SPICE subckts for parameterized circuits, the subckt name encodes topological parameter values to ensure each unique parameterization produces a distinct subckt definition. The naming algorithm is normative to ensure reproducible output across implementations.

Parameter ordering: Parameters appear alphabetically by name. This guarantees deterministic naming regardless of declaration order in source.

Value rendering: Each parameter type renders to a canonical string form:

| Type | Rendering | Example |
|------|-----------|---------|
| bool | `true` or `false` | `_hasTail_true` |
| int | Decimal without leading zeros | `_taps_2` |
| real | Scientific notation, mantissa normalized to one digit before decimal | `_W_2e-6` |
| polarity | `NMOS` or `PMOS` | `_p_NMOS` |

Format: The subckt name follows the pattern:

```acir
<CircuitName>_<param1>_<value1>_<param2>_<value2>...
```

Length limit: SPICE subckt names must not exceed 64 characters. If the generated name exceeds this limit, use a hash fallback:

```acir
<CircuitName>_<sha256_prefix_8>
```

The hash is computed over the full canonical name (before truncation) and uses the first 8 hexadecimal characters of the SHA-256 digest.

Examples:

```acir
CurrentMirror_p_PMOS_taps_2        // alphabetical: p before taps
DiffPair_hasTail_true_p_NMOS      // alphabetical: hasTail before p
VeryLongCircuitNameWithManyParams_abc123de   // hash fallback (exceeds 64 chars)
```

The ACIR writer maintains a mapping from canonical name to hash-based name when fallback is used, ensuring consistent naming within a single emission session.

---

## 3.14 Canonical Writer Rules

To keep diffs and golden tests stable, the canonical writer follows these rules:

- Order circuits with top-level first, then child circuits in dependency order.
- Within a circuit, order sections: level, inline, param declarations, size declarations, package, supplies, grounds, ports, fill, constraints, harness, benches, provenance.
- Within the `fill:` block, order: nets, instances, devices, attach statements, connections. Sort each category by id lexicographically.
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

```acir
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
- Ensures indices, if present, match resolved connectivity according to the hash.

The testing strategy encompasses three complementary approaches:

- Golden ACIR snapshots under `tests/golden/acir/`.
- Connectivity unit tests rebuild incidence from the resolved terminal-to-net mapping and assert simple graph properties: path_exists, cardinality, and fanout.
- Round-trip tests from ADL to ACIR to SPICE for small examples.

---

## 3.17 Cascode -> ACIR -> SPICE Pipeline

The transformation from ADL to SPICE follows a systematic progression through ACIR. Parsing and desugaring map ADL constructs to instances and nets. At EL, ACIR may also include `attach` and `connect` statements; these are resolved deterministically into a concrete terminal-to-net mapping before graph queries, validation, indexing, and SPICE emission. This keeps downstream passes mechanical and environment-independent once resolution has completed.

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
document     = "ACIR" MAJOR "." MINOR NL (bundleDef)* (traitDef)* (circuit)+ ;
MAJOR        = [0-9]+ ;
MINOR        = [0-9]+ ;

bundleDef    = "bundle" IDENT ":" NL (INDENT field NL)+ ;
field        = IDENT ":" domain ;

traitDef     = "trait" IDENT ":" NL (INDENT traitMember NL)+ ;
traitMember  = traitPort | connectorsBlock ;
traitPort    = "port" traitPortName ":" (domain | IDENT) ;
traitPortName= IDENT | IDENT "[" "*" "]" ;
connectorsBlock = "connectors:" NL (INDENT INDENT connectorDef NL)+ ;
connectorDef = "to" IDENT ":" NL (INDENT INDENT INDENT connectorMapping NL)+ ;
connectorMapping = terminalPath "->" terminalPath ;

circuit      = "circuit" IDENT (":" traits)? source? NL circuitBody ;
traits       = IDENT ("," IDENT)* ;
circuitBody  = (INDENT statement NL)* ;

statement    = levelDecl | inlineDecl | packageDecl | circuitParamDecl
             | sizeDecl
             | supplyDecl | groundDecl | portDecl | slotDecl
             | fillBlock | constraintsBlock | harnessBlock
             | benchesBlock | provenanceBlock | extensionsBlock ;

levelDecl    = "level" ("HL" | "ML" | "EL") ;
inlineDecl   = "inline" ;
packageDecl  = "package" qualifiedName ;
circuitParamDecl = "param" IDENT ":" paramType ("=" paramValue)? ;
sizeDecl     = "size" IDENT ("=" sizeLiteral)? ;
paramType    = "real" | "int" ;
supplyDecl   = "supply" IDENT source? ;
groundDecl   = "ground" IDENT source? ;
portDecl     = "port" IDENT ":" (domain | IDENT) source? ;

slotDecl     = "slot" IDENT connectionList? ":" (IDENT | traitList) source? NL (INDENT slotBody NL)* ;
traitList    = "[" IDENT ("," IDENT)* "]" ;
slotBody     = paramAssign ;

fillBlock    = "fill:" NL (INDENT INDENT fillContent NL)* ;
fillContent  = netDecl | instDecl | deviceDecl | attachStmt | connectStmt ;

symbol       = IDENT ("." IDENT)* ;  (* hierarchical name for nets, device ids *)
netDecl      = "net" symbol ":" domain source? ;

instDecl     = "inst" IDENT connectionList? ":" IDENT traits? source? NL (INDENT instBody NL)* ;
instBody     = paramAssign | sizeAssign | binding | instConnectStmt ;
instConnectStmt = "connect" endpoint "->" endpoint ;
paramAssign  = "param" IDENT "=" paramValue ;
sizeAssign   = "size" IDENT "=" sizeLiteral ;
connectionList = "(" connection ("," connection)* ")" ;
binding      = terminalPath "->" symbol ;
connection   = terminalPath "->" symbol ;

sizeLiteral  = "(" sizeEntry ("," sizeEntry)* ")" ;
sizeEntry    = IDENT "=" paramExpr ;

deviceDecl   = deviceType symbol connectionList? ":" deviceParams pdkDevice source? NL (INDENT binding NL)* ;
deviceType   = "nmos" | "pmos" | "resistor" | "capacitor" | "inductor" | "diode" ;
deviceParams = (IDENT "=" paramExpr)+ ;
paramExpr    = paramValue (("*" | "/" | "+" | "-") paramValue)* ;
pdkDevice    = IDENT ;

attachStmt   = "attach" IDENT "to" IDENT ("to" IDENT)* "via" connectorRef ("as" IDENT)? attachOverrides? ;
connectorRef = IDENT "::" IDENT ;
attachOverrides = "{" NL (INDENT attachMapping NL)* "}" ;
attachMapping = terminalPath "->" terminalPath ;

connectStmt  = "connect" endpoint "->" endpoint source? ;

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
value        = NUMBER SIUNIT? ;
SIUNIT       = SIPREFIX? BASEUNIT ;
SIPREFIX     = "f" | "p" | "n" | "u" | "m" | "k" | "M" | "G" | "T" ;
BASEUNIT     = "V" | "A" | "F" | "Ohm" | "H" | "Hz" | "W" | "s" ;
paramValue   = value | "$" IDENT | "??" | IDENT ;
source       = "@[" STRING "]" ;

IDENT        = [A-Za-z_][A-Za-z0-9_]* ;
NUMBER       = [0-9]+ ("." [0-9]*)? ([eE][+-]?[0-9]+)? ;
UNIT         = [A-Za-z]+ ;
INT          = [0-9]+ ;
STRING       = [^\]]+ ;
NL           = "\n" ;
INDENT       = "  " ;
```

The `paramExpr` production supports only the four binary arithmetic operators (`*`, `/`, `+`, `-`). No unary operators are permitted; negation must be expressed as `0 - x`. The grammar is intentionally flat with no precedence hierarchy; evaluation proceeds left-to-right. Parameter expressions may include SI-suffixed values as operands (see [§3.2.4](#324-lexical-elements) for the prefix table).

This grammar is informative; the normative specification is the prose in this chapter.
