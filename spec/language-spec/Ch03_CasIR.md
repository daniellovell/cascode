# CasIR: Chapter 3 - `cascode` Intermediate Representation

> This chapter defines CasIR as a data model and on-disk JSON format that carries circuit connectivity and analysis intent from Cascode ADL to synthesis, sizing, verification, and SPICE emission.


---

## 3.0 Summary

CasIR serves as the single, authoritative handoff between the Cascode front end and the rest of the toolchain. Its role is both simple and critical: representing every electrical connection as a binding from an instance pin to a net, preserving sufficient structure and metadata to support search operations, rewrites, sizing, and benchmark generation, while maintaining deterministic output that facilitates diff operations and serves as a golden artifact in tests and reviews.

If we do this well, getting from ADL to SPICE is a straight line: parse and elaborate ADL into instances and nets, write CasIR, pick and size implementations, then print SPICE by looking up port ordering in library templates and substituting the already-known node names.

---

## 3.1 Design Principles

The CasIR design prioritizes **connectivity as the primary concern**, establishing the port-to-net mapping within each motif instance as the sole source of truth for edges, deliberately avoiding duplication in canonical form. The **uniform instance model** ensures that after desugaring, every ADL structure becomes motif instances with ports and parameters, with syntactic sugar for constructs like attach, pair, mirror taps, and feedback already expanded.

**Deterministic JSON** output maintains stability by sorting arrays by id and writing objects with sorted keys, ensuring diff stability and CI compatibility. **Elaboration levels** provide flexibility through three distinct modes: HL (with open slots), ML (slots chosen with some symbolic parameters), and EL (fully numeric and SPICE-ready), with pin coverage rules becoming more stringent at each level.

**Derived indices remain optional** - while tools may serialize incidence or adjacency indices for debugging purposes, these are derived views that loaders must recompute and verify if present. Finally, the **extensible, non-leaky** architecture places vendor or dialect fields under extensions, avoiding special-purpose modifications to the core model.

---

## 3.2 Root Document Structure

The on-disk format is JSON. Canonical files use UTF-8, LF newlines, sorted keys, and explicit units where physical dimensions apply.

```json
{
  "ir_version": 1,
  "format": "casir-json-1",
  "level": "EL",                    
  "meta": { "tool": "cascode-0.2.0", "when": "2025-09-19T00:00:00Z" },

  "nets":      [ /* 3.3.1 */ ],
  "bundles":   [ /* 3.3.2 */ ],
  "motifs":    [ /* 3.3.3 */ ],

  "constraints": { /* 3.5 */ },
  "harness":   { /* 3.6 */ },
  "benches":   [ /* symbolic bench names */ ],
  "provenance":{ /* 3.8 */ },

  "indices":   { /* 3.4 optional derived */ },
  "extensions":{ }
}
```

Conformance requires that a reader can reconstruct the entire connectivity graph from motifs[].ports alone. All other graph views must match if present.

---

## 3.3 Graph Model

CasIR models the circuit as a bipartite graph. Instance pins connect to nets. The authoritative mapping is:

f: (instanceId, pinPath) -> netId

### 3.3.1 Nets

```json
{ "id": "OUT", "domain": "electrical", "roles": ["ota_out"] }
```

The domain field specifies one of supply, ground, electrical, bias, rf, or clk, with extensions permitted to introduce additional domains under their respective namespaces. Roles provide optional labels that assist pattern matching, bench selection, and diagnostics. The rail field supplies an optional canonical rail name for supply or ground nets, such as VDD or GND.

Invariants

- A net id is unique within the document.
- Supply and ground rails referenced by instances must correspond to exactly one canonical net each per rail name.

### 3.3.2 Bundles

Bundles are optional groupings of related nets for convenience, most commonly differential pairs.

```json
{ "id": "IN", "type": "Diff", "fields": {"p": "VINP", "n": "VINN"} }
```

**Rules**

Bundles do not create edges directly; instead, pins address bundle fields via dotted paths such as in.p and in.n, which resolve to the underlying nets. At ML and EL elaboration levels, all referenced bundle fields must resolve to concrete nets.

### 3.3.3 Motif Instances

Every design entity is a motif instance with type, ports, and params. Ports holds the only authoritative edges.

