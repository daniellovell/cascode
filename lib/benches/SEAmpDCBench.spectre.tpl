// cascode SEAmpDCBench (Spectre)
// DC characterization for single-ended amplifiers (single input, single output)
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
// Harness: single-ended DC input bias, output load
// ----------------------------------------------------------------------------
VSS (vss 0) vsource dc=0

{{ for supply in harness.supplies }}
V{{ supply.net }} ({{ supply.net }} vss) vsource dc={{ supply.value }}
{{ end }}

// Input DC bias source (swept or fixed)
VIN (IN vss) vsource dc={{ vcm }}

// Output load
CLOAD (OUT vss) capacitor c={{ env.cload_f }}
{{ if env.rload_ohms && env.rload_ohms > 0 }}
RLOAD (OUT vss) resistor r={{ env.rload_ohms }}
{{ end }}

// ----------------------------------------------------------------------------
// Device Under Test
// ----------------------------------------------------------------------------
XDUT {{ port_list }} {{ circuit_name }}

// ----------------------------------------------------------------------------
// Options and Analyses
// ----------------------------------------------------------------------------
simulatorOptions options reltol=1e-3 vabstol=1e-6 iabstol=1e-12 temp={{ spec.temperature_c }} tnom={{ spec.temperature_c }} \
    gmin=1e-12 maxnotes=5 maxwarns=5 digits=5 cols=80 pivrel=1e-3

{{ if sweep.InputDCBias }}
// InputDCBias sweep analysis
dcSweep dc param=VIN.dc start={{ sweep.InputDCBias.Start }} \
    stop={{ sweep.InputDCBias.Stop }} step={{ sweep.InputDCBias.Step }} \
    annotate=status
{{ else }}
// Single operating point
dcOp dc write="spectre.dc" maxiters=150 maxsteps=10000 annotate=status
{{ end }}

dcOpInfo info what=oppoint where=rawfile
modelParameter info what=models where=rawfile
element info what=inst where=rawfile
outputParameter info what=output where=rawfile
designParamVals info what=parameters where=rawfile
primitives info what=primitives where=rawfile
subckts info what=subckts where=rawfile

saveOptions options save=allpub
save IN OUT {{ for supply in harness.supplies }}{{ supply.net }}:p {{ end }}
