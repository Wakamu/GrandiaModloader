#!/usr/bin/env python3
"""High-level editor API for Grandia field scripts.

Provides safe, label-aware mutation operations on ScriptFile / Script objects
built by field_script_ir.  Changes are accumulated in an ``EditorSession``
and written out via ``emit_script_file``.

Mutation operations:
  - ``insert_op(script_id, before_index, op)``    insert an op before another
  - ``insert_dialogue_op(...)``                   type-1 talk, optional [menu]
  - ``insert_choice_ladder(...)``                 0x4C40 pick rungs after an op
  - ``append_op(script_id, op)``                  append an op at the end
  - ``remove_op(script_id, index)``               remove an op by list index
  - ``remove_script_op(script_id, index)``      remove any op (dialogue-aware)
  - ``remove_dialogue_op(script_id, index)``    remove dialogue + sync waits
  - ``replace_op(script_id, index, op)``          swap an op in-place
  - ``set_dialogue_text(script_id, index, texts)`` replace text in a dialogue op
  - ``apply_script_asm(script_id, text)``         replace one script from assembler text
  - ``apply_hook_asm(hook_session, text)``        replace MDP table-1 / table-2 from assembler text

All ops are kept in the ordered ``Script.ops`` list.  Labels are embedded in
the list (``Label`` nodes with zero size).  The emitter resolves branch
distances automatically, so inserting or removing ops between a jump and its
target is safe as long as no branch points outside the script's own range.

Cross-script jumps (``JumpOp.target.name not in label_offsets``) are
preserved using the ``original_skip``/``original_rel`` fallback in the emitter.
"""
from __future__ import annotations

import difflib
import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from portrait_resolver import (
    PRINTABLE_FACE_EXTRA_CHARS,
    is_printable_face_extra,
    packed_face_index_to_extra,
)
from field_script_dialog_markup import BOX_LINE_MAX_CHARS, BOX_MAX_LINES, apply_markup
from field_script_ir import (
    ActorCamOp,
    BranchOp,
    CameraArgs,
    DialogueOp,
    DToken,
    FaceKeyToken,
    FlagOp,
    GiveItemArgs,
    HeaderToken,
    HoldToken,
    IrOp,
    JumpOp,
    Label,
    LabelRef,
    LineStartToken,
    ModeOp,
    NewlineToken,
    PageBreakToken,
    CallHookArgs,
    RawByteToken,
    RawOp,
    RelJumpOp,
    Script,
    ScriptFile,
    TerminatorToken,
    TextToken,
    UiDialogueOp,
    WaitReadyArgs,
    YieldOp,
    emit_script,
    emit_script_file,
    emit_tokens,
    load_ir,
    parse_file_to_ir,
    tokenise_payload,
)


# ---------------------------------------------------------------------------
# Helper: build a minimal DialogueOp from text + original metadata
# ---------------------------------------------------------------------------

def build_dialogue_op_from_pages(
    pages: list[tuple[int, int, int, str]],  # [(pre, b1, expr, text)]
    holds: list[int] | None = None,
    face_keys: list[int | None] | None = None,
) -> DialogueOp:
    """Build a new ``DialogueOp`` from a list of (pre, b1, expr, text) pages.

    ``holds`` defaults to 30 frames per page if not supplied.
    ``face_keys`` defaults to None (no face-key token) if not supplied.
    """
    tokens: list[DToken] = []
    for i, (pre, b1, expr, text) in enumerate(pages):
        from field_script_ir import HeaderToken, NewlineToken
        tokens.append(HeaderToken(pre=pre, b1=b1, b2=0x0A, b3=0x0C, expr=expr))
        tokens.append(LineStartToken())
        # Encode text with \n → NewlineToken.
        parts = text.split(r"\n")
        for j, part in enumerate(parts):
            if part:
                tokens.append(TextToken(part))
            if j < len(parts) - 1:
                tokens.append(NewlineToken())
        hold = (holds[i] if holds and i < len(holds) else 30)
        tokens.append(HoldToken(hold=hold))
        fk = face_keys[i] if face_keys and i < len(face_keys) else None
        if fk is not None:
            tokens.append(FaceKeyToken(face_key=fk))
        if i < len(pages) - 1:
            tokens.append(RawByteToken(0x08))
    tokens.append(TerminatorToken())

    pay = emit_tokens(tokens)
    if len(pay) % 2:
        pay += b"\x00"
    payload_n = len(pay)
    # type-1 word: nibble 2 (type_idx=1) → 0x2000 | payload_n
    word = 0x2000 | payload_n
    return DialogueOp(word=word, tokens=tokens)


def build_wait_ready_op(ticks: int) -> ActorCamOp:
    """Build a type-6 wait_ready op for the given tick count."""
    # sub=0x0011, hi=1 (one u16 arg), outer word: nibble=7→0x7000, lower nibble=1
    # payload word: sub | (hi << 14) where hi=1 means 1 extra word
    # From: type6_payload_len(outer_word, pw): hi=1, n=(outer&0xF)+1=2, n&1==0→ extra=2*1=2
    # So payload = 2 (payload word) + 2 (ticks word) = 4 bytes
    outer_word = 0x7001   # nibble=7, n=1 → type6
    payload_word = 0x0011 | (1 << 14)   # sub=0x11, hi=1
    raw_payload = struct.pack("<HH", payload_word, ticks)
    from field_script_ir import WaitReadyArgs
    args = WaitReadyArgs(ticks=ticks, raw=raw_payload)
    return ActorCamOp(word=outer_word, sub=0x0011, args=args)


def build_call_hook_op(hook_id: int) -> ActorCamOp:
    """Build a type-6 call_hook op that dispatches MDP sec[7] hook ``hook_id``."""
    hid = int(hook_id) & 0x7FFF
    if hid <= 0:
        raise ValueError("hook_id must be in 1..32767")
    outer_word = 0x7001
    payload_word = 0x001A | (1 << 14)  # sub=call_hook, hi=1 (one u16 arg)
    raw_payload = struct.pack("<HH", payload_word, hid)
    args = CallHookArgs(hook_id=hid, raw=raw_payload)
    return ActorCamOp(word=outer_word, sub=0x001A, args=args)


def build_yield_op() -> YieldOp:
    """Build a yield (chunk boundary) op."""
    return YieldOp(word=0xF000)


_CHOICE_BRANCH_WORD = 0x4C40
_CHOICE_ARG = 0x4000
_MIN_CHOICE_OPTIONS = 2
_MAX_CHOICE_OPTIONS = 8


def build_choice_compare(n: int, else_name: str) -> BranchOp:
    """Type-3 ``0x4C40``: last pick (arg ``0x4000``) != extra ``N``."""
    return BranchOp(
        word=_CHOICE_BRANCH_WORD,
        arg=_CHOICE_ARG,
        extra=(int(n) & 0xFFFF).to_bytes(2, "little"),
        target=LabelRef(else_name),
    )


def build_blank_dialogue_op(*, menu_options: int = 0) -> DialogueOp:
    """Type-1 talk. ``menu_options`` >= 2 writes ``[menu]`` and numbered picks."""
    n = int(menu_options or 0)
    if n >= 2:
        lines = "\n".join(f"Option {i + 1}" for i in range(n))
        markup = f"[menu][P:0 slot=0x21]\n{lines}[wait]"
    else:
        markup = "[P:0]\n...[wait]"
    tokens = apply_markup([], markup, type8=False)
    pay = emit_tokens(tokens)
    if len(pay) % 2:
        pay += b"\x00"
    return DialogueOp(word=0x2000 | len(pay), tokens=tokens)


def build_jump_op(target: LabelRef, *, original_skip: int = 0) -> JumpOp:
    """Build a type-2 forward jump. Skip distance is resolved on emit."""
    return JumpOp(word=0x3000, target=target, original_skip=original_skip)


def build_rel_jump_op(target: LabelRef, *, original_rel: int = 0) -> RelJumpOp:
    """Build a type-7 relative jump. Rel distance is resolved on emit."""
    return RelJumpOp(word=0x8000, target=target, original_rel=original_rel)


def build_flag_ctx_begin() -> ModeOp:
    return ModeOp(word=0x1001)


def build_flag_ctx_end() -> ModeOp:
    return ModeOp(word=0x1000)


def build_skip_if_clear(flag_id: int) -> BranchOp:
    """Type-3 skip_if_clear — arms the following section jump when flag is clear."""
    return BranchOp(
        word=0x4001,
        arg=flag_id & 0xFFFF,
        extra=b"",
        target=LabelRef("L0000"),
    )


# Flag unlikely to be set — skip_if_clear stays armed for section jumps we insert.
_INSERT_JUMP_ARM_FLAG = 0xFFFF


def _jump_gate_block(jump_op: JumpOp | RelJumpOp) -> list[IrOp]:
    """Wrap a jump so the engine actually takes it (bare type-2 is ignored)."""
    return [
        build_flag_ctx_begin(),
        build_skip_if_clear(_INSERT_JUMP_ARM_FLAG),
        build_flag_ctx_end(),
        jump_op,
    ]


def _fresh_label_name(ops: list[IrOp]) -> str:
    return _fresh_label_names(ops, 1)[0]


def _fresh_label_names(ops: list[IrOp], count: int) -> list[str]:
    max_n = 0
    for item in ops:
        if isinstance(item, Label) and len(item.name) >= 2 and item.name[0] == "L":
            suffix = item.name[1:]
            if suffix.isdigit():
                max_n = max(max_n, int(suffix))
    return [f"L{max_n + 1 + i:04d}" for i in range(max(0, int(count)))]


def _ensure_label_at_op_index(ops: list[IrOp], op_index: int) -> tuple[str, int]:
    """Ensure a Label exists at ``op_index``. Returns (label_name, insert_before_index)."""
    if op_index < 0 or op_index >= len(ops):
        raise IndexError(f"op index {op_index} out of range for {len(ops)} op(s)")

    op = ops[op_index]
    if isinstance(op, Label):
        return op.name, op_index
    if op_index > 0 and isinstance(ops[op_index - 1], Label):
        return ops[op_index - 1].name, op_index - 1

    name = _fresh_label_name(ops)
    ops.insert(op_index, Label(name))
    return name, op_index


