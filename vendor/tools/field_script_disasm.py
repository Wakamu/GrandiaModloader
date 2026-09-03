#!/usr/bin/env python3
"""Disassemble Grandia HD field cutscene scripts from FIELD/*.mdp or TEXT/*.SCN.

Layout (map load +0x54A0E / resolve +0x6F0B0):
  sec[11]  (u16 script_id, u16 bank_offset)*  terminated by id 0xFFFF
  sec[12]  u16 LE word-stream bank

HD runtime cutscenes use:
  TEXT/<lang>/<map>.OFS  (same u16 id / u16 offset directory)
  TEXT/<lang>/<map>.SCN  (same u16 LE word-stream bank)

Opcode word: type_idx = (word >> 12) - 1  (nibble 1..15 → type 0..14)
Sizes mirror +0x6F130 handlers / field_script_cmd_log Consume* helpers.

Intro cinematic: BA38 (script 0x3000 opener, then 0x0000 body) → Parm 2000.
"""
from __future__ import annotations

import argparse
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

from portrait_resolver import dialogue_face_index, is_printable_face_extra

DEFAULT_CONTENT = Path(
    r"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\FIELD"
)
DEFAULT_TEXT = DEFAULT_CONTENT.parent / "TEXT" / "EN"

TYPE_NAMES = {
    0: "mode",
    1: "dialogue",
    2: "ctrl2",
    3: "compare/branch",
    4: "flag",
    5: "op5",
    6: "actor/cam",
    7: "reljump",
    8: "ui/action",
    9: "t9",
    10: "t10",
    11: "t11",
    12: "t12",
    13: "t13",
    14: "yield",
}
TYPE6_SUB_NAMES = {
    0x0004: "give_item",         # give item by id; arg0 = item_id
    0x0005: "give_item_alt",     # alternate give-item (used for Wound Salve etc.)
    0x000E: "restore",           # full-party heal (Save/Recover Recover)
    0x0010: "save_menu",         # open the save-game screen
    0x0011: "wait_ready",        # pause main thread N ticks
    0x0015: "sfx",               # sound cue; extra = catalog id (0x000C = recover)
    0x0016: "camera_cmd",        # overlay/deactivate_overlay = map BGM catalog id
    0x001A: "call_hook",         # +0x53560 → +0x53830; arg0 = hook_id (sec[7])
    0x001B: "stash_menu",        # open send-to-stash (0-arg)
    0x001C: "retrieve_menu",     # open get-from-stash (0-arg)
    0x0020: "wait_ready_aux",    # conditional wait; arg0 = condition id
    0x0021: "arm_wait",          # arm the script wait / sync point
}

TEXT_CTRL_NAMES = {
    0x01: r"\n",
    0x02: r"\p",
    0x07: r"\e",
    0x0A: r"\x0A",
}

# Type-1 header byte-1: textbox portrait-slot group.
# Only two values found across the entire game:
#   0x03 = NPC / non-party portrait slot
#   0x21 = party-hero portrait slot (Justin, Sue, Feena, Rapp)
# The specific face shown may follow a nearby call_hook in the same scene block.
T1_B1_NAMES: dict[int, str] = {
    0x03: "npc",
    0x21: "hero",
}



def mdp_sections(data: bytes) -> dict[int, tuple[int, int]]:
    out: dict[int, tuple[int, int]] = {}
    for i in range(64):
        ptr, size = struct.unpack_from("<II", data, i * 8)
        if 0 < ptr < len(data) and size and ptr + size <= len(data):
            out[i] = (ptr, size)
    return out


def find_mdp(content: Path, stem: str) -> Path:
    for name in (f"{stem}.mdp", f"{stem}.MDP", stem.upper() + ".mdp", stem.upper() + ".MDP"):
        p = content / name
        if p.exists():
            return p
    raise FileNotFoundError(f"{stem}.mdp not under {content}")


def find_text_pair(text_root: Path, stem: str) -> tuple[Path, Path]:
    for lang_stem in (stem.upper(), stem.lower(), stem):
        scn = text_root / f"{lang_stem}.SCN"
        ofs = text_root / f"{lang_stem}.OFS"
        if scn.exists() and ofs.exists():
            return scn, ofs
        scn = text_root / f"{lang_stem}.scn"
        ofs = text_root / f"{lang_stem}.ofs"
        if scn.exists() and ofs.exists():
            return scn, ofs
    raise FileNotFoundError(f"{stem}.SCN/.OFS not under {text_root}")


@dataclass
class ScriptEntry:
    script_id: int
    offset: int
    end: int  # exclusive bank offset


def parse_directory(directory: bytes, bank_size: int) -> list[ScriptEntry]:
    """sec[11]: (u16 id, u16 offset)*; size = next_offset - offset (bank_size for last)."""
    entries: list[tuple[int, int]] = []
    pos = 0
    while pos + 4 <= len(directory):
        sid, off = struct.unpack_from("<HH", directory, pos)
        if sid == 0xFFFF:
            break
        entries.append((sid, off))
        pos += 4
    # Game accumulate can add 0x10000 per wrap; keep low offsets as-is for small banks.
    entries_sorted = sorted(entries, key=lambda e: e[1])
    out: list[ScriptEntry] = []
    for i, (sid, off) in enumerate(entries_sorted):
        if i + 1 < len(entries_sorted):
            end = entries_sorted[i + 1][1]
        else:
            end = bank_size
        if end < off:
            end = bank_size
        out.append(ScriptEntry(sid, off, min(end, bank_size)))
    # Preserve directory order for listing, but return offset-sorted for disasm ranges.
    return out


