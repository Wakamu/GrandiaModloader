"""Shared MDP (Grandia field map) header + section helpers.

On-disk layout is the PS1 FIELD/*.mdp table: 512-byte header of 64×
{u32 ptr, u32 size}, little-endian. HD Remaster streams the file into the
field heap unchanged (see docs/mdp_load_re.md).
"""
from __future__ import annotations

import math
import struct
from dataclasses import dataclass
from pathlib import Path

N_SECTIONS = 64
HEADER_SIZE = 512
SIZE_TO_EOF = 0xFFFFFFFF
PS1_OFF_MASK = 0xFFFFFF  # exe +0x54990: and eax, 0xffffff ; add eax, mdp_base
EMPTY_PTRS = {0, SIZE_TO_EOF}

# Working knowledge - keep in sync with docs/mdp_format.md.
# `bank` is (raw_ptr >> 20) when the slot is a relocated PS1 address.
SECTION_ROLES: dict[int, str] = {
    0: "PS1 bank 0x01 - Huffman 3D object pack (+0x9A60; relocated directory)",
    1: "PS1 bank 0x07 - nibble Huffman VRAM dump (+0x9630, left tpages at y=256)",
    2: "PS1 bank 0x03 - AMAP Huffman CLUT column (LoadImage RECT, e.g. 16x183 @ 752,40)",
    4: "PS1 bank 0x06 - Huffman 256x256 RGB555 field overlay (minimap / walk)",
    7: "chest loot (0x9300 records, table often at +0x1C0)",
    8: "48-byte field instances (kind nibble, talk id, xyz); runtime [0x71992C]",
    10: "map params (512B); map id @+0; cam mode @+4; default view xyz @+6; gold @+0xF0",
    11: "script directory (u16 id, u16 offset)*; HD bind then overwritten by TEXT/*.OFS",
    12: "script bank / scene instances (tag 0x204C chests); HD overwritten by TEXT/*.SCN",
    13: "PS1 bank 0x60 - rare extra (same bank as sec[4])",
    14: "12-byte camera/view overrides (key at +3, xyz at +4; fallback sec[10]+6)",
    15: "camera-path bytecode chunks (+0x7C3FC → IP 719934; walker +0x5B770)",
    16: "rare extra 9630 image; LoadImage RECT (752,17) 16x1 CLUT row",
    21: "2D polylines: u32 count + 12-byte {u16 type, u16 n, u32 off_a, u32 off_b}",
    23: "mesh chunk, magic 00 07 00 01",
    26: "field mesh / FT4 quads, magic 00 06 00 01",
    27: "9630 right-hand tpages (y=256) + uncompressed CLUT row (Parm y=239)",
    29: "small header in front of sec[26]",
    30: "56-byte actor table (pos, XZ box, talk ids +2..+5, sprite pair = sec[31] ids)",
    31: "66-byte actor motion scripts (33 u16; op=(word>>10)&0x1F; paired with 30)",
    32: "PS1 bank 0x01 - two UV/tpage+CLUT blocks (copied at +0x7271C)",
    33: "10-byte VRAM-unit UVs for sec[32] block 0 (PS1 leftover; HD skips copy)",
    34: "PS1 bank 0x01 - 6-byte UV triples for FT3 (prim[+0xD] 1-based)",
    35: "PS1 bank 0x01 - 8-byte lighting recs (4x RGB555 → RGBX at +0x8378D)",
    48: "NOT a file pointer - small scalar in the ptr slot (in-header)",
    49: "object stream (0x80 + event u16 + placement); size often 0xFFFFFFFF",
    50: "NOT a file pointer - scalar / buffer size in header slots",
    51: "NOT a file pointer - scalar / buffer size in header slots",
    52: "NOT a file pointer - scalar / buffer size in header slots",
    63: "overlay: same ptr as sec[10], size ~ rest-of-file (translation leftover)",
}

# Steam install - both casings appear in older tools.
_CONTENT_CANDIDATES = (
    Path(r"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content"),
    Path(r"C:\Program Files (x86)\Steam\steamapps\common\Grandia HD Remaster\content"),
)


@dataclass(frozen=True)
class MdpSection:
    index: int
    ptr: int
    size_raw: int
    kind: str  # empty | file | ps1 | scalar | unresolved
    file_off: int | None
    bank: int | None
    note: str = ""

    @property
    def occupied(self) -> bool:
        return self.kind in ("file", "ps1")

    @property
    def valid(self) -> bool:
        return self.occupied and self.file_off is not None

    @property
    def header_off(self) -> int:
        return self.index * 8


