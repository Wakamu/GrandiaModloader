"""
portrait_resolver.py — Conservative helpers for field dialogue portrait RE.

Confirmed discoveries
---------------------
fc*_faces__spriteinfo.bin format (SoftHD SPRIV):
  0x00-0x04   "SPRIV" magic
  0x05-0x06   u16 LE  version (2)
  0x07-0x08   u16 LE  sprite count  (N)
  0x09 ..     N × 8 bytes, each little-endian (x u16, y u16, w u16, h u16)
  remainder   animation sequence data + "END" sentinel (not decoded here)

An earlier parser read count as big-endian at offset 6 and xywh as
big-endian at offset 8. Rect 0 coincidentally survived (2,2,128,128) but
every later rect collapsed into a fake 640-wide "strip", so the viewer
always showed Justin's still.

Each rect is one 128×128 dialogue face. The post-header extra is the
original 1-based FC##.DAT face id. SoftHD spriteinfo packs only the
used slots (holes are 0xFFFFFFFF in the DAT offset table). The editor
indexes the original DAT (FC03 has 60 slots, 10 unused) so unused
faces can be selected; HD atlas art is used as a fallback for slots
that still have a blob. Header expr is the fallback when no extra is
present (often 0). Extras persist for every cue that shares that
header.

  Binary extras: 0x07 Justin smile, 0x0D/0x11/0x13 Sue, 0x18 Puffy,
  0x32/0x33 Justin hurt/grin, 0x08 Justin shout (Parm "I didn't trash it!").
  Printable extras 0x20-0x3C (space .. '<'): same 1-based original
  ids, stored as the first text byte so they survive the ASCII
  pipeline (BA38 intro: '0' → fc01[47] Baal; 2404 ':Operation' →
  fc03 original slot 57). Strip from displayed text.

FaceKey (09 0F u16) is lip-sync only and never selects the still.

A 0x08 textbox-replace with no following Header drops the still.
0x07 immediately after a header is a face extra, not end-of-payload.

BA38's map→FC resolver returns row 1 (fc01), not fc22. fc01 holds
Mullen 32-38, Leen 39-44, Baal 45-48 — Justin is only 0-10 / 49-50.

Parm-family maps (0x20xx / 0x2Cxx) load two packs: FC01 and FC02. Low
party faces (Justin 0–10, Sue/Puffy, …) are duplicated across both, so
either row is fine. Unique faces live in the later row — Lilly / town
NPCs in FC02. FC01's unique high slots are later-story characters
(Mullen 32–38, …) and are the wrong still on those maps.

b1=0x21 (party) still prefers the later row first (Sue/Puffy on FC02).
When the tiles differ, NPC stills also use that later / map-local row.

Important correction
--------------------
An earlier hypothesis treated MDP sec[14] as a face-slot table. Local RE notes
and existing helper scripts show sec[14] is actually a camera/view override
table. Pack choice is map-row + “do the tiles differ?”, not sec[14].
"""
from __future__ import annotations

import struct
from functools import lru_cache
from pathlib import Path
from typing import NamedTuple

try:
    from PIL import Image, ImageDraw
    _PIL_AVAILABLE = True
except ImportError:
    _PIL_AVAILABLE = False
    Image = None  # type: ignore[assignment]
    ImageDraw = None  # type: ignore[assignment]


class FaceRect(NamedTuple):
    x: int
    y: int
    w: int
    h: int


@lru_cache(maxsize=64)
def _load_fc_rects(fc_id: int, field_root: Path) -> list[FaceRect]:
    """Load sprite rects from fc{fc_id:02d}_faces__spriteinfo.bin."""
    path = field_root / f"fc{fc_id:02d}_faces__spriteinfo.bin"
    if not path.exists():
        return []
    data = path.read_bytes()
    if data[:5] != b"SPRIV":
        return []
    count = struct.unpack_from("<H", data, 7)[0]
    rects = []
    for i in range(count):
        x, y, w, h = struct.unpack_from("<HHHH", data, 9 + i * 8)
        rects.append(FaceRect(x, y, w, h))
    return rects


