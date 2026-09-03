#!/usr/bin/env python3
"""Lossless Intermediate Representation for Grandia field scripts.

Design goals
------------
- **Round-trip identity**: parse(emit(parse(raw))) == parse(raw) AND emit(parse(raw)) == raw
  for every unmodified script.
- **Lossless**: every byte of the original script is preserved in the IR, either as a
  typed field or in a raw fallback bucket.  Nothing is silently discarded.
- **Editable**: well-understood ops expose typed operand objects; unknown bytes are kept
  verbatim so they survive unmodified.
- **Label-aware**: branch targets are stored as ``LabelRef`` objects, not raw offsets,
  so the assembler can recalculate distances when ops are inserted/removed.

Layers
------
ScriptFile
  └── Script[]
        └── IrOp[]  (one per script op, ordered)
              ├── RawOp           — fallback: verbatim bytes, no type knowledge
              ├── ModeOp          — type-0: flag_ctx_begin / flag_ctx_end
              ├── DialogueOp      — type-1: portrait+text stream (lossless token list)
              ├── JumpOp          — type-2: unconditional forward skip
              ├── BranchOp        — type-3: compare/branch  (label ref)
              ├── FlagOp          — type-4: set/clear flag
              ├── Op5             — type-5: raw (semantics unknown)
              ├── ActorCamOp      — type-6: actor/camera subop with typed args
              ├── RelJumpOp       — type-7: relative jump (label ref)
              ├── UiDialogueOp    — type-8: no-portrait speech  (lossless token list)
              ├── RawOp (t9-t13) — types 9-13: raw
              └── YieldOp         — type-14: yield / chunk boundary

Token stream (Type-1 / Type-8)
-------------------------------
Each dialogue payload is stored as an ordered list of ``DToken`` objects.  Every byte
of the original payload is represented by exactly one token; nothing is dropped.

  HeaderToken      — 5-byte portrait header  (pre, b1, b2, b3, expr)
  LineStartToken   — 09 01  (line-start marker)
  TextToken        — printable ASCII run
  NewlineToken     — 0x01 within text
  PageBreakToken   — 0x02 within text
  HoldToken        — 0E 00 <hold>  (auto-display timer)
  FaceKeyToken     — 09 0F <u16>   (packed face-key / expr setter)
  ControlToken     — other 09 <sub> [<arg u16>]  (generic 09-series)
  RawByteToken     — single unclassified control byte

Textbox boundary control bytes (0x02, 0x03 … 0x13 etc.) are preserved as
``RawByteToken`` so the emitter can reproduce them exactly.
"""
from __future__ import annotations

import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

# ---------------------------------------------------------------------------
# Token types for Type-1 / Type-8 dialogue payload streams
# ---------------------------------------------------------------------------

@dataclass
class HeaderToken:
    """5-byte portrait/textbox page header: pre b1 b2 b3 expr."""
    pre: int
    b1: int
    b2: int
    b3: int
    expr: int

    def to_bytes(self) -> bytes:
        return bytes([self.pre, self.b1, self.b2, self.b3, self.expr])

    def __repr__(self) -> str:
        return f"Header(pre={self.pre:#04x} b1={self.b1:#04x} expr={self.expr:#04x})"


@dataclass
class LineStartToken:
    """09 01 — explicit line-start marker."""

    def to_bytes(self) -> bytes:
        return bytes([0x09, 0x01])

    def __repr__(self) -> str:
        return "LineStart()"


@dataclass
class TextToken:
    """Run of printable ASCII characters (0x20-0x7E)."""
    text: str

    def to_bytes(self) -> bytes:
        return self.text.encode("ascii")

    def __repr__(self) -> str:
        return f"Text({self.text!r})"


@dataclass
class NewlineToken:
    """0x01 — inline newline within a textbox."""

    def to_bytes(self) -> bytes:
        return bytes([0x01])

    def __repr__(self) -> str:
        return "Newline()"


@dataclass
class PageBreakToken:
    """0x02 — page-break / wait-for-button (when inside text run)."""

    def to_bytes(self) -> bytes:
        return bytes([0x02])

    def __repr__(self) -> str:
        return "PageBreak()"


@dataclass
class HoldToken:
    """0E 00 <hold> — auto-display timer (hold value in frames)."""
    hold: int

    def to_bytes(self) -> bytes:
        return bytes([0x0E, 0x00, self.hold])

    def __repr__(self) -> str:
        return f"Hold({self.hold})"


