// cascode SEOpAmpDCBench (Spectre)
// DC characterization for single-ended output operational amplifiers (differential input)
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
// Harness: differential inputs with common-mode bias, output load
// ----------------------------------------------------------------------------
VSS (vss 0) vsource dc=0

{{ for supply in harness.supplies }}
V{{ supply.net }} ({{ supply.net }} vss) vsource dc={{ supply.value }}
{{ end }}

// Common-mode bias for differential input
VCM (vcm vss) vsource dc={{ vcm }}

// Differential inputs biased at common-mode
VINP_BIAS (IN_P vss) vsource dc={{ vcm }}
VINN_BIAS (IN_N vss) vsource dc={{ vcm }}

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

{{ if sweep.InputDCCommonMode }}
// InputDCCommonMode sweep analysis: vary common-mode on both inputs
dcSweep dc param=VCM.dc start={{ sweep.InputDCCommonMode.start }} \
    stop={{ sweep.InputDCCommonMode.stop }} step={{ sweep.InputDCCommonMode.step }} \
    annotate=status {
  alter VINP_BIAS.dc=VCM.dc
  alter VINN_BIAS.dc=VCM.dc
}
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
save IN_P IN_N OUT {{ for supply in harness.supplies }}{{ supply.net }}:p {{ end }}
