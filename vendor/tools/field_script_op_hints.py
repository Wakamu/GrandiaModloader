"""Human-readable labels and compaction hints for field-script IR ops."""
from __future__ import annotations

import struct

from field_script_disasm import TYPE6_SUB_NAMES
from field_script_ir import (
    ActorCamOp,
    ActorWalkArgs,
    BranchOp,
    CameraArgs,
    DialogueOp,
    FlagOp,
    GenericType6Args,
    GiveItemArgs,
    IrOp,
    JumpOp,
    Label,
    ModeOp,
    CallHookArgs,
    RawOp,
    RelJumpOp,
    UiDialogueOp,
    WaitReadyArgs,
    YieldOp,
)
from field_script_view import _actor_label
from field_script_hooks import hook_summary_for_step

# VM mutex flags — no story meaning; safe to ignore when reading flow.
VM_MUTEX_FLAGS = {0x080E, 0x0816, 0x08F9}

# Intro / progression flags seen on BA38 → Parm handoff (keep when stripping).
STORY_FLAGS: dict[int, str] = {
    0x0001: "intro already seen (replay skip)",
    0x009D: "branch gate (intro path)",
    0x009E: "branch gate (intro path)",
    0x03EA: "BA38 story beat",
    0x03EB: "BA38 late beat (Mullen pursuit)",
    0x03F6: "Parm arrival title path (map 2000)",
}


def _walk_route_id(raw: bytes) -> int | None:
    if len(raw) >= 4:
        return struct.unpack_from("<H", raw, 2)[0]
    return None


def _arm_wait_ticks(raw: bytes) -> int | None:
    if len(raw) >= 4:
        return struct.unpack_from("<H", raw, 2)[0]
    return None


def timeline_label_for_op(op: IrOp) -> str:
    """Primary one-line label for timeline / remove-op confirm."""
    if isinstance(op, UiDialogueOp):
        return "UI dialogue"
    if isinstance(op, DialogueOp):
        return "Portrait dialogue"
    if isinstance(op, ActorCamOp):
        if isinstance(op.args, WaitReadyArgs):
            if op.sub == 0x0020:
                return f"Conditional wait (0x{op.args.ticks:04X})"
            if op.sub == 0x0021:
                return f"Arm textbox sync ({op.args.ticks} tick)"
            return f"Wait {op.args.ticks} ticks"
        if isinstance(op.args, CallHookArgs):
            return f"call_hook({op.args.hook_id})"
        if isinstance(op.args, CameraArgs):
            mode = op.args.op.replace("_", " ")
            return f"Camera {mode} scene 0x{op.args.scene_id:02X}"
        if isinstance(op.args, GiveItemArgs):
            return f"Give item 0x{op.args.item_id:04X}"
        if op.sub == 0x0015 or isinstance(op.args, ActorWalkArgs):
            route = _walk_route_id(op.args.raw)
            if route == 0x000C:
                return "SFX recover"
            return f"SFX 0x{route:04X}" if route is not None else "SFX"
        if op.sub == 0x0010:
            return "Save menu"
        if op.sub == 0x000E:
            return "Restore party"
        if op.sub == 0x001B:
            return "Stash menu"
        if op.sub == 0x001C:
            return "Retrieve menu"
        sub_name = TYPE6_SUB_NAMES.get(op.sub, f"0x{op.sub:04X}")
        return f"Type-6 {sub_name}"
    if isinstance(op, FlagOp):
        verb = "Set" if op.is_set else "Clear"
        name = STORY_FLAGS.get(op.flag_id)
        if name:
            return f"{verb} flag 0x{op.flag_id:04X} ({name})"
        if op.flag_id in VM_MUTEX_FLAGS:
            return f"{verb} VM mutex 0x{op.flag_id:04X}"
        return f"{verb} flag 0x{op.flag_id:04X}"
    if isinstance(op, ModeOp):
        return "Flag context begin" if op.is_begin else "Flag context end"
    if isinstance(op, YieldOp):
        return "Yield (chunk boundary)"
    if isinstance(op, JumpOp):
        return f"Jump → {op.target.name}"
    if isinstance(op, RelJumpOp):
        return f"Rel jump → {op.target.name}"
    if isinstance(op, BranchOp):
        lo12 = op.word & 0x0FFF
        variants = {
            0x000: "check_flag",
            0x001: "skip_if_clear",
            0x040: "skip_if_set",
            0x041: "skip_if_set",
        }
        kind = variants.get(lo12, "branch")
        return f"{kind} 0x{op.arg:04X}"
    if isinstance(op, Label):
        return f"Label {op.name}"
    if isinstance(op, RawOp):
        return f"Raw op type {op.type_idx}"
    return type(op).__name__


