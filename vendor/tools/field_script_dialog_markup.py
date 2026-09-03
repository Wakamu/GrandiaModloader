#!/usr/bin/env python3
"""Linear markup for one field-script DialogueOp / UiDialogueOp.

A DialogueOp is one token stream. Markup is that stream, readable:

  [P:57]Operation Yggdrassil is entering
  the final stage! [D:128]Now we only have
  to find the last remaining piece![D:48]
  [P:26][clear]I needn't remind you that the depths
  of these ruins are dangerous.[D:112]

Tokens:
  [P:expr]                  set speaking still (0-based FC##.DAT slot)
  [P:expr slot=0x21]        same, header b1: 0x03 left, 0x12 center, 0x21 right
  [P:off] / [P:off slot=]   extra 0x00 — hide that still slot, not face 0
  [P:keep] / [P:keep slot=] accepted on parse (legacy); no extra is [P:0] or idle [P:1]
  ♥                        mid-line glyph (RawByte 0xD7)
  [voice:play id=0x5aa]     start voice catalog id on channel 0
  [voice:play ch=2 id=0x2d] same on channel 2 (0..2)
  [voice:stop]              stop channel 0
  [voice:stop id=0x20]      stop; leftover id is preserved for byte-identical emit
  [0B:0x2080]               accepted on parse (legacy raw 09 0B u16)
  [box:top] / [box:bottom]  accepted on parse (legacy); ignored on apply
  [swap]                    RawByte 0x08: flip the live present slot after the box opens
                            and clear all portrait slots (a later [P:] is optional)
  [menu]                    stream RawByte 0x05: choice-menu mode. Sits at the
                            start of a Save/Recover box, or just before the
                            option [P:] after a spoken prompt. Not a face —
                            post-header extra 0x05 stays [P:4]. The option
                            still is header-only (no face extra — extra 0x01
                            is a newline and would add a blank pick).
                            [P:off] belongs before [menu], not after.
                            The compare ladder after the op is not markup.
  [D:n]                     HoldToken, n frames
  [TS:252]                  voice-clock timestamp (engine units). [K:0xfc00] accepted (legacy file word)
  [clear]                   PageBreakToken (box text only; portraits stay).
                            Also a page-break sitting immediately after a
                            still — [P:n][clear]text — which is not a face extra.
  [wait]                    wait-for-confirm (no HoldToken). Confirm the
                            current page, then [clear] to wipe; a trailing
                            [wait] confirms the last page. Never [clear][wait].
  [overlay]                 open a second box (RawByte 0x06 before that box's header)
                            only while the live box is bottom; no-op on top.
                            does not clear portraits — [P:off] a slot to hide it
  [/overlay]                close the overlay (Control 09 02); base box stays
  [raw:0a00c0]              verbatim stream bytes that have no mnemonic (signpost
                            packets, leftover pads). Not a displayed glyph.
  [line]                    LineStart (09 01). Type-1 leftover 09 01 still
                            disables confirm; an editor conversion that wants
                            [wait] should drop [line], and apply then strips
                            leftover LineStart from the original prefix.
  [09:C:0x2d80]             generic 09-series control with a u16 (not voice).
                            09 0B stays [voice:]; 09 0A/0C/0D/0E use this.
  [09:03]                   2-byte 09-control (not [line] / [/overlay]).
  [end] / [end:0000]        terminator 07 plus bytes after it, only when that
                            pad is not the even-align 07 / 07 00 apply infers.
  [P:n pre=0x0B]            still header lead byte (default 0x0F omitted).
  [hdr:0b031f0100]          non-still 5-byte header (item-get chrome, etc.).
  Overlay with no [P:]      same-slot second box; a header is synthesized and omitted on serialize
  [[                        literal '['
Real newlines are NewlineToken.

Portrait ``slot=`` is header b1, one of three still windows: 0x03 left
(default, omitted), 0x12 center, 0x21 right. Screen side is not in the
text stream: type-8 opens present slot 1 (top on BA38), type-1 opens slot 0
(bottom). [swap] (0x08) flips the live box after kickoff and clears every
still. [clear] wipes text only. Overlay (0x06) only runs when that live slot
is 0; on slot 1 the engine returns immediately and following text stays in
the current top box. Overlay does not hide stills — use [P:off slot=] for a
specific window. 09 0B is a voice play/stop packet serialized as [voice:].
09 0F FaceKeys are serialized as [TS:n] (engine voice-clock, not a portrait).

Unchanged markup is a no-op on the original token list.

A textbox (bottom, top, or overlay) holds at most 3 visual lines of at most
38 displayed characters (spaces and glyphs count; ``[P:]`` / ``[D:]`` / …
do not). A ``[menu]`` box has no line cap; a portrait on its own line
before the first option is not a displayed row. The editor and
``apply_markup`` refuse edits that go past that budget. Existing vanilla
that already overflows can be saved unchanged.
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from field_script_ir import (
    ControlToken,
    DToken,
    FaceKeyToken,
    HeaderToken,
    HoldToken,
    LineStartToken,
    NewlineToken,
    PageBreakToken,
    RawByteToken,
    TerminatorToken,
    TextToken,
    emit_tokens,
)
from portrait_resolver import (
    dialogue_face_index,
    is_printable_face_extra,
    packed_face_index_to_extra,
)

PRINTABLE_EXTRA = set("".join(chr(c) for c in range(0x20, 0x3D)))


@dataclass(frozen=True)
class TextEvent:
    text: str


@dataclass(frozen=True)
class NewlineEvent:
    pass


@dataclass(frozen=True)
class PortraitEvent:
    expr: int
    b1: int | None = None
    pre: int | None = None


@dataclass(frozen=True)
class SlotOffEvent:
    b1: int | None = None
    pre: int | None = None


@dataclass(frozen=True)
class KeepPortraitEvent:
    b1: int | None = None
    pre: int | None = None


@dataclass(frozen=True)
class DelayEvent:
    frames: int


@dataclass(frozen=True)
class ClearEvent:
    pass


@dataclass(frozen=True)
class WaitEvent:
    pass


@dataclass(frozen=True)
class OverlayEvent:
    pass


@dataclass(frozen=True)
class OverlayEndEvent:
    pass


@dataclass(frozen=True)
class BoxEvent:
    """Legacy [box:] token. Ignored on apply; not emitted on serialize."""
    side: str  # "top" | "bottom"
    layout: int | None = None


@dataclass(frozen=True)
class Ctrl0BEvent:
    """09 0B <u16> voice play/stop. *arg* is the stored little-endian word."""
    arg: int

    @property
    def play(self) -> bool:
        return decode_voice_arg(self.arg)[0]

    @property
    def channel(self) -> int:
        return decode_voice_arg(self.arg)[1]

    @property
    def voice_id(self) -> int:
        return decode_voice_arg(self.arg)[2]


@dataclass(frozen=True)
class SwapEvent:
    """0x08: flip the live present slot after the box has opened."""
    pass


@dataclass(frozen=True)
class MenuEvent:
    """Stream 0x05: enter choice-menu mode. Not a [P:4] face extra."""
    pass


@dataclass(frozen=True)
class FaceKeyEvent:
    """09 0F <u16 LE> voice-clock. *key* is the stored file word."""
    key: int

    @property
    def timestamp(self) -> int:
        return stored_to_timestamp(self.key)


@dataclass(frozen=True)
class RawBytesEvent:
    """Verbatim payload bytes with no mnemonic."""
    data: bytes


@dataclass(frozen=True)
class LineEvent:
    """LineStart (09 01)."""
    pass


@dataclass(frozen=True)
class Ctrl09Event:
    """Generic 09 <sub> [<u16>] that is not voice / overlay-close / line / TS."""
    sub: int
    arg: int | None = None


@dataclass(frozen=True)
class EndEvent:
    """Terminator 0x07 plus the raw bytes that follow it."""
    pad: bytes


@dataclass(frozen=True)
class HdrEvent:
    """Non-still 5-byte page header."""
    pre: int
    b1: int
    b2: int
    b3: int
    expr: int


MarkupEvent = (
    TextEvent
    | NewlineEvent
    | PortraitEvent
    | SlotOffEvent
    | KeepPortraitEvent
    | DelayEvent
    | ClearEvent
    | WaitEvent
    | OverlayEvent
    | OverlayEndEvent
    | BoxEvent
    | Ctrl0BEvent
    | SwapEvent
    | MenuEvent
    | FaceKeyEvent
    | RawBytesEvent
    | LineEvent
    | Ctrl09Event
    | EndEvent
    | HdrEvent
)

# Engine textbox budget: spoken glyphs only (spaces and ♥ count; [P:]/[D:]/…
# tokens do not). Newlines start a visual row. [clear]/[swap]/[overlay]/
# [/overlay] start a new 3-line budget; portrait changes do not.
# [menu] starts a choice box with no line cap (Save/Recover can be 4+
# rows). A portrait parked on its own line before the first option is
# not a displayed row.
BOX_MAX_LINES = 3
BOX_LINE_MAX_CHARS = 38

_DEFAULT_SLOT = 0x03
_DEFAULT_PRE = 0x0F
_STILL_SLOTS = {0x03, 0x12, 0x21}
_STILL_HEADER = (0x0A, 0x0C)
_CTRL09_ARG_SUBS = {0x0A, 0x0B, 0x0C, 0x0D, 0x0E}
# Grandia English font extras that sit in the text stream, not as [P:] / controls.
_TEXT_GLYPHS = {0xD7: "♥"}
_TEXT_GLYPH_BYTES = {ch: byte for byte, ch in _TEXT_GLYPHS.items()}

_TOKEN_RE = re.compile(
    r"\[\[|"
    r"\[P:(off|keep|\d+)(?:\s+slot=(0x[0-9A-Fa-f]+|\d+))?(?:\s+pre=(?:0x[0-9A-Fa-f]+|\d+))?\]|"
    r"\[box:(top|bottom)(?:=(0x[0-9A-Fa-f]+|\d+))?\]|"
    r"\[0B:(0x[0-9A-Fa-f]+|\d+)\]|"
    r"\[voice:(?:play|stop)(?:\s+ch=\d+)?(?:\s+id=(?:0x[0-9A-Fa-f]+|\d+))?\]|"
    r"\[TS:(0x[0-9A-Fa-f]+|\d+)\]|"
    r"\[K:(?:0x[0-9A-Fa-f]+|\d+)\]|"
    r"\[D:(\d+)\]|"
    r"\[clear\]|"
    r"\[wait\]|"
    r"\[swap\]|"
    r"\[menu\]|"
    r"\[overlay\]|"
    r"\[/overlay\]|"
    r"\[raw:[0-9a-fA-F]+\]|"
    r"\[line\]|"
    r"\[09:[0-9A-Fa-f]{1,2}(?::(?:0x[0-9A-Fa-f]+|\d+))?\]|"
    r"\[end(?::[0-9a-fA-F]*)?\]|"
    r"\[hdr:[0-9a-fA-F]+\]|"
    r"\["
)
_RAW_RE = re.compile(r"\[raw:([0-9a-fA-F]+)\]")
_KEY_LEGACY_RE = re.compile(r"\[K:(0x[0-9A-Fa-f]+|\d+)\]")
_VOICE_RE = re.compile(
    r"\[voice:(play|stop)(?:\s+ch=(\d+))?(?:\s+id=(0x[0-9A-Fa-f]+|\d+))?\]"
)
_P_RE = re.compile(
    r"\[P:(off|keep|\d+)(?:\s+slot=(0x[0-9A-Fa-f]+|\d+))?(?:\s+pre=(0x[0-9A-Fa-f]+|\d+))?\]"
)
_CTRL09_RE = re.compile(
    r"\[09:([0-9A-Fa-f]{1,2})(?::(0x[0-9A-Fa-f]+|\d+))?\]"
)
_END_RE = re.compile(r"\[end(?::([0-9a-fA-F]*))?\]")
_HDR_RE = re.compile(r"\[hdr:([0-9a-fA-F]+)\]")


def normalize_markup(text: str) -> str:
    return (text or "").replace("\r\n", "\n").replace("\r", "\n")


def parse_slot_value(raw: str) -> int:
    s = raw.strip()
    if s.lower().startswith("0x"):
        return int(s, 16)
    return int(s)


def parse_markup(text: str) -> list[MarkupEvent]:
    """Parse editor markup into events. Raises ValueError on unknown ``[…]``."""
    src = normalize_markup(text)
    events: list[MarkupEvent] = []
    i = 0
    n = len(src)
    text_buf: list[str] = []

    def flush_text() -> None:
        if text_buf:
            events.append(TextEvent("".join(text_buf)))
            text_buf.clear()

    while i < n:
        if src[i] == "\n":
            flush_text()
            events.append(NewlineEvent())
            i += 1
            continue
        if src[i] != "[":
            text_buf.append(src[i])
            i += 1
            continue
        m = _TOKEN_RE.match(src, i)
        if not m or m.group(0) == "[":
            end = src.find("]", i)
            snippet = src[i: end + 1 if end >= 0 else min(i + 16, n)]
            raise ValueError(f"Unknown markup token {snippet!r}")
        tok = m.group(0)
        if tok == "[[":
            text_buf.append("[")
            i = m.end()
            continue
        flush_text()
        if tok == "[clear]":
            events.append(ClearEvent())
        elif tok == "[wait]":
            events.append(WaitEvent())
        elif tok == "[swap]":
            events.append(SwapEvent())
        elif tok == "[menu]":
            events.append(MenuEvent())
        elif tok == "[overlay]":
            events.append(OverlayEvent())
        elif tok == "[/overlay]":
            events.append(OverlayEndEvent())
        elif tok.startswith("[box:"):
            side = m.group(3)
            if side not in ("top", "bottom"):
                raise ValueError(f"Unknown markup token {tok!r}")
            layout = parse_slot_value(m.group(4)) if m.group(4) else None
            events.append(BoxEvent(side=side, layout=layout))
        elif tok.startswith("[0B:"):
            events.append(Ctrl0BEvent(arg=parse_slot_value(m.group(5))))
        elif tok.startswith("[voice:"):
            vm = _VOICE_RE.match(tok)
            if not vm:
                raise ValueError(f"Unknown markup token {tok!r}")
            play = vm.group(1) == "play"
            channel = int(vm.group(2)) if vm.group(2) else 0
            voice_id = parse_slot_value(vm.group(3)) if vm.group(3) else 0
            if not 0 <= channel <= 3:
                raise ValueError(f"Voice channel must be 0–3, got {channel}")
            if not 0 <= voice_id <= 0x1FFF:
                raise ValueError(f"Voice id must be 0–0x1FFF, got {voice_id:#x}")
            events.append(Ctrl0BEvent(arg=encode_voice_arg(play, channel, voice_id)))
        elif tok.startswith("[TS:"):
            ts = parse_slot_value(m.group(6))
            if not 0 <= ts <= 0xFFFF:
                raise ValueError(f"Timestamp must be 0–65535, got {ts}")
            events.append(FaceKeyEvent(key=timestamp_to_stored(ts)))
        elif tok.startswith("[K:"):
            km = _KEY_LEGACY_RE.match(tok)
            if not km:
                raise ValueError(f"Unknown markup token {tok!r}")
            key = parse_slot_value(km.group(1))
            if not 0 <= key <= 0xFFFF:
                raise ValueError(f"FaceKey must be 0–0xFFFF, got {key:#x}")
            events.append(FaceKeyEvent(key=key))
        elif tok.startswith("[D:"):
            frames = int(m.group(7))
            if frames < 0 or frames > 255:
                raise ValueError(f"Delay frames must be 0–255, got {frames}")
            events.append(DelayEvent(frames))
        elif tok == "[line]":
            events.append(LineEvent())
        elif tok.startswith("[09:"):
            cm = _CTRL09_RE.fullmatch(tok)
            if not cm:
                raise ValueError(f"Unknown markup token {tok!r}")
            sub = int(cm.group(1), 16)
            if cm.group(2) is not None:
                events.append(Ctrl09Event(sub=sub, arg=parse_slot_value(cm.group(2))))
            elif sub in _CTRL09_ARG_SUBS or sub == 0x0F:
                raise ValueError(f"{tok} needs an argument")
            else:
                events.append(Ctrl09Event(sub=sub, arg=None))
        elif tok.startswith("[end"):
            em = _END_RE.fullmatch(tok)
            if not em:
                raise ValueError(f"Unknown markup token {tok!r}")
            hx = em.group(1) or ""
            if len(hx) % 2:
                raise ValueError(f"odd [end:] hex length: {tok!r}")
            events.append(EndEvent(pad=bytes.fromhex(hx)))
        elif tok.startswith("[hdr:"):
            hm = _HDR_RE.fullmatch(tok)
            if not hm:
                raise ValueError(f"Unknown markup token {tok!r}")
            hx = hm.group(1)
            if len(hx) != 10:
                raise ValueError(f"[hdr:] must be 5 bytes, got {tok!r}")
            raw = bytes.fromhex(hx)
            events.append(HdrEvent(pre=raw[0], b1=raw[1], b2=raw[2], b3=raw[3], expr=raw[4]))
        elif tok.startswith("[raw:"):
            rm = _RAW_RE.fullmatch(tok)
            if not rm:
                raise ValueError(f"Unknown markup token {tok!r}")
            hx = rm.group(1)
            if len(hx) % 2:
                raise ValueError(f"odd [raw:] hex length: {tok!r}")
            events.append(RawBytesEvent(data=bytes.fromhex(hx)))
        else:
            pm = _P_RE.fullmatch(tok)
            if not pm:
                raise ValueError(f"Unknown markup token {tok!r}")
            kind = pm.group(1)
            b1 = parse_slot_value(pm.group(2)) if pm.group(2) else None
            pre = parse_slot_value(pm.group(3)) if pm.group(3) else None
            if kind == "off":
                events.append(SlotOffEvent(b1=b1, pre=pre))
            elif kind == "keep":
                events.append(KeepPortraitEvent(b1=b1, pre=pre))
            else:
                events.append(PortraitEvent(expr=int(kind), b1=b1, pre=pre))
        i = m.end()
    flush_text()
    return events


@dataclass(frozen=True)
class BoxLayout:
    """One on-screen textbox's displayed lines (markup tokens stripped)."""
    kind: str  # "bottom" | "top" | "overlay" | "menu"
    lines: list[str]


