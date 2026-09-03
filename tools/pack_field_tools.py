#!/usr/bin/env python3
"""Copy the field assembler Python closure into vendor/ for a standalone install.

The runtime only needs field_patch / field_script_asm / field_hook_asm and
their local imports, plus data/item_names.json. Source is the Grandipelago
repo when present; otherwise vendor/ is left as-is.
"""
from __future__ import annotations

import ast
import shutil
import sys
from pathlib import Path

ENTRY = ("field_patch.py", "field_script_asm.py", "field_hook_asm.py")
DATA_FILES = ("item_names.json",)

# stdlib / third-party; not copied from Grandipelago/tools.
SKIP = {
    "argparse", "collections", "dataclasses", "functools", "json", "math",
    "pathlib", "re", "struct", "sys", "typing", "unicodedata", "difflib",
    "PIL", "Image",
}


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def find_grandipelago() -> Path | None:
    here = repo_root()
    candidates = [
        here.parent / "Grandipelago" / "Grandipelago",
        here.parent / "Grandipelago",
        Path(r"C:\Users\User\Projects\Grandipelago\Grandipelago"),
    ]
    for c in candidates:
        if (c / "tools" / "field_patch.py").is_file():
            return c
    return None


def local_imports(py: Path) -> set[str]:
    tree = ast.parse(py.read_text(encoding="utf-8"), filename=str(py))
    names: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.ImportFrom) and node.module and not node.level:
            names.add(node.module.split(".", 1)[0])
        elif isinstance(node, ast.Import):
            for alias in node.names:
                names.add(alias.name.split(".", 1)[0])
    return {n for n in names if n not in SKIP}


def collect(tools_dir: Path) -> list[str]:
    seen: set[str] = set()
    queue = list(ENTRY)
    while queue:
        name = queue.pop()
        if name in seen:
            continue
        src = tools_dir / name
        if not src.is_file():
            raise FileNotFoundError(f"missing tool: {src}")
        seen.add(name)
        for mod in local_imports(src):
            candidate = f"{mod}.py"
            if (tools_dir / candidate).is_file() and candidate not in seen:
                queue.append(candidate)
    return sorted(seen)


def copy_tree(src_root: Path, dest: Path, files: list[str]) -> None:
    tools_dest = dest / "tools"
    if tools_dest.exists():
        shutil.rmtree(tools_dest)
    tools_dest.mkdir(parents=True)
    for name in files:
        shutil.copy2(src_root / "tools" / name, tools_dest / name)
    data_dest = dest / "data"
    data_dest.mkdir(parents=True, exist_ok=True)
    for name in DATA_FILES:
        src = src_root / "data" / name
        if src.is_file():
            shutil.copy2(src, data_dest / name)
    patch_optional_pillow(tools_dest / "decode_mdp_huffman.py")


def patch_optional_pillow(path: Path) -> None:
    """field_hook_xref pulls this in; only image export needs Pillow."""
    if not path.is_file():
        return
    text = path.read_text(encoding="utf-8")
    old = "from PIL import Image"
    new = "try:\n    from PIL import Image\nexcept ImportError:\n    Image = None  # type: ignore"
    if old in text and "except ImportError" not in text:
        path.write_text(text.replace(old, new, 1), encoding="utf-8")


def main() -> int:
    dest = repo_root() / "vendor"
    src = find_grandipelago()
    if src is None:
        if (dest / "tools" / "field_patch.py").is_file():
            print(f"Grandipelago not found; keeping {dest}")
            return 0
        print("Grandipelago repo not found and vendor/ is empty.", file=sys.stderr)
        return 1

    files = collect(src / "tools")
    dest.mkdir(parents=True, exist_ok=True)
    copy_tree(src, dest, files)
    print(f"packed {len(files)} tools + data from {src} -> {dest}")
    for name in files:
        print(f"  tools/{name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
