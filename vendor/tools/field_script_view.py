#!/usr/bin/env python3
"""
field_script_view.py  —  Human-readable narrative viewer for Grandia HD field scripts.

Usage:
    python tools/field_script_view.py <map_stem> [--id 0x<hex>] [--all] [--text <dir>]

Examples:
    python tools/field_script_view.py 2000 --id 0x0000
    python tools/field_script_view.py 2000 --all

Output structure per scene-block:
    ╔═ SECTION  [requires flag 0x0001] [skips if flag 0x070B]
    ║  SCENE 0x27  actors=[159, 68, 83]
    ║  [npc]  "Oh, Justin, not you again!"
    ║  [hero] "I didn't trash it!"
    ║  SET flag 0x070B
    ╚═
"""
from __future__ import annotations

import argparse
import struct
import sys
from dataclasses import dataclass, field
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from portrait_resolver import expr_kind, portrait_label
from field_script_disasm import (
    DEFAULT_CONTENT,
    DEFAULT_TEXT,
    ScriptEntry,
    T1_B1_NAMES,
    Op,
    disasm_script,
    load_map,
    load_text_map,
    parse_t1_cues,
    parse_t1_header,
    parse_t1_pages,
    parse_t8_cues,
    parse_t8_lines,
)

# ---------------------------------------------------------------------------
# Known actor ID → character name (partial — filled in from RE so far)
# Actor IDs that appear consistently across maps and match dialogue context.
# ---------------------------------------------------------------------------
ACTOR_NAMES: dict[int, str] = {
    # Party
    1:   "Justin",
    2:   "Sue",
    3:   "Feena",
    4:   "Rapp",
    5:   "Mulu",
    # Recurring NPCs (tentative — inferred from scene context)
    83:  "shed_owner",    # placed alone in scene with "This'll teach ya punk!"
    68:  "crowd_npc",
    # High-frequency cross-map ids (tentative)
    54:  "npc_54",
    55:  "npc_55",
    24:  "npc_24",
    50:  "npc_50",
}


# ---------------------------------------------------------------------------
# Data model
# ---------------------------------------------------------------------------

@dataclass
class FlagGate:
    """A conditional guard at the top of a section."""
    check_flag: int | None = None       # flag being tested
    skip_if_set: int | None = None      # jump past section if this flag is set
    skip_if_clear: int | None = None    # jump past section if this flag is clear
    jump_bytes: int | None = None       # unconditional jump size (for context)


@dataclass
class SpeechLine:
    """One displayed line / page of dialogue."""
    slot: str           # "hero", "npc", "item_notify", "bubble" (type-8)
    text: str           # the text, with \n embedded
    op_offset: int      # source op offset (for patch reference)
    page_idx: int = 0   # page index within this op (0-based)
    context_actor_id: int | None = None   # raw nearby place_actor context; not a proven speaker binding
    expr: int | None = None
    b1_slot: int | None = None  # raw b1 byte from Type-1 header (for portrait lookup)
    hold: int | None = None
    face_key: int | None = None
    cue_idx: int | None = None
    pre_controls: list[int] | None = None
    header_stack: list[tuple[int, int, int]] | None = None


@dataclass
class SceneBlock:
    """A logical scene: one camera activation bracket + everything inside."""
    scene_id: int | None        # camera scene id (None = no camera change)
    actors: list[int]           # actor_ids placed before scene activation
    lines: list[SpeechLine]     # dialogue lines in order
    flags_set: list[int]        # flags set during this block
    flags_cleared: list[int]    # flags cleared during this block
    items_given: list[int]      # item_ids given (give_item)
    has_yield: bool = False     # whether a yield follows this block


@dataclass
class Section:
    """A flag-gated story section containing one or more scene blocks."""
    gate: FlagGate
    blocks: list[SceneBlock]
    ip_start: int   # byte offset of first op in this section


# ---------------------------------------------------------------------------
# Lifter: Op list → Section/SceneBlock hierarchy
# ---------------------------------------------------------------------------

def _actor_label(aid: int) -> str:
    name = ACTOR_NAMES.get(aid)
    return f"{aid}({name})" if name else str(aid)


