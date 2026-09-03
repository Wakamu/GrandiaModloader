"""Decompress MDP image sections with the AMAP Huffman codec (+0x9A60 / +0x9B10)."""
from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    Image = None  # type: ignore

sys.path.insert(0, str(Path(__file__).resolve().parent))
from decode_amap import BitReader, build_leaves, build_tree, decode  # noqa: E402
from mdp_lib import find_field_dir, find_mdp, get_section  # noqa: E402

REPO = Path(__file__).resolve().parents[1]
OUT = REPO / "data" / "mdp_extract"


def rgb555_to_rgb(u: int) -> tuple[int, int, int]:
    r = (u & 0x1F) << 3
    g = ((u >> 5) & 0x1F) << 3
    b = ((u >> 10) & 0x1F) << 3
    return (r, g, b)


def decompress_counted(data: bytes, verbose: bool = False) -> bytes:
    """Port of +0x9A60: BE u16 block count at [0], then that many Huffman frames."""
    if len(data) < 2:
        raise ValueError("too short")
    count = (data[0] << 8) | data[1]
    br = BitReader(data, 2)
    out = bytearray()
    for i in range(count):
        tree: list[list[int]] = [[0, 0, 0, 0] for _ in range(0x220)]
        mode = build_leaves(br, tree)
        root = build_tree(tree)
        chunk = decode(br, tree, root, mode, be=False)
        out += chunk
        if verbose:
            print(f"    block {i}/{count} mode={mode} -> {len(chunk)} bytes (stream@{br.pos})")
    return bytes(out)


def decompress_one(data: bytes, start: int = 0) -> tuple[bytes, int]:
    """Single Huffman frame (AMAP-style), used by sec[27]/[32] after a size header."""
    br = BitReader(data, start)
    tree: list[list[int]] = [[0, 0, 0, 0] for _ in range(0x220)]
    mode = build_leaves(br, tree)
    root = build_tree(tree)
    return decode(br, tree, root, mode, be=False), br.pos


def stitch_tiles(blob: bytes, tile_w: int, tile_h: int, dest: Path, cols: int = 1) -> None:
    """Linear RGB555 tiles (AMAP-style) stacked into a contact sheet."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    tile_bytes = tile_w * tile_h * 2
    if tile_bytes == 0 or len(blob) < tile_bytes:
        save_rgb555(blob, dest, tile_w)
        return
    n = len(blob) // tile_bytes
    rem = len(blob) % tile_bytes
    extra_h = (rem // (tile_w * 2)) if rem else 0
    rows = (n + cols - 1) // cols
    img = Image.new("RGB", (tile_w * cols, tile_h * rows + extra_h))
    px = img.load()
    for t in range(n):
        col, row = t % cols, t // cols
        base = t * tile_bytes
        for y in range(tile_h):
            for x in range(tile_w):
                u = struct.unpack_from("<H", blob, base + (y * tile_w + x) * 2)[0]
                px[col * tile_w + x, row * tile_h + y] = rgb555_to_rgb(u)
    img.save(dest)
    print(f"    stitch {dest.name}: {n} tiles {tile_w}x{tile_h} rem={rem}")


def save_rgb555(blob: bytes, path: Path, width: int) -> None:
    n = len(blob) // 2
    if n == 0 or width <= 0:
        return
    h = max(1, (n + width - 1) // width)
    img = Image.new("RGB", (width, h))
    px = img.load()
    for i in range(n):
        u = struct.unpack_from("<H", blob, i * 2)[0]
        px[i % width, i // width] = rgb555_to_rgb(u)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path)


def try_sec32(blob: bytes, dest: Path) -> None:
    """sec[32]/[33]/[27]: repeating {u32 type=8, u32 size} then payload."""
    if len(blob) < 16 or blob[:4] != b"\x08\x00\x00\x00":
        return
    print(f"  packed header {blob[:20].hex()}")
    # Try Huffman from +0, +8, +0x10, +0x14
    for off in (0, 8, 0x10, 0x14):
        try:
            dec, end = decompress_one(blob, off)
        except Exception as ex:
            print(f"  huff@{off:#x} FAIL {ex}")
            continue
        print(f"  huff@{off:#x} -> {len(dec)} consumed_to={end:#x}")
        (dest / f"huff_{off:02x}.bin").write_bytes(dec)
        save_rgb555(dec, dest / f"huff_{off:02x}_w256.png", 256)
        save_rgb555(dec, dest / f"huff_{off:02x}_w128.png", 128)


def export_map(mid: str) -> None:
    data = find_mdp(find_field_dir(), mid).read_bytes()
    dest = OUT / mid / "huff"
    dest.mkdir(parents=True, exist_ok=True)
    print(f"\n======== {mid} ========")

    s0 = get_section(data, 0)
    if s0:
        print(f"sec[0] compressed={len(s0)} count_be={(s0[0]<<8)|s0[1]}")
        try:
            dec = decompress_counted(s0)
            (dest / "sec0.bin").write_bytes(dec)
            print(f"  decompressed={len(dec)} head={dec[:32].hex()}")
            for w in (64, 128, 256, 320, 512):
                save_rgb555(dec, dest / f"sec0_w{w}.png", w)
            stitch_tiles(dec, 256, 32, dest / "sec0_tiles_256x32.png", cols=1)
            stitch_tiles(dec, 128, 64, dest / "sec0_tiles_128x64.png", cols=2)
            stitch_tiles(dec, 256, 64, dest / "sec0_tiles_256x64.png", cols=1)
        except Exception as ex:
            print(f"  sec0 FAIL: {ex}")
            import traceback
            traceback.print_exc()

    s4 = get_section(data, 4)
    if s4:
        print(f"sec[4] compressed={len(s4)} head={s4[:8].hex()}")
        # Try counted (if 02 00 ... count=2) AND single-frame from +0/+2/+4
        try:
            cbe = (s4[0] << 8) | s4[1]
            print(f"  be16={cbe}")
            if 1 <= cbe <= 64:
                dec = decompress_counted(s4)
                (dest / "sec4_counted.bin").write_bytes(dec)
                print(f"  counted -> {len(dec)}")
                save_rgb555(dec, dest / "sec4_counted_w256.png", 256)
                save_rgb555(dec, dest / "sec4_counted_w128.png", 128)
        except Exception as ex:
            print(f"  sec4 counted FAIL: {ex}")
        for off in (0, 2, 4, 8):
            try:
                dec, end = decompress_one(s4, off)
            except Exception as ex:
                print(f"  sec4 huff@{off} FAIL {ex}")
                continue
            print(f"  sec4 huff@{off} -> {len(dec)} end={end:#x}")
            (dest / f"sec4_{off}.bin").write_bytes(dec)
            save_rgb555(dec, dest / f"sec4_{off}_w256.png", 256)

    for idx in (27, 32, 33):
        blob = get_section(data, idx)
        if not blob:
            continue
        print(f"sec[{idx}] len={len(blob)}")
        sub = dest / f"sec{idx}"
        sub.mkdir(exist_ok=True)
        try_sec32(blob, sub)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("maps", nargs="*", default=["2000", "2410", "BA38"])
    args = ap.parse_args()
    for mid in args.maps:
        try:
            export_map(mid)
        except FileNotFoundError as e:
            print(e)


if __name__ == "__main__":
    main()
