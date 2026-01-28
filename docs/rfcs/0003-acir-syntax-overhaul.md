# RFC: ACIR Syntax Overhaul

Status: Draft
Authors: Daniel Lovell
Created: 2026-01-27
Last Updated: 2026-01-27
Target Version: ACIR 3.0

---

## Abstract

This RFC proposes a comprehensive syntax overhaul for ACIR 3.0, introducing brace-delimited blocks, constructor-style instantiation, explicit primitive definitions, and several other changes that improve readability, reduce ambiguity, and align ACIR more closely with mainstream programming language conventions.

The changes fall into three categories: structural (brace delimiters, explicit closures), semantic (primitives, computed sizes), and notational (assignment operators, optional comma-separated bindings). Together, these ten changes address long-standing friction points in the current indentation-based syntax while preserving ACIR's core design as a line-oriented, diff-friendly intermediate representation.

This RFC is orthogonal to RFC 0000 (measurement abstraction) but supersedes the terminology in that RFC where they overlap: specifically, this RFC adopts `interface` for connector-carrying contracts, leaving `trait` for capability markers as defined in RFC 0000.

---

## 1. Problem Statement

The current ACIR syntax (version 2.x and early 3.0 drafts) uses a Python-inspired indentation-based structure with colon-terminated block headers. While this approach is visually clean, it creates several practical difficulties:

1. Ambiguous block boundaries. Without explicit closing delimiters, tools and humans must infer where a block ends based on indentation changes. This complicates copy-paste operations, automated refactoring, and diff interpretation.

2. Inconsistent declaration forms. Instance declarations (`inst dp : DiffPair`), device declarations (`nmos M_N (...) : nmos`), and constraint bindings (`c_gbw : ACBench::...`) all use the colon character for different purposes, creating parsing ambiguity and reader confusion.

3. Implicit device types. At EL level, device declarations reference implicit types (`nmos`, `pmos`) without explicit PDK binding. This conflates the device category with the concrete primitive, making PDK abstraction awkward.

4. Scattered parameter declarations. Circuit parameters and size packs are declared in the body rather than the signature, obscuring the circuit's interface contract at a glance.

---

## 2. Goals and Non-Goals

Goals:

1. Establish explicit block boundaries using braces, eliminating ambiguity about where constructs begin and end.
2. Unify the instantiation syntax for circuits, devices, and primitives around a constructor-style `= new Type(...)` form.
3. Introduce explicit primitive definitions that separate device category from PDK-specific model selection.
4. Move circuit parameters and size declarations into the signature for interface clarity.
5. Preserve ACIR's line-oriented, diff-friendly character.
6. Provide a clear migration path from the current syntax.

Non-goals:

1. Change ACIR's semantic model. These changes are syntactic; connectivity, elaboration levels, and bench semantics remain as specified in Chapter 3.
2. Introduce significant new features beyond syntax. Computed size expressions are the only semantic addition.
3. Maintain backward compatibility. This is a breaking change; migration tooling will be provided.

---

## 3. Proposal

### 3.1 Block Delimiters: Braces Instead of Colons

All block-introducing constructs use opening braces (`{`) instead of trailing colons, with explicit closing braces (`}`).

#### 3.1.1 Syntax

Affected constructs:
- `bundle Name { fields }`
- `interface Name { members }`
- `bench Name for Trait { body }`
- `outputs { items }`
- `connectors { items }`
- `to TargetTrait { mappings }`
- `fill { body }`
- `constraints { body }`
- `numeric { items }`
- `tech { items }`
- `graph { items }`
- `harness { body }`
- `primitive kind Name(params) { body }`

Circuit bodies use braces when parameters are declared in the signature (see §3.4); otherwise the existing indentation-based form is permitted for backward-compatible transition.

#### 3.1.2 Examples

```acir
bundle Diff {
  // Bundle fields do NOT require directionality
  P : analog
  N : analog
}

interface CurrentMirrorLike {
  input SENSE : analog
  output TAP[0] : analog
  connectors {
    to DiffPairLike {
      SENSE--OUT.N
      TAP[0]--OUT.P
    }
  }
}

bench ACBench for SingleEndedOpAmp {
  builtin SEOpAmpACBench
  outputs {
    GainBandwidth
    PassbandGain
    PhaseMargin
  }
}
```

#### 3.1.3 Rationale

Explicit braces eliminate the "where does this block end?" question that arises with indentation-only syntax. They also enable single-line compact forms when appropriate and simplify automated code generation.

---

### 3.2 Keyword Rename: `trait` → `interface`

The `trait` keyword is renamed to `interface` for connector-carrying contracts.

