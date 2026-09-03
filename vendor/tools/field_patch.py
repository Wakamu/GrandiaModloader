#!/usr/bin/env python3
"""Compile assembler patch files into a fopen overlay tree.

A patch is the existing script / table-1 / table-2 assembler plus a thin
envelope (map, replace vs add). Compile against vanilla FIELD + TEXT, write
complete MDP / SCN / OFS under an overlay root. The ASI serves that root
ahead of Steam files (and ahead of Redux).

    python tools/field_patch.py build data/field_patches -o data/field_patch_overlay

Patch grammar (comments ``# …``, blank lines ignored)::

    patch map=3424

    table 1 {
    replace dest=0x3404
    zone 0 setup dest=0x3404 …
    add
    zone 0 setup dest=0x9999 …
    }

    table 2 {
    replace id=1
    hook 1 setup …
    }

    script 0x0000 {
    script 0x0000
      …
    }

``replace dest=`` matches table-1 setup warps (handler 0x02). Add ``aabb=``
when two rows share a dest. ``replace id=`` matches table-2 ``hook`` id.
``add`` appends. Scripts are replaced whole by id.
"""
from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from field_hook_asm import (
    AsmError,
    _parse_aabb,
    _parse_hook_line,
    _parse_zone_line,
    _without_comment,
    parse_int,
)
from field_script_disasm import DEFAULT_CONTENT, DEFAULT_TEXT
from field_script_editor import EditorSession
from field_script_hooks import HookEditSession, validate_hook_bundle

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_PATCH_DIR = REPO_ROOT / "data" / "field_patches"
DEFAULT_OVERLAY_DIR = REPO_ROOT / "data" / "field_patch_overlay"


class PatchError(ValueError):
    pass


@dataclass
class TableOp:
    action: str  # replace | add
    dest: int | None = None
    aabb: bytes | None = None
    hook_id: int | None = None
    line: str = ""


@dataclass
class ScriptOp:
    script_id: int
    text: str


@dataclass
class FieldPatch:
    map_stem: str
    source: str = ""
    table1: list[TableOp] = field(default_factory=list)
    table2: list[TableOp] = field(default_factory=list)
    scripts: list[ScriptOp] = field(default_factory=list)


def _setup_dest(row: bytes) -> int | None:
    if len(row) < 7 or (row[1] & 0x3F) != 0x02:
        return None
    return (row[5] << 8) | row[6]


def _parse_replace_kv(rest: list[str]) -> tuple[int | None, bytes | None, int | None]:
    dest = aabb = hid = None
    for tok in rest:
        if "=" not in tok:
            raise PatchError(f"replace token {tok!r} needs key=value")
        key, _, val = tok.partition("=")
        if key == "dest":
            dest = parse_int(val)
        elif key == "aabb":
            aabb = _parse_aabb(val)
        elif key == "id":
            hid = parse_int(val)
        else:
            raise PatchError(f"unknown replace key {key!r}")
    return dest, aabb, hid


def parse_patch_text(text: str, *, source: str = "") -> FieldPatch:
    lines = text.splitlines()
    i = 0
    n = len(lines)

    def skip() -> None:
        nonlocal i
        while i < n:
            s = _without_comment(lines[i])
            if not s:
                i += 1
                continue
            break

    def raw() -> str:
        return _without_comment(lines[i])

    skip()
    if i >= n:
        raise PatchError("expected 'patch map=STEM'")
    hdr = raw().split()
    if not hdr or hdr[0] != "patch":
        raise PatchError(f"expected 'patch map=STEM', got {lines[i]!r}")
    stem = None
    for tok in hdr[1:]:
        if tok.startswith("map="):
            stem = tok.split("=", 1)[1].upper()
    if not stem:
        raise PatchError("patch line needs map=STEM")
    i += 1
    patch = FieldPatch(map_stem=stem, source=source)

    while True:
        skip()
        if i >= n:
            break
        toks = raw().split()
        if not toks:
            i += 1
            continue
        if toks[0] == "table" and len(toks) >= 2 and toks[1] in ("1", "2"):
            ti = int(toks[1])
            if len(toks) < 3 or toks[2] != "{":
                raise PatchError("expected 'table N {'")
            i += 1
            ops = _parse_table_block(lines, i, n, table=ti)
            i = ops[1]
            if ti == 1:
                patch.table1.extend(ops[0])
            else:
                patch.table2.extend(ops[0])
            continue
        if toks[0] == "script":
            if len(toks) < 3 or toks[2] != "{":
                raise PatchError("expected 'script 0xNNNN {'")
            sid = parse_int(toks[1])
            i += 1
            body, i = _parse_brace_block(lines, i, n)
            patch.scripts.append(ScriptOp(script_id=sid, text=body))
            continue
        raise PatchError(f"expected table or script block, got {lines[i]!r}")
    if not (patch.table1 or patch.table2 or patch.scripts):
        raise PatchError("patch has no table or script ops")
    return patch