@dataclass
class FaceKeyToken:
    """09 0F <u16 LE> — packed face/portrait key."""
    face_key: int

    def to_bytes(self) -> bytes:
        return bytes([0x09, 0x0F]) + struct.pack("<H", self.face_key)

    def __repr__(self) -> str:
        return f"FaceKey({self.face_key:#06x})"


@dataclass
class ControlToken:
    """09 <sub> [<u16 LE> arg] — generic 09-series control op (sub != 0x01, 0x0F)."""
    sub: int
    arg: int | None = None  # present when sub in 0x0A..0x0F

    def to_bytes(self) -> bytes:
        if self.arg is not None:
            return bytes([0x09, self.sub]) + struct.pack("<H", self.arg)
        return bytes([0x09, self.sub])

    def __repr__(self) -> str:
        if self.arg is not None:
            return f"Control(sub={self.sub:#04x} arg={self.arg:#06x})"
        return f"Control(sub={self.sub:#04x})"


@dataclass
class TerminatorToken:
    """0x07 — end-of-payload marker."""
    trailing: bytes = b""  # 0x00 padding byte after 0x07 if present

    def to_bytes(self) -> bytes:
        return bytes([0x07]) + self.trailing

    def __repr__(self) -> str:
        return "Terminator()"


@dataclass
class RawByteToken:
    """Single unclassified control byte (0x00-0x1F except the above)."""
    value: int

    def to_bytes(self) -> bytes:
        return bytes([self.value])

    def __repr__(self) -> str:
        return f"RawByte({self.value:#04x})"


# Type alias for the union of all token types.
DToken = (
    HeaderToken
    | LineStartToken
    | TextToken
    | NewlineToken
    | PageBreakToken
    | HoldToken
    | FaceKeyToken
    | ControlToken
    | TerminatorToken
    | RawByteToken
)


# ---------------------------------------------------------------------------
# Payload tokeniser  (lossless: byte-identical round-trip)
# ---------------------------------------------------------------------------

def tokenise_payload(payload: bytes) -> list[DToken]:
    """Parse a Type-1 or Type-8 payload into a lossless token list.

    The token stream covers *every* byte in the payload.  Calling
    ``emit_tokens(tokenise_payload(p))`` reproduces ``p`` byte-for-byte.
    """
    tokens: list[DToken] = []
    n = len(payload)
    pos = 0

    while pos < n:
        b = payload[pos]

        # ---- 5-byte portrait/page header ----
        if b in (0x0F, 0x0B, 0x21) and pos + 5 <= n:
            b2, b3 = payload[pos + 2], payload[pos + 3]
            if (b2, b3) in ((0x0A, 0x0C), (0x1F, 0x01)):
                tokens.append(HeaderToken(
                    pre=b,
                    b1=payload[pos + 1],
                    b2=b2,
                    b3=b3,
                    expr=payload[pos + 4],
                ))
                pos += 5
                continue

        # ---- 09 <sub> control series ----
        if b == 0x09 and pos + 1 < n:
            sub = payload[pos + 1]
            # Right after a portrait header, 09 <printable> is extra 0x09
            # (1-based face 9) plus that glyph — "Hello" is 09 48…, not
            # Control(sub=0x48). 09 09 is the same extra plus a following
            # 09-control (usually 09 01 line-start). Same idea as 0x07
            # after a header being a face id.
            if tokens and isinstance(tokens[-1], HeaderToken) and (
                sub == 0x09 or sub >= 0x20
            ):
                tokens.append(RawByteToken(0x09))
                pos += 1
                continue
            if sub == 0x01:
                tokens.append(LineStartToken())
                pos += 2
                continue
            if sub == 0x0F and pos + 4 <= n:
                face_key = struct.unpack_from("<H", payload, pos + 2)[0]
                tokens.append(FaceKeyToken(face_key))
                pos += 4
                continue
            # Generic 09 <sub>: subs 0x0A-0x0E carry a u16 arg; others don't.
            if sub in (0x0A, 0x0B, 0x0C, 0x0D, 0x0E) and pos + 4 <= n:
                arg = struct.unpack_from("<H", payload, pos + 2)[0]
                tokens.append(ControlToken(sub=sub, arg=arg))
                pos += 4
                continue
            tokens.append(ControlToken(sub=sub))
            pos += 2
            continue

        # ---- 0E 00 <hold> ----
        if b == 0x0E and pos + 2 < n and payload[pos + 1] == 0x00:
            tokens.append(HoldToken(hold=payload[pos + 2]))
            pos += 3
            continue

        # ---- terminator 0x07 ----
        if b == 0x07:
            # Immediately after a portrait header, 0x07 is a 1-based face id
            # (Parm 0x0000 "Don't worry!" is 0F 03 … 00 07 "Don't…"), not EOF.
            if tokens and isinstance(tokens[-1], HeaderToken):
                tokens.append(RawByteToken(0x07))
                pos += 1
                continue
            trailing = b""
            if pos + 1 < n and payload[pos + 1] == 0x00:
                trailing = bytes([0x00])
                pos += 2
            else:
                pos += 1
            tokens.append(TerminatorToken(trailing=trailing))
            # Preserve any padding after 0x07 up to even alignment (word stream).
            while pos < n:
                tokens.append(RawByteToken(payload[pos]))
                pos += 1
            break

        # ---- inline text bytes ----
        if 0x20 <= b <= 0x7E:
            # A single printable byte right after a header, immediately followed
            # by a 09-control, is a 1-based face id (0x32/'2' hurt, 0x33/'3' grin).
            last = tokens[-1] if tokens else None
            nxt_is_header = (
                pos + 6 <= n
                and payload[pos + 1] in (0x0F, 0x0B, 0x21)
                and (payload[pos + 3], payload[pos + 4]) in ((0x0A, 0x0C), (0x1F, 0x01))
            )
            if (
                isinstance(last, HeaderToken)
                and pos + 1 < n
                and (payload[pos + 1] == 0x09 or nxt_is_header)
            ):
                tokens.append(RawByteToken(b))
                pos += 1
                continue
            start = pos
            while pos < n and 0x20 <= payload[pos] <= 0x7E:
                pos += 1
            tokens.append(TextToken(payload[start:pos].decode("ascii")))
            continue

        # ---- 0x01 newline ----
        if b == 0x01:
            tokens.append(NewlineToken())
            pos += 1
            continue

        # ---- 0x02 page-break within text ----
        # Note: 0x02 appearing as a standalone pre-textbox control byte is also
        # valid; here we just encode it without distinction — the emitter emits
        # the byte either way.
        if b == 0x02:
            tokens.append(PageBreakToken())
            pos += 1
            continue

        # ---- everything else: raw byte ----
        tokens.append(RawByteToken(b))
        pos += 1

    return tokens