#### 3.2.1 Syntax

```ebnf
interfaceDecl = "interface" IDENT "{" interfaceBody "}" ;
```

#### 3.2.2 Semantics

An `interface` defines a contract that circuits can implement. Interfaces may declare ports, parameters, and connector blocks. The `implements` clause on circuits references interface names.

The term `trait` is reserved for capability markers as defined in RFC 0000 (e.g., `HasSupplyPort`, `BalancedInput`). This separation clarifies the distinction between connector contracts (interface) and capability flags (trait).

#### 3.2.3 Example

```acir
interface DiffPairLike {
  output OUT : Diff
}

interface CurrentMirrorLike {
  input SENSE : analog
  output TAP[0] : analog
  connectors {
    to DiffPairLike {
      SENSE--OUT.N
      TAP[0]--OUT.P
    }
  }
}

circuit DiffPair implements DiffPairLike {
  // ...
}
```

---

### 3.3 Instance Declaration: Constructor Syntax with `new`

Instance declarations use assignment syntax with the `new` keyword, placing parameters inline with instantiation.

#### 3.3.1 Syntax

```ebnf
instanceDecl = IDENT "=" "new" typeName ["(" argList ")"] "{" bindingList "}" ;
argList      = arg ("," arg)* ;
arg          = IDENT "=" argValue ;
argValue     = sizeExpr | scalarExpr ;
sizeExpr     = "size" "(" kvList ")" ;
scalarExpr   = NUMBER | IDENT ;
bindingList  = binding (["," | NEWLINE] binding)* ;
binding      = "." terminalPath "--" netPath ;
```

#### 3.3.2 Example

```acir
fill {
  dp = new DiffPair(InputPair=size(W=2u, L=180n, M=1), Tail=size(W=4u, L=180n, M=1)) {
    .VDD--VDD
    .GND--GND
    .IN--IN
    .OUT.N--mirror_gate
    .OUT.P--OUT
    .TAIL--VTAIL
  }

  cm = new CurrentMirror(Sense=size(W=2u, L=180n, M=1), ratio=1) {
    .VDD--VDD
    .GND--GND
    .SENSE--mirror_gate
    .TAP[0]--OUT
  }
}
```

#### 3.3.3 Rationale

Constructor syntax is familiar to programmers and makes the relationship between declaration and initialization explicit. All parameters (both size packs and scalar values) are passed in the constructor call, emphasizing that they are part of the instantiation. The brace block contains only terminal bindings.

---

### 3.4 Circuit Parameters in Signature

Circuit parameters and size declarations move from the body to the signature.

#### 3.4.1 Syntax

```ebnf
circuitDecl = "circuit" IDENT ["(" paramList ")"] "implements" traitList ["{" circuitBody "}"] ;
paramList   = paramDecl ("," paramDecl)* ;
paramDecl   = "size" IDENT ["=" sizeDefault]
            | typeName IDENT ["=" defaultValue] ;
typeName    = "real" | "int" | "bool" ;
```

#### 3.4.2 Example

```acir
circuit CurrentMirror(size Sense=(W=2u, L=180n, M=1), real ratio=1)
  implements CurrentMirrorLike {
  level EL
  supply VDD
  ground GND
  input SENSE : analog
  output TAP[0] : analog

  fill {
    // ...
  }
}

circuit DiffPair(size InputPair, size Tail) implements DiffPairLike {
  level EL
  inline
  supply VDD
  ground GND
  input IN : Diff
  output OUT : Diff
  input TAIL : bias

  fill {
    // ...
  }
}
```

#### 3.4.3 Rationale

Placing parameters in the signature makes the circuit's interface contract visible at the declaration site. Readers do not need to scan the body to understand what configuration a circuit accepts.

Parameters without defaults are required at instantiation; parameters with defaults are optional.

---

### 3.5 Primitive Device Templates

Primitive definitions introduce named, parameterized device templates. They serve two purposes:

1. Bind a device template to a concrete `device` key that names the model/subckt/P-cell being instantiated.
2. Define how an ACIR `size` tuple is expanded into the *exact* parameter set for that model/subckt/P-cell.

#### 3.5.1 Syntax

```ebnf
primitiveDecl   = "primitive" deviceKind IDENT "(" paramList ")" "{" primitiveBody "}" ;
deviceKind      = "nmos" | "pmos" | "resistor" | "capacitor" | "inductor" | "diode" ;
primitiveBody   = deviceDirective paramsBlock ;
deviceDirective = "device" STRING ;
paramsBlock     = "params" "{" paramMapping+ "}" ;
paramMapping    = IDENT "=" paramExpr ;
paramExpr       = sizeFieldAccess | expr ;
sizeFieldAccess = IDENT "." IDENT ;
```