def lift_script(ops: list[Op]) -> list[Section]:
    """
    Convert a flat op list into a list of Sections, each containing SceneBlocks.

    Algorithm:
      - flag_ctx_begin ... flag_ctx_end = one FlagGate
      - camera_cmd activate ... deactivate = one SceneBlock
      - Between camera brackets, dialogue and flag ops are attached to current block
      - yield closes the current scene block
    """
    sections: list[Section] = []

    cur_gate = FlagGate()
    cur_blocks: list[SceneBlock] = []
    cur_block: SceneBlock | None = None
    section_ip_start: int = ops[0].offset if ops else 0

    # Pending actors (placed before camera activation)
    pending_actors: list[int] = []
    # Pending scene id from the next camera activation
    pending_scene: int | None = None

    in_flag_ctx = False
    gate_building = FlagGate()
    # Raw context only: keep the most recent place_actor id around for RE notes,
    # but do not treat it as a proven "speaker" binding.
    last_placed_actor_id: int | None = None

    def flush_section() -> None:
        nonlocal cur_gate, cur_blocks, section_ip_start, pending_actors, pending_scene
        if cur_blocks or cur_gate.check_flag is not None:
            sections.append(Section(gate=cur_gate, blocks=cur_blocks, ip_start=section_ip_start))
        cur_gate = FlagGate()
        cur_blocks = []
        pending_actors = []
        pending_scene = None
        if ops:
            section_ip_start = ops[-1].offset

    def ensure_block() -> SceneBlock:
        nonlocal cur_block
        if cur_block is None:
            cur_block = SceneBlock(
                scene_id=pending_scene,
                actors=list(pending_actors),
                lines=[],
                flags_set=[],
                flags_cleared=[],
                items_given=[],
            )
            cur_blocks.append(cur_block)
            pending_actors.clear()
        return cur_block

    for op in ops:
        t = op.type_idx

        # --- Flag context ---
        if t == 0:
            lo12 = op.word & 0x0FFF
            if lo12 == 1:  # flag_ctx_begin
                in_flag_ctx = True
                gate_building = FlagGate()
            else:          # flag_ctx_end
                in_flag_ctx = False
                cur_gate = gate_building
            continue

        if t == 3 and in_flag_ctx:
            lo12 = op.word & 0x0FFF
            arg = struct.unpack_from("<H", op.payload)[0] if op.payload else 0
            if lo12 == 0x000:
                gate_building.check_flag = arg
            elif lo12 == 0x001:
                gate_building.skip_if_clear = arg
            elif lo12 in (0x040, 0x041):
                gate_building.skip_if_set = arg
            continue

        if t == 2:  # unconditional jump (section skip)
            lo12 = op.word & 0x0FFF
            cur_gate.jump_bytes = lo12
            continue

        # --- Flag set/clear ---
        if t == 4:
            flag_id = struct.unpack_from("<H", op.payload)[0] if op.payload else 0
            mode = op.word & 0xF
            blk = ensure_block()
            if mode:
                blk.flags_set.append(flag_id)
            else:
                blk.flags_cleared.append(flag_id)
            continue

        # --- Actor placement ---
        if t == 6 and len(op.payload) >= 4:
            pw = struct.unpack_from("<H", op.payload)[0]
            sub = pw & 0x3FFF
            if sub == 0x001A:
                aid = struct.unpack_from("<H", op.payload, 2)[0] & 0x7FFF
                pending_actors.append(aid)
                last_placed_actor_id = aid
                continue
            if sub == 0x0016 and len(op.payload) >= 4:
                a = struct.unpack_from("<H", op.payload, 2)[0]
                scene_id = a & 0x3FFF
                hi = a >> 14
                if hi == 0:   # activate
                    # Start a new scene block
                    cur_block = SceneBlock(
                        scene_id=scene_id,
                        actors=list(pending_actors),
                        lines=[],
                        flags_set=[],
                        flags_cleared=[],
                        items_given=[],
                    )
                    cur_blocks.append(cur_block)
                    pending_actors.clear()
                elif hi == 2:  # deactivate
                    # Close current block on next dialogue
                    pass
                continue
            if sub in (0x0004, 0x0005) and len(op.payload) >= 4:
                item_id = struct.unpack_from("<H", op.payload, 2)[0]
                blk = ensure_block()
                blk.items_given.append(item_id)
                continue

        # --- Dialogue ---
        if t == 8:
            blk = ensure_block()
            cues = parse_t8_cues(op.payload)
            if cues:
                for cue in cues:
                    if cue.pre == 0x0B:
                        slot = "item_notify"
                    elif cue.b1 is not None:
                        slot = T1_B1_NAMES.get(cue.b1, f"0x{cue.b1:02X}")
                    else:
                        slot = "ui_dialog" if cue.face_key is not None else "bubble"
                    blk.lines.append(
                        SpeechLine(
                            slot=slot,
                            text=cue.text,
                            op_offset=op.offset,
                            context_actor_id=last_placed_actor_id,
                            expr=cue.expr,
                            b1_slot=cue.b1,
                            hold=cue.hold,
                            face_key=cue.face_key,
                            cue_idx=cue.line_idx,
                            pre_controls=cue.pre_controls,
                        )
                    )
                continue

            lines = parse_t8_lines(op.payload)
            for ln in lines:
                if ln.strip():
                    blk.lines.append(
                        SpeechLine(
                            slot="bubble",
                            text=ln,
                            op_offset=op.offset,
                            context_actor_id=last_placed_actor_id,
                        )
                    )
            continue

        if t == 1:
            blk = ensure_block()
            context_actor_id = last_placed_actor_id
            cues = parse_t1_cues(op.payload)
            if cues:
                for cue in cues:
                    slot = "item_notify" if cue.pre == 0x0B else T1_B1_NAMES.get(cue.b1, f"0x{cue.b1:02X}")
                    if cue.text.strip():
                        blk.lines.append(
                            SpeechLine(
                                slot=slot,
                                text=cue.text,
                                op_offset=op.offset,
                                page_idx=cue.page_idx,
                                context_actor_id=context_actor_id,
                                expr=cue.expr,
                                b1_slot=cue.b1,
                                hold=cue.hold,
                                face_key=cue.face_key,
                                cue_idx=cue.line_idx,
                                pre_controls=cue.pre_controls,
                                header_stack=cue.header_stack,
                            )
                        )
                continue

            pages = parse_t1_pages(op.payload)
            if not pages:
                # Fallback: old behaviour (single header for whole op)
                hdr = parse_t1_header(op.payload)
                if hdr:
                    pre, b1, expr = hdr
                    slot = "item_notify" if pre == 0x0B else T1_B1_NAMES.get(b1, f"0x{b1:02X}")
                    blk.lines.append(
                        SpeechLine(
                            slot=slot,
                            text="(unparsed dialogue text)",
                            op_offset=op.offset,
                            page_idx=0,
                            context_actor_id=context_actor_id,
                            expr=expr,
                            b1_slot=b1,
                        )
                    )
                continue

            for i, pg in enumerate(pages):
                if pg.text.strip():
                    pre, b1, expr = pg.pre, pg.b1, pg.expr
                    slot = "item_notify" if pre == 0x0B else T1_B1_NAMES.get(b1, f"0x{b1:02X}")
                    blk.lines.append(
                        SpeechLine(
                            slot=slot,
                            text=pg.text,
                            op_offset=op.offset,
                            page_idx=i,
                            context_actor_id=context_actor_id,
                            expr=expr,
                            b1_slot=b1,
                        )
                    )
            continue

        # --- Yield ---
        if t == 14:
            blk = ensure_block()
            blk.has_yield = True
            # Close current block; next op starts fresh
            cur_block = None
            continue

    # Flush final section
    if cur_blocks:
        sections.append(Section(gate=cur_gate, blocks=cur_blocks, ip_start=section_ip_start))

    return sections


