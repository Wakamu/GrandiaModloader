"""Assembler aliases for flags, items, and unique hook kinds.

Pretty names are used only when the mapping is 1:1. Hex / decimal ids always
parse. Portrait ``[P:]`` stays numeric.
"""
from __future__ import annotations

import json
import re
import unicodedata
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path

_REPO = Path(__file__).resolve().parents[1]
_ITEM_NAMES_PATH = _REPO / "data" / "item_names.json"
_IDENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")

# Known story / VM flags. Prose lives in field_script_op_hints; these are
# assembler identifiers.
FLAG_NAMES: dict[int, str] = {
    0x0001: "intro_seen",
    0x009D: "intro_path_a",
    0x009E: "intro_path_b",
    0x03EA: "ba38_beat",
    0x03EB: "mullen_pursuit",
    0x03F6: "parm_title",
    0x080E: "box_lock",
    0x0816: "vm_lock",
    0x08F9: "cutscene_lock",
}


@dataclass(frozen=True)
class AsmNames:
    flag_id_to_name: dict[int, str]
    flag_name_to_id: dict[str, int]
    item_id_to_name: dict[int, str]
    item_name_to_id: dict[str, int]
    hook_id_to_name: dict[int, str]
    hook_name_to_id: dict[str, int]


def slugify(name: str) -> str | None:
    text = unicodedata.normalize("NFKD", name.strip())
    text = "".join(ch for ch in text if not unicodedata.combining(ch))
    text = text.replace("'", "").replace("\u2019", "")
    text = re.sub(r"[^A-Za-z0-9]+", "_", text).strip("_")
    if text and text[0].isdigit():
        text = "item_" + text
    if not text or not _IDENT.fullmatch(text):
        return None
    return text


def _invert_unique(id_to_name: dict[int, str]) -> dict[str, int]:
    counts: dict[str, int] = {}
    for name in id_to_name.values():
        counts[name] = counts.get(name, 0) + 1
    return {name: i for i, name in id_to_name.items() if counts.get(name) == 1}


@lru_cache(maxsize=1)
def _item_maps() -> tuple[dict[int, str], dict[str, int]]:
    id_to_name: dict[int, str] = {}
    if not _ITEM_NAMES_PATH.is_file():
        return {}, {}
    raw = json.loads(_ITEM_NAMES_PATH.read_text(encoding="utf-8"))
    for key, label in raw.items():
        try:
            item_id = int(key)
        except (TypeError, ValueError):
            continue
        if not isinstance(label, str) or label.strip() in {"", "---"}:
            continue
        slug = slugify(label)
        if slug is None:
            continue
        id_to_name[item_id] = slug
    name_to_id = _invert_unique(id_to_name)
    id_to_name = {i: n for i, n in id_to_name.items() if n in name_to_id}
    return id_to_name, name_to_id


@lru_cache(maxsize=1)
def _flag_maps() -> tuple[dict[int, str], dict[str, int]]:
    id_to_name = dict(FLAG_NAMES)
    name_to_id = _invert_unique(id_to_name)
    id_to_name = {i: n for i, n in id_to_name.items() if n in name_to_id}
    return id_to_name, name_to_id


@lru_cache(maxsize=64)
def _hook_maps(map_stem: str) -> tuple[dict[int, str], dict[str, int]]:
    from field_script_hooks import HANDLER_TYPE_NAMES, load_hook_bundle

    try:
        bundle = load_hook_bundle(map_stem)
    except (OSError, ValueError):
        return {}, {}
    if bundle is None:
        return {}, {}
    by_kind: dict[str, list[int]] = {}
    for row in bundle.rows:
        kind = row.decoded().get("kind") or HANDLER_TYPE_NAMES.get(row.handler_type)
        if not isinstance(kind, str) or not _IDENT.fullmatch(kind):
            continue
        ids = by_kind.setdefault(kind, [])
        if row.hook_id not in ids:
            ids.append(row.hook_id)
    name_to_id: dict[str, int] = {}
    id_to_name: dict[int, str] = {}
    for kind, ids in by_kind.items():
        if len(ids) == 1:
            hid = ids[0]
            name_to_id[kind] = hid
            id_to_name.setdefault(hid, kind)
            continue
        for hid in ids:
            alias = f"{kind}_{hid}"
            if not _IDENT.fullmatch(alias):
                continue
            name_to_id[alias] = hid
            id_to_name.setdefault(hid, alias)
    return id_to_name, name_to_id


@lru_cache(maxsize=64)
def load_asm_names(map_stem: str | None = None) -> AsmNames:
    flag_id_to_name, flag_name_to_id = _flag_maps()
    item_id_to_name, item_name_to_id = _item_maps()
    hook_id_to_name: dict[int, str] = {}
    hook_name_to_id: dict[str, int] = {}
    if map_stem:
        hook_id_to_name, hook_name_to_id = _hook_maps(map_stem.upper())
    return AsmNames(
        flag_id_to_name=flag_id_to_name,
        flag_name_to_id=flag_name_to_id,
        item_id_to_name=item_id_to_name,
        item_name_to_id=item_name_to_id,
        hook_id_to_name=hook_id_to_name,
        hook_name_to_id=hook_name_to_id,
    )


def format_flag(flag_id: int, names: AsmNames) -> str:
    return names.flag_id_to_name.get(flag_id) or f"0x{flag_id:04X}"


def format_item(item_id: int, names: AsmNames) -> str:
    return names.item_id_to_name.get(item_id) or f"0x{item_id:04X}"


def format_hook(hook_id: int, names: AsmNames) -> str:
    return names.hook_id_to_name.get(hook_id) or str(hook_id)


def resolve_named(text: str, name_to_id: dict[str, int], parse_int) -> int:
    if text in name_to_id:
        return name_to_id[text]
    return parse_int(text)