def type6_payload_len(outer_word: int, payload: int) -> int:
    """Bytes after the payload word (mirrors ConsumeType6Payload / +0x6F6D0)."""
    hi = payload >> 14
    if hi == 0:
        return 0
    n = (outer_word & 0xF) + 1
    if hi == 1 and (n & 1) != 0:
        n += 1
    return n << (hi - 1)


def read_u16(bank: bytes, off: int) -> int:
    if off + 2 > len(bank):
        raise IndexError(f"u16 read past bank at 0x{off:X}")
    return struct.unpack_from("<H", bank, off)[0]


@dataclass
class Op:
    offset: int
    word: int
    type_idx: int
    size: int
    payload: bytes
    note: str = ""


def parse_t1_header(payload: bytes) -> tuple[int, int, int] | None:
    """
    Parse the 5-byte type-1 page header.

    Two known formats:
      0x0F <b1> 0x0A 0x0C <expr>  — speech bubble (portrait visible)
      0x0B <b1> 0x0A 0x0C <expr>  — item-get notification (party-wide)
      0x0B <b1> 0x1F 0x01 <expr>  — item-get notification (alternate variant)
      0x21 <b1> 0x0A 0x0C <expr>  — (observed in some maps)

    Returns (pre, b1, expr) or None if not recognised.
    """
    if len(payload) < 5:
        return None
    pre, b1, b2, b3, expr = payload[0], payload[1], payload[2], payload[3], payload[4]
    if pre not in (0x0F, 0x0B, 0x21):
        return None
    # Accept standard (0A 0C) and alternate item-notification (1F 01) variants
    if (b2, b3) not in ((0x0A, 0x0C), (0x1F, 0x01)):
        return None
    return pre, b1, expr


def _t1_page_header_at(payload: bytes, pos: int) -> int | None:
    """Return the length of a type-1 page header starting at pos, or None."""
    if pos + 5 > len(payload):
        return None
    pre = payload[pos]
    if pre not in (0x0F, 0x0B, 0x21):
        return None
    b2, b3 = payload[pos + 2], payload[pos + 3]
    if (b2, b3) not in ((0x0A, 0x0C), (0x1F, 0x01)):
        return None
    return 5


def _read_dialogue_lines(payload: bytes, start: int) -> tuple[list[str], int]:
    """
    Read all dialogue lines from a word-stream payload starting at `start`.

    The format (shared by type-1 and type-8) is a sequence of:
      09 01  <text bytes>  0E 00 <hold>  09 0F <u16>

    where <text bytes> may contain:
      0x01  \n  (inline newline)
      0x02  \p  (page-break)
      0x20-0x7E  printable ASCII

    A new page header 0F <b1> 0A 0C <expr> appearing between lines signals
    a textbox boundary — we represent that as a page-separator in the output.

    Returns (lines, end_pos) where lines is a flat list of line strings
    (page boundaries are represented by the sentinel string '\x00PAGE\x00').
    """
    n = len(payload)
    pos = start
    lines: list[str] = []

    def read_text_line(p: int) -> tuple[str, int]:
        """Read one display line starting at byte p. Returns (text, new_p)."""
        text: list[str] = []
        while p < n:
            c = payload[p]
            if c == 0x07:
                break
            if c == 0x01:
                text.append(r"\n"); p += 1; continue
            if c == 0x02:
                text.append(r"\p"); p += 1; continue
            if 0x20 <= c <= 0x7E:
                text.append(chr(c)); p += 1; continue
            if c == 0x0E and p + 2 < n and payload[p + 1] == 0x00:
                p += 3; break  # end-of-line hold timer
            break
        return "".join(text), p

    def skip_transition(p: int) -> int:
        """Skip a 09 0F <u16> transition if present. Returns new p."""
        if p + 3 < n and payload[p] == 0x09 and payload[p + 1] == 0x0F:
            return p + 4
        return p

    def skip_09_control(p: int) -> int:
        """
        Skip a 09 <sub> control op that is *not* a 09 01 line-start.

        From engine RE of the 0x09 sub-dispatch:
          - sub in [0x0A..0x0F] consumes a 2-byte parameter => total 4 bytes: 09 xx lo hi
          - sub in [0x01..0x09] consumes no extra parameter => total 2 bytes: 09 xx
        """
        if p + 1 >= n:
            return p + 1
        if payload[p] != 0x09:
            return p
        sub = payload[p + 1]
        if sub in (0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F):
            return min(n, p + 4)
        return min(n, p + 2)

    def check_page_hdr(p: int) -> int:
        """If a page header is at p, emit PAGE sentinel and skip it. Returns new p."""
        h = _t1_page_header_at(payload, p)
        if h is not None:
            if lines:
                lines.append('\x00PAGE\x00')
            p += h
            # skip one post-header metadata byte if not a line-start or terminator
            if p < n and payload[p] not in (0x07, 0x09):
                p += 1
        return p

    def read_page_lines(entry_pos: int) -> int:
        """Read all display lines of one page starting at entry_pos.
        Stops at terminator, page header, or new 09 01.
        Returns new pos."""
        p = entry_pos
        while p < n and payload[p] != 0x07:
            # Page header in the middle of a page → next page
            h2 = _t1_page_header_at(payload, p)
            if h2 is not None:
                p = check_page_hdr(p)
                return p
            # 09 01 = start of a new page
            if payload[p] == 0x09 and p + 1 < n and payload[p + 1] == 0x01:
                return p
            # Skip 09 <sub> control ops that are not line-starts.
            if payload[p] == 0x09:
                p = skip_09_control(p)
                continue
            # Skip lone control bytes (like 0x01 appearing between transitions)
            if payload[p] < 0x20:
                p += 1
                continue
            prev_p = p
            line, p = read_text_line(p)
            lines.append(line)
            p = skip_transition(p)
            if p == prev_p:  # safety: ensure forward progress
                p += 1
        return p

    while pos < n:
        b = payload[pos]
        if b == 0x07:
            break

        # Page header
        h = _t1_page_header_at(payload, pos)
        if h is not None:
            pos = check_page_hdr(pos)
            continue

        # 09 01 — explicit line-start (first line of page)
        if b == 0x09 and pos + 1 < n and payload[pos + 1] == 0x01:
            pos += 2
            line, pos = read_text_line(pos)
            lines.append(line)
            pos = skip_transition(pos)
            pos = read_page_lines(pos)
            continue
        # Skip 09 <sub> control ops that are not line-starts.
        if b == 0x09:
            pos = skip_09_control(pos)
            continue

        if b < 0x20:
            pos += 1
            continue

        # Bare printable (type-8 with no 09 01 opener)
        prev = pos
        line, pos = read_text_line(pos)
        lines.append(line)
        pos = skip_transition(pos)
        if pos == prev:
            pos += 1

    return lines, pos