def find_content_root() -> Path:
    for p in _CONTENT_CANDIDATES:
        if (p / "FIELD").is_dir():
            return p
    raise FileNotFoundError("Grandia HD Remaster content/FIELD not found")


def find_field_dir() -> Path:
    return find_content_root() / "FIELD"


def find_exe() -> Path:
    root = find_content_root().parent
    exe = root / "grandia.exe"
    if exe.is_file():
        return exe
    raise FileNotFoundError(f"grandia.exe not next to {root}")


def find_mdp(field: Path, stem: str) -> Path:
    for name in (f"{stem}.mdp", f"{stem}.MDP", stem.upper() + ".mdp", stem.upper() + ".MDP"):
        p = field / name
        if p.is_file():
            return p
    raise FileNotFoundError(f"{stem}.mdp not under {field}")


def resolve_ptr(ptr: int, file_len: int) -> tuple[str, int | None, int | None]:
    """Classify a header pointer.

    Returns (kind, file_off, ps1_bank).
    `ps1` slots store a Saturn/PS1 destination in the high bits and a 20-bit
    file offset in the low bits - confirmed by tiling 2000.mdp and by the
    exe mask at +0x5B09C.
    """
    if ptr in EMPTY_PTRS:
        return "empty", None, None
    if HEADER_SIZE <= ptr < file_len:
        return "file", ptr, 0
    if ptr < HEADER_SIZE:
        return "scalar", ptr, None
    masked = ptr & PS1_OFF_MASK
    bank = ptr >> 24
    if HEADER_SIZE <= masked < file_len:
        return "ps1", masked, bank
    if masked < HEADER_SIZE:
        return "scalar", masked, bank
    return "unresolved", None, None


def parse_header(data: bytes) -> list[MdpSection]:
    if len(data) < HEADER_SIZE:
        raise ValueError(f"file shorter than header ({len(data)} < {HEADER_SIZE})")
    out: list[MdpSection] = []
    for i in range(N_SECTIONS):
        ptr, size = struct.unpack_from("<II", data, i * 8)
        kind, off, bank = resolve_ptr(ptr, len(data))
        note = kind
        if size == SIZE_TO_EOF:
            note = f"{kind}+size_to_eof"
        elif size == 0 and kind != "empty":
            note = f"{kind}+size_zero"
        out.append(
            MdpSection(
                index=i,
                ptr=ptr,
                size_raw=size,
                kind=kind,
                file_off=off,
                bank=bank,
                note=note,
            )
        )
    return out


def _effective_size(sec: MdpSection, file_len: int, next_off: int | None) -> int | None:
    if sec.file_off is None:
        return None
    if sec.size_raw not in EMPTY_PTRS and sec.size_raw and sec.file_off + sec.size_raw <= file_len:
        return sec.size_raw
    if next_off is not None and next_off > sec.file_off:
        return next_off - sec.file_off
    return file_len - sec.file_off


def section_span(sec: MdpSection, file_len: int, next_off: int | None = None) -> tuple[int, int] | None:
    """Return [start, end) in file for extractable blobs (file/ps1 only)."""
    if not sec.valid:
        return None
    size = _effective_size(sec, file_len, next_off)
    if not size:
        return None
    return (sec.file_off, sec.file_off + size)  # type: ignore[operator]


def next_offsets(secs: list[MdpSection]) -> dict[int, int]:
    offs = sorted({s.file_off for s in secs if s.file_off is not None})
    nxt: dict[int, int] = {}
    for a, b in zip(offs, offs[1:]):
        nxt[a] = b
    return nxt


def get_section(data: bytes, idx: int) -> bytes | None:
    secs = parse_header(data)
    nxt = next_offsets(secs)
    span = section_span(secs[idx], len(data), nxt.get(secs[idx].file_off or -1))
    if span is None:
        return None
    return data[span[0] : span[1]]


def section_file_span(data: bytes, idx: int) -> tuple[int, int] | None:
    """Return absolute ``[start, end)`` file offsets for section ``idx``, if extractable."""
    secs = parse_header(data)
    nxt = next_offsets(secs)
    return section_span(secs[idx], len(data), nxt.get(secs[idx].file_off or -1))


