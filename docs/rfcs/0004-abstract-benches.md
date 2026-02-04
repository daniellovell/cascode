# RFC-0004: Abstract Bench System

Status: Draft
Authors: Snappie (proposed), Daniel Lovell (review)
Created: 2026-02-04
Last Updated: 2026-02-04
Target Version: Cascode 3.x
Related Issue: #94

---

## Abstract

This RFC proposes an **abstract bench** mechanism to reduce boilerplate in bench families. Abstract benches define common structure (harness setup, shared constraints, and measurement patterns) while concrete benches override only the differing portions. This addresses repetition in `TransferBenches.cas` and similar bench collections, improving maintainability and reducing copy-paste errors.

---

## 1. Problem Statement

The current bench system requires each bench variant to fully specify its harness, constraints, and measurements, even when benches in a family share 80-90% of their structure. Transfer function benches like `DiffToSETransfer`, `SEToSETransfer`, and `DiffToDiffTransfer` share AC stimulus injection patterns, load configurations, frequency sweep parameters, and common constraint bindings. Only the input/output signal routing differs between them.

This repetition creates several problems. The same harness structure gets copied across multiple bench definitions, so fixing a bug or improving a pattern requires changes in N places. Variants can drift apart over time as independent edits accumulate. Users creating custom bench families face a high barrier to entry because they must understand and replicate the full pattern rather than specifying only what differs.

---

## 2. Goals and Non-Goals

### Goals

This RFC aims to allow bench families to share common structure through inheritance while clearly delineating what must be overridden versus what is inherited. The mechanism should integrate naturally with existing `bench ... for Trait` syntax and preserve the current bench semantics; this is purely a code organization mechanism with no runtime behavior changes.

### Non-Goals

This RFC does not address multiple inheritance for benches, runtime polymorphism or dynamic bench selection, changes to how benches bind to circuits or traits, or automatic generation of bench variants. These remain potential future work.

---

## 3. Proposal

### 3.1 Abstract Bench Declaration

A new `abstract bench` construct defines a bench template that cannot be instantiated directly:

```cascode
abstract bench AbstractTransfer for Amplifier {
    harness {
        // Common harness structure
        Vdd, Vss : supply
        Vin_ac : ac_source
        Rload : resistor = 10k
        Cload : capacitor = 1p
        
        // Abstract "holes" that must be defined by overriding bench
        abstract input_net : net
        abstract output_net : net
    }
    
    constraints {
        freq_start : 1
        freq_stop : 1G
        // Common constraints
    }
    
    measurements {
        gain_db = db(V(output_net) / V(input_net))
        f3db = cross(gain_db, gain_db[0] - 3, 'fall')
    }
}
```

### 3.2 Concrete Bench Override

Concrete benches use `overrides` to inherit from an abstract bench:

```cascode
bench DiffToSETransfer overrides AbstractTransfer {
    harness {
        // Fulfill the abstract requirements
        input_net = dut.inp - dut.inm  // differential input
        output_net = dut.out           // single-ended output
    }
    
    // Can add additional constraints or measurements
    measurements {
        // Inherits gain_db and f3db, can add more
        phase_margin = ...
    }
}

bench SEToSETransfer overrides AbstractTransfer {
    harness {
        input_net = dut.inp
        output_net = dut.out
    }
}
```

### 3.3 Keyword Choice: `overrides` vs Alternatives

| Keyword | Pros | Cons |
|---------|------|------|
| `overrides` | Clear intent, implies replacing/fulfilling | Might suggest complete replacement |
| `extends` | Familiar from OOP | Implies adding, not fulfilling holes |
| `implements` | Clear contract fulfillment | Usually for interfaces, not partial implementations |
| `specializes` | Accurate semantically | Verbose, unfamiliar |

The recommended keyword is `overrides` because it clearly communicates that the concrete bench is providing specific implementations for abstract holes while inheriting the rest.

### 3.4 Abstract Members

Members in an abstract bench fall into three categories. Concrete members are fully defined and inherited as-is by overriding benches. Abstract members are declared with the `abstract` keyword and must be provided by the overriding bench. Virtual members (a future extension not covered in this RFC) would have a default implementation that can be optionally overridden.

```cascode
abstract bench Example for SomeTrait {
    harness {
        concrete_thing : resistor = 1k     // Inherited
        abstract required_net : net         // Must override
    }
}
```

### 3.5 Override Validation

The compiler enforces that all abstract members must be provided by the overriding bench. Non-abstract members cannot be overridden unless marked virtual in a future extension. An abstract bench cannot be used directly in `attach` statements.

---

## 4. Examples

### 4.1 Transfer Bench Family (Motivating Example)

```cascode
abstract bench AbstractTransfer for Amplifier {
    harness {
        // Power
        Vdd : supply(voltage=dut.vdd_nom)
        Vss : supply(voltage=0)
        
        // AC stimulus
        Vac : ac_source(mag=1, phase=0)
        
        // Load
        Rload : resistor(r=dut.rload_nom)
        Cload : capacitor(c=dut.cload_nom)
        
        // Abstract: how input/output connect
        abstract stim_pos : net
        abstract stim_neg : net  
        abstract meas_pos : net
        abstract meas_neg : net
    }
    
    connections {
        Vac.p -> stim_pos
        Vac.n -> stim_neg
        Rload.p -> meas_pos
        Rload.n -> meas_neg
        Cload || Rload
    }
    
    measurements {
        v_in = V(stim_pos, stim_neg)
        v_out = V(meas_pos, meas_neg)
        gain = v_out / v_in
        gain_db = db(gain)
        f3db = cross(gain_db, gain_db[0] - 3, direction='fall')
        gbw = f3db * pow(10, gain_db[0]/20)
    }
}

bench DiffToDiffTransfer overrides AbstractTransfer {
    harness {
        stim_pos = dut.inp
        stim_neg = dut.inm
        meas_pos = dut.outp
        meas_neg = dut.outm
    }
}

bench DiffToSETransfer overrides AbstractTransfer {
    harness {
        stim_pos = dut.inp
        stim_neg = dut.inm
        meas_pos = dut.out
        meas_neg = gnd
    }
}

bench SEToSETransfer overrides AbstractTransfer {
    harness {
        stim_pos = dut.inp
        stim_neg = gnd
        meas_pos = dut.out
        meas_neg = gnd
    }
}
```

---

## 5. Migration and Compatibility

This is an additive feature. Existing benches continue to work unchanged. Users can incrementally refactor bench families to use abstract benches.

---

## 6. Open Questions

Several design decisions remain open for discussion. Chained inheritance (where `bench A overrides B` and `B overrides C`) is likely desirable but may warrant a depth limit to prevent overly complex hierarchies. The question of partial overrides arises: can an overriding bench itself be abstract, requiring further specialization? The recommended answer is yes, enabling layered abstraction.

Trait compatibility presents another choice: must the overriding bench explicitly specify the same trait as its parent, or is it inherited? Inheriting the trait and allowing omission seems cleaner. Finally, visibility modifiers (public/private) for abstract members could be useful but add complexity; this RFC defers that decision.

---

## 7. Future Extensions

Future work may introduce virtual members with defaults that can be optionally overridden, parameterized abstract benches (generic over types), and standard library abstract benches for common patterns like characterization and stress testing.

---

## References

- Issue #94: Abstract bench system proposal
- RFC-0000: ACIR Measurement Abstraction
- Current TransferBenches.cas implementation
