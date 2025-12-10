
Simulation Testing
==================

This directory contains infrastructure for verifying that generated SPICE netlists
are simulatable with ngspice.

Setup
-----

Install the conda environment:

    conda env create -f environment.yml
    conda activate cascode-sim

Running Tests
-------------

To verify all golden SPICE testbench files simulate successfully:

    ./verify_golden_spice.sh --verbose

To run ngspice on a single netlist:

    ./run_ngspice.sh path/to/netlist.sp

CI Integration
--------------

The `.github/workflows/simulation.yml` workflow runs these tests automatically on
push and pull request. It uses micromamba for fast conda environment setup.

Adding New Golden Tests
-----------------------

When adding new SPICE golden files under `tests/golden/spice/`:

1. Ensure the testbench file ends with `*Bench.sp` (e.g., `MyCircuit_ACBench.sp`)
2. The testbench must include the design file via `.include`
3. The testbench must use ngspice-compatible syntax (`.control`/`.endc` blocks)
4. Run `./verify_golden_spice.sh` locally before pushing