def emit_tokens(tokens: list[DToken]) -> bytes:
    """Re-serialise a token stream to bytes.  Inverse of ``tokenise_payload``."""
    out = bytearray()
    for tok in tokens:
        out += tok.to_bytes()
    return bytes(out)


# ---------------------------------------------------------------------------
# Label / branch reference
# ---------------------------------------------------------------------------

@dataclass
class Label:
    """A named anchor in the op list, corresponding to a branch target."""
    name: str


@dataclass
class LabelRef:
    """Reference to a label used in branch/jump operands."""
    name: str


# ---------------------------------------------------------------------------
# Typed operand records for Type-6 (actor/camera) subops
# ---------------------------------------------------------------------------

@dataclass
class WaitReadyArgs:
    ticks: int
    raw: bytes


@dataclass
class CallHookArgs:
    hook_id: int
    raw: bytes


# Back-compat alias (deprecated name from early RE).
PlaceActorArgs = CallHookArgs


@dataclass
class ActorWalkArgs:
    raw: bytes


@dataclass
class CameraArgs:
    """type-6 sub 0x0016: camera activate/deactivate."""
    op: str          # "activate" | "deactivate" | "overlay" | "deactivate_overlay"
    scene_id: int
    raw: bytes


@dataclass
class GiveItemArgs:
    item_id: int
    raw: bytes


@dataclass
class GenericType6Args:
    sub: int
    raw: bytes


# ---------------------------------------------------------------------------
# IR Op types
# ---------------------------------------------------------------------------

@dataclass
class RawOp:
    """Verbatim op bytes — used for unknown types and as the canonical fallback."""
    raw: bytes
    # Source offset in the original bank (informational only, not used for emit).
    source_offset: int = 0

    def to_bytes(self) -> bytes:
        return self.raw

    @property
    def type_idx(self) -> int:
        if len(self.raw) < 2:
            return -1
        return (struct.unpack_from("<H", self.raw, 0)[0] >> 12) - 1


@dataclass
class ModeOp:
    """Type-0: flag_ctx_begin (lo12!=0) or flag_ctx_end (lo12==0)."""
    word: int
    source_offset: int = 0

    @property
    def is_begin(self) -> bool:
        return bool(self.word & 0x0FFF)

    def to_bytes(self) -> bytes:
        return struct.pack("<H", self.word)

    @property
    def type_idx(self) -> int:
        return 0


