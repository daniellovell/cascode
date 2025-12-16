* cascode gm_id.v1 (Spectre, spice-mode)
simulator lang=spice
.temp {{ spec.temperature_c }}

* includes
{{ for inc in includes_with_section }}
{{ if section }}.lib "{{ inc }}" {{ section }}{{ else }}.include "{{ inc }}"{{ end }}
{{ end }}
{{ for inc in includes_without_section }}
.include "{{ inc }}"
{{ end }}

* sources
VSS s 0 0
VBS b s {{ params.vsb }}
VGS g 0 {{ params.start }}
{{ if params.drain_bias_mode == 'scaled' }}
VDR d 0 {{ params.drain_alpha }}*VGS
{{ else }}
VDR d 0 {{ params.vds }}
{{ end }}

* DUT
M1 d g s b {{ spec.model_name }} w={{ params.w_m }} l={{ params.l_m }} m={{ params.mult }}

.dc VGS {{ params.start }} {{ params.stop }} {{ params.step }}
.save all
.end
