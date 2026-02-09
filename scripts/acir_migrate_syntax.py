#!/usr/bin/env python3
"""
Migrate ACIR 2.x indentation syntax to ACIR 3.0.

This script focuses on common cases:
- Converts colon-delimited blocks to braces.
- Renames `trait` to `interface`.
- Converts `inst` declarations to constructor syntax.
- Converts primitive device declarations to constructor syntax with placeholder primitives.
- Moves instance `param`/`size` lines into constructor arguments.
- Converts `connect A -> B` to `A--B` and `->` to `--`.
- Converts numeric constraints `id : Bench::Metric ...` to `id = Bench::Metric ...`.

Manual review is required for:
- Primitive selection (placeholders must be replaced).
- Circuit parameter type inference (defaults to `real`).
- Complex sizing/parameter expressions.
"""

from __future__ import annotations

import argparse
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable


DEVICE_KINDS = ("nmos", "pmos", "resistor", "capacitor", "inductor", "diode")

PLACEHOLDER_PRIMITIVES: dict[str, str] = {
    "nmos": "Level1_NMOS",
    "pmos": "Level1_PMOS",
    "resistor": "Ideal_Resistor",
    "capacitor": "Ideal_Capacitor",
    "inductor": "Ideal_Inductor",
    "diode": "Ideal_Diode",
}

PRIMITIVE_DEFS: dict[str, list[str]] = {
    "Level1_NMOS": [
        "primitive nmos Level1_NMOS(size primSize) {",
        '  device "level1_nmos"',
        "  params {",
        "    W = primSize.W",
        "    L = primSize.L",
        "    m = primSize.M",
        "  }",
        "}",
    ],
    "Level1_PMOS": [
        "primitive pmos Level1_PMOS(size primSize) {",
        '  device "level1_pmos"',
        "  params {",
        "    W = primSize.W",
        "    L = primSize.L",
        "    m = primSize.M",
        "  }",
        "}",
    ],
    "Ideal_Resistor": [
        "primitive resistor Ideal_Resistor(size primSize) {",
        '  device "resistor"',
        "  params {",
        "    R = primSize.R",
        "  }",
        "}",
    ],
    "Ideal_Capacitor": [
        "primitive capacitor Ideal_Capacitor(size primSize) {",
        '  device "capacitor"',
        "  params {",
        "    C = primSize.C",
        "  }",
        "}",
    ],
    "Ideal_Inductor": [
        "primitive inductor Ideal_Inductor(size primSize) {",
        '  device "inductor"',
        "  params {",
        "    L = primSize.L",
        "  }",
        "}",
    ],
    "Ideal_Diode": [
        "primitive diode Ideal_Diode(size primSize) {",
        '  device "diode"',
        "  params {",
        "    A = primSize.A",
        "  }",
        "}",
    ],
}


BLOCK_KEYWORDS = {
    "bundle",
    "trait",
    "interface",
    "bench",
    "outputs",
    "connectors",
    "to",
    "fill",
    "constraints",
    "numeric",
    "tech",
    "harness",
    "provenance",
    "graph",
    "config",
}

CONNECT_STMT_RE = re.compile(r"^(?P<indent>\s*)connect\s+(?P<a>.+?)\s*->\s*(?P<b>.+?)\s*$")

INST_DECL_RE = re.compile(
    r"^(?P<indent>\s*)inst\s+(?P<id>\S+)(?P<bindings>\s*\([^)]*\))?\s*:\s*(?P<type>\S+)\s*$"
)

DEVICE_DECL_RE = re.compile(
    r"^(?P<indent>\s*)(?P<kind>"
    + "|".join(DEVICE_KINDS)
    + r")\s+(?P<id>\S+)(?P<bindings>\s*\([^)]*\))?\s*:\s*(?P<tail>.*)$"
)

