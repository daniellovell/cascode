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

The v0 implementation validates Cascode for the OTA motif in code (see
`OtaCompilerTests`) and compares compiler output to these snapshots.