def extract_portrait_image(
    fc_id: int,
    rect: FaceRect,
    field_root: Path,
) -> "Image.Image | None":
    """Crop and return the portrait PIL Image, or None if unavailable."""
    if not _PIL_AVAILABLE:
        return None
    atlas_path = field_root / f"fc{fc_id:02d}_faces__atlas.png"
    if not atlas_path.exists():
        return None
    atlas = Image.open(atlas_path)
    W, H = atlas.size
    if rect.x + rect.w > W or rect.y + rect.h > H:
        return None
    return atlas.crop((rect.x, rect.y, rect.x + rect.w, rect.y + rect.h))


# Maximum dimension treated as a valid portrait rect.
# Rects wider than this are multi-frame animation references, not portrait tiles.
_MAX_PORTRAIT_DIM = 256

# Special b1 slot codes handled by the engine outside sec[14]:
#   0x21 = hero/party slot  (Justin + party, fc02 is a common set)
#   0x0B = item_notify slot (no face portrait — shows item sprite)
_SPECIAL_SLOTS: dict[int, str] = {
    0x21: "hero",
    0x0B: "item",
}

_MAP_GROUP_TO_FC_ROW: dict[int, int] = {
    0x24: 3,
    0x28: 31,
    0x30: 4,
    0x34: 32,
    0x38: 5,
    0x3C: 5,
    0x40: 33,
    0x48: 9,
    0x50: 8,
    0x54: 9,
    0x58: 9,
    0x5C: 5,
    0x60: 9,
    0x64: 10,
    0x68: 34,
    0x6C: 11,
    0x70: 10,
    0x74: 35,
    0x78: 12,
    0x80: 11,
    0x88: 12,
    0x8C: 12,
    0x90: 15,
    0x94: 16,
    0x96: 15,
    0x98: 17,
    0x9C: 19,
    0xA0: 18,
    0xA2: 24,
    0xA4: 19,
    0xA8: 20,
    0xAC: 19,
    0xB4: 19,
    0xB8: 21,
    0xBA: 22,
    0xBC: 20,
    0xC0: 20,
    0xC4: 20,
    0xC8: 20,
    0xCA: 23,
    0xCC: 23,
    0xD0: 21,
    0xD4: 25,
    0xD8: 26,
    0xDC: 27,
    0xE0: 27,
    0xE4: 28,
}


def map_face_rows(map_stem: str) -> list[int]:
    """
    Return candidate FC rows used by the field face system for a map id.

    This mirrors the proven structure of the EXE resolver at +0x13A0:
    - a handful of exact-map special cases
    - several high-byte family rules
    - a fallback lookup table keyed by the map id high byte

    Some branches depend on live runtime flags, so this helper returns a list
    of candidates rather than pretending to know one exact row in all cases.
    """
    try:
        mid = int(map_stem, 16)
    except ValueError:
        return []

    if mid == 0x2001:
        mid = 0x2000

    if mid in (0xBA10, 0xBA1A):
        return [22]
    if mid == 0xBA38:
        return [1]
    if mid == 0x7C20:
        return [30]
    if mid == 0x7C44:
        return [13]
    if mid == 0xA203:
        return [26]
    if mid == 0x4C24:
        return [7]

    if mid in (0x6801, 0x6802):
        mid = 0x6800
    elif mid == 0x7401:
        mid = 0x7400
    elif mid == 0x3C02:
        mid = 0x3C00

    hi = (mid >> 8) & 0xFF
    lo = mid & 0xFF

    if hi in (0x20, 0x2C):
        return [1, 2]
    if hi == 0x44:
        return [6]
    if hi == 0x4C:
        return [7, 8]
    if hi == 0x7C:
        return [14] if lo > 0x10 else [13]

    row = _MAP_GROUP_TO_FC_ROW.get(hi)
    return [row] if row is not None else []


