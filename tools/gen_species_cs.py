#!/usr/bin/env python3
"""Generate sdk/Species.cs from HD Remaster monster_full_* names."""
from __future__ import annotations

import re
from collections import Counter
from pathlib import Path

STRINGS = Path(
    r"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\TEXT\EN\strings.txt"
)
OUT = Path(r"C:\Users\User\Projects\GrandiaFieldPatch\sdk\Species.cs")


def ident(name: str, i: int) -> str:
    parts = re.findall(r"[A-Za-z0-9]+", name.replace("'", ""))
    s = "".join(p[:1].upper() + p[1:] for p in parts)
    if not s:
        s = f"Id{i}"
    if s[0].isdigit():
        s = "Id" + s
    if s == "Skip":
        s = f"Skip{i}"
    return s


def main() -> None:
    rows: list[tuple[int, str]] = []
    for line in STRINGS.read_text(encoding="utf-8", errors="replace").splitlines():
        m = re.match(r"^monster_full_(\d+)=\\3(.*)$", line)
        if not m:
            continue
        rows.append((int(m.group(1)), m.group(2).strip()))

    counts = Counter(ident(n, i) for i, n in rows)
    used: set[str] = set()
    items: list[tuple[str, int, str]] = []
    for i, name in rows:
        base = ident(name, i)
        s = base
        if counts[base] > 1 or s in used:
            s = f"{base}{i}"
        used.add(s)
        items.append((s, i, name))

    lines = [
        "namespace Grandia.Sdk;",
        "",
        "/// <summary>",
        "/// M_DAT form-row / <c>monster_full_N</c> in TEXT/EN/strings.txt.",
        "/// Same id <see cref=\"BattleLoadEvent.SetEnemies\"/> uses as <c>species</c>.",
        "/// </summary>",
        "public enum Species",
        "{",
        "    None = 0,",
    ]
    for s, i, name in items:
        comment = name.replace("&", "&amp;")
        lines.append(f"    /// <summary>{comment} (<c>0x{i:02X}</c>).</summary>")
        lines.append(f"    {s} = {i},")
    lines.append("}")
    lines.append("")
    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"wrote {OUT} n={len(items)}")
    for want in (34, 75, 105, 57):
        print(next(x for x in items if x[1] == want))


if __name__ == "__main__":
    main()
