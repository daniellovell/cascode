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

* Differential Drive
* Inputs driven differentially around VCM
* IN_P = VCM + 0.5 * AC
* IN_N = VCM - 0.5 * AC
* Note: This assumes simplified differential drive without the complex
* balun subcircuit used in the Spectre template, which also offset DC.
* We strictly enforce VCM common mode here.

VAC_P IN_P_drv vcm AC 0.5 0
VAC_N IN_N_drv vcm AC 0.5 180

RINP IN_P IN_P_drv {{ env.source_ohms/2 }}
RINN IN_N IN_N_drv {{ env.source_ohms/2 }}

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
ac dec 100 {{ ac_start_hz }} {{ ac_stop_hz }}

* Measurements
* Gain = V(OUT_P, OUT_N) / 1
meas ac gain_dc find vdb(OUT_P, OUT_N) at=1
meas ac gbw when vdb(OUT_P, OUT_N)=0 cross=1
meas ac pm_raw find vp(OUT_P, OUT_N) at=gbw
let pm = 180 + pm_raw

let gain_3db = gain_dc - 3
meas ac f3db_1 when vdb(OUT_P, OUT_N)=gain_3db cross=1
meas ac f3db_2 when vdb(OUT_P, OUT_N)=gain_3db cross=2
let hp_bw = f3db_1
let lp_bw = f3db_2
if lp_bw <= 0
  let lp_bw = f3db_1
  let hp_bw = 0
end
let bp_bw = lp_bw - hp_bw
if bp_bw < 0
  let bp_bw = -bp_bw
end

echo "RESULT: PassbandGain = " $&gain_dc " dB"
echo "RESULT: GainBandwidth = " $&gbw " Hz"
echo "RESULT: PhaseMargin = " $&pm " deg"
echo "RESULT: LowpassBandwidth = " $&lp_bw " Hz"
echo "RESULT: HighpassBandwidth = " $&hp_bw " Hz"
echo "RESULT: BandpassBandwidth = " $&bp_bw " Hz"

quit
.endc
.end
