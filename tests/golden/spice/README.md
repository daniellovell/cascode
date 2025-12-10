SPICE golden netlists
=====================

This directory is reserved for SPICE netlists and bench harness netlists
generated from ACIR. It is not used by the v0 compiler slice yet. Once the
ACIR → SPICE backends are wired up, regression tests will read ACIR from
`tests/golden/acir/…`, materialize netlists here, and compare them against
these golden files (or validate them via simulator runs in CI where
available).