def _parse_brace_block(lines: list[str], i: int, n: int) -> tuple[str, int]:
    """Take lines until the matching envelope ``}``. Nested ``if`` / ``menu`` braces count."""
    body: list[str] = []
    depth = 1
    while i < n:
        s = _without_comment(lines[i])
        depth += s.count("{") - s.count("}")
        if depth <= 0:
            i += 1
            return "\n".join(body).strip() + "\n", i
        body.append(lines[i])
        i += 1
    raise PatchError("unclosed '{'")


def _parse_table_block(
    lines: list[str], i: int, n: int, *, table: int,
) -> tuple[list[TableOp], int]:
    ops: list[TableOp] = []
    pending: TableOp | None = None
    prefix = "zone" if table == 1 else "hook"

    def flush_pending() -> None:
        nonlocal pending
        if pending is not None:
            raise PatchError(f"{pending.action} needs a {prefix} line")

    while i < n:
        s = _without_comment(lines[i])
        if not s:
            i += 1
            continue
        if s == "}":
            flush_pending()
            return ops, i + 1
        toks = s.split()
        if toks[0] == "replace":
            flush_pending()
            dest, aabb, hid = _parse_replace_kv(toks[1:])
            if table == 1:
                if dest is None and aabb is None:
                    raise PatchError("table 1 replace needs dest= and/or aabb=")
                if hid is not None:
                    raise PatchError("table 1 replace uses dest=/aabb=, not id=")
            else:
                if hid is None:
                    raise PatchError("table 2 replace needs id=")
                if dest is not None or aabb is not None:
                    raise PatchError("table 2 replace uses id=, not dest=/aabb=")
            pending = TableOp(action="replace", dest=dest, aabb=aabb, hook_id=hid)
            i += 1
            continue
        if toks[0] == "add":
            flush_pending()
            pending = TableOp(action="add")
            i += 1
            continue
        if toks[0] != prefix:
            raise PatchError(f"expected {prefix} line, got {lines[i]!r}")
        if pending is None:
            raise PatchError(f"{prefix} line needs replace or add above it")
        pending.line = s
        ops.append(pending)
        pending = None
        i += 1
    raise PatchError("unclosed '{'")


def _match_table1(rows: list[bytes], op: TableOp) -> int:
    hits: list[int] = []
    for i, row in enumerate(rows):
        if op.dest is not None:
            if _setup_dest(row) != op.dest:
                continue
        if op.aabb is not None:
            if len(row) != 32 or row[20:32] != op.aabb:
                continue
        hits.append(i)
    if not hits:
        raise PatchError(
            f"no table-1 row matches dest={op.dest!r} aabb={op.aabb.hex() if op.aabb else None}"
        )
    if len(hits) > 1:
        raise PatchError(
            f"{len(hits)} table-1 rows match dest={op.dest!r}; add aabb= to disambiguate"
        )
    return hits[0]


def _match_table2(rows: list[bytes], op: TableOp) -> int:
    hits = [i for i, row in enumerate(rows) if row and row[0] == op.hook_id]
    if not hits:
        raise PatchError(f"no table-2 hook id {op.hook_id}")
    if len(hits) > 1:
        raise PatchError(f"{len(hits)} table-2 rows have id {op.hook_id}")
    return hits[0]


