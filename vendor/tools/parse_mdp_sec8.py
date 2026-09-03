"""Parse MDP sec[8] field-instance table (48-byte records).

Header: u16 packed_count (N = word & 0x3FFF; 0x4000 set), then N × 48-byte
records, 2 bytes pad. Copied as 0x800 words (4 KiB) at +0x61140 onto [0x63FA4C].
Runtime [0x71992C] = that buffer + 2 (first record). Gated on sec[8] global
[0x719700] != -1.

Record (consumers +0x897D0, +0x562A1, +0x89655, +0x89D76):
  u16 +0     packed id = (kind << 8) | sub_id (sub_id often 0 on kind-2 NPC rows)
  u8  +1     kind nibble (low 4). Spawn jump table +0x89BC4 handles kinds 0-9
             at +0x89840; kind 2 also special at +0x562A5. See docs/mdp_format.md.
  u8  +2     flags. High nibble -> 71AA40[+1] at +0x89658 (0x80 typical).
             Low nibble often 0; 0x0D cluster on some maps.
  u8  +3     talk id (sec[30] +2..+5 lookup via +0x897D0)
  s16 +4+6+8 xyz  (copied to 71AA40 slot at +0x8965D). NOT sec[30] actor xyz.
  u16 +A     CLUT GPU word (Parm ~0x50xx -> VRAM y=320; 0 on Marna kind-2)
  u16 +10    runtime flags (masked 0xF0 at +0x5A977; merged from spawn path)
  s16 +12+14+16+18  absolute XZ AABB (compared at +0x89D76). All-zero common.
  s16 +1A/+1C       inner XZ pad (+/-4 vs entity pos at +0x8A26D). Usually 0.
"""
from __future__ import annotations

import argparse
import struct
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from mdp_lib import find_field_dir, find_mdp, get_section  # noqa: E402

REPO = Path(__file__).resolve().parents[1]
OUT = REPO / "data" / "mdp_extract"
STRIDE = 48

KIND_NAMES = {
    0: "prop",
    1: "type6",
    2: "npc/cam",
    3: "enable0",
    4: "enable1",
    5: "party",
    6: "party_zone",
    7: "party_z2",
    8: "timer",
    9: "actor",
}


def u16(b: bytes, o: int) -> int:
    return struct.unpack_from("<H", b, o)[0]


def s16(b: bytes, o: int) -> int:
    return struct.unpack_from("<h", b, o)[0]


def parse_records(blob: bytes) -> tuple[int, int, list[bytes]]:
    if not blob or len(blob) < 2:
        return 0, 0, []
    packed = u16(blob, 0)
    n = packed & 0x3FFF
    recs = []
    o = 2
    for _ in range(n):
        if o + STRIDE > len(blob):
            break
        recs.append(blob[o : o + STRIDE])
        o += STRIDE
    return packed, n, recs


def export_map(mid: str) -> None:
    blob = get_section(find_mdp(find_field_dir(), mid).read_bytes(), 8)
    if not blob:
        print(f"{mid}: no sec[8]")
        return
    packed, n, recs = parse_records(blob)
    pad = len(blob) - (2 + len(recs) * STRIDE)
    kinds = Counter(r[1] & 0xF for r in recs)
    print(
        f"\n======== {mid} sec[8] len={len(blob)} packed={packed:#06x} "
        f"count={n} parsed={len(recs)} pad={pad} kinds={kinds.most_common()} ========"
    )
    dest = OUT / mid / "huff"
    dest.mkdir(parents=True, exist_ok=True)
    lines = [
        f"# {mid} sec[8] packed={packed:#06x} n={n}",
        "idx kind sub talk      x     y     z   +2  +A     +10   xzbox      +1A/+1C",
    ]
    for i, rec in enumerate(recs):
        kind = rec[1] & 0xF
        kname = KIND_NAMES.get(kind, "?")
        sub = u16(rec, 0) & 0xFF
        talk = rec[3]
        x, y, z = s16(rec, 4), s16(rec, 6), s16(rec, 8)
        box = (s16(rec, 0x12), s16(rec, 0x14), s16(rec, 0x16), s16(rec, 0x18))
        pad = (s16(rec, 0x1A), s16(rec, 0x1C))
        print(
            f"  inst[{i:2}] kind={kind}({kname}) sub={sub:3} talk={talk:3} pos=({x:5},{y:5},{z:5}) "
            f"+2={rec[2]:#04x} +A={u16(rec,0xA):#06x} "
            f"+10={u16(rec,0x10):#06x} xz={box} pad={pad}"
        )
        lines.append(
            f"{i:3} {kind:4} {sub:3} {talk:4} {x:6} {y:6} {z:6} "
            f"{rec[2]:02x} {u16(rec,0xA):04x} {u16(rec,0x10):04x} {box} {pad}"
        )
    (dest / "sec8_instances.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"  wrote {dest / 'sec8_instances.txt'}")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("maps", nargs="*", default=["2000", "2410", "204C"])
    args = ap.parse_args()
    for mid in args.maps:
        try:
            export_map(mid)
        except FileNotFoundError as e:
            print(e)


if __name__ == "__main__":
    main()
