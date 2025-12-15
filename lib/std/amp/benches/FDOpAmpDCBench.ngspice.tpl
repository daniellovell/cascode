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

{{ if sweep.InputDCCommonMode }}
* Common-mode bias sweep for differential inputs
VIN_CM IN_P 0 DC {{ sweep.InputDCCommonMode.start }}
EIN_N IN_N 0 IN_P 0 1
{{ else }}
* Common-mode bias (single point) for differential inputs
VIN_CM IN_P 0 DC {{ vcm }}
EIN_N IN_N 0 IN_P 0 1
{{ end }}

* Differential output loads
{{ for load in harness.loads }}
C{{ load.net }}_P_load {{ load.net }}_P 0 {{ load.c }}
C{{ load.net }}_N_load {{ load.net }}_N 0 {{ load.c }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
{{ if sweep.InputDCCommonMode }}
* InputDCCommonMode sweep analysis
dc VIN_CM {{ sweep.InputDCCommonMode.start }} {{ sweep.InputDCCommonMode.stop }} {{ sweep.InputDCCommonMode.step }}

* Output common-mode calculation
let out_cm = (v(OUT_P) + v(OUT_N)) / 2

* Measurements across sweep
meas dc out_cm_min min out_cm
meas dc out_cm_max max out_cm
{{ for supply in harness.supplies }}
let pwr_{{ supply.net }} = v({{ supply.net }})*(-i(V{{ supply.net }}))
{{ end }}

* Results output
echo "RESULT: OutputDCCommonMode_min = " $&out_cm_min " V"
echo "RESULT: OutputDCCommonMode_max = " $&out_cm_max " V"
{{ for supply in harness.supplies }}
echo "RESULT: QuiescentPower = " $&pwr_{{ supply.net }} " W"
{{ end }}
{{ else }}
* Single operating point
op

* Output common-mode calculation
let out_cm = (v(OUT_P) + v(OUT_N)) / 2

* Measurements
meas dc out_cm_val find out_cm
{{ for supply in harness.supplies }}
let pwr_{{ supply.net }} = v({{ supply.net }})*(-i(V{{ supply.net }}))
{{ end }}

* Results output
echo "RESULT: OutputDCCommonMode = " $&out_cm_val " V"
{{ for supply in harness.supplies }}
echo "RESULT: QuiescentPower = " $&pwr_{{ supply.net }} " W"
{{ end }}
{{ end }}

quit
.endc
.end
