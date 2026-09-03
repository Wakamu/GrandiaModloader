#!/usr/bin/env python3
"""1:1 assembler text for MDP sec[7] table-1 (32-byte zones) and table-2
(20-byte ``call_hook`` rows).

Unmodified tables must satisfy:

    parse(format(rows)) == rows

Pretty mnemonics are used only when a builder rebuilds the exact row.
Everything else is ``hex=``. Not a quest DSL.
"""
from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

from field_script_asm import AsmError, parse_hex_bytes, parse_int
from field_script_asm_names import format_item, load_asm_names, resolve_named
from field_script_hooks import (
    CUTSCENE_SUBTYPES,
    KNOWN_HOOK_NOTES,
    PARTY_ACTOR_SUBTYPES,
    SYS_LATCH_SUBTYPES,
    VISIBILITY_SUBTYPES,
    HookRow,
    build_anim_latch_row,
    build_cutscene_row,
    build_hook_fanout_row,
    build_present_channel_row,
    build_setup_row,
    build_sys_latch_row,
    build_typed_row,
    decode_hook_payload,
    explain_decoded,
    load_hook_bundle,
    _s8,
)
from field_hook_xref import MapXref, _kind_short, load_map_xref, sibling_index, xref_comment


def emit_table2(rows: list[bytes]) -> bytes:
    """Concatenated table-2 bytes (identity target)."""
    out = bytearray()
    for raw in rows:
        if len(raw) != 20:
            raise AsmError(f"table-2 row length {len(raw)} != 20")
        out.extend(raw)
    return bytes(out)


def table2_rows_from_bundle(bundle) -> list[bytes]:
    return [bytes(r.raw) for r in bundle.rows if r.table_index == 2]


def table1_rows_from_bundle(bundle) -> list[bytes]:
    return [bytes(r.raw) for r in bundle.rows if r.table_index == 1]


def _fanout_packed_children(decoded: dict) -> list[int] | None:
    kids = decoded.get("children") or []
    slots = [0] * 8
    for child in kids:
        bit = int(child.get("bit") or 0)
        if not 0 <= bit <= 7:
            return None
        slots[bit] = int(child.get("hookId") or 0)
    last = -1
    for i, hid in enumerate(slots):
        if hid:
            last = i
    if last < 0:
        return []
    if any(slots[i] == 0 for i in range(last)):
        return None
    return slots[: last + 1]


def _cutscene_name(subtype: int) -> str:
    name = CUTSCENE_SUBTYPES.get(subtype)
    if name in ("open_amap", "open_amap2"):
        return name
    if name and name != "nop" and not name.startswith("sub_"):
        return f"cutscene {name}"
    return f"cutscene {subtype}"


def _be_u16(raw: bytes, off: int) -> int:
    return (raw[off] << 8) | raw[off + 1]


def _plain(text: str) -> str:
    return (
        text.replace("\u2014", "-")
        .replace("\u2013", "-")
        .replace("\u2192", "->")
    )


def _comment_for(
    raw: bytes,
    map_stem: str,
    *,
    xref=None,
    siblings: dict[int, bytes] | None = None,
) -> str:
    decoded = decode_hook_payload(raw[1] & 0x3F, raw, row_size=20)
    xtext = xref_comment(
        decoded,
        xref if xref is not None else load_map_xref(map_stem),
        siblings=siblings,
    )
    base = explain_decoded(decoded)
    replace_kinds = {
        "camera_path",
        "attach_vis",
        "attach_anim",
        "attach_pos",
        "field_bind",
        "fx_pos",
        "zone",
        "hook_fanout",
        "flag_wait",
    }
    if xtext and decoded.get("kind") in replace_kinds:
        text = xtext
        delay = decoded.get("delayTicks") or decoded.get("delayCap")
        if delay and f"{delay} frames" not in text:
            text = f"{text}; after {delay} ticks"
    elif xtext:
        text = f"{base} {xtext}"
    else:
        text = base
    follow = decoded.get("followHookId")
    if follow:
        raw_f = (siblings or {}).get(int(follow))
        if raw_f is not None:
            tag = f"then hook {follow} ({_kind_short(raw_f)})"
        else:
            tag = f"then hook {follow}"
        if f"hook {follow}" not in text:
            text = f"{text.rstrip('.')} {tag}."
    note = KNOWN_HOOK_NOTES.get((map_stem.upper(), raw[0]), "")
    if note:
        first = note.split(". ")[0].rstrip(".") + "."
        if first not in text:
            text = f"{text.rstrip('. ')}. {first}"
    text = _plain(" ".join(text.split()))
    if len(text) > 220:
        text = text[:217] + "..."
    return text


def _omit_kv(parts: list[str], key: str, value: int, *, default: int = 0, hex_width: int | None = None) -> None:
    if value == default:
        return
    if hex_width is not None:
        parts.append(f"{key}=0x{value:0{hex_width}X}")
    else:
        parts.append(f"{key}={value}")


def _name_or_id(table: dict[int, str], key: int) -> str:
    name = table.get(key)
    if not name:
        return str(key)
    if sum(1 for v in table.values() if v == name) > 1:
        return str(key)
    return name


def _inv_unique(table: dict[int, str]) -> dict[str, int]:
    counts: dict[str, int] = {}
    for v in table.values():
        counts[v] = counts.get(v, 0) + 1
    return {v: k for k, v in table.items() if counts[v] == 1}


def _fmt_layout(
    hid: int,
    kind: str,
    words: list[str],
    *,
    flags: int | None = None,
    delay: int = 0,
    follow: int = 0,
    extra: list[str] | None = None,
    hi: int = 0,
) -> str:
    parts = [f"hook {hid}", kind, *words]
    if extra:
        parts.extend(extra)
    if flags is not None:
        parts.append(f"flags=0x{flags:02X}")
    if delay:
        parts.append(f"delay={delay}")
    if follow:
        parts.append(f"follow={follow}")
    if hi:
        parts.append(f"hi={hi}")
    return " ".join(parts)