```json
{
  "id": "dp",
  "type": "DiffPairNMOS",
  "traits": ["AmplifierStage"],
  "ports": {
    "in.p": "VINP",
    "in.n": "VINN",
    "drain_l": "N1",
    "drain_r": "N2",
    "src": "NSRC",
    "gnd": "GND"
  },
  "params": {
    "L": {"value": 1.8e-7, "unit": "m"},
    "W": {"symbolic": "Auto"}
  },
  "impl": { "char_ref": "lib.motifs/dp_nmos@1.3" }
}
```

Pin Path Grammar

- pinPath = ident ( "." ident )*
- ident = [A-Za-z_][A-Za-z0-9_]*

**Guidance**

External connectivity should prefer stable, named sub-pins over numeric indices (for example, tap.OUT rather than tap[1]) to improve diff stability and provenance tracking. Indices should be used only when the motif schema fundamentally requires ordered pins, at which point index positions become part of the schema contract.

### 3.3.4 Mirrors and Other Structured Pins

All external connectivity is under ports. Additional per-pin metadata such as mirror ratios live under pins_meta keyed by the same pin paths.

```json
{
  "id": "mirP",
  "type": "CurrentMirror",
  "params": {"polarity": "PMOS"},
  "ports": {"sense": "N1", "vref": "VDD", "tap.OUT": "OUT", "tap.N2": "N2"},
  "pins_meta": {"tap.OUT": {"ratio": 2}, "tap.N2": {"ratio": 1}}
}
```

This keeps connectivity uniform and makes incidence building trivial.

---

## 3.4 Derived Indices (Optional)

Tools routinely need fast graph queries. CasIR allows serializing derived views under indices. They are optional and must match ports exactly.

```json
"indices": {
  "connectivity_hash": "sha256:...", 
  "pin_to_net": { "dp.in.p": "VINP", "dp.drain_l": "N1" },
  "net_to_pins": { "VINP": ["dp.in.p"], "N1": ["dp.drain_l", "mirP.sense"] },
  "adjacency":   { "instance_to_instances": {"dp": ["mirP", "tail"]} }
}
```

The connectivity_hash is computed from a canonical serialization of motifs[].ports only, with readers required to recompute and compare when indices are present. Writers should not serialize indices by default, reserving them for debugging scenarios or heavy-duty solvers that benefit from a warm cache.

---

## 3.5 Constraints and Measurement Intents

Constraints live alongside the graph and come in four main kinds. They are evaluated during synthesis, sizing, and verification.

```json
"constraints": {
  "numeric": [
    {"id": "c_gbw", "kind": "ineq", "lhs": {"metric": "gbw"}, "op": ">=", "rhs": {"value": 1.0e8, "unit": "Hz"}, "scope": {"node": "OUT"}}
  ],
  "tech": [
    {"id": "t_lmin", "kind": "limit", "on": "*", "rule": "L>=", "value": 1.8e-7, "unit": "m"}
  ],
  "graph": [
    {"id": "g_card_tail", "rule": "cardinality", "select": "type:TailNMOS", "min": 1, "max": 1},
    {"id": "g_path", "rule": "path_exists", "from": "IN.p", "to": "OUT", "through_types": ["CurrentMirror"]}
  ],
  "measure": [
    {"id": "m_gbw", "bench": "AC_OpenLoop", "metric": "gbw", "node": "OUT"}
  ]
}
```

**Guidance**

Graph constraints operate on the derived incidence graph, leveraging the fact that explicit edges eliminate the need for wiring inference. Numeric constraints and measurement intents carry explicit units, with sizing tools responsible for conversion to internal SI base units.

---

## 3.6 Harness: Environment for Benches

The harness holds bench-only elements derived from ADL env blocks: supply values, source impedances, loads, and PVT selections. Harness elements are not part of the design graph and should not affect layout or LVS.

```json
"harness": {
  "supplies": [ {"net": "VDD", "value": 1.8, "unit": "V"} ],
  "sources":  [ {"bundle": "IN", "Z": 50.0, "unit": "Ohm"} ],
  "loads":    [ {"node": "OUT", "C": 1.0e-12, "unit": "F"} ],
  "icmr":     {"min": 0.55, "max": 0.75, "unit": "V"},
  "pvt":      {"corners": ["TT@27C", "SS@-40C", "FF@125C"]}
}
```

### 3.6.1 Bench Configuration (Extensions)

CasIR lists selected benches in `benches` for reproducibility. When a bench is
parameterized in ADL (e.g., `StepToggle { node=…, freq=…, duty=… }`), its
configuration is serialized under `extensions.benches.<BenchName>`.