def _resolve_jump_target_op_index(ops: list[IrOp], target_op_index: int | None, target_label_name: str | None) -> int:
    if target_label_name is not None:
        for i, op in enumerate(ops):
            if isinstance(op, Label) and op.name == target_label_name:
                return i
        raise ValueError(f"label {target_label_name!r} not found in script")
    if target_op_index is None:
        raise ValueError("targetOpListIndex or targetLabelName is required")
    if target_op_index < 0 or target_op_index >= len(ops):
        raise IndexError(f"target op index {target_op_index} out of range for {len(ops)} op(s)")
    return target_op_index


_ARM_WAIT_SUB = 0x0021
_WAIT_READY_SUB = 0x0011
_WAIT_READY_AUX_SUB = 0x0020


def _dialogue_sync_prelude_indices(ops: list[IrOp], dialogue_index: int) -> list[int]:
    """Return op indices to remove before dialogue to avoid textbox sync softlocks."""
    if dialogue_index <= 0:
        return []

    i = dialogue_index - 1
    aux_indices: list[int] = []
    while i >= 0 and len(aux_indices) < 2:
        op = ops[i]
        if isinstance(op, ActorCamOp) and op.sub == _WAIT_READY_AUX_SUB:
            aux_indices.append(i)
            i -= 1
            continue
        break
    if len(aux_indices) == 2:
        return sorted(aux_indices)

    i = dialogue_index - 1
    if i < 0 or not isinstance(ops[i], ActorCamOp) or ops[i].sub != _WAIT_READY_SUB:
        return []
    if not isinstance(ops[i].args, WaitReadyArgs) or ops[i].args.ticks != 1:
        return []
    wait_idx = i
    i -= 1
    if i < 0 or not isinstance(ops[i], ActorCamOp) or ops[i].sub != _ARM_WAIT_SUB:
        return []
    return [i, wait_idx]


def _dialogue_sync_postlude_indices(ops: list[IrOp], dialogue_index: int) -> list[int]:
    """Return op indices to remove after dialogue that wait for its textbox to finish."""
    i = dialogue_index + 1
    if i < len(ops) and isinstance(ops[i], ActorCamOp) and ops[i].sub == 0x0016:
        i += 1
    if i + 2 >= len(ops):
        return []
    a, b, c = ops[i], ops[i + 1], ops[i + 2]
    if not (
        isinstance(a, ActorCamOp)
        and a.sub == _WAIT_READY_SUB
        and isinstance(a.args, WaitReadyArgs)
        and a.args.ticks == 1
        and isinstance(b, ActorCamOp)
        and b.sub == _ARM_WAIT_SUB
        and isinstance(c, ActorCamOp)
        and c.sub == _WAIT_READY_SUB
        and isinstance(c.args, WaitReadyArgs)
        and c.args.ticks == 1
    ):
        return []
    return [i, i + 1, i + 2]


def _dialogue_sync_indices(ops: list[IrOp], dialogue_index: int) -> list[int]:
    """All sync-wait op indices paired with one dialogue op."""
    return sorted(
        set(_dialogue_sync_prelude_indices(ops, dialogue_index))
        | set(_dialogue_sync_postlude_indices(ops, dialogue_index))
    )


def _assert_no_orphan_dialogue_sync_waits(ops: list[IrOp]) -> None:
    """Raise if an arm_wait + wait_ready(1) pair sits immediately before non-dialogue."""
    for i in range(len(ops) - 2):
        a, b, c = ops[i], ops[i + 1], ops[i + 2]
        if not (
            isinstance(a, ActorCamOp)
            and a.sub == _ARM_WAIT_SUB
            and isinstance(b, ActorCamOp)
            and b.sub == _WAIT_READY_SUB
            and isinstance(b.args, WaitReadyArgs)
            and b.args.ticks == 1
        ):
            continue
        if not isinstance(c, (DialogueOp, UiDialogueOp)):
            raise ValueError(
                f"orphaned textbox sync wait at op index {i + 1}: "
                "arm_wait + wait_ready(1) is not immediately followed by dialogue"
            )


def describe_script_op(op: IrOp) -> str:
    """Short human-readable label for an IR op."""
    from field_script_op_hints import timeline_label_for_op

    return timeline_label_for_op(op)


# ---------------------------------------------------------------------------
# EditorSession
# ---------------------------------------------------------------------------