@dataclass(frozen=True)
class LayoutViolation:
    kind: str  # "chars" | "lines"
    box_kind: str
    line_index: int
    detail: int
    message: str


def box_layout_from_events(
    events: list[MarkupEvent],
    *,
    type8: bool = False,
) -> tuple[list[BoxLayout], list[LayoutViolation]]:
    """Walk markup events into per-box displayed lines and any over-budget rows.

    Overlay on a live top box is a no-op (engine returns immediately).
    ``[overlay]`` and ``[/overlay]`` each start a new 3-line budget; the
    closed box is not written into again. ``[menu]`` starts a choice box
    with no line cap; a leading portrait-only newline is not a row.
    """
    active_side = "top" if type8 else "bottom"
    overlay = False
    in_menu = False
    menu_has_text = False
    lines: list[str] = [""]
    finished: list[BoxLayout] = []

    def kind() -> str:
        if in_menu:
            return "menu"
        return "overlay" if overlay else active_side

    def dump() -> None:
        if any(lines):
            finished.append(BoxLayout(kind=kind(), lines=list(lines)))

    def reset() -> None:
        nonlocal lines
        lines = [""]

    for ev_i, ev in enumerate(events):
        if isinstance(ev, MenuEvent):
            if lines and not lines[-1]:
                lines.pop()
            dump()
            reset()
            in_menu = True
            menu_has_text = False
            continue
        if isinstance(ev, ClearEvent):
            dump()
            reset()
            continue
        if isinstance(ev, SwapEvent):
            dump()
            overlay = False
            active_side = "bottom" if active_side == "top" else "top"
            reset()
            continue
        if isinstance(ev, OverlayEvent):
            if active_side == "top":
                continue
            dump()
            reset()
            overlay = True
            continue
        if isinstance(ev, OverlayEndEvent):
            if not overlay:
                continue
            dump()
            overlay = False
            reset()
            continue
        if isinstance(ev, TextEvent):
            lines[-1] += ev.text
            if ev.text:
                menu_has_text = True
            continue
        if isinstance(ev, NewlineEvent):
            nxt = events[ev_i + 1] if ev_i + 1 < len(events) else None
            if isinstance(nxt, MenuEvent):
                continue
            if in_menu and not menu_has_text and not lines[-1]:
                continue
            lines.append("")
            continue

    dump()

    violations: list[LayoutViolation] = []
    for box in finished:
        if box.kind != "menu" and len(box.lines) > BOX_MAX_LINES:
            violations.append(
                LayoutViolation(
                    kind="lines",
                    box_kind=box.kind,
                    line_index=BOX_MAX_LINES + 1,
                    detail=len(box.lines),
                    message=(
                        f"{box.kind} textbox has {len(box.lines)} lines "
                        f"(max {BOX_MAX_LINES})"
                    ),
                )
            )
        for i, line in enumerate(box.lines, 1):
            if len(line) > BOX_LINE_MAX_CHARS:
                violations.append(
                    LayoutViolation(
                        kind="chars",
                        box_kind=box.kind,
                        line_index=i,
                        detail=len(line),
                        message=(
                            f"{box.kind} textbox line {i} is {len(line)} characters "
                            f"(max {BOX_LINE_MAX_CHARS})"
                        ),
                    )
                )
    return finished, violations


