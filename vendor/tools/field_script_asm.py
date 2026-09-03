#!/usr/bin/env python3
"""1:1 assembler text for one field-script IR ``Script``.

Unmodified scripts must satisfy:

    emit(parse(format(ir))) == emit(ir)

Pretty mnemonics are used only when they rebuild the same bytes. Dialogue that
cannot survive markup apply is stored as ``hex=`` (markup is kept as a comment).
Type-6 ``call_hook`` / ``wait`` / ``camera`` / ``sfx`` / ``give_item`` /
``save_menu`` / ``restore`` / ``stash_menu`` / ``retrieve_menu`` drop
``word=`` / ``raw=`` when the vanilla packet rebuilds the same bytes.
``walk`` is accepted on parse (legacy name for ``sfx``).
``call_hook N alt`` is operand bit 15 (engine still uses ``N & 0x7FFF``).
Known flags, unique hook kinds, and item names print as identifiers
(``intro_seen``, ``present_channel``, ``Apron``); hex / decimal still parse.
``[P:]`` stays numeric.

``if`` is a lift of the vanilla section gate:

    flag_ctx_begin / skip_if_clear|set … / flag_ctx_end / jump L

becomes ``if clear 0xID and set 0xID -> L { …body… }`` when the
jump target sits in this script. A choice rung ``0x4C40`` becomes
``if pick == N``. A ``[menu]`` dialog plus its vanilla pick rungs
becomes ``menu { say … pick N -> L }``. The last option still
falls through after the block. A gate whose label is missing stays
``if …`` plus an indented ``jump``. ``skip=`` / ``rel=`` are omitted
when the target label is in this script (emit rebuilds the distance).
Cross-script jumps keep the explicit value.
"""
from __future__ import annotations

import argparse
import re
import struct
import sys
from pathlib import Path

from field_script_asm_names import (
    AsmNames,
    format_flag,
    format_hook,
    format_item,
    load_asm_names,
    resolve_named,
)
from field_script_choice_view import has_menu_control
from field_script_dialog_markup import apply_markup, tokens_to_markup
from field_script_ir import (
    ActorCamOp,
    ActorWalkArgs,
    BranchOp,
    CallHookArgs,
    CameraArgs,
    DialogueOp,
    FlagOp,
    GenericType6Args,
    GiveItemArgs,
    JumpOp,
    Label,
    LabelRef,
    ModeOp,
    RawOp,
    RelJumpOp,
    Script,
    UiDialogueOp,
    WaitReadyArgs,
    YieldOp,
    emit_script,
    emit_tokens,
    tokenise_payload,
)

_HEX = re.compile(r"^[0-9a-fA-F]+$")
_LABEL = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
_KV = re.compile(r"(\w+)=(\S+)")
_IF_COND = re.compile(
    r"^(set|clear)\s+([A-Za-z_][A-Za-z0-9_]*|0x[0-9a-fA-F]+|\d+)"
    r"(?:\s+word=(0x[0-9a-fA-F]+|\d+))?$"
)
_IF_PICK = re.compile(r"^pick\s*==\s*(0x[0-9a-fA-F]+|\d+)$")
_PICK_HEAD = re.compile(
    r"^pick\s+(0x[0-9a-fA-F]+|\d+)\s*->\s*([A-Za-z_][A-Za-z0-9_]*)"
    r"(?:\s+skip=(0x[0-9a-fA-F]+|\d+))?"
    r"(?:\s+word=(0x[0-9a-fA-F]+|\d+))?$"
)
_IF_ARROW = re.compile(
    r"^(.*?)\s+->\s+([A-Za-z_][A-Za-z0-9_]*)"
    r"(?:\s+skip=(0x[0-9a-fA-F]+|\d+))?"
    r"(?:\s+word=(0x[0-9a-fA-F]+|\d+))?"
    r"\s*(\{)?$"
)

_MODE_BEGIN = 0x1001
_MODE_END = 0x1000
_FLAG_SET = 0x5001
_FLAG_CLEAR = 0x5000
_YIELD = 0xF000
_JUMP_HI = 0x3000
_RELJUMP = 0x8000
_SAY1_HI = 0x2000
_SAY8_HI = 0x9000

_BRANCH_CLEAR = 0x4001
_BRANCH_SET = 0x4040
_BRANCH_SET_ALT = 0x4041
_BRANCH_CHECK = 0x4000
_CHOICE_WORD = 0x4C40
_CHOICE_ARG = 0x4000
_TYPE6_WORD = 0x7000
_TYPE6_HI2 = 0x8000
_WAIT_AUX_WORD = 0x7001
_CAM_HI = {
    "activate": 0,
    "overlay": 1,
    "deactivate": 2,
    "deactivate_overlay": 3,
}


class AsmError(ValueError):
    pass


def parse_int(text: str) -> int:
    return int(text.strip(), 0)


def parse_hex_bytes(text: str) -> bytes:
    h = re.sub(r"\s+", "", text)
    if len(h) % 2:
        raise AsmError(f"odd hex length: {text!r}")
    if h and not _HEX.match(h):
        raise AsmError(f"invalid hex: {text!r}")
    return bytes.fromhex(h)


def _kv(parts: list[str]) -> dict[str, str]:
    out: dict[str, str] = {}
    for p in parts:
        m = _KV.fullmatch(p)
        if m:
            out[m.group(1)] = m.group(2)
    return out


def _word_clause(word: int, default: int) -> str:
    return "" if word == default else f" word=0x{word:04X}"


def _payload_bytes(tokens: list) -> bytes:
    pay = emit_tokens(tokens)
    if len(pay) % 2:
        pay += b"\x00"
    return pay


def _try_markup_rebuild(tokens: list, markup: str, *, type8: bool, map_stem: str | None) -> bool:
    try:
        rebuilt = apply_markup(
            [],
            markup,
            type8=type8,
            map_stem=map_stem,
        )
    except (ValueError, TypeError, IndexError):
        return False
    return _payload_bytes(rebuilt) == _payload_bytes(tokens)