@dataclass
class T1Page:
    """One textbox page with its own portrait header metadata."""
    pre: int
    b1: int
    expr: int
    text: str  # page text with "\n" separators between lines


@dataclass
class T1Cue:
    """One visible dialogue step inside a Type-1 textbox page."""
    pre: int
    b1: int
    expr: int
    page_idx: int
    line_idx: int
    text: str
    hold: int | None = None
    face_key: int | None = None
    pre_controls: list[int] | None = None
    header_stack: list[tuple[int, int, int]] | None = None


@dataclass
class T8Cue:
    """One visible dialogue step inside a Type-8 UI/action stream."""
    line_idx: int
    text: str
    hold: int | None = None
    face_key: int | None = None
    expr: int | None = None
    pre: int | None = None
    b1: int | None = None
    pre_controls: list[int] | None = None


def _consume_post_header_extras(payload: bytes, pos: int) -> tuple[list[int], int]:
    """Bytes immediately after a 5-byte portrait header.

    Binary extras (0x07 smile, 0x08 shout, 0x0D/0x11/0x18 Sue/Puffy, 0x32
    hurt) and printable 0x20-0x3C prefixes (BA38 ``0``/``.``/``"``,
    2404 ``:`` / leading space) are the
    1-based still. Stop before LineStart (09), newline, page-break, hold, or
    visible letters. A lone 0x07 with no following text is the payload
    terminator, not a face extra.
    """
    extras: list[int] = []
    n = len(payload)
    while pos < n:
        c = payload[pos]
        if c == 0x09 or c in (0x01, 0x02):
            break
        if c == 0x0E:
            break
        if c == 0x07:
            nxt = payload[pos + 1] if pos + 1 < n else 0
            if 0x20 <= nxt <= 0x7E or is_printable_face_extra(nxt):
                extras.append(c)
                pos += 1
            break
        if is_printable_face_extra(c) or c in (0x32, 0x33):
            extras.append(c)
            pos += 1
            break
        if 0x20 <= c <= 0x7E and _t1_page_header_at(payload, pos + 1) is not None:
            # Stacked portrait: a lone extra before the next header is the still
            # (2404 "Yes Sir!" uses '<' / ';' / '-' as 1-based face ids).
            extras.append(c)
            pos += 1
            break
        if c < 0x20:
            extras.append(c)
            pos += 1
            continue
        break
    return extras, pos


def parse_t8_cues(
    payload: bytes,
    *,
    map_stem: str | None = None,
    field_root: Path | None = None,
) -> list[T8Cue]:
    """Parse a Type-8 payload into playback-ready cues with hold/face metadata.

    Type-8 is mostly ``09 01`` / ``0E 00`` / ``09 0F`` but **can embed a type-1
    portrait header** (``0F/0B/21 … expr``) when the box switches speaker.
    Those 5 bytes must not be treated as bank-select controls — that used to
    drop ``b1`` and hide Justin's face on Parm 0x0000.
    """
    n = len(payload)

    def read_text_line_and_hold(p: int) -> tuple[str, int | None, int]:
        text: list[str] = []
        hold: int | None = None
        while p < n:
            c = payload[p]
            if c == 0x07:
                break
            if c == 0x01:
                text.append(r"\n")
                p += 1
                continue
            if c == 0x02:
                text.append(r"\p")
                p += 1
                continue
            if 0x20 <= c <= 0x7E:
                text.append(chr(c))
                p += 1
                continue
            if c == 0x0E and p + 2 < n and payload[p + 1] == 0x00:
                hold = payload[p + 2]
                p += 3
                break
            break
        return "".join(text), hold, p

    def append_cue(text: str, hold: int | None, face_key: int | None) -> None:
        nonlocal line_idx, pending_controls
        if not text.strip() or text.strip() == r"\p":
            return
        controls = list(header_face_controls) + list(pending_controls)
        expr = dialogue_face_index(
            header_expr=cur_header_expr,
            face_key=face_key,
            controls=controls,
            has_header=cur_pre is not None,
            map_stem=map_stem,
            b1_slot=cur_b1,
            field_root=field_root,
        )
        cues.append(T8Cue(
            line_idx=line_idx,
            text=text,
            hold=hold,
            face_key=face_key,
            expr=expr,
            pre=cur_pre,
            b1=cur_b1,
            pre_controls=controls,
        ))
        line_idx += 1
        pending_controls = []

    cues: list[T8Cue] = []
    pos = 0
    line_idx = 0
    pending_controls: list[int] = []
    header_face_controls: list[int] = []
    cur_pre: int | None = None
    cur_b1: int | None = None
    cur_header_expr: int | None = None

    while pos < n:
        if payload[pos] == 0x07:
            break
        hdr_len = _t1_page_header_at(payload, pos)
        if hdr_len is not None:
            hdr = parse_t1_header(payload[pos:])
            if hdr is not None:
                cur_pre, cur_b1, cur_header_expr = hdr
                pos += hdr_len
                header_face_controls, pos = _consume_post_header_extras(payload, pos)
                pending_controls = []
                continue
        if payload[pos] == 0x09 and pos + 1 < n and payload[pos + 1] == 0x01:
            pos += 2
            text, hold, pos = read_text_line_and_hold(pos)
            face_key: int | None = None
            if pos + 3 < n and payload[pos] == 0x09 and payload[pos + 1] == 0x0F:
                face_key = struct.unpack_from("<H", payload, pos + 2)[0]
                pos += 4
            append_cue(text, hold, face_key)
            continue
        if payload[pos] == 0x09 and pos + 3 < n and payload[pos + 1] == 0x0F:
            # Lip-sync FaceKey can sit between the extra and the first letter.
            pos += 4
            continue
        if payload[pos] == 0x09:
            pos += 2
            continue
        if payload[pos] in (0x01, 0x02) or 0x20 <= payload[pos] <= 0x7E:
            text, hold, pos = read_text_line_and_hold(pos)
            face_key: int | None = None
            if pos + 3 < n and payload[pos] == 0x09 and payload[pos + 1] == 0x0F:
                face_key = struct.unpack_from("<H", payload, pos + 2)[0]
                pos += 4
            append_cue(text, hold, face_key)
            continue
        if payload[pos] < 0x20:
            if payload[pos] == 0x08 and cur_pre is not None:
                # Box replace with no new header: drop the still.
                cur_pre = None
                cur_b1 = None
                cur_header_expr = None
                header_face_controls = []
            elif payload[pos] in (0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08):
                pending_controls.append(payload[pos])
            pos += 1
            continue
        pos += 1

    return cues


