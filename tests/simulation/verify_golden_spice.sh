#!/usr/bin/env bash
# Verifies that all golden SPICE testbench files are simulatable with ngspice.
# Usage: ./verify_golden_spice.sh [--verbose]
# Exit code 0 if all simulations pass, non-zero otherwise.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
GOLDEN_SPICE_DIR="$REPO_ROOT/tests/golden/spice"

VERBOSE=0
if [[ "${1:-}" == "--verbose" ]]; then
    VERBOSE=1
fi

if ! command -v ngspice &> /dev/null; then
    echo "Error: ngspice not found in PATH" >&2
    echo "Install with: conda env create -f tests/simulation/environment.yml" >&2
    exit 1
fi

FAILED=0
PASSED=0

# Find all testbench files (files with _*Bench.sp pattern, which include harness)
while IFS= read -r -d '' spfile; do
    relative="${spfile#$REPO_ROOT/}"
    
    if [[ $VERBOSE -eq 1 ]]; then
        echo "Testing: $relative"
    fi
    
    # Run ngspice in batch mode and capture output
    output=$(ngspice -b "$spfile" 2>&1)
    exit_code=$?
    
    # Check for measure failures or error messages (ngspice returns 0 even on measure failures)
    if [[ $exit_code -ne 0 ]] || echo "$output" | grep -qiE "measure.*failed|^Error:|no such function"; then
        ((FAILED++)) || true
        echo "FAIL: $relative"
        if [[ $VERBOSE -eq 1 ]]; then
            echo "$output" | grep -iE "measure.*failed|^Error:|no such function" || true
        fi
    else
        ((PASSED++)) || true
        if [[ $VERBOSE -eq 1 ]]; then
            echo "  PASS"
        fi
    fi
done < <(find "$GOLDEN_SPICE_DIR" -name "*Bench.sp" -type f -print0) || true

echo ""
echo "Results: $PASSED passed, $FAILED failed"

if [[ $FAILED -gt 0 ]]; then
    exit 1
fi
exit 0

