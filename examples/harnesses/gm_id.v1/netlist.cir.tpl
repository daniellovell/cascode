* cascode gm_id.v1 (ngspice)
.title gm_id
.option numdgt=7
.temp {{ spec.temperature_c }}

.param L={{ params.l_m }} VGS={{ params.start }}

{{ for inc in includes }}
{{ if section }}.lib "{{ inc }}" {{ section }}{{ else }}.include "{{ inc }}"{{ end }}
{{ end }}

* sources
VGS g 0 VGS
{{ if params.drain_bias_mode == 'scaled' }}
VDR d 0 {{ params.drain_alpha }}*VGS
{{ else }}
VDR d 0 {{ params.vds }}
{{ end }}
VBS b s {{ params.vsb }}

* DUT (geometry params are supported where ngspice model accepts them)
{{ params.inst_name }} d g s b {{ spec.model_name }} W={{ params.w_m }} L=L m={{ params.mult }} nf={{ params.nf }}

.control
set filetype=ascii
dc VGS {{ params.start }} {{ params.stop }} {{ params.step }}
* Export a minimal, cross-backend set: vgs, vd, id; id is -i(VDR)
let id = -i(VDR)
wrdata {{ spec.results_csv }} v(g) v(d) id
quit
.endc
.end
