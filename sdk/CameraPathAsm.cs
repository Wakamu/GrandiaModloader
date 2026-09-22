using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Grandia.Sdk;

/// <summary>
/// In-process sec[15] camera-path assembler. Same mnemonics as
/// <c>parse_mdp_sec15.py</c> / <see cref="MapCameraPathOp"/>. Offsets
/// (<c>@12</c>) and a <c>camera_path N</c> envelope are accepted on
/// parse and omitted from <see cref="MapCameraPath.Lines"/>.
/// </summary>
public static class CameraPathAsm
{
    private static readonly Regex OffsetTok = new(@"^@\d+$", RegexOptions.CultureInvariant);
    private static readonly Dictionary<string, MapCameraPathChannel> Channels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pos_x"] = MapCameraPathChannel.PosX,
            ["posx"] = MapCameraPathChannel.PosX,
            ["pos_y"] = MapCameraPathChannel.PosY,
            ["posy"] = MapCameraPathChannel.PosY,
            ["pos_z"] = MapCameraPathChannel.PosZ,
            ["posz"] = MapCameraPathChannel.PosZ,
            ["delta_x"] = MapCameraPathChannel.DeltaX,
            ["deltax"] = MapCameraPathChannel.DeltaX,
            ["delta_y"] = MapCameraPathChannel.DeltaY,
            ["deltay"] = MapCameraPathChannel.DeltaY,
            ["delta_z"] = MapCameraPathChannel.DeltaZ,
            ["deltaz"] = MapCameraPathChannel.DeltaZ,
            ["yaw"] = MapCameraPathChannel.Yaw,
            ["pitch"] = MapCameraPathChannel.Pitch,
            ["roll"] = MapCameraPathChannel.Roll,
            ["fov_a"] = MapCameraPathChannel.FovA,
            ["fova"] = MapCameraPathChannel.FovA,
            ["p28"] = MapCameraPathChannel.P28,
            ["fov_b"] = MapCameraPathChannel.FovB,
            ["fovb"] = MapCameraPathChannel.FovB,
        };

    public static string Format(MapCameraPath path)
    {
        var sb = new StringBuilder();
        sb.Append("camera_path ").Append(path.Id).AppendLine();
        foreach (var line in FormatLines(path.Ops, path.Empty))
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    public static string Format(IReadOnlyList<MapCameraPathOp> ops, int? id = null)
    {
        var sb = new StringBuilder();
        if (id is int n)
        {
            sb.Append("camera_path ").Append(n).AppendLine();
        }

        foreach (var line in FormatLines(ops, ops.Count == 0))
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    public static List<string> FormatLines(IReadOnlyList<MapCameraPathOp> ops, bool empty = false)
    {
        if (empty || ops.Count == 0)
        {
            return ["end"];
        }

        return ops.Select(FormatOp).ToList();
    }

    public static string FormatOp(MapCameraPathOp op)
    {
        var name = OpName(op);
        switch (op.Kind)
        {
            case MapCameraPathOpKind.SetPos:
            case MapCameraPathOpKind.SetDelta:
            case MapCameraPathOpKind.SetRot:
            case MapCameraPathOpKind.SetFov:
            case MapCameraPathOpKind.AddPos:
            case MapCameraPathOpKind.AddRot:
                return $"{name} {FmtVal(op, 0)} {FmtVal(op, 1)} {FmtVal(op, 2)}";
            case MapCameraPathOpKind.SetP28:
                return $"{name} {FmtVal(op, 0)}";
            case MapCameraPathOpKind.Channel:
                var ch = ChannelName(op.Channel, op.Arg);
                var body = $"{name} {ch} {FmtVal(op, 0)} {FmtVal(op, 1)}";
                var edx = op.TweenMode >= 0 ? op.TweenMode : 1;
                body += (edx & 3) is 0 or 2
                    ? $" v2={FmtVal(op, 2)}"
                    : $" dur={op.Duration}";
                if (op.Foot.Count > 0)
                {
                    body += $" foot={Convert.ToHexString(op.Foot.ToArray()).ToLowerInvariant()}";
                }

                return body;
            case MapCameraPathOpKind.TableS32:
                return $"{name} {op.Arg} {string.Join(' ', op.Values.Select(FormatValue))}";
            case MapCameraPathOpKind.SlotMeta:
                return
                    $"{name} {ChannelName(op.Channel, op.Arg)} scale={op.Scale} dur={op.Duration} mark=0x{op.Mark:X4}";
            case MapCameraPathOpKind.Wait:
            case MapCameraPathOpKind.WaitB:
                return $"{name} {op.Duration}";
            case MapCameraPathOpKind.Set63Fa5B:
            case MapCameraPathOpKind.Set63Faa2:
            case MapCameraPathOpKind.Set71A640:
            case MapCameraPathOpKind.Call70400:
                return $"{name} {op.Arg}";
            case MapCameraPathOpKind.YieldTween:
            case MapCameraPathOpKind.ResetCam:
            case MapCameraPathOpKind.End:
                return name;
            case MapCameraPathOpKind.SaveCam:
                return $"{name} {SaveBits(op.SaveFlags)}";
            case MapCameraPathOpKind.SetWords:
                return $"{name} {string.Join(' ', Enumerable.Range(0, 5).Select(i => i < op.Words.Count ? op.Words[i] : 0))}";
            case MapCameraPathOpKind.Skip3:
                var foot = op.Foot.Count >= 3 ? op.Foot.ToArray() : new byte[] { 0x1C, 0, 0 };
                return $"{name} {Convert.ToHexString(foot).ToLowerInvariant()}";
            default:
                return $"op{op.Opcode:x2}";
        }
    }

    public static byte[] Assemble(string text) => MdpSec15.EncodeChunk(Parse(text));

    public static List<MapCameraPathOp> Parse(string text)
    {
        var ops = new List<MapCameraPathOp>();
        var raw = (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .TrimStart('\uFEFF').Split('\n');
        for (var i = 0; i < raw.Length; i++)
        {
            var line = StripComment(raw[i]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line is "{")
            {
                continue;
            }

            if (line is "}")
            {
                continue;
            }

            var toks = Split(line);
            if (toks.Count == 0)
            {
                continue;
            }

            if (toks[0].Equals("camera_path", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (OffsetTok.IsMatch(toks[0]))
            {
                toks.RemoveAt(0);
                if (toks.Count == 0)
                {
                    continue;
                }
            }

            try
            {
                ops.Add(ParseOp(toks));
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
            {
                throw new ArgumentException($"camera_path line {i + 1}: {ex.Message}", ex);
            }
        }

        return ops;
    }

    private static MapCameraPathOp ParseOp(List<string> toks)
    {
        var name = toks[0];
        var rest = toks.Skip(1).ToList();
        var kv = Kv(rest);
        var pos = rest.Where(t => t.IndexOf('=') < 0).ToList();

        if (TryKind(name, out var kind, out var tween))
        {
            switch (kind)
            {
                case MapCameraPathOpKind.SetPos:
                case MapCameraPathOpKind.SetDelta:
                case MapCameraPathOpKind.SetRot:
                case MapCameraPathOpKind.SetFov:
                case MapCameraPathOpKind.AddPos:
                case MapCameraPathOpKind.AddRot:
                    return new MapCameraPathOp(0, (byte)kind, kind,
                        [Val(pos, 0), Val(pos, 1), Val(pos, 2)]);
                case MapCameraPathOpKind.SetP28:
                    return new MapCameraPathOp(0, 0x05, kind, [Val(pos, 0)]);
                case MapCameraPathOpKind.Channel:
                    return ParseChannel(name, tween, pos, kv);
                case MapCameraPathOpKind.TableS32:
                    var idx = kv.TryGetValue("idx", out var idxs) ? ParseInt(idxs) : IntTok(pos, 0);
                    var vals = (kv.ContainsKey("idx") ? pos : pos.Skip(1)).Select(ParseValue).ToList();
                    return new MapCameraPathOp(0, 0x0C, kind, vals, arg: idx);
                case MapCameraPathOpKind.SlotMeta:
                    return ParseSlotMeta(pos, kv);
                case MapCameraPathOpKind.Wait:
                case MapCameraPathOpKind.WaitB:
                    return new MapCameraPathOp(0, (byte)kind, kind, duration: IntTok(pos, 0));
                case MapCameraPathOpKind.Set63Fa5B:
                case MapCameraPathOpKind.Set63Faa2:
                case MapCameraPathOpKind.Set71A640:
                case MapCameraPathOpKind.Call70400:
                    return new MapCameraPathOp(0, (byte)kind, kind, arg: IntTok(pos, 0));
                case MapCameraPathOpKind.YieldTween:
                case MapCameraPathOpKind.ResetCam:
                case MapCameraPathOpKind.End:
                    return new MapCameraPathOp(0, (byte)kind, kind);
                case MapCameraPathOpKind.SaveCam:
                    return new MapCameraPathOp(0, 0x15, kind, saveFlags: ParseSave(pos, kv));
                case MapCameraPathOpKind.SetWords:
                    var words = pos.Select(ParseInt).ToList();
                    while (words.Count < 5)
                    {
                        words.Add(0);
                    }

                    return new MapCameraPathOp(0, 0x16, kind, words: words.Take(5).ToList());
                case MapCameraPathOpKind.Skip3:
                    var hex = pos.Count > 0 ? ParseHexBytes(string.Concat(pos)) : [0x1C, 0, 0];
                    if (hex.Length < 3)
                    {
                        hex = [0x1C, 0, 0];
                    }

                    return new MapCameraPathOp(0, hex[0], kind, foot: hex[..3]);
            }
        }

        throw new ArgumentException($"unknown camera-path op '{name}'");
    }

    private static MapCameraPathOp ParseChannel(string name, int tween, List<string> pos, Dictionary<string, string> kv)
    {
        var edx = tween >= 0 ? tween : 1;
        if (kv.TryGetValue("mode", out var ms))
        {
            edx = ParseInt(ms);
        }

        var channel = ParseChannelTok(pos.Count > 0 ? pos[0] : "0");
        var v0 = pos.Count > 1 ? ParseValue(pos[1]) : default;
        var v1 = pos.Count > 2 ? ParseValue(pos[2]) : default;
        var values = new List<MapCameraPathValue> { v0, v1 };
        var duration = kv.TryGetValue("dur", out var ds) ? ParseInt(ds) : 0;
        if ((edx & 3) is 0 or 2)
        {
            if (kv.TryGetValue("v2", out var v2s))
            {
                values.Add(ParseValue(v2s));
            }
            else if (pos.Count > 3)
            {
                values.Add(ParseValue(pos[3]));
            }
            else
            {
                values.Add(default);
            }
        }
        else if (duration == 0 && pos.Count > 3)
        {
            duration = ParseInt(pos[3]);
        }

        byte[]? foot = null;
        if (kv.TryGetValue("foot", out var fs))
        {
            foot = ParseHexBytes(fs);
        }

        var opcode = edx switch
        {
            0 => (byte)0x08,
            1 => (byte)0x09,
            2 => (byte)0x0A,
            3 => (byte)0x0B,
            4 => (byte)0x17,
            5 => (byte)0x18,
            6 => (byte)0x19,
            7 => (byte)0x1A,
            _ => (byte)0x09,
        };
        if (name.Length >= 3 && name.StartsWith("ch_mode", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name["ch_mode".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n is >= 0 and <= 7)
        {
            opcode = n switch
            {
                0 => 0x08,
                1 => 0x09,
                2 => 0x0A,
                3 => 0x0B,
                4 => 0x17,
                5 => 0x18,
                6 => 0x19,
                7 => 0x1A,
                _ => opcode,
            };
            edx = n;
        }

        return new MapCameraPathOp(0, opcode, MapCameraPathOpKind.Channel, values, channel, edx,
            duration, foot);
    }

    private static MapCameraPathOp ParseSlotMeta(List<string> pos, Dictionary<string, string> kv)
    {
        var chTok = kv.TryGetValue("ch", out var cs) ? cs : pos.ElementAtOrDefault(0) ?? "0";
        var channel = ParseChannelTok(chTok);
        var scale = kv.TryGetValue("scale", out var ss) ? ParseInt(ss.TrimEnd('%')) : 0;
        var dur = kv.TryGetValue("dur", out var ds) ? ParseInt(ds) : 0;
        var mark = kv.TryGetValue("mark", out var ms) ? ParseInt(ms) : 0;
        return new MapCameraPathOp(0, 0x0D, MapCameraPathOpKind.SlotMeta, channel: channel,
            duration: dur, arg: (int)channel, scale: scale, mark: mark);
    }

    private static bool TryKind(string name, out MapCameraPathOpKind kind, out int tween)
    {
        tween = -1;
        var key = name.Trim().ToLowerInvariant().Replace('-', '_');
        switch (key)
        {
            case "set_pos":
            case "setpos":
                kind = MapCameraPathOpKind.SetPos;
                return true;
            case "set_delta":
            case "setdelta":
                kind = MapCameraPathOpKind.SetDelta;
                return true;
            case "set_rot":
            case "setrot":
                kind = MapCameraPathOpKind.SetRot;
                return true;
            case "set_fov":
            case "setfov":
                kind = MapCameraPathOpKind.SetFov;
                return true;
            case "set_p28":
            case "setp28":
                kind = MapCameraPathOpKind.SetP28;
                return true;
            case "add_pos":
            case "addpos":
                kind = MapCameraPathOpKind.AddPos;
                return true;
            case "add_rot":
            case "addrot":
                kind = MapCameraPathOpKind.AddRot;
                return true;
            case "table_s32":
            case "tables32":
                kind = MapCameraPathOpKind.TableS32;
                return true;
            case "slot_meta":
            case "slotmeta":
                kind = MapCameraPathOpKind.SlotMeta;
                return true;
            case "wait":
                kind = MapCameraPathOpKind.Wait;
                return true;
            case "set_63fa5b":
            case "set63fa5b":
                kind = MapCameraPathOpKind.Set63Fa5B;
                return true;
            case "set_63faa2":
            case "set63faa2":
                kind = MapCameraPathOpKind.Set63Faa2;
                return true;
            case "set_71a640":
            case "set71a640":
                kind = MapCameraPathOpKind.Set71A640;
                return true;
            case "wait_b":
            case "waitb":
                kind = MapCameraPathOpKind.WaitB;
                return true;
            case "yield_tween":
            case "yieldtween":
                kind = MapCameraPathOpKind.YieldTween;
                return true;
            case "save_cam":
            case "savecam":
                kind = MapCameraPathOpKind.SaveCam;
                return true;
            case "set_words":
            case "setwords":
                kind = MapCameraPathOpKind.SetWords;
                return true;
            case "call_70400":
            case "call70400":
                kind = MapCameraPathOpKind.Call70400;
                return true;
            case "skip3":
                kind = MapCameraPathOpKind.Skip3;
                return true;
            case "reset_cam":
            case "resetcam":
                kind = MapCameraPathOpKind.ResetCam;
                return true;
            case "end":
                kind = MapCameraPathOpKind.End;
                return true;
            case "channel":
            case "tween":
                kind = MapCameraPathOpKind.Channel;
                tween = 1;
                return true;
        }

        if (key.StartsWith("ch_mode", StringComparison.Ordinal) &&
            int.TryParse(key["ch_mode".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) &&
            n is >= 0 and <= 7)
        {
            kind = MapCameraPathOpKind.Channel;
            tween = n;
            return true;
        }

        if (Enum.TryParse(name, ignoreCase: true, out kind) && kind != MapCameraPathOpKind.Unknown)
        {
            if (kind == MapCameraPathOpKind.Channel)
            {
                tween = 1;
            }

            return true;
        }

        kind = MapCameraPathOpKind.Unknown;
        return false;
    }

    private static string OpName(MapCameraPathOp op) =>
        op.Kind switch
        {
            MapCameraPathOpKind.SetPos => "set_pos",
            MapCameraPathOpKind.SetDelta => "set_delta",
            MapCameraPathOpKind.SetRot => "set_rot",
            MapCameraPathOpKind.SetFov => "set_fov",
            MapCameraPathOpKind.SetP28 => "set_p28",
            MapCameraPathOpKind.AddPos => "add_pos",
            MapCameraPathOpKind.AddRot => "add_rot",
            MapCameraPathOpKind.Channel => $"ch_mode{(op.TweenMode >= 0 ? op.TweenMode : 1)}",
            MapCameraPathOpKind.TableS32 => "table_s32",
            MapCameraPathOpKind.SlotMeta => "slot_meta",
            MapCameraPathOpKind.Wait => "wait",
            MapCameraPathOpKind.Set63Fa5B => "set_63fa5b",
            MapCameraPathOpKind.Set63Faa2 => "set_63faa2",
            MapCameraPathOpKind.Set71A640 => "set_71a640",
            MapCameraPathOpKind.WaitB => "wait_b",
            MapCameraPathOpKind.YieldTween => "yield_tween",
            MapCameraPathOpKind.SaveCam => "save_cam",
            MapCameraPathOpKind.SetWords => "set_words",
            MapCameraPathOpKind.Call70400 => "call_70400",
            MapCameraPathOpKind.Skip3 => "skip3",
            MapCameraPathOpKind.ResetCam => "reset_cam",
            MapCameraPathOpKind.End => "end",
            _ => $"op{op.Opcode:x2}",
        };

    private static string ChannelName(MapCameraPathChannel? channel, int arg)
    {
        var id = channel ?? (arg is >= 0 and <= 11 ? (MapCameraPathChannel)arg : MapCameraPathChannel.PosX);
        return id switch
        {
            MapCameraPathChannel.PosX => "pos_x",
            MapCameraPathChannel.PosY => "pos_y",
            MapCameraPathChannel.PosZ => "pos_z",
            MapCameraPathChannel.DeltaX => "delta_x",
            MapCameraPathChannel.DeltaY => "delta_y",
            MapCameraPathChannel.DeltaZ => "delta_z",
            MapCameraPathChannel.Yaw => "yaw",
            MapCameraPathChannel.Pitch => "pitch",
            MapCameraPathChannel.Roll => "roll",
            MapCameraPathChannel.FovA => "fov_a",
            MapCameraPathChannel.P28 => "p28",
            MapCameraPathChannel.FovB => "fov_b",
            _ => ((int)id).ToString(CultureInfo.InvariantCulture),
        };
    }

    private static MapCameraPathChannel ParseChannelTok(string raw)
    {
        if (Channels.TryGetValue(raw, out var ch))
        {
            return ch;
        }

        if (Enum.TryParse(raw, ignoreCase: true, out ch))
        {
            return ch;
        }

        var n = ParseInt(raw);
        if (n is < 0 or > 11)
        {
            throw new ArgumentException($"unknown channel '{raw}'");
        }

        return (MapCameraPathChannel)n;
    }

    private static string FmtVal(MapCameraPathOp op, int i) =>
        FormatValue(i < op.Values.Count ? op.Values[i] : default);

    internal static string FormatValue(MapCameraPathValue v)
    {
        if (v.IsInherit)
        {
            return "inherit";
        }

        if (v.IsUnset)
        {
            return "unset";
        }

        if (v.IsPlayer)
        {
            return "player";
        }

        if (v.IsSnapshot)
        {
            return "snapshot";
        }

        if ((v.Raw & 0xFFFF) == 0)
        {
            return (v.Signed >> 16).ToString(CultureInfo.InvariantCulture);
        }

        return v.Units.ToString("G9", CultureInfo.InvariantCulture);
    }

    private static MapCameraPathValue ParseValue(string raw)
    {
        raw = raw.Trim().Trim(',', '[', ']');
        if (raw.Equals("inherit", StringComparison.OrdinalIgnoreCase))
        {
            return new MapCameraPathValue(MapCameraPathValue.InheritRaw);
        }

        if (raw.Equals("unset", StringComparison.OrdinalIgnoreCase))
        {
            return new MapCameraPathValue(MapCameraPathValue.UnsetRaw);
        }

        if (raw.Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            return new MapCameraPathValue(MapCameraPathValue.PlayerRaw);
        }

        if (raw.Equals("snapshot", StringComparison.OrdinalIgnoreCase))
        {
            return new MapCameraPathValue(MapCameraPathValue.SnapshotRaw);
        }

        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return new MapCameraPathValue(uint.Parse(raw[2..], NumberStyles.HexNumber,
                CultureInfo.InvariantCulture));
        }

        if (raw.Contains('.') || raw.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            return MapCameraPathValue.FromUnits(
                (float)double.Parse(raw, CultureInfo.InvariantCulture));
        }

        return MapCameraPathValue.FromUnits(int.Parse(raw, CultureInfo.InvariantCulture));
    }

    private static MapCameraPathValue Val(List<string> pos, int i) =>
        i < pos.Count ? ParseValue(pos[i]) : default;

    private static int IntTok(List<string> pos, int i) =>
        i < pos.Count ? ParseInt(pos[i]) : 0;

    private static int ParseSave(List<string> pos, Dictionary<string, string> kv)
    {
        if (kv.TryGetValue("flags", out var fs))
        {
            return ParseInt(fs);
        }

        if (pos.Count == 1 && (pos[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                               int.TryParse(pos[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            return ParseInt(pos[0]);
        }

        var flags = 0;
        foreach (var tok in pos)
        {
            foreach (var bit in tok.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                flags |= bit.ToLowerInvariant() switch
                {
                    "pos" => 1,
                    "delta" => 2,
                    "rot" => 4,
                    "none" => 0,
                    _ => 0,
                };
            }
        }

        return flags;
    }

    private static string SaveBits(int flags)
    {
        var bits = new List<string>();
        if ((flags & 1) != 0)
        {
            bits.Add("pos");
        }

        if ((flags & 2) != 0)
        {
            bits.Add("delta");
        }

        if ((flags & 4) != 0)
        {
            bits.Add("rot");
        }

        return bits.Count == 0 ? "none" : string.Join(',', bits);
    }

    private static Dictionary<string, string> Kv(IEnumerable<string> toks)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tok in toks)
        {
            var eq = tok.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            d[tok[..eq]] = tok[(eq + 1)..];
        }

        return d;
    }

    private static List<string> Split(string line)
    {
        var s = line.Replace('(', ' ').Replace(')', ' ').Replace('[', ' ').Replace(']', ' ');
        return s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        return hash < 0 ? line : line[..hash];
    }

    private static int ParseInt(string raw)
    {
        raw = raw.Trim().TrimEnd('%').Trim(',', '[', ']');
        return raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(raw, CultureInfo.InvariantCulture);
    }

    private static byte[] ParseHexBytes(string text)
    {
        var h = Regex.Replace(text, @"[\s,]", "");
        if (h.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            h = h[2..];
        }

        if (h.Length % 2 != 0 || h.Length == 0)
        {
            throw new ArgumentException($"invalid hex: {text}");
        }

        return Convert.FromHexString(h);
    }
}