# ---------------------------------------------------------------------------
# Formatter
# ---------------------------------------------------------------------------

SLOT_LABELS = {
    "hero":        "HERO",
    "npc":         "NPC ",
    "bubble":      "BBLE",   # no-portrait speech bubble
    "ui_dialog":   "DLOG",
    "item_notify": "ITEM",
    "?":           "?   ",
}

# Flags that are pure VM-level state (dialogue mutex, camera state, etc.)
# and carry no story meaning — suppress from viewer output.
NOISE_FLAGS = {
    0x080E,  # dialogue-active mutex (set before / cleared after each op)
    0x0816,  # secondary dialogue mutex
}

CONT = "║  │  "          # continuation prefix inside a block
CONT_WRAP = "║  │           "  # wrap continuation (aligns under label text)

def _format_speech(
    slot: str,
    text: str,
    context_actor_id: int | None = None,
    expr: int | None = None,
    b1_slot: int | None = None,
    hold: int | None = None,
    face_key: int | None = None,
    page_idx: int | None = None,
    cue_idx: int | None = None,
    map_stem: str | None = None,
    field_root: Path | None = None,
    show_dialog_context: bool = False,
    playback_mode: bool = False,
    width: int = 58,
) -> list[str]:
    """Format one speech line/page into display rows."""
    label = SLOT_LABELS.get(slot, f"{slot:<4}")
    meta_parts: list[str] = []
    if show_dialog_context and context_actor_id is not None:
        meta_parts.append(f"ctx={_actor_label(context_actor_id)}")
    if slot == "item_notify" and expr is not None and field_root:
        meta_parts.append(f"item-sprite expr=0x{expr:02X} {expr_kind(expr, field_root)}")
    elif b1_slot is not None and expr is not None and map_stem and field_root:
        prt = portrait_label(map_stem, b1_slot, expr, field_root)
        meta_parts.append(prt)
    elif expr is not None:
        meta_parts.append(f"expr=0x{expr:02X}")
    if playback_mode:
        if page_idx is not None:
            meta_parts.append(f"page={page_idx + 1}")
        if cue_idx is not None:
            meta_parts.append(f"cue={cue_idx + 1}")
        if hold is not None:
            meta_parts.append(f"hold=0x{hold:02X}")
        if face_key is not None:
            meta_parts.append(f"face=0x{face_key:04X}")
    meta = " ".join(meta_parts)
    first_prefix = f"{CONT}[{label}]"
    if meta:
        first_prefix += f" {meta}"
    first_prefix += " "
    wrap_prefix  = CONT_WRAP
    out = []
    for seg in text.replace(r"\p", " [wait] ").split(r"\n"):
        seg = seg.strip()
        if not seg:
            continue
        while len(seg) > width:
            out.append(seg[:width])
            seg = seg[width:].strip()
        if seg:
            out.append(seg)
    if not out:
        return []
    result = [first_prefix + out[0]]
    for line in out[1:]:
        result.append(wrap_prefix + line)
    return result


