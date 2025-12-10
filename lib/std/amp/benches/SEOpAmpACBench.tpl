// cascode SEOpAmpACBench (Spectre)
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
// Harness: sources, balun, source impedance, and output load
// ----------------------------------------------------------------------------
// Local ground reference
VSS (vss 0) vsource dc=0

// Common-mode bias at inputs (provided upstream; default passed as {{ vcm }})
VCM (vcm vss) vsource dc={{ vcm }}

// Small-signal stimulus: single-ended AC source; differentialized via ideal balun
VIN (vin_src vss) vsource dc=0 ac={{ ac_mag }}

// Ideal balun to create differential inputs around VCM, following the example pattern
// Primary: (d 0) = (vin_src 0); Center tap = vcm; Secondary: outputs to in_p_drv/in_n_drv
subckt ideal_balun d c p n
    K0 (d 0 p c) transformer n1=2
    K1 (d 0 c n) transformer n1=2
ends ideal_balun

IBAL_IN (vin_src vcm in_p_drv in_n_drv) ideal_balun

// Source impedance split across each leg
RINP (IN_P in_p_drv) resistor r={{ env.source_ohms/2 }}
RINN (IN_N in_n_drv) resistor r={{ env.source_ohms/2 }}

// Output load on single-ended OUT
CLOAD (OUT vss) capacitor c={{ env.cload_f }}
{% if env.rload_ohms is defined and env.rload_ohms > 0 %}
RLOAD (OUT vss) resistor r={{ env.rload_ohms }}
{% endif %}

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
save IN_P IN_N OUT