def markup_box_layout(
    markup: str,
    *,
    type8: bool = False,
) -> tuple[list[BoxLayout], list[LayoutViolation]]:
    return box_layout_from_events(parse_markup(markup), type8=type8)


def layout_overflow_score(violations: list[LayoutViolation]) -> int:
    """How far *violations* exceed the 3×38 budget. 0 means every box fits."""
    score = 0
    for item in violations:
        if item.kind == "lines":
            score += max(0, item.detail - BOX_MAX_LINES) * 10000
        elif item.kind == "chars":
            score += max(0, item.detail - BOX_LINE_MAX_CHARS)
    return score


def validate_markup_box_layout(markup: str, *, type8: bool = False) -> None:
    """Raise ValueError if a non-menu textbox exceeds 3×38, or any line is over 38."""
    _, violations = markup_box_layout(markup, type8=type8)
    if violations:
        raise ValueError(violations[0].message)


def _is_still_header(tok: DToken) -> bool:
    """True portrait header: 0F/0B/21 + 0A 0C and a left/center/right still slot.

    Item-get chrome is Header(pre=0x0B, b2=0x1F, b3=0x01) with b1=0x00 — not a still.
    """
    return (
        isinstance(tok, HeaderToken)
        and (tok.b2, tok.b3) == _STILL_HEADER
        and tok.b1 in _STILL_SLOTS
    )


def _is_overlay_close(tok: DToken) -> bool:
    return isinstance(tok, ControlToken) and tok.sub == 0x02 and tok.arg is None


def _is_0b_control(tok: DToken) -> bool:
    return isinstance(tok, ControlToken) and tok.sub == 0x0B and tok.arg is not None


def decode_voice_arg(arg: int) -> tuple[bool, int, int]:
    """Split the stored little-endian 09 0B word into (play, channel, id)."""
    engine = ((int(arg) & 0xFF) << 8) | ((int(arg) >> 8) & 0xFF)
    play = (engine & 0x8000) == 0
    channel = (engine >> 13) & 3
    voice_id = engine & 0x1FFF
    return play, channel, voice_id