def _layout_pretty(raw: bytes, decoded: dict) -> str | None:
    hid = raw[0]
    handler = raw[1] & 0x3F
    kind = decoded.get("kind")
    specs: dict[str, tuple[int, tuple[int, ...]]] = {
        "cam_word": (0x00, (4, 5, 6, 7, 8, 0x10, 0x13)),
        "sys_latch": (0x1E, (4, 5, 6, 0x10, 0x13)),
        "param_block": (0x15, (4, 5, 6, 7, 8, 9, 10, 0xB, 0x10, 0x13)),
        "visibility": (0x18, (4, 5, 6, 0xF, 0x10, 0x13)),
        "camera_path": (0x0D, (4, 5, 6, 0x10, 0x13)),
        "scene_boot": (0x0E, (4, 5, 0x10, 0x13)),
        "unit_bind": (0x14, (4, 5, 6, 7, 8, 9, 0x10, 0x13)),
        "sfx_fx": (0x09, (4, 5, 6, 0x10, 0x12, 0x13)),
        "zone": (0x07, (4, 5, 6, 0xF, 0x10, 0x11, 0x13)),
        "attach_vis": (0x06, (4, 5, 6, 0xF, 0x10, 0x13)),
        "attach_anim": (0x11, (4, 5, 6, 7, 8, 9, 0xA, 0xB, 0xD, 0xF, 0x10, 0x12, 0x13)),
        "flag_wait": (0x0C, (4, 5, 6, 7, 8, 0xF, 0x10, 0x13)),
        "field_bind": (0x04, (4, 5, 6, 7, 8, 9, 0xB, 0xF, 0x10, 0x11, 0x12, 0x13)),
        "attach_pos": (0x03, (4, 5, 6, 7, 8, 9, 0xA, 0xF, 0x10, 0x11, 0x12, 0x13)),
        "fx_pos": (0x08, (4, 5, 6, 7, 8, 9, 0xA, 0xF, 0x10, 0x11, 0x12, 0x13)),
        "cam_nudge": (0x0B, (4, 5, 6, 7, 8, 9, 0xA, 0xF, 0x10, 0x13)),
        "party_actor": (0x1D, (4, 5, 6, 7, 8, 9, 0xA, 0xB, 0x10, 0x11, 0x13)),
        "party_state": (0x1B, (4, 5, 6, 7, 8, 9, 0xA, 0xB, 0xF, 0x10, 0x11, 0x12, 0x13)),
    }
    spec = specs.get(kind or "")
    if spec is None:
        return None
    want_handler, offs = spec
    if handler != want_handler:
        return None
    rebuilt = build_typed_row(
        hid, handler, {off: raw[off] for off in offs}, flags1=raw[1] >> 6,
    )
    if rebuilt != raw:
        return None
    flags = raw[4]
    delay = raw[0x10]
    follow = raw[0x13]
    hi = raw[1] >> 6
    extra: list[str] = []
    words: list[str] = []
    flags_emit: int | None = flags if flags else None
    if kind == "cam_word":
        words.append(f"0x{_be_u16(raw, 5):04X}")
        _omit_kv(extra, "hold", raw[7])
        _omit_kv(extra, "poll", raw[8])
    elif kind == "sys_latch":
        sub = flags & 0xF
        words.append(_name_or_id(SYS_LATCH_SUBTYPES, sub))
        _omit_kv(extra, "param", raw[5])
        _omit_kv(extra, "value", raw[6])
        flags_emit = None if flags == (0x40 | sub) else flags
        return _fmt_layout(hid, "sys_latch", words, flags=flags_emit, delay=delay, follow=follow, extra=extra, hi=hi)
    elif kind == "param_block":
        words.extend(f"0x{_be_u16(raw, off):04X}" for off in (5, 7, 9))
        _omit_kv(extra, "mode", raw[0xB])
    elif kind == "visibility":
        words.append(_name_or_id(VISIBILITY_SUBTYPES, raw[5]))
        words.append(str(raw[6]))
        _omit_kv(extra, "hold", raw[0xF])
    elif kind == "camera_path":
        words.append(str(raw[5]))
        _omit_kv(extra, "stream", raw[6])
    elif kind == "scene_boot":
        words.append(f"0x{raw[5]:02X}")
    elif kind == "unit_bind":
        words.append(str(raw[5]))
        words.append(str(raw[6]))
        _omit_kv(extra, "aux", raw[7])
        _omit_kv(extra, "unk89", _be_u16(raw, 8), hex_width=4)
    elif kind == "sfx_fx":
        words.append(str(raw[5]))
        _omit_kv(extra, "op", raw[6])
        _omit_kv(extra, "late", raw[0x12])
    elif kind == "zone":
        words.append(str(raw[5]))
        words.append(str(raw[6]))
        _omit_kv(extra, "hold", raw[0xF])
        _omit_kv(extra, "poll", raw[0x11])
    elif kind == "attach_vis":
        words.append(str(raw[5]))
        _omit_kv(extra, "single", raw[6])
        _omit_kv(extra, "hold", raw[0xF])
    elif kind == "attach_anim":
        words.append(str(raw[5]))
        words.append(str(raw[6]))
        _omit_kv(extra, "start", (raw[8] << 8) | raw[9])
        _omit_kv(extra, "end", (raw[0xA] << 8) | raw[0x12])
        _omit_kv(extra, "step", raw[0xB])
        _omit_kv(extra, "interp", raw[0xD])
        _omit_kv(extra, "hold", raw[0xF])
        _omit_kv(extra, "latch", raw[7], hex_width=2)
    elif kind == "flag_wait":
        words.append(str(raw[5]))
        words.append(str(_be_u16(raw, 6)))
        if flags & 0x20:
            _omit_kv(extra, "stream", raw[8])
        else:
            _omit_kv(extra, "expect", raw[8])
        _omit_kv(extra, "hold", raw[0xF])
    elif kind == "field_bind":
        words.append(str(raw[5]))
        words.append(str(raw[6]))
        extra.append(f"delta={_s8(raw[7])},{_s8(raw[8])},{_s8(raw[9])}")
        _omit_kv(extra, "step", raw[0xB])
        _omit_kv(extra, "hold", raw[0xF])
        _omit_kv(extra, "mid", raw[0x11])
        _omit_kv(extra, "late", raw[0x12])
    elif kind in ("attach_pos", "fx_pos"):
        words.append(str(raw[5]))
        words.append(str(raw[6]))
        extra.append(f"delta={_s8(raw[7])},{_s8(raw[8])},{_s8(raw[9])}")
        _omit_kv(extra, "step", raw[0xA])
        _omit_kv(extra, "hold", raw[0xF])
        _omit_kv(extra, "mid", raw[0x11])
        _omit_kv(extra, "late", raw[0x12])
    elif kind == "cam_nudge":
        extra.append("delta=" + ",".join(str(_s8(raw[i])) for i in range(5, 0xB)))
        _omit_kv(extra, "hold", raw[0xF])
    elif kind == "party_actor":
        sub = flags & 0xF
        words.append(_name_or_id(PARTY_ACTOR_SUBTYPES, sub))
        _omit_kv(extra, "char", raw[5], default=0)
        _omit_kv(extra, "p0", raw[6])
        _omit_kv(extra, "p1", raw[7])
        _omit_kv(extra, "p2", raw[8])
        _omit_kv(extra, "p3", raw[9])
        _omit_kv(extra, "p4", raw[0xA])
        _omit_kv(extra, "p5", raw[0xB])
        _omit_kv(extra, "p6", raw[0x11])
        flags_emit = None if flags == (0x40 | sub) else flags
    elif kind == "party_state":
        words.append(str(flags & 0xF))
        start = _be_u16(raw, 5)
        duration = (raw[0x12] << 8) | raw[0x10]
        _omit_kv(extra, "start", start, hex_width=4)
        _omit_kv(extra, "duration", duration)
        _omit_kv(extra, "p7", raw[7])
        _omit_kv(extra, "p8", raw[8])
        _omit_kv(extra, "p9", raw[9])
        _omit_kv(extra, "pA", raw[0xA])
        _omit_kv(extra, "pB", raw[0xB])
        _omit_kv(extra, "hold", raw[0xF])
        _omit_kv(extra, "mid", raw[0x11])
        delay = 0
        flags_emit = None if (flags & 0xF0) == 0 else flags
    else:
        return None
    return _fmt_layout(hid, kind, words, flags=flags_emit, delay=delay, follow=follow, extra=extra, hi=hi)


