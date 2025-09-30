gm_id_pmos.v1 — Spectre-first PMOS DC sweep harness.

This harness mirrors the Virtuoso characterization bench for PMOS devices:
  - sweeps VSG with optional scaled drain bias (VSD = alpha * VSG)
  - biases the device from VDD = 1 V
  - exports VSG, VD, ID (source-to-drain), gm, gds, cgs, cgd, and vth (signed as -VTH)

Currently Spectre is the primary backend; ngspice support will follow.