```json
"benches": ["StepToggle"],
"extensions": {
  "benches": {
    "StepToggle": { "node": "COMP_OUT", "freq": {"value": 5.0e7, "unit": "Hz"}, "duty": 0.5, "slew": {"symbolic": "Auto"}, "cycles": 3 }
  }
}
```

Readers that do not understand a given bench must ignore its extension block.

---

## 3.7 Elaboration Levels

CasIR files declare a level: HL, ML, or EL. Pin coverage and parameter rules depend on the level.

HL - High Level

- Slots are represented as instances with type "__slot__" and required traits. All pins are connected to nets, but values and some params may be symbolic or null.

ML - Mid Level

- Slots are replaced by concrete motif types. All pins are connected to nets. Params may still be symbolic.

EL - Electrical Level

- All params are numeric. All pins are connected. The document is ready for SPICE emission.

Parameter Representation

- A parameter is either a number or a symbolic placeholder. When physical, it includes a unit.

```json
"params": {
  "W": {"value": 2.0e-6, "unit": "m"},
  "L": {"value": 1.8e-7, "unit": "m"},
  "Cc": {"symbolic": "Auto"}
}
```

### 3.11.1 Example: ML CasIR for Latch→Pad Buffer Slice (with stdcell INV)

This ML example focuses on the output buffer slot filled by a PDK stdcell
inverter after synthesis. Nets are electrical; the stdcell appears as a normal
motif instance with explicit rails.

```json
{
  "ir_version": 1,
  "format": "casir-json-1",
  "level": "ML",
  "meta": {"tool": "cascode-0.2.0", "when": "2025-09-21T00:00:00Z"},

  "nets": [
    {"id": "VDD",  "domain": "supply",   "rail": "VDD"},
    {"id": "GND",  "domain": "ground",   "rail": "GND"},
    {"id": "COMP_OUT", "domain": "electrical"},
    {"id": "PAD",  "domain": "electrical"}
  ],

  "motifs": [
    {
      "id": "Buf",
      "type": "sky130_fd_sc_hd__inv_4",
      "traits": ["InverterLike"],
      "ports": {"IN": "COMP_OUT", "OUT": "PAD", "VDD": "VDD", "GND": "GND", "VPB": "VDD", "VNB": "GND"},
      "impl": { "view": "spectre", "subckt": "sky130_fd_sc_hd__inv_4", "char_ref": "lib.std.sky130.hd/inv_4@1.0" }
    }
  ],

  "constraints": {
    "numeric": [
      {"id": "c_rise", "kind": "ineq", "lhs": {"metric": "rise_time", "node": "PAD", "v_lo": {"symbolic":"0.1*VDD"}, "v_hi": {"symbolic":"0.9*VDD"}}, "op": "<=", "rhs": {"value": 1.2e-9, "unit": "s"}},
      {"id": "c_fall", "kind": "ineq", "lhs": {"metric": "fall_time", "node": "PAD", "v_hi": {"symbolic":"0.9*VDD"}, "v_lo": {"symbolic":"0.1*VDD"}}, "op": "<=", "rhs": {"value": 1.2e-9, "unit": "s"}},
      {"id": "c_voh",  "kind": "ineq", "lhs": {"metric": "voh", "node": "PAD"}, "op": ">=", "rhs": {"value": 0.9, "unit": "VDD"}},
      {"id": "c_vol",  "kind": "ineq", "lhs": {"metric": "vol", "node": "PAD"}, "op": "<=", "rhs": {"value": 0.1, "unit": "VDD"}}
    ],
    "measure": [
      {"id": "m_rise", "bench": "StepToggle", "metric": "rise_time", "node": "PAD"},
      {"id": "m_fall", "bench": "StepToggle", "metric": "fall_time", "node": "PAD"}
    ]
  },

  "harness": {
    "supplies": [ {"net": "VDD", "value": 1.8, "unit": "V"} ],
    "loads":    [ {"node": "PAD", "C": 1.5e-11, "unit": "F"} ]
  },

  "benches": ["StepToggle"],
  "extensions": {
    "benches": {
      "StepToggle": { "node": "COMP_OUT", "freq": {"value": 5.0e7, "unit": "Hz"}, "duty": 0.5, "slew": {"symbolic": "Auto"}, "cycles": 3 }
    }
  }
}
```

---

## 3.8 Provenance and Diagnostics

Provenance links IR elements back to ADL source and records transformation steps. This enables precise diagnostics and reproducibility.