def parse_t1_cues(
    payload: bytes,
    *,
    map_stem: str | None = None,
    field_root: Path | None = None,
) -> list[T1Cue]:
    """
    Parse a Type-1 payload into playback-ready cues.

    Each cue corresponds to one visible text emission:
      09 01 <text> 0E 00 <hold> [09 0F <u16>]

    Page headers update the current portrait/textbox metadata for subsequent cues.
    """
    n = len(payload)

    def parse_page_header_at(p: int) -> tuple[int, int, int, int] | None:
        if p + 5 > n:
            return None
        pre = payload[p]
        if pre not in (0x0F, 0x0B, 0x21):
            return None
        b1 = payload[p + 1]
        b2 = payload[p + 2]
        b3 = payload[p + 3]
        if (b2, b3) not in ((0x0A, 0x0C), (0x1F, 0x01)):
            return None
        expr = payload[p + 4]
        return pre, b1, expr, 5

    def skip_09_control(p: int) -> int:
        if p + 1 >= n:
            return p + 1
        sub = payload[p + 1]
        if sub in (0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F):
            return min(n, p + 4)
        return min(n, p + 2)

    def read_text_line_and_hold(p: int) -> tuple[str, int | None, int]:
        text: list[str] = []
        hold: int | None = None
        while p < n:
            c = payload[p]
            if c == 0x07:
                break
            if c == 0x01:
                text.append(r"\n")
                p += 1
                continue
            if c == 0x02:
                text.append(r"\p")
                p += 1
                continue
            if 0x20 <= c <= 0x7E:
                text.append(chr(c))
                p += 1
                continue
            if c == 0x0E and p + 2 < n and payload[p + 1] == 0x00:
                hold = payload[p + 2]
                p += 3
                break
            break
        return "".join(text), hold, p

    cues: list[T1Cue] = []
    cur_meta: tuple[int, int, int] | None = None
    page_idx = -1  # now means "textbox id", not "header count"
    line_idx = 0
    portrait_active = False
    pos = 0

    pending_controls: list[int] = []
    header_face_controls: list[int] = []
    active_headers: list[tuple[int, int, int]] = []

    def append_cue(text: str, hold: int | None, face_key: int | None) -> None:
        nonlocal cur_meta, page_idx, line_idx, pending_controls, active_headers, header_face_controls
        if cur_meta is None:
            cur_meta = (0x0F, 0x00, 0x00)
            if page_idx < 0:
                page_idx = 0
        controls = list(header_face_controls) + list(pending_controls)
        expr = dialogue_face_index(
            header_expr=cur_meta[2] if portrait_active else None,
            face_key=face_key,
            controls=controls,
            has_header=portrait_active,
            map_stem=map_stem,
            b1_slot=cur_meta[1] if portrait_active else None,
            field_root=field_root,
        )
        stripped = text.strip()
        if not stripped:
            if hold is None:
                return
        elif stripped == r"\p":
            pending_controls.append(0x02)
            active_headers = []
            header_face_controls = []
            return
        if stripped == r"\n":
            return

        if any(c in (0x02, 0x06, 0x07, 0x08, 0x13) for c in pending_controls):
            if page_idx < 0:
                page_idx = 0
            else:
                page_idx += 1
            line_idx = 0

        cues.append(T1Cue(
            pre=cur_meta[0],
            b1=cur_meta[1],
            expr=expr,
            page_idx=max(page_idx, 0),
            line_idx=line_idx,
            text=text,
            hold=hold,
            face_key=face_key,
            pre_controls=controls,
            header_stack=list(active_headers),
        ))
        line_idx += 1
        pending_controls = []

    while pos < n:
        if payload[pos] == 0x07:
            break

        h = parse_page_header_at(pos)
        if h is not None:
            cur_meta = (h[0], h[1], h[2])
            portrait_active = True
            active_headers.append((h[0], h[1], h[2]))
            pos += h[3]
            header_face_controls, pos = _consume_post_header_extras(payload, pos)
            pending_controls = []
            continue

        if payload[pos] == 0x09 and pos + 1 < n and payload[pos + 1] == 0x01:
            if cur_meta is None:
                cur_meta = (0x0F, 0x00, 0x00)
            pos += 2
            text, hold, pos = read_text_line_and_hold(pos)
            face_key: int | None = None
            if pos + 3 < n and payload[pos] == 0x09 and payload[pos + 1] == 0x0F:
                face_key = struct.unpack_from("<H", payload, pos + 2)[0]
                pos += 4
            append_cue(text, hold, face_key)
            continue

        if payload[pos] == 0x09:
            pos = skip_09_control(pos)
            continue

        # Continuation cues can appear without a fresh 09 01 marker, usually after
        # a transition or as newline-prefixed follow-up text inside the same page.
        if (cur_meta is not None) and (payload[pos] in (0x01, 0x02) or 0x20 <= payload[pos] <= 0x7E):
            text, hold, pos = read_text_line_and_hold(pos)
            face_key: int | None = None
            if pos + 3 < n and payload[pos] == 0x09 and payload[pos + 1] == 0x0F:
                face_key = struct.unpack_from("<H", payload, pos + 2)[0]
                pos += 4
            append_cue(text, hold, face_key)
            continue
        if payload[pos] == 0x0E and pos + 2 < n and payload[pos + 1] == 0x00:
            hold = payload[pos + 2]
            pos += 3
            append_cue("", hold, None)
            continue
        if payload[pos] < 0x20:
            if payload[pos] == 0x08 and portrait_active:
                portrait_active = False
                header_face_controls = []
                active_headers = []
            elif payload[pos] in (0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08):
                pending_controls.append(payload[pos])
                if payload[pos] in (0x02, 0x06):
                    active_headers = []
            pos += 1
            continue
        pos += 1

    return cues


