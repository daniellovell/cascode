Cascode golden snapshots
========================

This directory holds canonical linked Cascode text snapshots (`*.cai`) for
selected Cascode sources. Each subdirectory mirrors a logical library or
domain, for example:

- `ota/OTA5TSingleEnded.ml.cai`
- `ota/OTA5TSingleEndedSimplified.ml.cai`

Files are named with the pattern `{circuit}.{level}.cai` where level is one of:
- `hl` - High Level (slots)
- `ml` - Mid Level (instances)
- `el` - Electrical Level (devices)

The `.cai` extension indicates these are linked Cascode files. Default link
mode is self-contained (no `include` statements); include-pruned mode may
retain a minimal include set.

When a linked snapshot preserves `implements`, it is expected to preserve the
corresponding contract information as well, either directly in the document or
through retained includes. Commands that enforce complete-document semantics,
notably `link`, `emit`, and `erc`, should be able to validate the surviving
interface contract without relying on out-of-band context.

These snapshots are useful downstream fixtures, but they are not a substitute
for source-flow regressions. Behavior that depends on linking, inherited bench
bindings, or interface contract enforcement should also be covered from `.cas`
inputs so the link step itself remains under test.

The v0 implementation validates Cascode for the OTA motif in code (see
`OtaCompilerTests`) and compares compiler output to these snapshots.