def patch_section_bytes(data: bytes, idx: int, rel_off: int, patch: bytes) -> bytes:
    """Return a copy of ``data`` with ``patch`` written at ``section[idx] + rel_off``.

    Section size is unchanged (in-place overwrite only). Raises if the patch
    would write past the section end.
    """
    span = section_file_span(data, idx)
    if span is None:
        raise ValueError(f"mdp section {idx} is not an extractable file blob")
    start, end = span
    if rel_off < 0 or rel_off + len(patch) > (end - start):
        raise ValueError(
            f"patch [{rel_off}:{rel_off + len(patch)}] out of range for "
            f"section {idx} size {end - start}"
        )
    out = bytearray(data)
    abs_off = start + rel_off
    out[abs_off : abs_off + len(patch)] = patch
    return bytes(out)


def _retarget_ptr(ptr: int, kind: str, new_off: int) -> int:
    if kind == "ps1":
        return (ptr & ~PS1_OFF_MASK) | (new_off & PS1_OFF_MASK)
    return new_off


def replace_section(data: bytes, idx: int, new_blob: bytes) -> bytes:
    """Replace section ``idx`` contents.

    Same size → in-place overwrite (keeps file layout).
    Shorter → in-place overwrite, zero-padded to the old span.
    Longer → splice at the old section end and bump later header pointers
    so this section's ``ptr`` stays put. Do not append at EOF: map load
    copies ``0x4000`` bytes from sec[7]'s file pointer (+0x5489B), and a
    retargeted EOF pointer is past a size-capped read (garbage table-2).
    """
    secs = parse_header(data)
    if idx < 0 or idx >= len(secs):
        raise IndexError(f"section index {idx} out of range")
    sec = secs[idx]
    span = section_file_span(data, idx)
    if span is None or sec.kind not in ("file", "ps1"):
        raise ValueError(f"mdp section {idx} is not a replaceable file blob")
    start, end = span
    old_size = end - start
    if len(new_blob) < old_size:
        new_blob = new_blob + bytes(old_size - len(new_blob))
    if len(new_blob) == old_size:
        out = bytearray(data)
        out[start:end] = new_blob
        return bytes(out)

    delta = len(new_blob) - old_size
    out = bytearray(data)
    out[start:end] = new_blob
    struct.pack_into("<I", out, sec.header_off + 4, len(new_blob))
    for other in secs:
        if other.index == idx or other.file_off is None:
            continue
        if other.file_off >= end:
            struct.pack_into(
                "<I",
                out,
                other.header_off,
                _retarget_ptr(other.ptr, other.kind, other.file_off + delta),
            )
    return pad_sec7_heap_copy(bytes(out))


def retarget_section(
    data: bytes,
    idx: int,
    blob: bytes,
    *,
    kind: str | None = None,
    bank: int | None = None,
) -> bytes:
    """Point slot ``idx`` at a new EOF blob. Other file offsets stay put.

    Use this when the new payload is longer (or a different encoding) and
    ``replace_section`` would shift dest-cam sections. The old span is
    abandoned in place.
    """
    secs = parse_header(data)
    if idx < 0 or idx >= len(secs):
        raise IndexError(f"section index {idx} out of range")
    sec = secs[idx]
    use_kind = kind or (sec.kind if sec.kind in ("file", "ps1") else "file")
    use_bank = PS1_BANKS.get(idx, 0) if bank is None else bank
    if sec.bank:
        use_bank = bank if bank is not None else sec.bank
    off = len(data)
    if use_kind == "ps1":
        ptr = ((use_bank & 0xFF) << 24) | (off & PS1_OFF_MASK)
    else:
        ptr = off
    out = bytearray(data)
    struct.pack_into("<I", out, idx * 8, ptr)
    struct.pack_into("<I", out, idx * 8 + 4, len(blob))
    out.extend(blob)
    return pad_sec7_heap_copy(bytes(out))


def append_section(data: bytes, idx: int, blob: bytes, *, kind: str = "file", bank: int = 0) -> bytes:
    """Write ``blob`` at EOF and point header slot ``idx`` at it.

    Occupied slots use ``replace_section`` (no move). Empty slots stay
    empty in the rest of the header so dest-cam file offsets do not shift.
    """
    secs = parse_header(data)
    if idx < 0 or idx >= len(secs):
        raise IndexError(f"section index {idx} out of range")
    sec = secs[idx]
    if sec.kind in ("file", "ps1"):
        return replace_section(data, idx, blob)
    if sec.kind not in ("empty",):
        raise ValueError(f"mdp section {idx} is {sec.kind}, not an empty slot")
    return retarget_section(data, idx, blob, kind=kind, bank=bank)