def _format_say(op: DialogueOp | UiDialogueOp, *, map_stem: str | None) -> list[str]:
    type8 = isinstance(op, UiDialogueOp)
    kind = "type8" if type8 else "type1"
    pay = _payload_bytes(op.tokens)
    expected_hi = _SAY8_HI if type8 else _SAY1_HI
    word_s = _word_clause(op.word & 0xF000, expected_hi)
    try:
        markup = tokens_to_markup(op.tokens, map_stem=map_stem, type8=type8)
    except (ValueError, TypeError, IndexError):
        markup = ""
    lines = [f"say {kind}{word_s}"]
    if markup and _try_markup_rebuild(op.tokens, markup, type8=type8, map_stem=map_stem):
        for row in markup.split("\n"):
            lines.append(f"  {row}")
        return lines
    if markup:
        for row in markup.split("\n"):
            lines.append(f"  # {row}")
    lines.append(f"  hex={pay.hex()}")
    return lines


def _pack_type6_0arg(sub: int) -> bytes:
    return struct.pack("<H", sub & 0x3FFF)


def _pack_type6_1arg(sub: int, arg: int) -> bytes:
    return struct.pack("<HH", (sub & 0x3FFF) | _TYPE6_HI2, arg & 0xFFFF)


def _pack_wait_aux(ticks: int, extra: int = 0) -> bytes:
    return struct.pack("<HHH", 0x0020 | _TYPE6_HI2, ticks & 0xFFFF, extra & 0xFFFF)


def _camera_arg(op_name: str, scene_id: int) -> int | None:
    hi = _CAM_HI.get(op_name)
    if hi is None:
        return None
    return (scene_id & 0x3FFF) | (hi << 14)


def _type6_named(op: ActorCamOp, names: AsmNames) -> tuple[str, int | None, bytes | None]:
    """Mnemonic plus the vanilla packet that mnemonic rebuilds to."""
    args = op.args
    if isinstance(args, CallHookArgs):
        arg = args.hook_id
        if len(args.raw) >= 4:
            arg = struct.unpack_from("<H", args.raw, 2)[0]
        hid = arg & 0x7FFF
        hook_s = format_hook(hid, names)
        pretty = f"call_hook {hook_s} alt" if arg & 0x8000 else f"call_hook {hook_s}"
        return pretty, _TYPE6_WORD, _pack_type6_1arg(0x001A, arg)
    if isinstance(args, WaitReadyArgs):
        if op.sub == 0x0021:
            return f"arm_wait {args.ticks}", _TYPE6_WORD, _pack_type6_1arg(0x0021, args.ticks)
        if op.sub == 0x0020:
            return f"wait_aux 0x{args.ticks:04X}", _WAIT_AUX_WORD, _pack_wait_aux(args.ticks, 0)
        return f"wait {args.ticks}", _TYPE6_WORD, _pack_type6_1arg(0x0011, args.ticks)
    if isinstance(args, CameraArgs):
        pretty = f"camera {args.op} 0x{args.scene_id:02X}"
        packed = _camera_arg(args.op, args.scene_id)
        if packed is None:
            return pretty, None, None
        return pretty, _TYPE6_WORD, _pack_type6_1arg(0x0016, packed)
    if isinstance(args, GiveItemArgs):
        name = "give_item_alt" if op.sub == 0x0005 else "give_item"
        sub = 0x0005 if op.sub == 0x0005 else 0x0004
        item_s = format_item(args.item_id, names)
        return f"{name} {item_s}", _TYPE6_WORD, _pack_type6_1arg(sub, args.item_id)
    if op.sub == 0x0015 or isinstance(args, ActorWalkArgs):
        extra = struct.unpack_from("<H", args.raw, 2)[0] if len(args.raw) >= 4 else 0
        if extra == 0x000C:
            return "sfx recover", _TYPE6_WORD, _pack_type6_1arg(0x0015, 0x000C)
        return f"sfx 0x{extra:04X}", _TYPE6_WORD, _pack_type6_1arg(0x0015, extra)
    if isinstance(args, GenericType6Args) or op.sub in (0x0010, 0x000E, 0x001B, 0x001C):
        if op.sub == 0x0010:
            return "save_menu", _TYPE6_WORD, _pack_type6_0arg(0x0010)
        if op.sub == 0x000E:
            return "restore", _TYPE6_WORD, _pack_type6_0arg(0x000E)
        if op.sub == 0x001B:
            return "stash_menu", _TYPE6_WORD, _pack_type6_0arg(0x001B)
        if op.sub == 0x001C:
            return "retrieve_menu", _TYPE6_WORD, _pack_type6_0arg(0x001C)
    return f"type6 0x{op.sub:04X}", None, None


def _format_type6(op: ActorCamOp, names: AsmNames) -> str:
    pretty, word, raw = _type6_named(op, names)
    if word is not None and raw is not None and op.word == word and op.args.raw == raw:
        return pretty
    return f"{pretty} word=0x{op.word:04X} raw={op.args.raw.hex()}"


def _line_indent(line: str) -> int:
    n = 0
    for ch in line:
        if ch == " ":
            n += 1
        elif ch == "\t":
            n += 2
        else:
            break
    return n


def _referenced_labels(ops: list) -> set[str]:
    names: set[str] = set()
    for item in ops:
        if isinstance(item, (JumpOp, RelJumpOp)):
            names.add(item.target.name)
    return names


def _label_index(ops: list, name: str) -> int | None:
    for i, item in enumerate(ops):
        if isinstance(item, Label) and item.name == name:
            return i
    return None


def _dialog_is_menu(op) -> bool:
    return isinstance(op, (DialogueOp, UiDialogueOp)) and has_menu_control(op.tokens)


