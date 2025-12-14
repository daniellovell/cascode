* OTA5TSingleEnded_DCSwept - Generated from ACIR EL

.subckt OTA5TSingleEnded_DCSwept IN_P IN_N OUT VTAIL VDD GND

* Internal nets: mirror_gate, tnode

Mcm.M_SENSE mirror_gate mirror_gate VDD VDD pmos W=2u L=180n m=1
Mcm.M_TAP0 OUT mirror_gate VDD VDD pmos W=2u L=180n m=1
Mdp.M_N mirror_gate IN_P tnode GND nmos W=2u L=180n m=1
Mdp.M_P OUT IN_N tnode GND nmos W=2u L=180n m=1
Mdp.M_TAIL tnode VTAIL GND GND nmos W=4u L=180n m=1

.ends OTA5TSingleEnded_DCSwept
