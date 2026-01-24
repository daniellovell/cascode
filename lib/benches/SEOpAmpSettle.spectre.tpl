// cascode SEOpAmpSettle (Spectre)
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
// Harness: small step via balun, source impedance, output load
// ----------------------------------------------------------------------------
VSS (vss 0) vsource dc=0
VCM (vcm vss) vsource dc={{ vcm }}

// Step source around VCM (amplitude provided upstream, e.g., 1%, 10%, 100%)
VIN (vin_src vss) vsource type=pulse val0={{ vcm }} val1={{ vcm + step_amp_v }} \
    rise={{ step_rise_s }} fall={{ step_fall_s }} width={{ step_width_s }} delay={{ step_delay_s }} period={{ step_period_s }}

subckt ideal_balun d c p n
    K0 (d 0 p c) transformer n1=2
    K1 (d 0 c n) transformer n1=2
ends ideal_balun

IBAL_IN (vin_src vcm in_p_drv in_n_drv) ideal_balun

RINP (IN_P in_p_drv) resistor r={{ env.source_ohms/2 }}
RINN (IN_N in_n_drv) resistor r={{ env.source_ohms/2 }}

CLOAD (OUT vss) capacitor c={{ env.cload_f }}
{{ if env.rload_ohms && env.rload_ohms > 0 }}
RLOAD (OUT vss) resistor r={{ env.rload_ohms }}
{{ end }}

// ----------------------------------------------------------------------------
// Options and Analyses
// ----------------------------------------------------------------------------
simulatorOptions options reltol=1e-3 vabstol=1e-6 iabstol=1e-12 temp={{ spec.temperature_c }} tnom={{ spec.temperature_c }} \
    gmin=1e-12 maxnotes=5 maxwarns=5 digits=5 cols=80 pivrel=1e-3

dcOp dc write="spectre.dc" maxiters=150 maxsteps=10000 annotate=status

tran tran stop={{ tran_stop_s }} errpreset=conservative {{ if tran_maxstep_s }}maxstep={{ tran_maxstep_s }}{{ end }} annotate=status

saveOptions options save=allpub
save IN_P IN_N OUT

