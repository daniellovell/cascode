CasIR JSON Schema
=================

This folder contains JSON Schemas for CasIR v1, corresponding to the spec in `spec/language-spec/Ch03_CasIR.md`.

Files
- `casir-json-1.schema.json` - Root schema for HL/ML/EL CasIR documents (Draft 2020-12).
- `casir-json-1-el.schema.json` - Overlay enforcing `level: "EL"` and forbidding symbolic params.

Notes
- The schema validates shapes, enums, and patterns (ids, pin paths). Cross-references (e.g., that every `ports` net id exists in `nets`) are validated by the compiler.
- Indices under `indices` are optional and derived. When present, they should be accompanied by a `connectivity_hash`, and tools must recompute and compare.
- Canonical on-disk CasIR uses JSON with sorted keys and ids; YAML is intentionally not supported.

Validation
- Use any Draft 2020-12 validator (e.g., `ajv`, `JsonSchema.Net`).
- Example CLI flow (to be implemented): `cascode verify --schema spec/casir-schema/casir-json-1.schema.json build/foo.cir.json`.