class EditorSession:
    """Accumulates mutations to a ``ScriptFile`` and provides safe write-out.

    Usage::

        sess = EditorSession.open("2000", field_root, text_root)
        sc = sess.get_script(0x0000)
        # Find the index of a specific DialogueOp by source offset.
        idx = sess.find_op_index(0x0000, source_offset=0x0048)
        # Replace dialogue text on that op.
        sess.set_dialogue_text(0x0000, idx, [
            "Hello, world!",
            "This is page two.",
        ])
        # Insert a wait-ready before that op.
        sess.insert_op(0x0000, idx, build_wait_ready_op(60))
        # Write out.
        new_scn, new_ofs = sess.emit()
    """

    def __init__(self, sf: ScriptFile, field_root: Path | None = None) -> None:
        self._sf = sf
        self._field_root = Path(field_root) if field_root is not None else None
        self._dirty: set[int] = set()

    # ------------------------------------------------------------------
    # Factory
    # ------------------------------------------------------------------

    @classmethod
    def open(
        cls,
        stem: str,
        field_root: Path,
        text_root: Path,
        source: str = "auto",
    ) -> "EditorSession":
        sf = load_ir(stem, source, field_root, text_root)
        return cls(sf, field_root)

    @classmethod
    def from_script_file(cls, sf: ScriptFile, field_root: Path | None = None) -> "EditorSession":
        return cls(sf, field_root)

    # ------------------------------------------------------------------
    # Accessors
    # ------------------------------------------------------------------

    @property
    def script_file(self) -> ScriptFile:
        return self._sf

    @property
    def dirty(self) -> bool:
        return bool(self._dirty)

    def get_script(self, script_id: int) -> Script:
        for sc in self._sf.scripts:
            if sc.script_id == script_id:
                return sc
        raise KeyError(f"script 0x{script_id:04X} not found")

    def get_scripts(self) -> list[Script]:
        return self._sf.scripts

    def find_op_index(self, script_id: int, *, source_offset: int) -> int:
        """Return the index in the op list of the op with the given source_offset."""
        sc = self.get_script(script_id)
        for i, op in enumerate(sc.ops):
            if isinstance(op, Label):
                continue
            if getattr(op, "source_offset", None) == source_offset:
                return i
        raise LookupError(f"no op at source_offset={source_offset:#x} in script 0x{script_id:04X}")

    def list_dialogue_ops(self, script_id: int) -> list[tuple[int, DialogueOp | UiDialogueOp]]:
        """Return [(list_index, op)] for all dialogue/ui-dialogue ops."""
        sc = self.get_script(script_id)
        result = []
        for i, op in enumerate(sc.ops):
            if isinstance(op, (DialogueOp, UiDialogueOp)):
                result.append((i, op))
        return result

    # ------------------------------------------------------------------
    # Mutation operations
    # ------------------------------------------------------------------

    def insert_op(self, script_id: int, before_index: int, new_op: IrOp) -> None:
        """Insert ``new_op`` into the script before op at ``before_index``."""
        sc = self.get_script(script_id)
        sc.ops.insert(before_index, new_op)
        self._dirty.add(script_id)

    def insert_dialogue_op(
        self,
        script_id: int,
        op_list_index: int,
        *,
        position: str = "after",
        menu_options: int = 0,
    ) -> int:
        """Insert a type-1 dialogue op before or after ``op_list_index``."""
        if position not in {"before", "after"}:
            raise ValueError("position must be 'before' or 'after'")
        sc = self.get_script(script_id)
        if op_list_index < 0 or op_list_index >= len(sc.ops):
            raise IndexError(f"op_list_index {op_list_index} out of range for {len(sc.ops)} op(s)")
        insert_index = op_list_index if position == "before" else op_list_index + 1
        self.insert_op(script_id, insert_index, build_blank_dialogue_op(menu_options=menu_options))
        return insert_index

    def insert_choice_ladder(
        self,
        script_id: int,
        after_index: int,
        n_options: int,
        *,
        fallthrough: bool = False,
    ) -> int:
        """Insert a vanilla ``0x4C40`` pick ladder after ``after_index``.

        Each rung is flag_ctx + branch != N + jump + a Yield stub + ELSE label.
        With ``fallthrough``, the last pick has no extra and lands on a Yield
        after the last ELSE (Save/Cancel style). Otherwise every pick has a rung
        (Lilly style).
        """
        n = int(n_options)
        if n < _MIN_CHOICE_OPTIONS or n > _MAX_CHOICE_OPTIONS:
            raise ValueError(
                f"option count must be {_MIN_CHOICE_OPTIONS}–{_MAX_CHOICE_OPTIONS}, got {n}"
            )
        sc = self.get_script(script_id)
        if after_index < 0 or after_index >= len(sc.ops):
            raise IndexError(f"after_index {after_index} out of range for {len(sc.ops)} op(s)")
        n_rungs = n - 1 if fallthrough else n
        names = _fresh_label_names(sc.ops, n_rungs)
        block: list[IrOp] = []
        for i, else_name in enumerate(names):
            block.extend(
                [
                    build_flag_ctx_begin(),
                    build_choice_compare(i, else_name),
                    build_flag_ctx_end(),
                    build_jump_op(LabelRef(else_name), original_skip=2),
                    build_yield_op(),
                    Label(else_name),
                ]
            )
        if fallthrough:
            block.append(build_yield_op())
        insert_at = after_index + 1
        sc.ops[insert_at:insert_at] = block
        self._dirty.add(script_id)
        return insert_at

    def insert_jump_to_op(
        self,
        script_id: int,
        before_index: int,
        *,
        target_op_index: int | None = None,
        target_label_name: str | None = None,
    ) -> tuple[int, str, str]:
        """Insert a jump before ``before_index``.

        Forward targets get ``flag_ctx + skip_if_clear + type-2`` (bare type-2
        is ignored). Backward targets get a bare type-7 RelJump plus a Yield,
        which is how vanilla loops a menu after a talk.

        Returns ``(inserted_index, label_name, jump_kind)`` where ``jump_kind``
        is ``forward`` or ``relative``.
        """
        from field_script_ir import _resolve_op_sizes

        sc = self.get_script(script_id)
        if before_index < 0 or before_index > len(sc.ops):
            raise IndexError(f"before_index {before_index} out of range for {len(sc.ops)} op(s)")

        target_index = _resolve_jump_target_op_index(sc.ops, target_op_index, target_label_name)
        if isinstance(sc.ops[target_index], Label):
            label_name = sc.ops[target_index].name
            label_at = target_index
        else:
            label_name, label_at = _ensure_label_at_op_index(sc.ops, target_index)
            if label_at < before_index:
                before_index += 1

        if before_index == label_at or before_index == label_at + 1:
            raise ValueError("Jump target must differ from the insert location")

        def _resolve_jump_in_block(trial_ops: list[IrOp], jump_idx: int) -> tuple[JumpOp | RelJumpOp, str]:
            label_offsets, op_offsets = _resolve_op_sizes(trial_ops)
            jump_off = op_offsets.get(jump_idx)
            target_off = label_offsets.get(label_name)
            if jump_off is None or target_off is None:
                raise RuntimeError("failed to resolve jump offsets for label-aware insert")
            forward_skip = target_off - (jump_off + 2)
            if forward_skip > 0:
                if forward_skip > 0xFFF:
                    raise ValueError(
                        f"Forward jump distance {forward_skip} bytes exceeds the 4095-byte limit; "
                        "pick a closer target."
                    )
                return build_jump_op(LabelRef(label_name), original_skip=forward_skip), "forward"
            rel = target_off - (jump_off + 4)
            if not -32768 <= rel <= 32767:
                raise ValueError("Jump target is too far away for a relative jump")
            if rel == 0:
                raise ValueError("Jump target must differ from the insert location")
            return build_rel_jump_op(LabelRef(label_name), original_rel=rel), "relative"

        probe = list(sc.ops)
        probe.insert(before_index, build_jump_op(LabelRef(label_name)))
        _, jump_kind = _resolve_jump_in_block(probe, before_index)
        if jump_kind == "relative":
            trial_ops = list(sc.ops)
            trial_ops.insert(before_index, build_rel_jump_op(LabelRef(label_name)))
            jump_op, jump_kind = _resolve_jump_in_block(trial_ops, before_index)
            block = [jump_op, build_yield_op()]
        else:
            placeholder_block = _jump_gate_block(build_jump_op(LabelRef(label_name)))
            trial_ops = list(sc.ops)
            trial_ops[before_index:before_index] = placeholder_block
            jump_idx = before_index + len(placeholder_block) - 1
            jump_op, jump_kind = _resolve_jump_in_block(trial_ops, jump_idx)
            block = _jump_gate_block(jump_op)

        for offset, op in enumerate(block):
            sc.ops.insert(before_index + offset, op)
        self._dirty.add(script_id)
        return before_index, label_name, jump_kind

    def append_op(self, script_id: int, new_op: IrOp) -> None:
        """Append ``new_op`` at the end of the script (before any trailing Label)."""
        sc = self.get_script(script_id)
        # Insert before trailing labels / yield.
        insert_at = len(sc.ops)
        for i in range(len(sc.ops) - 1, -1, -1):
            if isinstance(sc.ops[i], (Label, YieldOp)):
                insert_at = i
            else:
                break
        sc.ops.insert(insert_at, new_op)
        self._dirty.add(script_id)

    def remove_op(self, script_id: int, index: int) -> IrOp:
        """Remove and return the op at ``index``."""
        sc = self.get_script(script_id)
        removed = sc.ops.pop(index)
        self._dirty.add(script_id)
        return removed

    def remove_dialogue_op(self, script_id: int, index: int) -> DialogueOp | UiDialogueOp:
        """Remove a dialogue op and any immediately preceding textbox sync waits."""
        sc = self.get_script(script_id)
        op = sc.ops[index]
        if not isinstance(op, (DialogueOp, UiDialogueOp)):
            raise TypeError(f"op at index {index} is {type(op).__name__}, not a dialogue op")
        prelude = _dialogue_sync_indices(sc.ops, index)
        removed_dialogue = sc.ops[index]
        assert isinstance(removed_dialogue, (DialogueOp, UiDialogueOp))
        for rm_idx in sorted({index, *prelude}, reverse=True):
            sc.ops.pop(rm_idx)
        self._dirty.add(script_id)
        return removed_dialogue

    def remove_script_op(self, script_id: int, index: int) -> IrOp:
        """Remove one script op. Dialogue ops also drop paired textbox sync waits."""
        sc = self.get_script(script_id)
        if index < 0 or index >= len(sc.ops):
            raise IndexError(f"op index {index} out of range for {len(sc.ops)} op(s)")
        op = sc.ops[index]
        if isinstance(op, Label):
            raise ValueError("Cannot remove label ops; they are branch targets.")
        if isinstance(op, (DialogueOp, UiDialogueOp)):
            return self.remove_dialogue_op(script_id, index)
        return self.remove_op(script_id, index)

    def replace_op(self, script_id: int, index: int, new_op: IrOp) -> IrOp:
        """Replace the op at ``index`` with ``new_op``.  Returns the old op."""
        sc = self.get_script(script_id)
        old = sc.ops[index]
        sc.ops[index] = new_op
        self._dirty.add(script_id)
        return old

    def set_call_hook_id(self, script_id: int, index: int, hook_id: int) -> ActorCamOp:
        """Replace a call_hook op's hook id in place. Returns the new op."""
        sc = self.get_script(script_id)
        if index < 0 or index >= len(sc.ops):
            raise IndexError(f"op index {index} out of range for {len(sc.ops)} op(s)")
        old = sc.ops[index]
        if not (isinstance(old, ActorCamOp) and isinstance(old.args, CallHookArgs)):
            raise ValueError(f"op #{index} is not a call_hook")
        new_op = build_call_hook_op(hook_id)
        sc.ops[index] = new_op
        self._dirty.add(script_id)
        return new_op

    def set_dialogue_text(
        self,
        script_id: int,
        index: int,
        page_texts: list[str],
        *,
        preserve_metadata: bool = True,
    ) -> None:
        """Replace the text content of a ``DialogueOp`` at ``index``.

        ``page_texts`` is a list of page strings; \\n within a string emits an
        inline newline within the page.

        When ``preserve_metadata=True`` (the default), the original hold timings,
        face-key tokens, header bytes (pre, b1, expr), and inter-cue control
        bytes are preserved for all pages that exist in the original.  Extra new
        pages use the last page's metadata.
        """
        sc = self.get_script(script_id)
        op = sc.ops[index]
        if not isinstance(op, (DialogueOp, UiDialogueOp)):
            raise TypeError(f"op at index {index} is {type(op).__name__}, not a dialogue op")
        if preserve_metadata:
            if isinstance(op, UiDialogueOp):
                current_pages = _ui_display_pages(op.tokens)
            else:
                current_pages = _dialogue_text_pages(op.tokens)
            if current_pages == page_texts:
                return

        if isinstance(op, UiDialogueOp):
            _validate_ui_layout_edit(op.tokens, page_texts)
            new_tokens = _rebuild_ui_dialogue_tokens(op.tokens, page_texts)
        elif not preserve_metadata:
            # Minimal rebuild: just text tokens with default hold=30.
            new_tokens: list[DToken] = []
            for i, text in enumerate(page_texts):
                if isinstance(op, DialogueOp):
                    # Try to reuse header from original.
                    orig_headers = [t for t in op.tokens if isinstance(t, HeaderToken)]
                    if i < len(orig_headers):
                        new_tokens.append(orig_headers[i])
                    elif orig_headers:
                        new_tokens.append(orig_headers[-1])
                new_tokens.append(LineStartToken())
                parts = text.split(r"\n")
                for j, part in enumerate(parts):
                    if part:
                        new_tokens.append(TextToken(part))
                    if j < len(parts) - 1:
                        from field_script_ir import NewlineToken
                        new_tokens.append(NewlineToken())
                new_tokens.append(HoldToken(hold=30))
                if i < len(page_texts) - 1:
                    new_tokens.append(RawByteToken(0x08))
            new_tokens.append(TerminatorToken())
        else:
            new_tokens = _rebuild_dialogue_tokens(op.tokens, page_texts)

        # Rebuild op with new token stream.
        pay = emit_tokens(new_tokens)
        if len(pay) % 2:
            pay += b"\x00"
        new_word = (op.word & 0xF000) | len(pay)
        if isinstance(op, DialogueOp):
            sc.ops[index] = DialogueOp(word=new_word, tokens=new_tokens, source_offset=op.source_offset)
        else:
            sc.ops[index] = UiDialogueOp(word=new_word, tokens=new_tokens, source_offset=op.source_offset)
        self._dirty.add(script_id)

    def _apply_ui_cue_list(self, script_id: int, index: int, cues: list[str]) -> None:
        sc = self.get_script(script_id)
        op = sc.ops[index]
        if not isinstance(op, UiDialogueOp):
            raise TypeError(f"op at index {index} is {type(op).__name__}, not a UiDialogueOp")
        current = _ui_display_pages(op.tokens)
        if current == cues:
            return
        _validate_ui_layout_edit(op.tokens, cues)
        new_tokens = _rebuild_ui_dialogue_tokens(op.tokens, cues)
        pay = emit_tokens(new_tokens)
        if len(pay) % 2:
            pay += b"\x00"
        new_word = (op.word & 0xF000) | len(pay)
        sc.ops[index] = UiDialogueOp(word=new_word, tokens=new_tokens, source_offset=op.source_offset)
        self._dirty.add(script_id)

    def set_ui_cue_text(
        self,
        script_id: int,
        index: int,
        cue_index: int,
        text: str,
    ) -> None:
        """Replace one visible cue inside a Type-8 ``UiDialogueOp``."""
        op = self.get_script(script_id).ops[index]
        if not isinstance(op, UiDialogueOp):
            raise TypeError(f"op at index {index} is {type(op).__name__}, not a UiDialogueOp")
        cues = _ui_display_pages(op.tokens)
        if cue_index < 0 or cue_index >= len(cues):
            raise IndexError(f"cue index {cue_index} out of range for {len(cues)} cue(s)")
        cues[cue_index] = _normalize_editor_text(text)
        self._apply_ui_cue_list(script_id, index, cues)

    def insert_ui_cue(
        self,
        script_id: int,
        index: int,
        cue_index: int,
        text: str,
        *,
        position: str = "after",
    ) -> int:
        """Insert a new Type-8 cue before/after ``cue_index``. Returns the new cue index."""
        if position not in {"before", "after"}:
            raise ValueError("position must be 'before' or 'after'")
        op = self.get_script(script_id).ops[index]
        if not isinstance(op, UiDialogueOp):
            raise TypeError(f"op at index {index} is {type(op).__name__}, not a UiDialogueOp")
        cues = _ui_display_pages(op.tokens)
        if cue_index < 0 or cue_index >= len(cues):
            raise IndexError(f"cue index {cue_index} out of range for {len(cues)} cue(s)")
        insert_at = cue_index if position == "before" else cue_index + 1
        cues.insert(insert_at, _normalize_editor_text(text))
        self._apply_ui_cue_list(script_id, index, cues)
        return insert_at

    def remove_ui_cue(self, script_id: int, index: int, cue_index: int) -> None:
        """Remove one cue from a Type-8 ``UiDialogueOp``."""
        op = self.get_script(script_id).ops[index]
        if not isinstance(op, UiDialogueOp):
            raise TypeError(f"op at index {index} is {type(op).__name__}, not a UiDialogueOp")
        cues = _ui_display_pages(op.tokens)
        if len(cues) <= 1:
            raise ValueError("Cannot remove the only cue in this Type-8 op")
        if cue_index < 0 or cue_index >= len(cues):
            raise IndexError(f"cue index {cue_index} out of range for {len(cues)} cue(s)")
        del cues[cue_index]
        self._apply_ui_cue_list(script_id, index, cues)

    def set_t8_text(self, script_id: int, index: int, line_texts: list[str]) -> None:
        """Replace text in a ``UiDialogueOp`` while preserving holds/face-keys."""
        self.set_dialogue_text(script_id, index, line_texts)

    def apply_dialog_blocks(
        self,
        script_id: int,
        index: int,
        blocks: list[dict[str, Any]],
    ) -> None:
        """Replace every cue/page in a dialogue op from editor blocks.

        Each block is ``{text, hold, advance, expr, b1}``. ``advance`` is
        ``auto``, ``confirm``, or ``select``. Auto-delay is a HoldToken
        (frames). Wait-for-confirm is no HoldToken. ``\\p`` is box-clear and is
        kept in the text. Type-8 cues always keep a HoldToken (it delimits
        cues), so they are auto-delay. Type-1 also splits on HoldToken, so
        intra-box delays are separate blocks. A pause-only Hold (no new text)
        may have empty text.
        """
        sc = self.get_script(script_id)
        op = sc.ops[index]
        if not isinstance(op, (DialogueOp, UiDialogueOp)):
            raise TypeError(f"op at index {index} is {type(op).__name__}, not a dialogue op")
        if not blocks:
            raise ValueError("Dialog op must keep at least one block")

        type8 = isinstance(op, UiDialogueOp)
        texts: list[str] = []
        for i, block in enumerate(blocks):
            raw = block.get("text")
            if not isinstance(raw, str):
                raise ValueError(f"block {i} text must be a string")
            text = _normalize_editor_text(raw)
            is_pause = str(block.get("role") or "") == "pause" or (
                not text.strip() and block.get("hold") is not None
            )
            if not text.strip() and not is_pause:
                raise ValueError(f"block {i + 1} cannot be empty")
            texts.append(text)

        self.set_dialogue_text(script_id, index, texts)
        op = sc.ops[index]
        new_tokens = _patch_dialog_block_metadata(
            list(op.tokens),
            blocks,
            isinstance(op, UiDialogueOp),
            map_stem=self._sf.stem,
            field_root=self._field_root,
        )
        pay = emit_tokens(new_tokens)
        if len(pay) % 2:
            pay += b"\x00"
        new_word = (op.word & 0xF000) | len(pay)
        if isinstance(op, DialogueOp):
            sc.ops[index] = DialogueOp(word=new_word, tokens=new_tokens, source_offset=op.source_offset)
        else:
            sc.ops[index] = UiDialogueOp(word=new_word, tokens=new_tokens, source_offset=op.source_offset)
        self._dirty.add(script_id)

    def convert_dialogue_kind(self, script_id: int, index: int, *, type8: bool) -> None:
        """Flip type-1 ↔ type-8. Payload tokens stay; only the opcode nibble changes.

        Type-8 → type-1 drops ``09 01`` (LineStart). On a type-1 kickoff that
        packet XORs present flag ``0x400`` and the confirm prompt never arms.
        """
        sc = self.get_script(script_id)
        op = sc.ops[index]
        if not isinstance(op, (DialogueOp, UiDialogueOp)):
            raise TypeError(f"op at index {index} is {type(op).__name__}, not a dialogue op")
        already = isinstance(op, UiDialogueOp)
        if already == type8:
            return
        new_word = (0x9000 if type8 else 0x2000) | (op.word & 0x0FFF)
        tokens = list(op.tokens)
        if not type8:
            tokens = [t for t in tokens if not isinstance(t, LineStartToken)]
        off = op.source_offset
        if type8:
            sc.ops[index] = UiDialogueOp(word=new_word, tokens=tokens, source_offset=off)
        else:
            sc.ops[index] = DialogueOp(word=new_word, tokens=tokens, source_offset=off)
        self._dirty.add(script_id)

    def apply_dialog_markup(self, script_id: int, index: int, markup: str) -> None:
        """Replace a dialogue op from linear editor markup (``[P:]`` / ``[D:]`` / ``[TS:]`` / ``[clear]`` / ``[wait]``)."""
        from field_script_dialog_markup import apply_markup

        sc = self.get_script(script_id)
        op = sc.ops[index]
        if not isinstance(op, (DialogueOp, UiDialogueOp)):
            raise TypeError(f"op at index {index} is {type(op).__name__}, not a dialogue op")
        if not isinstance(markup, str):
            raise ValueError("markup must be a string")
        type8 = isinstance(op, UiDialogueOp)
        new_tokens = apply_markup(
            list(op.tokens),
            markup,
            type8=type8,
            map_stem=self._sf.stem,
            field_root=self._field_root,
        )
        if not new_tokens:
            raise ValueError("Dialog markup produced an empty token stream")
        pay = emit_tokens(new_tokens)
        if len(pay) % 2:
            pay += b"\x00"
        new_word = (op.word & 0xF000) | len(pay)
        if isinstance(op, DialogueOp):
            sc.ops[index] = DialogueOp(word=new_word, tokens=new_tokens, source_offset=op.source_offset)
        else:
            sc.ops[index] = UiDialogueOp(word=new_word, tokens=new_tokens, source_offset=op.source_offset)
        self._dirty.add(script_id)

    def script_asm_text(self, script_id: int) -> str:
        """Pretty assembler for one script. Same as ``format_script``."""
        from field_script_asm import format_script

        return format_script(self.get_script(script_id), map_stem=self._sf.stem)

    def apply_script_asm(self, script_id: int, text: str) -> dict[str, Any]:
        """Replace one script from assembler text.

        ``emit(parse(text))`` must succeed. The ``script 0xNNNN`` header must
        match ``script_id``. Same bytes as the current IR is a no-op.
        """
        from field_script_asm import AsmError, parse_script_text

        if not isinstance(text, str):
            raise ValueError("assembler text must be a string")
        sc = self.get_script(script_id)
        try:
            parsed = parse_script_text(text, map_stem=self._sf.stem)
        except AsmError as exc:
            raise ValueError(str(exc)) from exc
        if parsed.script_id != script_id:
            raise ValueError(
                f"script header 0x{parsed.script_id:04X} does not match "
                f"open script 0x{script_id:04X}"
            )
        before = emit_script(sc)
        after = emit_script(parsed)
        if after == before:
            return {
                "unchanged": True,
                "beforeBytes": len(before),
                "afterBytes": len(after),
            }
        sc.ops = list(parsed.ops)
        self._dirty.add(script_id)
        return {
            "unchanged": False,
            "beforeBytes": len(before),
            "afterBytes": len(after),
        }

    def hook_asm_text(self, hook_session: Any) -> str:
        """Pretty assembler for this map's table-2 hook rows."""
        return hook_session.hook_asm_text()

    def apply_hook_asm(self, hook_session: Any, text: str) -> dict[str, Any]:
        """Replace table 2 from assembler text. Header stem must match."""
        return hook_session.apply_hook_asm(text)

    # ------------------------------------------------------------------
    # Emit
    # ------------------------------------------------------------------

    def emit(self) -> tuple[bytes, bytes]:
        """Assemble the full script file to (bank_bytes, directory_bytes)."""
        return emit_script_file(self._sf)

    def emit_single(self, script_id: int) -> bytes:
        """Assemble a single script to bytes (for preview / validation)."""
        sc = self.get_script(script_id)
        return emit_script(sc)

    def verify_roundtrip(self, script_id: int) -> bool:
        """Return True if emitting and re-parsing the script produces the same IR."""
        sc = self.get_script(script_id)
        from field_script_ir import parse_script_to_ir
        emitted = emit_script(sc)
        reparsed = parse_script_to_ir(emitted, script_id, 0, len(emitted))
        # Compare emitted representations.
        return emit_script(reparsed) == emitted


