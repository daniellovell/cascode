## Architecture Docs Guide

Purpose: Keep stable, present‑tense architecture in one place, separate from proposals (RFCs) and decisions (ADRs).

Doc types & locations
- Architecture docs (this folder): [docs/architecture](./) — current system design and invariants.
- RFCs (proposals): [docs/rfcs](../rfcs/README.md) — time‑boxed change proposals and historical design records.
- ADRs (decisions): [docs/adr](../adr/README.md) — short, immutable records of decisions with context.

Each architecture doc should fit on 1–2 pages and use this shape:
- Scope & goals — what this component owns and explicitly does not own.
- Responsibilities & boundaries — inputs/outputs, owning assemblies, allowed deps.
- Key components — classes/modules and how they collaborate.
- Data & flow — main pipelines, schemas, and invariants.
- Public surface — CLI/API entry points and stability expectations.
- Testing & observability — how we keep it correct and visible.
- Open questions / planned work — short, non‑binding list.

Style
- Present tense; describe what is, not what might be.
- Diagrams optional; keep text concise and skimmable.
- Cross-link source files and related RFC/ADR IDs when useful.

Update policy
- Update the relevant doc when behavior or boundaries change.
- If the change is non‑trivial (new APIs/DB schema/cross‑layer deps), write an RFC and add an ADR upon merge.

See also: [AGENTS.md](../../AGENTS.md) for size limits, testing expectations, and determinism rules.