def stored_to_timestamp(stored: int) -> int:
    """Byte-swap the stored 09 0F word into the engine voice-clock value."""
    word = int(stored) & 0xFFFF
    return ((word & 0xFF) << 8) | (word >> 8)


def timestamp_to_stored(timestamp: int) -> int:
    """Pack an engine voice-clock value back into the stored 09 0F word."""
    return stored_to_timestamp(timestamp)


def encode_voice_arg(play: bool, channel: int, voice_id: int) -> int:
    """Pack play/channel/id back into the stored little-endian 09 0B word."""
    engine = (0 if play else 0x8000) | ((int(channel) & 3) << 13) | (int(voice_id) & 0x1FFF)
    return ((engine & 0xFF) << 8) | ((engine >> 8) & 0xFF)


def _format_0b(arg: int) -> str:
    play, channel, voice_id = decode_voice_arg(arg)
    parts = ["play" if play else "stop"]
    if channel:
        parts.append(f"ch={channel}")
    if voice_id:
        parts.append(f"id={voice_id:#x}")
    return "[voice:" + " ".join(parts) + "]"


def _format_face_key(key: int) -> str:
    return f"[TS:{stored_to_timestamp(key)}]"


def _preceding_header(tokens: list[DToken], i: int) -> bool:
    prev = i - 1
    while prev >= 0 and _is_hidden(tokens[prev]):
        prev -= 1
    return prev >= 0 and isinstance(tokens[prev], HeaderToken)


def _is_exclusive_swap(tokens: list[DToken], i: int) -> bool:
    """RawByte 0x08 that flips the live present slot.

    Vanilla uses this both before a new header (BA38 #46) and before more
    text with no still (Parm "OWWW!" / "... Jussstiiiin!"). 0x08 immediately
    after a Header is a 1-based face extra, not a swap.
    """
    tok = tokens[i] if i < len(tokens) else None
    if not (isinstance(tok, RawByteToken) and tok.value == 0x08):
        return False
    if _preceding_header(tokens, i):
        return False
    j = _skip_hidden_after_overlay(tokens, i + 1)
    if j >= len(tokens):
        return False
    nxt = tokens[j]
    return isinstance(
        nxt, (HeaderToken, TextToken, NewlineToken, HoldToken, PageBreakToken)
    )


def _skip_hidden_after_overlay(tokens: list[DToken], j: int) -> int:
    n = len(tokens)
    while j < n:
        tok = tokens[j]
        if isinstance(tok, (FaceKeyToken, LineStartToken)):
            j += 1
            continue
        if isinstance(tok, ControlToken) and not _is_overlay_close(tok):
            j += 1
            continue
        if isinstance(tok, RawByteToken) and tok.value in {0x00, 0x02}:
            j += 1
            continue
        break
    return j


def _follows_with_header(tokens: list[DToken], i: int) -> bool:
    """True if token *i* is followed by a Header, skipping hidden junk."""
    j = _skip_hidden_after_overlay(tokens, i + 1)
    return j < len(tokens) and isinstance(tokens[j], HeaderToken)


def _is_overlay_open(tokens: list[DToken], i: int) -> bool:
    tok = tokens[i] if i < len(tokens) else None
    if not (isinstance(tok, RawByteToken) and tok.value == 0x06):
        return False
    if _follows_with_header(tokens, i):
        return True
    # Stray 0x06 parked next to 09 02 (old [P:][overlay]text[/overlay] apply).
    j = _skip_hidden_after_overlay(tokens, i + 1)
    return j < len(tokens) and _is_overlay_close(tokens[j])


def _is_hidden(tok: DToken) -> bool:
    if _is_overlay_close(tok) or _is_0b_control(tok):
        return False
    return isinstance(tok, ControlToken)


def _next_significant(tokens: list[DToken], i: int) -> DToken | None:
    while i < len(tokens):
        tok = tokens[i]
        if isinstance(
            tok,
            (HoldToken, HeaderToken, TextToken, NewlineToken, PageBreakToken, TerminatorToken),
        ):
            return tok
        i += 1
    return None


_HEADER_EXTRA_SKIP = {0x00, 0x02, 0x06}


def _header_extra(tokens: list[DToken], header_i: int) -> tuple[int | None, bool]:
    """Return (extra byte or None, extra lives in following TextToken)."""
    j = header_i + 1
    n = len(tokens)
    while j < n and isinstance(tokens[j], RawByteToken) and tokens[j].value in _HEADER_EXTRA_SKIP:
        j += 1
    if j < n and isinstance(tokens[j], RawByteToken):
        val = tokens[j].value
        if val == 0x00:
            return None, False
        return val, False
    if j < n and isinstance(tokens[j], TextToken) and tokens[j].text[:1] in PRINTABLE_EXTRA:
        return ord(tokens[j].text[0]), True
    return None, False


def _is_header_face_extra(tokens: list[DToken], i: int) -> bool:
    """True if tokens[i] is the 1-based face extra sitting after a Header."""
    if i <= 0 or i >= len(tokens):
        return False
    tok = tokens[i]
    if not isinstance(tok, RawByteToken) or tok.value in _HEADER_EXTRA_SKIP:
        return False
    prev = i - 1
    header_i: int | None = None
    while prev >= 0:
        if isinstance(tokens[prev], HeaderToken):
            header_i = prev
            break
        if isinstance(tokens[prev], RawByteToken) and tokens[prev].value in _HEADER_EXTRA_SKIP:
            prev -= 1
            continue
        if _is_hidden(tokens[prev]):
            prev -= 1
            continue
        return False
    if header_i is None:
        return False
    extra, in_text = _header_extra(tokens, header_i)
    if in_text or extra is None:
        return False
    j = header_i + 1
    n = len(tokens)
    while j < n and isinstance(tokens[j], RawByteToken) and tokens[j].value in _HEADER_EXTRA_SKIP:
        j += 1
    return j == i


def _raw_belongs_to_header(tokens: list[DToken], i: int) -> bool:
    """True if this RawByte is already covered by [P:] / [P:off]."""
    if _is_header_face_extra(tokens, i):
        return True
    tok = tokens[i] if 0 <= i < len(tokens) else None
    if not isinstance(tok, RawByteToken):
        return False
    prev = i - 1
    while prev >= 0 and (
        _is_hidden(tokens[prev])
        or (
            isinstance(tokens[prev], RawByteToken)
            and tokens[prev].value in _HEADER_EXTRA_SKIP
        )
    ):
        prev -= 1
    if prev < 0 or not isinstance(tokens[prev], HeaderToken):
        return False
    extra, _in_text = _header_extra(tokens, prev)
    if tok.value == 0x00 and extra is None:
        return True
    # 0x06 after a header is a real stream byte (same-slot overlay / pad),
    # not covered by [P:]. 0x02 after a header is PageBreakToken, not RawByte.
    return False


def _is_named_stream_raw(tokens: list[DToken], i: int) -> bool:
    if i < 0 or i >= len(tokens) or not isinstance(tokens[i], RawByteToken):
        return False
    val = tokens[i].value
    if val in _TEXT_GLYPHS:
        return True
    if is_stream_menu(tokens, i) or _is_overlay_open(tokens, i) or _is_exclusive_swap(tokens, i):
        return True
    return _raw_belongs_to_header(tokens, i)


def is_stream_menu(tokens: list[DToken], i: int) -> bool:
    """RawByte 0x05 that starts choice-menu mode, not a [P:4] face extra."""
    if i < 0 or i >= len(tokens):
        return False
    tok = tokens[i]
    if not (isinstance(tok, RawByteToken) and tok.value == 0x05):
        return False
    return not _is_header_face_extra(tokens, i)


def _tokens_until_menu_text(tokens: list[DToken], menu_i: int) -> list[int]:
    """Indices after stream 0x05 until the first option TextToken."""
    out: list[int] = []
    j = menu_i + 1
    while j < len(tokens) and not isinstance(tokens[j], (TextToken, TerminatorToken)):
        out.append(j)
        j += 1
    return out


