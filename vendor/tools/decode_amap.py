"""Decompress Grandia AMAP#.ADD (Huffman @ grandia+0x9D50) and dump sections."""
from __future__ import annotations

import struct
from pathlib import Path

FIELD = Path(r"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\FIELD")
OUT = Path(r"C:\Users\User\Projects\Grandipelago\Grandipelago\data\amap_decoded")


class BitReader:
    def __init__(self, data: bytes, pos: int = 0):
        self.data = data
        self.pos = pos

    def u8(self) -> int:
        if self.pos >= len(self.data):
            raise EOFError("truncated")
        b = self.data[self.pos]
        self.pos += 1
        return b

    def read(self, n: int) -> bytes:
        out = self.data[self.pos : self.pos + n]
        if len(out) < n:
            raise EOFError("truncated")
        self.pos += n
        return out


def build_leaves(br: BitReader, tree: list[list[int]]) -> int:
    """Port of +9B10. Returns high-nibble mode. Fills tree[0..255].word0."""
    first = br.u8()
    mode = first >> 4
    kind = first & 0xF
    if kind == 0:
        for i in range(0x100):
            tree[i][0] = br.u8()
        tree[0x100][0] = 1
        return mode

    default0 = br.u8()
    if kind == 1:
        mask = br.read(0x20)
        for i in range(0x100):
            bit = (mask[i >> 3] >> (7 - (i & 7))) & 1
            if bit:
                tree[i][0] = br.u8()
            else:
                tree[i][0] = default0
        tree[0x100][0] = 1
        return mode

    # kind >= 2: two defaults + 2-bit selector mask
    default1 = br.u8()
    default2 = br.u8()
    mask = br.read(0x40)
    for i in range(0x100):
        byte = mask[i >> 2]
        shift = (3 - (i & 3)) * 2
        sel = (byte >> shift) & 3
        if sel == 0:
            tree[i][0] = default0
        elif sel == 1:
            tree[i][0] = default1
        elif sel == 2:
            tree[i][0] = default2
        else:
            tree[i][0] = br.u8()
    tree[0x100][0] = 1
    return mode