# ---------------------------------------------------------------------------
# Internal helper: rebuild token stream with new texts
# ---------------------------------------------------------------------------

def _rebuild_dialogue_tokens(
    original_tokens: list[DToken],
    new_page_texts: list[str],
) -> list[DToken]:
    """Rebuild a token stream with replacement text while preserving metadata.

    Strategy:
      - Split the original stream into Hold-delimited cue buckets (stacked
        portrait headers without text stay attached to the first spoken cue).
      - For each new page: reuse headers, holds, and face-keys; replace only
        the spoken TextToken / NewlineToken / PageBreakToken run.
      - If there are fewer original pages than new pages, clone the last bucket.
    """
    buckets = _hold_cue_buckets(original_tokens)

    # If no pages found at all, fall back to a minimal single-bucket.
    if not buckets:
        buckets = [[LineStartToken(), HoldToken(30), TerminatorToken()]]

    result: list[DToken] = []
    for page_idx, text in enumerate(new_page_texts):
        if page_idx < len(buckets):
            bucket = buckets[page_idx]
        else:
            bucket = list(buckets[-1])  # clone last bucket
        new_bucket = _replace_bucket_spoken_text(bucket, text)
        result.extend(new_bucket)

    # Ensure the token list ends with a TerminatorToken.
    if not result or not isinstance(result[-1], TerminatorToken):
        # Remove any stale terminators that ended up in the middle.
        result = [t for t in result if not isinstance(t, TerminatorToken)]
        result.append(TerminatorToken())

    return result


