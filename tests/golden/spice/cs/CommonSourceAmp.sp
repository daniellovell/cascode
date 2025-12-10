* CommonSourceAmp - Generated from ACIR EL

.subckt CommonSourceAmp IN OUT VBIAS VDD GND

MM_in OUT IN GND GND nmos W=10u L=180n m=1
Mload.M OUT VBIAS VDD VDD pmos W=20u L=180n m=1

.ends CommonSourceAmp
