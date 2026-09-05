"""Parse MDP sec[7] hook dispatch tables used by SCN ``call_hook`` (type-6 0x001A).

Map load copies sec[7] to heap ``[0x63FA6C]`` (+0x5489B, **0x2000 words = 0x4000 bytes**).
``+0x60DC6`` relocates four id→row tables; ``+0x53560`` looks up a hook id and calls
``+0x53830`` (VA ``0x453830``).

Hook "code" is **not** x86 — each row is a typed handler record
(``handler = row[1] & 0x3F``) interpreted by ``+0x53830``. That entry allocates an
8-byte event slot at ``[0x71C6E0+]`` (``slot+4`` = row pointer) and dispatches via
jump table ``+0x53CF8`` to per-type tick FSMs.

Sec[7] on-disk layout (authoring contract):
- ``+0``: flags u8; ``+1..+4``: counts[4] u8; ``+8..+17``: rels[4] u32 LE.
- Tables pack **tight** after an opaque prefix (``sec7[0x18:first_table_rel]``,
  preserves mystery table0 / padding). Row sizes: table1=32, table2/3=20.
- BA38/2000/E010 have **no free space** before the next MDP section — growing
  sec[7] splices bytes at the old end and bumps later header pointers
  (``replace_section``). The sec[7] file pointer must stay put.
- Rebuilt size must stay ≤ ``SEC7_HEAP_BUDGET`` (0x4000). Use ``validate_hook_bundle``.

Handler type → callee (RVA), from ``+0x53CF8``:

| type | callee   | role (RE) |
|------|----------|-----------|
| 0x00 | +0x72C00 | cam_word — write camera/map word [0x719930] |
| 0x01 | +0x72DC0 | present_channel — SoftHD channel latch (ch1 → intro FSM → map 0x2000) |
| 0x02 | +0x72EC0 | setup — party leader parms (+0x614D0) / optional +0x7EC50 |
| 0x03 | +0x73080 | attach_pos — XYZ nudge on attach kinds (+0x73230) |
| 0x04 | +0x73450 | field_bind — UV/offset scroll on attach kinds (+0x73660) |
| 0x06 | +0x73B10 | attach_vis — sec[0] hdr+4 attach show/hide (+0x73CB0) |
| 0x07 | +0x73EB0 | zone — enable/clear collision flags on attach parts (+0x74100) |
| 0x08 | +0x74400 | fx_pos — position tween on FX rows (+0x745B0) |
| 0x09 | +0x74880 | sfx_fx — toggle FX entry flags at [0x71CB0C] (+0x74A20) |
| 0x0B | +0x74D00 | cam_nudge — signed cam deltas + arm present [0x713F83] |
| 0x0C | +0x74EB0 | flag_wait — packed flag / SoftHD stream (mode 0–6 bank) |
| 0x0D | +0x750C0 | camera_path — select camera track / SoftHD stream |
| 0x0E | +0x752F0 | scene_boot — arm scene transition (+0x81170) |
| 0x10 | +0x755A0 | anim_latch — bind anim id onto actor slot (+0x7A1A0) |
| 0x11 | +0x75970 | attach_anim — hdr+4 part tween (+0x75AC0 / +0x59F40) |
| 0x13 | +0x75D60 | chest — table-1 0x9300 loot AABB (+0x534F8) |
| 0x14 | +0x76660 | unit_bind — bind field/party unit into [0x719BC0] |
| 0x15 | +0x76860 | param_block — write/tween camera parms at [0x7193E0] |
| 0x18 | +0x76D80 | visibility — party/NPC visible flags (+0x76F20) |
| 0x19 | +0x77160 | scripted_battle — write encounter blob, latch [0x71CD58]=1 |
| 0x1A | +0x773A0 | cutscene — fade / talk cast / script word |
| 0x1B | +0x778F0 | party_state — party / control FSM |
| 0x1C | +0x78000 | hook_fanout — fire child hook ids |
| 0x1D | +0x78180 | party_actor — party/NPC cmds (subtype in row[+4]&0xF) |
| 0x1E | +0x788A0 | sys_latch — SoftHD / camera / UI latches (subtype +4&0xF) |
"""
from __future__ import annotations

import struct
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path
from typing import Any

from mdp_lib import (
    CAMERA_MODE_LABELS,
    SELECT_PAN_OFF,
    decode_select_pan_bytes,
    encode_select_pan,
    find_field_dir,
    find_mdp,
    get_section,
    pad_sec7_heap_copy,
    patch_section_bytes,
    replace_section,
)

# row[1] & 0x3F → name. Names refined where callee payload is decoded.
HANDLER_TYPE_NAMES: dict[int, str] = {
    0x00: "cam_word",
    0x01: "present_channel",
    0x02: "setup",
    0x03: "attach_pos",
    0x04: "field_bind",
    0x05: "type_05",
    0x06: "attach_vis",
    0x07: "zone",
    0x08: "fx_pos",
    0x09: "sfx_fx",
    0x0A: "type_0A",
    0x0B: "cam_nudge",
    0x0C: "flag_wait",
    0x0D: "camera_path",
    0x0E: "scene_boot",
    0x0F: "type_0F",
    0x10: "anim_latch",
    0x11: "attach_anim",
    0x13: "type_13",
    0x14: "unit_bind",
    0x15: "param_block",
    0x16: "type_16",
    0x17: "type_17",
    0x18: "visibility",
    0x19: "scripted_battle",
    0x1A: "cutscene",
    0x1B: "party_state",
    0x1C: "hook_fanout",
    0x1D: "party_actor",
    0x1E: "sys_latch",
}

# Jump-table targets at +0x53CF8 (handler type → callee RVA).
HANDLER_CALLEES: dict[int, int] = {
    0x00: 0x72C00,
    0x01: 0x72DC0,
    0x02: 0x72EC0,
    0x03: 0x73080,
    0x04: 0x73450,
    0x05: 0x737D0,
    0x06: 0x73B10,
    0x07: 0x73EB0,
    0x08: 0x74400,
    0x09: 0x74880,
    0x0A: 0x74C10,
    0x0B: 0x74D00,
    0x0C: 0x74EB0,
    0x0D: 0x750C0,
    0x0E: 0x752F0,
    0x0F: 0x75400,
    0x10: 0x755A0,
    0x11: 0x75970,
    0x13: 0x75D60,
    0x14: 0x76660,
    0x15: 0x76860,
    0x16: 0x769E0,
    0x17: 0x76B10,
    0x18: 0x76D80,
    0x19: 0x77160,
    0x1A: 0x773A0,
    0x1B: 0x778F0,
    0x1C: 0x78000,
    0x1D: 0x78180,
    0x1E: 0x788A0,
}

# Empirical / tested annotations (map stem upper-case, hook id).
KNOWN_HOOK_NOTES: dict[tuple[str, int], str] = {
    ("BA38", 25): (
        "Intro handoff latch (tested: title + BGM + Parm). "
        "table2 fanout -> anim_latch(51) + present_channel(52, ch=1). "
        "A/B: call_hook(52) alone still does title+BGM→Parm; call_hook(51) alone softlocks. "
        "Do NOT convert this row to setup warp from BA38 intro — black-screens. "
        "To skip title/BGM: keep present_channel(52) and run tools/patch_softhd_ch1_skip.py. "
        "Stock table2 raw: 191c000090010000003334000000000000000000"
    ),
    ("BA38", 51): (
        "Child of hook 25 — anim_latch. "
        "A/B: replacing call_hook(25) with 51 alone softlocks (no Parm)."
    ),
    ("BA38", 52): (
        "Child of hook 25 — present_channel channel 1. "
        "A/B: alone still does title+BGM→Parm. "
        "Path: +72DC0 sets [713F85]=1 and [713F88+ch]=1; SoftHD poll +69005 "
        "calls +6C000(ch). ch1 FSM [6412BB] states 0..5; state5 hardcodes "
        "ecx=0x2000 (Parm) into map-travel +614D0. Title/BGM from SoftHD "
        "stream helpers +53E90/+53ED0/+70400 — not SCN opcodes. "
        "Map id is NOT in the MDP row (only channel=1)."
    ),
}

TABLE_ROW_SIZES = (0, 32, 20, 20)  # table0 unused size; 1=32b; 2/3=20b

# Map-load copies sec[7] as 0x2000 words (+0x5489B) → hard size ceiling.
SEC7_HEAP_BUDGET = 0x4000

# Stock BA38 table-2 fanout for hook 25 (children 51 + 52).
STOCK_BA38_HOOK25_FANOUT_HEX = "191c000090010000003334000000000000000000"

# type 0x0C (+0x74EB0) mode 0–6 adds these banks to the BE u16 at +6/+7,
# then tests the packed flag via +0x705F0 (or SoftHD stream +0x70400 if +4&0x20).
FLAG_WAIT_BANKS = (0, 0x800, 0x900, 0xA00, 0xE00, 0x1600, 0x1E00)

# 0x1C fanout child field offsets in 20-byte rows (+0x78000).
_FANOUT_CHILD_OFFS = (9, 10, 11, 15, 16, 17, 18, 19)
_SCRIPTED_BATTLE_PAIR_OFFS = (8, 9, 0xA, 0xB, 0xF, 0x10, 0x11, 0x12)

# party_actor (0x1D) subtype = row[+4] & 0xF (+0x78868 shared table).
PARTY_ACTOR_SUBTYPES: dict[int, str] = {
    0: "set_pos",
    1: "clear_busy",
    2: "set_timer",
    3: "walk",
    4: "set_dest_xyz",
    5: "set_busy",
    6: "set_field_byte",
    7: "set_party_byte",
    8: "instance_facing",
}

# visibility (0x18) target kind = row[+5] (+0x77140).
VISIBILITY_SUBTYPES: dict[int, str] = {
    0: "party_slot",
    1: "talk_id",
    2: "field_npc",
    3: "talk_mesh",
}

# sys_latch (0x1E) subtype = row[+4] & 0xF (+0x78C20).
SYS_LATCH_SUBTYPES: dict[int, str] = {
    0: "softhd_cue_8fe",
    1: "clear_713ea0",
    2: "present_flag_713f83",
    3: "cam_tween_1",
    4: "cam_tween_2",
    5: "cam_tween_3",
    6: "cam_tween_4",
    7: "set_719420",
    8: "nop",
    9: "arm_713ea0",
    10: "nop",
    11: "cam_tween_a",
}

# cutscene (0x1A) subtype = row[+4] & 0xF (+0x778A4).
CUTSCENE_SUBTYPES: dict[int, str] = {
    0: "nop",
    1: "fade",
    2: "talk_cast",
    3: "nop",
    4: "open_amap",
    5: "fade_mode",
    6: "open_amap2",
    7: "call_89220",
    8: "script_word",
    9: "fade_a",
    10: "fade_b",
    11: "multi_param",
}


def _s8(b: int) -> int:
    return b - 256 if b > 127 else b


@dataclass(frozen=True)
class HookRow:
    hook_id: int
    handler_type: int
    table_index: int
    row_index: int
    row_size: int
    raw: bytes
    sec7_offset: int

    @property
    def handler_name(self) -> str:
        name = HANDLER_TYPE_NAMES.get(self.handler_type)
        return f"0x{self.handler_type:02X}" + (f" ({name})" if name else "")

    @property
    def callee_rva(self) -> int | None:
        return HANDLER_CALLEES.get(self.handler_type)

    def decoded(self) -> dict[str, Any]:
        return decode_hook_payload(self.handler_type, self.raw, row_size=self.row_size)

    def to_dict(self) -> dict[str, Any]:
        callee = self.callee_rva
        return {
            "hookId": self.hook_id,
            "handlerType": self.handler_type,
            "handlerName": self.handler_name,
            "calleeRva": f"+0x{callee:X}" if callee is not None else None,
            "tableIndex": self.table_index,
            "rowIndex": self.row_index,
            "rowSize": self.row_size,
            "sec7Offset": self.sec7_offset,
            "rawHex": self.raw.hex(),
            "decoded": self.decoded(),
            "note": "",
        }


@dataclass
class HookBundle:
    map_stem: str
    flags: int
    counts: tuple[int, int, int, int]
    table_offsets: tuple[int, int, int, int]
    rows: list[HookRow]
    sec7: bytes

    def lookup(self, hook_id: int) -> list[HookRow]:
        return [r for r in self.rows if r.hook_id == hook_id]

    def best(self, hook_id: int, *, mode: int | None = None) -> HookRow | None:
        matches = self.lookup(hook_id)
        if not matches:
            return None
        if mode is not None and 1 <= mode <= 3:
            for row in matches:
                if row.table_index == mode:
                    return row
        # Prefer table 2 (20-byte, script mode 2) then others.
        matches.sort(key=lambda r: (0 if r.table_index == 2 else r.table_index, r.row_index))
        return matches[0]


def _event_slot_bind_mode(row_flags: int) -> int:
    """Pack row[+4] the way +0x76660 does before calling +0x78EA0."""
    packed = ((((row_flags & 0xE0) << 2) & 0xFF) | ((row_flags >> 3) & 3)) & 0x7F
    return packed