def menu_opener_needs_fix(tokens: list[DToken]) -> bool:
    """True if the post-[menu] opener is not vanilla (one bare header, then text).

    A face extra after that header is stored as ``0x01`` for [P:0], which the
    engine reads as a newline — a blank first pick. A second header
    ([P:off]) is another blank pick.
    """
    for i in range(len(tokens)):
        if not is_stream_menu(tokens, i):
            continue
        headers = 0
        for j in _tokens_until_menu_text(tokens, i):
            if isinstance(tokens[j], HeaderToken):
                headers += 1
                extra, _ = _header_extra(tokens, j)
                if extra is not None or headers > 1:
                    return True
    return False


def _is_slot_clear(tokens: list[DToken], header_i: int) -> bool:
    """Header + extra 0x00 and no face extra — hide this box, not show face 0."""
    extra, _ = _header_extra(tokens, header_i)
    if extra is not None:
        return False
    j = header_i + 1
    return j < len(tokens) and isinstance(tokens[j], RawByteToken) and tokens[j].value == 0x00


def _format_p(
    kind: str,
    b1: int,
    last_b1: int | None,
    pre: int = _DEFAULT_PRE,
    last_pre: int = _DEFAULT_PRE,
) -> str:
    bits: list[str] = []
    show_slot = b1 != _DEFAULT_SLOT or (last_b1 is not None and b1 != last_b1)
    if show_slot:
        bits.append(f"slot={b1:#04x}")
    if pre != last_pre:
        bits.append(f"pre={pre:#04x}")
    if bits:
        return f"[P:{kind} {' '.join(bits)}]"
    return f"[P:{kind}]"


def _format_09_control(tok: ControlToken) -> str:
    sub = int(tok.sub)
    if tok.arg is None:
        return f"[09:{sub:02X}]"
    return f"[09:{sub:X}:{int(tok.arg):#x}]"


def _still_wants_face_extra(events: list[MarkupEvent], start: int) -> bool:
    """False when [clear] / [raw:06] sits on the still before text.

    Vanilla then has Header + 0x02 / 0x06 + text, not a 1-based face extra.
    [P:0] never writes extra 0x01 — that byte is NewlineToken (``\\n``).
    [overlay] after [P:] is the editor form (0x06 before that header) and
    still has a face extra. Another still or spoken text also gets the extra.
    """
    ev0 = events[start]
    if isinstance(ev0, PortraitEvent) and ev0.expr == 0:
        return False
    for ev in events[start + 1 :]:
        if isinstance(ev, TextEvent):
            # Idle [P:1] plus a letter or '=' is header-only. Extra 0x02 is
            # [clear]; a printable extra (space / digit / …) was stripped.
            if (
                isinstance(ev0, PortraitEvent)
                and ev0.expr == 1
                and ev.text[:1] not in PRINTABLE_EXTRA
            ):
                return False
            return True
        if isinstance(ev, NewlineEvent):
            # Idle [P:1]\\n is a blank first row, not face extra 0x02 (page-break).
            if isinstance(ev0, PortraitEvent) and ev0.expr == 1:
                return False
            return True
        if isinstance(
            ev,
            (
                LineEvent,
                Ctrl0BEvent,
                Ctrl09Event,
                FaceKeyEvent,
                DelayEvent,
                WaitEvent,
                BoxEvent,
            ),
        ):
            continue
        if isinstance(ev, ClearEvent):
            return False
        if isinstance(ev, RawBytesEvent) and ev.data == b"\x06":
            return False
        if isinstance(
            ev,
            (
                PortraitEvent,
                SlotOffEvent,
                KeepPortraitEvent,
                SwapEvent,
                MenuEvent,
                HdrEvent,
                OverlayEndEvent,
                EndEvent,
            ),
        ):
            return True
    return True


def _inferred_term_pad(content_len: int) -> bytes:
    """Even-align pad apply-from-empty uses after 0x07."""
    return b"\x00" if content_len % 2 == 0 else b""


def _even_payload(data: bytes) -> bytes:
    return data if len(data) % 2 == 0 else data + b"\x00"


def _portrait_expr(
    header: HeaderToken,
    extra: int | None,
    *,
    map_stem: str | None,
    field_root: Path | None,
) -> int:
    controls = [] if extra is None else [extra]
    expr = dialogue_face_index(
        header_expr=header.expr,
        face_key=None,
        controls=controls,
        has_header=True,
        map_stem=map_stem,
        b1_slot=header.b1,
        field_root=field_root,
    )
    return 0 if expr is None else int(expr)


def _escape_text(text: str) -> str:
    return (text or "").replace("[", "[[")


def tokens_to_markup(
    tokens: list[DToken],
    *,
    map_stem: str | None = None,
    field_root: Path | None = None,
    type8: bool = False,
) -> str:
    """Serialize a dialogue token stream to editor markup."""
    out: list[str] = []
    last_b1: int | None = None
    last_pre = _DEFAULT_PRE
    pending_spoken = False
    spoken_emitted = False
    # True after displayed text/newline in the current box. Hold does not
    # reset this — a following newline is a real line, not a page separator.
    box_has_text = False
    strip_text_extra = False
    just_opened_overlay = False
    i = 0
    n = len(tokens)

    def emit_wait() -> None:
        nonlocal pending_spoken
        if pending_spoken and not type8:
            out.append("[wait]")
            pending_spoken = False

    while i < n:
        tok = tokens[i]
        if isinstance(tok, TerminatorToken):
            emit_wait()
            extra = bytearray(tok.trailing or b"")
            j = i + 1
            while j < n and isinstance(tokens[j], RawByteToken):
                extra.append(tokens[j].value)
                j += 1
            content = emit_tokens(tokens[:i])
            inferred = _inferred_term_pad(len(content))
            actual_tail = bytes([0x07]) + bytes(extra)
            infer_tail = bytes([0x07]) + inferred
            if (not out) or _even_payload(content + actual_tail) != _even_payload(content + infer_tail):
                out.append(f"[end:{bytes(extra).hex()}]" if extra else "[end]")
            break
        if _is_overlay_close(tok):
            out.append("[/overlay]")
            just_opened_overlay = False
            box_has_text = False
            i += 1
            continue
        if isinstance(tok, ControlToken):
            if tok.sub == 0x0B and tok.arg is not None:
                out.append(_format_0b(int(tok.arg)))
            else:
                out.append(_format_09_control(tok))
            i += 1
            continue
        if isinstance(tok, FaceKeyToken):
            out.append(_format_face_key(int(tok.face_key)))
            i += 1
            continue
        if isinstance(tok, LineStartToken):
            out.append("[line]")
            i += 1
            continue
        if _is_hidden(tok):
            i += 1
            continue
        if isinstance(tok, HeaderToken):
            if not _is_still_header(tok):
                out.append(
                    f"[hdr:{tok.pre:02x}{tok.b1:02x}{tok.b2:02x}{tok.b3:02x}{tok.expr:02x}]"
                )
                last_b1 = tok.b1
                last_pre = tok.pre
                just_opened_overlay = False
                strip_text_extra = False
                i += 1
                continue
            pending_spoken = False
            extra, in_text = _header_extra(tokens, i)
            if (
                just_opened_overlay
                and extra is None
                and not _is_slot_clear(tokens, i)
                and last_b1 is not None
                and tok.b1 == last_b1
            ):
                # Same-slot overlay with no still: [overlay]text[/overlay]
                last_b1 = tok.b1
                last_pre = tok.pre
                just_opened_overlay = False
                strip_text_extra = False
                i += 1
                continue
            just_opened_overlay = False
            if _is_slot_clear(tokens, i):
                out.append(_format_p("off", tok.b1, last_b1, tok.pre, last_pre))
                last_b1 = tok.b1
                last_pre = tok.pre
                strip_text_extra = False
                i += 1
                continue
            if extra is None:
                # No face extra: a replacement header after spoken text in this
                # op is header.expr (Parm "That awful man" is really [P:0]).
                # Opening an empty first slot uses idle original DAT 1
                # (Parm "Oh, Sue!").
                if spoken_emitted:
                    expr = int(tok.expr)
                elif tok.b1 == _DEFAULT_SLOT:
                    expr = 1
                else:
                    expr = int(tok.expr)
                out.append(_format_p(str(expr), tok.b1, last_b1, tok.pre, last_pre))
                last_b1 = tok.b1
                last_pre = tok.pre
                strip_text_extra = False
                i += 1
                continue
            expr = _portrait_expr(tok, extra, map_stem=map_stem, field_root=field_root)
            out.append(_format_p(str(expr), tok.b1, last_b1, tok.pre, last_pre))
            last_b1 = tok.b1
            last_pre = tok.pre
            skip_has_06 = False
            j = i + 1
            while j < n and isinstance(tokens[j], RawByteToken) and tokens[j].value in _HEADER_EXTRA_SKIP:
                if tokens[j].value == 0x06:
                    skip_has_06 = True
                j += 1
            # [raw:06]text keeps the extra glyph in the text; apply does not
            # write a second face extra after 0x06.
            strip_text_extra = in_text and not skip_has_06
            i += 1
            continue
        if isinstance(tok, TextToken):
            chunk = tok.text
            if strip_text_extra and chunk[:1] in PRINTABLE_EXTRA:
                chunk = chunk[1:]
            strip_text_extra = False
            if chunk:
                out.append(_escape_text(chunk))
                pending_spoken = True
                spoken_emitted = True
                box_has_text = True
            i += 1
            continue
        if isinstance(tok, NewlineToken):
            out.append("\n")
            pending_spoken = True
            spoken_emitted = True
            box_has_text = True
            strip_text_extra = False
            i += 1
            continue
        if isinstance(tok, PageBreakToken):
            nxt = _next_significant(tokens, i + 1)
            if pending_spoken and not isinstance(nxt, HoldToken):
                emit_wait()
            else:
                pending_spoken = False
            out.append("[clear]")
            box_has_text = False
            strip_text_extra = False
            i += 1
            continue
        if isinstance(tok, HoldToken):
            out.append(f"[D:{tok.hold}]")
            pending_spoken = False
            strip_text_extra = False
            i += 1
            continue
        if isinstance(tok, RawByteToken):
            if tok.value in _TEXT_GLYPHS:
                out.append(_TEXT_GLYPHS[tok.value])
                pending_spoken = True
                spoken_emitted = True
                box_has_text = True
                i += 1
                continue
            if is_stream_menu(tokens, i):
                out.append("[menu]")
                just_opened_overlay = False
                i += 1
                continue
            if _is_overlay_open(tokens, i):
                out.append("[overlay]")
                just_opened_overlay = True
                box_has_text = False
                i += 1
                continue
            if _is_exclusive_swap(tokens, i):
                out.append("[swap]")
                just_opened_overlay = False
                box_has_text = False
                i += 1
                continue
            if _raw_belongs_to_header(tokens, i):
                i += 1
                continue
            buf: list[int] = []
            while i < n and isinstance(tokens[i], RawByteToken) and not _is_named_stream_raw(tokens, i):
                buf.append(tokens[i].value)
                i += 1
            if buf:
                out.append(f"[raw:{bytes(buf).hex()}]")
            just_opened_overlay = False
            continue
        i += 1

    emit_wait()
    return "".join(out)


