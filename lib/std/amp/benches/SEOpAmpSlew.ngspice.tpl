* {{ circuit_name }}_{{ bench_name }} - Generated from ACIR EL
.title {{ circuit_name }}_{{ bench_name }}

{{ if generic_models }}
* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05
{{ end }}

{{ for inc in includes_with_section }}
{{ if section }}.lib "{{ inc }}" {{ section }}{{ else }}.include "{{ inc }}"{{ end }}
{{ end }}
{{ for inc in includes_without_section }}
.include "{{ inc }}"
{{ end }}

* Harness
{{ for supply in harness.supplies }}
V{{ supply.net }} {{ supply.net }} 0 DC {{ supply.value }}
{{ end }}

* Common-mode bias
VCM_SRC vcm 0 DC {{ vcm }}

* Slew Pulse
* PULSE(V1 V2 TD TR TF PW PER)
VIN_SRC vin_src 0 PULSE({{ vcm - 0.5*slew_amp_v }} {{ vcm + 0.5*slew_amp_v }} 0 {{ slew_rise_s }} {{ slew_fall_s }} {{ slew_width_s }} {{ slew_period_s }})

* Input Drive via dependent sources (mimicking balun behavior)
E_IN_P IN_P_drv 0 VOL = 'v(vcm) + 0.5 * v(vin_src)'
E_IN_N IN_N_drv 0 VOL = 'v(vcm) - 0.5 * v(vin_src)'

RINP IN_P IN_P_drv {{ env.source_ohms/2 }}
RINN IN_N IN_N_drv {{ env.source_ohms/2 }}

{{ for load in harness.loads }}
{{ if load.c }}C{{ load.net }}_load {{ load.net }} 0 {{ load.c }}{{ end }}
{{ if load.r }}R{{ load.net }}_load {{ load.net }} 0 {{ load.r }}{{ end }}
{{ end }}

CLOAD OUT 0 {{ env.cload_f }}
{{ if env.rload_ohms && env.rload_ohms > 0 }}
RLOAD OUT 0 {{ env.rload_ohms }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
op
tran {{ tran_maxstep_s }} {{ tran_stop_s }}

write {{ circuit_name }}_{{ bench_name }}.raw

quit
.endc
.end