def _gate_skip_kind(op: BranchOp) -> str | None:
    """'clear' / 'set' for a 4-byte skip_if_* used in a section gate."""
    if op.extra:
        return None
    if op.word == _BRANCH_CLEAR:
        return "clear"
    if op.word in (_BRANCH_SET, _BRANCH_SET_ALT):
        return "set"
    return None


def _format_if_cond(op: BranchOp, names: AsmNames) -> str:
    kind = _gate_skip_kind(op)
    assert kind is not None
    default = _BRANCH_CLEAR if kind == "clear" else _BRANCH_SET
    return f"{kind} {format_flag(op.arg, names)}{_word_clause(op.word, default)}"


def _match_if_gate(
    ops: list,
    i: int,
    referenced: set[str],
) -> tuple[int, list[BranchOp], JumpOp] | None:
    """If ops[i] starts a liftable gate, return (index_after, skips, jump)."""
    if i >= len(ops):
        return None
    begin = ops[i]
    if not isinstance(begin, ModeOp) or not begin.is_begin or begin.word != _MODE_BEGIN:
        return None
    j = i + 1
    skips: list[BranchOp] = []
    while j < len(ops):
        while j < len(ops) and isinstance(ops[j], Label):
            if ops[j].name in referenced:
                return None
            j += 1
        if j < len(ops) and isinstance(ops[j], BranchOp) and _gate_skip_kind(ops[j]):
            skips.append(ops[j])
            j += 1
            continue
        break
    if not skips:
        return None
    while j < len(ops) and isinstance(ops[j], Label):
        if ops[j].name in referenced:
            return None
        j += 1
    if j >= len(ops):
        return None
    end = ops[j]
    if not isinstance(end, ModeOp) or end.is_begin or end.word != _MODE_END:
        return None
    j += 1
    if j >= len(ops) or not isinstance(ops[j], JumpOp):
        return None
    return j + 1, skips, ops[j]


def _match_if_body(
    ops: list,
    i: int,
    referenced: set[str],
) -> tuple[int, list[BranchOp], JumpOp, list] | None:
    """Gate plus a forward in-script body. Return (after_label, skips, jump, body)."""
    gate = _match_if_gate(ops, i, referenced)
    if gate is None:
        return None
    after, skips, jump = gate
    tgt = _label_index(ops, jump.target.name)
    if tgt is None or tgt < after:
        return None
    if not isinstance(ops[tgt], Label) or ops[tgt].name != jump.target.name:
        return None
    return tgt + 1, skips, jump, list(ops[after:tgt])


def _is_choice_rung(op: BranchOp) -> bool:
    return (
        op.word == _CHOICE_WORD
        and op.arg == _CHOICE_ARG
        and len(op.extra) == 2
    )


def _match_pick_gate(
    ops: list,
    i: int,
    referenced: set[str],
) -> tuple[int, int, JumpOp] | None:
    """If ops[i] starts a 0x4C40 pick rung, return (index_after, pick, jump)."""
    if i >= len(ops):
        return None
    begin = ops[i]
    if not isinstance(begin, ModeOp) or not begin.is_begin or begin.word != _MODE_BEGIN:
        return None
    j = i + 1
    while j < len(ops) and isinstance(ops[j], Label):
        if ops[j].name in referenced:
            return None
        j += 1
    if j >= len(ops) or not isinstance(ops[j], BranchOp) or not _is_choice_rung(ops[j]):
        return None
    pick = int.from_bytes(ops[j].extra, "little")
    j += 1
    while j < len(ops) and isinstance(ops[j], Label):
        if ops[j].name in referenced:
            return None
        j += 1
    if j >= len(ops):
        return None
    end = ops[j]
    if not isinstance(end, ModeOp) or end.is_begin or end.word != _MODE_END:
        return None
    j += 1
    if j >= len(ops) or not isinstance(ops[j], JumpOp):
        return None
    return j + 1, pick, ops[j]


def _match_menu(
    ops: list,
    i: int,
    referenced: set[str],
) -> tuple[int, DialogueOp | UiDialogueOp, list] | None:
    """If ops[i] is a [menu] say plus 0x4C40 rungs, return (after, say, rungs).

    Each rung is ``(pick, jump, body)``. The jump target label is consumed.
    A leftover last option is not wrapped — it stays after the block.
    """
    if i >= len(ops) or not _dialog_is_menu(ops[i]):
        return None
    say = ops[i]
    j = i + 1
    while j < len(ops) and isinstance(ops[j], Label):
        if ops[j].name in referenced:
            return None
        j += 1
    rungs: list = []
    while True:
        gate = _match_pick_gate(ops, j, referenced)
        if gate is None:
            break
        after, pick, jump = gate
        tgt = _label_index(ops, jump.target.name)
        if tgt is None or tgt < after:
            return None
        if not isinstance(ops[tgt], Label) or ops[tgt].name != jump.target.name:
            return None
        rungs.append((pick, jump, list(ops[after:tgt])))
        j = tgt + 1
        while j < len(ops) and isinstance(ops[j], Label) and ops[j].name not in referenced:
            j += 1
    if not rungs:
        return None
    return j, say, rungs


def _script_labels(ops: list) -> set[str]:
    return {item.name for item in ops if isinstance(item, Label)}


def _skip_clause(op: JumpOp, labels: set[str]) -> str:
    if op.target.name in labels:
        return ""
    return f" skip=0x{op.original_skip:X}"


def _rel_clause(op: RelJumpOp, labels: set[str]) -> str:
    if op.target.name in labels:
        return ""
    return f" rel={op.original_rel}"


def _format_jump(op: JumpOp, labels: set[str]) -> str:
    word_s = _word_clause(op.word & 0xF000, _JUMP_HI)
    return f"jump {op.target.name}{_skip_clause(op, labels)}{word_s}"


