#!/usr/bin/env python3
"""
Migrate legacy ACIR connection syntax to the current (unreleased) ACIR 3.0 syntax:

- Connection operator: '->'  ->  '--'
- Declaration-local bindings: 'TERM -> net'  ->  '.TERM--net'
- Fill-level connect statements: 'connect A -> B'  ->  'A--B'
- Device declarations: move sizing/value params out of the header into body statements:
    - '... : W=.. L=.. <pdk>' -> '... : <pdk>' + '  size (W=.., L=..)'
    - '... : size=Name <pdk>' -> '... : <pdk>' + '  size Name'
    - Passive params 'R=..'/'C=..'/'L=..' become body 'R = ..' etc.

This script is tooling only. The ACIR reader does not accept legacy syntax.
"""

from __future__ import annotations

import argparse
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List, Optional, Tuple


DEVICE_KINDS = ("nmos", "pmos", "resistor", "capacitor", "inductor", "diode")

DEVICE_DECL_RE = re.compile(
    r"^(?P<indent>\s*)(?P<kind>"
    + "|".join(DEVICE_KINDS)
    + r")\s+(?P<id>\S+)(?P<bindings>\s*\([^)]*\))?\s*:\s*(?P<tail>.*)$"
)

INST_DECL_RE = re.compile(
    r"^(?P<indent>\s*)inst\s+(?P<id>\S+)(?P<bindings>\s*\([^)]*\))?\s*:\s*(?P<type>\S+)\s*$"
)

CONNECT_STMT_RE = re.compile(r"^(?P<indent>\s*)connect\s+(?P<a>.+?)\s*->\s*(?P<b>.+?)\s*$")

PLAIN_MAPPING_RE = re.compile(r"^(?P<indent>\s*)(?P<a>[^/].*?)\s*->\s*(?P<b>.+?)\s*$")

SIZE_ASSIGN_RE = re.compile(r"\bsize\s*=\s*(\([^)]*\)|[A-Za-z_][A-Za-z0-9_]*)")
KV_RE = re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*|[RCL])\s*=\s*([^\s]+)")


@dataclass
class BlockState:
    trait_indent: Optional[int] = None
    in_trait_connectors: bool = False
    connectors_indent: Optional[int] = None
    in_trait_connectors_to: bool = False
    to_indent: Optional[int] = None

    instance_id: Optional[str] = None
    instance_indent: Optional[int] = None

    in_attach_overrides: bool = False
    attach_overrides_indent: Optional[int] = None


def _split_bindings_list(text: str) -> List[Tuple[str, str]]:
    inner = text.strip()
    if inner.startswith("(") and inner.endswith(")"):
        inner = inner[1:-1]
    parts = [p.strip() for p in inner.split(",") if p.strip()]
    out: List[Tuple[str, str]] = []
    for p in parts:
        m = re.match(r"^(?P<a>.+?)\s*->\s*(?P<b>.+?)$", p)
        if not m:
            continue
        out.append((m.group("a").strip(), m.group("b").strip()))
    return out


def _format_binding_pairs(pairs: Iterable[Tuple[str, str]]) -> str:
    items = [f".{a}--{b}" for a, b in pairs]
    return "(" + ", ".join(items) + ")"


