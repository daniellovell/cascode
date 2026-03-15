#!/usr/bin/env python3

from __future__ import annotations

import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
SCOPES = ("README.md", "docs", "spec", "tests", "editors", "tools")
IGNORE_PARTS = {"bin", "obj", "build", "node_modules", ".git"}
LINKABLE_PREFIXES = (
    "docs/",
    "spec/",
    "tools/",
    "tests/",
    "lib/",
    "editors/",
    "scripts/",
    "README.md",
    "AGENTS.md",
)

FENCE_RE = re.compile(r"```.*?```", re.DOTALL)
LINK_RE = re.compile(r"(?<!\!)\[[^\]]*\]\(([^)]+)\)")
INLINE_CODE_RE = re.compile(r"(?<!`)`([^`\n]+)`(?!`)")
HEADING_RE = re.compile(r"^(#{1,6})\s+(.*)$", re.MULTILINE)


def iter_markdown_files() -> list[Path]:
    files: list[Path] = []
    for scope in SCOPES:
        target = ROOT / scope
        if target.is_file():
            files.append(target)
            continue
        if not target.exists():
            continue
        for path in target.rglob("*.md"):
            if any(part in IGNORE_PARTS for part in path.parts):
                continue
            files.append(path)
    return sorted(set(files))


def slugify(heading: str) -> str:
    heading = heading.strip().lower()
    heading = re.sub(r"[^\w\s-]", "", heading)
    heading = re.sub(r"\s+", "-", heading)
    heading = re.sub(r"-{2,}", "-", heading)
    return heading.strip("-")


def load_anchors(path: Path, cache: dict[Path, set[str]]) -> set[str]:
    anchors = cache.get(path)
    if anchors is not None:
        return anchors
    text = path.read_text(encoding="utf-8")
    anchors = {slugify(match.group(2)) for match in HEADING_RE.finditer(text)}
    cache[path] = anchors
    return anchors


def is_external(target: str) -> bool:
    return target.startswith(("http://", "https://", "mailto:", "data:"))


def validate_links(path: Path, text: str, anchor_cache: dict[Path, set[str]]) -> list[str]:
    errors: list[str] = []
    for match in LINK_RE.finditer(text):
        raw_target = match.group(1).strip()
        if not raw_target or is_external(raw_target):
            continue
        if raw_target.startswith("<") and raw_target.endswith(">"):
            raw_target = raw_target[1:-1]
        target_part, anchor = raw_target.split("#", 1) if "#" in raw_target else (raw_target, "")
        if raw_target.startswith("#"):
            target_path = path
            anchor = raw_target[1:]
        else:
            target_path = (path.parent / target_part).resolve()
        if not target_path.exists():
            errors.append(f"{path.relative_to(ROOT)}: broken link target '{raw_target}'")
            continue
        if anchor:
            anchors = load_anchors(target_path, anchor_cache)
            if anchor not in anchors:
                errors.append(
                    f"{path.relative_to(ROOT)}: broken anchor '{anchor}' in '{raw_target}'"
                )
    return errors


def looks_like_repo_path(value: str) -> bool:
    if " " in value or any(ch in value for ch in "*<>"):
        return False
    return value.startswith(LINKABLE_PREFIXES)


def validate_inline_paths(path: Path, text: str) -> list[str]:
    relative_parts = path.relative_to(ROOT).parts
    if relative_parts[:2] == ("docs", "rfcs"):
        return []
    without_fences = FENCE_RE.sub("", text)
    without_links = LINK_RE.sub("", without_fences)
    errors: list[str] = []
    for match in INLINE_CODE_RE.finditer(without_links):
        value = match.group(1).strip()
        if not looks_like_repo_path(value):
            continue
        target = (ROOT / value).resolve()
        if target.exists():
            errors.append(
                f"{path.relative_to(ROOT)}: inline repo path should be a markdown link: '{value}'"
            )
    return errors


def main() -> int:
    anchor_cache: dict[Path, set[str]] = {}
    errors: list[str] = []
    for path in iter_markdown_files():
        text = path.read_text(encoding="utf-8")
        errors.extend(validate_links(path, text, anchor_cache))
        errors.extend(validate_inline_paths(path, text))
    if errors:
        for error in errors:
            print(error)
        print(f"\n{len(errors)} markdown issues found.")
        return 1
    print("Markdown links and repo references look good.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
