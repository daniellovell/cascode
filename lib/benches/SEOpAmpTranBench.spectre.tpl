// cascode SEOpAmpTranBench (Spectre)
simulator lang=spectre
global 0

// includes
{{ for inc in includes_with_section }}
{{ if section }}include "{{ inc }}" section={{ section }}{{ else }}include "{{ inc }}"{{ end }}
{{ end }}
{{ for inc in includes_without_section }}
include "{{ inc }}"
{{ end }}

// DUT (placeholder for tran bench)
XDUT ({{ port_list }}) {{ circuit_name }}

// Placeholder: use the ngspice template for SingleEndedOutputSwing results.