def _format_branch(op: BranchOp, names: AsmNames) -> str:
    extra = f" extra={op.extra.hex()}" if op.extra else ""
    flag_s = format_flag(op.arg, names)
    if not op.extra:
        if op.word == _BRANCH_CLEAR:
            return f"skip_if_clear {flag_s}"
        if op.word == _BRANCH_SET:
            return f"skip_if_set {flag_s}"
        if op.word == _BRANCH_SET_ALT:
            return f"skip_if_set {flag_s} word=0x{op.word:04X}"
        if op.word == _BRANCH_CHECK:
            return f"check_flag {flag_s}"
    return f"branch word=0x{op.word:04X} arg=0x{op.arg:04X}{extra}"


def _format_pick_header(pick: int, jump: JumpOp, labels: set[str]) -> str:
    word_s = _word_clause(jump.word & 0xF000, _JUMP_HI)
    return f"pick {pick} -> {jump.target.name}{_skip_clause(jump, labels)}{word_s}"


def _format_if_header(skips: list[BranchOp], jump: JumpOp, labels: set[str], names: AsmNames) -> str:
    conds = " and ".join(_format_if_cond(op, names) for op in skips)
    word_s = _word_clause(jump.word & 0xF000, _JUMP_HI)
    return f"if {conds} -> {jump.target.name}{_skip_clause(jump, labels)}{word_s} {{"


def _indent_rows(rows: list[str], n: int) -> list[str]:
    pad = " " * n
    return [pad + row if row else row for row in rows]


def _format_ops(
    ops: list,
    referenced: set[str],
    labels: set[str],
    names: AsmNames,
    *,
    map_stem: str | None,
) -> list[str]:
    lines: list[str] = []
    i = 0
    while i < len(ops):
        menu = _match_menu(ops, i, referenced)
        if menu is not None:
            nxt, say, rungs = menu
            lines.append("menu {")
            lines.extend(_indent_rows(_format_say(say, map_stem=map_stem), 2))
            for pick, jump, body in rungs:
                lines.append(f"  {_format_pick_header(pick, jump, labels)}")
                lines.extend(_indent_rows(_format_ops(body, referenced, labels, names, map_stem=map_stem), 4))
            lines.append("}")
            i = nxt
            continue
        body_if = _match_if_body(ops, i, referenced)
        if body_if is not None:
            nxt, skips, jump, body = body_if
            lines.append(_format_if_header(skips, jump, labels, names))
            lines.extend(_indent_rows(_format_ops(body, referenced, labels, names, map_stem=map_stem), 2))
            lines.append("}")
            i = nxt
            continue
        gate = _match_if_gate(ops, i, referenced)
        if gate is not None:
            nxt, skips, jump = gate
            conds = " and ".join(_format_if_cond(op, names) for op in skips)
            lines.append(f"if {conds}")
            lines.append(f"  {_format_jump(jump, labels)}")
            i = nxt
            continue
        pick_gate = _match_pick_gate(ops, i, referenced)
        if pick_gate is not None:
            nxt, pick, jump = pick_gate
            lines.append(f"if pick == {pick}")
            lines.append(f"  {_format_jump(jump, labels)}")
            i = nxt
            continue
        item = ops[i]
        if isinstance(item, Label):
            lines.append(f"label {item.name}")
        elif isinstance(item, ModeOp):
            name = "flag_ctx_begin" if item.is_begin else "flag_ctx_end"
            default = _MODE_BEGIN if item.is_begin else _MODE_END
            lines.append(f"{name}{_word_clause(item.word, default)}")
        elif isinstance(item, FlagOp):
            name = "set_flag" if item.is_set else "clear_flag"
            default = _FLAG_SET if item.is_set else _FLAG_CLEAR
            lines.append(f"{name} {format_flag(item.flag_id, names)}{_word_clause(item.word, default)}")
        elif isinstance(item, YieldOp):
            lines.append(f"yield{_word_clause(item.word, _YIELD)}")
        elif isinstance(item, JumpOp):
            lines.append(_format_jump(item, labels))
        elif isinstance(item, RelJumpOp):
            word_s = _word_clause(item.word, _RELJUMP)
            lines.append(f"reljump {item.target.name}{_rel_clause(item, labels)}{word_s}")
        elif isinstance(item, BranchOp):
            lines.append(_format_branch(item, names))
        elif isinstance(item, ActorCamOp):
            lines.append(_format_type6(item, names))
        elif isinstance(item, (DialogueOp, UiDialogueOp)):
            lines.extend(_format_say(item, map_stem=map_stem))
        elif isinstance(item, RawOp):
            lines.append(f"raw hex={item.raw.hex()}")
        else:
            raise AsmError(f"cannot format {type(item).__name__}")
        i += 1
    return lines


def format_script(script: Script, *, map_stem: str | None = None) -> str:
    """Print one script as assembler text."""
    referenced = _referenced_labels(script.ops)
    labels = _script_labels(script.ops)
    names = load_asm_names(map_stem)
    lines = [f"script 0x{script.script_id:04X}", ""]
    lines.extend(_format_ops(script.ops, referenced, labels, names, map_stem=map_stem))
    lines.append("")
    return "\n".join(lines)