# Leading bytes the engine consumes before visible dialogue (must be preserved on emit).
# These are 1-based face ids encoded as ASCII (space .. '<'), not display punctuation.
_UI_PREFIX_MARKERS = set(PRINTABLE_FACE_EXTRA_CHARS)
# Visible-width budget per rendered line inside a Type-8 cue (same as markup boxes).
_UI_LINE_MAX_CHARS = BOX_LINE_MAX_CHARS
_UI_MAX_LINES = BOX_MAX_LINES


def _normalize_editor_text(text: str) -> str:
    """Normalize browser textarea content to internal ``\\n`` / ``\\p`` escapes."""
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    out: list[str] = []
    i = 0
    while i < len(text):
        if text[i] == "\n":
            out.append(r"\n")
            i += 1
        elif text[i : i + 2] == r"\n":
            out.append(r"\n")
            i += 2
        elif text[i : i + 2] == r"\p":
            out.append(r"\p")
            i += 2
        else:
            out.append(text[i])
            i += 1
    return "".join(out)


def _ui_display_text(original_tokens: list[DToken]) -> tuple[str, str]:
    """Return ``(display_text, prefix_byte)`` for a Type-8 token stream."""
    from field_script_ir import NewlineToken, PageBreakToken

    parts: list[str] = []
    prefix = ""
    first_text = True
    for tok in original_tokens:
        if isinstance(tok, TextToken):
            chunk = tok.text
            if first_text:
                first_text = False
                if chunk[:1] in _UI_PREFIX_MARKERS:
                    prefix = chunk[:1]
                    chunk = chunk[1:]
            parts.append(chunk)
        elif isinstance(tok, NewlineToken):
            parts.append(r"\n")
        elif isinstance(tok, PageBreakToken):
            parts.append(r"\p")
    return "".join(parts), prefix


def _ui_display_pages(original_tokens: list[DToken]) -> list[str]:
    """Return editable cue texts for a Type-8 op.

    Type-8 streams are cue-based: text is emitted up to a ``HoldToken``, then
    the engine may continue in-place, clear the box, or jump to another row
    before the next cue. Treating the whole stream as one freeform textbox loses
    that timing structure, so the editor exposes one chunk per visible cue.
    """
    cues: list[str] = []
    current: list[str] = []
    first_text = True

    def flush() -> None:
        nonlocal current
        text = "".join(current)
        if text.strip() and text.strip() != r"\p":
            cues.append(text)
        current = []

    for tok in original_tokens:
        if isinstance(tok, TextToken):
            chunk = tok.text
            if first_text:
                first_text = False
                if chunk[:1] in _UI_PREFIX_MARKERS:
                    chunk = chunk[1:]
            current.append(chunk)
        elif isinstance(tok, NewlineToken):
            current.append(r"\n")
        elif isinstance(tok, PageBreakToken):
            current.append(r"\p")
        elif isinstance(tok, HoldToken):
            flush()
        elif isinstance(tok, TerminatorToken):
            flush()
            break
    flush()
    return cues


def _split_ui_pages_and_lines(text: str) -> list[list[str]]:
    """Split ``\\p`` pages into ``\\n`` lines for Type-8 layout checks."""
    pages = text.split(r"\p")
    return [page.split(r"\n") for page in pages]


@dataclass(frozen=True)
class _UiCueMeta:
    hold: HoldToken
    face: FaceKeyToken | None = None


def _find_first_cue_text_start(tokens: list[DToken]) -> int:
    first_hold = next(i for i, tok in enumerate(tokens) if isinstance(tok, HoldToken))
    pos = first_hold - 1
    while pos >= 0 and isinstance(tokens[pos], (TextToken, NewlineToken, PageBreakToken)):
        pos -= 1
    return pos + 1


def _extract_ui_prefix_tokens(tokens: list[DToken]) -> list[DToken]:
    """Tokens before the first pause-driven visible cue."""
    return list(tokens[: _find_first_cue_text_start(tokens)])


def _extract_ui_cue_metas(tokens: list[DToken]) -> list[_UiCueMeta]:
    metas: list[_UiCueMeta] = []
    for i, tok in enumerate(tokens):
        if not isinstance(tok, HoldToken):
            continue
        face = tokens[i + 1] if i + 1 < len(tokens) and isinstance(tokens[i + 1], FaceKeyToken) else None
        metas.append(_UiCueMeta(hold=tok, face=face))
    return metas


def _emit_cue_text_tokens(text: str, *, prefix_char: str | None = None) -> list[DToken]:
    """Turn one editable cue string into ``Text``/``Newline``/``PageBreak`` tokens."""
    tokens: list[DToken] = []
    buf: list[str] = []
    first_text = True

    def flush_buf() -> None:
        nonlocal first_text
        if not buf:
            return
        chunk = "".join(buf)
        buf.clear()
        if first_text and prefix_char:
            chunk = prefix_char + chunk
            first_text = False
        elif first_text:
            first_text = False
        if chunk:
            tokens.append(TextToken(chunk))

    i = 0
    while i < len(text):
        if text[i : i + 2] == r"\n":
            flush_buf()
            tokens.append(NewlineToken())
            i += 2
            continue
        if text[i : i + 2] == r"\p":
            flush_buf()
            tokens.append(PageBreakToken())
            i += 2
            continue
        if text[i] == "\n":
            flush_buf()
            tokens.append(NewlineToken())
            i += 1
            continue
        buf.append(text[i])
        i += 1
    flush_buf()
    return tokens


def _map_edited_cues_to_metas(original_cues: list[str], edited_cues: list[str]) -> list[tuple[str, int]]:
    """Align edited cue texts to original cue indices for metadata reuse."""
    if len(edited_cues) < len(original_cues):
        if not edited_cues:
            raise ValueError("Type-8 op must keep at least one cue")
    elif len(edited_cues) == len(original_cues) and not edited_cues:
        return []

    mapped: list[tuple[str, int]] = []
    matcher = difflib.SequenceMatcher(a=original_cues, b=edited_cues)
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag == "delete":
            continue
        if tag in {"equal", "replace"}:
            orig_span = max(1, i2 - i1)
            for offset in range(j2 - j1):
                meta_idx = min(i1 + min(offset, orig_span - 1), len(original_cues) - 1)
                mapped.append((edited_cues[j1 + offset], meta_idx))
        elif tag == "insert":
            anchor = max(0, min(i1, len(original_cues) - 1))
            for offset in range(j2 - j1):
                mapped.append((edited_cues[j1 + offset], anchor))
    return mapped


def _ui_cue_overflow_score(text: str) -> int:
    score = 0
    for page_lines in _split_ui_pages_and_lines(text):
        if len(page_lines) > _UI_MAX_LINES:
            score += (len(page_lines) - _UI_MAX_LINES) * 10000
        for line in page_lines:
            if len(line) > _UI_LINE_MAX_CHARS:
                score += len(line) - _UI_LINE_MAX_CHARS
    return score


def _validate_single_ui_cue_change(orig_text: str, new_text: str, *, cue_num: int) -> None:
    """Validate one Type-8 cue edit against its previous text."""
    if new_text == orig_text:
        return
    if not new_text.strip():
        raise ValueError(f"Type-8 cue {cue_num} cannot be empty")
    if _ui_cue_overflow_score(new_text) <= _ui_cue_overflow_score(orig_text):
        return
    for subpage_idx, page_lines in enumerate(_split_ui_pages_and_lines(new_text), 1):
        if len(page_lines) > _UI_MAX_LINES:
            raise ValueError(
                f"Type-8 cue {cue_num} page {subpage_idx} has {len(page_lines)} lines "
                f"(max {_UI_MAX_LINES}). Insert a page break or shorten the cue."
            )
        for line_idx, line in enumerate(page_lines, 1):
            if len(line) > _UI_LINE_MAX_CHARS:
                raise ValueError(
                    f"Type-8 cue {cue_num} line {line_idx} is {len(line)} chars "
                    f"(max {_UI_LINE_MAX_CHARS}). Shorten the line or insert a new cue."
                )


def _split_ui_cue_slices(
    tokens: list[DToken],
) -> tuple[list[DToken], list[list[DToken]], list[_UiCueMeta], list[DToken]]:
    """Split a Type-8 token stream into prefix, cue bodies, hold/face metas, and trailing bytes."""
    prefix = _extract_ui_prefix_tokens(tokens)
    start = _find_first_cue_text_start(tokens)
    slices: list[list[DToken]] = []
    metas: list[_UiCueMeta] = []
    pos = start
    while pos < len(tokens):
        hold_index = next(
            (i for i in range(pos, len(tokens)) if isinstance(tokens[i], HoldToken)),
            -1,
        )
        if hold_index < 0:
            break
        slices.append(list(tokens[pos:hold_index]))
        hold = tokens[hold_index]
        face_index = hold_index + 1
        face = tokens[face_index] if face_index < len(tokens) and isinstance(tokens[face_index], FaceKeyToken) else None
        metas.append(_UiCueMeta(hold=hold, face=face))
        pos = hold_index + 1 + (1 if face is not None else 0)
    trailing: list[DToken] = []
    for tok in tokens[pos:]:
        if isinstance(tok, TerminatorToken):
            trailing.append(tok)
            break
        if isinstance(tok, RawByteToken):
            trailing.append(tok)
    return prefix, slices, metas, trailing or [TerminatorToken()]


