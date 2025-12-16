// cascode FDAmplifierStability (Spectre)
simulator lang=spectre
global 0

// includes
{{ for inc in includes_with_section }}
{{ if section }}include "{{ inc }}" section={{ section }}{{ else }}include "{{ inc }}"{{ end }}
{{ end }}
{{ for inc in includes_without_section }}
include "{{ inc }}"
{{ end }}

// ----------------------------------------------------------------------------
// Harness: unity-gain non-inverting (per-leg), loop break on negative leg
// ----------------------------------------------------------------------------
VSS (vss 0) vsource dc=0
VCM (vcm vss) vsource dc={{ vcm }}

// Bias the non-inverting input at VCM (differential)
VINP_BIAS (IN_P vss) vsource dc={{ vcm }}

// Close loop from OUT_P to IN_N via iprobe (approximate per-leg loop)
IPRB0 (OUT_P IN_N) iprobe

// Differential output loading (split capacitance equally)
CLOADP (OUT_P vss) capacitor c={{ env.cload_f/2 }}
CLOADN (OUT_N vss) capacitor c={{ env.cload_f/2 }}
{{ if env.rload_ohms && env.rload_ohms > 0 }}
RLOADP (OUT_P vss) resistor r={{ env.rload_ohms }}
RLOADN (OUT_N vss) resistor r={{ env.rload_ohms }}
{{ end }}

// ----------------------------------------------------------------------------
// Options and Analyses
// ----------------------------------------------------------------------------
simulatorOptions options reltol=1e-3 vabstol=1e-6 iabstol=1e-12 temp={{ spec.temperature_c }} tnom={{ spec.temperature_c }} \
    gmin=1e-12 maxnotes=5 maxwarns=5 digits=5 cols=80 pivrel=1e-3

dcOp dc write="spectre.dc" maxiters=150 maxsteps=10000 annotate=status

stb stb start={{ stb_start_hz }} stop={{ stb_stop_hz }} probe=IPRB0 localgnd=vss annotate=status

saveOptions options save=allpub
save IN_P IN_N OUT_P OUT_N