#### 3.5.2 Semantics

A primitive definition declares a named template for a device kind (e.g., `nmos`) and specifies:

1. A `device` key (required) naming the concrete model/subckt/P-cell.
2. A `params` block (required) that is a 1-1 map to the parameters of that concrete model/subckt/P-cell.

The mapping from EL `size` tuples into PDK-specific model parameters happens *only here*, in EL primitives. No other phase (including `pdk scan`, include resolution, or SPICE emission) performs implicit parameter renaming (e.g., `M → nf`) or synthesis.

It is not legal for a primitive to omit `device`. Primitives must always be fully-resolved at EL so emission never needs to guess.

Cascode ships with always-available built-in device keys for simulation (e.g., `level1_nmos`, `level1_pmos`). For PDK-backed flows, `pdk scan` is responsible for generating legal primitive definitions in the EL document, including the correct `device` key and the correct, concrete `params` map for the chosen PDK model/subckt/P-cell.

#### 3.5.3 Example

```acir
primitive nmos Level1_NMOS(size primSize) {
  device "level1_nmos"
  params {
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }
}

primitive pmos Level1_PMOS(size primSize) {
  device "level1_pmos"
  params {
    W = primSize.W
    L = primSize.L
    m = primSize.M
  }
}

primitive nmos PdkBacked_NMOS(size primSize) {
  device "nfet_01v8"
  params {
    w = primSize.W
    l = primSize.L
    nf = primSize.M
  }
}
```

The `params` block is the central place where size expansion is specified (and where any computed size expressions can be referenced).

#### 3.5.4 Rationale

Explicit primitives with size-to-parameter mapping provide three benefits:

1. Size expansion is explicit and centralized: device declarations no longer need ad hoc rules for “what does this size mean?”

2. PDK binding is explicit when needed: the `device` key directly names the PDK device being targeted.

3. Device emission becomes predictable: emitters can rely on a fixed set of ACIR parameter names per device kind (e.g., MOSFETs consume `W`, `L`, `M`), regardless of how users author their size packs.

---

### 3.6 Device Declaration with Primitive Reference

Device declarations use constructor syntax referencing explicit primitives.

#### 3.6.1 Syntax

```ebnf
deviceDecl = deviceKind IDENT "=" "new" primitiveName "(" sizeArg ")" "{" bindingList "}" ;
sizeArg    = IDENT | sizeExpr ;
```

#### 3.6.2 Example

```acir
fill {
  nmos M_N = new Level1_NMOS(InputPair) {
    .B--GND, .D--OUT.N, .G--IN.P, .S--tnode
  }

  nmos M_P = new Level1_NMOS(InputPair) {
    .B--GND, .D--OUT.P, .G--IN.N, .S--tnode
  }

  nmos M_TAIL = new Level1_NMOS(Tail) {
    .B--GND, .D--tnode, .G--TAIL, .S--GND
  }
}
```

#### 3.6.3 Legacy Syntax (Deprecated)

The existing syntax remains valid during the transition period but is deprecated:

```acir
// Deprecated - will be removed in a future version
nmos M_TAIL (.B--GND, .D--tnode, .G--TAIL, .S--GND) : nmos
  size Tail
```

---

### 3.7 Constraint Assignment Operator

Constraint bindings use `=` instead of `:` for assignment.

#### 3.7.1 Syntax

```ebnf
numericConstraint = IDENT "=" benchMetric comparator value ;
benchMetric       = IDENT "::" IDENT ["at" nodeRef] ;
```

#### 3.7.2 Example

```acir
constraints {
  numeric {
    c_gbw = ACBench::GainBandwidth at net::OUT >= 20MHz
    c_gain = ACBench::PassbandGain at net::OUT >= 40dB
    c_pm = ACBench::PhaseMargin at net::OUT >= 60deg
    c_pwr = DCBench::QuiescentPower <= 500uW
  }
}
```

#### 3.7.3 Rationale

The colon character is now reserved for type annotations (as in `input IN : Diff`). Using `=` for assignment aligns with the constructor syntax and removes the notational overloading.

---

### 3.8 Computed Size Expressions

Size declarations may include computed expressions that reference other sizes and parameters.

#### 3.8.1 Syntax