# Overlay / page bytes that can sit next to a face extra.
# 0x08 is a face extra *after a header* (1-based 8 → Justin shout) and a
# still-drop when it appears with no following header.
# 0x03/0x05/0x07/0x12 after a header are 1-based face ids (not box-only).
_NOT_FACE_EXTRAS = {0x00, 0x02, 0x06}
_BOX_CONTROLS = {0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x12}

# Printable post-header extra: 1-based original face id encoded as ASCII.
# 0x20-0x3C covers space through '<' (FC03's highest original slot is 60).
PRINTABLE_FACE_EXTRA_MIN = 0x20
PRINTABLE_FACE_EXTRA_MAX = 0x3C
PRINTABLE_FACE_EXTRA_CHARS = "".join(
    chr(c) for c in range(PRINTABLE_FACE_EXTRA_MIN, PRINTABLE_FACE_EXTRA_MAX + 1)
)


def is_printable_face_extra(value: int) -> bool:
    return PRINTABLE_FACE_EXTRA_MIN <= int(value) <= PRINTABLE_FACE_EXTRA_MAX


def parse_fc_dat_offsets(data: bytes) -> list[int]:
    """Original FC##.DAT slot offsets. ``0xFFFFFFFF`` is an unused face.

    The file starts with an offset table; face blobs follow. The first
    in-range payload offset is the table size, so slot count is that
    offset / 4. FC03 has 60 slots with 10 holes; HD spriteinfo keeps the
    50 used faces in table order.
    """
    if len(data) < 8:
        return []
    i = 0
    limit = min(len(data), 512 * 4)
    while i + 4 <= limit:
        off = struct.unpack_from("<I", data, i)[0]
        if off != 0xFFFFFFFF and off % 4 == 0 and off >= i + 4 and off <= len(data):
            count = off // 4
            if 1 <= count <= 512 and count * 4 <= len(data):
                offs = [struct.unpack_from("<I", data, j * 4)[0] for j in range(count)]
                if all(e == 0xFFFFFFFF or off <= e < len(data) for e in offs):
                    return offs
        i += 4
    return []


def fc_dat_slot_maps(data: bytes) -> tuple[tuple[int | None, ...], tuple[int, ...]]:
    """Return ``(orig_to_packed, packed_to_orig)`` from an FC##.DAT blob."""
    offs = parse_fc_dat_offsets(data)
    orig_to_packed: list[int | None] = []
    packed_to_orig: list[int] = []
    for orig, off in enumerate(offs):
        if off == 0xFFFFFFFF:
            orig_to_packed.append(None)
        else:
            orig_to_packed.append(len(packed_to_orig))
            packed_to_orig.append(orig)
    return tuple(orig_to_packed), tuple(packed_to_orig)


def _fc_dat_path(fc_id: int, field_root: Path) -> Path | None:
    for name in (f"FC{fc_id:02d}.DAT", f"fc{fc_id:02d}.dat", f"FC{fc_id:02d}.dat"):
        path = field_root / name
        if path.exists():
            return path
    return None


@lru_cache(maxsize=64)
def _fc_slot_maps(fc_id: int, field_root: Path) -> tuple[tuple[int | None, ...], tuple[int, ...]] | None:
    path = _fc_dat_path(fc_id, field_root)
    if path is None:
        return None
    try:
        maps = fc_dat_slot_maps(path.read_bytes())
    except OSError:
        return None
    orig_to_packed, packed_to_orig = maps
    if not packed_to_orig:
        return None
    return orig_to_packed, packed_to_orig


def original_slot_to_packed(
    original_slot: int,
    fc_id: int,
    field_root: Path,
) -> int | None:
    """Map a 0-based original FC slot to the packed HD spriteinfo index."""
    maps = _fc_slot_maps(fc_id, field_root)
    if maps is None:
        return original_slot
    orig_to_packed, _packed_to_orig = maps
    if 0 <= original_slot < len(orig_to_packed):
        return orig_to_packed[original_slot]
    return None