def _parse_device_tail(tail: str, kind: str) -> Tuple[str, Optional[str], List[Tuple[str, str]], List[Tuple[str, str]]]:
    """
    Returns (pdk, explicit_size, size_entries, other_params).
    - explicit_size: either '(...)' or 'Name' if present via 'size=...'
    - size_entries: collected from bare W/L/M tokens if no explicit_size
    - other_params: key/value params excluding W/L/M folded into size_entries
    """
    explicit_size: Optional[str] = None
    size_m = SIZE_ASSIGN_RE.search(tail)
    tail_wo_size = tail
    if size_m:
        explicit_size = size_m.group(1).strip()
        tail_wo_size = tail[: size_m.start()] + tail[size_m.end() :]

    params_in_order: List[Tuple[str, str]] = []
    for m in KV_RE.finditer(tail_wo_size):
        params_in_order.append((m.group(1), m.group(2)))
    tail_wo_params = KV_RE.sub("", tail_wo_size)

    leftovers = [t for t in tail_wo_params.split() if t.strip()]
    pdk = leftovers[-1] if leftovers else kind

    size_entries: List[Tuple[str, str]] = []
    other_params: List[Tuple[str, str]] = []

    if explicit_size is None:
        for k, v in params_in_order:
            if k in ("W", "L", "M"):
                size_entries.append((k, v))
            else:
                other_params.append((k, v))
    else:
        other_params = params_in_order

    return pdk, explicit_size, size_entries, other_params


def _device_body_lines(indent: str, kind: str, tail: str) -> List[str]:
    pdk, explicit_size, size_entries, other_params = _parse_device_tail(tail, kind)

    body_indent = indent + "  "
    lines: List[str] = []

    if explicit_size is not None:
        lines.append(f"{body_indent}size {explicit_size}")
    elif size_entries:
        parts = ", ".join([f"{k}={v}" for k, v in size_entries])
        lines.append(f"{body_indent}size ({parts})")

    for k, v in other_params:
        lines.append(f"{body_indent}{k} = {v}")

    return lines


def _convert_instance_connect(
    instance_id: str, a: str, b: str
) -> Tuple[Optional[str], Optional[str], Optional[str]]:
    prefix = instance_id + "."
    a_is = a == instance_id or a.startswith(prefix)
    b_is = b == instance_id or b.startswith(prefix)

    def strip(x: str) -> str:
        return x[len(prefix) :] if x.startswith(prefix) else x

    if a_is and not b_is:
        return strip(a), b, None
    if b_is and not a_is:
        return strip(b), a, None

    # Not a simple instance binding; leave as a fill-level connection.
    return None, None, f"{a}--{b}"


