#!/usr/bin/env python3
"""Assemble scripts/*.asm to <name>.bin for embedding (mod build time)."""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

_STEM_ID = re.compile(
    r"^(?P<stem>[A-Za-z0-9]+)_(?:0x)?(?P<id>[0-9A-Fa-f]+)$",
    re.IGNORECASE,
)
_NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]*$")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--in", dest="src", type=Path, required=True, help="folder of .asm files")
    ap.add_argument("--out", dest="dest", type=Path, required=True, help="folder for .bin files")
    ap.add_argument("--grandipelago", type=Path, required=True, help="Grandipelago repo root")
    ap.add_argument("--python", default=sys.executable)
    args = ap.parse_args()
    asm_py = args.grandipelago / "tools" / "field_script_asm.py"
    if not asm_py.is_file():
        print(f"field_script_asm.py not found: {asm_py}", file=sys.stderr)
        return 1
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
        cmd = [args.python, str(asm_py), "--emit", str(src), "-o", str(out)]
        m = _STEM_ID.match(name)
        if m:
            cmd.insert(2, m.group("stem"))
        proc = subprocess.run(cmd, cwd=str(args.grandipelago), capture_output=True, text=True)
        if proc.returncode != 0:
            err = (proc.stderr or proc.stdout or "").strip()
            print(f"assemble {src.name} failed: {err}", file=sys.stderr)
            return proc.returncode or 1
        if proc.stdout:
            print(proc.stdout.strip())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