def packed_index_to_original_slot(
    packed_index: int,
    fc_id: int,
    field_root: Path,
) -> int | None:
    """Map a packed HD spriteinfo index back to the original FC slot."""
    maps = _fc_slot_maps(fc_id, field_root)
    if maps is None:
        return packed_index
    _orig_to_packed, packed_to_orig = maps
    if 0 <= packed_index < len(packed_to_orig):
        return packed_to_orig[packed_index]
    return None


def _face_pack_ids(
    *,
    fc_id: int | None,
    map_stem: str | None,
    b1_slot: int | None,
) -> list[int]:
    if fc_id is not None:
        return [int(fc_id)]
    if map_stem:
        return face_row_candidates(map_stem, b1_slot)
    return []


def packed_face_index_to_extra(
    packed_index: int,
    *,
    fc_id: int | None = None,
    map_stem: str | None = None,
    b1_slot: int | None = None,
    field_root: Path | None = None,
) -> int:
    """1-based extra to write for an editor face index.

    Editor indices are original FC##.DAT slots, so the extra is index+1.
    ``fc_id`` / ``map_stem`` / ``field_root`` are kept for call-site
    compatibility; they are not used to pack through SoftHD holes.
    """
    return int(packed_index) + 1


def dialogue_face_index(
    *,
    header_expr: int | None,
    face_key: int | None,
    controls: list[int] | None,
    has_header: bool = False,
    fc_id: int | None = None,
    map_stem: str | None = None,
    b1_slot: int | None = None,
    field_root: Path | None = None,
) -> int | None:
    """Pick the original FC##.DAT face slot for one dialogue cue.

    The still is a post-header extra (original 1-based face id) when present:
    0x0D/0x11/0x13/0x18 Sue/Puffy, 0x32/0x33 Justin hurt/grin, 0x07 smile,
    0x08 shout, 0x20-0x3C ASCII prefixes (BA38 ``0``/``.``/``"``, 2404
    ``:`` / leading space). FaceKey is lip-sync and
    must not pick the still — cues that share one header use the same extra.
    Without a header there is no still (even if 0x08 / FaceKey remain).

    The returned index is the original DAT slot (extra-1), including unused
    holes, not the packed SoftHD spriteinfo index.
    """
    if not has_header:
        return None
    extras = [c for c in (controls or []) if c not in _NOT_FACE_EXTRAS]
    if extras:
        raw = extras[-1]
        if raw >= 1:
            return raw - 1
    return header_expr


# Justin's extra stills that are unique to the first Parm pack (FC01).
# Same indices in FC02 are town NPCs. Extras 0x32 / 0x33 → slots 49 / 50.
_FIRST_PACK_UNIQUE_SLOTS = frozenset({49, 50})


def face_row_candidates(map_stem: str, b1_slot: int | None) -> list[int]:
    """FC pack order for a textbox slot. Party slot prefers the later map row."""
    rows = map_face_rows(map_stem)
    if b1_slot == 0x21 and len(rows) > 1:
        return list(reversed(rows))
    return list(rows)


def _face_images_equal(a: object, b: object) -> bool:
    if a is None or b is None:
        return False
    try:
        if a.size != b.size:  # type: ignore[attr-defined]
            return False
        return a.tobytes() == b.tobytes()  # type: ignore[attr-defined]
    except AttributeError:
        return False


