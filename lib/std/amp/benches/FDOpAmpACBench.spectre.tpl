// cascode FDOpAmpACBench (Spectre)
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
// Harness: differential sources via balun, source impedance, differential load
// ----------------------------------------------------------------------------
VSS (vss 0) vsource dc=0

// Common-mode bias for inputs
VCM (vcm vss) vsource dc={{ vcm }}

// Small-signal AC drive; differentialized via ideal balun
VIN (vin_src vss) vsource dc=0 ac={{ ac_mag }}

subckt ideal_balun d c p n
    K0 (d 0 p c) transformer n1=2
    K1 (d 0 c n) transformer n1=2
ends ideal_balun

IBAL_IN (vin_src vcm in_p_drv in_n_drv) ideal_balun

// Split source impedance across both legs into the DUT inputs
RINP (IN_P in_p_drv) resistor r={{ env.source_ohms/2 }}
RINN (IN_N in_n_drv) resistor r={{ env.source_ohms/2 }}

// Differential output loading
CLOADP (OUT_P vss) capacitor c={{ env.cload_f }}
CLOADN (OUT_N vss) capacitor c={{ env.cload_f }}
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
dcOpInfo info what=oppoint where=rawfile
modelParameter info what=models where=rawfile
element info what=inst where=rawfile
outputParameter info what=output where=rawfile
designParamVals info what=parameters where=rawfile
primitives info what=primitives where=rawfile
subckts info what=subckts where=rawfile

// Small-signal AC sweep
ac ac start={{ ac_start_hz }} stop={{ ac_stop_hz }} annotate=status

saveOptions options save=allpub
save IN_P IN_N OUT_P OUT_N
