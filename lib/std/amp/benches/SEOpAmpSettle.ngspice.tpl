* {{ circuit_name }}_{{ bench_name }} - Generated from ACIR EL
.title {{ circuit_name }}_{{ bench_name }}

{{ if generic_models }}
* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05
{{ end }}

.include "{{ design_file }}"

* Harness
{{ for supply in harness.supplies }}
V{{ supply.net }} {{ supply.net }} 0 DC {{ supply.value }}
{{ end }}

* Common-mode bias
VCM_SRC vcm 0 DC {{ vcm }}

* Step source (Pulse)
* PULSE(V1 V2 TD TR TF PW PER)
VIN_SRC vin_src 0 PULSE({{ vcm }} {{ vcm + step_amp_v }} {{ step_delay_s }} {{ step_rise_s }} {{ step_fall_s }} {{ step_width_s }} {{ step_period_s }})

* Input Drive via dependent sources (mimicking balun behavior)
* V(IN_P_drv) = vcm + 0.5 * vin_src
* V(IN_N_drv) = vcm - 0.5 * vin_src
* Note: If vin_src is around vcm, this adds significant DC offset (1.5*vcm).
* Validated against Spectre template behavior.
E_IN_P IN_P_drv 0 VOL = 'v(vcm) + 0.5 * v(vin_src)'
E_IN_N IN_N_drv 0 VOL = 'v(vcm) - 0.5 * v(vin_src)'

RINP IN_P IN_P_drv {{ env.source_ohms/2 }}
RINN IN_N IN_N_drv {{ env.source_ohms/2 }}

{{ for load in harness.loads }}
C{{ load.net }}_load {{ load.net }} 0 {{ load.c }}
{{ end }}

CLOAD OUT 0 {{ env.cload_f }}
{{ if env.rload_ohms && env.rload_ohms > 0 }}
RLOAD OUT 0 {{ env.rload_ohms }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
op
* Transient analysis
* tran step stop
tran {{ tran_maxstep_s }} {{ tran_stop_s }}

* Save all results for post-processing
write {{ circuit_name }}_{{ bench_name }}.raw

quit
.endc
.end