def purpose_hint_for_op(op: IrOp, *, map_stem: str | None = None) -> str:
    """Explain what this op likely does in-game."""
    if isinstance(op, UiDialogueOp):
        return "Shows UI textbox (no portrait). Removing needs paired sync waits removed too."
    if isinstance(op, DialogueOp):
        from field_script_choice_view import has_menu_control, pickable_labels
        if has_menu_control(op.tokens) and pickable_labels(op.tokens):
            labels = " / ".join(pickable_labels(op.tokens))
            return (
                f"Choice menu ({labels}). Raw 0x05 + starred lines; "
                "following 0x4C40 branches compare the pick (0x4000) to extra N. "
                "Edit labels in the dialog op; routes are the jumps after this op."
            )
        return "Shows portrait dialogue. Removing needs paired sync waits removed too."
    if isinstance(op, ActorCamOp):
        if isinstance(op.args, WaitReadyArgs):
            if op.sub == 0x0020:
                return (
                    "Conditional/aux wait — often paired before portrait dialogue. "
                    "Do not remove alone if dialogue remains."
                )
            if op.sub == 0x0021:
                return (
                    "Arms the textbox ready-flag before dialogue/UI. "
                    "Must be followed by dialogue or removed together."
                )
            if op.args.ticks == 1:
                return (
                    "Sync wait (1 tick) — blocks until textbox/engine ready. "
                    "Usually paired with dialogue; safe to NOP only when stripping dialogue."
                )
            return (
                f"Timer wait ({op.args.ticks} frames) — animation/camera pacing. "
                "Usually safe to remove or shorten when compacting."
            )
        if isinstance(op.args, CallHookArgs):
            hid = op.args.hook_id
            extra = ""
            if map_stem and map_stem.upper() == "BA38" and hid == 25:
                extra = " Tested: title + BGM + Parm; fanout to hooks 51+52."
            return (
                f"Dispatch hook {hid} via MDP sec[7] (+0x53560 → +0x53830). "
                f"Map-local typed handler — fanout, party, SoftHD latch, etc.{extra} "
                "Use parse_mdp_hooks.py or the browser hook panel to inspect the row."
            )
        if isinstance(op.args, CameraArgs):
            if op.args.op == "activate":
                return (
                    f"Camera cut to scene 0x{op.args.scene_id:02X}. "
                    "Starts a new scene block; pending call_hook ops attach here."
                )
            if op.args.op == "overlay":
                return (
                    f"Start map-music catalog 0x{op.args.scene_id:02X} "
                    "(script 0x3000 usually pairs this with wait 1 + "
                    "deactivate_overlay; not MDP sec[10]+0xD0)."
                )
            if op.args.op == "deactivate":
                return f"End camera scene 0x{op.args.scene_id:02X}; return toward gameplay camera."
            if op.args.op == "deactivate_overlay":
                return (
                    f"Release map-music catalog 0x{op.args.scene_id:02X} "
                    "(keeps the track looping after the 1-tick arm)."
                )
            return f"Camera command ({op.args.op})."
        if isinstance(op.args, GiveItemArgs):
            return f"Give item 0x{op.args.item_id:04X} to the party."
        if op.sub == 0x0015 or isinstance(op.args, ActorWalkArgs):
            route = _walk_route_id(op.args.raw)
            if route == 0x000C:
                return "Play the Recover sound (type-6 0x0015 extra 0x000C)."
            return f"Play sound 0x{route:04X} (type-6 0x0015). Actor motion is usually a hook, not this packet."
        if op.sub == 0x0010:
            return "Open the save-game menu."
        if op.sub == 0x000E:
            return "Restore: heal the party to full HP/MP/status."
        if op.sub == 0x001B:
            return "Open the stash-item menu (send party items to stash)."
        if op.sub == 0x001C:
            return "Open the get-item menu (retrieve items from stash)."
        return f"Type-6 sub {TYPE6_SUB_NAMES.get(op.sub, hex(op.sub))}."
    if isinstance(op, FlagOp):
        if op.flag_id in VM_MUTEX_FLAGS:
            return "VM dialogue mutex — not story state; keep if dialogue remains."
        if op.flag_id in STORY_FLAGS:
            return f"Story flag: {STORY_FLAGS[op.flag_id]}. Keep when compacting intro handoff."
        verb = "Sets" if op.is_set else "Clears"
        return f"{verb} story/event flag 0x{op.flag_id:04X}."
    if isinstance(op, ModeOp):
        return "Opens/closes a flag-check context block."
    if isinstance(op, YieldOp):
        if map_stem and map_stem.upper() == "BA38":
            return (
                "Script chunk end — engine may swap script or map. "
                "Final yields (+0x1400) exit BA38 → Parm; do not remove last yields."
            )
        return "Script chunk boundary — may load next script slice or return to map."
    if isinstance(op, JumpOp):
        return (
            f"Jump → {op.target.name}. Taken only when preceded by flag_ctx + skip_if_* "
            "(the insert-jump tool adds that gate automatically)."
        )
    if isinstance(op, RelJumpOp):
        return f"Relative jump to label {op.target.name}."
    if isinstance(op, BranchOp):
        return "Conditional skip — changes which intro path runs. Keep unless you know the branch."
    if isinstance(op, Label):
        return "Branch target — do not remove."
    return "Unknown opcode."