def _clone_header(src: HeaderToken | None, b1: int | None) -> HeaderToken:
    if src is None:
        return HeaderToken(pre=0x0F, b1=b1 if b1 is not None else 0x03, b2=0x0A, b3=0x0C, expr=0)
    return HeaderToken(
        pre=src.pre,
        b1=b1 if b1 is not None else src.b1,
        b2=src.b2,
        b3=src.b3,
        expr=src.expr,
    )


def _write_extra(tokens: list[DToken], header_i: int, extra: int) -> None:
    j = header_i + 1
    while j < len(tokens) and isinstance(tokens[j], RawByteToken) and tokens[j].value in _HEADER_EXTRA_SKIP:
        j += 1
    if j < len(tokens) and isinstance(tokens[j], TextToken) and tokens[j].text:
        rest = tokens[j].text
        if rest[:1] in PRINTABLE_EXTRA:
            rest = rest[1:]
        if is_printable_face_extra(extra):
            tokens[j] = TextToken(chr(extra) + rest)
        else:
            tokens.insert(j, RawByteToken(extra))
            tokens[j + 1] = TextToken(rest)
        return
    if is_printable_face_extra(extra) and j < len(tokens) and isinstance(tokens[j], TextToken):
        tokens[j] = TextToken(chr(extra) + tokens[j].text)
        return
    tokens.insert(j, RawByteToken(extra))


def _merge_printable_face_extras(tokens: list[DToken]) -> None:
    """Fold a printable header extra RawByte into the following TextToken.

    Apply writes the extra before text exists, so empty-original rebuilds
    used to be Header + RawByte + Text. Vanilla stores that extra as the
    first character of the TextToken.
    """
    i = 0
    while i < len(tokens) - 1:
        cur, nxt = tokens[i], tokens[i + 1]
        if (
            isinstance(cur, RawByteToken)
            and is_printable_face_extra(cur.value)
            and isinstance(nxt, TextToken)
            and _is_header_face_extra(tokens, i)
        ):
            tokens[i : i + 2] = [TextToken(chr(cur.value) + nxt.text)]
            continue
        i += 1


def _split_prefix(tokens: list[DToken]) -> tuple[list[DToken], list[DToken], TerminatorToken | None]:
    prefix: list[DToken] = []
    term: TerminatorToken | None = None
    rest: list[DToken] = []
    seen_content = False
    for i, tok in enumerate(tokens):
        if isinstance(tok, TerminatorToken):
            term = tok
            continue
        if not seen_content and _is_overlay_close(tok):
            seen_content = True
            rest.append(tok)
            continue
        if not seen_content and _is_overlay_open(tokens, i):
            seen_content = True
            rest.append(tok)
            continue
        if not seen_content and isinstance(tok, ControlToken):
            seen_content = True
            rest.append(tok)
            continue
        if not seen_content and _is_exclusive_swap(tokens, i):
            seen_content = True
            rest.append(tok)
            continue
        if not seen_content and is_stream_menu(tokens, i):
            seen_content = True
            rest.append(tok)
            continue
        if not seen_content and _is_hidden(tok):
            prefix.append(tok)
            continue
        if not seen_content and isinstance(tok, HeaderToken) and not _is_still_header(tok):
            prefix.append(tok)
            continue
        if not seen_content and isinstance(tok, RawByteToken):
            prefix.append(tok)
            continue
        seen_content = True
        rest.append(tok)
    return prefix, rest, term


def _first_header(tokens: list[DToken]) -> HeaderToken | None:
    for tok in tokens:
        if _is_still_header(tok):
            return tok
    return None


def _append_markup_text(out: list[DToken], text: str) -> None:
    buf: list[str] = []
    for ch in text:
        raw = _TEXT_GLYPH_BYTES.get(ch)
        if raw is None:
            buf.append(ch)
            continue
        if buf:
            out.append(TextToken("".join(buf)))
            buf = []
        out.append(RawByteToken(raw))
    if buf:
        out.append(TextToken("".join(buf)))


def _append_hold(out: list[DToken], frames: int) -> None:
    out.append(HoldToken(hold=max(0, min(255, int(frames)))))