PARAM_LINE_RE = re.compile(
    r"^param\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)"
    r"(?:\s*:\s*(?P<type>[A-Za-z_][A-Za-z0-9_]*))?"
    r"(?:\s*=\s*(?P<value>.+))?$"
)
SIZE_LINE_RE = re.compile(
    r"^size\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=\s*(?P<value>.+))?$"
)
ASSIGN_RE = re.compile(r"^(?P<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?P<value>.+)$")
SIZE_ASSIGN_RE = re.compile(r"\bsize\s*=\s*(\([^)]*\)|[A-Za-z_][A-Za-z0-9_]*)")
KV_RE = re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*|[RCLMW])\s*=\s*([^\s]+)")
DOLLAR_IDENT_RE = re.compile(r"\$([A-Za-z_][A-Za-z0-9_]*)")


def _split_bindings_list(text: str) -> list[tuple[str, str]]:
    inner = text.strip()
    if inner.startswith("(") and inner.endswith(")"):
        inner = inner[1:-1]
    parts = [p.strip() for p in inner.split(",") if p.strip()]
    out: list[tuple[str, str]] = []
    for p in parts:
        m = re.match(r"^(?P<a>.+?)\s*(?:->|--)\s*(?P<b>.+?)$", p)
        if not m:
            continue
        out.append((m.group("a").strip(), m.group("b").strip()))
    return out


def _format_bindings(pairs: Iterable[tuple[str, str]]) -> list[str]:
    lines: list[str] = []
    for a, b in pairs:
        if not a.startswith("."):
            a = "." + a
        lines.append(f"{a}--{b}")
    return lines


def _parse_device_tail(tail: str, kind: str) -> tuple[str | None, dict[str, str], list[tuple[str, str]]]:
    explicit_size: str | None = None
    size_m = SIZE_ASSIGN_RE.search(tail)
    tail_wo_size = tail
    if size_m:
        explicit_size = size_m.group(1).strip()
        tail_wo_size = tail[: size_m.start()] + tail[size_m.end() :]

    params_in_order: list[tuple[str, str]] = []
    for m in KV_RE.finditer(tail_wo_size):
        params_in_order.append((m.group(1), m.group(2)))

    size_entries: dict[str, str] = {}
    other_params: list[tuple[str, str]] = []
    for k, v in params_in_order:
        if k in ("W", "L", "M", "R", "C", "L"):
            size_entries[k] = v
        else:
            other_params.append((k, v))

    return explicit_size, size_entries, other_params


def _parse_kv_list(text: str) -> dict[str, str]:
    inner = text.strip()
    if inner.startswith("(") and inner.endswith(")"):
        inner = inner[1:-1]
    entries: dict[str, str] = {}
    for part in [p.strip() for p in inner.split(",") if p.strip()]:
        m = re.match(r"^(?P<k>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?P<v>.+)$", part)
        if m:
            entries[m.group("k")] = m.group("v").strip()
    return entries


def _build_size_arg(kind: str, size_entries: dict[str, str]) -> str:
    if not size_entries:
        if kind in ("nmos", "pmos"):
            size_entries = {"W": "??", "L": "??", "M": "1"}
        elif kind == "resistor":
            size_entries = {"R": "??"}
        elif kind == "capacitor":
            size_entries = {"C": "??"}
        elif kind == "inductor":
            size_entries = {"L": "??"}
        elif kind == "diode":
            size_entries = {"A": "??"}
    parts = ", ".join([f"{k}={v}" for k, v in size_entries.items()])
    return f"size({parts})"


def _normalize_size_value(value: str) -> str:
    v = value.strip()
    if v.startswith("size("):
        return v
    if v.startswith("(") and v.endswith(")"):
        return "size" + v
    return v


def _strip_dollar_identifiers(text: str) -> str:
    return DOLLAR_IDENT_RE.sub(r"\1", text)


@dataclass
class InstanceState:
    indent: str
    indent_len: int
    inst_id: str
    inst_type: str
    bindings: list[str] = field(default_factory=list)
    args: list[str] = field(default_factory=list)
    body_lines: list[str] = field(default_factory=list)

    def flush(self, out_lines: list[str]) -> None:
        args_part = f"({', '.join(self.args)})" if self.args else ""
        out_lines.append(f"{self.indent}{self.inst_id} = new {self.inst_type}{args_part} {{")
        for line in self.bindings + self.body_lines:
            out_lines.append(f"{self.indent}  {line}")
        out_lines.append(f"{self.indent}}}")


