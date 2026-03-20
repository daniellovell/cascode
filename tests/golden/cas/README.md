Cascode golden snapshots
========================

This directory holds canonical linked Cascode text snapshots (`*.cai`) for
selected Cascode sources. Each subdirectory mirrors a logical library or
domain, for example:

- `ota/OTA5TSingleEnded.ml.cai`
- `ota/OTA5TSingleEndedSimplified.ml.cai`

Files are named with the pattern `{circuit}.{level}.cai` where level is one of:
- `hl` - High Level (bare synthesis request)
- `ml` - Mid Level (topology fixed, children may still be `Some` requests)
- `el` - Electrical Level (fully concrete and emission-ready)

The `.cai` extension indicates these are linked Cascode files. Default link
mode is self-contained (no `include` statements); include-pruned mode may
retain a minimal include set.

When a linked snapshot preserves `implements`, it is expected to preserve the
corresponding contract information as well, either directly in the document or
through retained includes. Commands that enforce complete-document semantics,
notably `link`, `emit`, and `erc`, should be able to validate the surviving
interface contract without relying on out-of-band context.

Render-only topology fixtures that are not valid complete documents do not
belong in this tree. Keep those under [tests/golden/render](../render) so the `.cai`
files in [tests/golden/cas](./) remain valid linked artifacts.

These snapshots are useful downstream fixtures, but they are not a substitute
for source-flow regressions. Behavior that depends on linking, inherited bench
bindings, or interface contract enforcement should also be covered from `.cas`
inputs so the link step itself remains under test.

The v0 implementation validates Cascode for the OTA motif in code (see
`OtaCompilerTests`) and compares compiler output to these snapshots.

Canonical source examples live alongside the linked snapshots. For the PCB
flow, use these source files as the long-term semantic references:

- `pcb/SensorFrontendPCB.hl.cas`
- `pcb/SensorFrontendPCB.ml.cas`
- `pcb/SensorFrontendPCB.el.cas`

