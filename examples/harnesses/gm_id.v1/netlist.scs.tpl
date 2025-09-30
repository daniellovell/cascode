// cascode gm_id.v1 (Spectre)
simulator lang=spectre
global 0
parameters L={{ params.l_m }} VGS={{ params.start }}

// includes
{% for inc in includes_with_section %}
{% if section %}include "{{ inc }}" section={{ section }}{% else %}include "{{ inc }}"{% endif %}
{% endfor %}
{% for inc in includes_without_section %}
include "{{ inc }}"
{% endfor %}

// sources
// Tie source and bulk to a defined reference (VSS=0)
VSS (s 0) vsource dc=0
// Body bias relative to source (V(b)-V(s)=VSB)
VBS (b s) vsource dc={{ params.vsb }}
// Gate sweep source uses parameter VGS
VGS (g 0) vsource dc=VGS
// Drain bias: either fixed VDS or scaled alpha*VGS
{% if params.drain_bias_mode == 'scaled' %}
VDR (d 0) vsource dc={{ params.drain_alpha }}*VGS
{% else %}
VDR (d 0) vsource dc={{ params.vds }}
{% endif %}

// DUT
{{ params.inst_name }} (d g s b) {{ spec.model_name }} w={{ params.w_m }} l=L m={{ params.mult }} nf={{ params.nf }} \
        as={{ params.as }} ad={{ params.ad }} ps={{ params.ps }} \
        pd={{ params.pd }} nrd={{ params.nrd }} nrs={{ params.nrs }} \
        sa={{ params.sa }} sb={{ params.sb }} sd={{ params.sd }} \
        sca={{ params.sca }} scb={{ params.scb }} scc={{ params.scc }}

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

// Sweep VGS with per-step operating point export
vgsSweep sweep param=VGS start={{ params.start }} stop={{ params.stop }} step={{ params.step }} {
dc1 dc
opinfo info what=oppoint where=file file="oppoint.%A"
elinfo info what=inst where=file file="elem.%A"
}

saveOptions options save=allpub
save g
save s
save d
save {{ params.inst_name }}:d
save {{ params.inst_name }}:oppoint
