Cascode golden snapshots
========================

This directory holds canonical Cascode text snapshots (`*.cas`) for
selected Cascode sources. Each subdirectory mirrors a logical library or
domain, for example:

- `ota/OTA5TSingleEnded.ml.cas`
- `ota/OTA5TSingleEndedSimplified.ml.cas`

Files are named with the pattern `{circuit}.{level}.cas` where level is one of:
- `hl` - High Level (slots)
- `ml` - Mid Level (instances)
- `el` - Electrical Level (devices)

The v0 implementation validates Cascode for the OTA motif in code (see
`OtaCompilerTests`) and compares compiler output to these snapshots.