def apply_markup(
    original: list[DToken],
    markup: str,
    *,
    type8: bool = False,
    map_stem: str | None = None,
    field_root: Path | None = None,
) -> list[DToken]:
    """Rebuild tokens from markup. Unchanged markup returns a copy of *original*.

    Type-1 ``[wait]`` cannot keep a leftover ``09 01`` (LineStart). That
    packet XORs present flag ``0x400``, which skips the confirm prompt.
    """
    text = normalize_markup(markup)
    current = tokens_to_markup(
        original, map_stem=map_stem, field_root=field_root, type8=type8
    )
    wants_confirm = (not type8) and ("[wait]" in text)
    leftover_line_start = any(isinstance(t, LineStartToken) for t in original)
    if (
        text == current
        and not (wants_confirm and leftover_line_start)
        and not menu_opener_needs_fix(original)
    ):
        return list(original)

    events = parse_markup(text)
    if not events:
        raise ValueError("Dialog markup cannot be empty")
    if original:
        _, old_violations = markup_box_layout(current, type8=type8)
        _, new_violations = box_layout_from_events(events, type8=type8)
        if layout_overflow_score(new_violations) > layout_overflow_score(old_violations):
            raise ValueError(new_violations[0].message)

    prefix, _rest, term = _split_prefix(original)
    if wants_confirm:
        prefix = [t for t in prefix if not isinstance(t, LineStartToken)]
    src_header = _first_header(original)
    last_b1: int | None = None
    out: list[DToken] = list(prefix)
    spoken_open = False
    pending_header_i: int | None = None
    pending_overlay = False
    synth_overlay_header = False
    pending_swap = False
    skip_before = -1
    skip_event_ids: set[int] = set()
    end_pad: bytes | None = None

    def emit_hold(frames: int) -> None:
        _append_hold(out, frames)

    def resolve_b1(ev_b1: int | None) -> int:
        if ev_b1 is not None:
            return ev_b1
        if last_b1 is not None:
            return last_b1
        return _DEFAULT_SLOT

    def emit_header(b1: int, *, pre: int | None = None) -> int:
        nonlocal last_b1, src_header
        hdr = _clone_header(src_header, b1)
        if pre is not None and pre != hdr.pre:
            hdr = HeaderToken(pre=pre, b1=hdr.b1, b2=hdr.b2, b3=hdr.b3, expr=hdr.expr)
        out.append(hdr)
        last_b1 = hdr.b1
        src_header = hdr
        return len(out) - 1

    def flush_type8_hold() -> None:
        nonlocal spoken_open
        if type8 and spoken_open and original:
            emit_hold(30)
            spoken_open = False

    def attach_extra_if_needed(header_i: int, expr: int, b1: int | None) -> None:
        extra = packed_face_index_to_extra(
            expr, map_stem=map_stem, b1_slot=b1, field_root=field_root
        )
        _write_extra(out, header_i, extra)

    def flush_swap() -> None:
        nonlocal pending_swap
        if pending_swap:
            out.append(RawByteToken(0x08))
            pending_swap = False

    def flush_pending_overlay() -> None:
        nonlocal pending_overlay
        if pending_overlay:
            out.append(RawByteToken(0x06))
            pending_overlay = False

    def insert_overlay_before_last_header() -> bool:
        """0x06 must sit immediately before the overlay box's header."""
        for i in range(len(out) - 1, -1, -1):
            if isinstance(out[i], HeaderToken):
                if i > 0 and isinstance(out[i - 1], RawByteToken) and out[i - 1].value == 0x06:
                    return True
                out.insert(i, RawByteToken(0x06))
                return True
        return False

    def last_header_has_spoken() -> bool:
        for i in range(len(out) - 1, -1, -1):
            if isinstance(out[i], HeaderToken):
                return any(
                    isinstance(t, (TextToken, NewlineToken, HoldToken, PageBreakToken))
                    for t in out[i + 1 :]
                )
        return False

    def ensure_overlay_header() -> None:
        nonlocal synth_overlay_header, pending_overlay
        if not synth_overlay_header:
            return
        flush_swap()
        flush_pending_overlay()
        if pending_overlay:
            out.append(RawByteToken(0x06))
            pending_overlay = False
        emit_header(last_b1 if last_b1 is not None else _DEFAULT_SLOT)
        synth_overlay_header = False

    def overlay_targets_next_header(start: int) -> bool:
        for later in events[start + 1 :]:
            if isinstance(later, (PortraitEvent, SlotOffEvent, KeepPortraitEvent)):
                return True
            if isinstance(later, OverlayEndEvent):
                return False
        return False

    def emit_still(e: MarkupEvent, *, face_extra: bool) -> None:
        nonlocal synth_overlay_header, pending_header_i
        synth_overlay_header = False
        flush_type8_hold()
        flush_swap()
        flush_pending_overlay()
        if isinstance(e, SlotOffEvent):
            header_i = emit_header(resolve_b1(e.b1), pre=e.pre)
            out.insert(header_i + 1, RawByteToken(0x00))
            return
        if isinstance(e, KeepPortraitEvent):
            emit_header(resolve_b1(e.b1), pre=e.pre)
            return
        if isinstance(e, PortraitEvent):
            b1 = resolve_b1(e.b1)
            header_i = emit_header(b1, pre=e.pre)
            pending_header_i = header_i
            if face_extra:
                attach_extra_if_needed(header_i, e.expr, b1)

    for ev_i, ev in enumerate(events):
        if ev_i < skip_before or ev_i in skip_event_ids:
            continue
        if isinstance(ev, OverlayEvent):
            if overlay_targets_next_header(ev_i):
                pending_overlay = True
                synth_overlay_header = False
            elif last_header_has_spoken():
                pending_overlay = True
                synth_overlay_header = True
            elif insert_overlay_before_last_header():
                synth_overlay_header = False
            else:
                pending_overlay = True
                synth_overlay_header = True
            continue
        if isinstance(ev, OverlayEndEvent):
            ensure_overlay_header()
            if pending_overlay:
                insert_overlay_before_last_header()
                pending_overlay = False
            out.append(ControlToken(sub=0x02))
            synth_overlay_header = False
            continue
        if isinstance(ev, SwapEvent):
            pending_swap = True
            continue
        if isinstance(ev, MenuEvent):
            j = ev_i + 1
            cluster: list[MarkupEvent] = []
            while j < len(events) and isinstance(
                events[j],
                (PortraitEvent, SlotOffEvent, KeepPortraitEvent, NewlineEvent),
            ):
                cluster.append(events[j])
                j += 1
            skip_before = j
            offs = [e for e in cluster if isinstance(e, SlotOffEvent)]
            nls = [e for e in cluster if isinstance(e, NewlineEvent)]
            faces = [
                e for e in cluster
                if isinstance(e, (PortraitEvent, KeepPortraitEvent))
            ]
            prelude, last = faces[:-1], faces[-1:]
            last_i = cluster.index(last[0]) if last else -1
            offs_before = [e for e in offs if last_i < 0 or cluster.index(e) < last_i]
            offs_after = [e for e in offs if last_i >= 0 and cluster.index(e) > last_i]
            # [menu][P:0][P:off] hides the other slot before 0x05.
            # [menu][P:off][P:0] is 05, hide, option (vanilla after a prompt).
            for e in (*offs_after, *prelude):
                emit_still(e, face_extra=True)
            # 0x05 after a header extra is read as that header's face id.
            if out and (
                isinstance(out[-1], HeaderToken)
                or (
                    isinstance(out[-1], RawByteToken)
                    and out[-1].value in _HEADER_EXTRA_SKIP
                )
            ):
                out.append(NewlineToken())
            out.append(RawByteToken(0x05))
            for e in offs_before:
                emit_still(e, face_extra=True)
            last_idx = None
            for k in range(ev_i + 1, skip_before):
                if isinstance(events[k], PortraitEvent):
                    last_idx = k
            for e in last:
                if isinstance(e, PortraitEvent):
                    b1 = resolve_b1(e.b1)
                    header_i = emit_header(b1, pre=e.pre)
                    j = skip_before
                    if (
                        j < len(events)
                        and isinstance(events[j], RawBytesEvent)
                        and events[j].data
                        and events[j].data != b"\x06"
                        and all(b in _HEADER_EXTRA_SKIP for b in events[j].data)
                    ):
                        for b in events[j].data:
                            out.append(RawByteToken(b))
                        skip_event_ids.add(j)
                    if last_idx is not None and _still_wants_face_extra(events, last_idx):
                        attach_extra_if_needed(header_i, e.expr, b1)
                else:
                    emit_still(e, face_extra=False)
            for _ in nls:
                out.append(NewlineToken())
            continue
        if isinstance(ev, BoxEvent):
            continue
        if isinstance(ev, Ctrl0BEvent):
            flush_swap()
            flush_pending_overlay()
            out.append(ControlToken(sub=0x0B, arg=int(ev.arg) & 0xFFFF))
            continue
        if isinstance(ev, LineEvent):
            flush_swap()
            flush_pending_overlay()
            out.append(LineStartToken())
            continue
        if isinstance(ev, Ctrl09Event):
            flush_swap()
            flush_pending_overlay()
            out.append(ControlToken(sub=int(ev.sub), arg=ev.arg))
            continue
        if isinstance(ev, HdrEvent):
            synth_overlay_header = False
            flush_type8_hold()
            flush_swap()
            flush_pending_overlay()
            hdr = HeaderToken(pre=ev.pre, b1=ev.b1, b2=ev.b2, b3=ev.b3, expr=ev.expr)
            out.append(hdr)
            last_b1 = hdr.b1
            src_header = hdr
            continue
        if isinstance(ev, EndEvent):
            end_pad = ev.pad
            continue
        if isinstance(ev, RawBytesEvent):
            flush_swap()
            flush_pending_overlay()
            for b in ev.data:
                out.append(RawByteToken(b))
            continue
        if isinstance(ev, FaceKeyEvent):
            flush_swap()
            flush_pending_overlay()
            out.append(FaceKeyToken(face_key=int(ev.key) & 0xFFFF))
            continue
        if isinstance(ev, SlotOffEvent):
            synth_overlay_header = False
            flush_type8_hold()
            flush_swap()
            flush_pending_overlay()
            header_i = emit_header(resolve_b1(ev.b1), pre=ev.pre)
            out.insert(header_i + 1, RawByteToken(0x00))
            continue
        if isinstance(ev, KeepPortraitEvent):
            synth_overlay_header = False
            flush_type8_hold()
            flush_swap()
            flush_pending_overlay()
            emit_header(resolve_b1(ev.b1), pre=ev.pre)
            continue
        if isinstance(ev, PortraitEvent):
            synth_overlay_header = False
            flush_type8_hold()
            flush_swap()
            flush_pending_overlay()
            b1 = resolve_b1(ev.b1)
            header_i = emit_header(b1, pre=ev.pre)
            pending_header_i = header_i
            j = ev_i + 1
            if (
                j < len(events)
                and isinstance(events[j], RawBytesEvent)
                and events[j].data
                and events[j].data != b"\x06"
                and all(b in _HEADER_EXTRA_SKIP for b in events[j].data)
            ):
                # Only the first skip-raw sits before the extra. A second
                # [raw:00] is chrome after it (item-get 00 0B 00 …).
                for b in events[j].data:
                    out.append(RawByteToken(b))
                skip_event_ids.add(j)
            if _still_wants_face_extra(events, ev_i):
                attach_extra_if_needed(header_i, ev.expr, b1)
            continue
        if isinstance(ev, TextEvent):
            ensure_overlay_header()
            flush_swap()
            if ev.text:
                _append_markup_text(out, ev.text)
                spoken_open = True
            continue
        if isinstance(ev, NewlineEvent):
            ensure_overlay_header()
            flush_swap()
            out.append(NewlineToken())
            spoken_open = True
            continue
        if isinstance(ev, ClearEvent):
            ensure_overlay_header()
            flush_swap()
            out.append(PageBreakToken())
            continue
        if isinstance(ev, DelayEvent):
            ensure_overlay_header()
            flush_swap()
            emit_hold(ev.frames)
            spoken_open = False
            continue
        if isinstance(ev, WaitEvent):
            spoken_open = False
            continue

    flush_type8_hold()
    ensure_overlay_header()
    if pending_overlay:
        if not insert_overlay_before_last_header():
            out.append(RawByteToken(0x06))
        pending_overlay = False
    flush_swap()
    _merge_printable_face_extras(out)
    if end_pad is not None:
        trailing = b"\x00" if end_pad[:1] == b"\x00" else b""
        rest = end_pad[1:] if trailing else end_pad
        out.append(TerminatorToken(trailing=trailing))
        for b in rest:
            out.append(RawByteToken(b))
    elif term is not None:
        out.append(term)
    elif not out or not isinstance(out[-1], TerminatorToken):
        pad = TerminatorToken()
        if len(emit_tokens([*out, pad])) % 2:
            pad = TerminatorToken(trailing=b"\x00")
        out.append(pad)
    return out


