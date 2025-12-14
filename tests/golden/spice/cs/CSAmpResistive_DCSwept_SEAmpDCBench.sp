* CSAmpResistive_DCSwept_SEAmpDCBench - Generated from ACIR EL
.title CSAmpResistive_DCSwept_SEAmpDCBench


* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05


.include "CSAmpResistive_DCSwept.sp"

* Harness

VVDD VDD 0 DC 1.8V



* Input DC bias sweep
VIN IN 0 DC 0.3


* Output load

COUT_load OUT 0 1p


* DUT
XDUT IN OUT VDD GND CSAmpResistive_DCSwept

.control

* DC sweep analysis
dc VIN 0.3 1.5 0.1

* Measurements across sweep
meas dc out_dc_min min v(OUT)
meas dc out_dc_max max v(OUT)

meas dc pwr_VDD param='v(VDD)*(-i(VVDD))'


* Results output
echo "RESULT: OutputDCBias_min = " out_dc_min " V"
echo "RESULT: OutputDCBias_max = " out_dc_max " V"

echo "RESULT: QuiescentPower = " pwr_VDD " W"



quit
.endc
.end
