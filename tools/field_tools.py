#!/usr/bin/env python3
"""CLI for the packed field assembler. Frozen as field_tools.exe.

    field_tools patch build FILE.patch -o OVERLAY --field FIELD --text TEXT
    field_tools script [STEM] --emit FILE.asm -o FILE.bin
    field_tools script STEM --dump FILE.json --field FIELD --text TEXT
    field_tools hook --emit FILE.asm -o FILE.bin
    field_tools embed --in scripts --out scripts
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


def _meipass() -> Path | None:
    raw = getattr(sys, "_MEIPASS", None)
    return Path(raw) if raw else None


def _vendor_tools() -> Path:
    frozen = _meipass()
    if frozen is not None:
        return frozen
    return Path(__file__).resolve().parents[1] / "vendor" / "tools"


def _prepare() -> None:
    tools = _vendor_tools()
    sys.path.insert(0, str(tools))
    import field_script_asm_names as names

    root = _meipass()
    if root is None:
        root = tools.parent
    names._REPO = root
    names._ITEM_NAMES_PATH = root / "data" / "item_names.json"


_STEM_ID = re.compile(
    r"^(?P<stem>[A-Za-z0-9]+)_(?:0x)?(?P<id>[0-9A-Fa-f]+)$",
    re.IGNORECASE,
)
_NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]*$")


def _embed(argv: list[str]) -> int:
    from field_script_asm import main as script_main

    ap = argparse.ArgumentParser(prog="field_tools embed")
    ap.add_argument("--in", dest="src", type=Path, required=True)
    ap.add_argument("--out", dest="dest", type=Path, required=True)
    args = ap.parse_args(argv)
    if not args.src.is_dir():
        print(f"no scripts folder: {args.src}")
        return 0
    files = sorted(args.src.rglob("*.asm"))
    if not files:
        print("no .asm files")
        return 0
    args.dest.mkdir(parents=True, exist_ok=True)
    for src in files:
        name = src.stem
        if not _NAME.match(name):
            print(f"skip {src.name}: use a simple file name (hub_save.asm)", file=sys.stderr)
            return 1
        out = args.dest / f"{name}.bin"
        if out.is_file() and out.stat().st_mtime >= src.stat().st_mtime:
            print(f"up-to-date {out.name}")
            continue
        cmd = ["--emit", str(src), "-o", str(out)]
        m = _STEM_ID.match(name)
        if m:
            cmd.insert(0, m.group("stem"))
        rc = script_main(cmd)
        if rc:
            print(f"assemble {src.name} failed", file=sys.stderr)
            return rc
        print(f"assembled {out.name}")
    return 0


def main(argv: list[str] | None = None) -> int:
    _prepare()
    args = list(sys.argv[1:] if argv is None else argv)
    if not args or args[0] in ("-h", "--help"):
        print(__doc__.strip(), file=sys.stderr)
        return 0
    cmd, rest = args[0], args[1:]
    if cmd == "patch":
        from field_patch import main as patch_main
        return patch_main(rest)
    if cmd == "script":
        from field_script_asm import main as script_main
        return script_main(rest)
    if cmd == "hook":
        from field_hook_asm import main as hook_main
        return hook_main(rest)
    if cmd == "embed":
        return _embed(rest)
    print(f"unknown command {cmd}", file=sys.stderr)
    print(__doc__.strip(), file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