def magic8(blob: bytes) -> str:
    return blob[:8].hex() if blob else ""


# PS1 bank byte (ptr >> 24) used by +0x54990. Fixed per section index on stock maps.
PS1_BANKS: dict[int, int] = {
    0: 0x01,
    1: 0x07,
    2: 0x03,
    4: 0x06,
    13: 0x06,
    32: 0x01,
    33: 0x01,
    34: 0x01,
    35: 0x01,
}

# sec[63] is a translation leftover: same file pointer as sec[10].
DEFAULT_OVERLAYS: dict[int, int] = {63: 10}


@dataclass
class MdpBlob:
    """One unique on-disk payload to pack into an MDP."""

    index: int
    data: bytes
    kind: str  # file | ps1
    bank: int = 0
    size_raw: int = 0  # 0 → header size = len(data); SIZE_TO_EOF preserved


@dataclass
class MdpPack:
    """Typed MDP pieces: unique blobs + header scalars + overlay slots."""

    blobs: dict[int, MdpBlob]
    scalars: dict[int, tuple[int, int]]
    overlays: dict[int, int]
    order: list[int]


def unpack_mdp(data: bytes) -> MdpPack:
    """Split an MDP into unique section blobs (overlays recorded, not duplicated)."""
    secs = parse_header(data)
    blobs: dict[int, MdpBlob] = {}
    scalars: dict[int, tuple[int, int]] = {}
    overlays: dict[int, int] = {}
    off_owner: dict[int, int] = {}
    order_pairs: list[tuple[int, int]] = []
    for sec in secs:
        if sec.kind == "scalar":
            scalars[sec.index] = (sec.ptr, sec.size_raw)
            continue
        if not sec.occupied or sec.file_off is None:
            continue
        owner = off_owner.get(sec.file_off)
        if owner is not None:
            overlays[sec.index] = owner
            continue
        blob = get_section(data, sec.index)
        if blob is None:
            continue
        blobs[sec.index] = MdpBlob(
            index=sec.index,
            data=blob,
            kind=sec.kind,
            bank=sec.bank or 0,
            size_raw=sec.size_raw,
        )
        off_owner[sec.file_off] = sec.index
        order_pairs.append((sec.file_off, sec.index))
    order = [idx for _, idx in sorted(order_pairs)]
    return MdpPack(blobs=blobs, scalars=scalars, overlays=overlays, order=order)


def pack_mdp(pack: MdpPack) -> bytes:
    """Assemble a 512-byte header + tight-packed section blobs.

    sec[4] is forced to file offset 0x200 when present (stock convention).
    Overlay slots reuse the source section's pointer; size is ``file_len - off``.
    Unused header slots are ``{ptr=0xFFFFFFFF, size=0xFFFFFFFF}``.
    """
    order = list(pack.order) if pack.order else sorted(pack.blobs)
    if 4 in pack.blobs:
        order = [4] + [i for i in order if i != 4]
    seen: set[int] = set()
    unique: list[int] = []
    for idx in order:
        if idx in pack.blobs and idx not in seen:
            unique.append(idx)
            seen.add(idx)
    for idx in sorted(pack.blobs):
        if idx not in seen:
            unique.append(idx)
            seen.add(idx)

    body = bytearray()
    file_off: dict[int, int] = {}
    for idx in unique:
        blob = pack.blobs[idx]
        off = HEADER_SIZE + len(body)
        file_off[idx] = off
        body.extend(blob.data)

    file_len = HEADER_SIZE + len(body)
    header = bytearray()
    overlays = dict(DEFAULT_OVERLAYS)
    overlays.update(pack.overlays)

    for i in range(N_SECTIONS):
        if i in pack.blobs:
            blob = pack.blobs[i]
            off = file_off[i]
            size = len(blob.data)
            if blob.size_raw == SIZE_TO_EOF:
                size = SIZE_TO_EOF
            if blob.kind == "ps1":
                bank = blob.bank or PS1_BANKS.get(i, 0)
                ptr = ((bank & 0xFF) << 24) | (off & PS1_OFF_MASK)
            else:
                ptr = off
            header.extend(struct.pack("<II", ptr, size))
        elif i in pack.scalars:
            ptr, size = pack.scalars[i]
            header.extend(struct.pack("<II", ptr, size))
        elif i in overlays and overlays[i] in file_off:
            src = overlays[i]
            off = file_off[src]
            src_blob = pack.blobs[src]
            if src_blob.kind == "ps1":
                bank = src_blob.bank or PS1_BANKS.get(src, 0)
                ptr = ((bank & 0xFF) << 24) | (off & PS1_OFF_MASK)
            else:
                ptr = off
            size = file_len - off
            header.extend(struct.pack("<II", ptr, size))
        else:
            header.extend(struct.pack("<II", SIZE_TO_EOF, SIZE_TO_EOF))

    if len(header) != HEADER_SIZE:
        raise RuntimeError(f"header size {len(header)} != {HEADER_SIZE}")
    return bytes(header) + bytes(body)