def _first_cue_prefix_char(original_tokens: list[DToken]) -> str | None:
    """Return the engine prefix byte on the op's first visible cue, if any."""
    _, slices, _, _ = _split_ui_cue_slices(original_tokens)
    if not slices:
        return None
    for tok in slices[0]:
        if isinstance(tok, TextToken) and tok.text[:1] in _UI_PREFIX_MARKERS:
            return tok.text[:1]
    return None


def _cue_display_from_content_tokens(content: list[DToken], *, strip_leading_prefix: bool = False) -> str:
    """Rebuild the editable cue string represented by one cue body token slice."""
    parts: list[str] = []
    first_text = True
    for tok in content:
        if isinstance(tok, TextToken):
            chunk = tok.text
            if first_text and strip_leading_prefix and chunk[:1] in _UI_PREFIX_MARKERS:
                chunk = chunk[1:]
            first_text = False
            parts.append(chunk)
        elif isinstance(tok, NewlineToken):
            parts.append(r"\n")
        elif isinstance(tok, PageBreakToken):
            parts.append(r"\p")
    return "".join(parts)


def _append_ui_cue_body(
    out: list[DToken],
    *,
    content: list[DToken],
    text: str,
    meta: _UiCueMeta,
    prefix_char: str | None = None,
) -> None:
    """Append one cue body plus hold/face, preserving original tokens when unchanged."""
    current = _cue_display_from_content_tokens(content, strip_leading_prefix=prefix_char is not None)
    if content and text == current:
        out.extend(content)
    else:
        i = 0
        while i < len(content) and not isinstance(content[i], (TextToken, NewlineToken, PageBreakToken)):
            i += 1
        out.extend(content[:i])
        use_prefix = prefix_char if i == 0 else None
        out.extend(_emit_cue_text_tokens(text, prefix_char=use_prefix))
    out.append(meta.hold)
    if meta.face is not None:
        out.append(meta.face)


def _rebuild_ui_cue_list_surgical(
    original_tokens: list[DToken],
    edited_cues: list[str],
) -> list[DToken]:
    """Insert/remove/edit cues while preserving untouched cue token slices."""
    orig_cues = _ui_display_pages(original_tokens)
    prefix, slices, metas, trailing = _split_ui_cue_slices(original_tokens)
    if not metas:
        metas = [_UiCueMeta(hold=HoldToken(30))]

    new_tokens: list[DToken] = list(prefix)
    first_prefix = _first_cue_prefix_char(original_tokens)
    matcher = difflib.SequenceMatcher(a=orig_cues, b=edited_cues)
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag == "delete":
            continue
        if tag == "equal":
            for offset in range(i2 - i1):
                orig_i = i1 + offset
                edited_j = j1 + offset
                _append_ui_cue_body(
                    new_tokens,
                    content=slices[orig_i],
                    text=edited_cues[edited_j],
                    meta=metas[orig_i],
                    prefix_char=first_prefix if orig_i == 0 else None,
                )
        elif tag == "replace":
            span = max(i2 - i1, j2 - j1)
            for offset in range(span):
                orig_i = min(i1 + offset, i2 - 1) if i2 > i1 else min(i1, len(slices) - 1)
                edited_j = j1 + offset
                if edited_j >= j2:
                    break
                _append_ui_cue_body(
                    new_tokens,
                    content=slices[orig_i],
                    text=edited_cues[edited_j],
                    meta=metas[orig_i],
                    prefix_char=first_prefix if orig_i == 0 else None,
                )
        elif tag == "insert":
            anchor = min(i1, len(metas) - 1)
            for offset in range(j2 - j1):
                _append_ui_cue_body(
                    new_tokens,
                    content=[],
                    text=edited_cues[j1 + offset],
                    meta=metas[anchor],
                    prefix_char=None,
                )
    new_tokens.extend(trailing)
    return new_tokens


def _validate_ui_layout_edit(original_tokens: list[DToken], page_texts: list[str]) -> None:
    """Reject Type-8 edits that are likely to corrupt packed multi-line layouts."""
    original_cues = _ui_display_pages(original_tokens)
    edited_cues = [_normalize_editor_text(text) for text in page_texts]
    if edited_cues == original_cues:
        return

    if len(edited_cues) < len(original_cues):
        if not edited_cues:
            raise ValueError("Type-8 op must keep at least one cue")
        return

    matcher = difflib.SequenceMatcher(a=original_cues, b=edited_cues)
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag == "insert":
            for offset in range(j2 - j1):
                if not edited_cues[j1 + offset].strip():
                    raise ValueError(f"Type-8 cue {j1 + offset + 1} cannot be empty")
            continue
        if tag not in {"equal", "replace"}:
            continue
        for offset in range(min(i2 - i1, j2 - j1)):
            orig_text = original_cues[i1 + offset]
            new_text = edited_cues[j1 + offset]
            _validate_single_ui_cue_change(orig_text, new_text, cue_num=i1 + offset + 1)


def _dialogue_text_pages(original_tokens: list[DToken]) -> list[str]:
    """Extract spoken cue texts from a dialogue token stream for no-op detection."""
    if not any(isinstance(tok, HeaderToken) for tok in original_tokens):
        return _ui_display_pages(original_tokens)
    pages: list[str] = []
    for bucket in _hold_cue_buckets(original_tokens):
        text = _bucket_spoken_text(bucket)
        has_hold = any(isinstance(tok, HoldToken) for tok in bucket)
        body = text.replace(r"\n", "").replace(r"\p", "").strip()
        if body or has_hold:
            pages.append(text)
    return pages


def _split_edited_by_delimiters(edited: str, delimiters: list[str]) -> list[str] | None:
    """Split *edited* on the delimiter sequence taken from the original stream."""
    if not delimiters:
        return [edited]
    chunks: list[str] = []
    rest = edited
    for delim in delimiters:
        pos = rest.find(delim)
        if pos == -1 and delim == r"\n":
            pos = rest.find("\n")
            if pos != -1:
                chunks.append(rest[:pos])
                rest = rest[pos + 1 :]
                continue
        if pos == -1:
            return None
        chunks.append(rest[:pos])
        rest = rest[pos + len(delim) :]
    chunks.append(rest)
    return chunks


def _proportional_split(text: str, weights: list[int]) -> list[str]:
    if not weights:
        return []
    if len(text) == 0:
        return [""] * len(weights)
    total = sum(weights) or len(weights)
    parts: list[str] = []
    pos = 0
    for i, weight in enumerate(weights):
        if i == len(weights) - 1:
            parts.append(text[pos:])
            break
        share = max(0, round(len(text) * weight / total))
        parts.append(text[pos : pos + share])
        pos += share
    return parts


def _split_group_text(gtext: str, original_parts: list[str]) -> list[str]:
    """Map edited group text back onto the original text-token boundaries."""
    orig = "".join(original_parts)
    if gtext == orig:
        return list(original_parts)
    if len(original_parts) == 1:
        return [gtext]
    if gtext.startswith(orig):
        result = list(original_parts)
        result[-1] = result[-1] + gtext[len(orig) :]
        return result
    if gtext.endswith(orig):
        extra = gtext[: len(gtext) - len(orig)]
        result = list(original_parts)
        result[0] = extra + result[0]
        return result
    return _proportional_split(gtext, [len(part) for part in original_parts])


def _rebuild_ui_dialogue_tokens_inplace(
    original_tokens: list[DToken],
    edited_cues: list[str],
) -> list[DToken]:
    """Update cue text in-place when cue count is unchanged."""
    first_text = True
    first_text_index: int | None = None
    cue_groups: list[list[tuple[str, object]]] = [[]]
    for i, tok in enumerate(original_tokens):
        if isinstance(tok, TextToken):
            chunk = tok.text
            if first_text:
                first_text = False
                first_text_index = i
                if chunk[:1] in _UI_PREFIX_MARKERS:
                    chunk = chunk[1:]
            cue_groups[-1].append(("text", (i, chunk)))
        elif isinstance(tok, NewlineToken):
            cue_groups[-1].append(("delim", r"\n"))
        elif isinstance(tok, PageBreakToken):
            cue_groups[-1].append(("delim", r"\p"))
        elif isinstance(tok, HoldToken):
            cue_groups.append([])

    if cue_groups and not cue_groups[-1]:
        cue_groups.pop()
    if len(cue_groups) != len(edited_cues):
        raise ValueError(
            f"Type-8 edit changed cue count {len(cue_groups)} -> {len(edited_cues)}. "
            "Use ---PAGE--- to add new pause segments."
        )

    new_tokens = list(original_tokens)
    prefix = ""
    if first_text_index is not None:
        tok = original_tokens[first_text_index]
        if isinstance(tok, TextToken) and tok.text[:1] in _UI_PREFIX_MARKERS:
            prefix = tok.text[:1]
    for group_segments, gtext in zip(cue_groups, edited_cues):
        group: list[tuple[int, str]] = []
        delimiters: list[str] = []
        for kind, payload in group_segments:
            if kind == "text":
                idx, chunk = payload  # type: ignore[misc]
                group.append((idx, chunk))
            else:
                delimiters.append(str(payload))
        if not group:
            continue
        group_texts = _split_edited_by_delimiters(gtext, delimiters)
        if group_texts is None or len(group_texts) != len(group):
            group_texts = _proportional_split(gtext, [len(chunk) for _idx, chunk in group])
        parts = [chunk for _idx, chunk in group]
        if len(group_texts) == len(group):
            mapped = group_texts
        else:
            mapped = _split_group_text(gtext, parts)
        for (ti, _orig), chunk in zip(group, mapped):
            if ti == first_text_index and prefix:
                new_tokens[ti] = TextToken(prefix + chunk)
            else:
                new_tokens[ti] = TextToken(chunk)
    return new_tokens


def _rebuild_ui_dialogue_tokens(original_tokens: list[DToken], page_texts: list[str]) -> list[DToken]:
    """Replace Type-8 dialogue text while preserving pause/timing metadata."""
    edited_cues = [_normalize_editor_text(text) for text in page_texts]
    orig_cues = _ui_display_pages(original_tokens)
    if edited_cues == orig_cues:
        return list(original_tokens)
    return _rebuild_ui_cue_list_surgical(original_tokens, edited_cues)


