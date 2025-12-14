* OTA5TSingleEnded_DCSwept_SEOpAmpDCBench - Generated from ACIR EL
.title OTA5TSingleEnded_DCSwept_SEOpAmpDCBench


* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05


.include "OTA5TSingleEnded_DCSwept.sp"

* Harness

VVDD VDD 0 DC 1.8V

VVTAIL VTAIL 0 DC 0.6V



* Common-mode bias sweep for differential inputs
VIN_P IN_P 0 DC 0.4
VIN_N IN_N 0 DC 0.4


* Output load

COUT_load OUT 0 1p


* DUT
XDUT IN_P IN_N OUT VTAIL VDD GND OTA5TSingleEnded_DCSwept

.control

* ICMR sweep analysis
dc VIN_P 0.4 1.4 0.1 VIN_N 0.4 1.4 0.1

* Measurements across sweep
meas dc out_dc_min min v(OUT)
meas dc out_dc_max max v(OUT)

meas dc pwr_VDD param='v(VDD)*(-i(VVDD))'

meas dc pwr_VTAIL param='v(VTAIL)*(-i(VVTAIL))'


* Results output
echo "RESULT: OutputDCBias_min = " out_dc_min " V"
echo "RESULT: OutputDCBias_max = " out_dc_max " V"

echo "RESULT: QuiescentPower = " pwr_VDD " W"

echo "RESULT: QuiescentPower = " pwr_VTAIL " W"



quit
.endc
.end
