* CommonSourceAmp_SEAmpACBench - Generated from ACIR EL
.title CommonSourceAmp_SEAmpACBench

* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05

.include "CommonSourceAmp.sp"

* Harness: supply, bias, input stimulus, and load
VVDD VDD 0 DC 1.8
VBIAS VBIAS 0 DC 0.7
VIN IN 0 DC 0.9 AC 1
COUT_load OUT 0 1p

* DUT (GND connected to node 0)
XDUT IN OUT VBIAS VDD 0 CommonSourceAmp

.control
op
ac dec 100 1 10G
quit
.endc
.end