def parse_t1_pages(payload: bytes) -> list[T1Page]:
    """
    Parse type-1 payload into textbox pages.

    Type-1 payload is an inline control stream:
      - repeated portrait/textbox headers: 0F/0B/21 <b1> 0A/1F 0C/01 <expr>
      - followed by one or more line-start sequences: 09 01 <text bytes> 0E 00 <hold>
      - followed by optional transitions: 09 0F <u16>

    We decode per-page (pre,b1,expr) from each embedded header and attach subsequent
    decoded text lines to that page until the next header.
    """
    n = len(payload)

    def parse_page_header_at(p: int) -> tuple[int, int, int, int] | None:
        """Return (pre,b1,expr,hdr_len) for a header at p."""
        if p + 5 > n:
            return None
        pre = payload[p]
        if pre not in (0x0F, 0x0B, 0x21):
            return None
        b1 = payload[p + 1]
        b2 = payload[p + 2]
        b3 = payload[p + 3]
        if (b2, b3) not in ((0x0A, 0x0C), (0x1F, 0x01)):
            return None
        expr = payload[p + 4]
        return pre, b1, expr, 5

    def skip_transition(p: int) -> int:
        """Skip a 09 0F <u16> transition if present. Returns new pos."""
        if p + 3 < n and payload[p] == 0x09 and payload[p + 1] == 0x0F:
            return p + 4
        return p

    def skip_09_control(p: int) -> int:
        """Skip 09 <sub> controls (not 09 01 start)."""
        if p + 1 >= n:
            return p + 1
        sub = payload[p + 1]
        if sub in (0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F):
            return min(n, p + 4)
        return min(n, p + 2)

    def read_text_line(p: int) -> tuple[str, int]:
        """Read one display line starting at byte p. Returns (text, new_p)."""
        text: list[str] = []
        while p < n:
            c = payload[p]
            if c == 0x07:
                break
            if c == 0x01:
                text.append(r"\n")
                p += 1
                continue
            if c == 0x02:
                text.append(r"\p")
                p += 1
                continue
            if 0x20 <= c <= 0x7E:
                text.append(chr(c))
                p += 1
                continue
            # end-of-line hold timer 0E 00 <hold>
            if c == 0x0E and p + 2 < n and payload[p + 1] == 0x00:
                p += 3
                break
            break
        return "".join(text), p

    pages: list[T1Page] = []
    cur_meta: tuple[int, int, int] | None = None  # (pre,b1,expr)
    cur_lines: list[str] = []
    cur_expr_from_transition: bool = False

    def flush_page() -> None:
        nonlocal cur_meta, cur_lines, cur_expr_from_transition
        if cur_meta is None:
            cur_lines = []
            cur_expr_from_transition = False
            return
        if not cur_lines:
            return
        pre, b1, expr = cur_meta
        pages.append(T1Page(pre=pre, b1=b1, expr=expr, text=r"\n".join(cur_lines)))
        cur_lines = []
        cur_expr_from_transition = False

    pos = 0
    while pos < n:
        if payload[pos] == 0x07:
            break

        # Embedded portrait/textbox header => new page
        h = parse_page_header_at(pos)
        if h is not None:
            flush_page()
            cur_meta = (h[0], h[1], h[2])
            pos += h[3]
            cur_expr_from_transition = False
            # Skip an extra metadata/control byte after a header when present.
            if pos < n and payload[pos] < 0x20 and payload[pos] != 0x09:
                pos += 1
            elif pos + 1 < n and payload[pos] not in (0x07, 0x09) and payload[pos + 1] == 0x09:
                pos += 1
            continue

        # Line start: 09 01 <text bytes> ...
        if payload[pos] == 0x09 and pos + 1 < n and payload[pos + 1] == 0x01:
            if cur_meta is None:
                # Fallback: create an "unknown" page meta so we don't crash;
                # portrait resolver will likely mark this as out-of-range.
                cur_meta = (0x0F, 0x00, 0x00)
            pos += 2
            line, pos = read_text_line(pos)
            cur_lines.append(line)
            # After each text line, the engine may emit: 09 0F <u16>.
            # The 0x0F subcommand directly sets the packed face-key (dx),
            # whose low byte matches the expr used by the portrait rect table.
            if pos + 3 < n and payload[pos] == 0x09 and payload[pos + 1] == 0x0F:
                dx_lo = payload[pos + 2]
                # dx_hi = payload[pos + 3]  # currently unused in the viewer
                if not cur_expr_from_transition and cur_meta is not None:
                    pre, b1, _expr = cur_meta
                    cur_meta = (pre, b1, dx_lo)
                    cur_expr_from_transition = True
                pos += 4
            else:
                pos = skip_transition(pos)
            continue

        # Other 09 <sub> control ops (not line start)
        if payload[pos] == 0x09:
            pos = skip_09_control(pos)
            continue

        # If we see printable text without an explicit 09 01 opener, try to decode a line.
        b = payload[pos]
        if 0x20 <= b <= 0x7E:
            if cur_meta is None:
                cur_meta = (0x0F, 0x00, 0x00)
            line, pos = read_text_line(pos)
            cur_lines.append(line)
            pos = skip_transition(pos)
            continue

        # skip other control / unknown bytes safely
        if payload[pos] < 0x20:
            pos += 1
            continue

        pos += 1

    flush_page()
    return pages