@dataclass
class DialogueOp:
    """Type-1: portrait+speech stream stored as a lossless token list."""
    word: int                      # original opcode word (top nibble = 2)
    tokens: list[DToken]
    source_offset: int = 0

    def payload(self) -> bytes:
        return emit_tokens(self.tokens)

    def to_bytes(self) -> bytes:
        pay = self.payload()
        # Align to even length.
        if len(pay) % 2:
            pay += b"\x00"
        new_word = (self.word & 0xF000) | len(pay)
        return struct.pack("<H", new_word) + pay

    @property
    def type_idx(self) -> int:
        return 1


@dataclass
class JumpOp:
    """Type-2: unconditional forward skip; target stored as LabelRef for relocation."""
    word: int
    target: LabelRef
    original_skip: int = 0  # original encoded skip distance (bytes), for cross-script fallback
    source_offset: int = 0

    def to_bytes(self, resolved_skip: int | None = None) -> bytes:
        """Emit with a resolved skip distance (bytes), or original if None."""
        skip = resolved_skip if resolved_skip is not None else self.original_skip
        new_word = (self.word & 0xF000) | (skip & 0x0FFF)
        return struct.pack("<H", new_word)

    @property
    def type_idx(self) -> int:
        return 2


@dataclass
class BranchOp:
    """Type-3: compare/branch (4 or 6 bytes).  Target stored as LabelRef."""
    word: int
    arg: int
    extra: bytes            # bytes 4-5 if word & 0x800 (6-byte variant)
    target: LabelRef
    source_offset: int = 0

    def to_bytes(self, resolved_arg: int | None = None) -> bytes:
        """Emit; resolved_arg overrides self.arg when provided."""
        a = resolved_arg if resolved_arg is not None else self.arg
        out = struct.pack("<HH", self.word, a)
        if self.extra:
            out += self.extra
        return out

    @property
    def type_idx(self) -> int:
        return 3


@dataclass
class FlagOp:
    """Type-4: set_flag / clear_flag."""
    word: int
    flag_id: int
    source_offset: int = 0

    @property
    def is_set(self) -> bool:
        return bool(self.word & 0xF)

    def to_bytes(self) -> bytes:
        return struct.pack("<HH", self.word, self.flag_id)

    @property
    def type_idx(self) -> int:
        return 4


@dataclass
class ActorCamOp:
    """Type-6: actor/camera subop with decoded args and verbatim payload."""
    word: int
    sub: int
    args: WaitReadyArgs | CallHookArgs | ActorWalkArgs | CameraArgs | GiveItemArgs | GenericType6Args
    source_offset: int = 0

    def to_bytes(self) -> bytes:
        raw_payload = self.args.raw
        return struct.pack("<H", self.word) + raw_payload

    @property
    def type_idx(self) -> int:
        return 6


@dataclass
class RelJumpOp:
    """Type-7: relative jump; target stored as LabelRef for relocation."""
    word: int
    target: LabelRef
    original_rel: int = 0  # original signed relative distance, for cross-script fallback
    source_offset: int = 0

    def to_bytes(self, resolved_rel: int | None = None) -> bytes:
        """Emit with a resolved relative offset (signed s16, from ip+4), or original if None."""
        rel = resolved_rel if resolved_rel is not None else self.original_rel
        return struct.pack("<Hh", self.word, rel)

    @property
    def type_idx(self) -> int:
        return 7


@dataclass
class UiDialogueOp:
    """Type-8: no-portrait speech stream, lossless token list."""
    word: int
    tokens: list[DToken]
    source_offset: int = 0

    def payload(self) -> bytes:
        return emit_tokens(self.tokens)

    def to_bytes(self) -> bytes:
        pay = self.payload()
        if len(pay) % 2:
            pay += b"\x00"
        new_word = (self.word & 0xF000) | len(pay)
        return struct.pack("<H", new_word) + pay

    @property
    def type_idx(self) -> int:
        return 8


@dataclass
class YieldOp:
    """Type-14: yield / chunk boundary."""
    word: int
    source_offset: int = 0

    def to_bytes(self) -> bytes:
        return struct.pack("<H", self.word)

    @property
    def type_idx(self) -> int:
        return 14


# Union of all IR op types.
IrOp = (
    RawOp | ModeOp | DialogueOp | JumpOp | BranchOp | FlagOp
    | ActorCamOp | RelJumpOp | UiDialogueOp | YieldOp
)


# ---------------------------------------------------------------------------
# Script IR container
# ---------------------------------------------------------------------------

