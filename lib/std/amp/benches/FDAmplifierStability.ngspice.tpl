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

* Bias Non-Inverting Inputs
VINP_BIAS IN_P 0 DC {{ vcm }}

* Feedback Loop: OUT_P -> IN_N
* Unity gain non-inverting config
L_FB OUT_P IN_N 1T
C_INJ IN_N_src IN_N 1T
V_INJ IN_N_src 0 AC 1

{{ for load in harness.loads }}
C{{ load.net }}_load {{ load.net }} 0 {{ load.c }}
{{ end }}

* Differential Load
CLOADP OUT_P 0 {{ env.cload_f }}
CLOADN OUT_N 0 {{ env.cload_f }}
{{ if env.rload_ohms && env.rload_ohms > 0 }}
RLOADP OUT_P 0 {{ env.rload_ohms }}
RLOADN OUT_N 0 {{ env.rload_ohms }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
op
ac dec 100 {{ stb_start_hz }} {{ stb_stop_hz }}

* Approximate loop-gain measurement using injection at IN_N.
* Loop gain T ~= -V(OUT_P) / V(IN_N)
let loop = -v(OUT_P)/v(IN_N)
meas ac ugf when db(loop)=0 cross=1
meas ac pm_raw find ph(loop) at=ugf
let pm = 180 + pm_raw

echo "RESULT: PhaseMargin = " $&pm " deg"
quit
.endc
.end
