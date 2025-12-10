// cascode SEAmpACBench (Spectre)
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
// Harness: single-ended AC source, source impedance, and output load
// ----------------------------------------------------------------------------
// Local ground reference
VSS (vss 0) vsource dc=0

// DC bias at input (provided upstream; default passed as {{ vcm }})
VCM (vcm vss) vsource dc={{ vcm }}

// Small-signal stimulus: single-ended AC source with DC bias
VIN (vin_drv vss) vsource dc={{ vcm }} ac={{ ac_mag }}

// Source impedance
RIN (vin_drv IN) resistor r={{ env.source_ohms }}

// Output load on single-ended OUT
CLOAD (OUT vss) capacitor c={{ env.cload_f }}
{{ if env.rload_ohms && env.rload_ohms > 0 }}
RLOAD (OUT vss) resistor r={{ env.rload_ohms }}
{{ end }}

// ----------------------------------------------------------------------------
// Options and Analyses
// ----------------------------------------------------------------------------
simulatorOptions options reltol=1e-3 vabstol=1e-6 iabstol=1e-12 temp={{ spec.temperature_c }} tnom={{ spec.temperature_c }} \
    gmin=1e-12 maxnotes=5 maxwarns=5 digits=5 cols=80 pivrel=1e-3

// Bias first
dcOp dc write="spectre.dc" maxiters=150 maxsteps=10000 annotate=status
dcOpInfo info what=oppoint where=rawfile
modelParameter info what=models where=rawfile
element info what=inst where=rawfile
outputParameter info what=output where=rawfile
designParamVals info what=parameters where=rawfile
primitives info what=primitives where=rawfile
subckts info what=subckts where=rawfile

// Small-signal AC sweep (ranges inferred upstream from spec)
ac ac start={{ ac_start_hz }} stop={{ ac_stop_hz }} annotate=status

saveOptions options save=allpub
save IN OUT