```json
"provenance": {
  "sources": [ {"file": "examples/OTA5T.cas", "span": {"from": 1, "to": 120}} ],
  "pin_spans": { "dp.drain_l": {"file": "examples/OTA5T.cas", "from": 10, "to": 10} },
  "aliases": [ {"name": "n1", "pin": "dp.drain_l"}, {"name": "n2", "pin": "dp.drain_r"} ],
  "transforms": ["desugar.attach", "desugar.mirror", "slot.fill"]
}
```

---

## 3.9 Validation Rules

CasIR validation executes after build completion and before consumption by downstream passes, enforcing several invariants:

- **Pin coverage**: every required pin path for every instance appears exactly once in ports at ML and EL.
- **Bundle completeness**: any referenced bundle field resolves to a concrete net id at ML and EL.
- **Domain compatibility**: pin kind and net domain are compatible according to the library schema.
- **Rail uniqueness**: each named rail such as VDD or GND maps to one net id across the document.
- **No dangling nets**: any net with zero incident pins is pruned unless referenced by harness.
- **Indices consistency**: when indices are present, connectivity_hash matches a recomputed hash from motifs[].ports.
- **Allowed loops**: cycles are allowed unless explicitly forbidden by rule or library schema. Algebraic loops of ideal passives without controlled sources may be rejected.

Diagnostics leverage source spans via provenance information, ensuring error messages point to the specific ADL construct that introduced the problematic edge or parameter.

---

## 3.10 Core IR Operations

The synthesis and optimization engine modifies the graph through a constrained set of operations that update ports and mark indices dirty:

- add_instance(type, id, ports, params)
- connect(inst.portPath, netId)
- disconnect(inst.portPath)
- new_net(domain, rail)
- merge_nets(a, b)
- split_net(n, partition)
- replace_subgraph(patternId, binder)
- set_param(inst, name, value)

High-level patterns and syntactic sugar in ADL—including attach, pair, mirror, and feedback constructs—lower to sequences of these primitive operations during the desugaring phase.

---

## 3.11 Example: EL CasIR for a 5T OTA Slice

```json
{
  "ir_version": 1,
  "format": "casir-json-1",
  "level": "EL",
  "meta": {"tool": "cascode-0.2.0", "when": "2025-09-19T00:00:00Z"},

  "nets": [
    {"id": "VDD",  "domain": "supply",    "rail": "VDD"},
    {"id": "GND",  "domain": "ground",    "rail": "GND"},
    {"id": "VINP", "domain": "electrical"},
    {"id": "VINN", "domain": "electrical"},
    {"id": "N1",   "domain": "electrical"},
    {"id": "N2",   "domain": "electrical"},
    {"id": "NSRC", "domain": "electrical"},
    {"id": "OUT",  "domain": "electrical"}
  ],

  "bundles": [
    {"id": "IN", "type": "Diff", "fields": {"p": "VINP", "n": "VINN"}}
  ],

  "motifs": [
    {
      "id": "dp",
      "type": "DiffPairNMOS",
      "traits": ["AmplifierStage"],
      "ports": {"in.p": "VINP", "in.n": "VINN", "drain_l": "N1", "drain_r": "N2", "src": "NSRC", "gnd": "GND"},
      "params": {"L": {"value": 1.8e-7, "unit": "m"}, "W": {"value": 2.0e-6, "unit": "m"}}
    },
    {
      "id": "tail",
      "type": "TailCurrentSourceNMOS",
      "ports": {"out": "NSRC", "gate": "VBIAS_N", "gnd": "GND"},
      "params": {"L": {"value": 5.0e-7, "unit": "m"}, "W": {"value": 1.0e-6, "unit": "m"}}
    },
    {
      "id": "pml",
      "type": "PMOSMirrorActiveLoad",
      "ports": {"sense": "N1", "vref": "VDD", "tap.OUT": "OUT"},
      "pins_meta": {"tap.OUT": {"ratio": 1}}
    },
    {
      "id": "cl",
      "type": "Cap",
      "ports": {"p": "OUT", "n": "GND"},
      "params": {"C": {"value": 1.0e-12, "unit": "F"}}
    }
  ],

  "constraints": {
    "numeric": [
      {"id": "c_gbw",  "kind": "ineq", "lhs": {"metric": "gbw"},     "op": ">=", "rhs": {"value": 5.0e7, "unit": "Hz"}, "scope": {"node": "OUT"}},
      {"id": "c_gain", "kind": "ineq", "lhs": {"metric": "gain_db"}, "op": ">=", "rhs": {"value": 55,    "unit": "dB"}, "scope": {"node": "OUT"}},
      {"id": "c_pm",   "kind": "ineq", "lhs": {"metric": "pm_deg"},  "op": ">=", "rhs": {"value": 60,    "unit": "deg"}, "scope": {"node": "OUT"}},
      {"id": "c_pwr",  "kind": "ineq", "lhs": {"metric": "power"},   "op": "<=", "rhs": {"value": 2.0e-3, "unit": "W"}}
    ],
    "graph": [
      {"id": "g_card_tail", "rule": "cardinality", "select": "type:TailCurrentSourceNMOS", "min": 1, "max": 1},
      {"id": "g_path", "rule": "path_exists", "from": "IN.p", "to": "OUT", "through_types": ["PMOSMirrorActiveLoad"]}
    ],
    "tech": [ {"id": "t_lmin", "kind": "limit", "on": "*", "rule": "L>=", "value": 1.8e-7, "unit": "m"} ],
    "measure": [ {"id": "m_gbw", "bench": "AC_OpenLoop", "metric": "gbw", "node": "OUT"} ]
  },

  "harness": {
    "supplies": [ {"net": "VDD", "value": 1.8, "unit": "V"} ],
    "loads":    [ {"node": "OUT", "C": 1.0e-12, "unit": "F"} ],
    "icmr":     {"min": 0.55, "max": 0.75, "unit": "V"},
    "pvt":      {"corners": ["TT@27C"]}
  },

  "benches": ["AC_OpenLoop", "UnityUGF", "Step"],
  "provenance": {"sources": [ {"file": "examples/OTA5T.cas", "span": {"from": 1, "to": 120}} ]}
}
```

