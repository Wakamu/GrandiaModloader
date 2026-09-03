#!/usr/bin/env python3
"""Resolve Save/stash/NPC menu choices to the ops each pick runs.

A menu dialog has a *stream* ``0x05`` (presenter menu mode). That byte
is either the first payload byte (Save/Recover) or sits immediately
before the option ``[P:]`` after a spoken prompt. A ``0x05`` right after
a header is face extra 5 (``[P:4]``), not a menu. Save/stash use starred
option lines; NPC talks (Lilly's excuses, Yes/No) use the spoken lines.
After the dialog, vanilla uses this compare/jump ladder (type-3 word ``0x4C40``):
vanilla uses this compare/jump ladder (type-3 word ``0x4C40``):

    flag_ctx_begin
    branch 0x4C40 arg=0x4000 extra=N   # choice-result != N
    flag_ctx_end
    jump ELSE                         # taken when this is not the pick
    ACTION…                           # runs when choice == N
    label ELSE

``0x4000`` is the last menu pick (loaded by ``+0x6FF90``). Extra ``N`` is the
0-based option index. Compare mode of ``0x4C40`` is ``!=`` (``esi>>7 & 7``).
The leftover option (usually Cancel) has no ``N`` and falls through the ladder.
A ``* * * Title * * *`` header line is not a pick.
"""
from __future__ import annotations

from typing import Any

from field_script_ir import (
    BranchOp,
    DialogueOp,
    HeaderToken,
    IrOp,
    JumpOp,
    Label,
    ModeOp,
    NewlineToken,
    PageBreakToken,
    RawByteToken,
    TextToken,
    UiDialogueOp,
)
from portrait_resolver import is_printable_face_extra
from field_script_op_hints import timeline_label_for_op

_CHOICE_LO12 = 0xC40
_CHOICE_ARG = 0x4000
_PARTY_SLOT = 0x21


def option_label(line: str) -> str | None:
    """Return the inner label if this line is a starred menu option."""
    import re

    s = (line or "").replace(r"\n", "\n").strip()
    if len(s) < 3 or not s.startswith("*") or not s.endswith("*"):
        return None
    parts = [p.strip() for p in re.split(r"\*+", s) if p.strip()]
    if not parts:
        return None
    label = re.sub(r"\s+", " ", " ".join(parts)).strip()
    return label or None


def is_menu_title_line(line: str) -> bool:
    """True for a ``* * * Header * * *`` title, not a pickable row."""
    s = (line or "").strip()
    return s.startswith("* * *") and s.endswith("* * *")


def starred_menu_lines(tokens: list[Any]) -> list[str]:
    labels: list[str] = []
    for tok in tokens:
        if not isinstance(tok, TextToken):
            continue
        for raw in (tok.text or "").split("\n"):
            label = option_label(raw)
            if label:
                labels.append(label if not is_menu_title_line(raw) else f"#{label}")
    return labels


def pickable_labels(tokens: list[Any]) -> list[str]:
    """Option rows only — drop a leading stash-style title."""
    labels: list[str] = []
    for tok in tokens:
        if not isinstance(tok, TextToken):
            continue
        for raw in (tok.text or "").split("\n"):
            if is_menu_title_line(raw):
                continue
            label = option_label(raw)
            if label:
                labels.append(label)
    return labels


def has_menu_control(tokens: list[Any]) -> bool:
    """True if the stream has choice-menu mode (not a [P:4] face extra)."""
    from field_script_dialog_markup import is_stream_menu

    return any(is_stream_menu(tokens, i) for i in range(len(tokens)))


def _normalize_option_line(line: str) -> str:
    if is_menu_title_line(line):
        return ""
    return option_label(line) or (line or "").strip()


def _last_party_header_index(tokens: list[Any]) -> int | None:
    last: int | None = None
    for i, tok in enumerate(tokens):
        if isinstance(tok, HeaderToken) and tok.b1 == _PARTY_SLOT:
            last = i
    return last


def menu_option_labels(
    tokens: list[Any],
    *,
    n_options: int | None = None,
) -> list[str]:
    """Labels the player actually picks.

    Prompt text (Lilly's welcome) stays before the last party still
    ``[P:0 slot=0x21]``. After that come plain lines and/or ``* * Save * *``.
    With no party still, a starred-only menu (Save/Recover) or the last
    *n* spoken lines are the picks.
    """
    starred = pickable_labels(tokens)
    spoken = spoken_menu_lines(tokens)
    party_i = _last_party_header_index(tokens)
    after_party = spoken_menu_lines(tokens[party_i:]) if party_i is not None else []

    n = int(n_options or 0)
    k = max(n, len(starred), len(after_party) if after_party else 0)

    if after_party:
        picks = [_normalize_option_line(ln) for ln in after_party]
    elif k and len(spoken) >= k:
        picks = [_normalize_option_line(ln) for ln in spoken[-k:]]
    else:
        picks = list(starred) or [_normalize_option_line(ln) for ln in spoken]
    picks = [ln for ln in picks if ln]
    if n and len(picks) > n and not after_party:
        picks = picks[-n:]
    elif n and after_party and len(picks) > n:
        picks = picks[-n:]
    return picks


