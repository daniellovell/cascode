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
* Single-ended input: DC bias (swept) with AC stimulus
VIN IN 0 DC {{ sweep.InputDCBias.start }} AC 1
{{ else }}
* Single-ended input: DC bias with AC stimulus
VIN IN 0 DC {{ bias_v }} AC 1
{{ end }}
{{ for load in harness.loads }}
C{{ load.net }}_load {{ load.net }} 0 {{ load.c }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
{{ if sweep.InputDCBias }}
* InputDCBias sweep: iterate over bias points with AC at each
let bias_start = {{ sweep.InputDCBias.start }}
let bias_stop = {{ sweep.InputDCBias.stop }}
let bias_step = {{ sweep.InputDCBias.step }}
let num_points = floor((bias_stop - bias_start) / bias_step) + 1
let gbw_min = 1e12
let gain_min = 1000

let bias_val = bias_start
while bias_val <= bias_stop
  alter VIN DC=$&bias_val
  op
  ac dec 100 1 10G

  meas ac gain_pt find vdb({{ out_node }}) at=1
  meas ac gbw_pt when vdb({{ out_node }})=0 cross=1

  if gain_pt < gain_min
    let gain_min = gain_pt
  end
  if gbw_pt < gbw_min
    let gbw_min = gbw_pt
  end

  let bias_val = bias_val + bias_step
end

* Results output (worst-case across sweep)
echo "RESULT: PassbandGain = " $&gain_min " dB"
echo "RESULT: GainBandwidth = " $&gbw_min " Hz"
{{ else }}
op
ac dec 100 1 10G

* Measurements
meas ac gain_dc find vdb({{ out_node }}) at=1
meas ac gbw when vdb({{ out_node }})=0 cross=1

* Results output
echo "RESULT: PassbandGain = " $&gain_dc " dB"
echo "RESULT: GainBandwidth = " $&gbw " Hz"
{{ end }}

quit
.endc
.end