---

## 3.12 SPICE Emission

SPICE emission is a direct traversal over motifs. No connectivity inference is required.

1) For each motif instance, look up its SPICE template from the library or from wrap spice metadata.
2) Determine the ordered list of pins required by the template. For each pin name, find the net id from ports and print the node.
3) Print parameter values. Where supported by the simulator, emit named parameters; otherwise, inject them as model or width/length tokens.
4) Append harness devices and analysis statements generated from benches and measure intents.

Because ports hold all edges, node substitution is O(1) per pin. This keeps the SPICE writer small, predictable, and testable.

Note (stdcells)

* Stdcells wrapped as motifs emit as ordinary `.subckt` instances with their rail pins included. No special handling is required beyond using the mapped pin order and including the PDK stdcell deck(s) discovered by the PDK scan.

---

## 3.13 Canonical JSON Writer Rules

To keep diffs and golden tests stable, the canonical writer follows these rules:

- Sort arrays by id where present, otherwise by a stable key defined in the local section.
- Sort object keys lexicographically.
- Use plain numbers; forbid NaN and Infinity in on-disk IR.
- Always include units for physical quantities. Internal tools may normalize to SI, but on disk we do not hide units.
- Use UTF-8, LF, and no trailing spaces.

---

## 3.14 Extensibility

Vendor or dialect additions live under extensions. Extensions must not redefine core keys. If an extension affects connectivity semantics, it must include a versioned schema and a compatibility note.

---

## 3.15 Conformance and Testing

A conformant CasIR producer must satisfy the following requirements:

- Emits motifs with ports that cover all required pins at the declared level.
- Emits nets and, if used, bundles consistent with pin paths.
- Emits constraints with explicit units and resolvable node references.
- Ensures indices, if present, match ports according to connectivity_hash.

The testing strategy encompasses three complementary approaches:

- Golden IR snapshots in JSON under tests/golden.
- Connectivity unit tests rebuild incidence from ports and assert simple graph properties: path_exists, cardinality, and fanout.
- Round-trip tests from ADL to IR to SPICE for small examples.

---

## 3.16 *cascode* -> CasIR -> SPICE

The transformation from ADL to SPICE follows a systematic progression through CasIR. Parsing and desugaring map ADL constructs to instances and nets, expanding high-level constructs like attach, pair, mirror, and feedback into concrete motifs and connections. CasIR captures these connections uniformly within ports, enabling the synthesis engine to perform path queries and edits directly without inferring wiring relationships.

Sizing augments parameter values without modifying connectivity, and once all parameters become numeric, the IR reaches EL status and becomes ready for emission. SPICE writing reads ports to determine node names and prints devices according to library templates, with harness elements and bench configurations derived from constraints.measure and harness specifications.

This architectural separation maintains focus on structure and semantics in the front end, while the back end treats CasIR as a stable, mechanical contract.
