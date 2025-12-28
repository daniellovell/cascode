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

* Bias Non-Inverting Input
VIN_BIAS IN_P 0 DC {{ vcm }}

* Feedback Loop: OUT -> IN_N
* Using L-C injection method for stability analysis
* L_FB ensures DC feedback (closed loop)
* C_INJ injects AC signal (breaking loop for AC)
* Note: This mimics 'stb' analysis qualitatively.

L_FB OUT IN_N 1T
C_INJ IN_N_src IN_N 1T
V_INJ IN_N_src 0 AC 1

{{ for load in harness.loads }}
{{ for c in load.cs }}C{{ load.net }}_load{{ if load.cs.size > 1 }}_{{ for.index }}{{ end }} {{ load.net }} 0 {{ c }}
{{ end }}{{ for r in load.rs }}R{{ load.net }}_load{{ if load.rs.size > 1 }}_{{ for.index }}{{ end }} {{ load.net }} 0 {{ r }}
{{ end }}{{ end }}

CLOAD OUT 0 {{ env.cload_f }}
{{ if env.rload_ohms && env.rload_ohms > 0 }}
RLOAD OUT 0 {{ env.rload_ohms }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
op
ac dec 100 {{ stb_start_hz }} {{ stb_stop_hz }}

* Stability Measurements
* Approximate loop gain T ~= -V(OUT) / V(IN_N)
let loop = -v(OUT)/v(IN_N)
meas ac ugf when db(loop)=0 cross=1
meas ac pm_raw find ph(loop) at=ugf
let pm = 180 + pm_raw

echo "RESULT: PhaseMargin = " $&pm " deg"

quit
.endc
.end