def _try_pretty(raw: bytes) -> str | None:
    if len(raw) != 20:
        return None
    hid = raw[0]
    handler = raw[1] & 0x3F
    flags1 = raw[1] >> 6
    decoded = decode_hook_payload(handler, raw, row_size=20)
    kind = decoded.get("kind")
    rebuilt: bytes | None = None
    parts: list[str] = [f"hook {hid}"]

    if kind == "present_channel":
        flags = int(decoded.get("rowFlags") or 0)
        delay = int(decoded.get("delayCap") or 0)
        follow = int(decoded.get("followHookId") or 0)
        ch = int(decoded.get("channel") or 0)
        rebuilt = build_present_channel_row(
            hid, channel=ch, delay_cap=delay, follow_hook_id=follow, row_flags=flags,
            flags1=flags1,
        )
        parts += ["present_channel", str(ch)]
        if flags:
            parts.append(f"flags=0x{flags:02X}")
        if delay:
            parts.append(f"delay={delay}")
        if follow:
            parts.append(f"follow={follow}")
    elif kind == "hook_fanout":
        kids = _fanout_packed_children(decoded)
        if kids is None:
            return None
        flags = int(decoded.get("rowFlags") or 0)
        delay = int(decoded.get("delayTicks") or 0)
        modes = int(decoded.get("modeBits") or 0)
        rebuilt = build_hook_fanout_row(
            hid, kids, delay_ticks=delay, mode_bits=modes, row_flags=flags,
            flags1=flags1,
        )
        parts.append("fanout")
        parts.extend(str(k) for k in kids)
        if flags != 0x90:
            parts.append(f"flags=0x{flags:02X}")
        if delay:
            parts.append(f"delay={delay}")
        if modes:
            parts.append(f"modes=0x{modes:02X}")
    elif kind == "setup":
        flags = int(decoded.get("rowFlags") or 0)
        dest = int(decoded.get("pair0") or 0)
        spawn = int(decoded.get("pair1") or 0)
        aux9 = int(decoded.get("aux9") or 0)
        aux_a = int(decoded.get("auxA") or 0)
        delay = int(decoded.get("delayTicks") or 0)
        walk_x = int(decoded.get("walkX") or 0)
        walk_z = int(decoded.get("walkZ") or 0)
        rebuilt = build_setup_row(
            hid, pair0=dest, pair1=spawn, row_flags=flags,
            aux9=aux9, aux_a=aux_a, delay_ticks=delay,
            walk_x=walk_x, walk_z=walk_z, flags1=flags1,
            gate_flag1=int(decoded.get("gateFlag1") or 0),
            gate_flag2=int(decoded.get("gateFlag2") or 0),
        )
        rebuilt = bytearray(rebuilt)
        rebuilt[2] = raw[2]
        rebuilt = bytes(rebuilt)
        parts += ["setup", f"dest=0x{dest:04X}"]
        if spawn:
            parts.append(f"spawn={spawn}")
        if flags != 0x40:
            parts.append(f"flags=0x{flags:02X}")
        if aux9:
            parts.append(f"aux9={aux9}")
        if aux_a:
            parts.append(f"auxA={aux_a}")
        if delay:
            parts.append(f"delay={delay}")
        if walk_x or walk_z:
            parts.append(f"walk={walk_x},{walk_z}")
        mode = int(decoded.get("gateMode") or 0)
        f1 = int(decoded.get("gateFlag1") or 0)
        f2 = int(decoded.get("gateFlag2") or 0)
        pol = int(decoded.get("gatePolarity") or 0)
        if mode == 1 and f1 and not f2:
            parts.append(f"{'if_set' if pol else 'if_clear'}=0x{f1:X}")
        elif mode == 5 and f1 and pol:
            parts.append(f"if_set=0x{f1:X}")
            parts.append(f"if_clear=0x{f2:X}")
        elif mode == 5 and f1:
            parts.append(f"gate=5 a=0x{f1:X} b=0x{f2:X}")
        elif mode:
            parts.append(f"gate={mode}")
            if f1:
                parts.append(f"a=0x{f1:X}")
            if f2:
                parts.append(f"b=0x{f2:X}")
        else:
            if f1:
                parts.append(f"a=0x{f1:X}")
            if f2:
                parts.append(f"b=0x{f2:X}")
        if raw[2]:
            parts.append(f"trig=0x{raw[2]:02X}")
    elif kind == "anim_latch":
        flags = int(decoded.get("rowFlags") or 0)
        mode = int(decoded.get("latchMode") or 0)
        anim = int(decoded.get("animId") or 0)
        unit = int(decoded.get("unitKey") or 0)
        delay = int(decoded.get("delayTicks") or 0)
        follow = int(decoded.get("followHookId") or 0)
        rebuilt = build_anim_latch_row(
            hid, anim_id=anim, unit_key=unit, latch_mode=mode,
            delay_ticks=delay, follow_hook_id=follow, row_flags=flags,
            slot_result=int(decoded.get("slotResult") or 0),
            flags1=flags1,
        )
        parts += ["anim", str(anim)]
        if unit:
            parts.append(f"unit={unit}")
        if mode != 1:
            parts.append(f"mode={mode}")
        slot = int(decoded.get("slotResult") or 0)
        if slot:
            parts.append(f"slot={slot}")
        if flags != 0x90:
            parts.append(f"flags=0x{flags:02X}")
        if delay:
            parts.append(f"delay={delay}")
        if follow:
            parts.append(f"follow={follow}")
    elif kind == "cutscene":
        flags = int(decoded.get("rowFlags") or 0)
        subtype = int(decoded.get("subtype") or 0)
        param = int(decoded.get("param") or 0)
        args = tuple(int(x) for x in (decoded.get("args") or [0, 0, 0, 0])[:4])
        delay = int(decoded.get("delayTicks") or 0)
        follow = int(decoded.get("followHookId") or 0)
        if any(args):
            return None
        rebuilt = build_cutscene_row(
            hid, subtype=subtype, param=param, args=args,
            row_flags=flags, delay_ticks=delay, follow_hook_id=follow,
            flags1=flags1,
        )
        name = _cutscene_name(subtype)
        parts.append(name)
        if subtype in (4, 6):
            if param:
                parts.append(f"node={param}")
        elif param:
            parts.append(f"param={param}")
        if flags != subtype:
            parts.append(f"flags=0x{flags:02X}")
        if delay:
            parts.append(f"delay={delay}")
        if follow:
            parts.append(f"follow={follow}")
    else:
        return _layout_pretty(raw, decoded)

    if rebuilt != raw:
        return None
    if flags1:
        parts.append(f"hi={flags1}")
    return " ".join(parts)


