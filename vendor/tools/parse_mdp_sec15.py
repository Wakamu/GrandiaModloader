"""Parse MDP sec[15] camera-path bytecode.

Directory: self-relative u32 offsets, count = first/4.
Each chunk is a script for +0x5B770 (esi = [0x719934] IP).
Integers are big-endian. 5EAD0 sentinels:

  80000000  tag -2  inherit live
  7FFFFFFF  tag -1
  7FFFFFFE  tag -3  use player/entity axis
  7FFFFFFD  tag -4  use op-0x15 snapshot
  else      16.16 (or angle; 0x1680000 = 360deg)

Channel ops 8/9/A/B/17/18/19/1A call +0x5E070 with edx = 0..7 (tween mode).
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

SENTINELS = {
    0x80000000: "inherit",
    0x7FFFFFFF: "unset",
    0x7FFFFFFE: "player",
    0x7FFFFFFD: "snapshot",
}

# edx passed to +0x5E070
CHANNEL_EDX = {
    0x08: 0,
    0x09: 1,
    0x0A: 2,
    0x0B: 3,
    0x17: 4,
    0x18: 5,
    0x19: 6,
    0x1A: 7,
}

OP_NAMES = {
    0x01: "set_pos",       # 71931c/320/324
    0x02: "set_delta",     # 719328/32c/330
    0x03: "set_rot",       # 719300/304/308
    0x04: "set_fov",       # 71930c/314/318
    0x05: "set_p28",       # 719334
    0x06: "add_pos",
    0x07: "add_rot",
    0x08: "ch_mode0",
    0x09: "ch_mode1",
    0x0A: "ch_mode2",
    0x0B: "ch_mode3",
    0x0C: "table_s32",
    0x0D: "slot_meta",
    0x0F: "wait",          # yield; BE u16 frames
    0x10: "set_63fa5b",
    0x11: "set_63faa2",
    0x12: "set_71a640",
    0x13: "wait_b",        # yield; duration -> 6c2c10
    0x14: "yield_tween",   # 1 byte; 6c2c08=1
    0x15: "save_cam",      # flags bit0 pos, bit1 delta, bit2 rot
    0x16: "set_words",     # 5x BE u16
    0x17: "ch_mode4",
    0x18: "ch_mode5",
    0x19: "ch_mode6",
    0x1A: "ch_mode7",
    0x1B: "call_70400",
    0x1C: "skip3",
    0x1D: "reset_cam",
    0xFF: "end",
}

CHANNELS = {
    0: "pos_x",
    1: "pos_y",
    2: "pos_z",
    3: "delta_x",
    4: "delta_y",
    5: "delta_z",
    6: "yaw",
    7: "pitch",
    8: "roll",
    9: "fov_a",
    10: "p28",
    11: "fov_b",
}


def be_u32(b: bytes, o: int) -> int:
    return struct.unpack_from(">I", b, o)[0]


def be_i32(b: bytes, o: int) -> int:
    return struct.unpack_from(">i", b, o)[0]


def fmt_val(u: int) -> str:
    name = SENTINELS.get(u)
    if name:
        return name
    s = struct.unpack(">i", struct.pack(">I", u & 0xFFFFFFFF))[0]
    return f"{s / 65536:.4g}"


def channel_payload_size(edx: int) -> int:
    """Bytes after the channel-id byte (+0x5E5D0)."""
    n = 8  # two BE s32
    n += 4 if (edx & 3) in (0, 2) else 2
    n += 3 if (edx & 4) else 2
    return n


def parse_chunk(ch: bytes) -> tuple[list[str], int, str | None]:
    """Return (events, pos, error). pos==len means fully consumed (plus 0-pad)."""
    ev: list[str] = []
    pos = 0
    n = len(ch)
    while pos < n:
        op = ch[pos]
        if op == 0x00:
            # padding after end, or leading empty
            if all(b == 0 for b in ch[pos:]):
                return ev, n, None
            return ev, pos, f"unexpected 0x00 at {pos}"
        name = OP_NAMES.get(op, f"op{op:#x}")
        if op == 0xFF:
            ev.append(f"@{pos} end")
            pos += 1
            while pos < n and ch[pos] == 0:
                pos += 1
            if pos != n:
                return ev, pos, f"trailing junk after end: {ch[pos:pos+8].hex()}"
            return ev, pos, None
        if op in (0x01, 0x02, 0x03, 0x04, 0x06, 0x07):
            if pos + 13 > n:
                return ev, pos, f"{name} truncated"
            a, b, c = (be_u32(ch, pos + 1 + i * 4) for i in range(3))
            ev.append(f"@{pos} {name} {fmt_val(a)} {fmt_val(b)} {fmt_val(c)}")
            pos += 13
            continue
        if op == 0x05:
            if pos + 5 > n:
                return ev, pos, "set_p28 truncated"
            ev.append(f"@{pos} set_p28 {fmt_val(be_u32(ch, pos + 1))}")
            pos += 5
            continue
        if op in CHANNEL_EDX:
            edx = CHANNEL_EDX[op]
            need = 2 + channel_payload_size(edx)
            if pos + need > n:
                return ev, pos, f"{name} truncated need={need}"
            ch_id = ch[pos + 1]
            p = pos + 2
            v0, v1 = be_u32(ch, p), be_u32(ch, p + 4)
            p += 8
            extra = ""
            if (edx & 3) in (0, 2):
                extra = f" v2={fmt_val(be_u32(ch, p))}"
                p += 4
            else:
                dur = (ch[p] << 8) | ch[p + 1]
                extra = f" dur={dur}"
                p += 2
            foot_n = 3 if (edx & 4) else 2
            foot = ch[p : p + foot_n].hex()
            p += foot_n
            chn = CHANNELS.get(ch_id, f"ch{ch_id}")
            ev.append(f"@{pos} {name} {chn} {fmt_val(v0)} {fmt_val(v1)}{extra} foot={foot}")
            pos = p
            continue
        if op == 0x0C:
            if pos + 3 > n:
                return ev, pos, "table_s32 truncated hdr"
            idx, cnt = ch[pos + 1], ch[pos + 2]
            pos += 3
            if pos + cnt * 4 > n:
                return ev, pos, f"table_s32 truncated n={cnt}"
            vals = [fmt_val(be_u32(ch, pos + i * 4)) for i in range(cnt)]
            ev.append(f"@{pos - 3} table_s32 idx={idx} {vals}")
            pos += cnt * 4
            continue
        if op == 0x0D:
            if pos + 7 > n:
                return ev, pos, "slot_meta truncated"
            ch_id, scale, d_hi, d_lo, m_hi, m_lo = (ch[pos + i] for i in range(1, 7))
            dur = (d_hi << 8) | d_lo
            mark = (m_hi << 8) | m_lo
            ev.append(
                f"@{pos} slot_meta ch={ch_id} scale={scale}% dur={dur} mark={mark:#06x}"
            )
            pos += 7
            continue
        if op == 0x0F:
            if pos + 3 > n:
                return ev, pos, "wait truncated"
            frames = (ch[pos + 1] << 8) | ch[pos + 2]
            ev.append(f"@{pos} wait {frames}")
            pos += 3
            continue
        if op in (0x10, 0x11, 0x12):
            if pos + 2 > n:
                return ev, pos, f"{name} truncated"
            ev.append(f"@{pos} {name} {ch[pos + 1]}")
            pos += 2
            continue
        if op == 0x13:
            if pos + 3 > n:
                return ev, pos, "wait_b truncated"
            a, b = ch[pos + 1], ch[pos + 2]
            ev.append(f"@{pos} wait_b {a:#x},{b:#x}")
            pos += 3
            continue
        if op == 0x14:
            ev.append(f"@{pos} yield_tween")
            pos += 1
            continue
        if op == 0x15:
            if pos + 2 > n:
                return ev, pos, "save_cam truncated"
            fl = ch[pos + 1]
            bits = []
            if fl & 1:
                bits.append("pos")
            if fl & 2:
                bits.append("delta")
            if fl & 4:
                bits.append("rot")
            ev.append(f"@{pos} save_cam {fl:#x} ({','.join(bits) or 'none'})")
            pos += 2
            continue
        if op == 0x16:
            if pos + 11 > n:
                return ev, pos, "set_words truncated"
            words = [
                (ch[pos + 1 + i * 2] << 8) | ch[pos + 2 + i * 2] for i in range(5)
            ]
            ev.append(f"@{pos} set_words {words}")
            pos += 11
            continue
        if op == 0x1B:
            if pos + 2 > n:
                return ev, pos, "call_70400 truncated"
            ev.append(f"@{pos} call_70400 {ch[pos + 1]}")
            pos += 2
            continue
        if op == 0x1C:
            if pos + 3 > n:
                return ev, pos, "skip3 truncated"
            ev.append(f"@{pos} skip3 {ch[pos : pos + 3].hex()}")
            pos += 3
            continue
        if op == 0x1D:
            ev.append(f"@{pos} reset_cam")
            pos += 1
            while pos < n and ch[pos] == 0:
                pos += 1
            if pos != n:
                return ev, pos, f"trailing junk after reset_cam: {ch[pos:pos+8].hex()}"
            return ev, pos, None
        return ev, pos, f"unknown op {op:#x}"
    return ev, pos, None


def iter_chunks(blob: bytes) -> list[bytes]:
    if len(blob) < 4:
        return []
    first = struct.unpack_from("<I", blob, 0)[0]
    if first < 4 or first % 4 or first > len(blob):
        return []
    n = first // 4
    offs = [struct.unpack_from("<I", blob, i * 4)[0] for i in range(n)]
    out = []
    for i, o in enumerate(offs):
        end = offs[i + 1] if i + 1 < n else len(blob)
        out.append(blob[o:end])
    return out


def export_map(mid: str) -> None:
    blob = get_section(find_mdp(find_field_dir(), mid).read_bytes(), 15)
    if not blob:
        print(f"{mid}: no sec[15]")
        return
    cs = iter_chunks(blob)
    print(f"\n======== {mid} sec[15] len={len(blob)} chunks={len(cs)} ========")
    dest = OUT / mid / "huff"
    dest.mkdir(parents=True, exist_ok=True)
    lines = [f"# {mid} sec[15] camera bytecode  n={len(cs)}"]
    for i, ch in enumerate(cs):
        if not ch or ch[:4] == b"\xff\x00\x00\x00" or (ch[0] == 0xFF and all(b == 0 for b in ch[1:])):
            print(f"  [{i:2}] empty")
            lines.append(f"[{i}] empty")
            continue
        ev, pos, err = parse_chunk(ch)
        ok = err is None and pos == len(ch)
        print(f"  [{i:2}] len={len(ch):4} ok={ok} ops={len(ev)}" + (f" ERR {err}" if err else ""))
        for e in ev[:14]:
            print(f"       {e}")
        if len(ev) > 14:
            print(f"       ... {len(ev) - 14} more")
        lines.append(f"[{i}] len={len(ch)} ok={ok}" + (f" {err}" if err else ""))
        lines.extend(f"  {e}" for e in ev)
    (dest / "sec15_chunks.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"  wrote {dest / 'sec15_chunks.txt'}")


def census() -> None:
    field = find_field_dir()
    ok = fail = empty = 0
    ops = Counter()
    first_op = Counter()
    fails: list[str] = []
    seen: set[str] = set()
    for p in field.glob("*.mdp"):
        k = p.name.lower()
        if k in seen:
            continue
        seen.add(k)
        b = get_section(p.read_bytes(), 15)
        if not b:
            continue
        for ch in iter_chunks(b):
            if not ch or ch[0] == 0xFF:
                empty += 1
                continue
            first_op[ch[0]] += 1
            ev, pos, err = parse_chunk(ch)
            for e in ev:
                # "@N name ..."
                parts = e.split()
                if len(parts) >= 2:
                    ops[parts[1]] += 1
            if err is None and pos == len(ch):
                ok += 1
            else:
                fail += 1
                if len(fails) < 12:
                    fails.append(f"  {p.stem} len={len(ch)} pos={pos}/{len(ch)} {err} head={ch[:16].hex()}")
    print(f"\ncensus ok={ok} fail={fail} empty={empty}")
    print("first opcode", first_op.most_common(12))
    print("ops", ops.most_common(20))
    for f in fails:
        print(f)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("maps", nargs="*", default=["2000", "2410"])
    ap.add_argument("--census", action="store_true")
    args = ap.parse_args()
    for mid in args.maps:
        try:
            export_map(mid)
        except FileNotFoundError as e:
            print(e)
    if args.census or not args.maps:
        census()


if __name__ == "__main__":
    main()
