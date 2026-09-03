"""Resolve table-2 hook operands against other MDP sections.

Used only for regenerated ``#`` comments. Pretty assembler lines stay numeric
so ``parse(format(rows)) == rows``.
"""
from __future__ import annotations

import struct
from dataclasses import dataclass, field
from functools import lru_cache
from typing import Any

from decode_mdp_huffman import decompress_counted
from field_script_hooks import decode_hook_payload
from mdp_lib import find_field_dir, find_mdp, get_section
from parse_mdp_sec8 import KIND_NAMES, parse_records, s16
from parse_mdp_sec15 import iter_chunks, parse_chunk


def _u16(b: bytes, o: int) -> int:
    return struct.unpack_from("<H", b, o)[0]


def _u32(b: bytes, o: int) -> int:
    return struct.unpack_from("<I", b, o)[0]


def _clip_empty(ch: bytes) -> bool:
    if not ch:
        return True
    if ch[:4] == b"\xff\x00\x00\x00":
        return True
    return ch[0] == 0xFF and all(b == 0 for b in ch[1:])


def summarize_camera_clip(ch: bytes) -> str:
    if _clip_empty(ch):
        return "empty"
    ev, _pos, _err = parse_chunk(ch)
    inherit_pos = False
    snap: str | None = None
    tables = 0
    wait = 0
    restore = False
    for e in ev:
        parts = e.split()
        if len(parts) < 2:
            continue
        op = parts[1]
        if op == "set_pos":
            args = parts[2:5]
            if args == ["inherit", "inherit", "inherit"]:
                inherit_pos = True
            elif snap is None and len(args) >= 3:
                snap = ",".join(args)
        elif op == "table_s32":
            tables += 1
        elif op in ("wait", "wait_b") and parts[-1].isdigit():
            wait += int(parts[-1])
        if "snapshot" in e:
            restore = True
    bits: list[str] = []
    if inherit_pos:
        bits.append("from current camera")
    elif snap:
        bits.append(f"snap to {snap}")
    if tables:
        bits.append("table pan")
    if wait:
        bits.append(f"{wait} frames")
    if restore:
        bits.append("then restore")
    return ", ".join(bits) if bits else (f"{len(ev)} ops" if ev else "unparsed")


def _attach_band(kind: int) -> str:
    if 1 <= kind <= 16:
        return "low"
    if 50 <= kind <= 59:
        return "50s prop"
    if 100 <= kind <= 133:
        return f"talk body {kind - 99}"
    if 150 <= kind <= 159:
        return "150s prop"
    if 200 <= kind <= 209:
        return "200s"
    if 240 <= kind <= 249:
        return "240s root"
    return ""


@dataclass
class MapXref:
    stem: str = ""
    cam_clips: dict[int, str] = field(default_factory=dict)
    cam_count: int = 0
    talks: dict[int, str] = field(default_factory=dict)
    _attach: dict[int, str] | None = None

    @property
    def attach(self) -> dict[int, str]:
        if self._attach is None:
            self._attach = _load_attach(self.stem) if self.stem else {}
        return self._attach


def _load_cam_clips(stem: str) -> tuple[dict[int, str], int]:
    try:
        blob = get_section(find_mdp(find_field_dir(), stem).read_bytes(), 15)
    except FileNotFoundError:
        return {}, 0
    if not blob:
        return {}, 0
    chunks = iter_chunks(blob)
    out: dict[int, str] = {}
    for i, ch in enumerate(chunks):
        out[i + 1] = summarize_camera_clip(ch)
    return out, len(chunks)


def _load_attach(stem: str) -> dict[int, str]:
    try:
        blob = get_section(find_mdp(find_field_dir(), stem).read_bytes(), 0)
    except FileNotFoundError:
        return {}
    if not blob:
        return {}
    try:
        dec = decompress_counted(blob)
    except (ValueError, IndexError, struct.error):
        return {}
    if len(dec) < 0x58:
        return {}
    t4, t8 = _u32(dec, 4), _u32(dec, 8)
    if not (0 < t4 < t8 <= len(dec)):
        return {}
    out: dict[int, str] = {}
    for off in range(0, t8 - t4 - 3, 4):
        kind = _u16(dec, t4 + off)
        obj = _u16(dec, t4 + off + 2)
        band = _attach_band(kind)
        label = f"obj {obj}"
        if band:
            label = f"{label}, {band}"
        out[kind] = label
    return out


def _load_talks(stem: str) -> dict[int, str]:
    try:
        blob = get_section(find_mdp(find_field_dir(), stem).read_bytes(), 8)
    except FileNotFoundError:
        return {}
    if not blob:
        return {}
    _packed, _n, recs = parse_records(blob)
    buckets: dict[int, list[str]] = {}
    for rec in recs:
        talk = rec[3]
        if not talk:
            continue
        kind = rec[1] & 0xF
        kname = KIND_NAMES.get(kind, f"kind {kind}")
        x, y, z = s16(rec, 4), s16(rec, 6), s16(rec, 8)
        buckets.setdefault(talk, []).append(f"{kname} at {x},{y},{z}")
    out: dict[int, str] = {}
    for talk, labels in buckets.items():
        if len(labels) == 1:
            out[talk] = labels[0]
        else:
            out[talk] = f"{len(labels)} instances, first {labels[0]}"
    return out


