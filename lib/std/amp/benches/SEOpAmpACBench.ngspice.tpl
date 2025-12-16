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
{{ if sweep.InputDCCommonMode }}
VCM_SRC vcm 0 DC {{ sweep.InputDCCommonMode.start }}
{{ else }}
VCM_SRC vcm 0 DC {{ vcm }}
{{ end }}

* Differential Drive
* Inputs driven differentially around VCM
* IN_P = VCM + 0.5 * AC
* IN_N = VCM - 0.5 * AC
VAC_P IN_P_drv vcm AC 0.5 0
VAC_N IN_N_drv vcm AC 0.5 180

RINP IN_P IN_P_drv {{ env.source_ohms/2 }}
RINN IN_N IN_N_drv {{ env.source_ohms/2 }}

{{ for load in harness.loads }}
C{{ load.net }}_load {{ load.net }} 0 {{ load.c }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
{{ if sweep.InputDCCommonMode }}
* InputDCCommonMode sweep: iterate over common-mode points with AC at each
let cm_start = {{ sweep.InputDCCommonMode.start }}
let cm_stop = {{ sweep.InputDCCommonMode.stop }}
let cm_step = {{ sweep.InputDCCommonMode.step }}
let gbw_min = 1e12
let gain_min = 1000
let pm_min = 360
let point_index = 0

let cm_val = cm_start
while cm_val <= cm_stop
  alter VCM_SRC DC=$&cm_val
  op
  ac dec 100 1 10G

  meas ac gain_pt find vdb({{ out_node }}) at=1
  meas ac gbw_pt when vdb({{ out_node }})=0 cross=1
  meas ac pm_raw_pt find vp({{ out_node }}) at=gbw_pt
  let pm_pt = 180 + pm_raw_pt

  echo CASCODE_POINT point_index=$&point_index InputDCCommonMode_V=$&cm_val PassbandGain_dB=$&gain_pt GainBandwidth_Hz=$&gbw_pt PhaseMargin_deg=$&pm_pt

  if gain_pt < gain_min
    let gain_min = gain_pt
  end
  if gbw_pt < gbw_min
    let gbw_min = gbw_pt
  end
  if pm_pt < pm_min
    let pm_min = pm_pt
  end

  let point_index = point_index + 1
  let cm_val = cm_val + cm_step
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

echo CASCODE_POINT point_index=0 PassbandGain_dB=$&gain_dc GainBandwidth_Hz=$&gbw PhaseMargin_deg=$&pm

* Results output
echo "RESULT: PassbandGain = " $&gain_dc " dB"
echo "RESULT: GainBandwidth = " $&gbw " Hz"
echo "RESULT: PhaseMargin = " $&pm " deg"
{{ end }}

quit
.endc
.end