def _parse_type6_line(kind: str, rest: list[str], kv: dict[str, str], names: AsmNames) -> ActorCamOp:
    has_raw = "raw" in kv
    has_word = "word" in kv
    if has_raw ^ has_word:
        raise AsmError(f"{kind} needs both word= and raw=, or neither")
    if has_raw:
        word = parse_int(kv["word"])
        raw = parse_hex_bytes(kv["raw"])
        if len(raw) < 2:
            raise AsmError(f"{kind} raw too short")
        sub = struct.unpack_from("<H", raw, 0)[0] & 0x3FFF
        if kind == "call_hook":
            hook_id = resolve_named(rest[0], names.hook_name_to_id, parse_int) if rest else 0
            return ActorCamOp(word=word, sub=sub, args=CallHookArgs(hook_id=hook_id, raw=raw))
        if kind in ("wait", "arm_wait", "wait_aux"):
            ticks = parse_int(rest[0]) if rest else 0
            return ActorCamOp(word=word, sub=sub, args=WaitReadyArgs(ticks=ticks, raw=raw))
        if kind == "camera":
            op_name = rest[0] if rest else "activate"
            scene = parse_int(rest[1]) if len(rest) > 1 else 0
            return ActorCamOp(
                word=word, sub=sub,
                args=CameraArgs(op=op_name, scene_id=scene, raw=raw),
            )
        if kind in ("give_item", "give_item_alt"):
            item_id = resolve_named(rest[0], names.item_name_to_id, parse_int) if rest else 0
            return ActorCamOp(word=word, sub=sub, args=GiveItemArgs(item_id=item_id, raw=raw))
        if kind == "walk":
            return ActorCamOp(word=word, sub=sub, args=ActorWalkArgs(raw=raw))
        if kind == "sfx":
            return ActorCamOp(word=word, sub=sub, args=ActorWalkArgs(raw=raw))
        if kind in ("save_menu", "restore", "stash_menu", "retrieve_menu"):
            return ActorCamOp(word=word, sub=sub, args=GenericType6Args(sub=sub, raw=raw))
        if kind == "type6":
            if rest:
                sub = parse_int(rest[0])
            return ActorCamOp(word=word, sub=sub, args=GenericType6Args(sub=sub, raw=raw))
        raise AsmError(f"unknown type6 kind {kind}")
    if kind == "call_hook":
        hook_id = resolve_named(rest[0], names.hook_name_to_id, parse_int) if rest else 0
        alt = any(p == "alt" for p in rest[1:])
        arg = (hook_id & 0x7FFF) | (0x8000 if alt else 0)
        raw = _pack_type6_1arg(0x001A, arg)
        return ActorCamOp(
            word=_TYPE6_WORD, sub=0x001A,
            args=CallHookArgs(hook_id=hook_id & 0x7FFF, raw=raw),
        )
    if kind == "wait":
        ticks = parse_int(rest[0]) if rest else 0
        raw = _pack_type6_1arg(0x0011, ticks)
        return ActorCamOp(word=_TYPE6_WORD, sub=0x0011, args=WaitReadyArgs(ticks=ticks, raw=raw))
    if kind == "arm_wait":
        ticks = parse_int(rest[0]) if rest else 0
        raw = _pack_type6_1arg(0x0021, ticks)
        return ActorCamOp(word=_TYPE6_WORD, sub=0x0021, args=WaitReadyArgs(ticks=ticks, raw=raw))
    if kind == "wait_aux":
        ticks = parse_int(rest[0]) if rest else 0
        raw = _pack_wait_aux(ticks, 0)
        return ActorCamOp(word=_WAIT_AUX_WORD, sub=0x0020, args=WaitReadyArgs(ticks=ticks, raw=raw))
    if kind == "camera":
        op_name = rest[0] if rest else "activate"
        scene = parse_int(rest[1]) if len(rest) > 1 else 0
        packed = _camera_arg(op_name, scene)
        if packed is None:
            raise AsmError(f"camera needs activate/overlay/deactivate/deactivate_overlay, got {op_name!r}")
        raw = _pack_type6_1arg(0x0016, packed)
        return ActorCamOp(
            word=_TYPE6_WORD, sub=0x0016,
            args=CameraArgs(op=op_name, scene_id=scene, raw=raw),
        )
    if kind in ("give_item", "give_item_alt"):
        item_id = resolve_named(rest[0], names.item_name_to_id, parse_int) if rest else 0
        sub = 0x0005 if kind == "give_item_alt" else 0x0004
        raw = _pack_type6_1arg(sub, item_id)
        return ActorCamOp(word=_TYPE6_WORD, sub=sub, args=GiveItemArgs(item_id=item_id, raw=raw))
    if kind == "walk":
        extra = parse_int(rest[0]) if rest else 0
        raw = _pack_type6_1arg(0x0015, extra)
        return ActorCamOp(word=_TYPE6_WORD, sub=0x0015, args=ActorWalkArgs(raw=raw))
    if kind == "sfx":
        extra = 0x000C
        if rest:
            extra = 0x000C if rest[0] == "recover" else parse_int(rest[0])
        raw = _pack_type6_1arg(0x0015, extra)
        return ActorCamOp(word=_TYPE6_WORD, sub=0x0015, args=ActorWalkArgs(raw=raw))
    if kind == "save_menu":
        raw = _pack_type6_0arg(0x0010)
        return ActorCamOp(
            word=_TYPE6_WORD, sub=0x0010,
            args=GenericType6Args(sub=0x0010, raw=raw),
        )
    if kind == "restore":
        raw = _pack_type6_0arg(0x000E)
        return ActorCamOp(
            word=_TYPE6_WORD, sub=0x000E,
            args=GenericType6Args(sub=0x000E, raw=raw),
        )
    if kind == "stash_menu":
        raw = _pack_type6_0arg(0x001B)
        return ActorCamOp(
            word=_TYPE6_WORD, sub=0x001B,
            args=GenericType6Args(sub=0x001B, raw=raw),
        )
    if kind == "retrieve_menu":
        raw = _pack_type6_0arg(0x001C)
        return ActorCamOp(
            word=_TYPE6_WORD, sub=0x001C,
            args=GenericType6Args(sub=0x001C, raw=raw),
        )
    raise AsmError(f"{kind} needs word= and raw=")


def _make_say(kind: str, word_hi: int, tokens: list, explicit_word: int | None) -> DialogueOp | UiDialogueOp:
    pay = _payload_bytes(tokens)
    hi = explicit_word if explicit_word is not None else word_hi
    word = (hi & 0xF000) | (len(pay) & 0x0FFF)
    if kind == "type8":
        return UiDialogueOp(word=word, tokens=tokens)
    return DialogueOp(word=word, tokens=tokens)


