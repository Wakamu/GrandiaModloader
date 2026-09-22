using System.Buffers.Binary;

namespace Grandia.Sdk;

/// <summary>
/// MDP sec[15] camera-path bytecode. Directory is self-relative u32
/// offsets; count = first/4. Hook <c>camera_path N</c> is 1-based
/// (<c>+0x7C3FC</c> does <c>dec dl</c> then
/// <c>IP = base + [base + (N-1)*4]</c> → <c>[0x719934]</c>). Walker
/// <c>+0x5B770</c>. Integers inside a chunk are big-endian.
/// </summary>
public static class MdpSec15
{
    private static readonly Dictionary<byte, int> ChannelTween =
        new()
        {
            [0x08] = 0,
            [0x09] = 1,
            [0x0A] = 2,
            [0x0B] = 3,
            [0x17] = 4,
            [0x18] = 5,
            [0x19] = 6,
            [0x1A] = 7,
        };

    public static MapCameraPathTable Parse(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 4)
        {
            return new MapCameraPathTable();
        }

        var first = BinaryPrimitives.ReadUInt32LittleEndian(blob);
        if (first < 4 || first % 4 != 0 || first > (uint)blob.Length)
        {
            return new MapCameraPathTable();
        }

        var n = (int)(first / 4);
        var offs = new int[n];
        for (var i = 0; i < n; i++)
        {
            offs[i] = (int)BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(i * 4));
        }

        var items = new List<MapCameraPath>(n);
        for (var i = 0; i < n; i++)
        {
            var start = offs[i];
            var end = i + 1 < n ? offs[i + 1] : blob.Length;
            if (start < 0 || start > blob.Length || end < start || end > blob.Length)
            {
                items.Add(new MapCameraPath(i, i + 1, error: "bad offset"));
                continue;
            }

            var raw = blob.Slice(start, end - start).ToArray();
            if (raw.Length == 0 || raw[0] == 0xFF)
            {
                items.Add(new MapCameraPath(i, i + 1, raw));
                continue;
            }

            var (ops, error) = ParseChunk(raw);
            items.Add(new MapCameraPath(i, i + 1, raw, ops, error));
        }

        return new MapCameraPathTable(items);
    }

    internal static (List<MapCameraPathOp> Ops, string? Error) ParseChunk(ReadOnlySpan<byte> ch)
    {
        var ev = new List<MapCameraPathOp>();
        var pos = 0;
        var n = ch.Length;
        while (pos < n)
        {
            var op = ch[pos];
            if (op == 0x00)
            {
                var rest = ch[pos..];
                if (rest.IndexOfAnyExcept((byte)0) < 0)
                {
                    return (ev, null);
                }

                return (ev, $"unexpected 0x00 at {pos}");
            }

            if (op == 0xFF)
            {
                ev.Add(new MapCameraPathOp(pos, op, MapCameraPathOpKind.End));
                pos++;
                while (pos < n && ch[pos] == 0)
                {
                    pos++;
                }

                return pos == n ? (ev, null) : (ev, $"trailing junk after end at {pos}");
            }

            if (op is 0x01 or 0x02 or 0x03 or 0x04 or 0x06 or 0x07)
            {
                if (pos + 13 > n)
                {
                    return (ev, $"{(MapCameraPathOpKind)op} truncated");
                }

                ev.Add(new MapCameraPathOp(
                    pos,
                    op,
                    (MapCameraPathOpKind)op,
                    [BeVal(ch, pos + 1), BeVal(ch, pos + 5), BeVal(ch, pos + 9)]));
                pos += 13;
                continue;
            }

            if (op == 0x05)
            {
                if (pos + 5 > n)
                {
                    return (ev, "set_p28 truncated");
                }

                ev.Add(new MapCameraPathOp(
                    pos, op, MapCameraPathOpKind.SetP28, [BeVal(ch, pos + 1)]));
                pos += 5;
                continue;
            }

            if (ChannelTween.TryGetValue(op, out var edx))
            {
                var need = 2 + ChannelPayloadSize(edx);
                if (pos + need > n)
                {
                    return (ev, $"ch_mode{edx} truncated");
                }

                var chId = ch[pos + 1];
                var p = pos + 2;
                var values = new List<MapCameraPathValue> { BeVal(ch, p), BeVal(ch, p + 4) };
                p += 8;
                var duration = 0;
                if ((edx & 3) is 0 or 2)
                {
                    values.Add(BeVal(ch, p));
                    p += 4;
                }
                else
                {
                    duration = (ch[p] << 8) | ch[p + 1];
                    p += 2;
                }

                var footN = (edx & 4) != 0 ? 3 : 2;
                var foot = ch.Slice(p, footN).ToArray();
                p += footN;
                var channel = chId <= 11 ? (MapCameraPathChannel?)(MapCameraPathChannel)chId : null;
                ev.Add(new MapCameraPathOp(
                    pos,
                    op,
                    MapCameraPathOpKind.Channel,
                    values,
                    channel,
                    edx,
                    duration,
                    foot));
                pos = p;
                continue;
            }

            if (op == 0x0C)
            {
                if (pos + 3 > n)
                {
                    return (ev, "table_s32 truncated");
                }

                var idx = ch[pos + 1];
                var cnt = ch[pos + 2];
                if (pos + 3 + cnt * 4 > n)
                {
                    return (ev, $"table_s32 truncated n={cnt}");
                }

                var vals = new MapCameraPathValue[cnt];
                for (var i = 0; i < cnt; i++)
                {
                    vals[i] = BeVal(ch, pos + 3 + i * 4);
                }

                ev.Add(new MapCameraPathOp(
                    pos, op, MapCameraPathOpKind.TableS32, vals, arg: idx));
                pos += 3 + cnt * 4;
                continue;
            }

            if (op == 0x0D)
            {
                if (pos + 7 > n)
                {
                    return (ev, "slot_meta truncated");
                }

                ev.Add(new MapCameraPathOp(
                    pos,
                    op,
                    MapCameraPathOpKind.SlotMeta,
                    channel: ch[pos + 1] <= 11 ? (MapCameraPathChannel)ch[pos + 1] : null,
                    duration: (ch[pos + 3] << 8) | ch[pos + 4],
                    arg: ch[pos + 1],
                    scale: ch[pos + 2],
                    mark: (ch[pos + 5] << 8) | ch[pos + 6]));
                pos += 7;
                continue;
            }

            if (op == 0x0F)
            {
                if (pos + 3 > n)
                {
                    return (ev, "wait truncated");
                }

                ev.Add(new MapCameraPathOp(
                    pos,
                    op,
                    MapCameraPathOpKind.Wait,
                    duration: (ch[pos + 1] << 8) | ch[pos + 2]));
                pos += 3;
                continue;
            }

            if (op is 0x10 or 0x11 or 0x12)
            {
                if (pos + 2 > n)
                {
                    return (ev, $"{(MapCameraPathOpKind)op} truncated");
                }

                ev.Add(new MapCameraPathOp(
                    pos, op, (MapCameraPathOpKind)op, arg: ch[pos + 1]));
                pos += 2;
                continue;
            }

            if (op == 0x13)
            {
                if (pos + 3 > n)
                {
                    return (ev, "wait_b truncated");
                }

                ev.Add(new MapCameraPathOp(
                    pos,
                    op,
                    MapCameraPathOpKind.WaitB,
                    duration: (ch[pos + 1] << 8) | ch[pos + 2]));
                pos += 3;
                continue;
            }

            if (op == 0x14)
            {
                ev.Add(new MapCameraPathOp(pos, op, MapCameraPathOpKind.YieldTween));
                pos += 1;
                continue;
            }

            if (op == 0x15)
            {
                if (pos + 2 > n)
                {
                    return (ev, "save_cam truncated");
                }

                ev.Add(new MapCameraPathOp(
                    pos, op, MapCameraPathOpKind.SaveCam, saveFlags: ch[pos + 1]));
                pos += 2;
                continue;
            }

            if (op == 0x16)
            {
                if (pos + 11 > n)
                {
                    return (ev, "set_words truncated");
                }

                var words = new int[5];
                for (var i = 0; i < 5; i++)
                {
                    words[i] = (ch[pos + 1 + i * 2] << 8) | ch[pos + 2 + i * 2];
                }

                ev.Add(new MapCameraPathOp(
                    pos, op, MapCameraPathOpKind.SetWords, words: words));
                pos += 11;
                continue;
            }

            if (op == 0x1B)
            {
                if (pos + 2 > n)
                {
                    return (ev, "call_70400 truncated");
                }

                ev.Add(new MapCameraPathOp(
                    pos, op, MapCameraPathOpKind.Call70400, arg: ch[pos + 1]));
                pos += 2;
                continue;
            }

            if (op == 0x1C)
            {
                if (pos + 3 > n)
                {
                    return (ev, "skip3 truncated");
                }

                ev.Add(new MapCameraPathOp(
                    pos, op, MapCameraPathOpKind.Skip3, foot: ch.Slice(pos, 3).ToArray()));
                pos += 3;
                continue;
            }

            if (op == 0x1D)
            {
                ev.Add(new MapCameraPathOp(pos, op, MapCameraPathOpKind.ResetCam));
                pos++;
                while (pos < n && ch[pos] == 0)
                {
                    pos++;
                }

                return pos == n ? (ev, null) : (ev, $"trailing junk after reset_cam at {pos}");
            }

            return (ev, $"unknown op 0x{op:X2}");
        }

        return (ev, null);
    }

    /// <summary>
    /// Rebuild sec[15]. Removed slots stay as <c>0xFF</c> stubs so
    /// <c>camera_path N</c> ids do not shift. Trailing stubs are kept so
    /// <see cref="MapCameraPath.Id"/> still matches the directory slot.
    /// </summary>
    public static byte[] Emit(MapCameraPathTable table)
    {
        var live = table.Items.ToList();
        if (live.Count == 0)
        {
            return [4, 0, 0, 0, 0xFF];
        }

        var chunks = new List<byte[]>(live.Count);
        foreach (var row in live)
        {
            if (row.Removed || row.Empty)
            {
                chunks.Add([0xFF]);
                continue;
            }

            var raw = row.RawBytes;
            chunks.Add(raw.Length > 0 ? raw : EncodeChunk(row.Ops));
        }

        var dir = live.Count * 4;
        var cursor = dir;
        var blob = new byte[dir + chunks.Sum(c => c.Length)];
        for (var i = 0; i < chunks.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(i * 4), (uint)cursor);
            chunks[i].CopyTo(blob.AsSpan(cursor));
            cursor += chunks[i].Length;
        }

        return blob;
    }

    public static byte[] Apply(MapCameraPathTable table) => Emit(table);

    public static byte[] EncodeChunk(IReadOnlyList<MapCameraPathOp> ops)
    {
        if (ops.Count == 0)
        {
            return [0xFF];
        }

        var dest = new List<byte>(64);
        foreach (var op in ops)
        {
            EncodeOp(dest, op);
        }

        if (dest.Count == 0 || dest[^1] is not (0xFF or 0x1D))
        {
            dest.Add(0xFF);
        }

        return [.. dest];
    }

    private static void EncodeOp(List<byte> dest, MapCameraPathOp op)
    {
        switch (op.Kind)
        {
            case MapCameraPathOpKind.SetPos:
            case MapCameraPathOpKind.SetDelta:
            case MapCameraPathOpKind.SetRot:
            case MapCameraPathOpKind.SetFov:
            case MapCameraPathOpKind.AddPos:
            case MapCameraPathOpKind.AddRot:
                dest.Add((byte)op.Kind);
                WriteBe(dest, Val(op, 0));
                WriteBe(dest, Val(op, 1));
                WriteBe(dest, Val(op, 2));
                break;
            case MapCameraPathOpKind.SetP28:
                dest.Add(0x05);
                WriteBe(dest, Val(op, 0));
                break;
            case MapCameraPathOpKind.Channel:
                dest.Add(ChannelOpcode(op.TweenMode, op.Opcode));
                dest.Add((byte)(op.Channel ?? 0));
                WriteBe(dest, Val(op, 0));
                WriteBe(dest, Val(op, 1));
                var edx = op.TweenMode >= 0 ? op.TweenMode : ChannelTween.GetValueOrDefault(op.Opcode, 1);
                if ((edx & 3) is 0 or 2)
                {
                    WriteBe(dest, Val(op, 2));
                }
                else
                {
                    dest.Add((byte)((op.Duration >> 8) & 0xFF));
                    dest.Add((byte)(op.Duration & 0xFF));
                }

                var footN = (edx & 4) != 0 ? 3 : 2;
                for (var i = 0; i < footN; i++)
                {
                    dest.Add(i < op.Foot.Count ? op.Foot[i] : (byte)0);
                }

                break;
            case MapCameraPathOpKind.TableS32:
                dest.Add(0x0C);
                dest.Add((byte)op.Arg);
                dest.Add((byte)op.Values.Count);
                foreach (var v in op.Values)
                {
                    WriteBe(dest, v.Raw);
                }

                break;
            case MapCameraPathOpKind.SlotMeta:
                dest.Add(0x0D);
                dest.Add((byte)(op.Channel ?? (MapCameraPathChannel)op.Arg));
                dest.Add((byte)op.Scale);
                dest.Add((byte)((op.Duration >> 8) & 0xFF));
                dest.Add((byte)(op.Duration & 0xFF));
                dest.Add((byte)((op.Mark >> 8) & 0xFF));
                dest.Add((byte)(op.Mark & 0xFF));
                break;
            case MapCameraPathOpKind.Wait:
            case MapCameraPathOpKind.WaitB:
                dest.Add((byte)op.Kind);
                dest.Add((byte)((op.Duration >> 8) & 0xFF));
                dest.Add((byte)(op.Duration & 0xFF));
                break;
            case MapCameraPathOpKind.Set63Fa5B:
            case MapCameraPathOpKind.Set63Faa2:
            case MapCameraPathOpKind.Set71A640:
            case MapCameraPathOpKind.Call70400:
                dest.Add((byte)op.Kind);
                dest.Add((byte)op.Arg);
                break;
            case MapCameraPathOpKind.YieldTween:
            case MapCameraPathOpKind.ResetCam:
            case MapCameraPathOpKind.End:
                dest.Add((byte)op.Kind);
                break;
            case MapCameraPathOpKind.SaveCam:
                dest.Add(0x15);
                dest.Add((byte)op.SaveFlags);
                break;
            case MapCameraPathOpKind.SetWords:
                dest.Add(0x16);
                for (var i = 0; i < 5; i++)
                {
                    var w = i < op.Words.Count ? op.Words[i] : 0;
                    dest.Add((byte)((w >> 8) & 0xFF));
                    dest.Add((byte)(w & 0xFF));
                }

                break;
            case MapCameraPathOpKind.Skip3:
                if (op.Foot.Count >= 3)
                {
                    dest.Add(op.Foot[0]);
                    dest.Add(op.Foot[1]);
                    dest.Add(op.Foot[2]);
                }
                else
                {
                    dest.Add(0x1C);
                    dest.Add(0);
                    dest.Add(0);
                }

                break;
            default:
                throw new ArgumentException($"cannot encode camera-path op {op.Kind}");
        }
    }

    private static uint Val(MapCameraPathOp op, int i) =>
        i < op.Values.Count ? op.Values[i].Raw : 0;

    private static void WriteBe(List<byte> dest, uint raw)
    {
        dest.Add((byte)((raw >> 24) & 0xFF));
        dest.Add((byte)((raw >> 16) & 0xFF));
        dest.Add((byte)((raw >> 8) & 0xFF));
        dest.Add((byte)(raw & 0xFF));
    }

    private static byte ChannelOpcode(int tweenMode, byte opcode)
    {
        if (ChannelTween.ContainsKey(opcode))
        {
            return opcode;
        }

        return tweenMode switch
        {
            0 => 0x08,
            1 => 0x09,
            2 => 0x0A,
            3 => 0x0B,
            4 => 0x17,
            5 => 0x18,
            6 => 0x19,
            7 => 0x1A,
            _ => 0x09,
        };
    }

    private static int ChannelPayloadSize(int edx)
    {
        var n = 8;
        n += (edx & 3) is 0 or 2 ? 4 : 2;
        n += (edx & 4) != 0 ? 3 : 2;
        return n;
    }

    private static MapCameraPathValue BeVal(ReadOnlySpan<byte> src, int off) =>
        new(BinaryPrimitives.ReadUInt32BigEndian(src.Slice(off)));
}