SEC7_COPY_SIZE = 0x4000


def pad_sec7_heap_copy(data: bytes) -> bytes:
    """Append zeros so map load can copy 0x4000 bytes from sec[7]'s pointer.

    ``+0x5489B`` always copies that many bytes onto ``[0x63FA6C]``. A packed
    map with sec[7] at EOF (CC15 hub) is shorter than the copy window and
    the read runs off the heap mapping.
    """
    span = section_file_span(data, 7)
    if span is None:
        return data
    need = span[0] + SEC7_COPY_SIZE
    if len(data) >= need:
        return data
    return data + bytes(need - len(data))


CAMERA_MODE_LABELS = {
    0: "town / large outdoor (Select tilt)",
    1: "outdoor / dungeon",
    2: "interior (no Select tilt)",
    3: "special",
}


def patch_sec10_map_id(blob: bytes, map_id: int) -> bytes:
    """Rewrite sec[10]+0 (LE u16 map id). Blob must be the 512-byte params block."""
    if len(blob) < 2:
        raise ValueError("sec[10] too short to hold a map id")
    if not 0 <= map_id <= 0xFFFF:
        raise ValueError("map id must be 0..65535")
    out = bytearray(blob)
    struct.pack_into("<H", out, 0, map_id)
    return bytes(out)


def patch_sec10_camera_mode(blob: bytes, mode: int) -> bytes:
    """Rewrite sec[10]+4 camera mode (0/1/2/3)."""
    if len(blob) < 5:
        raise ValueError("sec[10] too short to hold camera mode")
    if mode not in CAMERA_MODE_LABELS:
        raise ValueError("camera mode must be 0..3")
    out = bytearray(blob)
    out[4] = mode
    return bytes(out)


def patch_sec10_u32(blob: bytes, offset: int, value: int) -> bytes:
    """Rewrite a little-endian u32 in the 512-byte sec[10] params block."""
    if len(blob) < offset + 4:
        raise ValueError(f"sec[10] too short for u32 at +{offset:#x}")
    if not 0 <= value <= 0xFFFFFFFF:
        raise ValueError("sec[10] u32 must be 0..0xFFFFFFFF")
    out = bytearray(blob)
    struct.pack_into("<I", out, offset, int(value))
    return bytes(out)


# Select pan AABB at sec[10]+0x94..+0x97. HD copies these to 713F44/3E/40/42
# and clamps Select camera X/Z. Byte 0x94 == 0 turns pan off (not a west wall
# of −2048). World units are 16-aligned: X=(b−128)<<4, Z=(128−b)<<4.
SELECT_PAN_OFF = 0x94
SELECT_PAN_X_MIN = -2048
SELECT_PAN_X_MAX = 2032
SELECT_PAN_Z_MIN = -2032
SELECT_PAN_Z_MAX = 2048
# Nearly full overlay with pan on (west cannot be −2048 while enabled).
SELECT_PAN_FULL = bytes([0x01, 0x00, 0xFF, 0xFF])


def _snap16(value: int) -> int:
    return int(round(int(value) / 16.0)) * 16


def cover_select_pan_aabb(
    x_min: int, z_min: int, x_max: int, z_max: int
) -> tuple[int, int, int, int]:
    """Expand world XZ to 16-unit pan steps so the box still covers the overlay."""
    xmin = math.floor(int(x_min) / 16.0) * 16
    zmin = math.floor(int(z_min) / 16.0) * 16
    xmax = math.ceil(int(x_max) / 16.0) * 16
    zmax = math.ceil(int(z_max) / 16.0) * 16
    return int(xmin), int(zmin), int(xmax), int(zmax)


