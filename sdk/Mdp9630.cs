namespace Grandia.Sdk;

/// <summary>
/// Field TIM codec at <c>+0x9630</c> (nibble Huffman + zero-run).
/// Same algorithm as <c>tools/decode_mdp_sec1.py</c>.
/// </summary>
public static class Mdp9630
{
    public static bool TryDecompress(ReadOnlySpan<byte> src, out byte[] dest, int destCap = 0x200000)
    {
        dest = [];
        if (src.Length < 12)
        {
            return false;
        }

        var hdr = src[0] | (src[1] << 8);
        var mode = hdr >> 14;
        var inHuff = ((hdr >> 13) & 1) != 0;
        var treeOff = hdr & 0x1FFF;
        if (treeOff + 0xB > src.Length)
        {
            return false;
        }

        var counts = new int[17];
        for (var i = 0; i < 9; i++)
        {
            var b = Get(src, treeOff + 2 + i);
            counts[i * 2] = b >> 4;
            if (i * 2 + 1 < 17)
            {
                counts[i * 2 + 1] = b & 0xF;
            }
        }

        if (!TryBuildTables(counts, out var tableSym, out var tableLen))
        {
            return false;
        }

        var buf = new byte[destCap];
        var ebx = 2;
        var esiPhase = 0;
        var ediOut = 0;
        var edxPend = 0;
        var outI = 0;
        var prevLen = 0;
        var prevSym = 0;
        var bitPtr = treeOff + 0xB;
        var bitPos = 0;
        var guard = 0;
        var maxSteps = Math.Max(destCap, 8) * 32;

        void WriteZeroRun(int length)
        {
            prevLen = length;
            for (var n = 0; n < length; n++)
            {
                if (ediOut == 0)
                {
                    ediOut = 1;
                    edxPend = 0;
                }
                else
                {
                    if (outI < destCap)
                    {
                        buf[outI] = (byte)(edxPend & 0xFF);
                    }

                    outI++;
                    ediOut = 0;
                    edxPend = 0;
                }
            }
        }

        while (outI < destCap && guard < maxSteps)
        {
            guard++;
            if (!inHuff)
            {
                var b = Get(src, ebx);
                var low = b & 0xF;
                var high = b >> 4;
                var cmd = esiPhase == 0 ? low : high;
                if (esiPhase != 0)
                {
                    ebx++;
                }

                var phaseAfter = esiPhase == 0 ? 1 : 0;
                var ebxAfterCmd = ebx;
                int length;
                if (cmd < 0xD)
                {
                    length = prevLen - 6 + cmd;
                    esiPhase = phaseAfter;
                }
                else if (cmd == 0xD)
                {
                    var b2 = Get(src, ebx);
                    var extra = esiPhase != 0 ? b2 & 0xF : b2 >> 4;
                    if (esiPhase == 0)
                    {
                        ebx++;
                    }

                    esiPhase = esiPhase != 0 ? 1 : 0;
                    var adj = extra < 8 ? 0xE : 1;
                    length = extra - adj + prevLen;
                }
                else if (cmd == 0xE)
                {
                    var byteA = Get(src, ebx);
                    ebx++;
                    if (esiPhase != 0)
                    {
                        length = ((byteA & 0xF) << 4) | (byteA >> 4);
                    }
                    else
                    {
                        var byteB = Get(src, ebx);
                        length = (byteA & 0xF0) | (byteB & 0xF);
                    }

                    esiPhase = phaseAfter;
                    if (length == 0)
                    {
                        break;
                    }
                }
                else if (cmd == 0xF)
                {
                    var oldPhase = phaseAfter == 1 ? 0 : 1;
                    var aPtr = ebxAfterCmd;
                    var byteA = Get(src, aPtr);
                    ebx = aPtr + 1;
                    if (oldPhase != 0)
                    {
                        var byteB = Get(src, ebx);
                        ebx++;
                        var acc = ((byteA & 0xF) << 8) + (byteA & 0xF0);
                        acc = ((acc + (byteB & 0xF)) << 4) + (byteB >> 4);
                        length = acc & 0xFFFF;
                    }
                    else
                    {
                        var byteB = Get(src, ebx);
                        ebx++;
                        var byteC = Get(src, ebx);
                        var acc = (byteA & 0xF0) + (byteB & 0xF);
                        length = ((acc << 8) + (byteC & 0xF) + (byteB & 0xF0)) & 0xFFFF;
                    }

                    esiPhase = phaseAfter;
                }
                else
                {
                    length = cmd;
                    esiPhase = phaseAfter;
                }

                WriteZeroRun(length);
                inHuff = true;
                continue;
            }

            var word = Get(src, bitPtr) | (Get(src, bitPtr + 1) << 8);
            var peek = (word >> bitPos) & 0xFF;
            var total = tableLen[peek] + bitPos;
            if (total >= 8)
            {
                bitPtr++;
                bitPos = total - 8;
            }
            else
            {
                bitPos = total;
            }

            var symbol = tableSym[peek];
            if (symbol == 0x10)
            {
                inHuff = false;
                continue;
            }

            if (mode == 1)
            {
                symbol = (prevSym ^ symbol) & 0xF;
                prevSym = symbol;
            }
            else if (mode == 2)
            {
                symbol = (prevSym - symbol) & 0xF;
                prevSym = symbol;
            }

            if (ediOut == 0)
            {
                ediOut = 1;
                edxPend = symbol;
            }
            else
            {
                if (outI < destCap)
                {
                    buf[outI] = (byte)((edxPend | (symbol << 4)) & 0xFF);
                }

                outI++;
                ediOut = 0;
            }
        }

        if (guard >= maxSteps)
        {
            return false;
        }

        var n = Math.Max(outI, 8);
        dest = new byte[n];
        Buffer.BlockCopy(buf, 0, dest, 0, n);
        return true;
    }