def resolve_map_face(
    map_stem: str,
    b1_slot: int | None,
    expr: int,
    field_root: Path,
    *,
    face_key: int | None = None,
) -> tuple[int, object] | None:
    """Pick (fc_row, image) for one still on this map.

    Duplicated party tiles can come from either pack. When the tiles
    differ, use the last ``map_face_rows`` entry (FC02 on Parm = Lilly).
    """
    rows = face_row_candidates(map_stem, b1_slot)
    if not rows:
        return None
    map_rows = map_face_rows(map_stem)
    local = map_rows[-1] if len(map_rows) > 1 else rows[0]

    usable: list[tuple[int, object]] = []
    unused_rows: list[int] = []
    for fc_id in rows:
        if is_unused_face_slot(fc_id, expr, field_root):
            unused_rows.append(fc_id)
            continue
        img = load_face_image(fc_id, expr, field_root, face_key=face_key)
        if img is None:
            continue
        if img.size[0] > _MAX_PORTRAIT_DIM or img.size[1] > _MAX_PORTRAIT_DIM:
            continue
        usable.append((fc_id, img))

    if not usable:
        if not unused_rows:
            return None
        fc_id = unused_rows[0]
        img = load_face_image(fc_id, expr, field_root, face_key=face_key)
        return (fc_id, img) if img is not None else None

    by_id = {fc: img for fc, img in usable}
    first_row = map_rows[0] if map_rows else usable[0][0]
    if expr in _FIRST_PACK_UNIQUE_SLOTS and first_row in by_id:
        return first_row, by_id[first_row]
    if len(usable) == 1:
        return usable[0]
    first_img = usable[0][1]
    if all(_face_images_equal(first_img, img) for _fc, img in usable[1:]):
        return usable[0]
    if local in by_id:
        return local, by_id[local]
    return usable[-1]


def _rect_kind(rect: FaceRect | None) -> str:
    if rect is None:
        return "out-of-range"
    if rect.w <= _MAX_PORTRAIT_DIM and rect.h <= _MAX_PORTRAIT_DIM:
        return "portrait-ref"
    return "anim-ref"


@lru_cache(maxsize=64)
def _fc_dat_offsets(fc_id: int, field_root: Path) -> tuple[int, ...] | None:
    path = _fc_dat_path(fc_id, field_root)
    if path is None:
        return None
    try:
        offs = parse_fc_dat_offsets(path.read_bytes())
    except OSError:
        return None
    return tuple(offs) if offs else None


def face_slot_count(fc_id: int, field_root: Path) -> int:
    """Original DAT slot count, else SoftHD spriteinfo count."""
    offs = _fc_dat_offsets(fc_id, field_root)
    if offs:
        return len(offs)
    return len(_load_fc_rects(fc_id, field_root))


def is_unused_face_slot(fc_id: int, slot: int, field_root: Path) -> bool:
    offs = _fc_dat_offsets(fc_id, field_root)
    if offs is None or not (0 <= slot < len(offs)):
        return False
    return offs[slot] == 0xFFFFFFFF


def _unused_face_image() -> "Image.Image | None":
    if not _PIL_AVAILABLE:
        return None
    img = Image.new("RGBA", (128, 128), (28, 32, 40, 255))
    draw = ImageDraw.Draw(img)
    draw.rectangle((8, 8, 119, 119), outline=(90, 98, 112, 255))
    draw.line((8, 8, 119, 119), fill=(90, 98, 112, 255))
    draw.line((119, 8, 8, 119), fill=(90, 98, 112, 255))
    return img


def load_face_image(
    fc_id: int,
    original_slot: int,
    field_root: Path,
    face_key: int | None = None,
) -> "Image.Image | None":
    """Portrait for an original FC slot: HD atlas if used, placeholder if unused."""
    if is_unused_face_slot(fc_id, original_slot, field_root):
        return _unused_face_image()
    packed = original_slot_to_packed(original_slot, fc_id, field_root)
    if packed is None:
        return None
    rects = _load_fc_rects(fc_id, field_root)
    if not (0 <= packed < len(rects)):
        return None
    img = extract_portrait_image(fc_id, rects[packed], field_root)
    return crop_face_tile(img, face_key=face_key)


@lru_cache(maxsize=64)
def expr_kind(expr: int, field_root: Path, sample_fc_id: int = 1) -> str:
    """
    Classify an expr index using a representative FC pack.

    This does NOT identify which FC pack a field line uses. It only tells us
    whether an expr usually points at a portrait-sized face tile or at one of
    the oversized animation/sequence references embedded in the spriteinfo.
    """
    n = face_slot_count(sample_fc_id, field_root)
    if expr < 0 or expr >= n:
        return "out-of-range"
    if is_unused_face_slot(sample_fc_id, expr, field_root):
        return "unused"
    rects = _load_fc_rects(sample_fc_id, field_root)
    packed = original_slot_to_packed(expr, sample_fc_id, field_root)
    if packed is None or not (0 <= packed < len(rects)):
        return "portrait-ref"
    return _rect_kind(rects[packed])


