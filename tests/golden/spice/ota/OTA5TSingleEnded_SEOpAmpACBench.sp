* OTA5TSingleEnded_SEOpAmpACBench - Generated from ACIR EL
.title OTA5TSingleEnded_SEOpAmpACBench


* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05


.include "OTA5TSingleEnded.sp"

* Harness

VVDD VDD 0 DC 1.8V

* Differential input: common-mode bias with AC on positive input
VIN_P IN_P 0 DC 0.9 AC 1
VIN_N IN_N 0 DC 0.9

COUT_load OUT 0 1p


* DUT
XDUT IN_P IN_N OUT VTAIL VDD 0 OTA5TSingleEnded

.control
op
ac dec 100 1 10G

* Measurements
meas ac gain_dc find vdb(OUT) at=1
meas ac gbw when vdb(OUT)=0 cross=1
meas ac pm_raw find vp(OUT) at=gbw
let pm = 180 + pm_raw

* Results output
echo "RESULT: PassbandGain = " gain_dc " dB"
echo "RESULT: GainBandwidth = " gbw " Hz"
echo "RESULT: PhaseMargin = " pm " deg"

quit
.endc
.end

