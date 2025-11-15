CasIR golden snapshots
======================

This directory will hold canonical CasIR JSON snapshots (`*.cir`) for
selected Cascode sources. Each subdirectory mirrors a logical package or
domain, for example:

- `ota/OTA5TSingleEnded.ml.cir`
- `prim/DiffPair.hl.cir`

The v0 implementation validates CasIR for the OTA motif in code (see
`OtaCompilerTests`) but does not yet persist that JSON here. Once the
compiler surface is stable, the plan is to move those expectations into
versioned JSON files under this tree and have tests compare the compiler’s
output to these snapshots. 

