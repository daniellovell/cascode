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

{{ if sweep.InputDCBias }}
* Single-ended input: DC bias sweep
VIN IN 0 DC {{ sweep.InputDCBias.start }}
{{ else }}
* Single-ended input: DC bias (single point)
VIN IN 0 DC {{ bias_v }}
{{ end }}

* Output load
{{ for load in harness.loads }}
C{{ load.net }}_load {{ load.net }} 0 {{ load.c }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
{{ if sweep.InputDCBias }}
* InputDCBias sweep analysis
dc VIN {{ sweep.InputDCBias.start }} {{ sweep.InputDCBias.stop }} {{ sweep.InputDCBias.step }}

* Measurements across sweep
meas dc out_dc_min min v({{ out_node }})
meas dc out_dc_max max v({{ out_node }})
{{ for supply in harness.supplies }}
let pwr_{{ supply.net }} = v({{ supply.net }})*(-i(V{{ supply.net }}))
{{ end }}

* Results output
echo "RESULT: OutputDCBias_min = " $&out_dc_min " V"
echo "RESULT: OutputDCBias_max = " $&out_dc_max " V"
{{ for supply in harness.supplies }}
echo "RESULT: QuiescentPower = " $&pwr_{{ supply.net }} " W"
{{ end }}
{{ else }}
* Single operating point
op

* Measurements
meas dc out_dc find v({{ out_node }})
{{ for supply in harness.supplies }}
let pwr_{{ supply.net }} = v({{ supply.net }})*(-i(V{{ supply.net }}))
{{ end }}

* Results output
echo "RESULT: OutputDCBias = " $&out_dc " V"
{{ for supply in harness.supplies }}
echo "RESULT: QuiescentPower = " $&pwr_{{ supply.net }} " W"
{{ end }}
{{ end }}

quit
.endc
.end
