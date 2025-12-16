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
VIN_CM IN_P 0 DC {{ sweep.InputDCCommonMode.Start }}
EIN_N IN_N 0 IN_P 0 1
{{ else }}
* Common-mode bias (single point) for differential inputs
VIN_CM IN_P 0 DC {{ vcm }}
EIN_N IN_N 0 IN_P 0 1
{{ end }}

* Differential output loads (split capacitance equally)
{{ for load in harness.loads }}
C{{ load.net }}_P_load {{ load.net }}_P 0 {{ env.cload_f/2 }}
C{{ load.net }}_N_load {{ load.net }}_N 0 {{ env.cload_f/2 }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
{{ if sweep.InputDCCommonMode }}
* InputDCCommonMode sweep analysis (looped for per-point tracing)
let cm_start = {{ sweep.InputDCCommonMode.Start }}
let cm_stop = {{ sweep.InputDCCommonMode.Stop }}
let cm_step = {{ sweep.InputDCCommonMode.Step }}

let out_cm_min = 1e12
let out_cm_max = -1e12
let pwr_max = -1
let point_index = 0

let cm_val = cm_start
while cm_val <= cm_stop
  alter VIN_CM DC=$&cm_val
  op

  let out_cm_val = (v(OUT_P) + v(OUT_N)) / 2
  let pwr_total = 0
  {{ for supply in harness.supplies }}
  let pwr_src = v({{ supply.net }})*(-i(V{{ supply.net }}))
  let pwr_total = pwr_total + pwr_src
  {{ end }}

  echo CASCODE_POINT point_index=$&point_index InputDCCommonMode_V=$&cm_val OutputDCCommonMode_V=$&out_cm_val QuiescentPower_W=$&pwr_total

  if out_cm_val < out_cm_min
    let out_cm_min = out_cm_val
  end
  if out_cm_val > out_cm_max
    let out_cm_max = out_cm_val
  end
  if pwr_total > pwr_max
    let pwr_max = pwr_total
  end

  let point_index = point_index + 1
  let cm_val = cm_val + cm_step
end

* Results output (reduced across sweep)
echo "RESULT: OutputDCCommonMode_min = " $&out_cm_min " V"
echo "RESULT: OutputDCCommonMode_max = " $&out_cm_max " V"
echo "RESULT: QuiescentPower = " $&pwr_max " W"
{{ else }}
* Single operating point
op

* Measurements
let out_cm_val = (v(OUT_P) + v(OUT_N)) / 2
let pwr_total = 0
{{ for supply in harness.supplies }}
let pwr_src = v({{ supply.net }})*(-i(V{{ supply.net }}))
let pwr_total = pwr_total + pwr_src
{{ end }}

echo CASCODE_POINT point_index=0 OutputDCCommonMode_V=$&out_cm_val QuiescentPower_W=$&pwr_total

* Results output
echo "RESULT: OutputDCCommonMode = " $&out_cm_val " V"
echo "RESULT: QuiescentPower = " $&pwr_total " W"
{{ end }}

quit
.endc
.end
