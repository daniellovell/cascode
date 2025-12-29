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

* Bias Non-Inverting Inputs
VINP_BIAS IN_P 0 DC {{ vcm }}

* Feedback Loop: OUT_P -> IN_N
* Unity gain non-inverting config
L_FB OUT_P IN_N 1T
C_INJ IN_N_src IN_N 1T
V_INJ IN_N_src 0 AC 1

* Differential output loads
{{ for load in harness.loads }}
{{ if load.net | string.ends_with "_P" }}
{{ for c in load.cs }}C{{ load.net }}_load{{ if load.cs.size > 1 }}_{{ for.index }}{{ end }} {{ load.net }} 0 {{ c }}
{{ end }}{{ for r in load.rs }}R{{ load.net }}_load{{ if load.rs.size > 1 }}_{{ for.index }}{{ end }} {{ load.net }} 0 {{ r }}
{{ end }}{{ else if load.net | string.ends_with "_N" }}
{{ for c in load.cs }}C{{ load.net }}_load{{ if load.cs.size > 1 }}_{{ for.index }}{{ end }} {{ load.net }} 0 {{ c }}
{{ end }}{{ for r in load.rs }}R{{ load.net }}_load{{ if load.rs.size > 1 }}_{{ for.index }}{{ end }} {{ load.net }} 0 {{ r }}
{{ end }}{{ else }}
{{ for c in load.cs_half }}C{{ load.net }}_P_load{{ if load.cs_half.size > 1 }}_{{ for.index }}{{ end }} {{ load.net }}_P 0 {{ c }}
C{{ load.net }}_N_load{{ if load.cs_half.size > 1 }}_{{ for.index }}{{ end }} {{ load.net }}_N 0 {{ c }}
{{ end }}{{ for r in load.rs_half }}R{{ load.net }}_P_load{{ if load.rs_half.size > 1 }}_{{ for.index }}{{ end }} {{ load.net }}_P 0 {{ r }}
R{{ load.net }}_N_load{{ if load.rs_half.size > 1 }}_{{ for.index }}{{ end }} {{ load.net }}_N 0 {{ r }}
{{ end }}{{ end }}
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