def format_table2(
    rows: list[bytes] | list[HookRow],
    *,
    map_stem: str,
    resolve_xref: bool = True,
) -> str:
    """Print this map's table-2 rows as assembler text."""
    raw_rows: list[bytes] = []
    for item in rows:
        raw = item.raw if isinstance(item, HookRow) else bytes(item)
        if len(raw) != 20:
            raise AsmError(f"table-2 row length {len(raw)} != 20")
        raw_rows.append(raw)
    lines = [f"hooks {map_stem.upper()}", "table 2", ""]
    xref = load_map_xref(map_stem) if resolve_xref else MapXref()
    siblings = sibling_index(raw_rows)
    for raw in raw_rows:
        comment = _comment_for(raw, map_stem, xref=xref, siblings=siblings)
        if comment:
            lines.append(f"# {comment}")
        pretty = _try_pretty(raw)
        if pretty is not None:
            lines.append(pretty)
        else:
            lines.append(f"hook {raw[0]} hex={raw.hex()}")
    lines.append("")
    return "\n".join(lines)


def _aabb_tuple(raw: bytes) -> tuple[int, int, int, int, int, int]:
    return struct.unpack_from("<hhhhhh", raw, 20)


def _fmt_aabb(raw: bytes) -> str:
    return ",".join(str(v) for v in _aabb_tuple(raw))


def _parse_aabb(text: str) -> bytes:
    vals = _parse_csv_ints(text, 6, what="aabb")
    out = bytearray()
    for v in vals:
        if not -32768 <= v <= 32767:
            raise AsmError(f"aabb value {v} out of s16")
        out += struct.pack("<h", v)
    return bytes(out)


def _aabb_comment(raw: bytes) -> str:
    x_min, y_max, z_max, x_max, y_min, z_min = _aabb_tuple(raw)
    return f"AABB XZ ({x_min},{z_min})-({x_max},{z_max}) Y {y_min}..{y_max}"


def _is_chest(raw: bytes) -> bool:
    return len(raw) >= 2 and struct.unpack_from("<H", raw, 0)[0] == 0x9300


def _chest_event(raw: bytes) -> int:
    return ((0x0A + raw[8]) << 8) | raw[9]


def _try_pretty_chest(raw: bytes, *, map_stem: str) -> str | None:
    if len(raw) != 32 or not _is_chest(raw):
        return None
    hid = raw[0]
    fields: dict[int, int] = {
        5: raw[5],
        6: raw[6],
        8: raw[8],
        9: raw[9],
        10: raw[10],
        11: raw[11],
    }
    if raw[2]:
        fields[2] = raw[2]
    if raw[18]:
        fields[18] = raw[18]
    if raw[19]:
        fields[19] = raw[19]
    rebuilt = build_typed_row(hid, 0x13, fields, flags1=raw[1] >> 6) + raw[20:32]
    if rebuilt != raw:
        return None
    names = load_asm_names(map_stem)
    parts = [f"zone {hid}", "chest", f"event=0x{_chest_event(raw):04X}"]
    parts.append(f"item={format_item((raw[10] << 8) | raw[11], names)}")
    if raw[5] != 2:
        parts.append(f"open={raw[5]}")
    parts.append(f"kind={raw[6]}")
    if raw[18]:
        parts.append(f"t18={raw[18]}")
    if raw[19]:
        parts.append(f"t19={raw[19]}")
    if raw[2]:
        parts.append(f"trig=0x{raw[2]:02X}")
    parts.append(f"aabb={_fmt_aabb(raw)}")
    hi = raw[1] >> 6
    if hi:
        parts.append(f"hi={hi}")
    return " ".join(parts)


def _try_pretty_zone(raw: bytes, *, map_stem: str) -> str | None:
    if len(raw) != 32:
        return None
    chest = _try_pretty_chest(raw, map_stem=map_stem)
    if chest is not None:
        if _parse_zone_line(chest.split()) != raw:
            return None
        return chest
    pretty = _try_pretty(raw[:20])
    trig = raw[2]
    if pretty is None:
        head = bytearray(raw[:20])
        head[2] = 0
        pretty = _try_pretty(bytes(head))
    if pretty is None:
        return None
    if not pretty.startswith("hook "):
        return None
    line = "zone" + pretty[4:]
    if trig and "trig=" not in line:
        line += f" trig=0x{trig:02X}"
    line += f" aabb={_fmt_aabb(raw)}"
    rebuilt = _parse_zone_line(line.split())
    if rebuilt != raw:
        return None
    return line


def _comment_for_zone(
    raw: bytes,
    map_stem: str,
    *,
    xref=None,
) -> str:
    if _is_chest(raw):
        names = load_asm_names(map_stem)
        item = (raw[10] << 8) | raw[11]
        label = format_item(item, names)
        text = f"chest event 0x{_chest_event(raw):04X} item {label}. {_aabb_comment(raw)}."
        return _plain(" ".join(text.split()))[:220]
    text = _comment_for(raw[:20], map_stem, xref=xref, siblings=None)
    extra = _aabb_comment(raw)
    if extra not in text:
        text = f"{text.rstrip('. ')}. {extra}."
    text = _plain(" ".join(text.split()))
    if len(text) > 220:
        text = text[:217] + "..."
    return text


def format_table1(
    rows: list[bytes] | list[HookRow],
    *,
    map_stem: str,
    resolve_xref: bool = True,
) -> str:
    """Print this map's table-1 rows as assembler text."""
    raw_rows: list[bytes] = []
    for item in rows:
        raw = item.raw if isinstance(item, HookRow) else bytes(item)
        if len(raw) != 32:
            raise AsmError(f"table-1 row length {len(raw)} != 32")
        raw_rows.append(raw)
    lines = [f"hooks {map_stem.upper()}", "table 1", ""]
    xref = load_map_xref(map_stem) if resolve_xref else MapXref()
    for raw in raw_rows:
        comment = _comment_for_zone(raw, map_stem, xref=xref)
        if comment:
            lines.append(f"# {comment}")
        pretty = _try_pretty_zone(raw, map_stem=map_stem)
        if pretty is not None:
            lines.append(pretty)
        else:
            lines.append(f"zone {raw[0]} hex={raw.hex()}")
    lines.append("")
    return "\n".join(lines)