@dataclass
class Script:
    """One script (one entry from the OFS/sec[11] directory)."""
    script_id: int
    ops: list[IrOp] = field(default_factory=list)
    labels: dict[str, int] = field(default_factory=dict)
    # op-list index of each label name (populated by the parser).
    original_offset: int = 0   # bank offset of first byte of this script


@dataclass
class ScriptFile:
    """Full set of scripts from one SCN/OFS or MDP pair."""
    stem: str
    source: str   # "scn" or "mdp"
    original_bank: bytes
    original_directory: bytes
    scripts: list[Script] = field(default_factory=list)


# ---------------------------------------------------------------------------
# Parser: raw bytes → IR
# ---------------------------------------------------------------------------

def _parse_type6_args(word: int, raw_payload: bytes) -> WaitReadyArgs | CallHookArgs | ActorWalkArgs | CameraArgs | GiveItemArgs | GenericType6Args:
    if len(raw_payload) < 2:
        return GenericType6Args(sub=0, raw=raw_payload)
    sub = struct.unpack_from("<H", raw_payload, 0)[0] & 0x3FFF
    words = [struct.unpack_from("<H", raw_payload, off)[0] for off in range(0, len(raw_payload), 2)]

    if sub in (0x0011, 0x0020, 0x0021) and len(words) >= 2:
        return WaitReadyArgs(ticks=words[1], raw=raw_payload)
    if sub == 0x001A and len(words) >= 2:
        return CallHookArgs(hook_id=words[1] & 0x7FFF, raw=raw_payload)
    if sub == 0x0015:
        return ActorWalkArgs(raw=raw_payload)
    if sub == 0x0016 and len(words) >= 2:
        a = words[1]
        scene_id = a & 0x3FFF
        hi = a >> 14
        op_name = {0: "activate", 1: "overlay", 2: "deactivate", 3: "deactivate_overlay"}.get(hi, f"hi{hi}")
        return CameraArgs(op=op_name, scene_id=scene_id, raw=raw_payload)
    if sub in (0x0004, 0x0005) and len(words) >= 2:
        return GiveItemArgs(item_id=words[1], raw=raw_payload)
    return GenericType6Args(sub=sub, raw=raw_payload)


def _type6_payload_len(outer_word: int, payload_word: int) -> int:
    """Bytes after the payload word (mirrors disassembler logic)."""
    hi = payload_word >> 14
    if hi == 0:
        return 0
    n = (outer_word & 0xF) + 1
    if hi == 1 and (n & 1) != 0:
        n += 1
    return n << (hi - 1)


