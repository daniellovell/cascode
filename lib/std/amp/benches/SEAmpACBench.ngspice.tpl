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
* Single-ended input: DC bias with AC stimulus
VIN IN 0 DC {{ bias_v }} AC 1
{{ for load in harness.loads }}
C{{ load.net }}_load {{ load.net }} 0 {{ load.c }}
{{ end }}

* DUT
XDUT {{ port_list }} {{ circuit_name }}

.control
op
ac dec 100 1 10G

* Measurements
meas ac gain_dc find vdb({{ out_node }}) at=1
meas ac gbw when vdb({{ out_node }})=0 cross=1

* Results output
echo "RESULT: PassbandGain = " gain_dc " dB"
echo "RESULT: GainBandwidth = " gbw " Hz"

quit
.endc
.end