def apply_patch(
    patch: FieldPatch,
    *,
    field_root: Path,
    text_root: Path,
) -> dict[str, Any]:
    """Merge one patch into in-memory MDP / SCN sessions. Returns dirty flags."""
    stem = patch.map_stem
    hooks: HookEditSession | None = None
    scripts: EditorSession | None = None
    try:
        if patch.table1 or patch.table2:
            hooks = HookEditSession(stem, field_root)
        if patch.scripts:
            scripts = EditorSession.open(stem, field_root, text_root, source="scn")
    except (FileNotFoundError, OSError) as exc:
        raise PatchError(str(exc)) from exc

    if hooks is not None:
        for op in patch.table1:
            try:
                raw = _parse_zone_line(op.line.split())
            except AsmError as exc:
                raise PatchError(str(exc)) from exc
            if op.action == "replace":
                idx = _match_table1(hooks.table1_rows(), op)
                hooks.replace_row(1, idx, raw)
            else:
                if len(hooks.table1_rows()) >= 255:
                    raise PatchError("table 1 count would exceed 255")
                hooks.insert_row(1, raw)
        for op in patch.table2:
            try:
                raw = _parse_hook_line(op.line.split())
            except AsmError as exc:
                raise PatchError(str(exc)) from exc
            if op.action == "replace":
                idx = _match_table2(hooks.table2_rows(), op)
                hooks.replace_row(2, idx, raw)
            else:
                if len(hooks.table2_rows()) >= 255:
                    raise PatchError("table 2 count would exceed 255")
                hooks.insert_row(2, raw)
        errs = validate_hook_bundle(hooks.bundle())
        if errs:
            raise PatchError("sec[7] after patch: " + "; ".join(errs))

    if scripts is not None:
        for sop in patch.scripts:
            try:
                scripts.apply_script_asm(sop.script_id, sop.text)
            except (ValueError, KeyError) as exc:
                raise PatchError(str(exc)) from exc

    return {
        "map": stem,
        "hooks": hooks,
        "scripts": scripts,
        "mdp": hooks is not None and hooks.dirty,
        "scn": scripts is not None and scripts.dirty,
    }


def write_overlay(result: dict[str, Any], overlay_root: Path) -> dict[str, str]:
    stem = result["map"]
    written: dict[str, str] = {}
    if result["mdp"]:
        dest = overlay_root / "FIELD" / f"{stem}.mdp"
        result["hooks"].write_mdp(dest)
        written["mdp"] = str(dest)
    if result["scn"]:
        text_dir = overlay_root / "TEXT" / "EN"
        text_dir.mkdir(parents=True, exist_ok=True)
        bank, directory = result["scripts"].emit()
        scn = text_dir / f"{stem}.SCN"
        ofs = text_dir / f"{stem}.OFS"
        scn.write_bytes(bank)
        ofs.write_bytes(directory)
        written["scn"] = str(scn)
        written["ofs"] = str(ofs)
    return written


def collect_patch_files(paths: list[Path]) -> list[Path]:
    files: list[Path] = []
    for p in paths:
        if p.is_dir():
            files.extend(sorted(p.glob("*.patch")))
        else:
            files.append(p)
    if not files:
        raise PatchError("no .patch files")
    return files


def build_overlay(
    patch_paths: list[Path],
    *,
    overlay_root: Path,
    field_root: Path,
    text_root: Path,
) -> dict[str, list[str]]:
    """Apply patches in name order. Same-map patches stack."""
    files = collect_patch_files(patch_paths)
    patches = [parse_patch_text(p.read_text(encoding="utf-8"), source=str(p)) for p in files]
    by_map: dict[str, list[FieldPatch]] = {}
    for patch in patches:
        by_map.setdefault(patch.map_stem, []).append(patch)

    written: dict[str, list[str]] = {}
    for stem, group in by_map.items():
        merged: FieldPatch | None = None
        for patch in group:
            if merged is None:
                merged = FieldPatch(map_stem=stem)
            merged.table1.extend(patch.table1)
            merged.table2.extend(patch.table2)
            merged.scripts.extend(patch.scripts)
        assert merged is not None
        result = apply_patch(merged, field_root=field_root, text_root=text_root)
        paths = write_overlay(result, overlay_root)
        written[stem] = list(paths.values())
    return written


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Compile field assembler patches to a fopen overlay")
    sub = ap.add_subparsers(dest="cmd", required=True)
    b = sub.add_parser("build", help="compile .patch files into overlay FIELD/ + TEXT/EN/")
    b.add_argument("paths", nargs="+", type=Path, help=".patch file or directory")
    b.add_argument("-o", "--overlay", type=Path, default=DEFAULT_OVERLAY_DIR)
    b.add_argument("--field", type=Path, default=DEFAULT_CONTENT)
    b.add_argument("--text", type=Path, default=DEFAULT_TEXT)
    args = ap.parse_args(argv)
    if args.cmd == "build":
        try:
            written = build_overlay(
                args.paths,
                overlay_root=args.overlay,
                field_root=args.field,
                text_root=args.text,
            )
        except (PatchError, AsmError) as exc:
            print(f"error: {exc}", file=sys.stderr)
            return 1
        if not any(written.values()):
            print("no files written (patches were no-ops)")
            return 0
        for stem, paths in written.items():
            print(f"{stem}:")
            for p in paths:
                print(f"  {p}")
        print(f"overlay root {args.overlay}")
        print("Serve with GrandiaFieldPatch (injector --overlay, overlay/ next to the DLL, or GRANDIA_FIELD_PATCH).")
        return 0
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