def _parse_say_block(
    kind: str,
    kv: dict[str, str],
    body: list[str],
    *,
    map_stem: str | None,
) -> DialogueOp | UiDialogueOp:
    type8 = kind == "type8"
    word_hi = parse_int(kv["word"]) if "word" in kv else (_SAY8_HI if type8 else _SAY1_HI)
    hex_lines = [ln.strip()[4:] for ln in body if ln.strip().startswith("hex=")]
    markup_lines = [
        ln
        for ln in body
        if not ln.strip().startswith("hex=") and not ln.strip().startswith("#")
    ]
    if hex_lines:
        tokens = tokenise_payload(parse_hex_bytes("".join(hex_lines)))
        return _make_say(kind, word_hi, tokens, word_hi if "word" in kv else None)
    markup = "\n".join(markup_lines)
    tokens = apply_markup([], markup, type8=type8, map_stem=map_stem)
    return _make_say(kind, word_hi, tokens, word_hi if "word" in kv else None)


def _parse_op_line(head: str, rest: list[str], kv: dict[str, str], names: AsmNames) -> object:
    if head == "flag_ctx_begin":
        word = parse_int(kv["word"]) if "word" in kv else _MODE_BEGIN
        return ModeOp(word=word)
    if head == "flag_ctx_end":
        word = parse_int(kv["word"]) if "word" in kv else _MODE_END
        return ModeOp(word=word)
    if head == "set_flag":
        fid = resolve_named(rest[0], names.flag_name_to_id, parse_int) if rest else 0
        word = parse_int(kv["word"]) if "word" in kv else _FLAG_SET
        return FlagOp(word=word, flag_id=fid)
    if head == "clear_flag":
        fid = resolve_named(rest[0], names.flag_name_to_id, parse_int) if rest else 0
        word = parse_int(kv["word"]) if "word" in kv else _FLAG_CLEAR
        return FlagOp(word=word, flag_id=fid)
    if head == "yield":
        word = parse_int(kv["word"]) if "word" in kv else _YIELD
        return YieldOp(word=word)
    if head == "jump":
        if not rest or not _LABEL.match(rest[0]):
            raise AsmError(f"jump needs a label: {rest}")
        skip = parse_int(kv["skip"]) if "skip" in kv else 0
        hi = parse_int(kv["word"]) if "word" in kv else _JUMP_HI
        return JumpOp(word=hi & 0xF000, target=LabelRef(rest[0]), original_skip=skip)
    if head == "reljump":
        if not rest or not _LABEL.match(rest[0]):
            raise AsmError(f"reljump needs a label: {rest}")
        rel = parse_int(kv["rel"]) if "rel" in kv else 0
        word = parse_int(kv["word"]) if "word" in kv else _RELJUMP
        return RelJumpOp(word=word, target=LabelRef(rest[0]), original_rel=rel)
    if head == "skip_if_clear":
        arg = resolve_named(rest[0], names.flag_name_to_id, parse_int) if rest else 0
        word = parse_int(kv["word"]) if "word" in kv else _BRANCH_CLEAR
        extra = parse_hex_bytes(kv["extra"]) if "extra" in kv else b""
        return BranchOp(word=word, arg=arg, extra=extra, target=LabelRef("_"))
    if head == "skip_if_set":
        arg = resolve_named(rest[0], names.flag_name_to_id, parse_int) if rest else 0
        word = parse_int(kv["word"]) if "word" in kv else _BRANCH_SET
        extra = parse_hex_bytes(kv["extra"]) if "extra" in kv else b""
        return BranchOp(word=word, arg=arg, extra=extra, target=LabelRef("_"))
    if head == "check_flag":
        arg = resolve_named(rest[0], names.flag_name_to_id, parse_int) if rest else 0
        word = parse_int(kv["word"]) if "word" in kv else _BRANCH_CHECK
        extra = parse_hex_bytes(kv["extra"]) if "extra" in kv else b""
        return BranchOp(word=word, arg=arg, extra=extra, target=LabelRef("_"))
    if head == "branch":
        if "word" not in kv or "arg" not in kv:
            raise AsmError("branch needs word= and arg=")
        extra = parse_hex_bytes(kv["extra"]) if "extra" in kv else b""
        return BranchOp(
            word=parse_int(kv["word"]),
            arg=parse_int(kv["arg"]),
            extra=extra,
            target=LabelRef("_"),
        )
    if head == "raw":
        if "hex" not in kv:
            raise AsmError("raw needs hex=")
        return RawOp(raw=parse_hex_bytes(kv["hex"]))
    if head in (
        "call_hook", "wait", "arm_wait", "wait_aux", "camera",
        "give_item", "give_item_alt", "walk", "sfx", "save_menu",
        "restore", "stash_menu", "retrieve_menu", "type6",
    ):
        pos = [p for p in rest if "=" not in p]
        return _parse_type6_line(head, pos, kv, names)
    raise AsmError(f"unknown op {head!r}")


def _parse_if_conds(text: str, names: AsmNames) -> list[BranchOp]:
    chunks = re.split(r"\s+and\s+", text.strip())
    if not chunks or not chunks[0]:
        raise AsmError("if needs at least one condition")
    skips: list[BranchOp] = []
    for chunk in chunks:
        m = _IF_COND.match(chunk.strip())
        if not m:
            raise AsmError(f"bad if condition {chunk!r}")
        kind, arg_s, word_s = m.group(1), m.group(2), m.group(3)
        arg = resolve_named(arg_s, names.flag_name_to_id, parse_int)
        if kind == "clear":
            word = parse_int(word_s) if word_s else _BRANCH_CLEAR
        else:
            word = parse_int(word_s) if word_s else _BRANCH_SET
        skips.append(BranchOp(word=word, arg=arg, extra=b"", target=LabelRef("_")))
    return skips


def _expand_if_gate(skips: list[BranchOp], jump: JumpOp) -> list:
    return [ModeOp(word=_MODE_BEGIN), *skips, ModeOp(word=_MODE_END), jump]