def format_hook_asm(
    table1: list[bytes] | list[HookRow],
    table2: list[bytes] | list[HookRow],
    *,
    map_stem: str,
    resolve_xref: bool = True,
) -> str:
    """Table-1 then table-2. Omits an empty table-1 block."""
    t1 = [item.raw if isinstance(item, HookRow) else bytes(item) for item in table1]
    t2 = [item.raw if isinstance(item, HookRow) else bytes(item) for item in table2]
    if not t1:
        return format_table2(t2, map_stem=map_stem, resolve_xref=resolve_xref)
    body1 = format_table1(t1, map_stem=map_stem, resolve_xref=resolve_xref)
    body2 = format_table2(t2, map_stem=map_stem, resolve_xref=resolve_xref)
    lines = body1.rstrip().splitlines()
    # drop the trailing blank from table-1; table-2 brings its own header
    t2_lines = body2.splitlines()
    if t2_lines[:1] == [f"hooks {map_stem.upper()}"]:
        t2_lines = t2_lines[1:]
    while t2_lines and not t2_lines[0].strip():
        t2_lines = t2_lines[1:]
    lines.append("")
    lines.extend(t2_lines)
    if not lines[-1:]:
        lines.append("")
    text = "\n".join(lines)
    if not text.endswith("\n"):
        text += "\n"
    return text


def _without_comment(line: str) -> str:
    if "#" in line:
        line = line.split("#", 1)[0]
    return line.strip()


def _split_tokens(rest: list[str]) -> tuple[list[str], dict[str, str]]:
    pos: list[str] = []
    kv: dict[str, str] = {}
    for tok in rest:
        if "=" in tok:
            key, _, val = tok.partition("=")
            kv[key] = val
        else:
            pos.append(tok)
    return pos, kv


def _u8(n: int) -> int:
    if n < 0:
        n &= 0xFF
    if not 0 <= n <= 255:
        raise AsmError(f"byte {n} out of 0..255")
    return n


def _put_be16(fields: dict[int, int], off: int, value: int) -> None:
    if not 0 <= value <= 0xFFFF:
        raise AsmError(f"u16 {value} out of range")
    fields[off] = (value >> 8) & 0xFF
    fields[off + 1] = value & 0xFF


def _parse_csv_ints(text: str, count: int, *, what: str) -> list[int]:
    bits = [b.strip() for b in text.split(",")]
    if len(bits) != count:
        raise AsmError(f"{what} needs {count} comma-separated values")
    return [parse_int(b) for b in bits]


def _lookup_named(table: dict[int, str], token: str) -> int:
    inv = _inv_unique(table)
    if token in inv:
        return inv[token]
    return parse_int(token)


_LAYOUT_HANDLERS: dict[str, int] = {
    "cam_word": 0x00,
    "sys_latch": 0x1E,
    "param_block": 0x15,
    "visibility": 0x18,
    "camera_path": 0x0D,
    "scene_boot": 0x0E,
    "unit_bind": 0x14,
    "sfx_fx": 0x09,
    "zone": 0x07,
    "attach_vis": 0x06,
    "attach_anim": 0x11,
    "flag_wait": 0x0C,
    "field_bind": 0x04,
    "attach_pos": 0x03,
    "fx_pos": 0x08,
    "cam_nudge": 0x0B,
    "party_actor": 0x1D,
    "party_state": 0x1B,
}