def decode_select_pan_bytes(raw: bytes) -> dict:
    """Decode four pan bytes to world XZ. ``enabled`` is ``bytes[0] != 0``."""
    if len(raw) < 4:
        raise ValueError("select pan needs 4 bytes")
    b94, b95, b96, b97 = raw[0], raw[1], raw[2], raw[3]
    return {
        "enabled": b94 != 0,
        "xMin": (b94 - 128) << 4,
        "zMax": (128 - b95) << 4,
        "xMax": (b96 - 128) << 4,
        "zMin": (128 - b97) << 4,
        "bytes": [b94, b95, b96, b97],
    }


def encode_select_pan(
    x_min: int,
    z_min: int,
    x_max: int,
    z_max: int,
    *,
    enabled: bool = True,
) -> bytes:
    """Pack world XZ limits into sec[10]+0x94..+0x97.

    Snaps to 16-unit steps. While ``enabled``, west (x_min) of −2048 becomes
    −2032 so byte 0x94 is not the pan-off switch.
    """
    x_min = _snap16(x_min)
    x_max = _snap16(x_max)
    z_min = _snap16(z_min)
    z_max = _snap16(z_max)
    if x_min > x_max:
        x_min, x_max = x_max, x_min
    if z_min > z_max:
        z_min, z_max = z_max, z_min
    x_min = max(SELECT_PAN_X_MIN, min(SELECT_PAN_X_MAX, x_min))
    x_max = max(SELECT_PAN_X_MIN, min(SELECT_PAN_X_MAX, x_max))
    z_min = max(SELECT_PAN_Z_MIN, min(SELECT_PAN_Z_MAX, z_min))
    z_max = max(SELECT_PAN_Z_MIN, min(SELECT_PAN_Z_MAX, z_max))
    b94 = (x_min >> 4) + 128
    b96 = (x_max >> 4) + 128
    b95 = 128 - (z_max >> 4)
    b97 = 128 - (z_min >> 4)
    b94 = max(0, min(255, b94))
    b95 = max(0, min(255, b95))
    b96 = max(0, min(255, b96))
    b97 = max(0, min(255, b97))
    if not enabled:
        b94 = 0
    elif b94 == 0:
        b94 = 1
    return bytes([b94, b95, b96, b97])


def patch_sec10_select_pan(blob: bytes, pan: bytes) -> bytes:
    """Rewrite sec[10]+0x94..+0x97."""
    if len(blob) < SELECT_PAN_OFF + 4:
        raise ValueError("sec[10] too short for select pan")
    if len(pan) != 4:
        raise ValueError("select pan must be 4 bytes")
    out = bytearray(blob)
    out[SELECT_PAN_OFF : SELECT_PAN_OFF + 4] = pan
    return bytes(out)


def empty_sec8() -> bytes:
    """Zero field instances: packed count 0x4000, 2-byte pad (size = 4 + N*48)."""
    return struct.pack("<HH", 0x4000, 0)


def make_sec8_rec(
    x: int,
    y: int,
    z: int,
    *,
    kind: int = 2,
    talk: int = 0,
    flags: int = 0x80,
    xz: tuple[int, int, int, int] | None = None,
) -> bytes:
    """One 48-byte sec[8] instance."""
    rec = bytearray(48)
    struct.pack_into("<H", rec, 0, (kind & 0xF) << 8)
    rec[2] = flags & 0xFF
    rec[3] = talk & 0xFF
    struct.pack_into("<hhh", rec, 4, x, y, z)
    if xz is not None:
        struct.pack_into("<hhhh", rec, 0x12, *xz)
    return bytes(rec)


def pack_sec8(recs: list[bytes]) -> bytes:
    """Packed count + N×48 records + 2-byte pad."""
    body = b"".join(bytes(r)[:48].ljust(48, b"\x00") for r in recs)
    return struct.pack("<H", 0x4000 | len(recs)) + body + b"\x00\x00"


def spawn_sec8(x: int, y: int, z: int, *, kind: int = 2) -> bytes:
    """One 48-byte instance used as a debug-warp spawn / camera-facing hook.

    Kind 2 is the camera/facing handler (``+0x89B1E``) that syncs party spawn Y
    with the view. Packed id is ``(kind << 8)``. Size = 4 + 48.
    """
    return pack_sec8([make_sec8_rec(x, y, z, kind=kind)])


