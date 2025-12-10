ACIR golden snapshots
=====================

This directory holds canonical ACIR text snapshots (`*.cir`) for
selected Cascode sources. Each subdirectory mirrors a logical package or
domain, for example:

- `ota/OTA5TSingleEnded.ml.cir`
- `ota/OTA5TSingleEndedSimplified.ml.cir`

Files are named with the pattern `{circuit}.{level}.cir` where level is one of:
- `hl` - High Level (slots)
- `ml` - Mid Level (instances)
- `el` - Electrical Level (devices)

The v0 implementation validates ACIR for the OTA motif in code (see
`OtaCompilerTests`) and compares compiler output to these snapshots.