def parse_t8_lines(payload: bytes) -> list[str]:
    """
    Parse type-8 (ui/action) payload into dialogue lines.

    Type-8 has no page headers — it's a pure 09 01 / 0E 00 / 09 0F stream.
    Returns flat list of line strings (each line is one textbox display).
    """
    lines, _ = _read_dialogue_lines(payload, 0)
    return [ln for ln in lines if ln != '\x00PAGE\x00']


def format_t1_header(payload: bytes) -> str:
    """Return a short human-readable header summary for a type-1 op."""
    hdr = parse_t1_header(payload)
    if hdr is None:
        return ""
    pre, b1, expr = hdr
    pre_name = {0x0F: "speech", 0x0B: "item_notify", 0x21: "speech_alt"}.get(pre, f"0x{pre:02X}")
    slot = T1_B1_NAMES.get(b1, f"0x{b1:02X}")
    return f"pre={pre_name} slot={slot} expr=0x{expr:02X}"


def preview_text_payload(payload: bytes, limit: int = 80) -> str | None:
    """Return a readable preview of a type-1 dialogue payload."""
    pages = parse_t1_pages(payload)
    if not pages:
        return None
    text = r" \p ".join(p.text for p in pages if p.text.strip())
    if not text.strip():
        return None
    if len(text) > limit:
        text = text[:limit] + "..."
    return text


def preview_text_runs(payload: bytes, limit: int = 160, max_runs: int = 4) -> str | None:
    """Return a readable preview of a type-8 (ui/action) payload."""
    lines = parse_t8_lines(payload)
    if not lines:
        return None
    non_empty = [ln for ln in lines if ln.strip()]
    if not non_empty:
        return None
    text = " | ".join(non_empty[:max_runs])
    if len(text) > limit:
        text = text[:limit] + "..."
    return text


def format_type6_args(payload: bytes) -> str:
    if len(payload) < 2:
        return ""
    payload_w = struct.unpack_from("<H", payload, 0)[0]
    sub = payload_w & 0x3FFF
    arg_words = [struct.unpack_from("<H", payload, off)[0] for off in range(2, len(payload), 2)]
    if not arg_words:
        return ""

    if sub == 0x001A:
        return f" hook_id={arg_words[0] & 0x7FFF}"
    if sub in {0x0004, 0x0005}:
        return f" item_id=0x{arg_words[0]:04X}"
    if sub in {0x0011, 0x0021}:
        return f" ticks={arg_words[0]}"
    if sub == 0x0016:
        a = arg_words[0]
        scene_id = a & 0x3FFF
        hi = a >> 14
        # hi bits: 0=activate, 1=overlay, 2=deactivate, 3=deactivate-overlay
        cam_op = {0: "activate", 1: "overlay", 2: "deactivate", 3: "deactivate_overlay"}.get(hi, f"hi={hi}")
        return f" {cam_op} scene=0x{scene_id:02X}"
    if sub == 0x0020 and len(arg_words) >= 2:
        s0 = struct.unpack_from("<h", payload, 2)[0]
        s1 = struct.unpack_from("<h", payload, 4)[0]
        return f" arg0=0x{arg_words[0]:04X} sarg0={s0} arg1=0x{arg_words[1]:04X} sarg1={s1}"

    parts = [f"arg{i}=0x{word:04X}" for i, word in enumerate(arg_words)]
    return " " + " ".join(parts)