def spoken_menu_lines(tokens: list[Any]) -> list[str]:
    """Spoken lines of a dialog, with post-header face extras stripped."""
    lines: list[str] = []
    pending = ""

    def flush() -> None:
        nonlocal pending
        text = pending.strip()
        pending = ""
        if text:
            lines.append(text)

    i = 0
    n = len(tokens)
    while i < n:
        tok = tokens[i]
        if isinstance(tok, HeaderToken):
            flush()
            j = i + 1
            while (
                j < n
                and isinstance(tokens[j], RawByteToken)
                and tokens[j].value in {0x00, 0x02, 0x06}
            ):
                j += 1
            if j < n and isinstance(tokens[j], TextToken):
                chunk = tokens[j].text or ""
                if chunk and is_printable_face_extra(ord(chunk[0])):
                    pending += chunk[1:]
                    i = j + 1
                    continue
            i += 1
            continue
        if isinstance(tok, TextToken):
            pending += tok.text or ""
        elif isinstance(tok, (NewlineToken, PageBreakToken)):
            flush()
        i += 1
    flush()
    return lines


def choice_index(op: IrOp) -> int | None:
    if not isinstance(op, BranchOp):
        return None
    if (op.word & 0x0FFF) != _CHOICE_LO12:
        return None
    if not op.extra:
        return 0
    return int.from_bytes(op.extra[:2].ljust(2, b"\x00"), "little")


def _skip_labels(ops: list[IrOp], i: int) -> int:
    n = len(ops)
    while i < n and isinstance(ops[i], Label):
        i += 1
    return i


def _label_index(ops: list[IrOp], name: str) -> int | None:
    for i, op in enumerate(ops):
        if isinstance(op, Label) and op.name == name:
            return i
    return None


def _first_content(ops: list[IrOp], start: int, end: int) -> int | None:
    for i in range(start, min(end, len(ops))):
        if isinstance(ops[i], Label):
            continue
        return i
    return None


def _parse_choice_block(ops: list[IrOp], i: int) -> tuple[int, int, int, int] | None:
    """Return (choice_index, jump_op, action_start, action_end) or None."""
    n = len(ops)
    i = _skip_labels(ops, i)
    if i >= n or not isinstance(ops[i], ModeOp) or not ops[i].is_begin:
        return None
    i = _skip_labels(ops, i + 1)
    if i >= n:
        return None
    idx = choice_index(ops[i])
    if idx is None:
        return None
    i = _skip_labels(ops, i + 1)
    if i < n and isinstance(ops[i], ModeOp) and not ops[i].is_begin:
        i += 1
    i = _skip_labels(ops, i)
    if i >= n or not isinstance(ops[i], JumpOp):
        return None
    jump_i = i
    action_start = i + 1
    else_i = _label_index(ops, ops[i].target.name)
    action_end = else_i if else_i is not None else n
    return idx, jump_i, action_start, action_end


def resolve_choice_routes(
    ops: list[IrOp],
    dialog_index: int,
    *,
    labels: list[str] | None = None,
) -> list[dict[str, Any]]:
    """Map each pickable option to the ops that run when it is chosen."""
    if dialog_index < 0 or dialog_index >= len(ops):
        return []
    dialog = ops[dialog_index]
    if not isinstance(dialog, (DialogueOp, UiDialogueOp)):
        return []
    blocks: list[tuple[int, int, int, int]] = []
    i = dialog_index + 1
    while True:
        parsed = _parse_choice_block(ops, i)
        if parsed is None:
            break
        idx, jump_i, action_start, action_end = parsed
        blocks.append((idx, jump_i, action_start, action_end))
        i = action_end if action_end > jump_i else jump_i + 1
        if i <= jump_i:
            break

    starred = pickable_labels(dialog.tokens)
    n_ladder = max(b[0] for b in blocks) + 1 if blocks else 0
    if labels is not None:
        picks = list(labels)
    else:
        picks = menu_option_labels(dialog.tokens)
        if n_ladder and len(picks) < n_ladder:
            picks = menu_option_labels(dialog.tokens, n_options=n_ladder)

    if not blocks:
        if not starred and labels is None:
            return []
        if not picks:
            return []

    if blocks:
        n_opts = max(len(picks), n_ladder)
        while len(picks) < n_opts:
            picks.append(f"Option {len(picks)}")

    fallthrough = blocks[-1][3] if blocks else dialog_index + 1
    routes: list[dict[str, Any]] = []
    for opt_i, label in enumerate(picks):
        hit = next((b for b in blocks if b[0] == opt_i), None)
        if hit is not None:
            _idx, jump_i, start, end = hit
            first = _first_content(ops, start, end)
            routes.append(
                {
                    "index": opt_i,
                    "label": label,
                    "kind": "action",
                    "actionOpIndex": first,
                    "actionEndOpIndex": end,
                    "jumpOpIndex": jump_i,
                    "summary": (
                        timeline_label_for_op(ops[first]).replace("\u2192", "->")
                        if first is not None
                        else "(no ops)"
                    ),
                }
            )
            continue
        first = _first_content(ops, fallthrough, len(ops))
        routes.append(
            {
                "index": opt_i,
                "label": label,
                "kind": "fallthrough",
                "actionOpIndex": first,
                "actionEndOpIndex": None,
                "jumpOpIndex": None,
                "summary": (
                    timeline_label_for_op(ops[first]).replace("\u2192", "->")
                    if first is not None
                    else "end of script"
                ),
            }
        )
    return routes
