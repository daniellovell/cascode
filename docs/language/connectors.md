# Connectors and `attach`

This guide explains connector-driven structural composition. The authoritative syntax for connectors
and `attach` is in `spec/language/Ch03_Syntax_Reference.md` and `spec/language/Ch02_Core_Concepts.md`.

## Connectors in interfaces

Interfaces may declare connectors that map pin references from one interface view to another:

```cascode
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
```

The connector body uses `--` between pin references. Pin references may include bundle fields
(`OUT.N`) and indices (`TAP[0]`).

## Applying connectors with `attach`

`attach` applies a connector mapping between instances inside a `fill {}` block:

```cascode
attach cm to dp via CurrentMirrorLike::DiffPairLike as mirror_node
```

Intuitively, `attach` says: “wire `cm` to `dp` using the connector mapping that `CurrentMirrorLike`
defines for `DiffPairLike`”.

### Why `via` is explicit

The `via A::B` path avoids ambiguity. It makes the selected connector mapping explicit in source so
reviewers and tools can see exactly which mapping is applied.

## Bundles and leaf completeness

When a connector references a bundle terminal (for example, `OUT : Diff`), the mapping is understood
at the level of the bundle’s leaf terminals (`OUT.P` and `OUT.N`). As a result:

- Connector mappings that use bundles must be complete at the leaf level.
- Bench bindings that map bundle terminals must map the complete leaf set.

This is a deliberate rule: partial bundle wiring is a common source of silent errors.

## Where to look for working examples

- Interface connectors: `lib/std/amp/Common.cas`
- Interface bench bindings (separate from connectors): `lib/std/amp/SingleEndedOpAmp.cas`
- Hierarchical attach patterns: `tests/golden/cas/hierarchy/**`
  - `tests/golden/cas/hierarchy/OTA5T_Hierarchical.el.cai` is a compact, readable example.

## Worked example: CurrentMirrorLike → DiffPairLike

Connector declaration (from `lib/std/amp/Common.cas`):

```cascode
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

interface DiffPairLike {
  output OUT : Diff
}
```

Applying the connector in a circuit fill block:

```cascode
fill {
  DiffPair dp = new DiffPair(...) { .OUT--OUT /* ... */ }
  CurrentMirror cm = new CurrentMirror(...) { /* ... */ }
  attach cm to dp via CurrentMirrorLike::DiffPairLike
}
```

The connector makes the intended wiring reusable and reviewable: instead of repeating “sense goes to
OUT.N, tap goes to OUT.P” everywhere, the mapping is defined once on the interface and then applied
explicitly where needed.