@dataclass
class DeviceState:
    indent: str
    indent_len: int
    kind: str
    dev_id: str
    primitive: str
    bindings: list[str] = field(default_factory=list)
    size_arg: str | None = None
    size_entries: dict[str, str] = field(default_factory=dict)
    extra_params: list[tuple[str, str]] = field(default_factory=list)

    def flush(self, out_lines: list[str]) -> None:
        size_arg = self.size_arg or _build_size_arg(self.kind, self.size_entries)
        out_lines.append(
            f"{self.indent}{self.kind} {self.dev_id} = new {self.primitive}({size_arg}) {{"
        )
        for line in self.bindings:
            out_lines.append(f"{self.indent}  {line}")
        if self.extra_params:
            joined = ", ".join([f"{k}={v}" for k, v in self.extra_params])
            out_lines.append(f"{self.indent}  // TODO: migrate params {joined}")
        out_lines.append(f"{self.indent}}}")


@dataclass
class PendingCircuit:
    indent: str
    indent_len: int
    header: str
    params: list[str] = field(default_factory=list)
    leading_lines: list[str] = field(default_factory=list)

    def flush(self, out_lines: list[str], block_stack: list[int]) -> None:
        header = self.header
        if self.params:
            header = _append_signature_params(header, self.params)
        if not header.rstrip().endswith("{"):
            header = header.rstrip() + " {"
        out_lines.append(header)
        out_lines.extend(self.leading_lines)
        block_stack.append(self.indent_len)


def _append_signature_params(header: str, params: list[str]) -> str:
    if not params:
        return header
    m = re.match(r"^(?P<prefix>\s*circuit\s+\S+)(?P<sig>\([^)]*\))?(?P<rest>.*)$", header)
    if not m:
        return header
    prefix = m.group("prefix")
    sig = m.group("sig")
    rest = m.group("rest") or ""
    if sig:
        sig_inner = sig[1:-1].strip()
        combined = sig_inner + (", " if sig_inner else "") + ", ".join(params)
        sig = f"({combined})"
    else:
        sig = "(" + ", ".join(params) + ")"
    return prefix + sig + rest


