#!/usr/bin/env bash
# Runs ngspice on a SPICE netlist in batch mode and verifies it completes successfully.
# Usage: ./run_ngspice.sh <netlist.sp>
# Exit code 0 on success, non-zero on failure.
set -euo pipefail

if [[ $# -lt 1 ]]; then
    echo "Usage: $0 <netlist.sp>" >&2
    exit 1
fi

NETLIST="$1"

if [[ ! -f "$NETLIST" ]]; then
    echo "Error: Netlist file not found: $NETLIST" >&2
    exit 1
fi

echo "Running ngspice on: $NETLIST"
ngspice -b "$NETLIST"
echo "Simulation completed successfully."