def decode_hook_payload(handler_type: int, raw: bytes, *, row_size: int) -> dict[str, Any]:
    """Best-effort payload decode for known handler types (20-byte rows first)."""
    out: dict[str, Any] = {"handlerType": handler_type}
    if len(raw) < 2:
        return out
    out["flags1"] = raw[1] >> 6
    if row_size < 20 or len(raw) < 20:
        return out

    if row_size >= 32 and len(raw) >= 32 and struct.unpack_from("<H", raw, 0)[0] == 0x9300:
        # Table-1 0x9300 loot row. +0x534F8 polls the trailing AABB.
        event_id = ((0x0A + raw[8]) << 8) | raw[9]
        item_id = (raw[10] << 8) | raw[11]
        aabb = struct.unpack_from("<hhhhhh", raw, 20)
        out.update(
            {
                "kind": "chest",
                "rowFlags": raw[4],
                "openType": raw[5],
                "attachKind": raw[6],
                "eventId": event_id,
                "itemId": item_id,
                "trig": raw[2],
                "aabb": aabb,
            }
        )
        return out

    if handler_type == 0x14:
        # +0x76660 → +0x78EA0 registers a 0x90-byte unit at [0x719BC0].
        # bindMode 0: party grid [0x71CD28]; 1: sec[8] talk id via +0x897D0.
        b4 = raw[4]
        packed = _event_slot_bind_mode(b4)
        if packed == 0:
            bind = "party_grid"
        elif packed == 1:
            bind = "field_talk"
        else:
            bind = f"mode_{packed}"
        out.update(
            {
                "kind": "unit_bind",
                "rowFlags": b4,
                "bindMode": packed,
                "bindName": bind,
                "unitKey": raw[5],
                "targetId": raw[6],
                "auxCount": raw[7],
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
                # +8/+9 appear often but are unread by +0x76660.
                "unknown89": (raw[8] << 8) | raw[9],
            }
        )
        return out

    if handler_type == 0x1C:
        mode_bits = raw[8]
        children: list[dict[str, Any]] = []
        for i, off in enumerate(_FANOUT_CHILD_OFFS):
            hid = raw[off]
            if hid == 0:
                continue
            bit = 1 << i
            children.append(
                {
                    "hookId": hid,
                    "mode": 2 if (mode_bits & bit) else 1,
                    "bit": i,
                }
            )
        out.update(
            {
                "kind": "hook_fanout",
                "rowFlags": raw[4],
                "delayTicks": raw[7],
                "modeBits": mode_bits,
                "children": children,
            }
        )
        return out

    if handler_type == 0x01:
        # +0x72DC0: after delayCap ticks, set SoftHD master [713F85]=1 and
        # channel latch [713F88 + (row[+5]&7)]=1. SoftHD tick +69005 runs
        # +6C000 per latched channel. Channel 1 is the intro FSM that ends
        # with map-travel +614D0(ecx=0x2000) — map id is hardcoded in exe,
        # not in the MDP row.
        out.update(
            {
                "kind": "present_channel",
                "rowFlags": raw[4],
                "channel": raw[5] & 7,
                "delayCap": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x04:
        # +0x73450 FSM → +0x73660 applies UV/offset deltas via +0x59F40/+0x59FE0.
        b4 = raw[4]
        out.update(
            {
                "kind": "field_bind",
                "rowFlags": b4,
                "repeats": (b4 & 0xF) + 1,
                "fadeMode": (b4 >> 4) & 3,
                "attachBase": raw[5],
                "span": raw[6],
                "delta": {"d7": _s8(raw[7]), "d8": _s8(raw[8]), "d9": _s8(raw[9])},
                "step": raw[0xB] or 8,
                "holdTicks": raw[0xF],
                "delayTicks": raw[0x10],
                "midDelay": raw[0x11],
                "lateDelay": raw[0x12],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x07:
        # +0x73EB0 → +0x74100 toggles collision words on attach parts via [0x719194].
        b4 = raw[4]
        out.update(
            {
                "kind": "zone",
                "rowFlags": b4,
                "partCount": (b4 & 0xF) + 1,
                "needButton": bool(b4 & 0x20),
                "sticky": bool(b4 & 0x10),
                "objectId": raw[5],
                "partBase": raw[6],
                "holdTicks": raw[0xF],
                "delayTicks": raw[0x10],
                "pollDelay": raw[0x11],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x0C:
        # +0x74EB0: delay on +0x10, then BE u16 at +6/+7 plus FLAG_WAIT_BANKS[+5].
        # +4&0x20 clear → +0x705F0 packed-flag test, success iff bit == +8 (0 or 1).
        # +4&0x20 set → +0x70400 SoftHD stream with edx=+8.
        # +0xF copied to event+2; follow +0x13 via +0x53560.
        mode = raw[5]
        index = (raw[6] << 8) | raw[7]
        flag_id = (FLAG_WAIT_BANKS[mode] + index) if 0 <= mode < len(FLAG_WAIT_BANKS) else index
        b4 = raw[4]
        out.update(
            {
                "kind": "flag_wait",
                "rowFlags": b4,
                "useStream": bool(b4 & 0x20),
                "mode": mode,
                "index": index,
                "flagId": flag_id,
                "expect": raw[8],
                "streamSlot": raw[8] if (b4 & 0x20) else None,
                "holdTicks": raw[0xF],
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x0D:
        # +0x750C0: pathId → +0x7C390; optional SoftHD stream via +0x70400 when +6≠0.
        b4 = raw[4]
        out.update(
            {
                "kind": "camera_path",
                "rowFlags": b4,
                "facingMode": (b4 >> 3) & 3,
                "pathId": raw[5],
                "streamSlot": raw[6] or None,
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x15:
        # +0x76860: BE u16 triplet → [0x7193E0]/tween via +0x53ED0 when mode≠0.
        p0 = (raw[5] << 8) | raw[6]
        p1 = (raw[7] << 8) | raw[8]
        p2 = (raw[9] << 8) | raw[10]
        mode = raw[0xB]
        out.update(
            {
                "kind": "param_block",
                "rowFlags": raw[4],
                "params": [p0, p1, p2],
                "mode": mode,
                "snap": mode == 0,
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x02:
        # +0x72EC0: BE map/aux → +0x614D0; walk-to XZ at party+0x3A/+0x3C.
        # Table-1 overlapping warps: +0x53830 calls +0x53D80 with (+2)&7 as
        # mode and flags packed at +0xC/+0xD/+0xE. Mode 1 = one flag
        # (bit 0x08 = must be set); mode 5 = flag1 set AND flag2 clear
        # (bit 0x08 inverted).
        b4 = raw[4]
        walk_x = raw[0xF] << 8 | raw[0x11]
        walk_z = raw[0x12] << 8 | raw[0x13]
        if walk_x >= 0x8000:
            walk_x -= 0x10000
        if walk_z >= 0x8000:
            walk_z -= 0x10000
        flag1, flag2 = setup_gate_flags(raw)
        out.update(
            {
                "kind": "setup",
                "rowFlags": b4,
                "useAltPath": bool(b4 & 0x10),
                "pair0": (raw[5] << 8) | raw[6],
                "pair1": (raw[7] << 8) | raw[8],
                "aux9": raw[9],
                "auxA": raw[0xA],
                "walkX": walk_x,
                "walkZ": walk_z,
                "delayTicks": raw[0x10],
                "gateMode": raw[2] & 7,
                "gatePolarity": (raw[2] >> 3) & 1,
                "gateFlag1": flag1 or None,
                "gateFlag2": flag2 or None,
            }
        )
        return out

    if handler_type == 0x09:
        # +0x74880 → +0x74A20 toggles 0x80 on FX rows at [0x71CB0C].
        b4 = raw[4]
        out.update(
            {
                "kind": "sfx_fx",
                "rowFlags": b4,
                "fxCount": (b4 & 0xF) + 1,
                "bitMode": (b4 >> 4) & 3,
                "fxId": raw[5],
                "op": raw[6] & 3,
                "delayTicks": raw[0x10],
                "lateDelay": raw[0x12],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x18:
        # +0x76D80 → +0x76F20 party/NPC visibility flags.
        b4 = raw[4]
        sub = raw[5]
        out.update(
            {
                "kind": "visibility",
                "rowFlags": b4,
                "partCount": (b4 & 0xF) + 1,
                "visMode": (b4 >> 4) & 3,
                "subtype": sub,
                "subtypeName": VISIBILITY_SUBTYPES.get(sub, f"sub_{sub}"),
                "targetId": raw[6],
                "holdTicks": raw[0xF],
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x1E:
        # +0x788A0 → +0x78A40 SoftHD/camera/UI latches.
        subtype = raw[4] & 0xF
        out.update(
            {
                "kind": "sys_latch",
                "rowFlags": raw[4],
                "subtype": subtype,
                "subtypeName": SYS_LATCH_SUBTYPES.get(subtype, f"sub_{subtype}"),
                "param": raw[5],
                "value": raw[6],
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x00:
        # +0x72C00: BE u16 → [0x719930] / [0x63FAA0] via +0x57550.
        # On table-1 AABB rows the word is usually an SCN id (0x6xxx / 0x9xxx /
        # 0xDxxx). +0x53830 → +0x53D80 applies the same flag gate as setup
        # (+2 mode/polarity, packed flags at +0xC/+0xD/+0xE) before arming.
        flag1, flag2 = setup_gate_flags(raw)
        out.update(
            {
                "kind": "cam_word",
                "rowFlags": raw[4],
                "word": (raw[5] << 8) | raw[6],
                "holdTicks": raw[7],
                "pollTicks": raw[8],
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
                "gateMode": raw[2] & 7,
                "gatePolarity": (raw[2] >> 3) & 1,
                "gateFlag1": flag1 or None,
                "gateFlag2": flag2 or None,
            }
        )
        return out

    if handler_type in (0x03, 0x08):
        # 0x03 +0x73230 attach XYZ; 0x08 +0x745B0 FX XYZ — shared row layout.
        b4 = raw[4]
        out.update(
            {
                "kind": "attach_pos" if handler_type == 0x03 else "fx_pos",
                "rowFlags": b4,
                "repeats": (b4 & 0xF) + 1,
                "attachBase": raw[5],
                "span": raw[6],
                "delta": {"d7": _s8(raw[7]), "d8": _s8(raw[8]), "d9": _s8(raw[9])},
                "step": raw[0xA] & 0x7F,
                "holdTicks": raw[0xF],
                "delayTicks": raw[0x10],
                "midDelay": raw[0x11],
                "lateDelay": raw[0x12],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x0B:
        # +0x74D00: signed cam deltas → [0x7140B0]/arm [0x713F83].
        out.update(
            {
                "kind": "cam_nudge",
                "rowFlags": raw[4],
                "delta": {
                    "d5": _s8(raw[5]),
                    "d6": _s8(raw[6]),
                    "d7": _s8(raw[7]),
                    "d8": _s8(raw[8]),
                    "d9": _s8(raw[9]),
                    "dA": _s8(raw[0xA]),
                },
                "holdTicks": raw[0xF],
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x0E:
        # +0x752F0 → +0x81170 arms scene transition globals.
        out.update(
            {
                "kind": "scene_boot",
                "rowFlags": raw[4],
                "modeByte": raw[5],
                "force": raw[5] == 0xFF,
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x1A:
        # +0x773A0 cutscene / talk-cast / script-word dispatcher.
        subtype = raw[4] & 0xF
        out.update(
            {
                "kind": "cutscene",
                "rowFlags": raw[4],
                "subtype": subtype,
                "subtypeName": CUTSCENE_SUBTYPES.get(subtype, f"sub_{subtype}"),
                "param": raw[5],
                "args": [raw[6], raw[7], raw[8], raw[9]],
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x06:
        # +0x73B10 → visibility batch +0x73CB0 (docs: attach show/hide).
        b4 = raw[4]
        out.update(
            {
                "kind": "attach_vis",
                "rowFlags": b4,
                "partCount": (b4 & 0xF) + 1,
                "visMode": (b4 >> 4) & 3,
                "attachKind": raw[5],
                "singleKind": raw[6],
                "holdTicks": raw[0xF],
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x1D:
        subtype = raw[4] & 0xF
        decoded_1d: dict[str, Any] = {
            "kind": "party_actor",
            "rowFlags": raw[4],
            "subtype": subtype,
            "subtypeName": PARTY_ACTOR_SUBTYPES.get(subtype, f"sub_{subtype}"),
            "followHookId": raw[0x13] or None,
        }
        if subtype == 8:
            # instance_facing: talk id + facing index via +0x897D0 / 0x600918.
            decoded_1d.update(
                {
                    "useTalkId": bool(raw[5] & 1),
                    "talkId": raw[6],
                    "facing": raw[7] & 7,
                    "charFlags": raw[5],
                }
            )
        elif subtype in (2, 3):
            decoded_1d.update(
                {
                    "partyCharId": raw[5] or 1,
                    "paramHi": raw[6],
                    "paramLo": raw[7],
                    "param2": raw[8],
                }
            )
        elif subtype == 4:
            decoded_1d.update(
                {
                    "partyCharId": raw[5] or 1,
                    "xyz": {
                        "x": (raw[8] << 8) | raw[9],
                        "z": (raw[0xA] << 8) | raw[0xB],
                        "y": (raw[0x10] << 8) | raw[0x11],
                    },
                }
            )
        else:
            decoded_1d.update(
                {
                    "partyCharId": raw[5] or 1,
                    "param0": raw[6],
                    "param1": raw[7],
                    "param2": raw[8],
                    "param3": raw[9],
                }
            )
        out.update(decoded_1d)
        return out

    if handler_type == 0x1B:
        # Duration is BE u16 at +0x10/+0x12; optional start values at +5/+6.
        duration = (raw[0x12] << 8) | raw[0x10]
        start = (raw[5] << 8) | raw[6]
        out.update(
            {
                "kind": "party_state",
                "rowFlags": raw[4],
                "subtype": raw[4] & 0xF,
                "startValue": start,
                "duration": duration,
                "param8": raw[8],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x10:
        # +0x755A0 → +0x7A1A0 looks up animId in [0x71CAE0]/[0x71C1B4],
        # then latches onto actor slot matching unitKey at [0x71A740].
        b4 = raw[4]
        out.update(
            {
                "kind": "anim_latch",
                "rowFlags": b4,
                "latchMode": (b4 >> 4) & 3,
                "animId": raw[5],
                "unitKey": raw[6],
                "slotResult": raw[7],
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x11:
        # +0x75970 FSM → +0x75AC0 tweens hdr+4 part xyz via +0x59F40/+0x59FE0.
        # +8/+9 = BE start; +0xA/+0x12 = BE end (low byte of end lives at +0x12).
        # +0xC/+0xE are runtime scratch (armed 0x40 at +0x75AD4).
        b4 = raw[4]
        out.update(
            {
                "kind": "attach_anim",
                "rowFlags": b4,
                "partCount": (b4 & 0xF) + 1,
                "axis": (b4 >> 4) & 3,
                "attachKind": raw[5],
                "partIndex": raw[6],
                "latch": raw[7],
                "start": (raw[8] << 8) | raw[9],
                "end": (raw[0xA] << 8) | raw[0x12],
                "step": raw[0xB],
                "interp": raw[0xD],
                "holdTicks": raw[0xF],
                "delayTicks": raw[0x10],
                "followHookId": raw[0x13] or None,
            }
        )
        return out

    if handler_type == 0x19:
        # +0x77160 writes the encounter blob then [0x71CD58] = 1.
        # [5] = EncounterTable (B00x). [6:8] BE optional word.
        # Pairs at 8,9,A,B,F,10,11,12: high nibble = SpeciesIndex, low = Count.
        # Encounter[13] is hardcoded 0xFF (not a wanderer row).
        pairs = []
        for off in _SCRIPTED_BATTLE_PAIR_OFFS:
            b = raw[off]
            if b:
                pairs.append({"speciesIndex": b >> 4, "count": b & 0xF, "off": off})
        out.update(
            {
                "kind": "scripted_battle",
                "rowFlags": raw[4],
                "encounterTable": raw[5],
                "word": (raw[6] << 8) | raw[7],
                "pairs": pairs,
                "trig": raw[2],
            }
        )
        return out

    out["kind"] = HANDLER_TYPE_NAMES.get(handler_type, f"type_{handler_type:02X}")
    out["rowFlags"] = raw[4]
    return out


def _row_hook_id(row: bytes, row_size: int) -> int:
    if row_size == 20:
        return row[0]
    if row_size == 32 and len(row) >= 8:
        be = struct.unpack_from(">H", row, 6)[0]
        if be != 0:
            return be
    return row[0]


def parse_hook_bundle(map_stem: str, *, field_root: Path | None = None) -> HookBundle | None:
    root = field_root or find_field_dir()
    mdp = find_mdp(root, map_stem).read_bytes()
    sec7 = get_section(mdp, 7)
    if not sec7 or len(sec7) < 0x18:
        return None
    flags = sec7[0]
    counts = tuple(sec7[1:5])  # type: ignore[return-value]
    rels = tuple(struct.unpack_from("<I", sec7, 8 + i * 4)[0] for i in range(4))
    rows: list[HookRow] = []
    for ti, (cnt, rel, rs) in enumerate(zip(counts, rels, TABLE_ROW_SIZES)):
        if cnt == 0 or rs == 0:
            continue
        start = rel
        end = start + cnt * rs
        if end > len(sec7):
            continue
        for ri in range(cnt):
            off = start + ri * rs
            raw = sec7[off : off + rs]
            hid = _row_hook_id(raw, rs)
            handler = raw[1] & 0x3F if len(raw) > 1 else 0
            rows.append(
                HookRow(
                    hook_id=hid,
                    handler_type=handler,
                    table_index=ti,
                    row_index=ri,
                    row_size=rs,
                    raw=raw,
                    sec7_offset=off,
                )
            )
    return HookBundle(
        map_stem=map_stem.upper(),
        flags=flags,
        counts=counts,
        table_offsets=rels,
        rows=rows,
        sec7=sec7,
    )


@lru_cache(maxsize=64)
def load_hook_bundle(map_stem: str, field_root: str | None = None) -> HookBundle | None:
    path = Path(field_root) if field_root else None
    return parse_hook_bundle(map_stem, field_root=path)


def _format_decoded(decoded: dict[str, Any]) -> list[str]:
    kind = decoded.get("kind")
    if not kind:
        return []
    lines = [f"decoded: {kind}"]
    if kind == "attach_vis":
        lines.append(
            f"  kind={decoded.get('attachKind')} parts={decoded.get('partCount')} "
            f"mode={decoded.get('visMode')} single={decoded.get('singleKind')} "
            f"hold={decoded.get('holdTicks')} follow={decoded.get('followHookId')}"
        )
    elif kind == "field_bind":
        d = decoded.get("delta") or {}
        lines.append(
            f"  base={decoded.get('attachBase')} span={decoded.get('span')} "
            f"reps={decoded.get('repeats')} step={decoded.get('step')} "
            f"delta=[{d.get('d7')},{d.get('d8')},{d.get('d9')}] "
            f"hold={decoded.get('holdTicks')} follow={decoded.get('followHookId')}"
        )
    elif kind == "zone":
        lines.append(
            f"  obj={decoded.get('objectId')} part={decoded.get('partBase')} "
            f"parts={decoded.get('partCount')} poll={decoded.get('pollDelay')} "
            f"btn={decoded.get('needButton')} sticky={decoded.get('sticky')} "
            f"follow={decoded.get('followHookId')}"
        )
    elif kind == "camera_path":
        lines.append(
            f"  path={decoded.get('pathId')} stream={decoded.get('streamSlot')} "
            f"facing_mode={decoded.get('facingMode')} "
            f"delay={decoded.get('delayTicks')} follow={decoded.get('followHookId')}"
        )
    elif kind == "param_block":
        ps = decoded.get("params") or []
        lines.append(
            f"  params=[{', '.join(f'0x{p:04X}' for p in ps)}] "
            f"mode={decoded.get('mode')} snap={decoded.get('snap')} "
            f"delay={decoded.get('delayTicks')} follow={decoded.get('followHookId')}"
        )
    elif kind == "setup":
        lines.append(
            f"  pair0=0x{decoded.get('pair0', 0):04X} pair1=0x{decoded.get('pair1', 0):04X} "
            f"aux=[{decoded.get('aux9')},{decoded.get('auxA')}] "
            f"alt={decoded.get('useAltPath')} walk=({decoded.get('walkX')},{decoded.get('walkZ')})"
        )
    elif kind == "sfx_fx":
        lines.append(
            f"  fx={decoded.get('fxId')} count={decoded.get('fxCount')} "
            f"op={decoded.get('op')} bit_mode={decoded.get('bitMode')} "
            f"delay={decoded.get('delayTicks')} follow={decoded.get('followHookId')}"
        )
    elif kind == "visibility":
        lines.append(
            f"  subtype={decoded.get('subtypeName')} target={decoded.get('targetId')} "
            f"parts={decoded.get('partCount')} mode={decoded.get('visMode')} "
            f"follow={decoded.get('followHookId')}"
        )
    elif kind == "sys_latch":
        lines.append(
            f"  subtype={decoded.get('subtypeName')} param={decoded.get('param')} "
            f"value={decoded.get('value')} follow={decoded.get('followHookId')}"
        )
    elif kind == "cam_word":
        lines.append(
            f"  word=0x{decoded.get('word', 0):04X} hold={decoded.get('holdTicks')} "
            f"poll={decoded.get('pollTicks')} follow={decoded.get('followHookId')}"
        )
    elif kind in ("attach_pos", "fx_pos"):
        d = decoded.get("delta") or {}
        lines.append(
            f"  base={decoded.get('attachBase')} span={decoded.get('span')} "
            f"reps={decoded.get('repeats')} step={decoded.get('step')} "
            f"delta=[{d.get('d7')},{d.get('d8')},{d.get('d9')}] "
            f"hold={decoded.get('holdTicks')} follow={decoded.get('followHookId')}"
        )
    elif kind == "cam_nudge":
        d = decoded.get("delta") or {}
        lines.append(
            f"  delta=[{d.get('d5')},{d.get('d6')},{d.get('d7')},"
            f"{d.get('d8')},{d.get('d9')},{d.get('dA')}] "
            f"hold={decoded.get('holdTicks')} follow={decoded.get('followHookId')}"
        )
    elif kind == "scene_boot":
        lines.append(
            f"  mode=0x{decoded.get('modeByte', 0):02X} force={decoded.get('force')} "
            f"follow={decoded.get('followHookId')}"
        )
    elif kind == "cutscene":
        args = decoded.get("args") or []
        lines.append(
            f"  subtype={decoded.get('subtypeName')} param={decoded.get('param')} "
            f"args={args} follow={decoded.get('followHookId')}"
        )
    elif kind == "anim_latch":
        lines.append(
            f"  anim={decoded.get('animId')} unit={decoded.get('unitKey')} "
            f"mode={decoded.get('latchMode')} slot={decoded.get('slotResult')} "
            f"delay={decoded.get('delayTicks')} follow={decoded.get('followHookId')}"
        )
    elif kind == "attach_anim":
        lines.append(
            f"  kind={decoded.get('attachKind')} part={decoded.get('partIndex')} "
            f"axis={decoded.get('axis')} start={decoded.get('start')} "
            f"end={decoded.get('end')} step={decoded.get('step')} "
            f"interp={decoded.get('interp')} hold={decoded.get('holdTicks')} "
            f"follow={decoded.get('followHookId')}"
        )
    elif kind == "flag_wait":
        lines.append(
            f"  flag=0x{int(decoded.get('flagId') or 0):04X} mode={decoded.get('mode')} "
            f"index={decoded.get('index')} expect={decoded.get('expect')} "
            f"stream={decoded.get('useStream')} hold={decoded.get('holdTicks')} "
            f"delay={decoded.get('delayTicks')} follow={decoded.get('followHookId')}"
        )
    elif kind == "chest":
        aabb = decoded.get("aabb") or ()
        lines.append(
            f"  event=0x{int(decoded.get('eventId') or 0):04X} "
            f"item={decoded.get('itemId')} open={decoded.get('openType')} "
            f"kind={decoded.get('attachKind')} aabb={aabb}"
        )
    elif kind == "unit_bind":
        tgt = decoded.get("targetId")
        bind = decoded.get("bindName")
        tgt_note = "talk_id" if bind == "field_talk" else "target"
        lines.append(
            f"  bind={bind} unit_key={decoded.get('unitKey')} "
            f"{tgt_note}={tgt} aux={decoded.get('auxCount')} "
            f"delay={decoded.get('delayTicks')} follow={decoded.get('followHookId')}"
        )
    elif kind == "hook_fanout":
        kids = decoded.get("children") or []
        kid_s = ", ".join(f"{c['hookId']}(mode{c['mode']})" for c in kids) or "(none)"
        lines.append(
            f"  delay={decoded.get('delayTicks')} mode_bits=0x{decoded.get('modeBits', 0):02X} "
            f"row_flags=0x{decoded.get('rowFlags', 0):02X}"
        )
        lines.append(f"  children: {kid_s}")
    elif kind == "present_channel":
        lines.append(
            f"  channel={decoded.get('channel')} delay_cap={decoded.get('delayCap')} "
            f"follow={decoded.get('followHookId')}"
        )
    elif kind == "party_actor":
        sub = decoded.get("subtypeName", decoded.get("subtype"))
        if decoded.get("subtype") == 8:
            lines.append(
                f"  subtype={sub} talk_id={decoded.get('talkId')} "
                f"facing={decoded.get('facing')} follow={decoded.get('followHookId')}"
            )
        else:
            lines.append(
                f"  subtype={sub} char={decoded.get('partyCharId')} "
                f"follow={decoded.get('followHookId')}"
            )
    elif kind == "party_state":
        lines.append(
            f"  subtype={decoded.get('subtype')} start={decoded.get('startValue')} "
            f"duration={decoded.get('duration')} follow={decoded.get('followHookId')}"
        )
    else:
        if "rowFlags" in decoded:
            lines.append(f"  row_flags=0x{decoded['rowFlags']:02X}")
    return lines


def format_hook_row(row: HookRow, *, map_stem: str | None = None) -> str:
    stem = (map_stem or "").upper()
    note = KNOWN_HOOK_NOTES.get((stem, row.hook_id), "")
    callee = row.callee_rva
    callee_s = f"  callee +0x{callee:X}" if callee is not None else ""
    lines = [
        f"hook {row.hook_id}  handler {row.handler_name}{callee_s}",
        f"table {row.table_index} row {row.row_index}  ({row.row_size} bytes @ sec[7]+0x{row.sec7_offset:X})",
        f"raw: {row.raw.hex()}",
    ]
    lines[1:1] = _format_decoded(row.decoded())
    if note:
        lines.insert(1, f"note: {note}")
    explain = explain_hook_row(row, map_stem=stem)
    if explain:
        lines.insert(1, f"does: {explain}")
    return "\n".join(lines)


def cam_word_meaning(word: int) -> str:
    """Human label for a type-0 cam_word payload."""
    if 0xE000 <= word <= 0xE0FF:
        return f"Save/Recover/Hint menu (starts SCN 0x{word:04X})"
    if 0x9000 <= word <= 0x9FFF or 0x6000 <= word <= 0x6FFF:
        return f"starts SCN 0x{word:04X}"
    if 0xD000 <= word <= 0xD0FF:
        return f"door/talk prompt (starts SCN 0x{word:04X})"
    if 0x7F00 <= word <= 0x7FFF:
        pair = 0xD000 + (word - 0x7F00)
        return f"world-map edge marker (pair SCN 0x{pair:04X})"
    if word == 0:
        return "camera/map word 0 (clears / idle)"
    return f"camera/map word 0x{word:04X} (often starts SCN 0x{word:04X})"


def _looks_like_map_id(value: int) -> bool:
    return 0x1000 <= value <= 0xFF00 and (value & 0xF) == 0


def explain_decoded(decoded: dict[str, Any], *, hook_id: int | None = None) -> str:
    """One-sentence description of a decoded sec[7] row."""
    kind = decoded.get("kind") or "unknown"
    hid = f"hook {hook_id}: " if hook_id is not None else ""
    follow = decoded.get("followHookId")
    follow_s = f" Then fires hook {follow}." if follow else ""
    delay = decoded.get("delayTicks")
    delay_s = f" After {delay} ticks." if delay else ""

    if kind == "hook_fanout":
        kids = decoded.get("children") or []
        if not kids:
            return f"{hid}fanout with no children (no-op)."
        parts = [f"{c['hookId']} (mode {c['mode']})" for c in kids]
        return f"{hid}fanout — fires " + ", ".join(parts) + f".{delay_s}"
    if kind == "present_channel":
        ch = decoded.get("channel")
        extra = ""
        if ch == 1:
            extra = " Channel 1 is the SoftHD intro FSM (Parm map 0x2000 is hardcoded in exe)."
        return f"{hid}latches SoftHD channel {ch}.{extra}{delay_s}{follow_s}"
    if kind == "cam_word":
        word = int(decoded.get("word") or 0)
        mode = int(decoded.get("gateMode") or 0)
        f1 = int(decoded.get("gateFlag1") or 0)
        f2 = int(decoded.get("gateFlag2") or 0)
        pol = int(decoded.get("gatePolarity") or 0)
        cond = ""
        if mode == 1 and f1:
            cond = f" if flag 0x{f1:X} is {'set' if pol else 'clear'}"
        elif mode == 5 and f1:
            if pol:
                cond = f" if flag 0x{f1:X} is set and 0x{f2:X} is clear"
            else:
                cond = f" if flag 0x{f1:X} is clear or 0x{f2:X} is set"
        return f"{hid}writes {cam_word_meaning(word)}.{cond}{delay_s}{follow_s}"
    if kind == "setup":
        pair0 = int(decoded.get("pair0") or 0)
        mode = int(decoded.get("gateMode") or 0)
        f1 = int(decoded.get("gateFlag1") or 0)
        f2 = int(decoded.get("gateFlag2") or 0)
        pol = int(decoded.get("gatePolarity") or 0)
        cond = ""
        if mode == 1 and f1:
            cond = f" if flag 0x{f1:X} is {'set' if pol else 'clear'}"
        elif mode == 5 and f1:
            if pol:
                cond = f" if flag 0x{f1:X} is set and 0x{f2:X} is clear"
            else:
                cond = (
                    f" if flag 0x{f1:X} is clear or 0x{f2:X} is set"
                )
        elif mode:
            cond = f" gate={mode} a=0x{f1:X} b=0x{f2:X} pol={pol}"
        if _looks_like_map_id(pair0):
            return (
                f"{hid}setup warp to map 0x{pair0:04X}{cond} "
                f"(walk to {decoded.get('walkX')},{decoded.get('walkZ')}).{delay_s}"
            )
        return (
            f"{hid}setup pair0=0x{pair0:04X} pair1=0x{int(decoded.get('pair1') or 0):04X} "
            f"walk=({decoded.get('walkX')},{decoded.get('walkZ')}){cond}.{delay_s}"
        )
    if kind == "anim_latch":
        return (
            f"{hid}plays anim {decoded.get('animId')} on unit {decoded.get('unitKey')} "
            f"(latch mode {decoded.get('latchMode')}).{delay_s}{follow_s}"
        )
    if kind == "attach_anim":
        return (
            f"{hid}attach anim kind={decoded.get('attachKind')} "
            f"part={decoded.get('partIndex')} start={decoded.get('start')} "
            f"end={decoded.get('end')}.{follow_s}"
        )
    if kind == "flag_wait":
        fid = int(decoded.get("flagId") or 0)
        expect = int(decoded.get("expect") or 0)
        if decoded.get("useStream"):
            return (
                f"{hid}SoftHD stream {decoded.get('streamSlot')} "
                f"on flag 0x{fid:04X}.{delay_s}{follow_s}"
            )
        return f"{hid}waits until flag 0x{fid:04X} is {expect}.{delay_s}{follow_s}"
    if kind == "scripted_battle":
        table = int(decoded.get("encounterTable") or 0)
        pairs = decoded.get("pairs") or []
        pack = " ".join(
            f"{int(p.get('speciesIndex') or 0)}x{int(p.get('count') or 0)}" for p in pairs
        )
        extra = f" {pack}" if pack else ""
        word = int(decoded.get("word") or 0)
        word_s = f" word=0x{word:X}" if word else ""
        return (
            f"{hid}scripted battle table=0x{table:02X}{extra}{word_s} "
            f"(EncounterRow=255).{follow_s}"
        )
    if kind == "chest":
        return (
            f"{hid}chest event 0x{int(decoded.get('eventId') or 0):04X} "
            f"item {decoded.get('itemId')} open={decoded.get('openType')} "
            f"kind={decoded.get('attachKind')}."
        )
    if kind == "unit_bind":
        bind = decoded.get("bindName") or "unit"
        tgt = "talk id" if bind == "field_talk" else "target"
        return (
            f"{hid}binds {bind} unit {decoded.get('unitKey')} "
            f"({tgt} {decoded.get('targetId')}).{delay_s}{follow_s}"
        )
    if kind == "zone":
        btn = " needs button" if decoded.get("needButton") else ""
        return (
            f"{hid}zone on object {decoded.get('objectId')} "
            f"parts {decoded.get('partBase')}+{decoded.get('partCount')}{btn}.{follow_s}"
        )
    if kind == "camera_path":
        return (
            f"{hid}runs camera path {decoded.get('pathId')}"
            f"{' + SoftHD stream ' + str(decoded.get('streamSlot')) if decoded.get('streamSlot') else ''}."
            f"{delay_s}{follow_s}"
        )
    if kind == "visibility":
        return (
            f"{hid}visibility {decoded.get('subtypeName') or decoded.get('subtype')} "
            f"target {decoded.get('targetId')} mode {decoded.get('visMode')}.{follow_s}"
        )
    if kind == "sfx_fx":
        return f"{hid}FX {decoded.get('fxId')} op={decoded.get('op')} count={decoded.get('fxCount')}.{follow_s}"
    if kind == "sys_latch":
        return (
            f"{hid}sys latch {decoded.get('subtypeName') or decoded.get('subtype')} "
            f"param={decoded.get('param')} value={decoded.get('value')}.{follow_s}"
        )
    if kind == "party_actor":
        sub = decoded.get("subtypeName") or decoded.get("subtype")
        if decoded.get("subtype") == 8:
            return f"{hid}party actor {sub} talk_id={decoded.get('talkId')} facing={decoded.get('facing')}.{follow_s}"
        return f"{hid}party actor {sub} char={decoded.get('partyCharId')}.{follow_s}"
    if kind == "party_state":
        return f"{hid}party state subtype {decoded.get('subtype')} duration={decoded.get('duration')}.{follow_s}"
    if kind == "param_block":
        ps = decoded.get("params") or []
        hexes = ", ".join(f"0x{int(p):04X}" for p in ps)
        return f"{hid}camera param block [{hexes}] mode={decoded.get('mode')}.{follow_s}"
    if kind == "field_bind":
        return (
            f"{hid}field UV/offset bind base={decoded.get('attachBase')} "
            f"span={decoded.get('span')} reps={decoded.get('repeats')}.{follow_s}"
        )
    if kind in ("attach_pos", "fx_pos"):
        return f"{hid}{kind} base={decoded.get('attachBase')} span={decoded.get('span')} reps={decoded.get('repeats')}.{follow_s}"
    if kind == "attach_vis":
        return f"{hid}attach visibility kind={decoded.get('attachKind')} parts={decoded.get('partCount')}.{follow_s}"
    if kind == "cam_nudge":
        return f"{hid}nudges camera by signed deltas.{follow_s}"
    if kind == "scene_boot":
        return f"{hid}arms scene transition (mode 0x{int(decoded.get('modeByte') or 0):02X}).{follow_s}"
    if kind == "cutscene":
        return f"{hid}cutscene {decoded.get('subtypeName') or decoded.get('subtype')} param={decoded.get('param')}.{follow_s}"
    if str(kind).startswith("type_"):
        return f"{hid}handler {kind} — payload layout not fully decoded yet.{follow_s}"
    return f"{hid}{kind}.{follow_s}"


def explain_hook_row(row: HookRow, *, map_stem: str | None = None) -> str:
    text = explain_decoded(row.decoded(), hook_id=row.hook_id)
    note = KNOWN_HOOK_NOTES.get(((map_stem or "").upper(), row.hook_id), "")
    if note:
        # First sentence of the empirical note, keep the panel short.
        first = note.split(". ")[0].rstrip(".") + "."
        if first not in text:
            text = f"{text} {first}"
    return text.strip()


def _chain_node_from_row(row: HookRow, map_stem: str) -> dict[str, Any]:
    d = row.decoded()
    return {
        "hookId": row.hook_id,
        "handlerType": row.handler_type,
        "handlerName": row.handler_name,
        "kind": d.get("kind"),
        "tableIndex": row.table_index,
        "explanation": explain_hook_row(row, map_stem=map_stem),
        "followHookId": d.get("followHookId"),
        "children": [],
    }


def expand_hook_chain(
    bundle: HookBundle,
    hook_id: int,
    *,
    mode: int | None = None,
    max_depth: int = 5,
) -> dict[str, Any]:
    """Walk fanout children + followHookId so the UI can show the full dispatch."""
    seen: set[tuple[int, int]] = set()

    def walk(hid: int, depth: int, prefer_mode: int | None) -> dict[str, Any] | None:
        row = bundle.best(hid, mode=prefer_mode)
        if row is None:
            return {
                "hookId": hid,
                "found": False,
                "explanation": f"hook {hid} is not in this map's MDP sec[7].",
                "children": [],
            }
        key = (row.hook_id, row.sec7_offset)
        if key in seen:
            node = _chain_node_from_row(row, bundle.map_stem)
            node["found"] = True
            node["cycle"] = True
            node["explanation"] = (node["explanation"] + " (cycle, already expanded).").strip()
            return node
        seen.add(key)
        node = _chain_node_from_row(row, bundle.map_stem)
        node["found"] = True
        if depth >= max_depth:
            node["truncated"] = True
            return node
        decoded = row.decoded()
        child_specs: list[tuple[int, int | None]] = []
        if decoded.get("kind") == "hook_fanout":
            for child in decoded.get("children") or []:
                child_specs.append((int(child["hookId"]), int(child.get("mode") or 0) or None))
        follow = decoded.get("followHookId")
        if follow:
            child_specs.append((int(follow), None))
        for cid, cmode in child_specs:
            child_node = walk(cid, depth + 1, cmode)
            if child_node is not None:
                node["children"].append(child_node)
        return node

    return walk(hook_id, 0, mode) or {"hookId": hook_id, "found": False, "children": []}


def _attach_hook_explain(out: dict[str, Any], row: HookRow, bundle: HookBundle | None) -> dict[str, Any]:
    out["explanation"] = explain_hook_row(row, map_stem=bundle.map_stem if bundle else None)
    if bundle is not None:
        out["chain"] = expand_hook_chain(bundle, row.hook_id, mode=row.table_index)
    return out


def hook_summary_for_step(map_stem: str, hook_id: int, *, mode: int | None = None) -> dict[str, Any] | None:
    bundle = load_hook_bundle(map_stem)
    if bundle is None:
        return None
    row = bundle.best(hook_id, mode=mode)
    if row is None:
        return {"hookId": hook_id, "found": False}
    out = row.to_dict()
    out["found"] = True
    note = KNOWN_HOOK_NOTES.get((map_stem.upper(), hook_id))
    if note:
        out["note"] = note
    out["editableFields"] = editable_fields_for_row(row)
    return _attach_hook_explain(out, row, bundle)


def editable_fields_for_row(row: HookRow) -> list[dict[str, Any]]:
    """UI field descriptors for hook-row editing (kind-aware + raw hex)."""
    d = row.decoded()
    kind = d.get("kind")
    fields: list[dict[str, Any]] = []
    if kind == "present_channel":
        fields.extend(
            [
                {
                    "key": "channel",
                    "label": "channel (0-7)",
                    "type": "int",
                    "min": 0,
                    "max": 7,
                    "value": int(d.get("channel") or 0),
                    "hint": "SoftHD latch index; BA38 Parm intro uses 1",
                },
                {
                    "key": "delayCap",
                    "label": "delay_cap",
                    "type": "int",
                    "min": 0,
                    "max": 255,
                    "value": int(d.get("delayCap") or 0),
                },
                {
                    "key": "followHookId",
                    "label": "follow_hook",
                    "type": "int",
                    "min": 0,
                    "max": 255,
                    "value": int(d.get("followHookId") or 0),
                    "hint": "0 = none",
                },
            ]
        )
    elif kind == "hook_fanout":
        kids = [int(c["hookId"]) for c in (d.get("children") or [])]
        # Pad to 8 slots for stable editing.
        while len(kids) < 8:
            kids.append(0)
        fields.extend(
            [
                {
                    "key": "delayTicks",
                    "label": "delay",
                    "type": "int",
                    "min": 0,
                    "max": 255,
                    "value": int(d.get("delayTicks") or 0),
                },
                {
                    "key": "modeBits",
                    "label": "mode_bits",
                    "type": "int",
                    "min": 0,
                    "max": 255,
                    "value": int(d.get("modeBits") or 0),
                },
                {
                    "key": "children",
                    "label": "children (8 ids, comma-sep; 0=empty)",
                    "type": "int_list",
                    "count": 8,
                    "value": kids[:8],
                },
            ]
        )
    elif kind == "anim_latch":
        fields.extend(
            [
                {
                    "key": "animId",
                    "label": "anim",
                    "type": "int",
                    "min": 0,
                    "max": 255,
                    "value": int(d.get("animId") or 0),
                },
                {
                    "key": "unitKey",
                    "label": "unit",
                    "type": "int",
                    "min": 0,
                    "max": 255,
                    "value": int(d.get("unitKey") or 0),
                },
                {
                    "key": "latchMode",
                    "label": "mode (row[+4] nibble)",
                    "type": "int",
                    "min": 0,
                    "max": 3,
                    "value": int(d.get("latchMode") or 0),
                },
                {
                    "key": "delayTicks",
                    "label": "delay",
                    "type": "int",
                    "min": 0,
                    "max": 255,
                    "value": int(d.get("delayTicks") or 0),
                },
                {
                    "key": "followHookId",
                    "label": "follow_hook",
                    "type": "int",
                    "min": 0,
                    "max": 255,
                    "value": int(d.get("followHookId") or 0),
                },
            ]
        )
    elif kind == "setup":
        fields.extend(
            [
                {
                    "key": "pair0",
                    "label": "pair0 / map id (BE u16)",
                    "type": "int",
                    "min": 0,
                    "max": 65535,
                    "value": int(d.get("pair0") or 0),
                    "hint": "Passed as ecx to map-travel +614D0. Parm = 0x2000 (8192).",
                },
                {
                    "key": "pair1",
                    "label": "pair1 (BE u16)",
                    "type": "int",
                    "min": 0,
                    "max": 65535,
                    "value": int(d.get("pair1") or 0),
                },
                {
                    "key": "useAltPath",
                    "label": "use_alt_path (row[+4].4)",
                    "type": "int",
                    "min": 0,
                    "max": 1,
                    "value": 1 if d.get("useAltPath") else 0,
                },
                {
                    "key": "aux9",
                    "label": "aux9 (push)",
                    "type": "int",
                    "min": 0,
                    "max": 255,
                    "value": int(d.get("aux9") or 0),
                },
                {
                    "key": "auxA",
                    "label": "auxA (push)",
                    "type": "int",
                    "min": 0,
                    "max": 255,
                    "value": int(d.get("auxA") or 0),
                },
                {
                    "key": "delayTicks",
                    "label": "delay",
                    "type": "int",
                    "min": 0,
                    "max": 255,
                    "value": int(d.get("delayTicks") or 0),
                },
                {
                    "key": "walkX",
                    "label": "walk_to X (current map)",
                    "type": "int",
                    "min": -32768,
                    "max": 32767,
                    "value": int(d.get("walkX") or 0),
                    "hint": "Party auto-steps here before +614D0. Packed at +0xF/+0x11.",
                },
                {
                    "key": "walkZ",
                    "label": "walk_to Z (current map)",
                    "type": "int",
                    "min": -32768,
                    "max": 32767,
                    "value": int(d.get("walkZ") or 0),
                    "hint": "Packed at +0x12/+0x13. Shop Parm door uses ~-1441.",
                },
            ]
        )
    fields.append(
        {
            "key": "rawHex",
            "label": "raw hex (full row)",
            "type": "hex",
            "value": row.raw.hex(),
            "hint": f"{row.row_size} bytes; overrides typed fields when set alone",
        }
    )
    return fields


def apply_fields_to_raw(
    raw: bytes,
    fields: dict[str, Any],
    *,
    handler_type: int,
    row_size: int,
) -> bytes:
    """Apply decoded / rawHex field updates onto a copy of ``raw``."""
    if row_size <= 0 or len(raw) < row_size:
        raise ValueError(f"row too short: have {len(raw)}, need {row_size}")
    out = bytearray(raw[:row_size])
    cur_hex = bytes(out).hex()

    raw_hex = fields.get("rawHex")
    if raw_hex is not None and str(raw_hex).strip() != "":
        hx = str(raw_hex).strip().replace(" ", "").lower().replace("0x", "")
        try:
            blob = bytes.fromhex(hx)
        except ValueError as exc:
            raise ValueError(f"invalid rawHex: {exc}") from exc
        if len(blob) != row_size:
            raise ValueError(f"rawHex length {len(blob)} != row size {row_size}")
        # Prefer hex when the user actually changed it vs the current row.
        if hx != cur_hex:
            return blob

    typed = {
        k: v
        for k, v in fields.items()
        if k != "rawHex" and v is not None and not (isinstance(v, str) and v.strip() == "")
    }
    kind = HANDLER_TYPE_NAMES.get(handler_type & 0x3F)

    if kind == "present_channel":
        if "channel" in typed:
            ch = int(typed["channel"])
            if not 0 <= ch <= 7:
                raise ValueError("channel must be 0..7")
            out[5] = (out[5] & ~0x07) | (ch & 7)
        if "delayCap" in typed:
            out[0x10] = int(typed["delayCap"]) & 0xFF
        if "followHookId" in typed:
            out[0x13] = int(typed["followHookId"]) & 0xFF

    elif kind == "hook_fanout":
        if "delayTicks" in typed:
            out[7] = int(typed["delayTicks"]) & 0xFF
        if "modeBits" in typed:
            out[8] = int(typed["modeBits"]) & 0xFF
        if "children" in typed:
            kids = typed["children"]
            if isinstance(kids, str):
                parts = [p.strip() for p in kids.replace(";", ",").split(",") if p.strip() != ""]
                kids = [int(p, 0) for p in parts]
            if not isinstance(kids, (list, tuple)):
                raise ValueError("children must be a list or comma-separated string")
            kids_list = list(kids)[:8]
            while len(kids_list) < 8:
                kids_list.append(0)
            for hid, off in zip(kids_list, _FANOUT_CHILD_OFFS):
                out[off] = int(hid) & 0xFF

    elif kind == "anim_latch":
        # Decode: latchMode=(raw[4]>>4)&3, anim=+5, unit=+6, delay=+0x10, follow=+0x13.
        if "animId" in typed:
            out[5] = int(typed["animId"]) & 0xFF
        if "unitKey" in typed:
            out[6] = int(typed["unitKey"]) & 0xFF
        if "latchMode" in typed:
            mode = int(typed["latchMode"]) & 3
            out[4] = (out[4] & ~(0x3 << 4)) | (mode << 4)
        if "delayTicks" in typed:
            out[0x10] = int(typed["delayTicks"]) & 0xFF
        if "followHookId" in typed:
            out[0x13] = int(typed["followHookId"]) & 0xFF

    elif kind == "setup":
        if "pair0" in typed:
            p0 = int(typed["pair0"]) & 0xFFFF
            out[5] = (p0 >> 8) & 0xFF
            out[6] = p0 & 0xFF
        if "pair1" in typed:
            p1 = int(typed["pair1"]) & 0xFFFF
            out[7] = (p1 >> 8) & 0xFF
            out[8] = p1 & 0xFF
        if "useAltPath" in typed:
            if int(typed["useAltPath"]):
                out[4] = out[4] | 0x10
            else:
                out[4] = out[4] & ~0x10
        if "aux9" in typed:
            out[9] = int(typed["aux9"]) & 0xFF
        if "auxA" in typed:
            out[0xA] = int(typed["auxA"]) & 0xFF
        if "delayTicks" in typed:
            out[0x10] = int(typed["delayTicks"]) & 0xFF
        if "walkX" in typed:
            wx = int(typed["walkX"]) & 0xFFFF
            out[0xF] = (wx >> 8) & 0xFF
            out[0x11] = wx & 0xFF
        if "walkZ" in typed:
            wz = int(typed["walkZ"]) & 0xFFFF
            out[0x12] = (wz >> 8) & 0xFF
            out[0x13] = wz & 0xFF

    elif typed:
        raise ValueError(
            f"no typed editors for handler 0x{handler_type:02X}; set rawHex instead"
        )
    elif raw_hex is None or str(raw_hex).strip() == "":
        raise ValueError("no hook fields to apply")

    return bytes(out)


def _u8(v: int) -> int:
    return int(v) & 0xFF


def _handler_byte(handler: int, flags1: int = 0) -> int:
    return ((int(flags1) & 3) << 6) | (int(handler) & 0x3F)


def _be_u16(v: int) -> tuple[int, int]:
    x = int(v) & 0xFFFF
    return (x >> 8) & 0xFF, x & 0xFF


def setup_gate_flags(raw: bytes) -> tuple[int, int]:
    """12-bit flag ids packed at +0xC/+0xD/+0xE for +0x53D80."""
    if len(raw) < 0xF:
        return 0, 0
    flag1 = ((raw[0xC] << 4) | (raw[0xD] >> 4)) & 0xFFF
    flag2 = (((raw[0xD] & 0xF) << 8) | raw[0xE]) & 0xFFF
    return flag1, flag2


def pack_setup_gate(flag1: int, flag2: int = 0) -> tuple[int, int, int]:
    flag1 &= 0xFFF
    flag2 &= 0xFFF
    c = (flag1 >> 4) & 0xFF
    d = ((flag1 & 0xF) << 4) | ((flag2 >> 8) & 0xF)
    e = flag2 & 0xFF
    return c, d, e


def build_setup_row(
    hook_id: int,
    *,
    pair0: int = 0,
    pair1: int = 0,
    row_flags: int = 0x40,
    aux9: int = 0,
    aux_a: int = 0,
    delay_ticks: int = 0,
    follow_hook_id: int = 0,
    walk_x: int = 0,
    walk_z: int = 0,
    flags1: int = 0,
    gate_flag1: int = 0,
    gate_flag2: int = 0,
    row_size: int = 20,
) -> bytes:
    if row_size != 20:
        raise ValueError("20-byte rows only")
    raw = bytearray(row_size)
    raw[0] = _u8(hook_id)
    raw[1] = _handler_byte(0x02, flags1)
    raw[4] = _u8(row_flags)
    raw[5], raw[6] = _be_u16(pair0)
    raw[7], raw[8] = _be_u16(pair1)
    raw[9] = _u8(aux9)
    raw[0xA] = _u8(aux_a)
    raw[0x10] = _u8(delay_ticks)
    if gate_flag1 or gate_flag2:
        raw[0xC], raw[0xD], raw[0xE] = pack_setup_gate(gate_flag1, gate_flag2)
    wx = int(walk_x) & 0xFFFF
    wz = int(walk_z) & 0xFFFF
    if wx or wz:
        raw[0xF] = (wx >> 8) & 0xFF
        raw[0x11] = wx & 0xFF
        raw[0x12] = (wz >> 8) & 0xFF
        raw[0x13] = wz & 0xFF
    else:
        raw[0x13] = _u8(follow_hook_id)
    return bytes(raw)


def build_setup_warp_row(
    hook_id: int,
    map_id: int,
    *,
    pair1: int = 0,
    row_flags: int = 0x40,
    aux9: int = 0,
    aux_a: int = 0,
    row_size: int = 20,
) -> bytes:
    """Build a 20-byte ``setup`` (0x02) row that calls map-travel ``+614D0(map_id)``.

    Note: SoftHD ch1 state5 uses ecx=map, edx=0, stack (0,0). Matching that.
    On BA38 intro this often black-screens — prefer present_channel + SoftHD
    FSM skip (``tools/patch_softhd_ch1_skip.py``) instead.
    """
    return build_setup_row(
        hook_id,
        pair0=map_id,
        pair1=pair1,
        row_flags=row_flags,
        aux9=aux9,
        aux_a=aux_a,
        row_size=row_size,
    )


def build_cutscene_row(
    hook_id: int,
    *,
    subtype: int = 0,
    param: int = 0,
    args: tuple[int, int, int, int] = (0, 0, 0, 0),
    row_flags: int | None = None,
    delay_ticks: int = 0,
    follow_hook_id: int = 0,
    flags1: int = 0,
    row_size: int = 20,
) -> bytes:
    """Build ``cutscene`` (0x1A). Subtype 4/6 is ``open_amap`` / ``open_amap2``."""
    if row_size != 20:
        raise ValueError("20-byte rows only")
    sub = int(subtype) & 0xF
    flags = sub if row_flags is None else int(row_flags)
    raw = bytearray(row_size)
    raw[0] = _u8(hook_id)
    raw[1] = _handler_byte(0x1A, flags1)
    raw[4] = _u8(flags)
    raw[5] = _u8(param)
    for i, val in enumerate(list(args)[:4]):
        raw[6 + i] = _u8(val)
    raw[0x10] = _u8(delay_ticks)
    raw[0x13] = _u8(follow_hook_id)
    return bytes(raw)


def build_present_channel_row(
    hook_id: int,
    *,
    channel: int = 1,
    delay_cap: int = 0,
    follow_hook_id: int = 0,
    row_flags: int = 0,
    flags1: int = 0,
    row_size: int = 20,
) -> bytes:
    """Build ``present_channel`` (0x01). SoftHD latch; ch1 drives intro→Parm FSM."""
    if row_size != 20:
        raise ValueError("20-byte rows only")
    ch = int(channel)
    if not 0 <= ch <= 7:
        raise ValueError("channel must be 0..7")
    raw = bytearray(row_size)
    raw[0] = _u8(hook_id)
    raw[1] = _handler_byte(0x01, flags1)
    raw[4] = _u8(row_flags)
    raw[5] = ch & 7
    raw[0x10] = _u8(delay_cap)
    raw[0x13] = _u8(follow_hook_id)
    return bytes(raw)


def build_hook_fanout_row(
    hook_id: int,
    children: list[int],
    *,
    delay_ticks: int = 0,
    mode_bits: int = 0,
    row_flags: int = 0x90,
    flags1: int = 0,
    row_size: int = 20,
) -> bytes:
    """Build ``hook_fanout`` (0x1C) with up to 8 child hook ids."""
    if row_size != 20:
        raise ValueError("20-byte rows only")
    kids = list(children)[:8]
    while len(kids) < 8:
        kids.append(0)
    raw = bytearray(row_size)
    raw[0] = _u8(hook_id)
    raw[1] = _handler_byte(0x1C, flags1)
    raw[4] = _u8(row_flags)
    raw[7] = _u8(delay_ticks)
    raw[8] = _u8(mode_bits)
    for hid, off in zip(kids, _FANOUT_CHILD_OFFS):
        raw[off] = _u8(hid)
    return bytes(raw)


def build_anim_latch_row(
    hook_id: int,
    *,
    anim_id: int = 1,
    unit_key: int = 0,
    latch_mode: int = 1,
    delay_ticks: int = 0,
    follow_hook_id: int = 0,
    slot_result: int = 0,
    row_flags: int = 0x90,
    flags1: int = 0,
    row_size: int = 20,
) -> bytes:
    if row_size != 20:
        raise ValueError("20-byte rows only")
    mode = int(latch_mode) & 3
    raw = bytearray(row_size)
    raw[0] = _u8(hook_id)
    raw[1] = _handler_byte(0x10, flags1)
    raw[4] = (_u8(row_flags) & ~(0x3 << 4)) | (mode << 4)
    raw[5] = _u8(anim_id)
    raw[6] = _u8(unit_key)
    raw[7] = _u8(slot_result)
    raw[0x10] = _u8(delay_ticks)
    raw[0x13] = _u8(follow_hook_id)
    return bytes(raw)


def build_sys_latch_row(
    hook_id: int,
    *,
    subtype: int = 0,
    param: int = 0,
    value: int = 0,
    row_flags: int | None = None,
    delay_ticks: int = 0,
    follow_hook_id: int = 0,
    flags1: int = 0,
    row_size: int = 20,
) -> bytes:
    if row_size != 20:
        raise ValueError("20-byte rows only")
    sub = int(subtype) & 0xF
    flags = (0x40 | sub) if row_flags is None else int(row_flags)
    raw = bytearray(row_size)
    raw[0] = _u8(hook_id)
    raw[1] = _handler_byte(0x1E, flags1)
    raw[4] = _u8(flags)
    raw[5] = _u8(param)
    raw[6] = _u8(value)
    raw[0x10] = _u8(delay_ticks)
    raw[0x13] = _u8(follow_hook_id)
    return bytes(raw)


def build_scripted_battle_row(
    hook_id: int,
    table: int,
    pairs: list[tuple[int, int]] | None = None,
    *,
    word: int = 0,
    row_flags: int | None = None,
    flags1: int = 0,
) -> bytes:
    """20-byte handler 0x19. pairs are (SpeciesIndex, Count) nibbles."""
    fields: dict[int, int] = {
        4: _u8(0x40 if row_flags is None else row_flags),
        5: _u8(table),
        6: (int(word) >> 8) & 0xFF,
        7: int(word) & 0xFF,
    }
    packed = list(pairs or [])
    if len(packed) > len(_SCRIPTED_BATTLE_PAIR_OFFS):
        raise ValueError(
            f"scripted_battle allows {len(_SCRIPTED_BATTLE_PAIR_OFFS)} pairs, got {len(packed)}"
        )
    for i, (idx, cnt) in enumerate(packed):
        if not 0 <= int(idx) <= 15 or not 0 <= int(cnt) <= 15:
            raise ValueError("scripted_battle pair index/count must be 0..15")
        fields[_SCRIPTED_BATTLE_PAIR_OFFS[i]] = ((int(idx) & 0xF) << 4) | (int(cnt) & 0xF)
    return build_typed_row(hook_id, 0x19, fields, flags1=flags1)


def build_typed_row(hook_id: int, handler: int, fields: dict[int, int], *, flags1: int = 0, row_size: int = 20) -> bytes:
    """20-byte row with explicit byte offsets. Unlisted bytes stay 0."""
    if row_size != 20:
        raise ValueError("20-byte rows only")
    raw = bytearray(row_size)
    raw[0] = _u8(hook_id)
    raw[1] = _handler_byte(handler, flags1)
    for off, val in fields.items():
        idx = int(off)
        if not 2 <= idx <= 19:
            raise ValueError(f"typed row offset {idx} out of range")
        raw[idx] = _u8(val)
    return bytes(raw)


def validate_hook_bundle(bundle: HookBundle) -> list[str]:
    """Return human-readable layout problems (empty list ⇒ OK to rewrite)."""
    errs: list[str] = []
    sec7 = bundle.sec7
    if len(sec7) < 0x18:
        return ["sec[7] shorter than header"]
    if len(sec7) > SEC7_HEAP_BUDGET:
        errs.append(
            f"sec[7] size 0x{len(sec7):X} exceeds heap copy budget 0x{SEC7_HEAP_BUDGET:X}"
        )
    counts = list(bundle.counts)
    rels = list(bundle.table_offsets)
    regions: list[tuple[int, int, int]] = []
    for ti, (cnt, rel, rs) in enumerate(zip(counts, rels, TABLE_ROW_SIZES)):
        if cnt == 0 or rs == 0:
            continue
        end = rel + cnt * rs
        if rel < 0x18:
            errs.append(f"table{ti} rel 0x{rel:X} overlaps header")
        if end > len(sec7):
            errs.append(f"table{ti} extends past sec[7] (end 0x{end:X} > 0x{len(sec7):X})")
            continue
        regions.append((ti, rel, end))
    regions.sort(key=lambda r: r[1])
    for a, b in zip(regions, regions[1:]):
        if a[2] > b[1]:
            errs.append(f"table{a[0]} overlaps table{b[0]}")
        elif a[2] != b[1]:
            # Gaps are OK but note them — rewriter packs tight.
            pass
    return errs


def emit_sec7_from_tables(
    *,
    flags: int,
    count0: int,
    opaque_prefix: bytes,
    tables: list[list[bytes]],
) -> bytes:
    """Rebuild a packed sec[7] blob from per-table raw row lists.

    ``opaque_prefix`` is ``sec7[0x18 : first_packed_table_rel]`` (preserves
    table0 / padding). Tables with ``TABLE_ROW_SIZES[ti] == 0`` must be empty lists.
    """
    if len(tables) != 4:
        raise ValueError("tables must be length 4")
    counts = [int(count0) & 0xFF, 0, 0, 0]
    for ti in (1, 2, 3):
        rs = TABLE_ROW_SIZES[ti]
        rows = tables[ti]
        for raw in rows:
            if len(raw) != rs:
                raise ValueError(f"table{ti} row length {len(raw)} != {rs}")
        counts[ti] = len(rows)
        if counts[ti] > 255:
            raise ValueError(f"table{ti} count {counts[ti]} exceeds u8")

    packing_start = 0x18 + len(opaque_prefix)
    rels = [0, 0, 0, 0]
    # Preserve original table0 rel if present in opaque region convention: 0x20.
    rels[0] = 0x20 if count0 else 0

    body = bytearray()
    cursor = packing_start
    for ti in (1, 2, 3):
        rs = TABLE_ROW_SIZES[ti]
        if not tables[ti]:
            rels[ti] = 0
            continue
        rels[ti] = cursor
        for raw in tables[ti]:
            body.extend(raw)
            cursor += rs

    out = bytearray(0x18 + len(opaque_prefix) + len(body))
    out[0] = flags & 0xFF
    for i, c in enumerate(counts):
        out[1 + i] = c & 0xFF
    for i, rel in enumerate(rels):
        struct.pack_into("<I", out, 8 + i * 4, rel)
    out[0x18 : 0x18 + len(opaque_prefix)] = opaque_prefix
    out[packing_start:] = body

    if len(out) > SEC7_HEAP_BUDGET:
        raise ValueError(
            f"rebuilt sec[7] 0x{len(out):X} exceeds heap budget 0x{SEC7_HEAP_BUDGET:X}"
        )
    return bytes(out)


def parse_sec7_tables(sec7: bytes) -> tuple[int, int, bytes, list[list[bytes]], tuple[int, int, int, int]]:
    """Split sec[7] into (flags, count0, opaque_prefix, tables[4], rels)."""
    if len(sec7) < 0x18:
        raise ValueError("sec[7] too short")
    flags = sec7[0]
    counts = list(sec7[1:5])
    rels = tuple(struct.unpack_from("<I", sec7, 8 + i * 4)[0] for i in range(4))
    tables: list[list[bytes]] = [[], [], [], []]
    packing_rels = [rels[ti] for ti in (1, 2, 3) if counts[ti] and TABLE_ROW_SIZES[ti]]
    packing_start = min(packing_rels) if packing_rels else len(sec7)
    if packing_start < 0x18:
        raise ValueError("invalid packing start")
    opaque = bytes(sec7[0x18:packing_start])
    for ti in (1, 2, 3):
        cnt, rel, rs = counts[ti], rels[ti], TABLE_ROW_SIZES[ti]
        if cnt == 0 or rs == 0:
            continue
        end = rel + cnt * rs
        if end > len(sec7):
            raise ValueError(f"table{ti} truncated")
        tables[ti] = [bytes(sec7[rel + i * rs : rel + (i + 1) * rs]) for i in range(cnt)]
    return flags, counts[0], opaque, tables, rels


def _row_from_sec7(sec7: bytes, table_index: int, row_index: int, row_size: int, sec7_offset: int) -> HookRow:
    raw = sec7[sec7_offset : sec7_offset + row_size]
    hid = _row_hook_id(raw, row_size)
    handler = raw[1] & 0x3F if len(raw) > 1 else 0
    return HookRow(
        hook_id=hid,
        handler_type=handler,
        table_index=table_index,
        row_index=row_index,
        row_size=row_size,
        raw=raw,
        sec7_offset=sec7_offset,
    )


def _summary_from_row(
    row: HookRow,
    map_stem: str,
    *,
    dirty: bool = False,
    bundle: HookBundle | None = None,
) -> dict[str, Any]:
    out = row.to_dict()
    out["found"] = True
    out["dirty"] = dirty
    note = KNOWN_HOOK_NOTES.get((map_stem.upper(), row.hook_id))
    if note:
        out["note"] = note
    out["editableFields"] = editable_fields_for_row(row)
    return _attach_hook_explain(out, row, bundle)


def next_free_hook_id(bundle: HookBundle, *, prefer_lo: int = 56, prefer_hi: int = 255) -> int:
    """Pick an unused single-byte hook id for table-2/3 custom rows."""
    used = {r.hook_id for r in bundle.rows if r.row_size == 20}
    for hid in range(prefer_lo, prefer_hi + 1):
        if hid not in used:
            return hid
    for hid in range(1, prefer_lo):
        if hid not in used:
            return hid
    raise RuntimeError("no free hook id in 1..255")


def _sec10_u32(sec10: bytes | None, offset: int) -> int | None:
    if not sec10 or len(sec10) < offset + 4:
        return None
    return struct.unpack_from("<I", sec10, offset)[0]


def _sec10_u16(sec10: bytes | None, offset: int) -> int | None:
    if not sec10 or len(sec10) < offset + 2:
        return None
    return struct.unpack_from("<H", sec10, offset)[0]


def _sec10_pan(sec10: bytes | None) -> bytes | None:
    if not sec10 or len(sec10) < SELECT_PAN_OFF + 4:
        return None
    return bytes(sec10[SELECT_PAN_OFF : SELECT_PAN_OFF + 4])


def _pan_logical_from_disk(raw: bytes) -> tuple[bool, bytes]:
    """Disk byte 0x94==0 means pan off; keep a non-zero west so re-enable restores limits."""
    enabled = raw[0] != 0
    west = raw[0] if enabled else 1
    return enabled, bytes([west, raw[1], raw[2], raw[3]])


class HookEditSession:
    """In-memory MDP editor: sec[7] hook rows plus sec[10] camera params."""

    def __init__(self, map_stem: str, field_root: Path | None = None) -> None:
        self.map_stem = map_stem.upper()
        self.field_root = field_root or find_field_dir()
        self.mdp_path = find_mdp(self.field_root, self.map_stem)
        self._mdp = self.mdp_path.read_bytes()
        sec7 = get_section(self._mdp, 7)
        if not sec7:
            raise RuntimeError(f"{self.map_stem}: no MDP sec[7]")
        self._flags, self._count0, self._opaque, self._tables, _rels = parse_sec7_tables(sec7)
        self._baseline_sec7 = sec7
        self._dirty = False
        self._load_sec10_params(get_section(self._mdp, 10))
        self._commit_sec10_baseline()

    def _load_sec10_params(self, sec10: bytes | None) -> None:
        self._camera_mode = sec10[4] if sec10 and len(sec10) > 4 else None
        self._cam_0c = _sec10_u32(sec10, 0x0C)
        self._cam_10 = _sec10_u32(sec10, 0x10)
        self._cam_18 = _sec10_u32(sec10, 0x18)
        self._proj_84 = _sec10_u16(sec10, 0x84)
        self._proj_86 = _sec10_u16(sec10, 0x86)
        self._proj_88 = _sec10_u16(sec10, 0x88)
        self._load_pan(sec10)

    def _commit_sec10_baseline(self) -> None:
        self._baseline_camera_mode = self._camera_mode
        self._baseline_cam_0c = self._cam_0c
        self._baseline_cam_10 = self._cam_10
        self._baseline_cam_18 = self._cam_18
        self._baseline_proj_84 = self._proj_84
        self._baseline_proj_86 = self._proj_86
        self._baseline_proj_88 = self._proj_88
        self._baseline_pan = self._disk_pan() if self._pan_logical is not None else None

    def _load_pan(self, sec10: bytes | None) -> None:
        raw = _sec10_pan(sec10)
        if raw is None:
            self._pan_enabled = None
            self._pan_logical = None
            return
        self._pan_enabled, self._pan_logical = _pan_logical_from_disk(raw)

    def _disk_pan(self) -> bytes | None:
        if self._pan_logical is None or self._pan_enabled is None:
            return None
        out = bytearray(self._pan_logical)
        if not self._pan_enabled:
            out[0] = 0
        return bytes(out)

    @property
    def dirty(self) -> bool:
        return self._dirty or self.map_params_dirty

    @property
    def camera_mode_dirty(self) -> bool:
        return self._camera_mode != self._baseline_camera_mode

    @property
    def cam_0c_dirty(self) -> bool:
        return self._cam_0c != self._baseline_cam_0c

    @property
    def cam_10_dirty(self) -> bool:
        return self._cam_10 != self._baseline_cam_10

    @property
    def cam_18_dirty(self) -> bool:
        return self._cam_18 != self._baseline_cam_18

    @property
    def proj_84_dirty(self) -> bool:
        return self._proj_84 != self._baseline_proj_84

    @property
    def proj_86_dirty(self) -> bool:
        return self._proj_86 != self._baseline_proj_86

    @property
    def proj_88_dirty(self) -> bool:
        return self._proj_88 != self._baseline_proj_88

    @property
    def pan_dirty(self) -> bool:
        disk = self._disk_pan()
        return disk is not None and disk != self._baseline_pan

    @property
    def map_params_dirty(self) -> bool:
        return (
            self.camera_mode_dirty
            or self.cam_0c_dirty
            or self.cam_10_dirty
            or self.cam_18_dirty
            or self.proj_84_dirty
            or self.proj_86_dirty
            or self.proj_88_dirty
            or self.pan_dirty
        )

    @property
    def camera_mode(self) -> int | None:
        return self._camera_mode

    def camera_mode_info(self) -> dict[str, Any]:
        mode = self._camera_mode
        decoded = (
            decode_select_pan_bytes(self._pan_logical)
            if self._pan_logical is not None
            else None
        )
        disk = self._disk_pan()
        return {
            "cameraMode": mode,
            "cameraModeLabel": CAMERA_MODE_LABELS.get(mode) if mode is not None else None,
            "cameraModeDirty": self.camera_mode_dirty,
            "cameraModeBaseline": self._baseline_camera_mode,
            "cam0c": self._cam_0c,
            "cam0cDirty": self.cam_0c_dirty,
            "cam0cBaseline": self._baseline_cam_0c,
            "cam10": self._cam_10,
            "cam10Dirty": self.cam_10_dirty,
            "cam10Baseline": self._baseline_cam_10,
            "cam18": self._cam_18,
            "cam18Dirty": self.cam_18_dirty,
            "cam18Baseline": self._baseline_cam_18,
            "proj84": self._proj_84,
            "proj84Dirty": self.proj_84_dirty,
            "proj84Baseline": self._baseline_proj_84,
            "proj86": self._proj_86,
            "proj86Dirty": self.proj_86_dirty,
            "proj86Baseline": self._baseline_proj_86,
            "proj88": self._proj_88,
            "proj88Dirty": self.proj_88_dirty,
            "proj88Baseline": self._baseline_proj_88,
            "panEnabled": self._pan_enabled,
            "panEnabledDirty": self.pan_dirty,
            "panXMin": None if decoded is None else decoded["xMin"],
            "panXMax": None if decoded is None else decoded["xMax"],
            "panZMin": None if decoded is None else decoded["zMin"],
            "panZMax": None if decoded is None else decoded["zMax"],
            "panBytes": None if disk is None else list(disk),
            "panLogicalBytes": None if self._pan_logical is None else list(self._pan_logical),
            "panDirty": self.pan_dirty,
            "panBaseline": None if self._baseline_pan is None else list(self._baseline_pan),
            "mapParamsDirty": self.map_params_dirty,
        }

    def set_camera_mode(self, mode: int) -> dict[str, Any]:
        if mode not in CAMERA_MODE_LABELS:
            raise ValueError("camera mode must be 0..3")
        if self._baseline_camera_mode is None and self._camera_mode is None:
            raise RuntimeError(f"{self.map_stem}: no MDP sec[10] camera mode byte")
        self._camera_mode = int(mode)
        return self.camera_mode_info()

    def set_cam_0c(self, value: int) -> dict[str, Any]:
        if not 0 <= int(value) <= 0xFFFFFFFF:
            raise ValueError("sec[10]+0x0C must be 0..0xFFFFFFFF")
        if self._cam_0c is None and self._baseline_cam_0c is None:
            raise RuntimeError(f"{self.map_stem}: no MDP sec[10]+0x0C")
        self._cam_0c = int(value)
        return self.camera_mode_info()

    def set_cam_10(self, value: int) -> dict[str, Any]:
        if not 0 <= int(value) <= 0xFFFFFFFF:
            raise ValueError("sec[10]+0x10 must be 0..0xFFFFFFFF")
        if self._cam_10 is None and self._baseline_cam_10 is None:
            raise RuntimeError(f"{self.map_stem}: no MDP sec[10]+0x10")
        self._cam_10 = int(value)
        return self.camera_mode_info()

    def set_cam_18(self, value: int) -> dict[str, Any]:
        if not 0 <= int(value) <= 0xFFFFFFFF:
            raise ValueError("sec[10]+0x18 must be 0..0xFFFFFFFF")
        if self._cam_18 is None and self._baseline_cam_18 is None:
            raise RuntimeError(f"{self.map_stem}: no MDP sec[10]+0x18")
        self._cam_18 = int(value)
        return self.camera_mode_info()

    def set_proj(self, offset: int, value: int) -> dict[str, Any]:
        if offset not in (0x84, 0x86, 0x88):
            raise ValueError("projection offset must be +0x84, +0x86, or +0x88")
        if not 0 <= int(value) <= 0xFFFF:
            raise ValueError(f"sec[10]+{offset:#x} must be 0..0xFFFF (0 = HD default)")
        attr = {0x84: "_proj_84", 0x86: "_proj_86", 0x88: "_proj_88"}[offset]
        base = {0x84: "_baseline_proj_84", 0x86: "_baseline_proj_86", 0x88: "_baseline_proj_88"}[
            offset
        ]
        if getattr(self, attr) is None and getattr(self, base) is None:
            raise RuntimeError(f"{self.map_stem}: no MDP sec[10]+{offset:#x}")
        setattr(self, attr, int(value))
        return self.camera_mode_info()

    def set_select_pan(
        self,
        *,
        enabled: bool | None = None,
        x_min: int | None = None,
        z_min: int | None = None,
        x_max: int | None = None,
        z_max: int | None = None,
    ) -> dict[str, Any]:
        if self._pan_logical is None and self._baseline_pan is None:
            raise RuntimeError(f"{self.map_stem}: no MDP sec[10]+0x94 select pan")
        cur = decode_select_pan_bytes(self._pan_logical or bytes([1, 0, 0xFF, 0xFF]))
        if enabled is not None:
            self._pan_enabled = bool(enabled)
        xmin = cur["xMin"] if x_min is None else int(x_min)
        zmin = cur["zMin"] if z_min is None else int(z_min)
        xmax = cur["xMax"] if x_max is None else int(x_max)
        zmax = cur["zMax"] if z_max is None else int(z_max)
        # Always encode with pan on so the west byte stays a real limit.
        self._pan_logical = encode_select_pan(xmin, zmin, xmax, zmax, enabled=True)
        if self._pan_enabled is None:
            self._pan_enabled = True
        return self.camera_mode_info()

    def set_map_params(
        self,
        *,
        camera_mode: int | None = None,
        cam_0c: int | None = None,
        cam_10: int | None = None,
        cam_18: int | None = None,
        proj_84: int | None = None,
        proj_86: int | None = None,
        proj_88: int | None = None,
        pan_enabled: bool | None = None,
        pan_x_min: int | None = None,
        pan_z_min: int | None = None,
        pan_x_max: int | None = None,
        pan_z_max: int | None = None,
    ) -> dict[str, Any]:
        if camera_mode is not None:
            self.set_camera_mode(camera_mode)
        if cam_0c is not None:
            self.set_cam_0c(cam_0c)
        if cam_10 is not None:
            self.set_cam_10(cam_10)
        if cam_18 is not None:
            self.set_cam_18(cam_18)
        if proj_84 is not None:
            self.set_proj(0x84, proj_84)
        if proj_86 is not None:
            self.set_proj(0x86, proj_86)
        if proj_88 is not None:
            self.set_proj(0x88, proj_88)
        if any(
            v is not None
            for v in (pan_enabled, pan_x_min, pan_z_min, pan_x_max, pan_z_max)
        ):
            self.set_select_pan(
                enabled=pan_enabled,
                x_min=pan_x_min,
                z_min=pan_z_min,
                x_max=pan_x_max,
                z_max=pan_z_max,
            )
        return self.camera_mode_info()

    @property
    def patch_count(self) -> int:
        # Compatibility with browser status text.
        n = (
            int(self.camera_mode_dirty)
            +             int(self.cam_0c_dirty)
            + int(self.cam_10_dirty)
            + int(self.cam_18_dirty)
            + int(self.proj_84_dirty)
            + int(self.proj_86_dirty)
            + int(self.proj_88_dirty)
            + int(self.pan_dirty)
        )
        if not self._dirty:
            return n
        cur = self.emit_sec7()
        if cur == self._baseline_sec7:
            return n
        # Count changed / inserted rows roughly.
        rows = 0
        old = parse_sec7_tables(self._baseline_sec7)[3]
        for ti in (1, 2, 3):
            o, nlst = old[ti], self._tables[ti]
            if len(nlst) != len(o):
                rows += abs(len(nlst) - len(o))
            for a, b in zip(o, nlst):
                if a != b:
                    rows += 1
        return n + max(rows, 1)

    def table2_rows(self) -> list[bytes]:
        return [bytes(r) for r in self._tables[2]]

    def table1_rows(self) -> list[bytes]:
        return [bytes(r) for r in self._tables[1]]

    def apply_table1_rows(self, raw_rows: list[bytes]) -> dict[str, Any]:
        """Replace table 1 only. Same bytes as current is a no-op."""
        new_rows: list[bytes] = []
        for raw in raw_rows:
            row = bytes(raw)
            if len(row) != 32:
                raise ValueError(f"table-1 row length {len(row)} != 32")
            new_rows.append(row)
        if new_rows == list(self._tables[1]):
            n = len(new_rows) * 32
            return {"unchanged": True, "beforeBytes": n, "afterBytes": n}
        before = len(self._tables[1]) * 32
        self._tables[1] = new_rows
        self._dirty = True
        _ = self.emit_sec7()
        return {
            "unchanged": False,
            "beforeBytes": before,
            "afterBytes": len(new_rows) * 32,
        }

    def apply_table2_rows(self, raw_rows: list[bytes]) -> dict[str, Any]:
        """Replace table 2 only. Same bytes as current is a no-op."""
        new_rows: list[bytes] = []
        for raw in raw_rows:
            row = bytes(raw)
            if len(row) != 20:
                raise ValueError(f"table-2 row length {len(row)} != 20")
            new_rows.append(row)
        if new_rows == list(self._tables[2]):
            n = len(new_rows) * 20
            return {"unchanged": True, "beforeBytes": n, "afterBytes": n}
        before = len(self._tables[2]) * 20
        self._tables[2] = new_rows
        self._dirty = True
        _ = self.emit_sec7()
        return {
            "unchanged": False,
            "beforeBytes": before,
            "afterBytes": len(new_rows) * 20,
        }

    def hook_asm_text(self) -> str:
        from field_hook_asm import format_hook_asm

        return format_hook_asm(self.table1_rows(), self.table2_rows(), map_stem=self.map_stem)

    def apply_hook_asm(self, text: str) -> dict[str, Any]:
        from field_hook_asm import AsmError, parse_hook_asm_text

        if not isinstance(text, str):
            raise ValueError("assembler text must be a string")
        try:
            tables = parse_hook_asm_text(text, map_stem=self.map_stem)
        except AsmError as exc:
            raise ValueError(str(exc)) from exc
        unchanged = True
        before = 0
        after = 0
        if 1 in tables:
            result = self.apply_table1_rows(tables[1])
            unchanged = unchanged and result["unchanged"]
            before += result["beforeBytes"]
            after += result["afterBytes"]
        if 2 in tables:
            result = self.apply_table2_rows(tables[2])
            unchanged = unchanged and result["unchanged"]
            before += result["beforeBytes"]
            after += result["afterBytes"]
        return {
            "unchanged": unchanged,
            "beforeBytes": before,
            "afterBytes": after,
            "tables": sorted(tables),
        }

    def emit_sec7(self) -> bytes:
        return emit_sec7_from_tables(
            flags=self._flags,
            count0=self._count0,
            opaque_prefix=self._opaque,
            tables=self._tables,
        )

    def _sec7_view(self) -> bytes:
        return self.emit_sec7()

    def bundle(self) -> HookBundle:
        sec7 = self.emit_sec7()
        flags = sec7[0]
        counts = tuple(sec7[1:5])  # type: ignore[assignment]
        rels = tuple(struct.unpack_from("<I", sec7, 8 + i * 4)[0] for i in range(4))
        rows: list[HookRow] = []
        for ti, (cnt, rel, rs) in enumerate(zip(counts, rels, TABLE_ROW_SIZES)):
            if cnt == 0 or rs == 0:
                continue
            for ri in range(cnt):
                off = rel + ri * rs
                raw = sec7[off : off + rs]
                rows.append(
                    HookRow(
                        hook_id=_row_hook_id(raw, rs),
                        handler_type=raw[1] & 0x3F if len(raw) > 1 else 0,
                        table_index=ti,
                        row_index=ri,
                        row_size=rs,
                        raw=raw,
                        sec7_offset=off,
                    )
                )
        return HookBundle(
            map_stem=self.map_stem,
            flags=flags,
            counts=counts,  # type: ignore[arg-type]
            table_offsets=rels,
            rows=rows,
            sec7=sec7,
        )

    def summary(self, hook_id: int, *, mode: int | None = None) -> dict[str, Any]:
        row = self.bundle().best(hook_id, mode=mode)
        if row is None:
            return {"hookId": hook_id, "found": False}
        return _summary_from_row(row, self.map_stem, dirty=self._dirty, bundle=self.bundle())

    def set_row_raw(self, sec7_offset: int, new_raw: bytes) -> HookRow:
        b = self.bundle()
        match = next((r for r in b.rows if r.sec7_offset == sec7_offset), None)
        if match is None:
            raise LookupError(f"no hook row at sec[7]+0x{sec7_offset:X}")
        if len(new_raw) != match.row_size:
            raise ValueError(f"row size {len(new_raw)} != expected {match.row_size}")
        self._tables[match.table_index][match.row_index] = bytes(new_raw)
        self._dirty = True
        return self.bundle().rows[
            next(
                i
                for i, r in enumerate(self.bundle().rows)
                if r.table_index == match.table_index and r.row_index == match.row_index
            )
        ]

    def set_row_fields(
        self,
        hook_id: int,
        fields: dict[str, Any],
        *,
        table_index: int | None = None,
        sec7_offset: int | None = None,
    ) -> dict[str, Any]:
        b = self.bundle()
        if sec7_offset is not None:
            row = next((r for r in b.rows if r.sec7_offset == sec7_offset), None)
        else:
            row = b.best(hook_id, mode=table_index)
        if row is None:
            raise LookupError(f"hook {hook_id} not found in {self.map_stem} sec[7]")
        new_raw = apply_fields_to_raw(
            row.raw,
            fields,
            handler_type=row.handler_type,
            row_size=row.row_size,
        )
        self.set_row_raw(row.sec7_offset, new_raw)
        return self.summary(hook_id, mode=row.table_index)

    def convert_to_setup_warp(
        self,
        hook_id: int,
        map_id: int,
        *,
        pair1: int = 0,
        table_index: int | None = None,
        sec7_offset: int | None = None,
    ) -> dict[str, Any]:
        """Overwrite a hook row as ``setup`` map-travel. BA38 intro often black-screens."""
        b = self.bundle()
        if sec7_offset is not None:
            row = next((r for r in b.rows if r.sec7_offset == sec7_offset), None)
        else:
            row = b.best(hook_id, mode=table_index)
        if row is None:
            raise LookupError(f"hook {hook_id} not found in {self.map_stem} sec[7]")
        if row.row_size != 20:
            raise ValueError(f"hook {hook_id} row size {row.row_size} not supported")
        new_raw = build_setup_warp_row(row.hook_id, map_id, pair1=pair1, row_flags=0x40)
        self.set_row_raw(row.sec7_offset, new_raw)
        return self.summary(row.hook_id, mode=row.table_index)

    def insert_row(
        self,
        table_index: int,
        raw: bytes,
        *,
        at: int | None = None,
    ) -> HookRow:
        """Insert a raw row into table 1/2/3. Returns the new HookRow after rebuild."""
        if table_index not in (1, 2, 3):
            raise ValueError("only tables 1/2/3 are editable")
        rs = TABLE_ROW_SIZES[table_index]
        if len(raw) != rs:
            raise ValueError(f"row length {len(raw)} != table{table_index} size {rs}")
        rows = self._tables[table_index]
        idx = len(rows) if at is None else int(at)
        if idx < 0 or idx > len(rows):
            raise IndexError(f"insert index {idx} out of range for {len(rows)} rows")
        rows.insert(idx, bytes(raw))
        self._dirty = True
        # Validate rebuild fits budget.
        _ = self.emit_sec7()
        b = self.bundle()
        return next(r for r in b.rows if r.table_index == table_index and r.row_index == idx)

    def replace_row(self, table_index: int, row_index: int, raw: bytes) -> None:
        """Overwrite one packed row. Rebuild must still fit the heap budget."""
        if table_index not in (1, 2, 3):
            raise ValueError("only tables 1/2/3 are editable")
        rs = TABLE_ROW_SIZES[table_index]
        if len(raw) != rs:
            raise ValueError(f"row length {len(raw)} != table{table_index} size {rs}")
        rows = self._tables[table_index]
        if row_index < 0 or row_index >= len(rows):
            raise IndexError("row_index out of range")
        rows[row_index] = bytes(raw)
        self._dirty = True
        _ = self.emit_sec7()

    def delete_row(self, *, table_index: int, row_index: int) -> None:
        if table_index not in (1, 2, 3):
            raise ValueError("only tables 1/2/3 are editable")
        rows = self._tables[table_index]
        if row_index < 0 or row_index >= len(rows):
            raise IndexError("row_index out of range")
        del rows[row_index]
        self._dirty = True

    def insert_present_channel(
        self,
        *,
        hook_id: int | None = None,
        channel: int = 1,
        table_index: int = 2,
    ) -> dict[str, Any]:
        """Insert a new present_channel row (default SoftHD ch=1)."""
        b = self.bundle()
        hid = next_free_hook_id(b) if hook_id is None else int(hook_id)
        raw = build_present_channel_row(hid, channel=channel)
        row = self.insert_row(table_index, raw)
        return _summary_from_row(row, self.map_stem, dirty=True, bundle=self.bundle())

    def insert_setup_warp(
        self,
        map_id: int,
        *,
        hook_id: int | None = None,
        pair1: int = 0,
        table_index: int = 2,
    ) -> dict[str, Any]:
        """Insert a new ``setup`` map-travel row (does not overwrite an existing id)."""
        b = self.bundle()
        hid = next_free_hook_id(b) if hook_id is None else int(hook_id)
        if not 1 <= hid <= 255:
            raise ValueError("hook_id must be 1..255 for 20-byte rows")
        raw = build_setup_warp_row(hid, int(map_id) & 0xFFFF, pair1=pair1)
        row = self.insert_row(table_index, raw)
        return _summary_from_row(row, self.map_stem, dirty=True, bundle=self.bundle())

    def insert_fanout(
        self,
        children: list[int],
        *,
        hook_id: int | None = None,
        table_index: int = 2,
        row_flags: int = 0x90,
    ) -> dict[str, Any]:
        b = self.bundle()
        hid = next_free_hook_id(b) if hook_id is None else int(hook_id)
        raw = build_hook_fanout_row(hid, children, row_flags=row_flags)
        row = self.insert_row(table_index, raw)
        return _summary_from_row(row, self.map_stem, dirty=True, bundle=self.bundle())

    def duplicate_row(
        self,
        *,
        table_index: int,
        row_index: int,
        new_hook_id: int | None = None,
    ) -> dict[str, Any]:
        """Clone a row into the same table; optionally reassign hook id (byte0)."""
        rows = self._tables[table_index]
        if row_index < 0 or row_index >= len(rows):
            raise IndexError("row_index out of range")
        raw = bytearray(rows[row_index])
        hid = next_free_hook_id(self.bundle()) if new_hook_id is None else int(new_hook_id)
        if not 1 <= hid <= 255:
            raise ValueError("new_hook_id must be 1..255 for 20-byte rows")
        if TABLE_ROW_SIZES[table_index] == 20:
            raw[0] = hid & 0xFF
        row = self.insert_row(table_index, bytes(raw))
        return _summary_from_row(row, self.map_stem, dirty=True, bundle=self.bundle())

    def restore_ba38_hook25_fanout(self) -> dict[str, Any]:
        """Restore stock BA38 table-2 fanout raw for hook 25 (51+52 children)."""
        if self.map_stem != "BA38":
            raise ValueError("restore_ba38_hook25_fanout only applies to BA38")
        stock = bytes.fromhex(STOCK_BA38_HOOK25_FANOUT_HEX)
        b = self.bundle()
        row = b.best(25, mode=2)
        if row is None:
            # Insert if missing.
            self.insert_row(2, stock)
            return self.summary(25, mode=2)
        self.set_row_raw(row.sec7_offset, stock)
        return self.summary(25, mode=2)

    def insert_ba38_softhd_handoff(
        self,
        *,
        fanout_id: int | None = None,
        channel_id: int | None = None,
    ) -> dict[str, Any]:
        """Insert present_channel(ch=1) + fanout→that id (SoftHD-safe BA38 recipe).

        Returns the fanout hook summary. Drive from SCN with ``call_hook(fanout_id)``.
        Optional title skip: ``tools/patch_softhd_ch1_skip.py``.
        """
        b = self.bundle()
        ch_id = next_free_hook_id(b) if channel_id is None else int(channel_id)
        # Reserve fanout id after channel so next_free doesn't collide mid-insert.
        used = {r.hook_id for r in b.rows if r.row_size == 20} | {ch_id}
        if fanout_id is None:
            fo_id = next(
                i for i in range(56, 256) if i not in used
            )
        else:
            fo_id = int(fanout_id)
        self.insert_row(2, build_present_channel_row(ch_id, channel=1))
        fo = self.insert_row(
            2,
            build_hook_fanout_row(fo_id, [ch_id], row_flags=0x90),
        )
        out = _summary_from_row(fo, self.map_stem, dirty=True, bundle=self.bundle())
        out["childHookId"] = ch_id
        out["recipe"] = f"call_hook({fo_id}) → present_channel({ch_id}, ch=1)"
        return out

    def emit_mdp(self) -> bytes:
        data = self._mdp
        if self._dirty:
            data = replace_section(data, 7, self.emit_sec7())
        if self._camera_mode is not None:
            sec10 = get_section(data, 10)
            if not sec10 or len(sec10) < 5:
                raise RuntimeError(f"{self.map_stem}: no MDP sec[10] camera mode byte")
            if sec10[4] != self._camera_mode:
                data = patch_section_bytes(data, 10, 4, bytes([self._camera_mode]))
        if self._cam_0c is not None:
            sec10 = get_section(data, 10)
            if not sec10 or len(sec10) < 0x10:
                raise RuntimeError(f"{self.map_stem}: no MDP sec[10]+0x0C")
            if struct.unpack_from("<I", sec10, 0x0C)[0] != self._cam_0c:
                data = patch_section_bytes(data, 10, 0x0C, struct.pack("<I", self._cam_0c))
        if self._cam_10 is not None:
            sec10 = get_section(data, 10)
            if not sec10 or len(sec10) < 0x14:
                raise RuntimeError(f"{self.map_stem}: no MDP sec[10]+0x10")
            if struct.unpack_from("<I", sec10, 0x10)[0] != self._cam_10:
                data = patch_section_bytes(data, 10, 0x10, struct.pack("<I", self._cam_10))
        if self._cam_18 is not None:
            sec10 = get_section(data, 10)
            if not sec10 or len(sec10) < 0x1C:
                raise RuntimeError(f"{self.map_stem}: no MDP sec[10]+0x18")
            if struct.unpack_from("<I", sec10, 0x18)[0] != self._cam_18:
                data = patch_section_bytes(data, 10, 0x18, struct.pack("<I", self._cam_18))
        for off, value in (
            (0x84, self._proj_84),
            (0x86, self._proj_86),
            (0x88, self._proj_88),
        ):
            if value is None:
                continue
            sec10 = get_section(data, 10)
            if not sec10 or len(sec10) < off + 2:
                raise RuntimeError(f"{self.map_stem}: no MDP sec[10]+{off:#x}")
            if struct.unpack_from("<H", sec10, off)[0] != value:
                data = patch_section_bytes(data, 10, off, struct.pack("<H", value))
        disk_pan = self._disk_pan()
        if disk_pan is not None:
            sec10 = get_section(data, 10)
            if not sec10 or len(sec10) < SELECT_PAN_OFF + 4:
                raise RuntimeError(f"{self.map_stem}: no MDP sec[10]+0x94 select pan")
            if sec10[SELECT_PAN_OFF : SELECT_PAN_OFF + 4] != disk_pan:
                data = patch_section_bytes(data, 10, SELECT_PAN_OFF, disk_pan)
        return data

    def write_mdp(self, out_path: Path) -> Path:
        out_path = Path(out_path)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        data = pad_sec7_heap_copy(self.emit_mdp())
        out_path.write_bytes(data)
        self._mdp = data
        sec7 = get_section(self._mdp, 7)
        assert sec7
        self._flags, self._count0, self._opaque, self._tables, _ = parse_sec7_tables(sec7)
        self._baseline_sec7 = sec7
        self._dirty = False
        self._load_sec10_params(get_section(self._mdp, 10))
        self._commit_sec10_baseline()
        load_hook_bundle.cache_clear()
        return out_path

    def discard(self) -> None:
        sec7 = get_section(self._mdp, 7)
        if not sec7:
            raise RuntimeError(f"{self.map_stem}: no MDP sec[7]")
        self._flags, self._count0, self._opaque, self._tables, _ = parse_sec7_tables(sec7)
        self._baseline_sec7 = sec7
        self._dirty = False
        self._camera_mode = self._baseline_camera_mode
        self._cam_0c = self._baseline_cam_0c
        self._cam_10 = self._baseline_cam_10
        self._cam_18 = self._baseline_cam_18
        self._proj_84 = self._baseline_proj_84
        self._proj_86 = self._baseline_proj_86
        self._proj_88 = self._baseline_proj_88
        if self._baseline_pan is None:
            self._pan_enabled = None
            self._pan_logical = None
        else:
            self._pan_enabled, self._pan_logical = _pan_logical_from_disk(
                self._baseline_pan
            )

