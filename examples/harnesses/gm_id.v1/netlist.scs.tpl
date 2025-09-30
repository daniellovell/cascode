// cascode gm_id.v1 (Spectre)
simulator lang=spectre
global 0
parameters L={{ params.l_m }} VGS={{ params.start }}
temp = {{ spec.temperature_c }}

// includes
{% for inc in includes %}
{% if section %}include "{{ inc }}" section={{ section }}{% else %}include "{{ inc }}"{% endif %}
{% endfor %}

// sources
// Gate sweep source uses parameter VGS
VGS (g 0) vsource dc=VGS
// Drain bias: either fixed VDS or scaled alpha*VGS
{% if params.drain_bias_mode == 'scaled' %}
VDR (d 0) vsource dc={{ params.drain_alpha }}*VGS
{% else %}
VDR (d 0) vsource dc={{ params.vds }}
{% endif %}
// Body bias
VBS (b s) vsource dc={{ params.vsb }}

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

// Sweep VGS
dc sweep param=VGS start={{ params.start }} stop={{ params.stop }} step={{ params.step }}

saveOptions options save=allpub
// Save minimal cross-backend set and Spectre extras
save v(g) v(d) I(VDR) gm({{ params.inst_name }}) gds({{ params.inst_name }}) cgs({{ params.inst_name }}) cgd({{ params.inst_name }}) vth({{ params.inst_name }})

// Write CSV: vgs, vd, id, gm, gds, cgs, cgd, vth
printfile("{{ spec.results_csv }}", v(g) v(d) I(VDR) gm({{ params.inst_name }}) gds({{ params.inst_name }}) cgs({{ params.inst_name }}) cgd({{ params.inst_name }}) vth({{ params.inst_name }}) )
