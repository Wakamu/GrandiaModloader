namespace Grandia.Sdk;

internal abstract class FsItem
{
    public abstract int Size { get; }

    public abstract void WriteTo(IList<byte> dest, int resolvedSkip, int resolvedRel);
}

internal sealed class FsLabel : FsItem
{
    public FsLabel(string name) => Name = name;

    public string Name { get; }

    public override int Size => 0;

    public override void WriteTo(IList<byte> dest, int resolvedSkip, int resolvedRel)
    {
    }
}

internal sealed class FsRaw : FsItem
{
    public FsRaw(byte[] raw) => Raw = raw;

    public byte[] Raw { get; }

    public override int Size => Raw.Length;

    public override void WriteTo(IList<byte> dest, int resolvedSkip, int resolvedRel)
    {
        foreach (var b in Raw)
        {
            dest.Add(b);
        }
    }
}

internal sealed class FsWord : FsItem
{
    public FsWord(int word) => Word = word & 0xFFFF;

    public int Word { get; }

    public override int Size => 2;

    public override void WriteTo(IList<byte> dest, int resolvedSkip, int resolvedRel) =>
        FieldScriptIr.WriteU16(dest, Word);
}

internal sealed class FsFlag : FsItem
{
    public FsFlag(int word, int flagId)
    {
        Word = word & 0xFFFF;
        FlagId = flagId & 0xFFFF;
    }

    public int Word { get; }
    public int FlagId { get; }

    public override int Size => 4;

    public override void WriteTo(IList<byte> dest, int resolvedSkip, int resolvedRel)
    {
        FieldScriptIr.WriteU16(dest, Word);
        FieldScriptIr.WriteU16(dest, FlagId);
    }
}

internal sealed class FsJump : FsItem
{
    public FsJump(int word, string target, int originalSkip)
    {
        Word = word & 0xF000;
        Target = target;
        OriginalSkip = originalSkip;
    }

    public int Word { get; }
    public string Target { get; }
    public int OriginalSkip { get; }

    public override int Size => 2;

    public override void WriteTo(IList<byte> dest, int resolvedSkip, int resolvedRel) =>
        FieldScriptIr.WriteU16(dest, Word | (resolvedSkip & 0x0FFF));
}

internal sealed class FsRelJump : FsItem
{
    public FsRelJump(int word, string target, int originalRel)
    {
        Word = word & 0xFFFF;
        Target = target;
        OriginalRel = originalRel;
    }

    public int Word { get; }
    public string Target { get; }
    public int OriginalRel { get; }

    public override int Size => 4;

    public override void WriteTo(IList<byte> dest, int resolvedSkip, int resolvedRel)
    {
        FieldScriptIr.WriteU16(dest, Word);
        var rel = (short)resolvedRel;
        dest.Add((byte)rel);
        dest.Add((byte)(rel >> 8));
    }
}

internal sealed class FsBranch : FsItem
{
    public FsBranch(int word, int arg, byte[] extra)
    {
        Word = word & 0xFFFF;
        Arg = arg & 0xFFFF;
        Extra = extra;
    }

    public int Word { get; }
    public int Arg { get; }
    public byte[] Extra { get; }

    public override int Size => Extra.Length > 0 ? 6 : 4;

    public override void WriteTo(IList<byte> dest, int resolvedSkip, int resolvedRel)
    {
        FieldScriptIr.WriteU16(dest, Word);
        FieldScriptIr.WriteU16(dest, Arg);
        foreach (var b in Extra)
        {
            dest.Add(b);
        }
    }
}

internal sealed class FsType6 : FsItem
{
    public FsType6(int word, int sub, byte[] raw)
    {
        Word = word & 0xFFFF;
        Sub = sub;
        Raw = raw;
    }

    public int Word { get; }
    public int Sub { get; }
    public byte[] Raw { get; }

    public override int Size => 2 + Raw.Length;

    public override void WriteTo(IList<byte> dest, int resolvedSkip, int resolvedRel)
    {
        FieldScriptIr.WriteU16(dest, Word);
        foreach (var b in Raw)
        {
            dest.Add(b);
        }
    }
}

internal sealed class FsSay : FsItem
{
    public FsSay(int wordHi, bool type8, byte[] payload)
    {
        WordHi = wordHi & 0xF000;
        Type8 = type8;
        Payload = payload;
    }

    public int WordHi { get; }
    public bool Type8 { get; }
    public byte[] Payload { get; }

    public override int Size
    {
        get
        {
            var n = Payload.Length + (Payload.Length % 2);
            return 2 + n;
        }
    }

    public override void WriteTo(IList<byte> dest, int resolvedSkip, int resolvedRel)
    {
        var pay = Payload;
        if (pay.Length % 2 != 0)
        {
            pay = [.. pay, 0];
        }

        FieldScriptIr.WriteU16(dest, WordHi | (pay.Length & 0x0FFF));
        foreach (var b in pay)
        {
            dest.Add(b);
        }
    }
}

