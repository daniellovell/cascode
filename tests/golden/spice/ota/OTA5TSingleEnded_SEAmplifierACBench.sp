* OTA5TSingleEnded_SEAmplifierACBench - Generated from ACIR EL
.title OTA5TSingleEnded_SEAmplifierACBench

.include "OTA5TSingleEnded.sp"

* Harness
VVDD VDD 0 DC 1.8V
COUT_load OUT 0 1p

* DUT
XDUT IN_P IN_N OUT VTAIL VDD GND OTA5TSingleEnded

.control
op
ac dec 100 1 10G
quit
.endc
.end