def strip_hint_for_op(op: IrOp, *, map_stem: str | None = None) -> str:
    """Compaction advice mirroring field_script_strip.py policy."""
    if isinstance(op, (DialogueOp, UiDialogueOp)):
        return "Droppable for silent intro if sync waits removed too."
    if isinstance(op, ActorCamOp):
        if isinstance(op.args, WaitReadyArgs):
            if op.sub in {0x0011, 0x0020} and (op.sub != 0x0011 or op.args.ticks != 1):
                return "Usually droppable (timer/aux wait)."
            if op.sub == 0x0021 or (op.sub == 0x0011 and op.args.ticks == 1):
                return "Keep with dialogue; remove together when dropping dialogue."
            return "Review — sync vs timer."
        if isinstance(op.args, CameraArgs):
            return "Often droppable on BA38 (camera tweens play in real time)."
        if isinstance(op.args, CallHookArgs):
            if map_stem and map_stem.upper() == "BA38":
                return "Keep on BA38 — hooks drive intro handoff (e.g. hook 25 → Parm)."
            return "Keep unless you know the hook is cosmetic-only."
        if op.sub == 0x0015:
            return "Sound cue. Often droppable if the scene still reads."
        return "Review type-6 sub."
    if isinstance(op, FlagOp):
        if op.flag_id in VM_MUTEX_FLAGS:
            return "Keep if any dialogue remains."
        if op.flag_id in STORY_FLAGS:
            return "Keep — story progression / warp gate."
        return "Review — may affect story gates."
    if isinstance(op, YieldOp):
        return "Keep — especially terminal yields before map change."
    if isinstance(op, (JumpOp, RelJumpOp, BranchOp, Label, ModeOp)):
        return "Keep — flow control."
    return "Review manually."


def outside_script_note(map_stem: str | None = None) -> str:
    """Reminder that title card and BGM are not field-script opcodes."""
    lines = [
        "Map title cards (e.g. “Town of Parm”) are SCN text strings on the destination map (2000), not BA38 opcodes.",
        "Map BGM is script 0x3000: camera overlay / wait 1 / deactivate_overlay (catalog id, not MDP +0xD0).",
        "SoftHD intro movies are a separate client path, not field-script ops.",
    ]
    if map_stem and map_stem.upper() == "BA38":
        lines.append(
            "BA38 intro: call_hook(25)→52 present_channel(ch=1) latches SoftHD; "
            "exe FSM +6C000 state5 calls map-travel +614D0(ecx=0x2000=Parm). "
            "Map id is hardcoded in grandia.exe, not in the MDP hook row. "
            "Story flags 0x03EA/0x03EB and terminal yields near +0x1400 sit around that handoff."
        )
    return " ".join(lines)


def enrich_timeline_step(step: dict, ir_op: IrOp | None, *, map_stem: str) -> dict:
    """Add opSummary / purposeHint / stripHint to a timeline step dict."""
    if ir_op is None:
        return step
    step.setdefault("label", timeline_label_for_op(ir_op))
    step["opSummary"] = timeline_label_for_op(ir_op)
    step["purposeHint"] = purpose_hint_for_op(ir_op, map_stem=map_stem)
    step["stripHint"] = strip_hint_for_op(ir_op, map_stem=map_stem)
    if isinstance(ir_op, JumpOp):
        step["flowKind"] = "jump"
        step["targetLabelName"] = ir_op.target.name
    elif isinstance(ir_op, RelJumpOp):
        step["flowKind"] = "relJump"
        step["targetLabelName"] = ir_op.target.name
    elif isinstance(ir_op, BranchOp):
        step["flowKind"] = "branch"
        step["targetLabelName"] = ir_op.target.name
    if isinstance(ir_op, ActorCamOp) and isinstance(ir_op.args, CallHookArgs):
        step["hookId"] = ir_op.args.hook_id
        step["hookDetail"] = hook_summary_for_step(map_stem, ir_op.args.hook_id)
    return step
