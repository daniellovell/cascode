gm_id.v1 — template harness example (YAML)

This folder shows a user-extensible harness packaged as data only.

- harness.yaml: manifest with parameters, supported backends, and template files
- netlist.scs.tpl: Spectre template
- netlist.cir.tpl: ngspice template

Cascode renders these with a TestbenchSpec context (spec fields, device decks, section/corner) to produce a runnable bench and results.csv.