def _parse_layout(hid: int, kind: str, args: list[str], kv: dict[str, str], *, flags: int | None, delay: int, follow: int, flags1: int = 0) -> bytes:
    handler = _LAYOUT_HANDLERS[kind]
    fields: dict[int, int] = {}
    if kind != "party_state" and delay:
        fields[0x10] = _u8(delay)
    if follow:
        fields[0x13] = _u8(follow)

    def pos(i: int, default: int = 0) -> int:
        return parse_int(args[i]) if len(args) > i else default

    if kind == "cam_word":
        _put_be16(fields, 5, pos(0))
        if "hold" in kv:
            fields[7] = _u8(parse_int(kv["hold"]))
        if "poll" in kv:
            fields[8] = _u8(parse_int(kv["poll"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "sys_latch":
        if not args:
            raise AsmError("sys_latch needs a subtype")
        sub = _lookup_named(SYS_LATCH_SUBTYPES, args[0]) & 0xF
        return build_sys_latch_row(
            hid, subtype=sub,
            param=parse_int(kv["param"]) if "param" in kv else 0,
            value=parse_int(kv["value"]) if "value" in kv else 0,
            row_flags=flags,
            delay_ticks=delay, follow_hook_id=follow,
            flags1=flags1,
        )
    elif kind == "param_block":
        _put_be16(fields, 5, pos(0))
        _put_be16(fields, 7, pos(1))
        _put_be16(fields, 9, pos(2))
        if "mode" in kv:
            fields[0xB] = _u8(parse_int(kv["mode"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "visibility":
        if not args:
            raise AsmError("visibility needs a subtype")
        fields[5] = _u8(_lookup_named(VISIBILITY_SUBTYPES, args[0]))
        fields[6] = _u8(pos(1))
        if "hold" in kv:
            fields[0xF] = _u8(parse_int(kv["hold"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "camera_path":
        fields[5] = _u8(pos(0))
        if "stream" in kv:
            fields[6] = _u8(parse_int(kv["stream"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "scene_boot":
        fields[5] = _u8(pos(0))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "unit_bind":
        fields[5] = _u8(pos(0))
        fields[6] = _u8(pos(1))
        if "aux" in kv:
            fields[7] = _u8(parse_int(kv["aux"]))
        if "unk89" in kv:
            _put_be16(fields, 8, parse_int(kv["unk89"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "sfx_fx":
        fields[5] = _u8(pos(0))
        if "op" in kv:
            fields[6] = _u8(parse_int(kv["op"]))
        if "late" in kv:
            fields[0x12] = _u8(parse_int(kv["late"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "zone":
        fields[5] = _u8(pos(0))
        fields[6] = _u8(pos(1))
        if "hold" in kv:
            fields[0xF] = _u8(parse_int(kv["hold"]))
        if "poll" in kv:
            fields[0x11] = _u8(parse_int(kv["poll"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "attach_vis":
        fields[5] = _u8(pos(0))
        if "single" in kv:
            fields[6] = _u8(parse_int(kv["single"]))
        if "hold" in kv:
            fields[0xF] = _u8(parse_int(kv["hold"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "attach_anim":
        fields[5] = _u8(pos(0))
        fields[6] = _u8(pos(1))
        if "start" in kv:
            _put_be16(fields, 8, parse_int(kv["start"]))
        if "end" in kv:
            end = parse_int(kv["end"])
            if not 0 <= end <= 0xFFFF:
                raise AsmError(f"end {end} out of range")
            fields[0xA] = (end >> 8) & 0xFF
            fields[0x12] = end & 0xFF
        if "step" in kv:
            fields[0xB] = _u8(parse_int(kv["step"]))
        if "interp" in kv:
            fields[0xD] = _u8(parse_int(kv["interp"]))
        if "hold" in kv:
            fields[0xF] = _u8(parse_int(kv["hold"]))
        if "latch" in kv:
            fields[7] = _u8(parse_int(kv["latch"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "flag_wait":
        fields[5] = _u8(pos(0))
        _put_be16(fields, 6, pos(1))
        if "expect" in kv:
            fields[8] = _u8(parse_int(kv["expect"]))
        if "stream" in kv:
            fields[8] = _u8(parse_int(kv["stream"]))
        if "hold" in kv:
            fields[0xF] = _u8(parse_int(kv["hold"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind in ("field_bind", "attach_pos", "fx_pos"):
        fields[5] = _u8(pos(0))
        fields[6] = _u8(pos(1))
        if "delta" in kv:
            d7, d8, d9 = _parse_csv_ints(kv["delta"], 3, what="delta")
            fields[7] = _u8(d7)
            fields[8] = _u8(d8)
            fields[9] = _u8(d9)
        step_off = 0xB if kind == "field_bind" else 0xA
        if "step" in kv:
            fields[step_off] = _u8(parse_int(kv["step"]))
        if "hold" in kv:
            fields[0xF] = _u8(parse_int(kv["hold"]))
        if "mid" in kv:
            fields[0x11] = _u8(parse_int(kv["mid"]))
        if "late" in kv:
            fields[0x12] = _u8(parse_int(kv["late"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "cam_nudge":
        if "delta" in kv:
            vals = _parse_csv_ints(kv["delta"], 6, what="cam_nudge delta")
            for i, val in enumerate(vals):
                fields[5 + i] = _u8(val)
        if "hold" in kv:
            fields[0xF] = _u8(parse_int(kv["hold"]))
        if flags is not None:
            fields[4] = _u8(flags)
    elif kind == "party_actor":
        if not args:
            raise AsmError("party_actor needs a subtype")
        sub = _lookup_named(PARTY_ACTOR_SUBTYPES, args[0]) & 0xF
        fields[4] = _u8(flags if flags is not None else (0x40 | sub))
        if "char" in kv:
            fields[5] = _u8(parse_int(kv["char"]))
        for key, off in (("p0", 6), ("p1", 7), ("p2", 8), ("p3", 9), ("p4", 0xA), ("p5", 0xB), ("p6", 0x11)):
            if key in kv:
                fields[off] = _u8(parse_int(kv[key]))
    elif kind == "party_state":
        sub = pos(0) & 0xF
        fields[4] = _u8(flags if flags is not None else sub)
        if "start" in kv:
            _put_be16(fields, 5, parse_int(kv["start"]))
        if "duration" in kv:
            dur = parse_int(kv["duration"])
            if not 0 <= dur <= 0xFFFF:
                raise AsmError(f"duration {dur} out of range")
            fields[0x10] = dur & 0xFF
            fields[0x12] = (dur >> 8) & 0xFF
        if "p8" in kv:
            fields[8] = _u8(parse_int(kv["p8"]))
        if "p7" in kv:
            fields[7] = _u8(parse_int(kv["p7"]))
        if "p9" in kv:
            fields[9] = _u8(parse_int(kv["p9"]))
        if "pA" in kv:
            fields[0xA] = _u8(parse_int(kv["pA"]))
        if "pB" in kv:
            fields[0xB] = _u8(parse_int(kv["pB"]))
        if "hold" in kv:
            fields[0xF] = _u8(parse_int(kv["hold"]))
        if "mid" in kv:
            fields[0x11] = _u8(parse_int(kv["mid"]))
    else:
        raise AsmError(f"unknown hook kind {kind!r} (use hex= for raw rows)")
    return build_typed_row(hid, handler, fields, flags1=flags1)


def _parse_hook_line(tokens: list[str]) -> bytes:
    if len(tokens) < 2:
        raise AsmError("hook line needs an id")
    hid = parse_int(tokens[1])
    if not 0 <= hid <= 0x7FFF:
        raise AsmError(f"hook id {hid} out of 0..0x7FFF")
    hid &= 0xFF
    pos, kv = _split_tokens(tokens[2:])
    hex_s = kv.get("hex")
    if hex_s is not None or (pos and pos[0] == "raw" and "hex" in kv):
        raw = parse_hex_bytes(kv["hex"])
        if len(raw) != 20:
            raise AsmError(f"hex= must be 20 bytes, got {len(raw)}")
        if raw[0] != hid:
            raise AsmError(f"hook {hid} does not match hex id {raw[0]}")
        return raw

    kind = pos[0] if pos else ""
    args = pos[1:]
    flags = parse_int(kv["flags"]) if "flags" in kv else None
    delay = parse_int(kv["delay"]) if "delay" in kv else 0
    follow = parse_int(kv["follow"]) if "follow" in kv else 0
    flags1 = parse_int(kv["hi"]) if "hi" in kv else 0

    if kind == "present_channel":
        ch = parse_int(args[0]) if args else 0
        return build_present_channel_row(
            hid, channel=ch, delay_cap=delay, follow_hook_id=follow,
            row_flags=0 if flags is None else flags,
            flags1=flags1,
        )
    if kind == "fanout":
        kids = [parse_int(a) for a in args]
        modes = parse_int(kv["modes"]) if "modes" in kv else 0
        return build_hook_fanout_row(
            hid, kids, delay_ticks=delay, mode_bits=modes,
            row_flags=0x90 if flags is None else flags,
            flags1=flags1,
        )
    if kind == "setup":
        dest = parse_int(kv["dest"]) if "dest" in kv else 0
        spawn = parse_int(kv["spawn"]) if "spawn" in kv else 0
        aux9 = parse_int(kv["aux9"]) if "aux9" in kv else 0
        aux_a = parse_int(kv["auxA"]) if "auxA" in kv else 0
        walk_x = walk_z = 0
        if "walk" in kv:
            bits = kv["walk"].split(",")
            if len(bits) != 2:
                raise AsmError("setup walk= needs x,z")
            walk_x, walk_z = parse_int(bits[0]), parse_int(bits[1])
        flag1 = parse_int(kv["a"]) if "a" in kv else 0
        flag2 = parse_int(kv["b"]) if "b" in kv else 0
        mode5_and = "if_clear" in kv and "if_set" in kv
        if mode5_and:
            flag1 = parse_int(kv["if_set"])
            flag2 = parse_int(kv["if_clear"])
        elif "if_clear" in kv:
            flag1 = parse_int(kv["if_clear"])
            flag2 = 0
        elif "if_set" in kv:
            flag1 = parse_int(kv["if_set"])
            flag2 = 0
        row = bytearray(build_setup_row(
            hid, pair0=dest, pair1=spawn,
            row_flags=0x40 if flags is None else flags,
            aux9=aux9, aux_a=aux_a, delay_ticks=delay,
            walk_x=walk_x, walk_z=walk_z, follow_hook_id=follow,
            flags1=flags1,
            gate_flag1=flag1, gate_flag2=flag2,
        ))
        if "trig" in kv:
            row[2] = _u8(parse_int(kv["trig"]))
        if mode5_and:
            row[2] = (row[2] & 0xF0) | 0x0D
        elif "if_set" in kv:
            row[2] = (row[2] & 0xF0) | 0x09
        elif "if_clear" in kv:
            row[2] = (row[2] & 0xF0) | 0x01
        elif "gate" in kv:
            row[2] = (row[2] & 0xF8) | (parse_int(kv["gate"]) & 7)
        return bytes(row)
    if kind == "anim":
        anim = parse_int(args[0]) if args else 0
        unit = parse_int(kv["unit"]) if "unit" in kv else 0
        mode = parse_int(kv["mode"]) if "mode" in kv else 1
        slot = parse_int(kv["slot"]) if "slot" in kv else 0
        return build_anim_latch_row(
            hid, anim_id=anim, unit_key=unit, latch_mode=mode,
            delay_ticks=delay, follow_hook_id=follow,
            slot_result=slot,
            row_flags=0x90 if flags is None else flags,
            flags1=flags1,
        )
    if kind in ("open_amap", "open_amap2") or kind == "cutscene":
        if kind == "open_amap":
            subtype = 4
            param = parse_int(kv["node"]) if "node" in kv else (parse_int(args[0]) if args else 0)
        elif kind == "open_amap2":
            subtype = 6
            param = parse_int(kv["node"]) if "node" in kv else (parse_int(args[0]) if args else 0)
        else:
            if not args:
                raise AsmError("cutscene needs a subtype")
            name = args[0]
            inverse = {v: k for k, v in CUTSCENE_SUBTYPES.items()}
            subtype = inverse[name] if name in inverse else parse_int(name)
            param = parse_int(kv["param"]) if "param" in kv else (parse_int(args[1]) if len(args) > 1 else 0)
        return build_cutscene_row(
            hid, subtype=subtype, param=param,
            row_flags=subtype if flags is None else flags,
            delay_ticks=delay, follow_hook_id=follow,
            flags1=flags1,
        )
    if kind in _LAYOUT_HANDLERS:
        return _parse_layout(hid, kind, args, kv, flags=flags, delay=delay, follow=follow, flags1=flags1)
    raise AsmError(f"unknown hook kind {kind!r} (use hex= for raw rows)")


def _parse_chest_zone(hid: int, kv: dict[str, str], *, flags1: int, trig: int) -> bytes:
    if "event" not in kv:
        raise AsmError("chest needs event=")
    if "item" not in kv:
        raise AsmError("chest needs item=")
    if "aabb" not in kv:
        raise AsmError("chest needs aabb=")
    event = parse_int(kv["event"])
    names = load_asm_names("")
    item = resolve_named(kv["item"], names.item_name_to_id, parse_int)
    if not 0 <= item <= 0xFFFF:
        raise AsmError(f"item {item} out of range")
    kind = parse_int(kv["kind"]) if "kind" in kv else 0
    open_type = parse_int(kv["open"]) if "open" in kv else 2
    t18 = parse_int(kv["t18"]) if "t18" in kv else 0
    t19 = parse_int(kv["t19"]) if "t19" in kv else 0
    bank = ((event >> 8) - 0x0A) & 0xFF
    fields: dict[int, int] = {
        5: _u8(open_type),
        6: _u8(kind),
        8: _u8(bank),
        9: _u8(event & 0xFF),
        10: (item >> 8) & 0xFF,
        11: item & 0xFF,
    }
    if trig:
        fields[2] = _u8(trig)
    if t18:
        fields[18] = _u8(t18)
    if t19:
        fields[19] = _u8(t19)
    head = build_typed_row(hid, 0x13, fields, flags1=flags1)
    return head + _parse_aabb(kv["aabb"])


def _parse_zone_line(tokens: list[str]) -> bytes:
    if len(tokens) < 2:
        raise AsmError("zone line needs an id")
    hid = parse_int(tokens[1])
    if not 0 <= hid <= 255:
        raise AsmError(f"zone id {hid} out of 0..255")
    pos, kv = _split_tokens(tokens[2:])
    hex_s = kv.get("hex")
    if hex_s is not None or (pos and pos[0] == "raw" and "hex" in kv):
        raw = parse_hex_bytes(kv["hex"])
        if len(raw) != 32:
            raise AsmError(f"hex= must be 32 bytes, got {len(raw)}")
        if raw[0] != hid:
            raise AsmError(f"zone {hid} does not match hex id {raw[0]}")
        return raw
    trig = parse_int(kv["trig"]) if "trig" in kv else 0
    flags1 = parse_int(kv["hi"]) if "hi" in kv else 0
    kind = pos[0] if pos else ""
    if kind == "chest":
        return _parse_chest_zone(hid, kv, flags1=flags1, trig=trig)
    if "aabb" not in kv:
        raise AsmError("zone line needs aabb= (or hex=)")
    aabb = _parse_aabb(kv["aabb"])
    hook_kv = {k: v for k, v in kv.items() if k not in ("aabb", "trig", "hex")}
    hook_tokens = ["hook", str(hid), *pos]
    for key, val in hook_kv.items():
        hook_tokens.append(f"{key}={val}")
    head = bytearray(_parse_hook_line(hook_tokens))
    if len(head) != 20:
        raise AsmError("zone payload must be 20 bytes")
    if trig:
        if head[2] & 0x0F:
            head[2] = (_u8(trig) & 0xF0) | (head[2] & 0x0F)
        else:
            head[2] = _u8(trig)
    elif (head[1] & 0x3F) == 0x02 and (head[2] & 7) and (head[2] & 0xF0) == 0:
        head[2] |= 0x20
    return bytes(head) + aabb


def parse_table2_text(text: str, *, map_stem: str) -> list[bytes]:
    """Parse assembler text into 20-byte table-2 rows."""
    want = map_stem.upper()
    lines = text.splitlines()
    i = 0

    def skip() -> None:
        nonlocal i
        while i < len(lines):
            s = _without_comment(lines[i])
            if not s:
                i += 1
                continue
            break

    skip()
    if i >= len(lines):
        raise AsmError("expected 'hooks STEM'")
    hdr = _without_comment(lines[i]).split()
    if len(hdr) < 2 or hdr[0] != "hooks":
        raise AsmError("expected 'hooks STEM'")
    got = hdr[1].upper()
    if got != want:
        raise AsmError(f"hooks header {got} does not match open map {want}")
    i += 1
    skip()
    table_toks = _without_comment(lines[i]).split() if i < len(lines) else []
    if table_toks[:2] != ["table", "2"]:
        raise AsmError("expected 'table 2'")
    i += 1

    rows: list[bytes] = []
    while i < len(lines):
        skip()
        if i >= len(lines):
            break
        tokens = _without_comment(lines[i]).split()
        if not tokens or tokens[0] != "hook":
            raise AsmError(f"expected hook line, got {lines[i]!r}")
        rows.append(_parse_hook_line(tokens))
        i += 1
    return rows


def roundtrip_table2(rows: list[bytes], *, map_stem: str) -> list[bytes]:
    return parse_table2_text(
        format_table2(rows, map_stem=map_stem, resolve_xref=False),
        map_stem=map_stem,
    )


def parse_hook_asm_text(text: str, *, map_stem: str) -> dict[int, list[bytes]]:
    """Parse ``hooks STEM`` with one or both of ``table 1`` / ``table 2``."""
    want = map_stem.upper()
    lines = text.splitlines()
    i = 0

    def skip() -> None:
        nonlocal i
        while i < len(lines):
            s = _without_comment(lines[i])
            if not s:
                i += 1
                continue
            break

    skip()
    if i >= len(lines):
        raise AsmError("expected 'hooks STEM'")
    hdr = _without_comment(lines[i]).split()
    if len(hdr) < 2 or hdr[0] != "hooks":
        raise AsmError("expected 'hooks STEM'")
    got = hdr[1].upper()
    if got != want:
        raise AsmError(f"hooks header {got} does not match open map {want}")
    i += 1

    tables: dict[int, list[bytes]] = {}
    while True:
        skip()
        if i >= len(lines):
            break
        toks = _without_comment(lines[i]).split()
        if toks[:1] != ["table"] or len(toks) < 2 or toks[1] not in ("1", "2"):
            raise AsmError(f"expected 'table 1' or 'table 2', got {lines[i]!r}")
        ti = int(toks[1])
        if ti in tables:
            raise AsmError(f"duplicate table {ti} block")
        i += 1
        rows: list[bytes] = []
        prefix = "zone" if ti == 1 else "hook"
        while True:
            skip()
            if i >= len(lines):
                break
            nxt = _without_comment(lines[i]).split()
            if nxt[:1] == ["table"]:
                break
            if not nxt or nxt[0] != prefix:
                raise AsmError(f"expected {prefix} line, got {lines[i]!r}")
            if ti == 1:
                rows.append(_parse_zone_line(nxt))
            else:
                rows.append(_parse_hook_line(nxt))
            i += 1
        tables[ti] = rows
    if not tables:
        raise AsmError("expected 'table 1' or 'table 2'")
    return tables


def parse_table1_text(text: str, *, map_stem: str) -> list[bytes]:
    """Parse assembler text into 32-byte table-1 rows."""
    tables = parse_hook_asm_text(text, map_stem=map_stem)
    if 1 not in tables:
        raise AsmError("expected 'table 1'")
    return tables[1]


def roundtrip_table1(rows: list[bytes], *, map_stem: str) -> list[bytes]:
    return parse_table1_text(
        format_table1(rows, map_stem=map_stem, resolve_xref=False),
        map_stem=map_stem,
    )


def emit_hook_text(src: Path, dest: Path) -> int:
    """Assemble one ``hook N …`` line (or the first hook in a dump) to 20 bytes."""
    text = src.read_text(encoding="utf-8-sig")
    for raw in text.replace("\r\n", "\n").replace("\r", "\n").split("\n"):
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        tokens = line.split()
        if tokens[0] != "hook":
            continue
        row = _parse_hook_line(tokens)
        dest.write_bytes(row)
        print(f"emit hook {row[0]} {len(row)} bytes -> {dest}")
        return 0
    raise SystemExit("no hook line in --emit input")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Print / check table-1 / table-2 hook assembler text")
    ap.add_argument("stem", nargs="?", default=None, help="map stem, e.g. BA38")
    ap.add_argument("--table", choices=("1", "2", "all"), default="2")
    ap.add_argument("--check", action="store_true", help="assert parse(format) == rows")
    ap.add_argument("--emit", type=Path, metavar="ASM", help="assemble one hook line to 20 bytes")
    ap.add_argument("-o", "--output", type=Path, help="byte output (with --emit)")
    args = ap.parse_args(argv)
    if args.emit:
        if args.output is None:
            raise SystemExit("--emit needs -o/--output")
        return emit_hook_text(args.emit, args.output)
    if not args.stem:
        raise SystemExit("stem is required unless --emit is set")
    bundle = load_hook_bundle(args.stem.upper())
    if bundle is None:
        raise SystemExit(f"{args.stem}: no sec[7] hook bundle")
    t1 = table1_rows_from_bundle(bundle)
    t2 = table2_rows_from_bundle(bundle)
    if args.table == "1":
        text = format_table1(t1, map_stem=args.stem)
        if args.check:
            got = parse_table1_text(text, map_stem=args.stem)
            if got != t1:
                raise SystemExit(f"{args.stem}: table-1 assembler identity failed")
            print(f"{args.stem}: table 1 ok ({len(t1)} rows)")
            return 0
        sys.stdout.write(text)
        return 0
    if args.table == "all":
        text = format_hook_asm(t1, t2, map_stem=args.stem)
        if args.check:
            got = parse_hook_asm_text(text, map_stem=args.stem)
            if got.get(1, []) != t1 or got.get(2, []) != t2:
                raise SystemExit(f"{args.stem}: hook assembler identity failed")
            print(f"{args.stem}: ok (table1={len(t1)} table2={len(t2)})")
            return 0
        sys.stdout.write(text)
        return 0
    text = format_table2(t2, map_stem=args.stem)
    if args.check:
        got = parse_table2_text(text, map_stem=args.stem)
        if got != t2:
            raise SystemExit(f"{args.stem}: table-2 assembler identity failed")
        print(f"{args.stem}: ok ({len(t2)} rows)")
        return 0
    sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
