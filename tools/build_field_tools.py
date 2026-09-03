#!/usr/bin/env python3
"""Freeze vendor field assembler into vendor/field_tools/field_tools.exe."""
from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
VENDOR = REPO / "vendor"
ENTRY = REPO / "tools" / "field_tools.py"
OUT_DIR = VENDOR / "field_tools"
WORK = REPO / "vendor" / "_pyinstaller"


def main() -> int:
    if not (VENDOR / "tools" / "field_patch.py").is_file():
        print("vendor/tools missing; run tools/pack_field_tools.py first", file=sys.stderr)
        return 1
    data = VENDOR / "data" / "item_names.json"
    if not data.is_file():
        print(f"missing {data}", file=sys.stderr)
        return 1

    if WORK.exists():
        shutil.rmtree(WORK)
    WORK.mkdir(parents=True)
    if OUT_DIR.exists():
        shutil.rmtree(OUT_DIR)

    sep = ";" if sys.platform == "win32" else ":"
    hidden = []
    for py in sorted((VENDOR / "tools").glob("*.py")):
        hidden.extend(["--hidden-import", py.stem])

    cmd = [
        sys.executable, "-m", "PyInstaller",
        "--noconfirm",
        "--clean",
        "--onedir",
        "--console",
        "--name", "field_tools",
        "--paths", str(VENDOR / "tools"),
        "--add-data", f"{data}{sep}data",
        *hidden,
        "--exclude-module", "PIL",
        "--exclude-module", "tkinter",
        "--exclude-module", "matplotlib",
        "--exclude-module", "numpy",
        "--distpath", str(VENDOR),
        "--workpath", str(WORK),
        "--specpath", str(WORK),
        str(ENTRY),
    ]
    print(" ".join(cmd))
    proc = subprocess.run(cmd, cwd=str(REPO))
    if proc.returncode != 0:
        return proc.returncode
    exe = OUT_DIR / "field_tools.exe"
    if not exe.is_file():
        print(f"PyInstaller did not write {exe}", file=sys.stderr)
        return 1
    shutil.rmtree(WORK, ignore_errors=True)
    print(f"wrote {exe}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