    public static bool TryDecompressFrames(ReadOnlySpan<byte> payload, out byte[] dest)
    {
        dest = [];
        if (payload.Length < 4)
        {
            return false;
        }

        var count = BitConverter.ToUInt16(payload);
        var off = 4;
        using var ms = new MemoryStream();
        for (var i = 0; i < count; i++)
        {
            if (off + 4 > payload.Length)
            {
                return false;
            }

            var sz = BitConverter.ToInt32(payload.Slice(off));
            off += 4;
            if (sz < 0 || off + sz > payload.Length)
            {
                return false;
            }

            if (!TryDecompress(payload.Slice(off, sz), out var frame, 0x20000))
            {
                return false;
            }

            ms.Write(frame);
            off += sz;
        }

        dest = ms.ToArray();
        return dest.Length > 0;
    }

    private static int Get(ReadOnlySpan<byte> src, int i) =>
        i >= 0 && i < src.Length ? src[i] : 0;

    private static int Shuffle(int edi)
    {
        var esi = edi;
        var ecx = edi & 0x40;
        esi = (esi >> 2) & 0x20;
        var edx = edi;
        esi |= ecx;
        edx &= 1;
        esi >>= 2;
        ecx = edi & 0x20;
        edx <<= 2;
        esi |= ecx;
        ecx = edi & 0x10;
        esi >>= 2;
        esi |= ecx;
        ecx = edi & 2;
        esi >>= 1;
        edx |= ecx;
        ecx = edi & 4;
        edx <<= 2;
        edx |= ecx;
        ecx = edi & 8;
        edx <<= 2;
        edx |= ecx;
        edx += edx;
        esi |= edx;
        return esi & 0xFF;
    }

    private static bool TryBuildTables(int[] countsIn, out int[] tableSym, out int[] tableLen)
    {
        tableSym = new int[256];
        tableLen = new int[256];
        var counts = (int[])countsIn.Clone();
        var edi = 0;
        while (edi < 256)
        {
            var maxv = 0;
            var maxi = 0;
            var eax = 0;
            for (var ebx = 0; ebx < 17; ebx++)
            {
                var edx = counts[ebx];
                var esi = edx;
                var ecx = ebx;
                if (edx <= eax)
                {
                    esi = eax;
                    ecx = maxi;
                }

                maxv = esi;
                maxi = ecx;
                eax = esi;
            }

            if (maxv == 0)
            {
                return false;
            }

            counts[maxi] = 0;
            var nfill = 1 << (8 - maxv);
            for (var n = 0; n < nfill; n++)
            {
                var idx = Shuffle(edi);
                tableSym[idx] = maxi;
                tableLen[idx] = maxv;
                edi++;
                if (edi > 256)
                {
                    return false;
                }
            }
        }

        return edi == 256;
    }
}
