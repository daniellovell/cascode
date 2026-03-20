# Cascode Language Specification

This directory contains the normative language specification. The spec describes the surface syntax
and the semantic contracts that the toolchain relies on.

Normative keywords (MUST, MUST NOT, SHOULD, MAY) are used in the sense of RFC 2119.

## Sources of truth

The authoritative grammar is [tools/language/Cascode.g4](../../tools/language/Cascode.g4). The
standard library under [lib/std](../../lib/std) and the golden fixtures under
[tests/golden/cas](../../tests/golden/cas) are the canonical examples of current syntax and
intended behavior.

## Versioning and artifacts

Cascode files may begin with a `VERSION` header:

```cascode
VERSION 5.0
```

The canonical version is [tools/language/CascodeVersion.cs](../../tools/language/CascodeVersion.cs).

Source files use `.cas`. Linked intermediate outputs use `.cai` and are expected to include a
`VERSION` header. The default linker mode emits self-contained `.cai`; include-pruned mode
(`--no-link-benches`) intentionally preserves a minimal include surface for bench dependencies.
Linking also extracts synthesis guidance into `<name>.synth.yaml`.

## Chapters

- Chapter 1 ([Ch01_Introduction.md](./Ch01_Introduction.md)) gives the language and toolchain overview.
- Chapter 2 ([Ch02_Core_Concepts.md](./Ch02_Core_Concepts.md)) defines the semantic model: terminals, connectivity, benches,
  constraints, and harness/environment intent.
- Chapter 3 ([Ch03_Syntax_Reference.md](./Ch03_Syntax_Reference.md)) is a syntax-oriented reference aligned to the grammar.
- Chapter 4 ([Ch04_Bench_System.md](./Ch04_Bench_System.md)) specifies declarative benches, bindings, and measurement
  expressions.

## Determinism

Cascode is intended to be diff-friendly. Constructs that elaborate or expand (such as connector-driven
attach and fill-block sugar) must do so deterministically so that golden assets remain stable and
meaningful.

## Practical guide

For a user-facing guide (patterns, style, cookbook, and troubleshooting), see:

- [docs/language/README.md](../../docs/language/README.md)
- [docs/language/style.md](../../docs/language/style.md)
- [docs/language/bench-cookbook.md](../../docs/language/bench-cookbook.md)
- [docs/language/connectors.md](../../docs/language/connectors.md)
- [docs/language/troubleshooting.md](../../docs/language/troubleshooting.md)
