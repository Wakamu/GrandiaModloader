namespace Grandia.Sdk;

public static partial class FieldHookAsm
{
    private static readonly Dictionary<int, string> HandlerKind = new()
    {
        [0x00] = "cam_word",
        [0x03] = "attach_pos",
        [0x04] = "field_bind",
        [0x06] = "attach_vis",
        [0x07] = "zone",
        [0x08] = "fx_pos",
        [0x09] = "sfx_fx",
        [0x0B] = "cam_nudge",
        [0x0C] = "flag_wait",
        [0x0D] = "camera_path",
        [0x0E] = "scene_boot",
        [0x11] = "attach_anim",
        [0x14] = "unit_bind",
        [0x15] = "param_block",
        [0x18] = "visibility",
        [0x1B] = "party_state",
        [0x1D] = "party_actor",
        [0x1E] = "sys_latch",
    };

    public static string FormatHook(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != HookRowSize)
        {
            throw new ArgumentException($"hook row length {raw.Length} != {HookRowSize}");
        }

        var pretty = TryPrettyHook(raw);
        if (pretty != null && BytesEqual(AssembleHook(pretty), raw))
        {
            return pretty;
        }

        return $"hook {raw[0]} hex={Convert.ToHexString(raw.ToArray()).ToLowerInvariant()}";
    }

    public static string FormatZone(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != ZoneRowSize)
        {
            throw new ArgumentException($"zone row length {raw.Length} != {ZoneRowSize}");
        }

        var pretty = TryPrettyZone(raw);
        if (pretty != null && BytesEqual(AssembleZone(pretty), raw))
        {
            return pretty;
        }

        return $"zone {raw[0]} hex={Convert.ToHexString(raw.ToArray()).ToLowerInvariant()}";
    }

    public static ZoneBox ReadAabb(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < ZoneRowSize)
        {
            return default;
        }

        return new ZoneBox(
            BitConverter.ToInt16(raw.Slice(20, 2)),
            BitConverter.ToInt16(raw.Slice(22, 2)),
            BitConverter.ToInt16(raw.Slice(24, 2)),
            BitConverter.ToInt16(raw.Slice(26, 2)),
            BitConverter.ToInt16(raw.Slice(28, 2)),
            BitConverter.ToInt16(raw.Slice(30, 2)));
    }

    private static string? TryPrettyZone(ReadOnlySpan<byte> raw)
    {
        if (raw.Length == ZoneRowSize && BitConverter.ToUInt16(raw) == 0x9300)
        {
            return TryPrettyChest(raw);
        }

        var head = raw[..HookRowSize].ToArray();
        var trig = head[2];
        var pretty = TryPrettyHook(head);
        if (pretty == null)
        {
            head[2] = 0;
            pretty = TryPrettyHook(head);
        }

        if (pretty == null || !pretty.StartsWith("hook ", StringComparison.Ordinal))
        {
            return null;
        }

        var line = "zone" + pretty[4..];
        line = AppendTrigToken(line, trig);
        return line + $" aabb={ReadAabb(raw)}";
    }

    private static string? TryPrettyChest(ReadOnlySpan<byte> raw)
    {
        var hid = raw[0];
        var ev = ((0x0A + raw[8]) << 8) | raw[9];
        var item = (raw[10] << 8) | raw[11];
        var parts = new List<string>
        {
            $"zone {hid}",
            "chest",
            $"event=0x{ev:X4}",
            $"item={FormatItemName(item)}",
        };
        if (raw[5] != 2)
        {
            parts.Add($"open={raw[5]}");
        }

        parts.Add($"kind={raw[6]}");
        if (raw[18] != 0)
        {
            parts.Add($"t18={raw[18]}");
        }

        if (raw[19] != 0)
        {
            parts.Add($"t19={raw[19]}");
        }

        if (raw[2] != 0)
        {
            parts.Add($"trig=0x{raw[2]:X2}");
        }

        parts.Add($"aabb={ReadAabb(raw)}");
        var hi = raw[1] >> 6;
        if (hi != 0)
        {
            parts.Add($"hi={hi}");
        }

        return string.Join(' ', parts);
    }

    private static string? TryPrettyHook(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != HookRowSize)
        {
            return null;
        }

        var hid = raw[0];
        var handler = raw[1] & 0x3F;
        var flags1 = raw[1] >> 6;
        var flags = raw[4];
        var delay = raw[0x10];
        var follow = raw[0x13];
        var parts = new List<string> { $"hook {hid}" };

        switch (handler)
        {
            case 0x01:
                parts.Add("present_channel");
                parts.Add(raw[5].ToString());
                OmitHex(parts, "flags", flags);
                Omit(parts, "delay", delay);
                Omit(parts, "follow", follow);
                break;
            case 0x1C:
            {
                var kids = new List<int>();
                var empty = false;
                foreach (var off in FanoutChildOffs)
                {
                    var id = raw[off];
                    if (id == 0)
                    {
                        empty = true;
                        continue;
                    }

                    if (empty)
                    {
                        return null;
                    }

                    kids.Add(id);
                }

                parts.Add("fanout");
                parts.AddRange(kids.Select(k => k.ToString()));
                if (flags != 0x90)
                {
                    parts.Add($"flags=0x{flags:X2}");
                }

                Omit(parts, "delay", raw[7]);
                if (raw[8] != 0)
                {
                    parts.Add($"modes=0x{raw[8]:X2}");
                }

                break;
            }
            case 0x02:
                parts.Add("setup");
                parts.Add($"dest=0x{Be16(raw, 5):X4}");
                var spawn = Be16(raw, 7);
                Omit(parts, "spawn", spawn);
                if (flags != 0x40)
                {
                    parts.Add($"flags=0x{flags:X2}");
                }

                Omit(parts, "aux9", raw[9]);
                Omit(parts, "auxA", raw[0xA]);
                Omit(parts, "delay", delay);
                var wx = (raw[0xF] << 8) | raw[0x11];
                var wz = (raw[0x12] << 8) | raw[0x13];
                if (wx != 0 || wz != 0)
                {
                    parts.Add($"walk={wx},{wz}");
                }
                else
                {
                    Omit(parts, "follow", follow);
                }

                AppendSetupGate(parts, raw);
                AppendTrig(parts, raw[2]);

                break;
            case 0x10:
                parts.Add("anim");
                parts.Add(raw[5].ToString());
                Omit(parts, "unit", raw[6]);
                var mode = (flags >> 4) & 3;
                if (mode != 1)
                {
                    parts.Add($"mode={mode}");
                }

                Omit(parts, "slot", raw[7]);
                if (flags != 0x90)
                {
                    parts.Add($"flags=0x{flags:X2}");
                }

                Omit(parts, "delay", delay);
                Omit(parts, "follow", follow);
                break;
            case 0x1A:
            {
                var sub = flags & 0xF;
                if (sub is 4 or 6)
                {
                    parts.Add(sub == 4 ? "open_amap" : "open_amap2");
                    if (raw[5] != 0)
                    {
                        parts.Add($"node={raw[5]}");
                    }
                }
                else
                {
                    parts.Add("cutscene");
                    parts.Add(CutsceneSubtypes.TryGetValue(sub, out var name) ? name : sub.ToString());
                    if (raw[5] != 0)
                    {
                        parts.Add($"param={raw[5]}");
                    }
                }

                if (flags != sub)
                {
                    parts.Add($"flags=0x{flags:X2}");
                }

                Omit(parts, "delay", delay);
                Omit(parts, "follow", follow);
                break;
            }
            case 0x19:
            {
                parts.Add("scripted_battle");
                parts.Add($"table=0x{raw[5]:X2}");
                var last = -1;
                var packed = new (int Index, int Count)[ScriptedBattlePairOffs.Length];
                for (var i = 0; i < ScriptedBattlePairOffs.Length; i++)
                {
                    var b = raw[ScriptedBattlePairOffs[i]];
                    packed[i] = (b >> 4, b & 0xF);
                    if (b != 0)
                    {
                        last = i;
                    }
                }

                for (var i = 0; i <= last; i++)
                {
                    parts.Add($"{packed[i].Index}x{packed[i].Count}");
                }

                var word = Be16(raw, 6);
                if (word != 0)
                {
                    parts.Add($"word=0x{word:X}");
                }

                if (flags != 0x40)
                {
                    parts.Add($"flags=0x{flags:X2}");
                }

                break;
            }
            default:
                if (!HandlerKind.TryGetValue(handler, out var kind))
                {
                    return null;
                }

                var layout = TryPrettyLayout(hid, kind, raw, flags, delay, follow, flags1);
                if (layout == null)
                {
                    return null;
                }

                if (flags1 != 0 && !layout.Contains("hi=", StringComparison.Ordinal))
                {
                    layout += $" hi={flags1}";
                }

                return AppendGatePretty(layout, raw, flags1);
        }

        if (handler != 0x02)
        {
            AppendSetupGate(parts, raw);
        }

        AppendTrig(parts, raw[2]);

        if (flags1 != 0)
        {
            parts.Add($"hi={flags1}");
        }

        return string.Join(' ', parts);
    }

    private static string AppendGatePretty(string layout, ReadOnlySpan<byte> raw, int flags1)
    {
        var parts = layout.Split(' ').ToList();
        if (!parts.Exists(p => p.StartsWith("if_", StringComparison.Ordinal) ||
                               p.StartsWith("gate=", StringComparison.Ordinal) ||
                               p.StartsWith("a=0x", StringComparison.Ordinal)))
        {
            AppendSetupGate(parts, raw);
        }

        AppendTrig(parts, raw[2]);

        if (flags1 != 0 && !parts.Exists(p => p.StartsWith("hi=", StringComparison.Ordinal)))
        {
            parts.Add($"hi={flags1}");
        }

        return string.Join(' ', parts);
    }

    private static string? TryPrettyLayout(int hid, string kind, ReadOnlySpan<byte> raw, int flags,
        int delay, int follow, int flags1)
    {
        var extra = new List<string>();
        var words = new List<string>();
        int? flagsEmit = flags != 0 ? flags : null;
        var delayEmit = delay;
        switch (kind)
        {
            case "cam_word":
                words.Add($"0x{Be16(raw, 5):X4}");
                Omit(extra, "hold", raw[7]);
                Omit(extra, "poll", raw[8]);
                Omit(extra, "auxB", raw[0xB]);
                Omit(extra, "auxF", raw[0xF]);
                AppendSetupGate(extra, raw);
                break;
            case "sys_latch":
                words.Add(NameOrId(SysLatchSubtypes, flags & 0xF));
                Omit(extra, "param", raw[5]);
                Omit(extra, "value", raw[6]);
                flagsEmit = flags == (0x40 | (flags & 0xF)) ? null : flags;
                return FmtLayout(hid, "sys_latch", words, flagsEmit, delay, follow, extra, flags1);
            case "param_block":
                words.Add($"0x{Be16(raw, 5):X4}");
                words.Add($"0x{Be16(raw, 7):X4}");
                words.Add($"0x{Be16(raw, 9):X4}");
                Omit(extra, "mode", raw[0xB]);
                break;
            case "visibility":
                words.Add(NameOrId(VisibilitySubtypes, raw[5]));
                words.Add(raw[6].ToString());
                Omit(extra, "hold", raw[0xF]);
                break;
            case "camera_path":
                words.Add(raw[5].ToString());
                Omit(extra, "stream", raw[6]);
                break;
            case "scene_boot":
                words.Add($"0x{raw[5]:X2}");
                break;
            case "unit_bind":
                words.Add(raw[5].ToString());
                words.Add(raw[6].ToString());
                Omit(extra, "aux", raw[7]);
                OmitHex(extra, "unk89", Be16(raw, 8), width: 4);
                break;
            case "sfx_fx":
                words.Add(raw[5].ToString());
                Omit(extra, "op", raw[6]);
                Omit(extra, "late", raw[0x12]);
                break;
            case "zone":
                words.Add(raw[5].ToString());
                words.Add(raw[6].ToString());
                Omit(extra, "hold", raw[0xF]);
                Omit(extra, "poll", raw[0x11]);
                break;
            case "attach_vis":
                words.Add(raw[5].ToString());
                Omit(extra, "single", raw[6]);
                Omit(extra, "hold", raw[0xF]);
                break;
            case "attach_anim":
                words.Add(raw[5].ToString());
                words.Add(raw[6].ToString());
                Omit(extra, "start", (raw[8] << 8) | raw[9]);
                Omit(extra, "end", (raw[0xA] << 8) | raw[0x12]);
                Omit(extra, "step", raw[0xB]);
                Omit(extra, "interp", raw[0xD]);
                Omit(extra, "hold", raw[0xF]);
                OmitHex(extra, "latch", raw[7]);
                break;
            case "flag_wait":
                words.Add(raw[5].ToString());
                words.Add(Be16(raw, 6).ToString());
                if ((flags & 0x20) != 0)
                {
                    Omit(extra, "stream", raw[8]);
                }
                else
                {
                    Omit(extra, "expect", raw[8]);
                }

                Omit(extra, "hold", raw[0xF]);
                break;
            case "field_bind":
            case "attach_pos":
            case "fx_pos":
                words.Add(raw[5].ToString());
                words.Add(raw[6].ToString());
                extra.Add($"delta={S8(raw[7])},{S8(raw[8])},{S8(raw[9])}");
                Omit(extra, "step", kind == "field_bind" ? raw[0xB] : raw[0xA]);
                Omit(extra, "hold", raw[0xF]);
                Omit(extra, "mid", raw[0x11]);
                Omit(extra, "late", raw[0x12]);
                break;
            case "cam_nudge":
            {
                extra.Add(
                    $"delta={S8(raw[5])},{S8(raw[6])},{S8(raw[7])},{S8(raw[8])},{S8(raw[9])},{S8(raw[10])}");
                Omit(extra, "hold", raw[0xF]);
                break;
            }
            case "party_actor":
                words.Add(NameOrId(PartyActorSubtypes, flags & 0xF));
                Omit(extra, "char", raw[5]);
                Omit(extra, "p0", raw[6]);
                Omit(extra, "p1", raw[7]);
                Omit(extra, "p2", raw[8]);
                Omit(extra, "p3", raw[9]);
                Omit(extra, "p4", raw[0xA]);
                Omit(extra, "p5", raw[0xB]);
                Omit(extra, "p6", raw[0x11]);
                flagsEmit = flags == (0x40 | (flags & 0xF)) ? null : flags;
                break;
            case "party_state":
                words.Add((flags & 0xF).ToString());
                OmitHex(extra, "start", Be16(raw, 5), width: 4);
                Omit(extra, "duration", (raw[0x12] << 8) | raw[0x10]);
                Omit(extra, "p7", raw[7]);
                Omit(extra, "p8", raw[8]);
                Omit(extra, "p9", raw[9]);
                Omit(extra, "pA", raw[0xA]);
                Omit(extra, "pB", raw[0xB]);
                Omit(extra, "hold", raw[0xF]);
                Omit(extra, "mid", raw[0x11]);
                delayEmit = 0;
                flagsEmit = (flags & 0xF0) == 0 ? null : flags;
                break;
            default:
                return null;
        }

        return FmtLayout(hid, kind, words, flagsEmit, delayEmit, follow, extra, flags1);
    }

    private static bool HasGateToken(IEnumerable<string> parts) =>
        parts.Any(p =>
            p.StartsWith("if_set=", StringComparison.Ordinal) ||
            p.StartsWith("if_clear=", StringComparison.Ordinal) ||
            p.StartsWith("gate=", StringComparison.Ordinal));

    /// <summary>
    /// When <c>if_set</c>/<c>if_clear</c> already encode the mode nibble, print only
    /// the walk/party bits (<c>trig=0x20</c>). Copying a pretty line and dropping the
    /// flags then keeps an always-on box instead of an impossible mode-5 gate.
    /// </summary>
    private static void AppendTrig(List<string> parts, byte trig)
    {
        if (trig == 0 || parts.Any(p => p.StartsWith("trig=", StringComparison.Ordinal)))
        {
            return;
        }

        var shown = HasGateToken(parts) ? (byte)(trig & 0xF0) : trig;
        if (shown != 0)
        {
            parts.Add($"trig=0x{shown:X2}");
        }
    }

    private static string AppendTrigToken(string line, byte trig)
    {
        if (trig == 0 || line.Contains("trig=", StringComparison.Ordinal))
        {
            return line;
        }

        var shown = HasGateToken(line.Split(' ')) ? (byte)(trig & 0xF0) : trig;
        return shown != 0 ? $"{line} trig=0x{shown:X2}" : line;
    }

    private static void AppendSetupGate(List<string> parts, ReadOnlySpan<byte> raw)
    {
        var f1 = ((raw[0xC] << 4) | (raw[0xD] >> 4)) & 0xFFF;
        var f2 = (((raw[0xD] & 0xF) << 8) | raw[0xE]) & 0xFFF;
        var mode = raw[2] & 7;
        var pol = (raw[2] & 0x08) != 0;
        if (mode == 1 && f1 != 0 && f2 == 0)
        {
            parts.Add($"{(pol ? "if_set" : "if_clear")}=0x{f1:X}");
        }
        else if (mode == 5 && f1 != 0 && pol)
        {
            parts.Add($"if_set=0x{f1:X}");
            parts.Add($"if_clear=0x{f2:X}");
        }
        else if (mode == 5 && f1 != 0)
        {
            parts.Add($"gate=5 a=0x{f1:X} b=0x{f2:X}");
        }
        else if (mode != 0)
        {
            parts.Add($"gate={mode}");
            if (f1 != 0)
            {
                parts.Add($"a=0x{f1:X}");
            }

            if (f2 != 0)
            {
                parts.Add($"b=0x{f2:X}");
            }
        }
        else
        {
            if (f1 != 0)
            {
                parts.Add($"a=0x{f1:X}");
            }

            if (f2 != 0)
            {
                parts.Add($"b=0x{f2:X}");
            }
        }
    }

    private static string FmtLayout(int hid, string kind, List<string> words, int? flags, int delay,
        int follow, List<string> extra, int hi)
    {
        var parts = new List<string> { $"hook {hid}", kind };
        parts.AddRange(words);
        parts.AddRange(extra);
        if (flags is int f)
        {
            parts.Add($"flags=0x{f:X2}");
        }

        Omit(parts, "delay", delay);
        Omit(parts, "follow", follow);
        if (hi != 0)
        {
            parts.Add($"hi={hi}");
        }

        return string.Join(' ', parts);
    }

    private static void Omit(List<string> parts, string key, int value)
    {
        if (value != 0)
        {
            parts.Add($"{key}={value}");
        }
    }

    private static void OmitHex(List<string> parts, string key, int value, int width = 2)
    {
        if (value != 0)
        {
            parts.Add($"{key}=0x{value.ToString("X" + width)}");
        }
    }

    private static string NameOrId(Dictionary<int, string> table, int key)
    {
        if (!table.TryGetValue(key, out var name))
        {
            return key.ToString();
        }

        return table.Values.Count(v => v == name) > 1 ? key.ToString() : name;
    }

    private static string FormatItemName(int id)
    {
        return Enum.IsDefined(typeof(Item), id)
            ? ((Item)id).ToString()
            : $"0x{id:X4}";
    }

    private static int Be16(ReadOnlySpan<byte> raw, int off) => (raw[off] << 8) | raw[off + 1];

    private static int S8(int b) => b > 127 ? b - 256 : b;

    private static bool BytesEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => a.SequenceEqual(b);
}

public readonly record struct ZoneBox(short XMin, short YMax, short ZMax, short XMax, short YMin, short ZMin)
{
    public override string ToString() => $"{XMin},{YMax},{ZMax},{XMax},{YMin},{ZMin}";
}
