#!/usr/bin/env python3
"""Build modloader/app.ico from modloader/icon.png (nearest-neighbor, several sizes)."""
from __future__ import annotations

import struct
from pathlib import Path

from PIL import Image

APP = Path(__file__).resolve().parents[1] / "modloader"
PNG = APP / "icon.png"
ICO = APP / "app.ico"
FALLBACK = Path(
    r"C:\Users\User\.cursor\projects\c-Users-User-Projects-Grandipelago-Grandipelago\assets"
    r"\c__Users_User_AppData_Roaming_Cursor_User_workspaceStorage_52b32c3b518fe26d7655169304b1d277_images_icon-c37f5e9f-953d-47bc-b2ae-121ce6df4e81.png"
)
SIZES = (16, 32, 40, 48, 64, 256)


def dib32(im: Image.Image) -> bytes:
    w, h = im.size
    pixels = im.convert("RGBA").load()
    xor = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = pixels[x, y]
            xor += struct.pack("BBBB", b, g, r, a)
    row_and = ((w + 31) // 32) * 4
    and_mask = bytes(row_and * h)
    header = struct.pack(
        "<IIIHHIIIIII",
        40,
        w,
        h * 2,
        1,
        32,
        0,
        len(xor),
        0,
        0,
        0,
        0,
    )
    return header + xor + and_mask


def write_ico(path: Path, frames: list[Image.Image]) -> None:
    blobs = [dib32(frame) for frame in frames]
    count = len(frames)
    offset = 6 + 16 * count
    entries = bytearray()
    payload = bytearray()
    for frame, blob in zip(frames, blobs, strict=True):
        w, h = frame.size
        entries += struct.pack(
            "<BBBBHHII",
            w if w < 256 else 0,
            h if h < 256 else 0,
            0,
            0,
            1,
            32,
            len(blob),
            offset,
        )
        payload += blob
        offset += len(blob)
    path.write_bytes(struct.pack("<HHH", 0, 1, count) + entries + payload)


def main() -> None:
    src_path = PNG if PNG.is_file() else FALLBACK
    src = Image.open(src_path).convert("RGBA")
    if src_path != PNG:
        PNG.parent.mkdir(parents=True, exist_ok=True)
        src.save(PNG)
    frames = [src.resize((size, size), Image.Resampling.NEAREST) for size in SIZES]
    write_ico(ICO, frames)
    print(f"wrote {PNG} ({PNG.stat().st_size} bytes)")
    print(f"wrote {ICO} ({ICO.stat().st_size} bytes) sizes={list(SIZES)}")


if __name__ == "__main__":
    main()