def _clamp_hold(value: Any, default: int = 30) -> int:
    try:
        n = int(value)
    except (TypeError, ValueError):
        n = default
    return max(0, min(255, n))


def _block_hold_value(block: dict[str, Any], *, type8: bool) -> int | None:
    """Return frames, or None to drop the HoldToken (type-1 confirm only)."""
    adv = str(block.get("advance") or "").lower()
    if adv in {"confirm", "select"} and not type8:
        return None
    if block.get("hold") is not None:
        n = _clamp_hold(block["hold"])
        if n == 0 and not type8:
            return None
        return n
    if adv == "auto":
        return 30
    if type8:
        return 30
    return None


def _replace_extra_after_header(tokens: list[DToken], header_i: int, extra: int) -> None:
    """Write a 1-based face extra immediately after a HeaderToken."""
    j = header_i + 1
    while j < len(tokens) and isinstance(tokens[j], RawByteToken) and tokens[j].value in {0x00, 0x02, 0x06}:
        j += 1
    if j < len(tokens) and isinstance(tokens[j], RawByteToken):
        tokens[j] = RawByteToken(extra)
        return
    if j < len(tokens) and isinstance(tokens[j], TextToken) and tokens[j].text:
        rest = tokens[j].text
        if rest[:1] in _UI_PREFIX_MARKERS:
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
    tokens.insert(header_i + 1, RawByteToken(extra))


def _header_buckets(tokens: list[DToken]) -> list[list[DToken]]:
    """Split a type-1 stream on HeaderToken (one bucket per portrait header)."""
    buckets: list[list[DToken]] = []
    current: list[DToken] = []
    for tok in tokens:
        if isinstance(tok, HeaderToken) and current:
            buckets.append(current)
            current = [tok]
        elif isinstance(tok, HeaderToken):
            current = [tok]
        else:
            current.append(tok)
    if current:
        buckets.append(current)
    return buckets


def _hold_cue_buckets(tokens: list[DToken]) -> list[list[DToken]]:
    """Split a dialogue stream into Hold / ``\\p`` cue buckets.

    FaceKey immediately after a Hold stays on that cue. A Hold with no spoken
    text is a pause-only bucket. ``PageBreakToken`` after spoken text ends that
    cue; a bare ``\\p`` after a Hold is a leading box-clear on the next cue.
    A Header after spoken text (e.g. ``but`` then ``\\n`` plus a new face)
    starts a new cue. Trailing newlines before that Header stay on the next
    cue as a leading line break. A stray newline or bare ``\\p`` before a
    Header with no prior spoken body stays on the next cue. Stacked portrait
    headers before the first spoken run stay on the first cue. Streams with
    neither Hold nor page-break keep header grouping.
    """
    if not any(isinstance(tok, (HoldToken, PageBreakToken)) for tok in tokens):
        return _merge_portrait_stack_buckets(_header_buckets(tokens))

    buckets: list[list[DToken]] = []
    start = 0
    i = 0
    n = len(tokens)
    while i < n:
        tok = tokens[i]
        if isinstance(tok, HeaderToken) and i > start:
            if _bucket_has_spoken_text(tokens[start:i]):
                end = i
                while end > start and isinstance(tokens[end - 1], NewlineToken):
                    end -= 1
                if end > start:
                    buckets.append(list(tokens[start:end]))
                    start = end
                    continue
        if isinstance(tok, HoldToken):
            end = i + 1
            if end < n and isinstance(tokens[end], FaceKeyToken):
                end += 1
            buckets.append(list(tokens[start:end]))
            start = end
            i = end
            continue
        if isinstance(tok, PageBreakToken):
            if start < i:
                end = i + 1
                if end < n and isinstance(tokens[end], HoldToken):
                    end += 1
                    if end < n and isinstance(tokens[end], FaceKeyToken):
                        end += 1
                buckets.append(list(tokens[start:end]))
                start = end
                i = end
            else:
                # Bare ``\p`` after a Hold stays on the next cue as a leading clear.
                i += 1
            continue
        i += 1
    tail = list(tokens[start:])
    term: list[DToken] = []
    while tail and isinstance(tail[-1], TerminatorToken):
        term.insert(0, tail.pop())
    if tail:
        spoken = any(
            isinstance(tok, (TextToken, NewlineToken, PageBreakToken, HeaderToken, HoldToken))
            for tok in tail
        )
        if spoken or not buckets:
            buckets.append(tail)
        else:
            buckets[-1].extend(tail)
    if term:
        if buckets:
            buckets[-1].extend(term)
        else:
            buckets.append(term)
    return [b for b in buckets if b]


def _bucket_has_spoken_text(bucket: list[DToken]) -> bool:
    """True if this slice prints dialogue, not just extras / a box-clear."""
    text = _bucket_spoken_text(bucket)
    return bool(text.replace(r"\n", "").replace(r"\p", "").strip())


def _merge_portrait_stack_buckets(buckets: list[list[DToken]]) -> list[list[DToken]]:
    """Join portrait-only headers onto the next spoken box.

    Type-1 can stack several 5-byte headers (different faces/slots) before one
    HoldToken. Those are one in-game textbox, not separate editor blocks.
    """
    merged: list[list[DToken]] = []
    pending: list[DToken] = []
    for i, bucket in enumerate(buckets):
        if _bucket_has_spoken_text(bucket) or i == len(buckets) - 1:
            merged.append(pending + bucket)
            pending = []
        else:
            pending.extend(bucket)
    if pending:
        if merged:
            merged[-1].extend(pending)
        else:
            merged.append(pending)
    return merged


def _pre_header_line_markup(bucket: list[DToken], last_h: int) -> str:
    """``\\p`` / ``\\n`` tokens immediately before the last header."""
    if last_h <= 0:
        return ""
    marks: list[str] = []
    j = last_h - 1
    while j >= 0 and isinstance(bucket[j], (NewlineToken, PageBreakToken)):
        marks.append(r"\n" if isinstance(bucket[j], NewlineToken) else r"\p")
        j -= 1
    marks.reverse()
    return "".join(marks)


def _split_leading_nl_p(text: str) -> tuple[str, str]:
    """Split leading box-clear / line-break markup from editor text."""
    rest = text or ""
    lead: list[str] = []
    while True:
        if rest.startswith(r"\p"):
            lead.append(r"\p")
            rest = rest[2:]
            continue
        if rest.startswith(r"\n"):
            lead.append(r"\n")
            rest = rest[2:]
            continue
        if rest.startswith("\n"):
            lead.append(r"\n")
            rest = rest[1:]
            continue
        break
    return "".join(lead), rest


def _bucket_spoken_text(bucket: list[DToken]) -> str:
    """Visible editor text for one spoken type-1 box, extras stripped."""
    last_h = max((i for i, tok in enumerate(bucket) if isinstance(tok, HeaderToken)), default=-1)
    parts: list[str] = []
    leading_markup = _pre_header_line_markup(bucket, last_h)
    if leading_markup:
        parts.append(leading_markup)
    leading_clear = r"\p" in leading_markup
    started = False
    for tok in bucket[last_h + 1 :]:
        if isinstance(tok, RawByteToken):
            continue
        if isinstance(tok, TextToken):
            chunk = tok.text
            if (
                last_h >= 0
                and not started
                and not leading_clear
                and chunk[:1]
                and is_printable_face_extra(ord(chunk[0]))
            ):
                chunk = chunk[1:]
            parts.append(chunk)
            started = True
            continue
        if isinstance(tok, NewlineToken):
            parts.append(r"\n")
            started = True
            continue
        if isinstance(tok, PageBreakToken):
            parts.append(r"\p")
            started = True
            continue
        if isinstance(tok, (HoldToken, FaceKeyToken, HeaderToken, TerminatorToken, LineStartToken)):
            if started:
                break
            continue
        # Mid-line metadata (Raw 0xD7, camera 09-xx, etc.) is not a text end.
    return "".join(parts)


def _replace_bucket_spoken_text(bucket: list[DToken], text: str) -> list[DToken]:
    """Replace the spoken run after the last stacked header, keep extras."""
    if not (text or "").strip() and any(isinstance(tok, HoldToken) for tok in bucket):
        return [
            tok for tok in bucket
            if not isinstance(tok, (TextToken, NewlineToken, PageBreakToken))
        ]
    last_h = max((i for i, tok in enumerate(bucket) if isinstance(tok, HeaderToken)), default=-1)
    i = last_h + 1
    while i < len(bucket) and isinstance(bucket[i], RawByteToken):
        i += 1
    if last_h < 0:
        new_bucket: list[DToken] = []
        text_emitted = False
        for tok in bucket:
            if isinstance(tok, (TextToken, NewlineToken, PageBreakToken)):
                if not text_emitted:
                    new_bucket.extend(_emit_cue_text_tokens(text))
                    text_emitted = True
                continue
            new_bucket.append(tok)
        if not text_emitted:
            new_bucket.extend(_emit_cue_text_tokens(text))
        return new_bucket
    lead_markup, emit_text = _split_leading_nl_p(text)
    leading_clear = r"\p" in lead_markup
    pre = list(bucket[:last_h])
    while pre and isinstance(pre[-1], (NewlineToken, PageBreakToken)):
        pre.pop()
    pre.extend(_emit_cue_text_tokens(lead_markup))
    prefix = ""
    first_text = next((tok for tok in bucket[i:] if isinstance(tok, TextToken)), None)
    if (
        not leading_clear
        and first_text is not None
        and first_text.text[:1]
        and is_printable_face_extra(ord(first_text.text[0]))
    ):
        prefix = first_text.text[:1]
    new_mid: list[DToken] = []
    text_emitted = False
    rest_start = len(bucket)
    for k in range(i, len(bucket)):
        tok = bucket[k]
        if isinstance(tok, (HoldToken, HeaderToken, TerminatorToken)):
            rest_start = k
            break
        if isinstance(tok, (TextToken, NewlineToken, PageBreakToken)):
            if not text_emitted:
                new_mid.extend(_emit_cue_text_tokens(prefix + emit_text))
                text_emitted = True
            continue
        new_mid.append(tok)
    if not text_emitted:
        new_mid.extend(_emit_cue_text_tokens(prefix + emit_text))
    return pre + list(bucket[last_h:i]) + new_mid + bucket[rest_start:]