internal static class FieldScriptIr
{
    public static List<FsItem> Parse(ReadOnlySpan<byte> bank, int start, int end, int maxOps = 20000)
    {
        end = Math.Min(end, bank.Length);
        var targets = new SortedSet<int>();
        var ip = start;
        for (var guard = 0; guard < maxOps && ip + 2 <= end; guard++)
        {
            var word = U16(bank, ip);
            var nibble = word >> 12;
            if (nibble == 0)
            {
                break;
            }

            var type = nibble - 1;
            if (type == 2)
            {
                targets.Add(ip + 2 + (word & 0x0FFF));
                ip += 2;
            }
            else if (type == 7 && ip + 4 <= end)
            {
                targets.Add(ip + 4 + (short)U16(bank, ip + 2));
                ip += 4;
            }
            else
            {
                ip += OpSize(bank, ip, word, end);
            }
        }

        var labels = new Dictionary<int, string>();
        var n = 0;
        foreach (var off in targets)
        {
            if (off >= start && off <= end)
            {
                labels[off] = $"L{n:0000}";
                n += 1;
            }
        }

        var ops = new List<FsItem>();
        ip = start;
        for (var guard = 0; guard < maxOps && ip + 2 <= end; guard++)
        {
            if (labels.TryGetValue(ip, out var at))
            {
                ops.Add(new FsLabel(at));
            }

            var word = U16(bank, ip);
            var nibble = word >> 12;
            if (nibble == 0)
            {
                if (ip < end)
                {
                    ops.Add(new FsRaw(bank[ip..end].ToArray()));
                }

                ip = end;
                break;
            }

            var type = nibble - 1;
            var size = OpSize(bank, ip, word, end);
            if (ip + size > end)
            {
                size = end - ip;
            }

            if (type == 0)
            {
                ops.Add(new FsWord(word));
            }
            else if (type is 1 or 8)
            {
                var pay = ip + 2 <= ip + size ? bank[(ip + 2)..(ip + size)].ToArray() : [];
                ops.Add(new FsSay(word & 0xF000, type == 8, pay));
            }
            else if (type == 2)
            {
                var skip = word & 0x0FFF;
                var dest = ip + 2 + skip;
                var name = labels.TryGetValue(dest, out var lb) ? lb : $"X{dest:X4}";
                ops.Add(new FsJump(word, name, skip));
            }
            else if (type == 3)
            {
                var arg = ip + 4 <= end ? U16(bank, ip + 2) : 0;
                var extra = size > 4 ? bank[(ip + 4)..(ip + size)].ToArray() : [];
                ops.Add(new FsBranch(word, arg, extra));
            }
            else if (type == 4)
            {
                var flag = ip + 4 <= end ? U16(bank, ip + 2) : 0;
                ops.Add(new FsFlag(word, flag));
            }
            else if (type == 6)
            {
                var raw = ip + 2 <= ip + size ? bank[(ip + 2)..(ip + size)].ToArray() : [];
                var sub = raw.Length >= 2 ? U16(raw, 0) & 0x3FFF : 0;
                ops.Add(new FsType6(word, sub, raw));
            }
            else if (type == 7)
            {
                var rel = ip + 4 <= end ? (short)U16(bank, ip + 2) : (short)0;
                var dest = ip + 4 + rel;
                var name = labels.TryGetValue(dest, out var lb) ? lb : $"X{dest:X4}";
                ops.Add(new FsRelJump(word, name, rel));
            }
            else if (type == 14)
            {
                ops.Add(new FsWord(word));
            }
            else
            {
                ops.Add(new FsRaw(bank[ip..(ip + size)].ToArray()));
            }

            ip += size;
            if (size <= 0)
            {
                break;
            }
        }

        if (labels.TryGetValue(ip, out var tail))
        {
            ops.Add(new FsLabel(tail));
        }

        return ops;
    }

    public static byte[] Emit(IReadOnlyList<FsItem> ops)
    {
        var sizes = new int[ops.Count];
        for (var i = 0; i < ops.Count; i++)
        {
            sizes[i] = ops[i].Size;
        }

        var offsets = new int[ops.Count];
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        var at = 0;
        for (var i = 0; i < ops.Count; i++)
        {
            offsets[i] = at;
            if (ops[i] is FsLabel lab)
            {
                labels[lab.Name] = at;
            }

            at += sizes[i];
        }

        var dest = new List<byte>(at);
        for (var i = 0; i < ops.Count; i++)
        {
            var item = ops[i];
            var skip = 0;
            var rel = 0;
            if (item is FsJump jmp)
            {
                skip = labels.TryGetValue(jmp.Target, out var t)
                    ? Math.Max(0, t - (offsets[i] + 2))
                    : jmp.OriginalSkip;
            }
            else if (item is FsRelJump rj)
            {
                rel = labels.TryGetValue(rj.Target, out var t) ? t - (offsets[i] + 4) : rj.OriginalRel;
            }

            item.WriteTo(dest, skip, rel);
        }

        return dest.ToArray();
    }

    internal static int Type6Extra(int word, int payloadWord)
    {
        var hi = (payloadWord >> 14) & 3;
        if (hi == 0)
        {
            return 0;
        }

        var n = (word & 0xF) + 1;
        if (hi == 1 && (n & 1) != 0)
        {
            n += 1;
        }

        return n << (hi - 1);
    }

    internal static void WriteU16(IList<byte> dest, int value)
    {
        dest.Add((byte)value);
        dest.Add((byte)(value >> 8));
    }

    internal static int U16(ReadOnlySpan<byte> data, int off) =>
        off + 1 < data.Length ? data[off] | (data[off + 1] << 8) : 0;

    private static int OpSize(ReadOnlySpan<byte> bank, int ip, int word, int end)
    {
        var nibble = word >> 12;
        if (nibble == 0)
        {
            return 2;
        }

        var type = nibble - 1;
        if (type is 1 or 8)
        {
            return 2 + (word & 0x0FFF);
        }

        if (type == 3)
        {
            return (word & 0x800) != 0 ? 6 : 4;
        }

        if (type is 4 or 7)
        {
            return 4;
        }

        if (type == 5)
        {
            return 6;
        }

        if (type == 6)
        {
            if (ip + 4 > end)
            {
                return 2;
            }

            return 4 + Type6Extra(word, U16(bank, ip + 2));
        }

        return 2;
    }
}