```ebnf
sizeDecl = "size" IDENT "=" sizeExpr ;
sizeExpr = "size" "(" kvList ")"
         | "size" "(" exprList ")" ;
kvList   = kvPair ("," kvPair)* ;
kvPair   = IDENT "=" expr ;
exprList = expr ("," expr)* ;
expr     = IDENT "." IDENT          // field access
         | IDENT                    // parameter reference
         | NUMBER
         | expr ("*" | "/" | "+" | "-") expr ;
```

#### 3.8.2 Example

```acir
circuit CurrentMirror(size Sense=(W=2u, L=180n, M=1), real ratio=1)
  implements CurrentMirrorLike {
  // ...
  fill {
    size SenseMultiplied = size(Sense.W, Sense.L, Sense.M*ratio)

    pmos M_TAP0 = new Level1_PMOS(SenseMultiplied) {
      .B--VDD, .D--TAP[0], .G--SENSE, .S--VDD
    }
  }
}
```

#### 3.8.3 Rationale

Computed sizes enable parametric sizing without duplicating the base size values. The expression `Sense.M*ratio` makes the relationship between the sense transistor and tap transistor explicit.

---

### 3.9 Optional Comma-Separated Bindings

Terminal bindings within braces may optionally be comma-separated for brevity. Commas are not required; bindings may appear on separate lines without commas.

#### 3.9.1 Syntax

```ebnf
bindingList = binding (["," | NEWLINE] binding)* ;
binding     = "." terminalPath "--" netPath ;
```

All of the following forms are valid:

```acir
// Multi-line without commas (standard form)
{
  .B--VDD
  .D--SENSE
  .G--SENSE
  .S--VDD
}

// Single-line with commas (compact form)
{ .B--GND, .D--OUT.N, .G--IN.P, .S--tnode }

// Multi-line with commas (also valid)
{
  .B--GND,
  .D--OUT.N,
  .G--IN.P,
  .S--tnode
}
```

#### 3.9.2 Rationale

Comma separation is a convenience for compact single-line declarations, not a requirement. The standard multi-line form without commas remains valid and is often preferred for readability when there are many bindings.

---

### 3.10 Explicit Circuit Closing Braces

Circuits with signature parameters use explicit closing braces.

#### 3.10.1 Syntax

When a circuit declares parameters in its signature, it must use brace-delimited syntax:

```acir
circuit Name(params) implements Interfaces {
  // body
}
```

Circuits without signature parameters may continue to use indentation-based syntax during the transition period, but brace-delimited syntax is preferred.

---

## 4. Interactions with Other RFCs

### 4.1 RFC 0000 (Measurement Abstraction)

RFC 0000 introduces a `class`/`trait` distinction where `class` defines taxonomy (single inheritance) and `trait` defines capabilities (composable markers). This RFC adopts `interface` for what RFC 0000 calls connector-carrying contracts, preserving `trait` for capability markers.

Alignment:
- `class` (RFC 0000): Taxonomy with port bindings
- `trait` (RFC 0000): Capability marker (e.g., `HasSupplyPort`)
- `interface` (this RFC): Connector-carrying contract that circuits implement

If RFC 0000 is adopted, the `interface` keyword from this RFC would coexist with `class` and `trait`, each serving a distinct purpose.

### 4.2 RFC 0002 (Terminal Directionality)

RFC 0002 established directionality for port declarations. This RFC preserves that design: circuit ports require direction keywords (`input`, `output`, `io`), while bundle fields do not require directionality since they describe structural composition rather than interface contracts.

---

## 5. Grammar Changes

The following EBNF fragments summarize the grammar changes. A complete grammar specification is maintained in the parser implementation.