def parse_script_to_ir(
    bank: bytes,
    script_id: int,
    start: int,
    end: int,
    *,
    max_ops: int = 20000,
) -> Script:
    """Parse one script range from the bank into a ``Script`` IR object."""
    script = Script(script_id=script_id, original_offset=start)
    ops = script.ops
    label_counter = 0
    # Map source_offset → label_name for forward-reference resolution.
    offset_to_label: dict[int, str] = {}

    def fresh_label() -> str:
        nonlocal label_counter
        name = f"L{label_counter:04d}"
        label_counter += 1
        return name

    def ensure_label_at(off: int) -> str:
        if off not in offset_to_label:
            offset_to_label[off] = fresh_label()
        return offset_to_label[off]

    # First pass: collect all branch targets so we can assign labels.
    ip = start
    while ip + 2 <= end:
        word = struct.unpack_from("<H", bank, ip)[0]
        nibble = word >> 12
        if nibble == 0:
            break
        type_idx = nibble - 1
        if type_idx == 2:
            # type-2: skip (word & 0x0FFF) bytes forward from ip+2
            skip = word & 0x0FFF
            target_off = ip + 2 + skip
            ensure_label_at(target_off)
            ip += 2
        elif type_idx == 7:
            if ip + 4 <= end:
                rel = struct.unpack_from("<h", bank, ip + 2)[0]
                target_off = ip + 4 + rel
                ensure_label_at(target_off)
            ip += 4
        elif type_idx in (1, 8):
            payload_n = word & 0x0FFF
            ip += 2 + payload_n
        elif type_idx == 3:
            size = 6 if (word & 0x800) else 4
            ip += size
        elif type_idx == 4:
            ip += 4
        elif type_idx == 5:
            ip += 6
        elif type_idx == 6:
            if ip + 4 > end:
                ip += 2
                continue
            pw = struct.unpack_from("<H", bank, ip + 2)[0]
            extra = _type6_payload_len(word, pw)
            ip += 4 + extra
        elif type_idx == 14:
            ip += 2
        else:
            ip += 2

    # Reverse map: label_name → source_offset.
    label_source: dict[str, int] = {v: k for k, v in offset_to_label.items()}
    script.labels = {name: off for name, off in label_source.items()}

    # Pending labels to insert before the next op.
    def pending_labels_for(off: int) -> list[Label]:
        name = offset_to_label.get(off)
        if name is not None:
            return [Label(name)]
        return []

    # Second pass: build IR ops.
    ip = start
    op_count = 0
    while ip + 2 <= end and op_count < max_ops:
        # Insert label if any branch points here.
        for lbl in pending_labels_for(ip):
            ops.append(lbl)

        word = struct.unpack_from("<H", bank, ip)[0]
        nibble = word >> 12
        if nibble == 0:
            # Nibble 0 signals that the logical script ends here; the remaining
            # bytes up to `end` may be alignment padding or inter-script data.
            # Emit them verbatim so nothing is lost.
            ops.append(RawOp(raw=bank[ip:end], source_offset=ip))
            ip = end
            break
        type_idx = nibble - 1

        if type_idx == 0:
            ops.append(ModeOp(word=word, source_offset=ip))
            ip += 2

        elif type_idx == 1:
            payload_n = word & 0x0FFF
            pay = bank[ip + 2 : ip + 2 + payload_n]
            tokens = tokenise_payload(pay)
            ops.append(DialogueOp(word=word, tokens=tokens, source_offset=ip))
            ip += 2 + payload_n

        elif type_idx == 2:
            skip = word & 0x0FFF
            target_off = ip + 2 + skip
            label_name = ensure_label_at(target_off)
            ops.append(JumpOp(word=word, target=LabelRef(label_name), original_skip=skip, source_offset=ip))
            ip += 2

        elif type_idx == 3:
            size = 6 if (word & 0x800) else 4
            if ip + size > end:
                size = end - ip
            arg = struct.unpack_from("<H", bank, ip + 2)[0] if ip + 4 <= end else 0
            extra = bank[ip + 4 : ip + size] if size > 4 else b""
            # For type-3, the branch target is the current op + its size (skip-over semantics:
            # the next op is skipped if condition matches, so target = ip + size + 2).
            # We store the *fall-through* target (ip + size) as the label target for now;
            # the semantics differ from jump/reljump (type-3 skips the immediately following op).
            # We record target as a best-effort reference; the assembler keeps this lossless.
            target_off = ip + size  # fall-through target
            label_name = ensure_label_at(target_off)
            ops.append(BranchOp(
                word=word, arg=arg, extra=extra,
                target=LabelRef(label_name), source_offset=ip,
            ))
            ip += size

        elif type_idx == 4:
            flag = struct.unpack_from("<H", bank, ip + 2)[0] if ip + 4 <= end else 0
            ops.append(FlagOp(word=word, flag_id=flag, source_offset=ip))
            ip += 4

        elif type_idx == 5:
            ops.append(RawOp(raw=bank[ip:ip+6], source_offset=ip))
            ip += 6

        elif type_idx == 6:
            if ip + 4 > end:
                ops.append(RawOp(raw=bank[ip:ip+2], source_offset=ip))
                ip += 2
                continue
            pw = struct.unpack_from("<H", bank, ip + 2)[0]
            extra_len = _type6_payload_len(word, pw)
            total = 4 + extra_len
            if ip + total > end:
                total = end - ip
            raw_payload = bank[ip + 2 : ip + total]
            sub = pw & 0x3FFF
            args = _parse_type6_args(word, raw_payload)
            ops.append(ActorCamOp(word=word, sub=sub, args=args, source_offset=ip))
            ip += total

        elif type_idx == 7:
            if ip + 4 <= end:
                rel = struct.unpack_from("<h", bank, ip + 2)[0]
                target_off = ip + 4 + rel
                label_name = ensure_label_at(target_off)
                ops.append(RelJumpOp(word=word, target=LabelRef(label_name), original_rel=rel, source_offset=ip))
            else:
                ops.append(RawOp(raw=bank[ip:end], source_offset=ip))
            ip += 4

        elif type_idx == 8:
            payload_n = word & 0x0FFF
            pay = bank[ip + 2 : ip + 2 + payload_n]
            tokens = tokenise_payload(pay)
            ops.append(UiDialogueOp(word=word, tokens=tokens, source_offset=ip))
            ip += 2 + payload_n

        elif type_idx == 14:
            ops.append(YieldOp(word=word, source_offset=ip))
            ip += 2

        else:
            # Types 9-13: raw preservation.
            ops.append(RawOp(raw=bank[ip:ip+2], source_offset=ip))
            ip += 2

        op_count += 1

    # Drain any bytes left between the last parsed op and the script end.
    # This handles single-byte alignment padding and any bytes following a
    # nibble-0 / early-break scenario.
    if ip < end:
        for lbl in pending_labels_for(ip):
            ops.append(lbl)
        ops.append(RawOp(raw=bank[ip:end], source_offset=ip))
        ip = end

    # Insert any trailing labels (branch targets past the last op).
    for lbl in pending_labels_for(ip):
        ops.append(lbl)

    return script