def events_as_dicts(events: list[MarkupEvent]) -> list[dict[str, Any]]:
    """JSON-friendly event list for the live preview."""
    rows: list[dict[str, Any]] = []
    for ev in events:
        if isinstance(ev, TextEvent):
            rows.append({"kind": "text", "text": ev.text})
        elif isinstance(ev, NewlineEvent):
            rows.append({"kind": "newline"})
        elif isinstance(ev, PortraitEvent):
            rows.append({"kind": "portrait", "expr": ev.expr, "b1": ev.b1})
        elif isinstance(ev, SlotOffEvent):
            rows.append({"kind": "slotOff", "b1": ev.b1})
        elif isinstance(ev, KeepPortraitEvent):
            rows.append({"kind": "keep", "b1": ev.b1})
        elif isinstance(ev, DelayEvent):
            rows.append({"kind": "delay", "frames": ev.frames})
        elif isinstance(ev, ClearEvent):
            rows.append({"kind": "clear"})
        elif isinstance(ev, WaitEvent):
            rows.append({"kind": "wait"})
        elif isinstance(ev, OverlayEvent):
            rows.append({"kind": "overlay"})
        elif isinstance(ev, OverlayEndEvent):
            rows.append({"kind": "overlayEnd"})
        elif isinstance(ev, BoxEvent):
            rows.append({"kind": "box", "side": ev.side, "layout": ev.layout})
        elif isinstance(ev, Ctrl0BEvent):
            play, channel, voice_id = decode_voice_arg(ev.arg)
            rows.append({
                "kind": "voice",
                "arg": ev.arg,
                "play": play,
                "channel": channel,
                "voiceId": voice_id,
            })
        elif isinstance(ev, SwapEvent):
            rows.append({"kind": "swap"})
        elif isinstance(ev, MenuEvent):
            rows.append({"kind": "menu"})
        elif isinstance(ev, FaceKeyEvent):
            rows.append({
                "kind": "faceKey",
                "key": ev.key,
                "timestamp": stored_to_timestamp(ev.key),
            })
        elif isinstance(ev, RawBytesEvent):
            rows.append({"kind": "raw", "hex": ev.data.hex()})
        elif isinstance(ev, LineEvent):
            rows.append({"kind": "line"})
        elif isinstance(ev, Ctrl09Event):
            rows.append({"kind": "ctrl09", "sub": ev.sub, "arg": ev.arg})
        elif isinstance(ev, EndEvent):
            rows.append({"kind": "end", "hex": ev.pad.hex()})
        elif isinstance(ev, HdrEvent):
            rows.append({
                "kind": "hdr",
                "pre": ev.pre,
                "b1": ev.b1,
                "b2": ev.b2,
                "b3": ev.b3,
                "expr": ev.expr,
            })
    return rows


def first_spoken_line(markup: str) -> str:
    """First visible line of spoken text, for timeline labels."""
    try:
        events = parse_markup(markup)
    except ValueError:
        return ""
    parts: list[str] = []
    for ev in events:
        if isinstance(ev, TextEvent):
            parts.append(ev.text)
        elif isinstance(ev, NewlineEvent):
            break
        elif isinstance(ev, (ClearEvent, WaitEvent, DelayEvent, OverlayEvent, OverlayEndEvent, SlotOffEvent, KeepPortraitEvent, BoxEvent, Ctrl0BEvent, SwapEvent, MenuEvent, FaceKeyEvent, RawBytesEvent, LineEvent, Ctrl09Event, EndEvent, HdrEvent)):
            if parts:
                break
    return "".join(parts).strip()