def disasm_script(
    bank: bytes,
    start: int,
    end: int,
    max_ops: int = 20000,
    type8_max_runs: int = 4,
) -> list[Op]:
    ops: list[Op] = []
    ip = start
    while ip + 2 <= end and len(ops) < max_ops:
        word = read_u16(bank, ip)
        nibble = word >> 12
        if nibble == 0:
            ops.append(Op(ip, word, -1, 2, b"", "invalid nibble 0"))
            break
        type_idx = nibble - 1
        if type_idx > 14:
            ops.append(Op(ip, word, type_idx, 2, b"", "type>14"))
            break

        after = ip + 2
        size = 2
        note = TYPE_NAMES.get(type_idx, "?")

        try:
            if type_idx == 0:
                # 0x1000 = flag_ctx_end, 0x1001 = flag_ctx_begin
                lo12 = word & 0x0FFF
                note = "flag_ctx_begin" if lo12 else "flag_ctx_end"
                size = 2
            elif type_idx in (1, 8):
                payload_n = word & 0x0FFF
                size = 2 + payload_n
                note = f"{note} payload={payload_n}"
            elif type_idx == 2:
                # Unconditional forward jump: lo12 = byte offset to skip
                lo12 = word & 0x0FFF
                note = f"jump +0x{lo12:03X} ({lo12} bytes)"
                size = 2
            elif type_idx == 3:
                size = 4
                lo12 = word & 0x0FFF
                if after + 2 <= end:
                    arg = read_u16(bank, after)
                    # lo12 encodes condition variant:
                    #   0x000 = check_flag <arg>
                    #   0x001 = skip_if_clear <flag_id arg>
                    #   0x040 = skip_if_set <flag_id arg>
                    #   0x041 = skip_if_set variant
                    if lo12 == 0x000:
                        note = f"check_flag 0x{arg:04X}"
                    elif lo12 == 0x001:
                        note = f"skip_if_clear 0x{arg:04X}"
                    elif lo12 in (0x040, 0x041):
                        note = f"skip_if_set 0x{arg:04X}"
                    else:
                        note = f"compare/branch lo=0x{lo12:03X} arg=0x{arg:04X}"
                if word & 0x800:
                    size = 6
            elif type_idx == 4:
                size = 4
                if after + 2 <= end:
                    flag = read_u16(bank, after)
                    mode = word & 0xF
                    op_name = "set_flag" if mode else "clear_flag"
                    note = f"{op_name} 0x{flag:04X}"
            elif type_idx == 5:
                size = 6
            elif type_idx == 6:
                if after + 2 > end:
                    raise IndexError("type6 missing payload word")
                payload_w = read_u16(bank, after)
                extra = type6_payload_len(word, payload_w)
                size = 4 + extra
                sub = payload_w & 0x3FFF
                sub_name = TYPE6_SUB_NAMES.get(sub, "sub")
                note = f"actor/cam {sub_name} sub=0x{sub:04X} hi={payload_w >> 14} extra={extra}"
            elif type_idx == 7:
                size = 4
                if after + 2 <= end:
                    rel = struct.unpack_from("<h", bank, after)[0]
                    note = f"reljump {rel:+d} -> 0x{after + 2 + rel:X}"
            elif type_idx == 14:
                size = 2
                note = "yield"
            else:
                size = 2
                note = f"{note} (assumed bare)"
        except IndexError as exc:
            ops.append(Op(ip, word, type_idx, 2, b"", f"truncate: {exc}"))
            break

        if ip + size > end:
            # Clamp with warning — still emit.
            size = end - ip
            note += " (clamped)"

        payload = bank[ip + 2 : ip + size]
        if type_idx == 1:
            hdr_str = format_t1_header(payload)
            if hdr_str:
                note += f" [{hdr_str}]"
            preview = preview_text_payload(payload)
            if preview:
                note += f' text="{preview}"'
        elif type_idx == 8:
            preview = preview_text_runs(payload, max_runs=type8_max_runs)
            if preview:
                note += f' text="{preview}"'
        elif type_idx == 6:
            note += format_type6_args(payload)
        ops.append(Op(ip, word, type_idx, size, payload, note))
        if type_idx == 14:
            # Yield often ends a chunk; keep going (multi-chunk scripts are common).
            pass
        ip += size
        if size <= 0:
            break
    return ops


def format_op(op: Op, *, show_payload_hex: bool = False, payload_hex_prefix: int = 32) -> str:
    pay = op.payload.hex() if op.payload else ""
    if show_payload_hex and pay:
        if payload_hex_prefix > 0 and len(pay) > payload_hex_prefix * 2:
            pay = pay[: payload_hex_prefix * 2] + "..."
        pay_s = f"  +{pay}"
    else:
        pay_s = ""
    return (
        f"  +{op.offset:04X}  {op.word:04X}  t{op.type_idx:<2}  "
        f"n={op.word >> 12:X}  sz={op.size:<3}  {op.note}{pay_s}"
    )


def hist(ops: list[Op]) -> str:
    counts = [0] * 15
    for op in ops:
        if 0 <= op.type_idx <= 14:
            counts[op.type_idx] += 1
    parts = [f"t{i}={counts[i]}" for i in range(15) if counts[i]]
    return f"n={len(ops)} " + " ".join(parts)


def load_map(content: Path, stem: str) -> tuple[bytes, bytes, list[ScriptEntry]]:
    path = find_mdp(content, stem)
    data = path.read_bytes()
    secs = mdp_sections(data)
    if 11 not in secs or 12 not in secs:
        raise RuntimeError(f"{path.name}: missing sec[11]/[12]")
    dptr, dsize = secs[11]
    bptr, bsize = secs[12]
    directory = data[dptr : dptr + dsize]
    bank = data[bptr : bptr + bsize]
    entries = parse_directory(directory, len(bank))
    return directory, bank, entries


def load_text_map(text_root: Path, stem: str) -> tuple[bytes, bytes, list[ScriptEntry]]:
    ofs_path, scn_path = None, None
    scn_path, ofs_path = find_text_pair(text_root, stem)
    directory = ofs_path.read_bytes()
    bank = scn_path.read_bytes()
    entries = parse_directory(directory, len(bank))
    return directory, bank, entries


