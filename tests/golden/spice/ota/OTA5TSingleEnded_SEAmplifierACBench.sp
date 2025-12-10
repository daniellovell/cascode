* OTA5TSingleEnded_SEAmplifierACBench - Generated from ACIR EL
.title OTA5TSingleEnded_SEAmplifierACBench

* Generic MOSFET models for simulation
.model nmos nmos level=1 vto=0.5 kp=120u gamma=0.4 phi=0.65 lambda=0.04
.model pmos pmos level=1 vto=-0.5 kp=40u gamma=0.4 phi=0.65 lambda=0.05

.include "OTA5TSingleEnded.sp"

* Harness: supply, bias, input stimulus, and load
VVDD VDD 0 DC 1.8
VTAIL VTAIL 0 DC 0.6
VIN_P IN_P 0 DC 0.9 AC 1
VIN_N IN_N 0 DC 0.9
COUT_load OUT 0 1p

* DUT (GND connected to node 0)
XDUT IN_P IN_N OUT VTAIL VDD 0 OTA5TSingleEnded

.control
op
ac dec 100 1 10G
quit
.endc
.end
