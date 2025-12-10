* CSAmpResistive_SEAmpACBench - Generated from ACIR EL
.title CSAmpResistive_SEAmpACBench

* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04

.include "CSAmpResistive.sp"

* Harness: supply, input stimulus, and load
VVDD VDD 0 DC 1.8
VIN IN 0 DC 0.9 AC 1
COUT_load OUT 0 1p

* DUT (GND connected to node 0)
XDUT IN OUT VDD 0 CSAmpResistive

.control
op
ac dec 100 1 10G
quit
.endc
.end

