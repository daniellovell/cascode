* {{ circuit_name }}_{{ bench_name }} - Generated from Cascode EL
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

* Harness
{{ for supply in harness.supplies }}
V{{ supply.net }} {{ supply.net }} 0 DC {{ supply.value }}
{{ end }}

{{ if sweep.InputDCBias }}
* Single-ended input: DC bias sweep
VIN IN 0 DC {{ sweep.InputDCBias.Start }}
{{ else }}
* Single-ended input: DC bias (single point)
VIN IN 0 DC {{ bias_v }}
{{ end }}

* Output load
{{ load_elements }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
{{ if sweep.InputDCBias }}
* InputDCBias sweep analysis (looped for per-point tracing)
let bias_start = {{ sweep.InputDCBias.Start }}
let bias_stop = {{ sweep.InputDCBias.Stop }}
let bias_step = {{ sweep.InputDCBias.Step }}

let out_dc_min = 1e12
let out_dc_max = -1e12
let pwr_max = -1
let point_index = 0

let bias_val = bias_start
while bias_val <= bias_stop
  alter VIN DC=$&bias_val
  op

  let out_dc = v({{ out_node }})
  let pwr_total = 0
  {{ for supply in harness.supplies }}
  let pwr_src = v({{ supply.net }})*(-i(V{{ supply.net }}))
  let pwr_total = pwr_total + pwr_src
  {{ end }}

  echo CASCODE_POINT point_index=$&point_index InputDCBias_V=$&bias_val OutputDCBias_V=$&out_dc QuiescentPower_W=$&pwr_total

  if out_dc < out_dc_min
    let out_dc_min = out_dc
  end
  if out_dc > out_dc_max
    let out_dc_max = out_dc
  end
  if pwr_total > pwr_max
    let pwr_max = pwr_total
  end

  let point_index = point_index + 1
  let bias_val = bias_val + bias_step
end

* Results output (reduced across sweep)
echo "RESULT: OutputDCBias_min = " $&out_dc_min " V"
echo "RESULT: OutputDCBias_max = " $&out_dc_max " V"
echo "RESULT: QuiescentPower = " $&pwr_max " W"
{{ else }}
* Single operating point
op

* Measurements
let out_dc = v({{ out_node }})
let pwr_total = 0
{{ for supply in harness.supplies }}
let pwr_src = v({{ supply.net }})*(-i(V{{ supply.net }}))
let pwr_total = pwr_total + pwr_src
{{ end }}

echo CASCODE_POINT point_index=0 OutputDCBias_V=$&out_dc QuiescentPower_W=$&pwr_total

* Results output
echo "RESULT: OutputDCBias = " $&out_dc " V"
echo "RESULT: QuiescentPower = " $&pwr_total " W"
{{ end }}

quit
.endc
.end