def _expand_pick_gate(pick: int, jump: JumpOp) -> list:
    extra = pick.to_bytes(2, "little")
    rung = BranchOp(
        word=_CHOICE_WORD,
        arg=_CHOICE_ARG,
        extra=extra,
        target=LabelRef("_"),
    )
    return _expand_if_gate([rung], jump)


def _collect_indented_raw(raw_lines: list[str], i: int, parent_indent: int) -> tuple[list[str], int]:
    """Take following lines indented past ``parent_indent``. Dedent by parent+2."""
    body: list[str] = []
    cut = parent_indent + 2
    while i < len(raw_lines):
        nxt = raw_lines[i]
        nxt_s = nxt.strip()
        nxt_ind = _line_indent(nxt)
        if not nxt_s:
            # A column-0 blank ends the block. An indented all-space line is
            # markup (leading spaces or an empty box line), not a separator.
            if nxt_ind <= parent_indent:
                if body:
                    break
                i += 1
                continue
        if nxt_s.startswith("#") and nxt_ind <= parent_indent:
            break
        if nxt_ind > parent_indent:
            if len(nxt) >= cut and nxt[:cut].isspace():
                body.append(nxt[cut:])
            else:
                body.append(nxt.lstrip())
            i += 1
            continue
        break
    return body, i


def _skip_noise_at(raw_lines: list[str], i: int) -> int:
    while i < len(raw_lines):
        s = raw_lines[i].strip()
        if not s or s.startswith("#"):
            i += 1
            continue
        break
    return i


def _parse_if_arrow_header(cond_src: str) -> tuple[str, JumpOp] | None:
    m = _IF_ARROW.match(cond_src.strip())
    if not m:
        return None
    cond, name, skip_s, word_s, _brace = m.groups()
    if _IF_PICK.match(cond.strip()):
        return None
    skip = parse_int(skip_s) if skip_s else 0
    hi = parse_int(word_s) if word_s else _JUMP_HI
    jump = JumpOp(word=hi & 0xF000, target=LabelRef(name), original_skip=skip)
    return cond.strip(), jump


def _parse_if_block(cond_src: str, body: list[str], names: AsmNames) -> list:
    jump_line = next((ln for ln in body if ln and not ln.startswith("#")), None)
    if not jump_line:
        raise AsmError("if needs an indented jump")
    jtok = jump_line.split()
    if jtok[0] != "jump":
        raise AsmError(f"if body must be a jump, got {jtok[0]!r}")
    jump = _parse_op_line("jump", jtok[1:], _kv(jtok[1:]), names)
    if not isinstance(jump, JumpOp):
        raise AsmError("if body must be a jump")
    pick_m = _IF_PICK.match(cond_src)
    if pick_m:
        pick = parse_int(pick_m.group(1))
        if not 0 <= pick <= 0xFFFF:
            raise AsmError(f"pick index out of range: {pick}")
        return _expand_pick_gate(pick, jump)
    return _expand_if_gate(_parse_if_conds(cond_src, names), jump)


def _parse_pick_header(stripped: str) -> tuple[int, JumpOp]:
    m = _PICK_HEAD.match(stripped)
    if not m:
        raise AsmError(f"bad pick header {stripped!r}")
    pick = parse_int(m.group(1))
    if not 0 <= pick <= 0xFFFF:
        raise AsmError(f"pick index out of range: {pick}")
    skip = parse_int(m.group(3)) if m.group(3) else 0
    hi = parse_int(m.group(4)) if m.group(4) else _JUMP_HI
    jump = JumpOp(word=hi & 0xF000, target=LabelRef(m.group(2)), original_skip=skip)
    return pick, jump


def _parse_statement(raw_lines: list[str], i: int, *, map_stem: str | None, names: AsmNames) -> tuple[list, int]:
    """Parse one statement at ``raw_lines[i]``. Returns (ops, next_index)."""
    line = raw_lines[i]
    indent = _line_indent(line)
    stripped = line.strip()
    tokens = stripped.split()
    head = tokens[0]
    rest = tokens[1:]
    kv = _kv(rest)
    if head == "label":
        name = rest[0] if rest else ""
        if not _LABEL.match(name):
            raise AsmError(f"bad label {name!r}")
        return [Label(name)], i + 1
    if head == "if":
        cond_src = " ".join(p for p in rest)
        braced = cond_src.rstrip().endswith("{")
        arrow = _parse_if_arrow_header(cond_src)
        i += 1
        if arrow is not None:
            cond_only, jump = arrow
            body_ops: list = []
            while True:
                i = _skip_noise_at(raw_lines, i)
                if i >= len(raw_lines):
                    if braced:
                        raise AsmError("unclosed if {")
                    break
                if raw_lines[i].strip() == "}":
                    i += 1
                    break
                if not braced and _line_indent(raw_lines[i]) <= indent:
                    break
                stmt, i = _parse_statement(raw_lines, i, map_stem=map_stem, names=names)
                body_ops.extend(stmt)
            return [
                *_expand_if_gate(_parse_if_conds(cond_only, names), jump),
                *body_ops,
                Label(jump.target.name),
            ], i
        body: list[str] = []
        while i < len(raw_lines):
            nxt = raw_lines[i]
            nxt_s = nxt.strip()
            nxt_ind = _line_indent(nxt)
            if not nxt_s:
                if body:
                    break
                i += 1
                continue
            if nxt_s.startswith("#") and nxt_ind <= indent:
                break
            if nxt_ind > indent:
                body.append(nxt_s)
                i += 1
                continue
            break
        return _parse_if_block(cond_src, body, names), i
    if head == "say":
        kind = rest[0] if rest else "type1"
        if kind not in ("type1", "type8"):
            raise AsmError(f"say needs type1 or type8, got {kind!r}")
        body, i = _collect_indented_raw(raw_lines, i + 1, indent)
        return [_parse_say_block(kind, kv, body, map_stem=map_stem)], i
    if head == "menu":
        if not stripped.endswith("{"):
            raise AsmError("menu needs {")
        i += 1
        inner: list = []
        while True:
            while i < len(raw_lines):
                s = raw_lines[i].strip()
                if not s or s.startswith("#"):
                    i += 1
                    continue
                break
            if i >= len(raw_lines):
                raise AsmError("unclosed menu {")
            if raw_lines[i].strip() == "}":
                return inner, i + 1
            stmt, i = _parse_statement(raw_lines, i, map_stem=map_stem, names=names)
            inner.extend(stmt)
    if head == "pick":
        pick, jump = _parse_pick_header(stripped)
        i += 1
        body_ops: list = []
        while True:
            while i < len(raw_lines):
                s = raw_lines[i].strip()
                if not s or s.startswith("#"):
                    i += 1
                    continue
                break
            if i >= len(raw_lines):
                break
            if raw_lines[i].strip() == "}":
                break
            if _line_indent(raw_lines[i]) <= indent:
                break
            stmt, i = _parse_statement(raw_lines, i, map_stem=map_stem, names=names)
            body_ops.extend(stmt)
        return [*_expand_pick_gate(pick, jump), *body_ops, Label(jump.target.name)], i
    return [_parse_op_line(head, rest, kv, names)], i + 1