@lru_cache(maxsize=128)
def load_map_xref(map_stem: str) -> MapXref:
    stem = map_stem.upper()
    clips, n = _load_cam_clips(stem)
    return MapXref(
        stem=stem,
        cam_clips=clips,
        cam_count=n,
        talks=_load_talks(stem),
    )


def _kind_short(raw: bytes) -> str:
    d = decode_hook_payload(raw[1] & 0x3F, raw, row_size=20)
    kind = str(d.get("kind") or "?")
    if kind == "camera_path":
        return f"camera_path {d.get('pathId')}"
    if kind == "attach_anim":
        return f"attach_anim {d.get('attachKind')} p{d.get('partIndex')}"
    if kind == "flag_wait":
        return f"flag_wait 0x{int(d.get('flagId') or 0):04X}"
    if kind == "present_channel":
        return f"present_channel {d.get('channel')}"
    if kind == "anim_latch":
        return f"anim {d.get('animId')}"
    if kind == "setup":
        return f"setup 0x{int(d.get('pair0') or 0):04X}"
    if kind.startswith("type_"):
        return kind
    return kind


def xref_comment(
    decoded: dict[str, Any],
    xref: MapXref,
    *,
    siblings: dict[int, bytes] | None = None,
) -> str:
    """Extra sentence naming the MDP target. Empty if nothing to add."""
    kind = decoded.get("kind")
    bits: list[str] = []
    sibs = siblings or {}

    if kind == "camera_path":
        pid = int(decoded.get("pathId") or 0)
        if pid <= 0:
            bits.append("sec[15] clip 0 (engine skips)")
        elif pid in xref.cam_clips:
            bits.append(f"sec[15] clip {pid}: {xref.cam_clips[pid]}")
        elif xref.cam_count:
            bits.append(f"sec[15] clip {pid} missing (map has {xref.cam_count})")
        else:
            bits.append(f"sec[15] clip {pid}")
        stream = decoded.get("streamSlot")
        if stream:
            bits.append(f"SoftHD stream {stream}")

    elif kind in ("attach_vis", "attach_pos", "field_bind", "fx_pos", "zone", "attach_anim"):
        base = int(
            decoded.get("attachKind")
            or decoded.get("attachBase")
            or decoded.get("objectId")
            or 0
        )
        hit = xref.attach.get(base)
        noun = "kind"
        if kind == "zone":
            noun = "object/kind"
        if hit:
            bits.append(f"sec[0] hdr+4 {noun} {base} -> {hit}")
        elif xref.attach:
            bits.append(f"sec[0] hdr+4 {noun} {base} (not in table)")
        span = int(decoded.get("span") or 0)
        if span:
            bits.append(f"span {span}")
        if kind == "attach_anim":
            bits.append(f"part {decoded.get('partIndex')} start={decoded.get('start')} end={decoded.get('end')}")

    elif kind == "flag_wait":
        fid = int(decoded.get("flagId") or 0)
        expect = int(decoded.get("expect") or 0)
        if decoded.get("useStream"):
            bits.append(f"SoftHD stream {decoded.get('streamSlot')} on flag 0x{fid:04X}")
        else:
            bits.append(f"wait until flag 0x{fid:04X} is {expect}")

    elif kind == "unit_bind" and decoded.get("bindName") == "field_talk":
        tid = int(decoded.get("targetId") or 0)
        hit = xref.talks.get(tid)
        bits.append(f"sec[8] talk {tid}" + (f" ({hit})" if hit else ""))

    elif kind == "visibility":
        sub = decoded.get("subtypeName") or decoded.get("subtype")
        tid = int(decoded.get("targetId") or 0)
        if sub == "talk_id" or decoded.get("subtype") == 1:
            hit = xref.talks.get(tid)
            bits.append(f"sec[8] talk {tid}" + (f" ({hit})" if hit else ""))

    elif kind == "party_actor" and decoded.get("subtype") == 8:
        tid = int(decoded.get("talkId") or 0)
        hit = xref.talks.get(tid)
        bits.append(f"sec[8] talk {tid}" + (f" ({hit})" if hit else ""))

    elif kind == "hook_fanout":
        kids = decoded.get("children") or []
        named: list[str] = []
        for child in kids:
            hid = int(child.get("hookId") or 0)
            mode = child.get("mode")
            raw = sibs.get(hid)
            if raw is not None:
                named.append(f"{hid} {_kind_short(raw)} mode {mode}")
            else:
                named.append(f"{hid} mode {mode}")
        if named:
            bits.append("fires " + ", ".join(named))

    return "; ".join(bits)


def sibling_index(rows: list[bytes]) -> dict[int, bytes]:
    out: dict[int, bytes] = {}
    for raw in rows:
        hid = raw[0]
        if hid not in out:
            out[hid] = raw
    return out
