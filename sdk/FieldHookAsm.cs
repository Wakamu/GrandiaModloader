using System.Globalization;
using System.Text.RegularExpressions;

namespace Grandia.Sdk;

/// <summary>
/// In-process MDP sec[7] hook / zone assembler (same mnemonics as
/// <c>field_tools hook</c> / <c>field_hook_asm.py</c>). Encode only after a write.
/// </summary>
public static partial class FieldHookAsm
{
    public const int HookRowSize = 20;
    public const int ZoneRowSize = 32;

    private static readonly int[] FanoutChildOffs = [9, 10, 11, 15, 16, 17, 18, 19];

    /// <summary>type_19 nibble pair slots: high=SpeciesIndex, low=Count.</summary>
    internal static readonly int[] ScriptedBattlePairOffs = [8, 9, 0xA, 0xB, 0xF, 0x10, 0x11, 0x12];

    private static readonly Dictionary<string, int> LayoutHandlers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cam_word"] = 0x00,
        ["sys_latch"] = 0x1E,
        ["param_block"] = 0x15,
        ["visibility"] = 0x18,
        ["camera_path"] = 0x0D,
        ["scene_boot"] = 0x0E,
        ["unit_bind"] = 0x14,
        ["sfx_fx"] = 0x09,
        ["zone"] = 0x07,
        ["attach_vis"] = 0x06,
        ["attach_anim"] = 0x11,
        ["flag_wait"] = 0x0C,
        ["field_bind"] = 0x04,
        ["attach_pos"] = 0x03,
        ["fx_pos"] = 0x08,
        ["cam_nudge"] = 0x0B,
        ["party_actor"] = 0x1D,
        ["party_state"] = 0x1B,
    };

    private static readonly Dictionary<int, string> PartyActorSubtypes = new()
    {
        [0] = "set_pos",
        [1] = "clear_busy",
        [2] = "set_timer",
        [3] = "walk",
        [4] = "set_dest_xyz",
        [5] = "set_busy",
        [6] = "set_field_byte",
        [7] = "set_party_byte",
        [8] = "instance_facing",
    };

    private static readonly Dictionary<int, string> VisibilitySubtypes = new()
    {
        [0] = "party_slot",
        [1] = "talk_id",
        [2] = "field_npc",
        [3] = "talk_mesh",
    };

    private static readonly Dictionary<int, string> SysLatchSubtypes = new()
    {
        [0] = "softhd_cue_8fe",
        [1] = "clear_713ea0",
        [2] = "present_flag_713f83",
        [3] = "cam_tween_1",
        [4] = "cam_tween_2",
        [5] = "cam_tween_3",
        [6] = "cam_tween_4",
        [7] = "set_719420",
        [8] = "nop",
        [9] = "arm_713ea0",
        [10] = "nop",
        [11] = "cam_tween_a",
    };

    private static readonly Dictionary<int, string> CutsceneSubtypes = new()
    {
        [0] = "nop",
        [1] = "fade",
        [2] = "talk_cast",
        [3] = "nop",
        [4] = "open_amap",
        [5] = "fade_mode",
        [6] = "open_amap2",
        [7] = "call_89220",
        [8] = "script_word",
        [9] = "fade_a",
        [10] = "fade_b",
        [11] = "multi_param",
    };

    private static readonly Regex HexBytes = new("^[0-9a-fA-F]*$", RegexOptions.CultureInvariant);

    public static byte[] AssembleHook(string line, int? hookId = null)
    {
        var tokens = Tokens(FirstCodeLine(line, "hook"));
        if (hookId is int hid)
        {
            tokens = NormalizeId(tokens, "hook", hid);
        }

        return ParseHook(tokens);
    }

    public static byte[] AssembleZone(string line)
    {
        var tokens = Tokens(FirstCodeLine(line, "zone"));
        return ParseZone(tokens);
    }

    internal static string NormalizeHookLine(string line, int id)
    {
        var trimmed = (line ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return $"hook {id}";
        }

        var toks = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (toks[0].Equals("hook", StringComparison.OrdinalIgnoreCase))
        {
            if (toks.Length >= 2 && LooksLikeInt(toks[1]))
            {
                toks[1] = id.ToString(CultureInfo.InvariantCulture);
                return string.Join(' ', toks);
            }

            return toks.Length == 1
                ? $"hook {id}"
                : $"hook {id} " + string.Join(' ', toks.Skip(1));
        }

        return $"hook {id} {trimmed}";
    }

    internal static string NormalizeZoneLine(string line)
    {
        var trimmed = (line ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return "zone 0";
        }

        var toks = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return toks[0].Equals("zone", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"zone 0 {trimmed}";
    }

    private static string FirstCodeLine(string text, string prefix)
    {
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = StripComment(raw);
            if (line.Length == 0)
            {
                continue;
            }

            var toks = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (toks[0].Equals("hooks", StringComparison.OrdinalIgnoreCase) ||
                toks[0].Equals("table", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line;
        }

        throw new ArgumentException($"no {prefix} line");
    }

    private static string[] NormalizeId(string[] tokens, string prefix, int id)
    {
        if (tokens.Length >= 2 && tokens[0].Equals(prefix, StringComparison.OrdinalIgnoreCase) &&
            LooksLikeInt(tokens[1]))
        {
            tokens[1] = id.ToString(CultureInfo.InvariantCulture);
            return tokens;
        }

        if (tokens.Length >= 1 && tokens[0].Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return [prefix, id.ToString(CultureInfo.InvariantCulture), .. tokens.Skip(1)];
        }

        return [prefix, id.ToString(CultureInfo.InvariantCulture), .. tokens];
    }

    private static byte[] ParseHook(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            throw new ArgumentException("hook line needs an id");
        }

        var hid = ParseInt(tokens[1]);
        if (hid is < 0 or > 0x7FFF)
        {
            throw new ArgumentException($"hook id {hid} out of 0..0x7FFF");
        }

        hid &= 0xFF;
        SplitTokens(tokens.AsSpan(2), out var pos, out var kv);
        if (kv.TryGetValue("hex", out var hex) ||
            (pos.Count > 0 && pos[0].Equals("raw", StringComparison.OrdinalIgnoreCase) && kv.ContainsKey("hex")))
        {
            var raw = ParseHexBytes(kv["hex"]);
            if (raw.Length != HookRowSize)
            {
                throw new ArgumentException($"hex= must be 20 bytes, got {raw.Length}");
            }

            if (raw[0] != hid)
            {
                throw new ArgumentException($"hook {hid} does not match hex id {raw[0]}");
            }

            return raw;
        }

        var kind = pos.Count > 0 ? pos[0] : "";
        var args = pos.Count > 1 ? pos.Skip(1).ToList() : [];
        int? flags = kv.TryGetValue("flags", out var fs) ? ParseInt(fs) : null;
        var delay = kv.TryGetValue("delay", out var ds) ? ParseInt(ds) : 0;
        var follow = kv.TryGetValue("follow", out var fol) ? ParseInt(fol) : 0;
        var flags1 = kv.TryGetValue("hi", out var hi) ? ParseInt(hi) : 0;

        if (kind.Equals("present_channel", StringComparison.OrdinalIgnoreCase))
        {
            var ch = args.Count > 0 ? ParseInt(args[0]) : 0;
            return ApplyKvGate(BuildPresentChannel(hid, ch, delay, follow, flags ?? 0, flags1), kv);
        }

        if (kind.Equals("fanout", StringComparison.OrdinalIgnoreCase))
        {
            var kids = args.Select(ParseInt).ToList();
            var modes = kv.TryGetValue("modes", out var ms) ? ParseInt(ms) : 0;
            return ApplyKvGate(BuildFanout(hid, kids, delay, modes, flags ?? 0x90, flags1), kv);
        }

        if (kind.Equals("setup", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyKvGate(ParseSetup(hid, kv, flags, delay, follow, flags1), kv);
        }

        if (kind.Equals("scripted_battle", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("type_19", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyKvGate(ParseScriptedBattle(hid, args, kv, flags, flags1), kv);
        }

        if (kind.Equals("anim", StringComparison.OrdinalIgnoreCase))
        {
            var anim = args.Count > 0 ? ParseInt(args[0]) : 0;
            var unit = kv.TryGetValue("unit", out var un) ? ParseInt(un) : 0;
            var mode = kv.TryGetValue("mode", out var md) ? ParseInt(md) : 1;
            var slot = kv.TryGetValue("slot", out var sl) ? ParseInt(sl) : 0;
            return ApplyKvGate(
                BuildAnimLatch(hid, anim, unit, mode, delay, follow, slot, flags ?? 0x90, flags1), kv);
        }

        if (kind.Equals("open_amap", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("open_amap2", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("cutscene", StringComparison.OrdinalIgnoreCase))
        {
            int subtype;
            int param;
            if (kind.Equals("open_amap", StringComparison.OrdinalIgnoreCase))
            {
                subtype = 4;
                param = kv.TryGetValue("node", out var n) ? ParseInt(n) : (args.Count > 0 ? ParseInt(args[0]) : 0);
            }
            else if (kind.Equals("open_amap2", StringComparison.OrdinalIgnoreCase))
            {
                subtype = 6;
                param = kv.TryGetValue("node", out var n2) ? ParseInt(n2) : (args.Count > 0 ? ParseInt(args[0]) : 0);
            }
            else
            {
                if (args.Count == 0)
                {
                    throw new ArgumentException("cutscene needs a subtype");
                }

                subtype = LookupLastWins(CutsceneSubtypes, args[0]);
                param = kv.TryGetValue("param", out var p) ? ParseInt(p) : (args.Count > 1 ? ParseInt(args[1]) : 0);
            }

            return ApplyKvGate(
                BuildCutscene(hid, subtype, param, flags ?? subtype, delay, follow, flags1), kv);
        }

        if (LayoutHandlers.ContainsKey(kind))
        {
            return ApplyKvGate(
                ParseLayout(hid, kind.ToLowerInvariant(), args, kv, flags, delay, follow, flags1), kv);
        }

        throw new ArgumentException($"unknown hook kind '{kind}' (use hex= for raw rows)");
    }

    private static byte[] ParseSetup(int hid, Dictionary<string, string> kv, int? flags, int delay,
        int follow, int flags1)
    {
        var dest = kv.TryGetValue("dest", out var d) ? ParseInt(d) : 0;
        var spawn = kv.TryGetValue("spawn", out var s) ? ParseInt(s) : 0;
        var aux9 = kv.TryGetValue("aux9", out var a9) ? ParseInt(a9) : 0;
        var auxA = kv.TryGetValue("auxA", out var aa) ? ParseInt(aa) : 0;
        var walkX = 0;
        var walkZ = 0;
        if (kv.TryGetValue("walk", out var walk))
        {
            var bits = walk.Split(',');
            if (bits.Length != 2)
            {
                throw new ArgumentException("setup walk= needs x,z");
            }

            walkX = ParseInt(bits[0]);
            walkZ = ParseInt(bits[1]);
        }

        var flag1 = kv.TryGetValue("a", out var ga) ? ParseInt(ga) : 0;
        var flag2 = kv.TryGetValue("b", out var gb) ? ParseInt(gb) : 0;
        if (kv.ContainsKey("if_clear") && kv.ContainsKey("if_set"))
        {
            flag1 = ParseInt(kv["if_set"]);
            flag2 = ParseInt(kv["if_clear"]);
        }
        else if (kv.TryGetValue("if_clear", out var ic))
        {
            flag1 = ParseInt(ic);
        }
        else if (kv.TryGetValue("if_set", out var ist))
        {
            flag1 = ParseInt(ist);
        }

        return BuildSetup(hid, dest, spawn, flags ?? 0x40, aux9, auxA, delay, follow, walkX, walkZ,
            flags1, flag1, flag2);
    }

    private static byte[] ParseScriptedBattle(int hid, List<string> args, Dictionary<string, string> kv,
        int? flags, int flags1)
    {
        var table = kv.TryGetValue("table", out var t) ? ParseInt(t) : (int?)null;
        var pairs = new List<(int Index, int Count)>();
        foreach (var arg in args)
        {
            if (TryParseBattlePair(arg, out var idx, out var cnt))
            {
                pairs.Add((idx, cnt));
                continue;
            }

            if (table == null && pairs.Count == 0)
            {
                table = ParseInt(arg);
                continue;
            }

            throw new ArgumentException($"scripted_battle unknown arg '{arg}' (want table= and NxC pairs)");
        }

        if (table == null)
        {
            throw new ArgumentException("scripted_battle needs table=");
        }

        var word = kv.TryGetValue("word", out var w) ? ParseInt(w) : 0;
        return BuildScriptedBattle(hid, table.Value, pairs, word, flags, flags1);
    }

    private static bool TryParseBattlePair(string token, out int index, out int count)
    {
        index = 0;
        count = 0;
        var x = token.IndexOf('x');
        if (x < 0)
        {
            x = token.IndexOf('X');
        }

        if (x <= 0 || x >= token.Length - 1)
        {
            return false;
        }

        index = ParseInt(token[..x]);
        count = ParseInt(token[(x + 1)..]);
        return true;
    }

    private static byte[] ApplyKvGate(byte[] row, Dictionary<string, string> kv)
    {
        var flag1 = kv.TryGetValue("a", out var ga) ? ParseInt(ga) : 0;
        var flag2 = kv.TryGetValue("b", out var gb) ? ParseInt(gb) : 0;
        var mode5And = kv.ContainsKey("if_clear") && kv.ContainsKey("if_set");
        if (mode5And)
        {
            flag1 = ParseInt(kv["if_set"]);
            flag2 = ParseInt(kv["if_clear"]);
        }
        else if (kv.TryGetValue("if_clear", out var ic))
        {
            flag1 = ParseInt(ic);
            flag2 = 0;
        }
        else if (kv.TryGetValue("if_set", out var ist))
        {
            flag1 = ParseInt(ist);
            flag2 = 0;
        }

        if (flag1 != 0 || flag2 != 0)
        {
            PackSetupGate(flag1, flag2, out row[0xC], out row[0xD], out row[0xE]);
        }

        if (kv.TryGetValue("trig", out var tr))
        {
            row[2] = (byte)U8(ParseInt(tr));
        }

        if (mode5And)
        {
            row[2] = (byte)((row[2] & 0xF0) | 0x0D);
        }
        else if (kv.ContainsKey("if_set"))
        {
            row[2] = (byte)((row[2] & 0xF0) | 0x09);
        }
        else if (kv.ContainsKey("if_clear"))
        {
            row[2] = (byte)((row[2] & 0xF0) | 0x01);
        }
        else if (kv.TryGetValue("gate", out var gate))
        {
            row[2] = (byte)((row[2] & 0xF8) | (ParseInt(gate) & 7));
        }

        return row;
    }

    private static byte[] ParseLayout(int hid, string kind, List<string> args,
        Dictionary<string, string> kv, int? flags, int delay, int follow, int flags1)
    {
        var handler = LayoutHandlers[kind];
        var fields = new Dictionary<int, int>();
        if (kind != "party_state" && delay != 0)
        {
            fields[0x10] = U8(delay);
        }

        if (follow != 0)
        {
            fields[0x13] = U8(follow);
        }

        int Pos(int i, int fallback = 0) => i < args.Count ? ParseInt(args[i]) : fallback;

        switch (kind)
        {
            case "cam_word":
                PutBe16(fields, 5, Pos(0));
                PutOptU8(fields, kv, "hold", 7);
                PutOptU8(fields, kv, "poll", 8);
                PutOptU8(fields, kv, "auxB", 0xB);
                PutOptU8(fields, kv, "auxF", 0xF);
                PutFlags(fields, flags);
                break;
            case "sys_latch":
                if (args.Count == 0)
                {
                    throw new ArgumentException("sys_latch needs a subtype");
                }

                return BuildSysLatch(hid, LookupUnique(SysLatchSubtypes, args[0]) & 0xF,
                    kv.TryGetValue("param", out var p) ? ParseInt(p) : 0,
                    kv.TryGetValue("value", out var v) ? ParseInt(v) : 0,
                    flags, delay, follow, flags1);
            case "param_block":
                PutBe16(fields, 5, Pos(0));
                PutBe16(fields, 7, Pos(1));
                PutBe16(fields, 9, Pos(2));
                PutOptU8(fields, kv, "mode", 0xB);
                PutFlags(fields, flags);
                break;
            case "visibility":
                if (args.Count == 0)
                {
                    throw new ArgumentException("visibility needs a subtype");
                }

                fields[5] = U8(LookupUnique(VisibilitySubtypes, args[0]));
                fields[6] = U8(Pos(1));
                PutOptU8(fields, kv, "hold", 0xF);
                PutFlags(fields, flags);
                break;
            case "camera_path":
                fields[5] = U8(Pos(0));
                PutOptU8(fields, kv, "stream", 6);
                PutFlags(fields, flags);
                break;
            case "scene_boot":
                fields[5] = U8(Pos(0));
                PutFlags(fields, flags);
                break;
            case "unit_bind":
                fields[5] = U8(Pos(0));
                fields[6] = U8(Pos(1));
                PutOptU8(fields, kv, "aux", 7);
                if (kv.TryGetValue("unk89", out var u89))
                {
                    PutBe16(fields, 8, ParseInt(u89));
                }

                PutFlags(fields, flags);
                break;
            case "sfx_fx":
                fields[5] = U8(Pos(0));
                PutOptU8(fields, kv, "op", 6);
                PutOptU8(fields, kv, "late", 0x12);
                PutFlags(fields, flags);
                break;
            case "zone":
                fields[5] = U8(Pos(0));
                fields[6] = U8(Pos(1));
                PutOptU8(fields, kv, "hold", 0xF);
                PutOptU8(fields, kv, "poll", 0x11);
                PutFlags(fields, flags);
                break;
            case "attach_vis":
                fields[5] = U8(Pos(0));
                PutOptU8(fields, kv, "single", 6);
                PutOptU8(fields, kv, "hold", 0xF);
                PutFlags(fields, flags);
                break;
            case "attach_anim":
                fields[5] = U8(Pos(0));
                fields[6] = U8(Pos(1));
                if (kv.TryGetValue("start", out var st))
                {
                    PutBe16(fields, 8, ParseInt(st));
                }

                if (kv.TryGetValue("end", out var en))
                {
                    var end = ParseInt(en);
                    if (end is < 0 or > 0xFFFF)
                    {
                        throw new ArgumentException($"end {end} out of range");
                    }

                    fields[0xA] = (end >> 8) & 0xFF;
                    fields[0x12] = end & 0xFF;
                }

                PutOptU8(fields, kv, "step", 0xB);
                PutOptU8(fields, kv, "interp", 0xD);
                PutOptU8(fields, kv, "hold", 0xF);
                PutOptU8(fields, kv, "latch", 7);
                PutFlags(fields, flags);
                break;
            case "flag_wait":
                fields[5] = U8(Pos(0));
                PutBe16(fields, 6, Pos(1));
                PutOptU8(fields, kv, "expect", 8);
                PutOptU8(fields, kv, "stream", 8);
                PutOptU8(fields, kv, "hold", 0xF);
                PutFlags(fields, flags);
                break;
            case "field_bind":
            case "attach_pos":
            case "fx_pos":
                fields[5] = U8(Pos(0));
                fields[6] = U8(Pos(1));
                if (kv.TryGetValue("delta", out var delta))
                {
                    var d = ParseCsv(delta, 3, "delta");
                    fields[7] = U8(d[0]);
                    fields[8] = U8(d[1]);
                    fields[9] = U8(d[2]);
                }

                PutOptU8(fields, kv, "step", kind == "field_bind" ? 0xB : 0xA);
                PutOptU8(fields, kv, "hold", 0xF);
                PutOptU8(fields, kv, "mid", 0x11);
                PutOptU8(fields, kv, "late", 0x12);
                PutFlags(fields, flags);
                break;
            case "cam_nudge":
                if (kv.TryGetValue("delta", out var nd))
                {
                    var vals = ParseCsv(nd, 6, "cam_nudge delta");
                    for (var i = 0; i < vals.Count; i++)
                    {
                        fields[5 + i] = U8(vals[i]);
                    }
                }

                PutOptU8(fields, kv, "hold", 0xF);
                PutFlags(fields, flags);
                break;
            case "party_actor":
                if (args.Count == 0)
                {
                    throw new ArgumentException("party_actor needs a subtype");
                }

                var sub = LookupUnique(PartyActorSubtypes, args[0]) & 0xF;
                fields[4] = U8(flags ?? (0x40 | sub));
                PutOptU8(fields, kv, "char", 5);
                PutOptU8(fields, kv, "p0", 6);
                PutOptU8(fields, kv, "p1", 7);
                PutOptU8(fields, kv, "p2", 8);
                PutOptU8(fields, kv, "p3", 9);
                PutOptU8(fields, kv, "p4", 0xA);
                PutOptU8(fields, kv, "p5", 0xB);
                PutOptU8(fields, kv, "p6", 0x11);
                break;
            case "party_state":
                fields[4] = U8(flags ?? (Pos(0) & 0xF));
                if (kv.TryGetValue("start", out var start))
                {
                    PutBe16(fields, 5, ParseInt(start));
                }

                if (kv.TryGetValue("duration", out var durS))
                {
                    var dur = ParseInt(durS);
                    if (dur is < 0 or > 0xFFFF)
                    {
                        throw new ArgumentException($"duration {dur} out of range");
                    }

                    fields[0x10] = dur & 0xFF;
                    fields[0x12] = (dur >> 8) & 0xFF;
                }

                PutOptU8(fields, kv, "p7", 7);
                PutOptU8(fields, kv, "p8", 8);
                PutOptU8(fields, kv, "p9", 9);
                PutOptU8(fields, kv, "pA", 0xA);
                PutOptU8(fields, kv, "pB", 0xB);
                PutOptU8(fields, kv, "hold", 0xF);
                PutOptU8(fields, kv, "mid", 0x11);
                break;
            default:
                throw new ArgumentException($"unknown hook kind '{kind}' (use hex= for raw rows)");
        }

        return BuildTyped(hid, handler, fields, flags1);
    }

    private static byte[] ParseZone(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            throw new ArgumentException("zone line needs an id");
        }

        var hid = ParseInt(tokens[1]);
        if (hid is < 0 or > 255)
        {
            throw new ArgumentException($"zone id {hid} out of 0..255");
        }

        SplitTokens(tokens.AsSpan(2), out var pos, out var kv);
        if (kv.TryGetValue("hex", out var hex) ||
            (pos.Count > 0 && pos[0].Equals("raw", StringComparison.OrdinalIgnoreCase) && kv.ContainsKey("hex")))
        {
            var raw = ParseHexBytes(kv["hex"]);
            if (raw.Length != ZoneRowSize)
            {
                throw new ArgumentException($"hex= must be 32 bytes, got {raw.Length}");
            }

            if (raw[0] != hid)
            {
                throw new ArgumentException($"zone {hid} does not match hex id {raw[0]}");
            }

            return raw;
        }

        var trig = kv.TryGetValue("trig", out var tr) ? ParseInt(tr) : 0;
        var flags1 = kv.TryGetValue("hi", out var hi) ? ParseInt(hi) : 0;
        var kind = pos.Count > 0 ? pos[0] : "";
        if (kind.Equals("chest", StringComparison.OrdinalIgnoreCase))
        {
            return ParseChest(hid, kv, flags1, trig);
        }

        if (!kv.ContainsKey("aabb"))
        {
            throw new ArgumentException("zone line needs aabb= (or hex=)");
        }

        var aabb = ParseAabb(kv["aabb"]);
        var hookKv = kv.Where(p => p.Key is not "aabb" and not "trig" and not "hex")
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var hookTokens = new List<string> { "hook", hid.ToString(CultureInfo.InvariantCulture) };
        hookTokens.AddRange(pos);
        foreach (var (key, val) in hookKv)
        {
            hookTokens.Add($"{key}={val}");
        }

        var head = ParseHook(hookTokens.ToArray()).ToArray();
        if (trig != 0)
        {
            if ((head[2] & 0x0F) != 0)
            {
                head[2] = (byte)((U8(trig) & 0xF0) | (head[2] & 0x0F));
            }
            else
            {
                head[2] = (byte)U8(trig);
            }
        }
        else if ((head[1] & 0x3F) == 0x02 && (head[2] & 7) != 0 && (head[2] & 0xF0) == 0)
        {
            head[2] |= 0x20;
        }

        var outp = new byte[ZoneRowSize];
        head.CopyTo(outp, 0);
        aabb.CopyTo(outp, 20);
        return outp;
    }

    private static byte[] ParseChest(int hid, Dictionary<string, string> kv, int flags1, int trig)
    {
        if (!kv.ContainsKey("event") || !kv.ContainsKey("item") || !kv.ContainsKey("aabb"))
        {
            throw new ArgumentException("chest needs event=, item=, and aabb=");
        }

        var ev = ParseInt(kv["event"]);
        var item = ParseItem(kv["item"]);
        if (item is < 0 or > 0xFFFF)
        {
            throw new ArgumentException($"item {item} out of range");
        }

        var kind = kv.TryGetValue("kind", out var k) ? ParseInt(k) : 0;
        var open = kv.TryGetValue("open", out var o) ? ParseInt(o) : 2;
        var t18 = kv.TryGetValue("t18", out var a) ? ParseInt(a) : 0;
        var t19 = kv.TryGetValue("t19", out var b) ? ParseInt(b) : 0;
        var bank = ((ev >> 8) - 0x0A) & 0xFF;
        var fields = new Dictionary<int, int>
        {
            [5] = U8(open),
            [6] = U8(kind),
            [8] = U8(bank),
            [9] = U8(ev & 0xFF),
            [10] = (item >> 8) & 0xFF,
            [11] = item & 0xFF,
        };
        if (trig != 0)
        {
            fields[2] = U8(trig);
        }

        if (t18 != 0)
        {
            fields[18] = U8(t18);
        }

        if (t19 != 0)
        {
            fields[19] = U8(t19);
        }

        var head = BuildTyped(hid, 0x13, fields, flags1);
        var aabb = ParseAabb(kv["aabb"]);
        var outp = new byte[ZoneRowSize];
        head.CopyTo(outp, 0);
        aabb.CopyTo(outp, 20);
        return outp;
    }

    internal static byte[] BuildSetup(int hookId, int pair0, int pair1, int rowFlags, int aux9,
        int auxA, int delay, int follow, int walkX, int walkZ, int flags1, int gate1, int gate2)
    {
        var raw = new byte[HookRowSize];
        raw[0] = (byte)U8(hookId);
        raw[1] = HandlerByte(0x02, flags1);
        raw[4] = (byte)U8(rowFlags);
        PutBe16(raw, 5, pair0);
        PutBe16(raw, 7, pair1);
        raw[9] = (byte)U8(aux9);
        raw[0xA] = (byte)U8(auxA);
        raw[0x10] = (byte)U8(delay);
        if (gate1 != 0 || gate2 != 0)
        {
            PackSetupGate(gate1, gate2, out raw[0xC], out raw[0xD], out raw[0xE]);
        }

        var wx = walkX & 0xFFFF;
        var wz = walkZ & 0xFFFF;
        if (wx != 0 || wz != 0)
        {
            raw[0xF] = (byte)((wx >> 8) & 0xFF);
            raw[0x11] = (byte)(wx & 0xFF);
            raw[0x12] = (byte)((wz >> 8) & 0xFF);
            raw[0x13] = (byte)(wz & 0xFF);
        }
        else
        {
            raw[0x13] = (byte)U8(follow);
        }

        return raw;
    }

    internal static byte[] BuildPresentChannel(int hookId, int channel, int delay, int follow,
        int rowFlags, int flags1)
    {
        if (channel is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), "channel must be 0..7");
        }

        var raw = new byte[HookRowSize];
        raw[0] = (byte)U8(hookId);
        raw[1] = HandlerByte(0x01, flags1);
        raw[4] = (byte)U8(rowFlags);
        raw[5] = (byte)(channel & 7);
        raw[0x10] = (byte)U8(delay);
        raw[0x13] = (byte)U8(follow);
        return raw;
    }

    internal static byte[] BuildFanout(int hookId, IReadOnlyList<int> children, int delay, int modes,
        int rowFlags, int flags1)
    {
        var raw = new byte[HookRowSize];
        raw[0] = (byte)U8(hookId);
        raw[1] = HandlerByte(0x1C, flags1);
        raw[4] = (byte)U8(rowFlags);
        raw[7] = (byte)U8(delay);
        raw[8] = (byte)U8(modes);
        for (var i = 0; i < FanoutChildOffs.Length; i++)
        {
            raw[FanoutChildOffs[i]] = (byte)U8(i < children.Count ? children[i] : 0);
        }

        return raw;
    }

    internal static byte[] BuildAnimLatch(int hookId, int animId, int unitKey, int latchMode,
        int delay, int follow, int slot, int rowFlags, int flags1)
    {
        var raw = new byte[HookRowSize];
        raw[0] = (byte)U8(hookId);
        raw[1] = HandlerByte(0x10, flags1);
        var mode = latchMode & 3;
        raw[4] = (byte)((U8(rowFlags) & ~(0x3 << 4)) | (mode << 4));
        raw[5] = (byte)U8(animId);
        raw[6] = (byte)U8(unitKey);
        raw[7] = (byte)U8(slot);
        raw[0x10] = (byte)U8(delay);
        raw[0x13] = (byte)U8(follow);
        return raw;
    }

    internal static byte[] BuildCutscene(int hookId, int subtype, int param, int rowFlags, int delay,
        int follow, int flags1)
    {
        var raw = new byte[HookRowSize];
        raw[0] = (byte)U8(hookId);
        raw[1] = HandlerByte(0x1A, flags1);
        raw[4] = (byte)U8(rowFlags);
        raw[5] = (byte)U8(param);
        raw[0x10] = (byte)U8(delay);
        raw[0x13] = (byte)U8(follow);
        return raw;
    }

    internal static byte[] BuildScriptedBattle(int hookId, int table, IReadOnlyList<(int Index, int Count)> pairs,
        int word, int? flags, int flags1)
    {
        var fields = new Dictionary<int, int> { [4] = U8(flags ?? 0x40), [5] = U8(table) };
        PutBe16(fields, 6, word);
        if (pairs.Count > ScriptedBattlePairOffs.Length)
        {
            throw new ArgumentException(
                $"scripted_battle allows {ScriptedBattlePairOffs.Length} pairs, got {pairs.Count}");
        }

        for (var i = 0; i < pairs.Count; i++)
        {
            var (idx, cnt) = pairs[i];
            if (idx is < 0 or > 15 || cnt is < 0 or > 15)
            {
                throw new ArgumentException("scripted_battle pair index/count must be 0..15");
            }

            fields[ScriptedBattlePairOffs[i]] = ((idx & 0xF) << 4) | (cnt & 0xF);
        }

        return BuildTyped(hookId, 0x19, fields, flags1);
    }

    internal static byte[] BuildSysLatch(int hookId, int subtype, int param, int value, int? rowFlags,
        int delay, int follow, int flags1)
    {
        var raw = new byte[HookRowSize];
        raw[0] = (byte)U8(hookId);
        raw[1] = HandlerByte(0x1E, flags1);
        var sub = subtype & 0xF;
        raw[4] = (byte)U8(rowFlags ?? (0x40 | sub));
        raw[5] = (byte)U8(param);
        raw[6] = (byte)U8(value);
        raw[0x10] = (byte)U8(delay);
        raw[0x13] = (byte)U8(follow);
        return raw;
    }

    internal static byte[] BuildTyped(int hookId, int handler, Dictionary<int, int> fields, int flags1)
    {
        var raw = new byte[HookRowSize];
        raw[0] = (byte)U8(hookId);
        raw[1] = HandlerByte(handler, flags1);
        foreach (var (off, val) in fields)
        {
            if (off is < 2 or > 19)
            {
                throw new ArgumentException($"typed row offset {off} out of range");
            }

            raw[off] = (byte)U8(val);
        }

        return raw;
    }

    internal static int SetupDest(ReadOnlySpan<byte> row)
    {
        if (row.Length < 7 || (row[1] & 0x3F) != 0x02)
        {
            return -1;
        }

        return (row[5] << 8) | row[6];
    }

    private static void PackSetupGate(int flag1, int flag2, out byte c, out byte d, out byte e)
    {
        flag1 &= 0xFFF;
        flag2 &= 0xFFF;
        c = (byte)((flag1 >> 4) & 0xFF);
        d = (byte)(((flag1 & 0xF) << 4) | ((flag2 >> 8) & 0xF));
        e = (byte)(flag2 & 0xFF);
    }

    private static byte HandlerByte(int handler, int flags1) =>
        (byte)(((flags1 & 3) << 6) | (handler & 0x3F));

    private static void PutBe16(byte[] raw, int off, int value)
    {
        var x = value & 0xFFFF;
        raw[off] = (byte)((x >> 8) & 0xFF);
        raw[off + 1] = (byte)(x & 0xFF);
    }

    private static void PutBe16(Dictionary<int, int> fields, int off, int value)
    {
        if (value is < 0 or > 0xFFFF)
        {
            throw new ArgumentException($"u16 {value} out of range");
        }

        fields[off] = (value >> 8) & 0xFF;
        fields[off + 1] = value & 0xFF;
    }

    private static void PutOptU8(Dictionary<int, int> fields, Dictionary<string, string> kv, string key,
        int off)
    {
        if (kv.TryGetValue(key, out var s))
        {
            fields[off] = U8(ParseInt(s));
        }
    }

    private static void PutFlags(Dictionary<int, int> fields, int? flags)
    {
        if (flags is int f)
        {
            fields[4] = U8(f);
        }
    }

    private static byte[] ParseAabb(string text)
    {
        var vals = ParseCsv(text, 6, "aabb");
        var outp = new byte[12];
        for (var i = 0; i < 6; i++)
        {
            var v = vals[i];
            if (v is < -32768 or > 32767)
            {
                throw new ArgumentException($"aabb value {v} out of s16");
            }

            BitConverter.TryWriteBytes(outp.AsSpan(i * 2), (short)v);
        }

        return outp;
    }

    private static List<int> ParseCsv(string text, int count, string what)
    {
        var bits = text.Split(',');
        if (bits.Length != count)
        {
            throw new ArgumentException($"{what} needs {count} comma-separated values");
        }

        return bits.Select(ParseInt).ToList();
    }

    private static int ParseItem(string token)
    {
        if (LooksLikeInt(token))
        {
            return ParseInt(token);
        }

        if (Enum.TryParse<Item>(token, ignoreCase: true, out var item))
        {
            return (int)item;
        }

        var norm = Alnum(token);
        foreach (var value in Enum.GetValues<Item>())
        {
            if (string.Equals(Alnum(value.ToString()), norm, StringComparison.OrdinalIgnoreCase))
            {
                return (int)value;
            }
        }

        throw new ArgumentException($"unknown item '{token}'");
    }

    private static string Alnum(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());

    private static int LookupUnique(Dictionary<int, string> table, string token)
    {
        var counts = table.Values.GroupBy(v => v, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (id, name) in table)
        {
            if (counts.TryGetValue(name, out var n) && n == 1 &&
                name.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        return ParseInt(token);
    }

    private static int LookupLastWins(Dictionary<int, string> table, string token)
    {
        var found = -1;
        foreach (var (id, name) in table)
        {
            if (name.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                found = id;
            }
        }

        return found >= 0 ? found : ParseInt(token);
    }

    private static void SplitTokens(ReadOnlySpan<string> rest, out List<string> pos,
        out Dictionary<string, string> kv)
    {
        pos = [];
        kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tok in rest)
        {
            var eq = tok.IndexOf('=');
            if (eq > 0)
            {
                kv[tok[..eq]] = tok[(eq + 1)..];
            }
            else
            {
                pos.Add(tok);
            }
        }
    }

    private static string[] Tokens(string line) =>
        StripComment(line).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string StripComment(string line)
    {
        var i = line.IndexOf('#');
        return (i >= 0 ? line[..i] : line).Trim();
    }

    private static int U8(int n)
    {
        if (n < 0)
        {
            n &= 0xFF;
        }

        if (n is < 0 or > 255)
        {
            throw new ArgumentException($"byte {n} out of 0..255");
        }

        return n;
    }

    internal static int ParseInt(string raw)
    {
        raw = raw.Trim();
        var neg = raw.StartsWith('-');
        if (neg)
        {
            raw = raw[1..];
        }

        var v = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(raw, CultureInfo.InvariantCulture);
        return neg ? -v : v;
    }

    private static byte[] ParseHexBytes(string text)
    {
        var h = Regex.Replace(text, @"\s+", "");
        if (h.Length % 2 != 0 || (h.Length > 0 && !HexBytes.IsMatch(h)))
        {
            throw new ArgumentException($"invalid hex: {text}");
        }

        return h.Length == 0 ? [] : Convert.FromHexString(h);
    }

    internal static bool LooksLikeInt(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (token.StartsWith('-'))
        {
            token = token[1..];
        }

        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        }

        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }
}