def append_sec8(blob: bytes, rec: bytes) -> bytes:
    """Append one 48-byte instance to an existing sec[8] blob."""
    data = blob or empty_sec8()
    n = struct.unpack_from("<H", data, 0)[0] & 0x3FFF if len(data) >= 2 else 0
    recs = [data[2 + i * 48 : 2 + (i + 1) * 48] for i in range(n)]
    recs.append(bytes(rec)[:48].ljust(48, b"\x00"))
    return pack_sec8(recs)


def empty_sec14() -> bytes:
    """Zero camera/view overrides: N=0 with 0x4000 flag (4 bytes)."""
    return struct.pack("<HH", 0x4000, 0)


def make_sec14_views(views: list[tuple[int, int, int, int]]) -> bytes:
    """Door/setup landing views. Each item is ``(key, x, y, z)``.

    Setup travel writes ``pair1`` to ``[0x71CD48]``. Dest load at ``+0x61640``
    looks that id up in sec[14] (``byte[+3]``). ``0xFFFF`` (debug warp) skips
    the table. Empty N=0 is a safe miss (falls back to sec[10]+6) but every
    stock door dest has at least key 1.

    Record 0 overlaps the packed count. ``word[+2]`` high byte duplicates the
    key; ``+A/+B`` is the stock ``0x1E 0x02`` trailer. Size is ``N*12+4``.
    """
    if not views:
        return empty_sec14()
    out = bytearray(len(views) * 12 + 4)
    struct.pack_into("<H", out, 0, 0x4000 | len(views))
    for i, (key, x, y, z) in enumerate(views):
        off = i * 12
        out[off + 2] = 0
        out[off + 3] = int(key) & 0xFF
        struct.pack_into("<hhh", out, off + 4, int(x), int(y), int(z))
        out[off + 0xA] = 0x1E
        out[off + 0xB] = 0x02
    return bytes(out)


def empty_sec21() -> bytes:
    """Zero polyline/collision table: u32 count = 0.

    Bank root CC00 keeps sec[21] bound if the submap omits the slot.
    """
    return struct.pack("<I", 0)


def empty_sec7_hooks(sec7: bytes) -> bytes:
    """Drop every hook row, not just the count bytes.

    Zeroing counts[1:5] left CC14's 14+4 setup/zone records in the payload
    (rels still 32/480/560). Loaders that walk rel..next_rel still see them.
    """
    del sec7
    out = bytearray(0x18)
    for i in range(4):
        struct.pack_into("<I", out, 8 + i * 4, 0x18)
    return bytes(out)


def stub_sec26() -> bytes:
    """Minimal FT4 mesh header so a previous map's sec[26] bind is replaced."""
    blob = bytearray(0x80)
    blob[0:4] = b"\x00\x06\x00\x01"
    return bytes(blob)


def classify_blob(blob: bytes) -> list[str]:
    """Cheap on-disk fingerprints - not a full type system."""
    tags: list[str] = []
    if not blob:
        return ["empty"]
    if blob[:4] == b"\x10\x00\x00\x00":
        tags.append("tim?")
    if blob[:4] == b"\x41\x00\x00\x00":
        tags.append("tmd?")
    if blob[:4] == b"\x00\x06\x00\x01":
        tags.append("sec26-magic")
    if blob[:4] == b"\x00\x07\x00\x01":
        tags.append("sec23-magic")
    if len(blob) >= 2 and struct.unpack_from("<H", blob, 0)[0] == 0x9300:
        tags.append("tag-9300")
    if len(blob) >= 2 and struct.unpack_from("<H", blob, 0)[0] == 0x204C:
        tags.append("tag-204C")
    if blob[:1] == b"\x80":
        tags.append("lead-80")
    if b"\x78\x9c" in blob[:0x40] or b"\x78\xda" in blob[:0x40] or b"\x78\x01" in blob[:0x40]:
        tags.append("zlib?")
    printable = sum(1 for b in blob[:64] if 32 <= b < 127)
    if printable >= 40:
        tags.append("ascii-ish")
    if blob[:2] == b"\xcd\xcd" or blob[4:6] == b"\xcd\xcd":
        tags.append("cd-pad")
    return tags or ["raw"]