def migrate_text(text: str) -> str:
    out_lines: list[str] = []
    block_stack: list[int] = []
    required_primitives: set[str] = set()

    inst_state: InstanceState | None = None
    dev_state: DeviceState | None = None
    circuit_state: PendingCircuit | None = None

    lines = text.splitlines(keepends=False)
    i = 0
    while i < len(lines):
        line = _strip_dollar_identifiers(lines[i])
        stripped = line.lstrip()
        indent = line[: len(line) - len(stripped)]
        indent_len = len(indent)

        if stripped.startswith("//"):
            if circuit_state is not None:
                circuit_state.leading_lines.append(line)
            else:
                out_lines.append(line)
            i += 1
            continue

        if stripped.strip() == "" and circuit_state is not None:
            circuit_state.leading_lines.append(line)
            i += 1
            continue

        # Flush instance/device blocks on dedent.
        if inst_state is not None and stripped and indent_len <= inst_state.indent_len:
            inst_state.flush(out_lines)
            inst_state = None
        if dev_state is not None and stripped and indent_len <= dev_state.indent_len:
            dev_state.flush(out_lines)
            dev_state = None

        # Close generic blocks on dedent.
        if stripped:
            while block_stack and indent_len <= block_stack[-1]:
                out_lines.append(" " * block_stack[-1] + "}")
                block_stack.pop()

        # Handle pending circuit header.
        if circuit_state is not None:
            if stripped and indent_len <= circuit_state.indent_len:
                circuit_state.flush(out_lines, block_stack)
                circuit_state = None
            elif stripped and indent_len > circuit_state.indent_len:
                m_param = PARAM_LINE_RE.match(stripped)
                m_size = SIZE_LINE_RE.match(stripped)
                if m_param:
                    name = m_param.group("name")
                    param_type = m_param.group("type") or "real"
                    value = m_param.group("value")
                    if value:
                        circuit_state.params.append(f"{param_type} {name}={value.strip()}")
                    else:
                        circuit_state.params.append(f"{param_type} {name}")
                    i += 1
                    continue
                if m_size:
                    name = m_size.group("name")
                    value = m_size.group("value")
                    value = _normalize_size_value(value) if value else "size(??)"
                    circuit_state.params.append(f"size {name}={value}")
                    i += 1
                    continue
                circuit_state.flush(out_lines, block_stack)
                circuit_state = None
                continue

        # Convert connect statements.
        m_connect = CONNECT_STMT_RE.match(line)
        if m_connect:
            out_lines.append(
                f"{m_connect.group('indent')}{m_connect.group('a').strip()}--{m_connect.group('b').strip()}"
            )
            i += 1
            continue

        # Instance declaration.
        m_inst = INST_DECL_RE.match(line)
        if m_inst:
            inst_id = m_inst.group("id")
            inst_type = m_inst.group("type")
            bindings = m_inst.group("bindings")
            binding_lines: list[str] = []
            if bindings:
                binding_lines = _format_bindings(_split_bindings_list(bindings))
            inst_state = InstanceState(
                indent=indent, indent_len=indent_len, inst_id=inst_id, inst_type=inst_type
            )
            inst_state.bindings.extend(binding_lines)
            i += 1
            continue

        # Device declaration.
        m_dev = DEVICE_DECL_RE.match(line)
        if m_dev:
            kind = m_dev.group("kind")
            dev_id = m_dev.group("id")
            bindings = m_dev.group("bindings")
            binding_lines: list[str] = []
            if bindings:
                binding_lines = _format_bindings(_split_bindings_list(bindings))
            explicit_size, size_entries, other_params = _parse_device_tail(
                m_dev.group("tail").strip(), kind
            )
            primitive = PLACEHOLDER_PRIMITIVES.get(kind, f"{kind}_TODO")
            required_primitives.add(primitive)
            dev_state = DeviceState(
                indent=indent,
                indent_len=indent_len,
                kind=kind,
                dev_id=dev_id,
                primitive=primitive,
            )
            dev_state.bindings.extend(binding_lines)
            dev_state.size_entries.update(size_entries)
            dev_state.extra_params.extend(other_params)
            if explicit_size:
                if explicit_size.startswith("(") and explicit_size.endswith(")"):
                    dev_state.size_entries.update(_parse_kv_list(explicit_size))
                else:
                    dev_state.size_arg = explicit_size
            i += 1
            continue

        # Inside instance body.
        if inst_state is not None and stripped:
            if indent_len > inst_state.indent_len:
                m_param = PARAM_LINE_RE.match(stripped)
                m_size = SIZE_LINE_RE.match(stripped)
                if m_param:
                    name = m_param.group("name")
                    value = m_param.group("value")
                    value = value.strip() if value else "??"
                    inst_state.args.append(f"{name}={value}")
                    i += 1
                    continue
                if m_size:
                    name = m_size.group("name")
                    value = m_size.group("value")
                    if value:
                        value = _normalize_size_value(value)
                    else:
                        value = "size(??)"
                    if not value.startswith("size("):
                        value = "size(" + value + ")" if value != "??" else "size(??)"
                    inst_state.args.append(f"{name}={value}")
                    i += 1
                    continue
                if "--" in stripped or "->" in stripped:
                    normalized = stripped.replace("->", "--")
                    if not normalized.lstrip().startswith("."):
                        normalized = "." + normalized.strip()
                    inst_state.body_lines.append(normalized)
                    i += 1
                    continue
            # Fall through: treat as regular line inside instance block.

        # Inside device body.
        if dev_state is not None and stripped:
            if indent_len > dev_state.indent_len:
                if stripped.startswith("size "):
                    value = stripped[len("size ") :].strip()
                    if value:
                        value = _normalize_size_value(value)
                        if value.startswith("size("):
                            dev_state.size_arg = value
                        elif value.startswith("(") and value.endswith(")"):
                            dev_state.size_entries.update(_parse_kv_list(value))
                        else:
                            dev_state.size_arg = value
                    i += 1
                    continue
                m_assign = ASSIGN_RE.match(stripped)
                if m_assign:
                    key = m_assign.group("key")
                    value = m_assign.group("value").strip()
                    dev_state.size_entries[key] = value
                    i += 1
                    continue
                if "--" in stripped or "->" in stripped:
                    normalized = stripped.replace("->", "--")
                    if not normalized.lstrip().startswith("."):
                        normalized = "." + normalized.strip()
                    dev_state.bindings.append(normalized)
                    i += 1
                    continue

        m_size_decl = SIZE_LINE_RE.match(stripped)
        if m_size_decl:
            name = m_size_decl.group("name")
            value = m_size_decl.group("value")
            if value:
                value = _normalize_size_value(value)
            else:
                value = "size(??)"
            out_lines.append(f"{indent}size {name} = {value}")
            i += 1
            continue

        # Block headers with colon.
        if stripped.endswith(":"):
            head = stripped[:-1].strip()
            first = head.split()[0] if head else ""
            if first == "trait":
                head = "interface " + " ".join(head.split()[1:])
            if first == "circuit":
                circuit_state = PendingCircuit(indent=indent, indent_len=indent_len, header=indent + head)
                i += 1
                continue
            if first in BLOCK_KEYWORDS:
                out_lines.append(f"{indent}{head} {{")
                block_stack.append(indent_len)
                i += 1
                continue

        # Bench header without braces.
        if stripped.startswith("bench ") and "{" not in stripped:
            out_lines.append(f"{indent}{stripped} {{")
            block_stack.append(indent_len)
            i += 1
            continue

        # Circuit header without braces.
        if stripped.startswith("circuit ") and "{" not in stripped:
            circuit_state = PendingCircuit(indent=indent, indent_len=indent_len, header=line)
            i += 1
            continue

        # Constraint metric rewrite inside numeric/tech blocks.
        if block_stack and stripped and ":" in stripped:
            if stripped.lstrip().startswith(tuple("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ")):
                parts = stripped.split(":", 1)
                if len(parts) == 2 and "Bench::" in parts[1]:
                    out_lines.append(f"{indent}{parts[0].strip()} = {parts[1].strip()}")
                    i += 1
                    continue

        # Replace arrow syntax.
        if "->" in line:
            line = line.replace("->", "--")

        # Rename trait keyword outside definitions if present.
        if stripped.startswith("trait "):
            line = indent + "interface " + stripped[len("trait ") :]

        out_lines.append(line)
        i += 1

    if inst_state is not None:
        inst_state.flush(out_lines)
    if dev_state is not None:
        dev_state.flush(out_lines)

    while block_stack:
        out_lines.append(" " * block_stack.pop() + "}")

    if circuit_state is not None:
        circuit_state.flush(out_lines, block_stack)

    # Insert primitive definitions if missing.
    existing = set()
    for line in out_lines:
        if line.startswith("primitive "):
            existing.add(line.split()[2])
    to_insert = [p for p in sorted(required_primitives) if p in PRIMITIVE_DEFS and p not in existing]
    if to_insert:
        insert_at = 0
        for idx, line in enumerate(out_lines):
            if line.strip().startswith("ACIR "):
                insert_at = idx + 1
                break
        insert_lines: list[str] = [""]
        for prim in to_insert:
            insert_lines.extend(PRIMITIVE_DEFS[prim])
            insert_lines.append("")
        out_lines[insert_at:insert_at] = insert_lines

    return "\n".join(out_lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", type=Path, nargs="?", help="Cascode file to migrate")
    parser.add_argument("--out", type=Path, help="Output file (defaults to in-place)")
    args = parser.parse_args()

    if args.path is None:
        parser.print_help()
        return 2

    text = args.path.read_text(encoding="utf-8")
    migrated = migrate_text(text)

    if args.out:
        args.out.write_text(migrated, encoding="utf-8")
    else:
        args.path.write_text(migrated, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