def parse_script_text(text: str, *, map_stem: str | None = None) -> Script:
    """Parse assembler text into a ``Script``."""
    raw_lines = text.lstrip("\ufeff").splitlines()
    i = 0
    ops: list = []
    names = load_asm_names(map_stem)

    def skip_noise() -> None:
        nonlocal i
        while i < len(raw_lines):
            s = raw_lines[i].strip()
            if not s or s.startswith("#"):
                i += 1
                continue
            break

    skip_noise()
    if i >= len(raw_lines) or not raw_lines[i].strip().startswith("script"):
        raise AsmError("expected 'script 0xNNNN' as the first statement")
    hdr = raw_lines[i].split()
    if len(hdr) < 2:
        raise AsmError("script line needs an id")
    script_id = parse_int(hdr[1])
    i += 1

    while i < len(raw_lines):
        skip_noise()
        if i >= len(raw_lines):
            break
        line = raw_lines[i]
        if _line_indent(line) > 0:
            raise AsmError(f"unexpected indented line: {line!r}")
        stmt, i = _parse_statement(raw_lines, i, map_stem=map_stem, names=names)
        ops.extend(stmt)

    return Script(script_id=script_id, ops=ops)


def roundtrip_bytes(script: Script, *, map_stem: str | None = None) -> bytes:
    """format → parse → emit. Used by tests and ``--check``."""
    text = format_script(script, map_stem=map_stem)
    parsed = parse_script_text(text, map_stem=map_stem)
    parsed.script_id = script.script_id
    return emit_script(parsed)


def _dump_all(
    stem: str,
    dest: Path,
    *,
    field: Path | None,
    text: Path | None,
) -> int:
    import json

    from field_script_disasm import DEFAULT_CONTENT, DEFAULT_TEXT
    from field_script_ir import load_ir

    sf = load_ir(stem, "auto", field or DEFAULT_CONTENT, text or DEFAULT_TEXT)
    payload = [
        {"id": sc.script_id, "text": format_script(sc, map_stem=stem)}
        for sc in sf.scripts
    ]
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
    print(f"dumped {len(payload)} scripts -> {dest}")
    return 0


def _load_one(stem: str, script_id: int):
    from field_script_disasm import DEFAULT_CONTENT, DEFAULT_TEXT
    from field_script_ir import load_ir

    sf = load_ir(stem, "auto", DEFAULT_CONTENT, DEFAULT_TEXT)
    sc = next((s for s in sf.scripts if s.script_id == script_id), None)
    if sc is None:
        raise SystemExit(f"{stem}: no script 0x{script_id:04X}")
    return sc


def _emit_text(path: Path, dest: Path, *, map_stem: str | None) -> int:
    text = path.read_text(encoding="utf-8-sig")
    sc = parse_script_text(text, map_stem=map_stem)
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(emit_script(sc))
    print(f"emit 0x{sc.script_id:04X} {len(dest.read_bytes())} bytes -> {dest}")
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Print / check field-script assembler text")
    ap.add_argument("stem", nargs="?", default=None, help="map stem, e.g. BA38")
    ap.add_argument("--id", type=lambda s: int(s, 0), default=0, help="script id (default 0)")
    ap.add_argument("--check", action="store_true", help="assert emit(parse(format)) == emit")
    ap.add_argument("--dump", type=Path, metavar="JSON", help="write every script as JSON [{id,text}]")
    ap.add_argument("--emit", type=Path, metavar="ASM", help="assemble one script text file to bytes")
    ap.add_argument("-o", "--output", type=Path, help="byte output (with --emit)")
    ap.add_argument("--field", type=Path, default=None, help="FIELD folder (with --dump)")
    ap.add_argument("--text", type=Path, default=None, help="TEXT/EN folder (with --dump)")
    args = ap.parse_args(argv)
    if args.emit:
        if args.output is None:
            raise SystemExit("--emit needs -o/--output")
        return _emit_text(args.emit, args.output, map_stem=args.stem)
    if not args.stem:
        raise SystemExit("stem is required unless --emit is set")
    if args.dump:
        return _dump_all(args.stem, args.dump, field=args.field, text=args.text)
    sc = _load_one(args.stem, args.id)
    text = format_script(sc, map_stem=args.stem)
    if args.check:
        got = emit_script(parse_script_text(text, map_stem=args.stem))
        want = emit_script(sc)
        if got != want:
            print(f"FAIL {args.stem} 0x{args.id:04X} emit {len(want)} vs {len(got)}", file=sys.stderr)
            return 1
        print(f"ok {args.stem} 0x{args.id:04X} ({len(want)} bytes)")
        return 0
    sys.stdout.write(text)
    if not text.endswith("\n"):
        sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