def _type1_header_groups(tokens: list[DToken]) -> list[list[int]]:
    """Header-token index groups, one group per Hold-cue editor block."""
    groups: list[list[int]] = []
    offset = 0
    for bucket in _hold_cue_buckets(tokens):
        groups.append(
            [offset + i for i, tok in enumerate(bucket) if isinstance(tok, HeaderToken)]
        )
        offset += len(bucket)
    return groups


def _apply_hold_to_bucket(bucket: list[DToken], val: int | None) -> list[DToken]:
    """Set or drop the HoldToken in one type-1 page bucket."""
    out = [tok for tok in bucket if not isinstance(tok, HoldToken)]
    if val is None:
        return out
    hold = HoldToken(hold=val)
    last_text = -1
    for i, tok in enumerate(out):
        if isinstance(tok, (LineStartToken, TextToken, NewlineToken, PageBreakToken)):
            last_text = i
    if last_text >= 0:
        out.insert(last_text + 1, hold)
        return out
    term = next((i for i, tok in enumerate(out) if isinstance(tok, TerminatorToken)), len(out))
    out.insert(term, hold)
    return out


def _cue_body_ranges(tokens: list[DToken]) -> list[tuple[int, int]]:
    """Return ``(body_start, hold_index)`` for each HoldToken-delimited cue."""
    holds = [i for i, tok in enumerate(tokens) if isinstance(tok, HoldToken)]
    ranges: list[tuple[int, int]] = []
    for hold_i in holds:
        pos = hold_i - 1
        while pos >= 0 and isinstance(tokens[pos], (TextToken, NewlineToken, PageBreakToken)):
            pos -= 1
        ranges.append((pos + 1, hold_i))
    return ranges


def _clone_dialog_header(src: HeaderToken | None, b1: int | None) -> HeaderToken:
    if src is None:
        return HeaderToken(pre=0x0F, b1=b1 or 0x03, b2=0x0A, b3=0x0C, expr=0)
    return HeaderToken(pre=src.pre, b1=b1 if b1 is not None else src.b1, b2=src.b2, b3=src.b3, expr=src.expr)


def _read_extra_after_header(tokens: list[DToken], header_i: int, until: int) -> int | None:
    j = header_i + 1
    while j < until and isinstance(tokens[j], RawByteToken) and tokens[j].value in {0x00, 0x02, 0x06}:
        j += 1
    if j < until and isinstance(tokens[j], RawByteToken):
        return tokens[j].value
    if j < until and isinstance(tokens[j], TextToken) and tokens[j].text[:1] in _UI_PREFIX_MARKERS:
        return ord(tokens[j].text[0])
    return None


def _patch_type8_block_faces(
    tokens: list[DToken],
    extras: list[int | None],
    b1s: list[int | None],
) -> list[DToken]:
    """Write per-cue faces. Shared headers stay shared until a later cue wants a different extra."""
    ranges = _cue_body_ranges(tokens)
    if not ranges:
        extra0 = extras[0] if extras else None
        if extra0 is None:
            return tokens
        hdr = _clone_dialog_header(None, b1s[0] if b1s else None)
        tokens.insert(0, hdr)
        _replace_extra_after_header(tokens, 0, extra0)
        return tokens

    actions: list[tuple] = []
    sticky: int | None = None
    last_header: HeaderToken | None = None
    for cue_i, (start, _hold_i) in enumerate(ranges):
        prev = 0 if cue_i == 0 else ranges[cue_i - 1][1] + 1
        header_before_body: int | None = None
        for k in range(prev, start):
            tok = tokens[k]
            if isinstance(tok, HeaderToken):
                last_header = tok
                sticky = _read_extra_after_header(tokens, k, start)
                header_before_body = k
            elif (
                isinstance(tok, TextToken)
                and sticky is None
                and tok.text[:1] in _UI_PREFIX_MARKERS
            ):
                sticky = ord(tok.text[0])
        desired = extras[cue_i] if cue_i < len(extras) else None
        b1 = b1s[cue_i] if cue_i < len(b1s) else None
        if desired is None:
            continue
        if desired == sticky and (b1 is None or last_header is None or b1 == last_header.b1):
            continue
        if header_before_body is not None:
            actions.append(("replace", header_before_body, desired, b1))
        else:
            actions.append(("insert", start, desired, b1, last_header))
        sticky = desired
        if b1 is not None:
            last_header = _clone_dialog_header(last_header, b1)

    for action in reversed(actions):
        if action[0] == "replace":
            _, header_i, extra, b1 = action
            tok = tokens[header_i]
            if isinstance(tok, HeaderToken):
                tokens[header_i] = _clone_dialog_header(tok, b1)
            _replace_extra_after_header(tokens, header_i, extra)
        else:
            _, start, extra, b1, src_hdr = action
            hdr = _clone_dialog_header(src_hdr, b1)
            tokens.insert(start, hdr)
            _replace_extra_after_header(tokens, start, extra)
    return tokens


def _block_face_extra(
    face: dict[str, Any],
    *,
    map_stem: str | None,
    field_root: Path | None,
) -> int | None:
    expr = face.get("expr")
    if expr is None:
        return None
    fc_row = face.get("fcRow")
    return packed_face_index_to_extra(
        int(expr),
        fc_id=None if fc_row is None else int(fc_row),
        map_stem=map_stem,
        b1_slot=None if face.get("b1") is None else int(face["b1"]),
        field_root=field_root,
    )


def _patch_dialog_faces(
    tokens: list[DToken],
    blocks: list[dict[str, Any]],
    *,
    type8: bool,
    map_stem: str | None = None,
    field_root: Path | None = None,
) -> list[DToken]:
    """Apply expr/b1 from blocks onto headers. Shared headers stay shared unless expr changes."""
    extras: list[int | None] = []
    b1s: list[int | None] = []
    for block in blocks:
        speaking = block
        faces = block.get("portraits")
        if isinstance(faces, list) and faces:
            speaking = faces[-1]
        extras.append(_block_face_extra(speaking, map_stem=map_stem, field_root=field_root))
        b1 = block.get("b1")
        if b1 is None:
            b1 = speaking.get("b1")
        b1s.append(None if b1 is None else int(b1))

    if type8:
        return _patch_type8_block_faces(tokens, extras, b1s)

    groups = _type1_header_groups(tokens)
    if not groups:
        extra0 = extras[0] if extras else None
        b10 = b1s[0] if b1s else None
        if extra0 is None:
            return tokens
        hdr = HeaderToken(pre=0x0F, b1=b10 or 0x03, b2=0x0A, b3=0x0C, expr=0)
        tokens.insert(0, hdr)
        _replace_extra_after_header(tokens, 0, extra0)
        return tokens

    for bi, block in enumerate(blocks):
        if bi >= len(groups):
            break
        header_idxs = groups[bi]
        faces = block.get("portraits")
        if not isinstance(faces, list) or not faces:
            faces = [{
                "expr": block.get("expr"),
                "b1": block.get("b1"),
                "fcRow": block.get("fcRow") if block.get("fcRow") is not None else (block.get("portrait") or {}).get("fcRow"),
            }]
        if len(faces) == 1 and len(header_idxs) > 1:
            pairs = [(header_idxs[-1], faces[0])]
        else:
            pairs = list(zip(header_idxs, faces))
        for hpos, face in pairs:
            tok = tokens[hpos]
            if not isinstance(tok, HeaderToken):
                continue
            b1 = face.get("b1")
            if b1 is None:
                b1 = tok.b1
            tokens[hpos] = HeaderToken(pre=tok.pre, b1=int(b1), b2=tok.b2, b3=tok.b3, expr=tok.expr)
            extra = _block_face_extra(face, map_stem=map_stem, field_root=field_root)
            if extra is not None:
                _replace_extra_after_header(tokens, hpos, extra)
    return tokens


def _patch_dialog_block_metadata(
    tokens: list[DToken],
    blocks: list[dict[str, Any]],
    type8: bool,
    *,
    map_stem: str | None = None,
    field_root: Path | None = None,
) -> list[DToken]:
    if type8:
        out: list[DToken] = []
        hold_n = 0
        for tok in tokens:
            if isinstance(tok, HoldToken):
                block = blocks[hold_n] if hold_n < len(blocks) else None
                hold_n += 1
                if block is None:
                    out.append(tok)
                    continue
                val = _block_hold_value(block, type8=True)
                out.append(HoldToken(hold=30 if val is None else val))
                continue
            out.append(tok)
        return _patch_dialog_faces(
            out, blocks, type8=True, map_stem=map_stem, field_root=field_root
        )

    buckets = _hold_cue_buckets(tokens)
    if not buckets:
        return _patch_dialog_faces(
            list(tokens), blocks, type8=False, map_stem=map_stem, field_root=field_root
        )
    rebuilt: list[DToken] = []
    for i, bucket in enumerate(buckets):
        block = blocks[i] if i < len(blocks) else None
        if block is None:
            rebuilt.extend(bucket)
            continue
        rebuilt.extend(_apply_hold_to_bucket(bucket, _block_hold_value(block, type8=False)))
    return _patch_dialog_faces(
        rebuilt, blocks, type8=False, map_stem=map_stem, field_root=field_root
    )


# ---------------------------------------------------------------------------
# CLI: quick test
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    import sys
    from pathlib import Path
    sys.path.insert(0, str(Path(__file__).parent))
    from field_script_disasm import DEFAULT_CONTENT, DEFAULT_TEXT

    sess = EditorSession.open("2000", DEFAULT_CONTENT, DEFAULT_TEXT)
    sc = sess.get_script(0x0000)
    pairs = sess.list_dialogue_ops(0x0000)
    print(f"Script 0x0000 has {len(pairs)} dialogue ops.")

    if pairs:
        idx, op = pairs[0]
        print(f"First dialogue op at list index {idx}, source_offset={getattr(op, 'source_offset', '?')}")

    # Verify round-trip still holds after open (no mutations yet).
    new_bank, new_dir = sess.emit()
    if new_bank == sess.script_file.original_bank and new_dir == sess.script_file.original_directory:
        print("Round-trip OK: no mutations, byte-identical output.")
    else:
        print("Round-trip FAIL: unexpected difference before any mutations.")