def parse_file_to_ir(
    stem: str,
    source: str,
    bank: bytes,
    directory: bytes,
    entries: list[Any],
) -> ScriptFile:
    """Build a ``ScriptFile`` IR from a parsed bank and directory."""
    sf = ScriptFile(
        stem=stem,
        source=source,
        original_bank=bank,
        original_directory=directory,
    )
    for entry in sorted(entries, key=lambda e: e.offset):
        sc = parse_script_to_ir(bank, entry.script_id, entry.offset, entry.end)
        sf.scripts.append(sc)
    return sf


# ---------------------------------------------------------------------------
# Assembler / emitter
# ---------------------------------------------------------------------------

def _resolve_op_sizes(ops: list[IrOp | Label]) -> tuple[dict[str, int], dict[int, int]]:
    """
    Two-pass offset resolution.

    Returns:
      label_offsets: label_name → byte offset in the emitted bank slice
      op_offsets:    index in ops[] → byte offset in the emitted bank slice
    """
    # Pass 1: estimate sizes (labels have zero size; jumps have fixed sizes).
    sizes: list[int] = []
    for item in ops:
        if isinstance(item, Label):
            sizes.append(0)
        elif isinstance(item, (DialogueOp, UiDialogueOp)):
            pay = item.payload()
            if len(pay) % 2:
                pay += b"\x00"
            sizes.append(2 + len(pay))
        elif isinstance(item, BranchOp):
            sizes.append(6 if (item.word & 0x800) else 4)
        elif isinstance(item, (ModeOp, JumpOp, YieldOp)):
            sizes.append(2)
        elif isinstance(item, RelJumpOp):
            sizes.append(4)  # type-7: outer word + signed s16 rel
        elif isinstance(item, FlagOp):
            sizes.append(4)
        elif isinstance(item, ActorCamOp):
            sizes.append(len(item.to_bytes()))
        else:
            # RawOp or unknown
            sizes.append(len(item.to_bytes()) if hasattr(item, "to_bytes") else 0)

    # Compute cumulative offsets.
    cumulative = 0
    op_offsets: dict[int, int] = {}
    label_offsets: dict[str, int] = {}
    for i, (item, sz) in enumerate(zip(ops, sizes)):
        op_offsets[i] = cumulative
        if isinstance(item, Label):
            label_offsets[item.name] = cumulative
        cumulative += sz

    return label_offsets, op_offsets


def emit_script(script: Script, base_offset: int = 0) -> bytes:
    """Assemble a ``Script`` IR back to bytes.

    ``base_offset`` is the byte position of this script's first op within the
    parent bank, used for computing relative jump distances correctly.
    """
    ops = script.ops
    label_offsets, op_offsets = _resolve_op_sizes(ops)

    out = bytearray()
    for i, item in enumerate(ops):
        if isinstance(item, Label):
            continue
        item_off = op_offsets[i]  # offset of this item in the emitted slice

        if isinstance(item, (DialogueOp, UiDialogueOp, ModeOp, FlagOp, ActorCamOp, YieldOp)):
            out += item.to_bytes()

        elif isinstance(item, JumpOp):
            # type-2: skip N bytes forward from ip+2.
            # If the label target is outside the emitted slice (cross-script),
            # fall back to the original encoded skip distance.
            if item.target.name in label_offsets:
                target_off_in_slice = label_offsets[item.target.name]
                skip = target_off_in_slice - (item_off + 2)
                skip = max(0, skip)
                out += item.to_bytes(skip)
            else:
                out += item.to_bytes(None)  # uses original_skip

        elif isinstance(item, RelJumpOp):
            # type-7: rel from ip+4.
            # Fall back to original rel for cross-script references.
            if item.target.name in label_offsets:
                target_off_in_slice = label_offsets[item.target.name]
                rel = target_off_in_slice - (item_off + 4)
                out += item.to_bytes(rel)
            else:
                out += item.to_bytes(None)  # uses original_rel

        elif isinstance(item, BranchOp):
            out += item.to_bytes()

        elif isinstance(item, RawOp):
            out += item.to_bytes()

        else:
            out += item.to_bytes()

    return bytes(out)


