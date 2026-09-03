#!/usr/bin/env python3
"""Generate sdk/Item.cs from Grandipelago data/item_names.json."""
from __future__ import annotations

import json
import re
from pathlib import Path

NAMES = Path(r"C:\Users\User\Projects\Grandipelago\Grandipelago\data\item_names.json")
OUT = Path(r"C:\Users\User\Projects\GrandiaFieldPatch\sdk\Item.cs")
SKIP = {"", "---", "Prohibited", "Use Prohibited"}


def ident(name: str, i: int) -> str:
    parts = re.findall(r"[A-Za-z0-9]+", name.replace("'", "").replace("\u2019", ""))
    s = "".join(p[:1].upper() + p[1:] for p in parts)
    if not s:
        s = f"Id{i}"
    if s[0].isdigit():
        s = "Plus" + s if name.lstrip().startswith("+") else "Id" + s
    return s


def main() -> None:
    raw = json.loads(NAMES.read_text(encoding="utf-8"))
    rows: list[tuple[int, str]] = []
    for key, label in raw.items():
        try:
            item_id = int(key)
        except (TypeError, ValueError):
            continue
        if not isinstance(label, str) or label.strip() in SKIP:
            continue
        rows.append((item_id, label.strip()))
    rows.sort()

    used: set[str] = set()
    items: list[tuple[str, int, str]] = []
    for i, name in rows:
        base = ident(name, i)
        s = base if base not in used else f"{base}{i}"
        used.add(s)
        items.append((s, i, name))

    lines = [
        "namespace Grandia.Sdk;",
        "",
        "/// <summary>",
        "/// Vanilla item ids (WINDT / stash / enemy drops). Herbs is 346.",
        "/// Same numbers <see cref=\"GameStash\"/> and <see cref=\"EnemyDrop.Item\"/> use.",
        "/// </summary>",
        "public enum Item",
        "{",
        "    None = 0,",
    ]
    for s, i, name in items:
        comment = name.replace("&", "&amp;")
        lines.append(f"    /// <summary>{comment} (<c>{i}</c>).</summary>")
        lines.append(f"    {s} = {i},")
    lines.append("}")
    lines.append("")
    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"wrote {OUT} n={len(items)}")
    for want in (346, 354, 395, 31):
        print(next(x for x in items if x[1] == want))


if __name__ == "__main__":
    main()