def build_tree(tree: list[list[int]]) -> int:
    """Port of +9C50. Returns root byte-offset. Mutates tree in place.

    Node layout (8 bytes / 4 words conceptually):
      [0]=weight, [1]=saved, [2]=left_off, [3]=right_off  (as uint16s)
    """
    # Ensure space for internal nodes up to ~0x201
    while len(tree) < 0x220:
        tree.append([0, 0, 0, 0])

    tree[0x201][0] = 0xFFFF  # sentinel at index 0x201? code writes to +0x1008
    # 0x1008/8 = 0x201 yes

    node_count = 0x101  # leaves 0..0x100
    next_slot = 0x201
    write_ptr = 0x80E  # byte offset into tree buffer for new nodes' fields

    # In C, tree is byte array; word at ebx*8 etc.
    # We'll keep list of [w0,w1,w2,w3] per 8-byte node.

    def get_w(idx: int, which: int) -> int:
        return tree[idx][which] & 0xFFFF

    def set_w(idx: int, which: int, val: int) -> None:
        tree[idx][which] = val & 0xFFFF

    while True:
        # Find two smallest non-zero weights among indices 0..node_count-1
        # Port of the loop: eax walks 0..ecx-1, ecx starts as node_count
        best1 = 0x201  # ebx init
        best1_off = 0x1008  # esi init (byte off of sentinel)
        best2 = 0x201
        # Actually re-read the loop carefully...

        # At start of each iteration (+9C75):
        # ecx = node_count ([ebp-0xc])
        # ebx = 0x201 ([ebp-4] and [ebp-8] both set to 0x201)
        # Then if ecx<=0 break
        # esi = 0x1008
        # eax = 0, ecx = 0
        # loop:
        #   dx = word[ecx_ptr] where ecx_ptr = eax's *8? 
        #   movzx edx, word ptr [ecx + edi] with ecx starting 0 adding 8
        #   So edx = weight of node (ecx/8)

        ebx = 0x201
        ebp4 = 0x201
        ebp8 = 0x201
        esi = 0x1008
        eax = 0
        ecx_byte = 0
        while eax < node_count:
            dx = get_w(ecx_byte // 8, 0)
            if dx != 0:
                # shl ebx, 3 → compare with word[ebx+edi] where ebx was index
                left_idx = ebx  # current best1 index
                if dx < get_w(left_idx, 0):
                    # new best1
                    ebp8 = ebp4
                    esi = ebx * 8
                    ebx = eax
                    ebp4 = ebx
                else:
                    ebx = ebp4
                    if dx < get_w(esi // 8, 0):
                        ebp8 = eax
                        esi = ecx_byte
            eax += 1
            ecx_byte += 8

        edx = ebp8
        if edx == 0x201:
            break

        # Merge best ebx (ebp4) and edx (ebp8)
        ebx = ebp4
        ax = (get_w(edx, 0) + get_w(ebx, 0)) & 0xFFFF
        # write to new node at write_ptr: word[write_ptr-6] = ax
        # write_ptr starts 0x80e, so first new node at index 0x80e/8 = 0x101
        new_idx = write_ptr // 8
        # Ensure size
        while len(tree) <= new_idx:
            tree.append([0, 0, 0, 0])

        # mov word ptr [esi-6], ax  where esi=write_ptr
        # At offset write_ptr-6: that's within previous? 
        # write_ptr=0x80e → write_ptr-6=0x808 → node 0x101 word0
        set_w(new_idx, 0, ax)  # weight of new node — but -6 from 0x80e is 0x808 = node 0x101 * 8
        # Actually 0x80e - 6 = 0x808, 0x808/8 = 0x101. word at +0 of node 0x101.
        # word [esi-2] = left = ebx*8; word [esi] = right = edx*8
        # esi-2 = 0x80c = node 0x101 word at +4 (offset 4 within node = word index 2)
        # esi = 0x80e = node 0x101 word at +6 (word index 3)
        set_w(new_idx, 2, ebx * 8)  # left
        set_w(new_idx, 3, edx * 8)  # right

        # Save old weights into word1 and zero word0
        set_w(ebx, 1, get_w(ebx, 0))
        set_w(ebx, 0, 0)
        set_w(edx, 1, get_w(edx, 0))
        set_w(edx, 0, 0)

        write_ptr += 8
        node_count += 1
        # loop continues with updated node_count; ebx reset to 0x201 at top

    # Root: lea eax, [ecx*8 - 8] where ecx is final node_count
    root_off = node_count * 8 - 8
    # copy weight to word1
    root_idx = root_off // 8
    set_w(root_idx, 1, get_w(root_idx, 0))
    return root_off


def decode(br: BitReader, tree: list[list[int]], root_off: int, mode: int,
           be: bool = False, ring32: bool = False) -> bytes:
    """Port of +9DE0. MDP sec[2] (+0x72980) also adds a 32-byte history ring."""
    out = bytearray()
    bit_mask = 0x80
    bit_byte = 0
    phase = 0
    pending = 0
    prev_sym = 0
    root_idx = root_off // 8
    ring = [0] * 32
    ring_i = 0

    def next_bit() -> int:
        nonlocal bit_mask, bit_byte
        if bit_mask == 0x80:
            bit_byte = br.u8()
        bit = 1 if (bit_byte & bit_mask) else 0
        bit_mask >>= 1
        if bit_mask == 0:
            bit_mask = 0x80
        return bit

    # Start at root: edx = root_off, then sar/8 for index... 
    # Initial: mov eax, edx (root); cdq; and edx,7; add edx,eax; sar edx,3 → root_idx
    # Then loop uses eax as current index (after sar)

    while True:
        idx = root_idx
        while True:
            bit = next_bit()
            node = tree[idx]
            child_off = node[3] if bit else node[2]
            # child_off is signed word in game (movsx)
            if child_off >= 0x8000:
                child_off -= 0x10000
            # then cdq; and 7; add; sar 3
            idx = (child_off + (child_off & 7)) >> 3  # wait: cdq sign-extends, and edx,7
            # Actually: eax = child_off (movsx); cdq; and edx,7; add eax,edx; sar eax,3
            # For positive: (off + (off&7))? No - edx is sign of eax, and 7:
            # if eax>=0: edx=0, result = eax>>3
            # if eax<0: edx=-1, and 7 → 7, eax+7, sar 3 = floor div
            if child_off < 0:
                idx = (child_off + 7) >> 3  # arithmetic: (child_off + 7) // 8 for negative?
                # Standard signed sar: (x + 7) >> 3 only if using unsigned tricks;
                # actual: eax+edx where edx=7 if negative: (x+7)>>3 which is correct for toward-zero? 
                # SAR is toward -inf. (x+7)>>3 for x=-8: (-8+7)>>3 = -1>>3 = -1; -8>>3=-1. OK
                # For x=-1: (-1+7)>>3=6>>3=0; but -1>>3 = -1 in SAR. Hmm.
                # cdq on -1 → edx=0xFFFFFFFF; and 7 → 7; eax = -1+7 = 6; sar 3 = 0.
                # So it's toward-zero division by 8 for negatives? -1/8 → 0.
                idx = (child_off + 7) >> 3 if child_off < 0 else child_off >> 3
            else:
                idx = child_off >> 3

            if idx <= 0x100:
                break
            # idx > 0x100 → continue walking (ja)

        if idx == 0x100:
            # EOF
            if phase != 0:
                # flush pending
                if be:
                    out.append(pending & 0xFF)
                    out.append(0)
                else:
                    out.append(pending & 0xFF)
                    out.append(0)  # roughly
            break

        sym = idx & 0xFF
        if mode == 1:
            sym = (prev_sym ^ sym) & 0xFF
        elif mode == 2:
            sym = (prev_sym + sym) & 0xFF
        prev_sym = sym
        # Inlined MDP loop at +0x72A45: output += ring[i], store, i = (i+1)%32.
        # First 32 symbols match +9DE0 (ring is zero); later bytes diverge without this.
        if ring32:
            sym = (sym + ring[ring_i]) & 0xFF
            ring[ring_i] = sym
            ring_i = (ring_i + 1) & 31

        if phase == 0:
            pending = sym
            phase = 1
        else:
            if be:
                val = ((pending & 0xFF) << 8) | (sym & 0xFF)
            else:
                # non-be path: mov eax,edi; shl 8; add eax,ebx → high=prev? 
                # Looking at code when 63efe9==0:
                # mov eax, edi (current sym); shl 8; add eax, ebx (pending)
                # so high=sym, low=pending → little-endian store of (sym<<8)|pending
                # word ptr [ebx] = dx with that value
                val = ((sym & 0xFF) << 8) | (pending & 0xFF)
            out.append(val & 0xFF)
            out.append((val >> 8) & 0xFF)
            phase = 0

    return bytes(out)


def decompress_amap(data: bytes, ring32: bool = False) -> bytes:
    # Tree as list of [w0,w1,left,right]
    tree: list[list[int]] = [[0, 0, 0, 0] for _ in range(0x220)]
    br = BitReader(data, 0)
    mode = build_leaves(br, tree)
    root = build_tree(tree)
    # Decode writes starting at dest; stream continues from br.pos
    return decode(br, tree, root, mode, be=False, ring32=ring32)


def decompress_mdp_image(data: bytes) -> bytes:
    """MDP field image section (sec[2]): same Huffman as AMAP plus +0x72980 ring."""
    return decompress_amap(data, ring32=True)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for i in range(6):
        src = FIELD / f"AMAP{i}.ADD"
        if not src.exists():
            continue
        raw = src.read_bytes()
        print(f"\n=== AMAP{i}.ADD compressed={len(raw)} ===")
        try:
            dec = decompress_amap(raw)
        except Exception as ex:
            print(f"  FAIL: {ex}")
            import traceback
            traceback.print_exc()
            continue
        print(f"  decompressed={len(dec)}")
        out_path = OUT / f"AMAP{i}.bin"
        out_path.write_bytes(dec)
        print(f"  wrote {out_path}")
        print(f"  head32: {dec[:32].hex()}")
        if len(dec) >= 0x14:
            offs = [struct.unpack_from("<I", dec, j)[0] for j in range(0, 0x14, 4)]
            print(f"  dir: {[f'{o:08X}' for o in offs]}")
            for n, o in enumerate(offs):
                abs_off = o  # relative to dir base 0
                print(f"    sec{n}: +{abs_off:X} valid={abs_off < len(dec)}")


if __name__ == "__main__":
    main()