def emit_script_file(sf: ScriptFile) -> tuple[bytes, bytes]:
    """Emit a ``ScriptFile`` IR to (new_bank_bytes, new_directory_bytes).

    Scripts are emitted in original offset order.  The directory is rebuilt
    with updated offsets.  Untouched scripts are emitted byte-for-byte from
    ``original_bank``.
    """
    # Sort scripts by original bank offset to preserve ordering.
    sorted_scripts = sorted(sf.scripts, key=lambda s: s.original_offset)

    new_bank = bytearray()
    # Map (script_id, original_offset) → new bank offset so duplicate IDs work.
    new_offsets: dict[tuple[int, int], int] = {}

    for sc in sorted_scripts:
        new_offsets[(sc.script_id, sc.original_offset)] = len(new_bank)
        emitted = emit_script(sc, base_offset=len(new_bank))
        new_bank += emitted

    # Rebuild directory from the original OFS layout (preserve entry order).
    # The original directory entries are matched to scripts by (sid, original_offset).
    orig_dir = sf.original_directory
    new_dir = bytearray()
    pos = 0
    # Build a usage-ordered list: for each dir entry (sid, old_off), find new_off.
    while pos + 4 <= len(orig_dir):
        sid, old_off = struct.unpack_from("<HH", orig_dir, pos)
        if sid == 0xFFFF:
            new_dir += struct.pack("<HH", 0xFFFF, 0xFFFF)
            pos += 4
            break
        # Try exact (sid, old_off) match first; fall back to sid-only for unique IDs.
        key = (sid, old_off)
        if key in new_offsets:
            new_off = new_offsets[key]
        else:
            # Fallback: find any script with this id.
            matches = [v for (s, o), v in new_offsets.items() if s == sid]
            new_off = matches[0] if matches else old_off
        new_dir += struct.pack("<HH", sid, new_off)
        pos += 4

    # Preserve any trailing padding/alignment bytes.
    new_dir += orig_dir[pos:]
    return bytes(new_bank), bytes(new_dir)


# ---------------------------------------------------------------------------
# Convenience: load from existing disasm helpers
# ---------------------------------------------------------------------------

def load_ir(
    stem: str,
    source: str,
    field_root: Path,
    text_root: Path,
) -> ScriptFile:
    """High-level loader: auto-selects SCN vs MDP, returns ``ScriptFile``."""
    import sys
    sys.path.insert(0, str(Path(__file__).parent))
    from field_script_disasm import (
        load_map,
        load_text_map,
        parse_directory,
    )

    if source == "scn":
        directory, bank, entries = load_text_map(text_root, stem)
    elif source == "mdp":
        directory, bank, entries = load_map(field_root, stem)
    else:
        try:
            directory, bank, entries = load_text_map(text_root, stem)
            source = "scn"
        except FileNotFoundError:
            directory, bank, entries = load_map(field_root, stem)
            source = "mdp"

    return parse_file_to_ir(stem, source, bank, directory, entries)


# ---------------------------------------------------------------------------
# Dialogue-layer convenience helpers (editor-facing)
# ---------------------------------------------------------------------------

def iter_dialogue_tokens(op: DialogueOp | UiDialogueOp):
    """Iterate the meaningful text-bearing tokens in a dialogue op."""
    for tok in op.tokens:
        yield tok


def get_page_texts(op: DialogueOp) -> list[tuple[int, int, int, str]]:
    """Return [(pre, b1, expr, text_string)] for each page in a DialogueOp.

    This is a *view* over the token stream, not the canonical representation.
    Edits must target the token stream directly for lossless round-trip.
    """
    pages: list[tuple[int, int, int, str]] = []
    cur_header: HeaderToken | None = None
    cur_text: list[str] = []

    def flush() -> None:
        if cur_header is not None and cur_text:
            pages.append((cur_header.pre, cur_header.b1, cur_header.expr, "".join(cur_text)))

    for tok in op.tokens:
        if isinstance(tok, HeaderToken):
            flush()
            cur_header = tok
            cur_text = []
        elif isinstance(tok, TextToken):
            cur_text.append(tok.text)
        elif isinstance(tok, NewlineToken):
            cur_text.append(r"\n")
        elif isinstance(tok, PageBreakToken):
            cur_text.append(r"\p")
        elif isinstance(tok, (TerminatorToken, RawByteToken)):
            pass  # not text

    flush()
    return pages