def migrate_text(text: str) -> str:
    state = BlockState()

    out_lines: List[str] = []
    lines = text.splitlines(keepends=False)

    for i, line in enumerate(lines):
        stripped = line.lstrip()
        indent = line[: len(line) - len(stripped)]
        indent_len = len(indent)

        # Never rewrite comments; keep block state unchanged.
        if stripped.startswith("//"):
            out_lines.append(line)
            continue

        # Exit instance body on dedent (ignore blank/comment lines for structure).
        if state.instance_id is not None and stripped:
            if state.instance_indent is not None and indent_len <= state.instance_indent:
                state.instance_id = None
                state.instance_indent = None

        # Exit trait blocks on dedent.
        if state.trait_indent is not None and stripped:
            if indent_len <= state.trait_indent and not stripped.startswith("trait "):
                state.trait_indent = None
                state.in_trait_connectors = False
                state.connectors_indent = None
                state.in_trait_connectors_to = False
                state.to_indent = None

        # Exit connector-to on dedent.
        if state.in_trait_connectors_to and stripped:
            if state.to_indent is not None and indent_len <= state.to_indent:
                state.in_trait_connectors_to = False
                state.to_indent = None

        # Attach override brace tracking.
        if state.in_attach_overrides and stripped.strip() == "}":
            state.in_attach_overrides = False
            state.attach_overrides_indent = None
            out_lines.append(line)
            continue

        if stripped.startswith("trait "):
            state.trait_indent = indent_len
            state.in_trait_connectors = False
            state.connectors_indent = None
            state.in_trait_connectors_to = False
            state.to_indent = None

        if state.trait_indent is not None and stripped.strip() == "connectors:":
            state.in_trait_connectors = True
            state.connectors_indent = indent_len

        if state.in_trait_connectors and stripped.startswith("to ") and stripped.endswith(":"):
            state.in_trait_connectors_to = True
            state.to_indent = indent_len

        # Instance declaration line.
        m = INST_DECL_RE.match(line)
        if m and "->" in line:
            inst_id = m.group("id")
            inst_type = m.group("type")
            bind_text = m.group("bindings")
            if bind_text:
                pairs = _split_bindings_list(bind_text)
                rewritten: List[Tuple[str, str]] = []
                prefix = inst_id + "."
                for a, b in pairs:
                    if a.startswith(prefix):
                        a = a[len(prefix) :]
                    rewritten.append((a, b))
                bind_out = " " + _format_binding_pairs(rewritten)
            else:
                bind_out = ""
            out_lines.append(f"{m.group('indent')}inst {inst_id}{bind_out} : {inst_type}")
            state.instance_id = inst_id
            state.instance_indent = len(m.group("indent"))
            continue

        # Device declaration line.
        m = DEVICE_DECL_RE.match(line)
        if m and "->" in line:
            kind = m.group("kind")
            dev_id = m.group("id")
            tail = m.group("tail").strip()
            bind_text = m.group("bindings")
            pairs = _split_bindings_list(bind_text or "")
            bind_out = " " + _format_binding_pairs(pairs) if pairs else ""
            pdk, _, _, _ = _parse_device_tail(tail, kind)
            out_lines.append(f"{m.group('indent')}{kind} {dev_id}{bind_out} : {pdk}")
            for body_line in _device_body_lines(m.group("indent"), kind, tail):
                out_lines.append(body_line)
            continue

        # Start of attach overrides.
        if stripped.startswith("attach ") and stripped.endswith("{"):
            state.in_attach_overrides = True
            state.attach_overrides_indent = indent_len
            out_lines.append(line)
            continue

        # connect A -> B  (convert; if inside instance and references instance, convert to binding).
        m = CONNECT_STMT_RE.match(line)
        if m:
            a = m.group("a").strip()
            b = m.group("b").strip()
            if state.instance_id is not None:
                term, net, fallback = _convert_instance_connect(state.instance_id, a, b)
                if fallback is not None:
                    out_lines.append(f"{m.group('indent')}{fallback}")
                else:
                    out_lines.append(f"{m.group('indent')}.{term}--{net}")
            else:
                out_lines.append(f"{m.group('indent')}{a}--{b}")
            continue

        # Plain mapping line A -> B.
        m = PLAIN_MAPPING_RE.match(line)
        if m and "->" in line:
            a = m.group("a").strip()
            b = m.group("b").strip()

            if state.in_attach_overrides:
                out_lines.append(f"{m.group('indent')}.{a}--{b}")
                continue

            if state.instance_id is not None:
                out_lines.append(f"{m.group('indent')}.{a}--{b}")
                continue

            if state.in_trait_connectors_to:
                out_lines.append(f"{m.group('indent')}{a}--{b}")
                continue

        # Default: keep line, but do a safe '->' replacement in comments only? No; preserve.
        out_lines.append(line)

    return "\n".join(out_lines) + ("\n" if text.endswith("\n") else "")


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("paths", nargs="+", type=Path, help="ACIR .cir files or directories")
    ap.add_argument("--in-place", action="store_true", help="Rewrite files in place")
    ap.add_argument("-o", "--out", type=Path, help="Output file (single input only)")
    args = ap.parse_args(argv)

    inputs: List[Path] = []
    for p in args.paths:
        if p.is_dir():
            inputs.extend(sorted(p.rglob("*.cir")))
        else:
            inputs.append(p)

    if args.out is not None and len(inputs) != 1:
        ap.error("--out requires a single input file")

    for path in inputs:
        text = path.read_text(encoding="utf-8")
        migrated = migrate_text(text)

        if args.out is not None:
            args.out.write_text(migrated, encoding="utf-8")
            continue

        if args.in_place:
            path.write_text(migrated, encoding="utf-8")
            continue

        print(migrated, end="")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