def format_sections(
    sections: list[Section],
    *,
    show_actors: bool = True,
    show_dialog_context: bool = False,
    playback_mode: bool = False,
    map_stem: str | None = None,
    field_root: Path | None = None,
) -> str:
    rows: list[str] = []

    for sec_idx, sec in enumerate(sections):
        g = sec.gate

        # Gate header
        gate_parts = []
        if g.check_flag is not None:
            gate_parts.append(f"requires flag 0x{g.check_flag:04X}")
        if g.skip_if_set is not None:
            gate_parts.append(f"skips if 0x{g.skip_if_set:04X} set")
        if g.skip_if_clear is not None:
            gate_parts.append(f"skips if 0x{g.skip_if_clear:04X} clear")
        gate_str = "  [" + "]  [".join(gate_parts) + "]" if gate_parts else ""

        rows.append(f"╔═ SECTION {sec_idx}{gate_str}")

        for blk in sec.blocks:
            # Scene / actor header
            if blk.scene_id is not None:
                if show_actors and blk.actors:
                    actor_parts = [_actor_label(a) for a in blk.actors]
                    rows.append(f"║  ┌─ SCENE 0x{blk.scene_id:02X}  actors=[{', '.join(actor_parts)}]")
                else:
                    rows.append(f"║  ┌─ SCENE 0x{blk.scene_id:02X}")
            elif blk.actors and show_actors:
                actor_parts = [_actor_label(a) for a in blk.actors]
                rows.append(f"║  ┌─ (preload actors: {', '.join(actor_parts)})")
            else:
                rows.append(f"║  ┌─")

            # Dialogue lines
            for sl in blk.lines:
                rows.extend(_format_speech(
                    sl.slot, sl.text,
                    context_actor_id=sl.context_actor_id,
                    expr=sl.expr,
                    b1_slot=sl.b1_slot,
                    hold=sl.hold,
                    face_key=sl.face_key,
                    page_idx=sl.page_idx,
                    cue_idx=sl.cue_idx,
                    map_stem=map_stem,
                    field_root=field_root,
                    show_dialog_context=show_dialog_context,
                    playback_mode=playback_mode,
                ))

            # Items given
            for item_id in blk.items_given:
                rows.append(f"{CONT}[GIVE]  item_id=0x{item_id:04X}")

            # Flags (suppress VM-noise flags)
            for fid in blk.flags_set:
                if fid not in NOISE_FLAGS:
                    rows.append(f"{CONT}SET  flag 0x{fid:04X}")
            for fid in blk.flags_cleared:
                if fid not in NOISE_FLAGS:
                    rows.append(f"{CONT}CLR  flag 0x{fid:04X}")

            # Yield
            if blk.has_yield:
                rows.append(f"{CONT}─── yield ───")

            rows.append(f"║  └─")

        rows.append(f"╚═")
        rows.append("")

    return "\n".join(rows)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("map", help="Map stem, e.g. 2000")
    ap.add_argument("--id", dest="script_ids", action="append", default=[],
                    metavar="0xHEX", help="Script ID(s) to view (repeatable); default=all")
    ap.add_argument("--all", action="store_true", help="View all scripts")
    ap.add_argument("--no-actors", action="store_true", help="Omit actor ID lists")
    ap.add_argument(
        "--show-dialog-context",
        action="store_true",
        help="Show raw nearby place_actor id before dialogue (RE context only, not a proven speaker)",
    )
    ap.add_argument(
        "--playback",
        action="store_true",
        help="Show dialogue in playback order with per-cue page/hold/face metadata",
    )
    ap.add_argument(
        "--content",
        type=Path,
        default=DEFAULT_CONTENT,
        help="FIELD root (used when parsing FIELD/<map>.mdp)",
    )
    ap.add_argument(
        "--source",
        choices=("scn", "mdp", "auto"),
        default="scn",
        help="script source: scn prefers TEXT/<lang>/*.SCN, mdp uses FIELD/<map>.MDP (may not decode dialogue text)",
    )
    ap.add_argument("--text", type=Path, default=DEFAULT_TEXT, help="TEXT/<lang> dir")
    ap.add_argument("--out", type=Path, help="Write output to file instead of stdout")
    args = ap.parse_args()

    try:
        if args.source == "scn":
            directory, bank, entries = load_text_map(args.text, args.map)
        elif args.source == "mdp":
            directory, bank, entries = load_map(args.content, args.map)
        else:
            # auto
            try:
                directory, bank, entries = load_text_map(args.text, args.map)
            except FileNotFoundError:
                print(
                    f"warning: TEXT/<lang> for map {args.map} not found; falling back to FIELD/<map>.mdp. "
                    f"Dialogue text decoding may be wrong (use --source scn when possible).",
                    file=sys.stderr,
                )
                directory, bank, entries = load_map(args.content, args.map)
    except FileNotFoundError as e:
        print(f"error: {e}", file=sys.stderr)
        return 1

    want_ids: set[int] | None = None
    if args.script_ids:
        want_ids = {int(x, 16) for x in args.script_ids}
    elif not args.all:
        # Default: show script 0x0000 if present, else first script
        first_id = entries[0].script_id if entries else 0
        want_ids = {0x0000 if any(e.script_id == 0 for e in entries) else first_id}

    out_parts: list[str] = [f"# field_script_view  map={args.map}\n"]

    for entry in entries:
        if want_ids is not None and entry.script_id not in want_ids:
            continue
        ops = disasm_script(bank, entry.offset, entry.end)
        sections = lift_script(ops)
        out_parts.append(f"{'='*70}")
        out_parts.append(f"SCRIPT 0x{entry.script_id:04X}  [{entry.offset:#06x}, {entry.end:#06x})")
        out_parts.append(f"{'='*70}")
        out_parts.append(format_sections(
            sections,
            show_actors=not args.no_actors,
            show_dialog_context=args.show_dialog_context,
            playback_mode=args.playback,
            map_stem=args.map,
            field_root=args.content,
        ))

    output = "\n".join(out_parts)

    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(output, encoding="utf-8")
        print(f"wrote {args.out}", file=sys.stderr)
    else:
        sys.stdout.buffer.write(output.encode("utf-8"))
        sys.stdout.buffer.write(b"\n")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
