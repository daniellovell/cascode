// cascode gm_id_pmos.v1 (Spectre)
simulator lang=spectre
global 0
parameters L={{ params.l_m }} VSG={{ params.start }}
temp = {{ spec.temperature_c }}

// includes
{% for inc in includes %}
{% if section %}include "{{ inc }}" section={{ section }}{% else %}include "{{ inc }}"{% endif %}
{% endfor %}

// supply and sources
VDD (vdd 0) vsource dc={{ params.vdd }}
// Source node tied to VDD
VSRC (s vdd) vsource dc=0
// Gate bias: enforce V(S) - V(G) = VSG
VSGSRC (s g) vsource dc=VSG
// Drain bias: V(S) - V(D) = VSD
{% if params.drain_bias_mode == 'scaled' %}
VSD (s d) vsource dc={{ params.drain_alpha }}*VSG
{% else %}
VSD (s d) vsource dc={{ params.vsd }}
{% endif %}
// Body tied to source
VBS (b s) vsource dc=0

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

// Sweep VSG
dc sweep param=VSG start={{ params.start }} stop={{ params.stop }} step={{ params.step }}

saveOptions options save=allpub
save v(g) v(d) v(s) I(VSD) gm({{ params.inst_name }}) gds({{ params.inst_name }}) cgs({{ params.inst_name }}) cgd({{ params.inst_name }}) vth({{ params.inst_name }})

// Write CSV: vsg, vd, id (source->drain), gm (positive), gds, cgs, cgd, vth (positive)
printfile("{{ spec.results_csv }}", V(s)-V(g) V(d) -I(VSD) -gm({{ params.inst_name }}) gds({{ params.inst_name }}) cgs({{ params.inst_name }}) cgd({{ params.inst_name }}) -vth({{ params.inst_name }}) )
