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
* Differential input: common-mode bias (swept) with AC on positive input
VIN_P IN_P 0 DC {{ sweep.InputDCCommonMode.start }} AC 1
VIN_N IN_N 0 DC {{ sweep.InputDCCommonMode.start }}
{{ else }}
* Differential input: common-mode bias with AC on positive input
VIN_P IN_P 0 DC {{ vcm }} AC 1
VIN_N IN_N 0 DC {{ vcm }}
{{ end }}
{{ for load in harness.loads }}
C{{ load.net }}_load {{ load.net }} 0 {{ load.c }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
{{ if sweep.InputDCCommonMode }}
* ICMR sweep: iterate over common-mode points with AC at each
let cm_start = {{ sweep.InputDCCommonMode.start }}
let cm_stop = {{ sweep.InputDCCommonMode.stop }}
let cm_step = {{ sweep.InputDCCommonMode.step }}
let gbw_min = 1e12
let gain_min = 1000
let pm_min = 360

foreach cm_val $&cm_start $&cm_stop $&cm_step
  alter VIN_P DC=$cm_val
  alter VIN_N DC=$cm_val
  op
  ac dec 100 1 10G
  
  meas ac gain_pt find vdb({{ out_node }}) at=1
  meas ac gbw_pt when vdb({{ out_node }})=0 cross=1
  meas ac pm_raw_pt find vp({{ out_node }}) at=gbw_pt
  let pm_pt = 180 + pm_raw_pt
  
  if gain_pt < gain_min
    let gain_min = gain_pt
  end
  if gbw_pt < gbw_min
    let gbw_min = gbw_pt
  end
  if pm_pt < pm_min
    let pm_min = pm_pt
  end
end

* Results output (worst-case across sweep)
echo "RESULT: PassbandGain = " $&gain_min " dB"
echo "RESULT: GainBandwidth = " $&gbw_min " Hz"
echo "RESULT: PhaseMargin = " $&pm_min " deg"
{{ else }}
op
ac dec 100 1 10G

* Measurements
meas ac gain_dc find vdb({{ out_node }}) at=1
meas ac gbw when vdb({{ out_node }})=0 cross=1
meas ac pm_raw find vp({{ out_node }}) at=gbw
let pm = 180 + pm_raw

* Results output
echo "RESULT: PassbandGain = " gain_dc " dB"
echo "RESULT: GainBandwidth = " gbw " Hz"
echo "RESULT: PhaseMargin = " pm " deg"
{{ end }}

quit
.endc
.end