def crop_face_tile(img: "Image.Image | None", *, face_key: int | None = None):
    """If the atlas rect is a horizontal expression strip, crop one 128px face.

    Wide spriteinfo rects are N×128 talk frames packed left-to-right, not a
    single portrait. FaceKey high byte is *not* a reliable frame index (values
    like 0xDF), so default to the first tile unless hi is in ``0 .. n-1``.
    """
    if img is None:
        return None
    w, h = img.size
    if w <= _MAX_PORTRAIT_DIM and h <= _MAX_PORTRAIT_DIM:
        return img
    tile = h if h <= _MAX_PORTRAIT_DIM else _MAX_PORTRAIT_DIM
    if tile <= 0 or w < tile:
        return img
    n = max(1, w // tile)
    frame = 0
    if face_key is not None:
        hi = (int(face_key) >> 8) & 0xFF
        if hi < n:
            frame = hi
    x0 = frame * tile
    return img.crop((x0, 0, min(x0 + tile, w), min(tile, h)))


@lru_cache(maxsize=1)
def party_face_table(exe_path: Path) -> dict[int, dict[int, int]]:
    """
    Return the EXE costume table as {fc_row: {char_id: variant_byte}}.

    The table lives at RVA 0x201D2F and is used by menu/status/stash face
    loaders for party members. It is useful for original FC##.DAT research,
    but does not yet solve field-time NPC/party portrait binding.
    """
    data = exe_path.read_bytes()
    off = 0x201D2F
    rows: dict[int, dict[int, int]] = {}
    for row in range(0, 40):
        vals = data[off + row * 8 : off + row * 8 + 8]
        if len(vals) < 8:
            break
        rows[row] = {char_id + 1: vals[char_id] for char_id in range(8)}
    return rows


def portrait_label(
    map_stem: str,
    b1_slot: int | None,
    expr: int,
    field_root: Path,
) -> str:
    """
    Return a conservative human-readable label for embedding in the viewer.

    Special cases:
    - b1=0x21 (hero slot): party/global binding unresolved in field scripts
    - b1=0x0B (item_notify): not a face portrait
    - expr can still be classified as portrait-ref vs anim-ref
    """
    rows = map_face_rows(map_stem)
    sample = rows[0] if rows else 1
    row_hint = ""
    if rows:
        row_hint = " fc-row~" + "/".join(f"{row:02d}" for row in rows)
    kind = expr_kind(expr, field_root, sample_fc_id=sample)
    if b1_slot is None:
        return f"expr=0x{expr:02x} {kind}{row_hint}"
    special = _SPECIAL_SLOTS.get(b1_slot)
    if special == "item":
        return f"item-sprite expr=0x{expr:02x}{row_hint}"
    if special == "hero":
        return f"hero-slot expr=0x{expr:02x} {kind}{row_hint}"
    return f"npc-slot expr=0x{expr:02x} {kind}{row_hint}"


def face_png_bytes(fc_id: int, expr: int, field_root: Path) -> bytes | None:
    """Return a PNG of one face tile (original DAT slot), or None."""
    if not _PIL_AVAILABLE:
        return None
    img = load_face_image(fc_id, expr, field_root)
    if img is None:
        return None
    import io
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def map_face_catalog(map_stem: str, field_root: Path) -> list[dict[str, int | list[int]]]:
    """FC packs used by this map, with original DAT slot counts for the picker."""
    seen: set[int] = set()
    rows: list[dict[str, int | list[int]]] = []
    for fc_id in map_face_rows(map_stem):
        if fc_id in seen:
            continue
        seen.add(fc_id)
        n = face_slot_count(fc_id, field_root)
        if not n:
            continue
        unused = [
            i for i in range(n)
            if is_unused_face_slot(fc_id, i, field_root)
        ]
        rows.append({"fcRow": fc_id, "count": n, "unused": unused})
    return rows