```ebnf
// Bundles
bundleDecl  = "bundle" IDENT "{" bundleField+ "}" ;
bundleField = IDENT ":" domain ;

// Interfaces
interfaceDecl = "interface" IDENT "{" interfaceBody "}" ;
interfaceBody = (portDecl | connectorBlock)* ;
connectorBlock = "connectors" "{" connectorTo+ "}" ;
connectorTo   = "to" IDENT "{" mapping+ "}" ;
mapping       = terminalPath "--" terminalPath ;

// Benches
benchDecl = "bench" IDENT "for" IDENT "{" benchBody "}" ;
benchBody = "builtin" IDENT outputsBlock ;
outputsBlock = "outputs" "{" IDENT+ "}" ;

// Primitives
primitiveDecl   = "primitive" deviceKind IDENT "(" paramList ")" "{" primitiveBody "}" ;
primitiveBody   = deviceDirective paramsBlock ;
deviceDirective = "device" STRING ;
paramsBlock     = "params" "{" paramMapping+ "}" ;
paramMapping    = IDENT "=" paramExpr ;
paramExpr       = sizeFieldAccess | expr ;
sizeFieldAccess = IDENT "." IDENT ;

// Circuits
circuitDecl = "circuit" IDENT ["(" paramList ")"] "implements" identList
              ["{" circuitBody "}"] ;
paramList   = paramDecl ("," paramDecl)* ;
paramDecl   = "size" IDENT ["=" sizeDefault]
            | typeName IDENT ["=" defaultValue] ;

// Instances
instanceDecl = IDENT "=" "new" IDENT ["(" argList ")"] "{" bindingList "}" ;
argList      = arg ("," arg)* ;
arg          = IDENT "=" argValue ;
argValue     = sizeExpr | scalarExpr ;

// Devices
deviceDecl = deviceKind IDENT "=" "new" IDENT "(" sizeArg ")" "{" bindingList "}" ;

// Bindings (commas optional between bindings)
bindingList = [binding (["," | NEWLINE] binding)*] ;
binding     = "." terminalPath "--" netPath ;

// Constraints
constraintBlock = "constraints" "{" constraintBody "}" ;
numericBlock    = "numeric" "{" numericConstraint+ "}" ;
numericConstraint = IDENT "=" benchMetric comparator value ;

// Sizes
sizeDecl = "size" IDENT "=" sizeExpr ;
sizeExpr = "size" "(" kvList ")"
         | "size" "(" exprList ")" ;
```

---

## 6. Migration from ACIR 2.x

This RFC introduces breaking syntax changes. A migration script (`scripts/acir_migrate_syntax.py`) will be provided to automate the transformation.

### 6.1 Transformation Rules

| Before | After |
|--------|-------|
| `bundle Name:` | `bundle Name {` |
| `trait Name:` | `interface Name {` |
| `outputs:` | `outputs {` |
| `fill:` | `fill {` |
| `connectors:` | `connectors {` |
| `to Target:` | `to Target {` |
| `constraints:` | `constraints {` |
| `numeric:` | `numeric {` |
| `harness:` | `harness {` |
| `inst id : Type` | `id = new Type(...) { ... }` |
| `param name = value` (in inst body) | `name=value` (in constructor call) |
| `c_gbw : Bench::Metric ...` | `c_gbw = Bench::Metric ...` |
| `nmos M (...) : nmos` | `nmos M = new PrimName(...) { ... }` |

### 6.2 Manual Review Required

The following transformations require manual review:

1. Primitive selection: The migration tool cannot determine which PDK primitive to use; it will insert a placeholder `Level1_NMOS` or `Level1_PMOS` that must be replaced with the appropriate primitive name.

   Note: `Level1_NMOS`/`Level1_PMOS` are intended to be always-legal defaults (bound to `device "level1_nmos"` / `device "level1_pmos"`). When targeting a PDK-backed flow, `pdk scan` is responsible for generating (or rewriting) the primitive definitions in the EL document to use PDK device keys (e.g., `device "nfet_01v8"`), so the migration tool alone cannot finalize this choice.

2. Size expression extraction: Complex parameter expressions (e.g., `M = $ratio`) should be converted to computed size expressions, but the tool may not handle all cases.

3. Parameter migration: Parameters previously declared in circuit bodies (using `param`) or passed via `param` in instance brace blocks must be moved to circuit signatures and constructor calls respectively.

---

## 7. Alternatives Considered

### 7.1 Keep Colon-Based Syntax

Maintaining the existing colon-based, indentation-delimited syntax would avoid migration effort but perpetuates the ambiguity and parsing complexity described in §1.

### 7.2 Use Parentheses Instead of Braces

Some languages use parentheses for block delimiters. Braces were chosen because they are more commonly associated with block structure in C-family languages and are visually distinct from function call parentheses.

### 7.3 Make `interface` and `trait` Synonyms

Treating `interface` and `trait` as synonyms would simplify the keyword set but would obscure the semantic distinction introduced in RFC 0000 between connector contracts and capability markers.

### 7.4 Optional Primitives

Making primitive definitions optional (allowing the current implicit device syntax) would ease migration but would perpetuate the PDK abstraction problem. Requiring primitives ensures all EL documents have explicit PDK binding points.

---

## 8. Implementation Plan

1. Update the ACIR grammar (ANTLR) with the new syntax rules.
2. Extend the lexer with the `interface` and `primitive` keywords.
3. Update the parser to handle both old and new syntax during transition.
4. Modify the ACIR writer to emit the new syntax.
5. Create the migration script (`scripts/acir_migrate_syntax.py`).
6. Update all golden files in `tests/golden/acir/`.
7. Update Chapter 3 of the specification with the new syntax.
8. Remove support for the deprecated syntax after one release cycle.
