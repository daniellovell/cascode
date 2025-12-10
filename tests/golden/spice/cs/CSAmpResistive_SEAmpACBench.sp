* CSAmpResistive_SEAmpACBench - Generated from ACIR EL
.title CSAmpResistive_SEAmpACBench


* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05


.include "CSAmpResistive.sp"

* Harness

VVDD VDD 0 DC 1.8V

* Single-ended input: DC bias with AC stimulus
VIN IN 0 DC 0.9 AC 1

COUT_load OUT 0 1p


* DUT
XDUT IN OUT VDD 0 CSAmpResistive

.control
op
ac dec 100 1 10G

* Measurements
meas ac gain_dc find vdb(OUT) at=1
meas ac gbw when vdb(OUT)=0 cross=1

* Results output
echo "RESULT: PassbandGain = " gain_dc " dB"
echo "RESULT: GainBandwidth = " gbw " Hz"

quit
.endc
.end

