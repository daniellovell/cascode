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

The current bench system requires each bench variant to fully specify its harness, constraints, and measurements, even when benches in a family share 80-90% of their structure. For example, transfer function benches (`DiffToSETransfer`, `SEToSETransfer`, `DiffToDiffTransfer`) likely share:

- AC stimulus injection patterns
- Load configurations
- Frequency sweep parameters
- Common constraint bindings

Only the input/output signal routing differs between variants. This leads to:

1. **Code duplication**: Same harness structure copied across multiple bench definitions
2. **Maintenance burden**: Fixing a bug or improving a pattern requires changes in N places
3. **Inconsistency risk**: Variants can drift apart over time
4. **Barrier to entry**: Users creating custom bench families must understand and replicate the full pattern

---

## 2. Goals and Non-Goals

### Goals

1. Allow bench families to share common structure through inheritance
2. Clearly delineate what must be overridden vs. what is inherited
3. Integrate naturally with existing `bench ... for Trait` syntax
4. Preserve the current bench semantics (this is purely a code organization mechanism)

### Non-Goals

1. Multiple inheritance for benches
2. Runtime polymorphism or dynamic bench selection
3. Changes to how benches bind to circuits or traits
4. Automatic generation of bench variants (that's a separate feature)

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

**Recommendation:** `overrides` clearly communicates that the concrete bench is providing specific implementations for abstract holes while inheriting the rest.

### 3.4 Abstract Members

Members in an abstract bench can be:

1. **Concrete**: Fully defined, inherited as-is
2. **Abstract**: Declared with `abstract` keyword, must be provided by overriding bench
3. **Virtual**: Has a default but can be overridden (future extension, not in this RFC)

```cascode
abstract bench Example for SomeTrait {
    harness {
        concrete_thing : resistor = 1k     // Inherited
        abstract required_net : net         // Must override
    }
}
```

### 3.5 Override Validation

The compiler enforces:

1. All `abstract` members must be provided by the overriding bench
2. Non-abstract members cannot be overridden (unless marked `virtual` in future)
3. An abstract bench cannot be used directly in `attach` statements

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

1. **Chained inheritance**: Should `bench A overrides B` where `B overrides C` be allowed? (Recommend: yes, with depth limit)

2. **Partial override**: Can an overriding bench still be abstract? (e.g., `abstract bench PartialTransfer overrides AbstractTransfer`)

3. **Trait compatibility**: Must the overriding bench specify the same trait, or is it inherited? (Recommend: inherited, can be omitted)

4. **Visibility**: Should abstract members be able to specify visibility (public/private)?

---

## 7. Future Extensions

- `virtual` members with defaults that can be optionally overridden
- Parameterized abstract benches (generic over types)
- Abstract bench libraries for common patterns (characterization, stress testing)

---

## References

- Issue #94: Abstract bench system proposal
- RFC-0000: ACIR Measurement Abstraction
- Current TransferBenches.cas implementation
