// cascode SEOpAmpStability (Spectre)
simulator lang=spectre
global 0

// includes
{% for inc in includes_with_section %}
{% if section %}include "{{ inc }}" section={{ section }}{% else %}include "{{ inc }}"{% endif %}
{% endfor %}
{% for inc in includes_without_section %}
include "{{ inc }}"
{% endfor %}

// ----------------------------------------------------------------------------
// Harness: unity-gain non-inverting with loop break at IN_N via iprobe
// ----------------------------------------------------------------------------
VSS (vss 0) vsource dc=0
VCM (vcm vss) vsource dc={{ vcm }}

// Bias the non-inverting input at VCM
VIN_BIAS (IN_P vss) vsource dc={{ vcm }}

// Close loop from OUT to IN_N via iprobe (loop break here)
IPRB0 (OUT IN_N) iprobe

// Nominal output load
CLOAD (OUT vss) capacitor c={{ env.cload_f }}
{% if env.rload_ohms is defined and env.rload_ohms > 0 %}
RLOAD (OUT vss) resistor r={{ env.rload_ohms }}
{% endif %}

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

// Stability analysis across inferred frequency span
stb stb start={{ stb_start_hz }} stop={{ stb_stop_hz }} probe=IPRB0 localgnd=vss annotate=status

saveOptions options save=allpub
save IN_P IN_N OUT

