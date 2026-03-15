# Cascode language guide

This guide complements the normative specification in [spec/language](../../spec/language). It
focuses on practical authoring patterns, file organization, and troubleshooting.

If you are writing or reviewing language rules, start with
[spec/language/README.md](../../spec/language/README.md).

## Navigation

- Style guide: [docs/language/style.md](./style.md)
- Bench cookbook: [docs/language/bench-cookbook.md](./bench-cookbook.md)
- Connectors and attach: [docs/language/connectors.md](./connectors.md)
- Troubleshooting: [docs/language/troubleshooting.md](./troubleshooting.md)

## Quick pointers

- Short, complete example: [tests/golden/cas/bench/RcLowpass.el.cai](../../tests/golden/cas/bench/RcLowpass.el.cai)
- Standard benches: [lib/std/bench](../../lib/std/bench)
- Interface bench bindings: [lib/std/amp/SingleEndedOpAmp.cas](../../lib/std/amp/SingleEndedOpAmp.cas)
- Connector-driven hierarchy (`attach`): [tests/golden/cas/hierarchy](../../tests/golden/cas/hierarchy)