def export_patch(
    stem: str,
    bank: bytes,
    entries: list[ScriptEntry],
    want_ids: set[int] | None,
) -> str:
    """Generate a ready-to-edit patch file from all type-1 ops in the given scripts."""
    lines: list[str] = [
        f"# field_script_patch for map {stem}",
        f"# Generated by field_script_disasm.py  --  edit PAGE lines then run field_script_patch.py",
        f"# Syntax:  SCRIPT 0x<id> / OP 0x<offset> / PAGE <text>",
        f"# \\n = newline within textbox,  \\p = page-break / wait for button",
        "",
    ]
    for entry in entries:
        if want_ids is not None and entry.script_id not in want_ids:
            continue
        ops = disasm_script(bank, entry.offset, entry.end)
        dialogue_ops = [op for op in ops if op.type_idx in (1, 8)]
        if not dialogue_ops:
            continue
        lines.append(f"SCRIPT 0x{entry.script_id:04X}")
        for op in dialogue_ops:
            if op.type_idx == 1:
                hdr_parsed = parse_t1_header(op.payload)
                if hdr_parsed is None:
                    lines.append(f"")
                    lines.append(f"  # OP 0x{op.offset:04X}  skipped (unrecognised type-1 payload format)")
                    continue
                hdr = format_t1_header(op.payload)
                pages = parse_t1_pages(op.payload)
                lines.append(f"")
                non_empty_pages = [pg for pg in pages if pg.text.strip()]
                if not non_empty_pages:
                    lines.append(f"  # OP 0x{op.offset:04X}  # dialogue {hdr}  (no readable text — skipped)")
                    continue
                lines.append(f"  OP 0x{op.offset:04X}  # dialogue {hdr}")
                for pg in non_empty_pages:
                    lines.append(f"  PAGE {pg.text}")
            else:  # type-8
                t8_lines = parse_t8_lines(op.payload)
                lines.append(f"")
                non_empty_lines = [ln for ln in t8_lines if ln.strip()]
                if not non_empty_lines:
                    lines.append(f"  # OP 0x{op.offset:04X}  # ui/action  (no readable text — skipped)")
                    continue
                lines.append(f"  OP 0x{op.offset:04X}  # ui/action (no-portrait speech bubble)")
                for ln in non_empty_lines:
                    lines.append(f"  LINE {ln}")
        lines.append("")
    return "\n".join(lines) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("maps", nargs="+", help="MDP stems, e.g. BA38 2000")
    ap.add_argument("--content", type=Path, default=DEFAULT_CONTENT)
    ap.add_argument("--text", type=Path, default=DEFAULT_TEXT, help="TEXT/<lang> root for SCN/OFS")
    ap.add_argument("--type8-max-runs", type=int, default=4, help="type8 dialogue chunk previews per op")
    ap.add_argument("--payload-hex", action="store_true", help="also print raw payload hex (may be huge)")
    ap.add_argument("--payload-hex-prefix", type=int, default=32, help="max payload bytes to show when --payload-hex")
    ap.add_argument(
        "--source",
        choices=("auto", "mdp", "scn"),
        default="auto",
        help="script source: auto prefers SCN/OFS when present",
    )
    ap.add_argument("--id", action="append", default=[], help="script id hex (e.g. 3000); default all")
    ap.add_argument("--max-ops", type=int, default=5000)
    ap.add_argument("--out", type=Path, help="write listing to file")
    ap.add_argument(
        "--export-patch", type=Path, metavar="FILE",
        help="export a ready-to-edit patch file with all type-1 dialogue ops pre-filled",
    )
    args = ap.parse_args()

    want_ids: set[int] | None = None
    if args.id:
        want_ids = {int(x, 16) for x in args.id}

    lines: list[str] = []
    patch_parts: list[str] = []

    def emit(s: str = "") -> None:
        lines.append(s)
        print(s)

    for stem in args.maps:
        source = args.source
        if source == "auto":
            try:
                directory, bank, entries = load_text_map(args.text, stem)
                source = "scn"
            except FileNotFoundError:
                directory, bank, entries = load_map(args.content, stem)
                source = "mdp"
        elif source == "scn":
            directory, bank, entries = load_text_map(args.text, stem)
        else:
            directory, bank, entries = load_map(args.content, stem)

        if args.export_patch:
            patch_parts.append(export_patch(stem, bank, entries, want_ids))
        else:
            emit(f"=== {stem}  source={source}  bank=0x{len(bank):X}  scripts={len(entries)} ===")
            emit("directory (id @ offset .. end):")
            for e in entries:
                head = " ".join(f"{read_u16(bank, e.offset + i):04X}" for i in range(0, min(8, e.end - e.offset), 2) if e.offset + i + 2 <= len(bank))
                emit(f"  id=0x{e.script_id:04X}  [0x{e.offset:04X}, 0x{e.end:04X})  len=0x{e.end - e.offset:X}  head={head}")

            for e in entries:
                if want_ids is not None and e.script_id not in want_ids:
                    continue
                ops = disasm_script(
                    bank,
                    e.offset,
                    e.end,
                    max_ops=args.max_ops,
                    type8_max_runs=args.type8_max_runs,
                )
                emit(f"\n--- script 0x{e.script_id:04X}  {hist(ops)} ---")
                for op in ops:
                    emit(
                        format_op(
                            op,
                            show_payload_hex=args.payload_hex,
                            payload_hex_prefix=args.payload_hex_prefix,
                        )
                    )
                if ops:
                    last = ops[-1]
                    next_ip = last.offset + last.size
                    if next_ip < e.end:
                        emit(f"  !! stopped early at 0x{next_ip:X}, 0x{e.end - next_ip:X} bytes left")
                    elif next_ip > e.end:
                        emit(f"  !! overran end by 0x{next_ip - e.end:X}")
                    else:
                        emit("  (exact end)")

            emit()

    if args.export_patch:
        args.export_patch.parent.mkdir(parents=True, exist_ok=True)
        args.export_patch.write_text("\n".join(patch_parts), encoding="utf-8")
        print(f"wrote patch template {args.export_patch}", file=sys.stderr)

    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(f"wrote {args.out}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
