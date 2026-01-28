* {{ circuit_name }}_{{ bench_name }} - Generated from ACIR EL
.title {{ circuit_name }}_{{ bench_name }}

{{ if generic_models }}
* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05
.model level1_nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model level1_pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05
{{ end }}

{{ for inc in includes_with_section }}
{{ if section }}.lib "{{ inc }}" {{ section }}{{ else }}.include "{{ inc }}"{{ end }}
{{ end }}
{{ for inc in includes_without_section }}
.include "{{ inc }}"
{{ end }}

* Harness supplies and biases
{{ supply_elements }}

* Common-mode bias
VCM_SRC vcm 0 DC {{ vcm }}

* Differential stimulus
* PULSE(V1 V2 TD TR TF PW PER)
{{ if bench_config.tran_stop_s }}
{{ if bench_config.tran_maxstep_s }}
VIN_SRC vin_src 0 PULSE({{ -vcm }} {{ vcm }} 0 {{ bench_config.tran_maxstep_s }} {{ bench_config.tran_maxstep_s }} {{ bench_config.tran_stop_s }} {{ bench_config.tran_stop_s }})
{{ else }}
VIN_SRC vin_src 0 PULSE({{ -vcm }} {{ vcm }} 0 1n 1n {{ bench_config.tran_stop_s }} {{ bench_config.tran_stop_s }})
{{ end }}
{{ else }}
VIN_SRC vin_src 0 PULSE({{ -vcm }} {{ vcm }} 0 1n 1n 0.5u 1u)
{{ end }}

* Input Drive via dependent sources (mimicking balun behavior)
E_IN_P IN_P_drv 0 VOL = 'v(vcm) + 0.5 * v(vin_src)'
E_IN_N IN_N_drv 0 VOL = 'v(vcm) - 0.5 * v(vin_src)'

RINP IN_P IN_P_drv {{ env.source_ohms/2 }}
RINN IN_N IN_N_drv {{ env.source_ohms/2 }}

* Output loads
{{ load_elements }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
op
{{ if bench_config.tran_stop_s }}
{{ if bench_config.tran_maxstep_s }}
tran {{ bench_config.tran_maxstep_s }} {{ bench_config.tran_stop_s }}
{{ else }}
tran 1n {{ bench_config.tran_stop_s }}
{{ end }}
{{ else }}
tran 1n 1u
{{ end }}

meas tran vout_p_max MAX v(OUT_P)
meas tran vout_p_min MIN v(OUT_P)
meas tran vout_n_max MAX v(OUT_N)
meas tran vout_n_min MIN v(OUT_N)

let vout_diff = v(OUT_P) - v(OUT_N)
meas tran vout_diff_max MAX vout_diff
meas tran vout_diff_min MIN vout_diff

let swing_p = vout_p_max - vout_p_min
let swing_n = vout_n_max - vout_n_min
let swing = swing_p
if swing_n < swing_p
  let swing = swing_n
end

let diff_swing = vout_diff_max - vout_diff_min

echo "RESULT: DifferentialOutputSwing = " $&diff_swing " Vpp"
echo "RESULT: SingleEndedOutputSwing = " $&swing " Vpp"
quit
.endc
.end
