using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Grandia.Sdk;

/// <summary>
/// In-process field-script assembler (same mnemonics as <c>field_tools script</c>).
/// Dump lifts vanilla gates to <c>if … { }</c> / <c>menu { }</c> like the Python tool.
/// Unmodified scripts are never assembled — encode only after a write.
/// </summary>
public static class FieldScriptAsm
{
    public const int ModeBegin = 0x1001;
    public const int ModeEnd = 0x1000;
    public const int FlagSet = 0x5001;
    public const int FlagClear = 0x5000;
    public const int YieldWord = 0xF000;
    public const int JumpHi = 0x3000;
    public const int RelJumpWord = 0x8000;
    public const int Say1Hi = 0x2000;
    public const int Say8Hi = 0x9000;
    public const int BranchClear = 0x4001;
    public const int BranchSet = 0x4040;
    public const int BranchSetAlt = 0x4041;
    public const int BranchCheck = 0x4000;
    public const int ChoiceWord = 0x4C40;
    public const int ChoiceArg = 0x4000;
    public const int Type6Word = 0x7000;
    public const int Type6Hi2 = 0x8000;
    public const int WaitAuxWord = 0x7001;

    private static readonly Regex HexBytes = new("^[0-9a-fA-F]*$", RegexOptions.CultureInvariant);
    private static readonly Regex LabelName = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex Kv = new(@"(\w+)=(\S+)", RegexOptions.CultureInvariant);
    private static readonly Regex IfCond = new(
        @"^(set|clear)\s+(0x[0-9A-Fa-f]+|\d+)(?:\s+word=(0x[0-9A-Fa-f]+|\d+))?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex IfPick = new(@"^pick\s*==\s*(0x[0-9A-Fa-f]+|\d+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex PickHead = new(
        @"^pick\s+(0x[0-9A-Fa-f]+|\d+)\s*->\s*([A-Za-z_][A-Za-z0-9_]*)" +
        @"(?:\s+skip=(0x[0-9A-Fa-f]+|\d+))?(?:\s+word=(0x[0-9A-Fa-f]+|\d+))?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex IfArrow = new(
        @"^(.*?)\s+->\s+([A-Za-z_][A-Za-z0-9_]*)" +
        @"(?:\s+skip=(0x[0-9A-Fa-f]+|\d+))?(?:\s+word=(0x[0-9A-Fa-f]+|\d+))?\s*(\{)?$",
        RegexOptions.CultureInvariant);

    public static string Disassemble(ReadOnlySpan<byte> bytecode, int scriptId, string? mapStem = null)
    {
        var ops = FieldScriptIr.Parse(bytecode, 0, bytecode.Length);
        var labels = new HashSet<string>(ops.OfType<FsLabel>().Select(l => l.Name), StringComparer.Ordinal);
        var referenced = ReferencedLabels(ops);
        var sb = new StringBuilder();
        sb.Append("script 0x").Append(scriptId.ToString("X4", CultureInfo.InvariantCulture)).AppendLine();
        foreach (var line in FormatOps(ops, referenced, labels, mapStem))
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    public static byte[] Assemble(string text, int? scriptId = null, string? mapStem = null)
    {
        var ops = Parse(text, mapStem, out var parsedId);
        if (scriptId is int id && id != 0)
        {
            parsedId = id;
        }

        _ = parsedId;
        return FieldScriptIr.Emit(ops);
    }

    internal static List<FsItem> Parse(string text, string? mapStem, out int scriptId)
    {
        var raw = (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .TrimStart('\uFEFF').Split('\n');
        var i = 0;
        SkipNoise(raw, ref i);
        if (i >= raw.Length)
        {
            throw new ArgumentException("expected 'script 0xNNNN'");
        }

        var hdr = raw[i].Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (hdr.Length < 2 || !hdr[0].Equals("script", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("expected 'script 0xNNNN' as the first statement");
        }

        scriptId = ParseInt(hdr[1]);
        i += 1;
        var ops = new List<FsItem>();
        while (i < raw.Length)
        {
            SkipNoise(raw, ref i);
            if (i >= raw.Length)
            {
                break;
            }

            if (LineIndent(raw[i]) > 0)
            {
                throw new ArgumentException($"unexpected indented line: {raw[i]}");
            }

            ops.AddRange(ParseStatement(raw, ref i, mapStem));
        }

        return ops;
    }

    private static IEnumerable<string> FormatItem(FsItem item, HashSet<string> labels, string? mapStem)
    {
        switch (item)
        {
            case FsLabel lab:
                yield return $"label {lab.Name}";
                break;
            case FsWord w when (w.Word >> 12) - 1 == 14:
                yield return "yield" + WordClause(w.Word, YieldWord);
                break;
            case FsWord w when (w.Word >> 12) - 1 == 0:
                yield return (w.Word & 0x0FFF) != 0
                    ? "flag_ctx_begin" + WordClause(w.Word, ModeBegin)
                    : "flag_ctx_end" + WordClause(w.Word, ModeEnd);
                break;
            case FsFlag f:
                yield return ((f.Word & 0xF) != 0 ? "set_flag" : "clear_flag") +
                             $" 0x{f.FlagId:X4}" + WordClause(f.Word, (f.Word & 0xF) != 0 ? FlagSet : FlagClear);
                break;
            case FsJump j:
                yield return FormatJump(j, labels);
                break;
            case FsRelJump r:
                var rel = labels.Contains(r.Target) ? "" : $" rel={r.OriginalRel}";
                yield return $"reljump {r.Target}{rel}{WordClause(r.Word, RelJumpWord)}";
                break;
            case FsBranch b:
                yield return FormatBranch(b);
                break;
            case FsType6 t:
                yield return FormatType6(t);
                break;
            case FsSay s:
                foreach (var line in FormatSay(s, mapStem))
                {
                    yield return line;
                }

                break;
            case FsRaw raw:
                yield return $"raw hex={Convert.ToHexString(raw.Raw).ToLowerInvariant()}";
                break;
            default:
                yield return $"raw hex=";
                break;
        }
    }

    private static List<string> FormatOps(
        IReadOnlyList<FsItem> ops, HashSet<string> referenced, HashSet<string> labels, string? mapStem)
    {
        var lines = new List<string>();
        var i = 0;
        while (i < ops.Count)
        {
            if (TryMatchMenu(ops, i, referenced, out var menuAfter, out var say, out var rungs))
            {
                lines.Add("menu {");
                lines.AddRange(IndentRows(FormatSay(say, mapStem), 2));
                foreach (var (rungPick, jump, body) in rungs)
                {
                    lines.Add("  " + FormatPickHeader(rungPick, jump, labels));
                    lines.AddRange(IndentRows(FormatOps(body, referenced, labels, mapStem), 4));
                }

                lines.Add("}");
                i = menuAfter;
                continue;
            }

            if (TryMatchIfBody(ops, i, referenced, out var ifAfter, out var skips, out var ifJump, out var ifBody))
            {
                lines.Add(FormatIfHeader(skips, ifJump, labels));
                lines.AddRange(IndentRows(FormatOps(ifBody, referenced, labels, mapStem), 2));
                lines.Add("}");
                i = ifAfter;
                continue;
            }

            if (TryMatchIfGate(ops, i, referenced, out var gateAfter, out var gateSkips, out var gateJump))
            {
                lines.Add("if " + string.Join(" and ", gateSkips.Select(FormatIfCond)));
                lines.Add("  " + FormatJump(gateJump, labels));
                i = gateAfter;
                continue;
            }

            if (TryMatchPickGate(ops, i, referenced, out var pickAfter, out var pick, out var pickJump))
            {
                lines.Add($"if pick == {pick}");
                lines.Add("  " + FormatJump(pickJump, labels));
                i = pickAfter;
                continue;
            }

            lines.AddRange(FormatItem(ops[i], labels, mapStem));
            i += 1;
        }

        return lines;
    }

    private static bool TryMatchIfGate(
        IReadOnlyList<FsItem> ops, int i, HashSet<string> referenced,
        out int after, out List<FsBranch> skips, out FsJump jump)
    {
        after = i;
        skips = [];
        jump = null!;
        if (i >= ops.Count || !IsModeBegin(ops[i]))
        {
            return false;
        }

        var j = i + 1;
        while (j < ops.Count)
        {
            while (j < ops.Count && ops[j] is FsLabel lab)
            {
                if (referenced.Contains(lab.Name))
                {
                    return false;
                }

                j += 1;
            }

            if (j < ops.Count && ops[j] is FsBranch br && GateSkipKind(br) is not null)
            {
                skips.Add(br);
                j += 1;
                continue;
            }

            break;
        }

        if (skips.Count == 0)
        {
            return false;
        }

        while (j < ops.Count && ops[j] is FsLabel mid)
        {
            if (referenced.Contains(mid.Name))
            {
                return false;
            }

            j += 1;
        }

        if (j >= ops.Count || !IsModeEnd(ops[j]))
        {
            return false;
        }

        j += 1;
        if (j >= ops.Count || ops[j] is not FsJump jmp)
        {
            return false;
        }

        after = j + 1;
        jump = jmp;
        return true;
    }

    private static bool TryMatchIfBody(
        IReadOnlyList<FsItem> ops, int i, HashSet<string> referenced,
        out int after, out List<FsBranch> skips, out FsJump jump, out List<FsItem> body)
    {
        after = i;
        skips = [];
        jump = null!;
        body = [];
        if (!TryMatchIfGate(ops, i, referenced, out var gateAfter, out skips, out jump))
        {
            return false;
        }

        var tgt = LabelIndex(ops, jump.Target);
        if (tgt < 0 || tgt < gateAfter)
        {
            return false;
        }

        if (ops[tgt] is not FsLabel lab || lab.Name != jump.Target)
        {
            return false;
        }

        after = tgt + 1;
        body = ops.Skip(gateAfter).Take(tgt - gateAfter).ToList();
        return true;
    }

    private static bool TryMatchPickGate(
        IReadOnlyList<FsItem> ops, int i, HashSet<string> referenced,
        out int after, out int pick, out FsJump jump)
    {
        after = i;
        pick = 0;
        jump = null!;
        if (i >= ops.Count || !IsModeBegin(ops[i]))
        {
            return false;
        }

        var j = i + 1;
        while (j < ops.Count && ops[j] is FsLabel skipLab)
        {
            if (referenced.Contains(skipLab.Name))
            {
                return false;
            }

            j += 1;
        }

        if (j >= ops.Count || ops[j] is not FsBranch br || !IsChoiceRung(br))
        {
            return false;
        }

        pick = br.Extra[0] | (br.Extra[1] << 8);
        j += 1;
        while (j < ops.Count && ops[j] is FsLabel mid)
        {
            if (referenced.Contains(mid.Name))
            {
                return false;
            }

            j += 1;
        }

        if (j >= ops.Count || !IsModeEnd(ops[j]))
        {
            return false;
        }

        j += 1;
        if (j >= ops.Count || ops[j] is not FsJump jmp)
        {
            return false;
        }

        after = j + 1;
        jump = jmp;
        return true;
    }

    private static bool TryMatchMenu(
        IReadOnlyList<FsItem> ops, int i, HashSet<string> referenced,
        out int after, out FsSay say, out List<(int Pick, FsJump Jump, List<FsItem> Body)> rungs)
    {
        after = i;
        say = null!;
        rungs = [];
        if (i >= ops.Count || ops[i] is not FsSay dialog || !IsDialogMenu(dialog))
        {
            return false;
        }

        say = dialog;
        var j = i + 1;
        while (j < ops.Count && ops[j] is FsLabel lead)
        {
            if (referenced.Contains(lead.Name))
            {
                return false;
            }

            j += 1;
        }

        while (true)
        {
            if (!TryMatchPickGate(ops, j, referenced, out var gateAfter, out var pick, out var jump))
            {
                break;
            }

            var tgt = LabelIndex(ops, jump.Target);
            if (tgt < 0 || tgt < gateAfter)
            {
                return false;
            }

            if (ops[tgt] is not FsLabel lab || lab.Name != jump.Target)
            {
                return false;
            }

            rungs.Add((pick, jump, ops.Skip(gateAfter).Take(tgt - gateAfter).ToList()));
            j = tgt + 1;
            while (j < ops.Count && ops[j] is FsLabel extra && !referenced.Contains(extra.Name))
            {
                j += 1;
            }
        }

        if (rungs.Count == 0)
        {
            return false;
        }

        after = j;
        return true;
    }

    private static HashSet<string> ReferencedLabels(IEnumerable<FsItem> ops)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in ops)
        {
            switch (item)
            {
                case FsJump j:
                    names.Add(j.Target);
                    break;
                case FsRelJump r:
                    names.Add(r.Target);
                    break;
            }
        }

        return names;
    }

    private static int LabelIndex(IReadOnlyList<FsItem> ops, string name)
    {
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i] is FsLabel lab && lab.Name == name)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsModeBegin(FsItem item) => item is FsWord w && w.Word == ModeBegin;

    private static bool IsModeEnd(FsItem item) => item is FsWord w && w.Word == ModeEnd;

    private static bool IsChoiceRung(FsBranch op) =>
        op.Word == ChoiceWord && op.Arg == ChoiceArg && op.Extra.Length == 2;

    private static bool IsDialogMenu(FsSay op) =>
        DialogueMarkup.HasMenuControl(DialogueTokens.Tokenize(op.Payload));

    private static string? GateSkipKind(FsBranch op)
    {
        if (op.Extra.Length != 0)
        {
            return null;
        }

        if (op.Word == BranchClear)
        {
            return "clear";
        }

        if (op.Word is BranchSet or BranchSetAlt)
        {
            return "set";
        }

        return null;
    }

    private static string FormatIfCond(FsBranch op)
    {
        var kind = GateSkipKind(op) ?? "set";
        var def = kind == "clear" ? BranchClear : BranchSet;
        return $"{kind} 0x{op.Arg:X4}{WordClause(op.Word, def)}";
    }

    private static string FormatIfHeader(IReadOnlyList<FsBranch> skips, FsJump jump, HashSet<string> labels) =>
        $"if {string.Join(" and ", skips.Select(FormatIfCond))} -> {jump.Target}{SkipClause(jump, labels)}{WordClause(jump.Word & 0xF000, JumpHi)} {{";

    private static string FormatPickHeader(int pick, FsJump jump, HashSet<string> labels) =>
        $"pick {pick} -> {jump.Target}{SkipClause(jump, labels)}{WordClause(jump.Word & 0xF000, JumpHi)}";

    private static string FormatJump(FsJump j, HashSet<string> labels) =>
        $"jump {j.Target}{SkipClause(j, labels)}{WordClause(j.Word & 0xF000, JumpHi)}";

    private static string SkipClause(FsJump j, HashSet<string> labels) =>
        labels.Contains(j.Target) ? "" : $" skip=0x{j.OriginalSkip:X}";

    private static List<string> IndentRows(IEnumerable<string> rows, int n)
    {
        var pad = new string(' ', n);
        return rows.Select(row => row.Length == 0 ? row : pad + row).ToList();
    }

    private static string FormatBranch(FsBranch op)
    {
        var extra = op.Extra.Length > 0 ? $" extra={Convert.ToHexString(op.Extra).ToLowerInvariant()}" : "";
        if (op.Extra.Length == 0)
        {
            if (op.Word == BranchClear)
            {
                return $"skip_if_clear 0x{op.Arg:X4}";
            }

            if (op.Word == BranchSet)
            {
                return $"skip_if_set 0x{op.Arg:X4}";
            }

            if (op.Word == BranchSetAlt)
            {
                return $"skip_if_set 0x{op.Arg:X4} word=0x{op.Word:X4}";
            }

            if (op.Word == BranchCheck)
            {
                return $"check_flag 0x{op.Arg:X4}";
            }
        }

        return $"branch word=0x{op.Word:X4} arg=0x{op.Arg:X4}{extra}";
    }

    private static string FormatType6(FsType6 op)
    {
        var (pretty, word, raw) = Type6Named(op);
        if (word is int w && raw is not null && op.Word == w && op.Raw.AsSpan().SequenceEqual(raw))
        {
            return pretty;
        }

        return $"{pretty} word=0x{op.Word:X4} raw={Convert.ToHexString(op.Raw).ToLowerInvariant()}";
    }

    private static (string pretty, int? word, byte[]? raw) Type6Named(FsType6 op)
    {
        var raw = op.Raw;
        var arg = raw.Length >= 4 ? FieldScriptIr.U16(raw, 2) : 0;
        if (op.Sub == 0x001A)
        {
            var hid = arg & 0x7FFF;
            var pretty = (arg & 0x8000) != 0 ? $"call_hook {hid} alt" : $"call_hook {hid}";
            return (pretty, Type6Word, PackType6Arg(0x001A, arg));
        }

        if (op.Sub == 0x0021)
        {
            return ($"arm_wait {arg}", Type6Word, PackType6Arg(0x0021, arg));
        }

        if (op.Sub == 0x0020)
        {
            return ($"wait_aux 0x{arg:X4}", WaitAuxWord, PackWaitAux(arg));
        }

        if (op.Sub == 0x0011)
        {
            return ($"wait {arg}", Type6Word, PackType6Arg(0x0011, arg));
        }

        if (op.Sub == 0x0016)
        {
            var scene = arg & 0x3FFF;
            var hi = arg >> 14;
            var name = hi switch { 0 => "activate", 1 => "overlay", 2 => "deactivate", 3 => "deactivate_overlay", _ => $"hi{hi}" };
            var packed = CameraArg(name, scene);
            return packed is int p
                ? ($"camera {name} 0x{scene:X2}", Type6Word, PackType6Arg(0x0016, p))
                : ($"camera {name} 0x{scene:X2}", null, null);
        }

        if (op.Sub is 0x0004 or 0x0005)
        {
            var kind = op.Sub == 0x0005 ? "give_item_alt" : "give_item";
            return ($"{kind} 0x{arg:X4}", Type6Word, PackType6Arg(op.Sub, arg));
        }

        if (op.Sub == 0x0015)
        {
            return arg == 0x000C
                ? ("sfx recover", Type6Word, PackType6Arg(0x0015, 0x000C))
                : ($"sfx 0x{arg:X4}", Type6Word, PackType6Arg(0x0015, arg));
        }

        if (op.Sub == 0x0010)
        {
            return ("save_menu", Type6Word, PackType6(0x0010));
        }

        if (op.Sub == 0x000E)
        {
            return ("restore", Type6Word, PackType6(0x000E));
        }

        if (op.Sub == 0x001B)
        {
            return ("stash_menu", Type6Word, PackType6(0x001B));
        }

        if (op.Sub == 0x001C)
        {
            return ("retrieve_menu", Type6Word, PackType6(0x001C));
        }

        return ($"type6 0x{op.Sub:X4}", null, null);
    }

    private static List<string> FormatSay(FsSay op, string? mapStem)
    {
        var tokens = DialogueTokens.Tokenize(op.Payload);
        var markup = DialogueMarkup.FromTokens(tokens, op.Type8, mapStem);
        var kind = op.Type8 ? "type8" : "type1";
        var hi = op.Type8 ? Say8Hi : Say1Hi;
        var lines = new List<string> { $"say {kind}{WordClause(op.WordHi, hi)}" };
        var rebuilt = false;
        try
        {
            var next = DialogueMarkup.Apply([], markup, op.Type8, mapStem);
            rebuilt = DialogueTokens.Emit(next).AsSpan().SequenceEqual(Even(op.Payload));
        }
        catch (Exception)
        {
            rebuilt = false;
        }

        if (rebuilt)
        {
            foreach (var row in markup.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                lines.Add("  " + row);
            }

            return lines;
        }

        if (!string.IsNullOrEmpty(markup))
        {
            foreach (var row in markup.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                lines.Add("  # " + row);
            }
        }

        lines.Add("  hex=" + Convert.ToHexString(Even(op.Payload)).ToLowerInvariant());
        return lines;
    }

    private static List<FsItem> ParseStatement(string[] raw, ref int i, string? mapStem)
    {
        var line = raw[i];
        var indent = LineIndent(line);
        var stripped = line.Trim();
        var tokens = stripped.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var head = tokens[0];
        var rest = tokens.Skip(1).ToArray();
        var kv = ParseKv(rest);
        if (head.Equals("label", StringComparison.OrdinalIgnoreCase))
        {
            var name = rest.Length > 0 ? rest[0] : "";
            if (!LabelName.IsMatch(name))
            {
                throw new ArgumentException($"bad label {name}");
            }

            i += 1;
            return [new FsLabel(name)];
        }

        if (head.Equals("if", StringComparison.OrdinalIgnoreCase))
        {
            return ParseIf(raw, ref i, indent, string.Join(' ', rest), mapStem);
        }

        if (head.Equals("say", StringComparison.OrdinalIgnoreCase))
        {
            var kind = rest.Length > 0 ? rest[0] : "type1";
            if (kind is not ("type1" or "type8"))
            {
                throw new ArgumentException($"say needs type1 or type8, got {kind}");
            }

            i += 1;
            var body = CollectIndented(raw, ref i, indent);
            return [ParseSay(kind, kv, body, mapStem)];
        }

        if (head.Equals("menu", StringComparison.OrdinalIgnoreCase))
        {
            if (!stripped.EndsWith('{'))
            {
                throw new ArgumentException("menu needs {");
            }

            i += 1;
            var inner = new List<FsItem>();
            while (true)
            {
                SkipNoise(raw, ref i);
                if (i >= raw.Length)
                {
                    throw new ArgumentException("unclosed menu {");
                }

                if (raw[i].Trim() == "}")
                {
                    i += 1;
                    return inner;
                }

                inner.AddRange(ParseStatement(raw, ref i, mapStem));
            }
        }

        if (head.Equals("pick", StringComparison.OrdinalIgnoreCase))
        {
            var m = PickHead.Match(stripped);
            if (!m.Success)
            {
                throw new ArgumentException($"bad pick header {stripped}");
            }

            var pick = ParseInt(m.Groups[1].Value);
            var skip = m.Groups[3].Success ? ParseInt(m.Groups[3].Value) : 0;
            var hi = m.Groups[4].Success ? ParseInt(m.Groups[4].Value) : JumpHi;
            var jump = new FsJump(hi, m.Groups[2].Value, skip);
            i += 1;
            var body = new List<FsItem>();
            while (true)
            {
                SkipNoise(raw, ref i);
                if (i >= raw.Length || raw[i].Trim() == "}" || LineIndent(raw[i]) <= indent)
                {
                    break;
                }

                body.AddRange(ParseStatement(raw, ref i, mapStem));
            }

            return [.. ExpandPick(pick, jump), .. body, new FsLabel(jump.Target)];
        }

        i += 1;
        return [ParseOp(head, rest, kv)];
    }

    private static List<FsItem> ParseIf(string[] raw, ref int i, int indent, string condSrc, string? mapStem)
    {
        var braced = condSrc.TrimEnd().EndsWith('{');
        var arrow = MatchIfArrow(condSrc);
        i += 1;
        if (arrow is { } a)
        {
            var body = new List<FsItem>();
            while (true)
            {
                SkipNoise(raw, ref i);
                if (i >= raw.Length)
                {
                    if (braced)
                    {
                        throw new ArgumentException("unclosed if {");
                    }

                    break;
                }

                if (raw[i].Trim() == "}")
                {
                    i += 1;
                    break;
                }

                if (!braced && LineIndent(raw[i]) <= indent)
                {
                    break;
                }

                body.AddRange(ParseStatement(raw, ref i, mapStem));
            }

            return [.. ExpandIf(ParseIfConds(a.cond), a.jump), .. body, new FsLabel(a.jump.Target)];
        }

        var rows = new List<string>();
        while (i < raw.Length)
        {
            var nxt = raw[i];
            var nxts = nxt.Trim();
            var nxti = LineIndent(nxt);
            if (string.IsNullOrEmpty(nxts))
            {
                if (rows.Count > 0)
                {
                    break;
                }

                i += 1;
                continue;
            }

            if (nxts.StartsWith('#') && nxti <= indent)
            {
                break;
            }

            if (nxti > indent)
            {
                rows.Add(nxts);
                i += 1;
                continue;
            }

            break;
        }

        var jumpLine = rows.FirstOrDefault(ln => ln.Length > 0 && !ln.StartsWith('#'))
                       ?? throw new ArgumentException("if needs an indented jump");
        var jtok = jumpLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (!jtok[0].Equals("jump", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"if body must be a jump, got {jtok[0]}");
        }

        var jump = (FsJump)ParseOp("jump", jtok.Skip(1).ToArray(), ParseKv(jtok.Skip(1)));
        var pick = IfPick.Match(condSrc.Trim());
        return pick.Success ? ExpandPick(ParseInt(pick.Groups[1].Value), jump) : ExpandIf(ParseIfConds(condSrc), jump);
    }

    private static (string cond, FsJump jump)? MatchIfArrow(string condSrc)
    {
        var m = IfArrow.Match(condSrc.Trim());
        if (!m.Success || IfPick.IsMatch(m.Groups[1].Value.Trim()))
        {
            return null;
        }

        var skip = m.Groups[3].Success ? ParseInt(m.Groups[3].Value) : 0;
        var hi = m.Groups[4].Success ? ParseInt(m.Groups[4].Value) : JumpHi;
        return (m.Groups[1].Value.Trim(), new FsJump(hi, m.Groups[2].Value, skip));
    }

    private static List<FsBranch> ParseIfConds(string text)
    {
        var skips = new List<FsBranch>();
        foreach (var chunk in Regex.Split(text.Trim(), @"\s+and\s+"))
        {
            var m = IfCond.Match(chunk.Trim());
            if (!m.Success)
            {
                throw new ArgumentException($"bad if condition {chunk}");
            }

            var word = m.Groups[1].Value == "clear"
                ? (m.Groups[3].Success ? ParseInt(m.Groups[3].Value) : BranchClear)
                : (m.Groups[3].Success ? ParseInt(m.Groups[3].Value) : BranchSet);
            skips.Add(new FsBranch(word, ParseInt(m.Groups[2].Value), []));
        }

        if (skips.Count == 0)
        {
            throw new ArgumentException("if needs at least one condition");
        }

        return skips;
    }

    private static List<FsItem> ExpandIf(IReadOnlyList<FsBranch> skips, FsJump jump)
    {
        var ops = new List<FsItem> { new FsWord(ModeBegin) };
        ops.AddRange(skips);
        ops.Add(new FsWord(ModeEnd));
        ops.Add(jump);
        return ops;
    }

    private static List<FsItem> ExpandPick(int pick, FsJump jump)
    {
        var extra = new byte[] { (byte)pick, (byte)(pick >> 8) };
        return ExpandIf([new FsBranch(ChoiceWord, ChoiceArg, extra)], jump);
    }

    private static FsSay ParseSay(string kind, Dictionary<string, string> kv, List<string> body, string? mapStem)
    {
        var type8 = kind == "type8";
        var hi = kv.TryGetValue("word", out var ws) ? ParseInt(ws) : (type8 ? Say8Hi : Say1Hi);
        var hex = string.Concat(body.Where(ln => ln.TrimStart().StartsWith("hex=", StringComparison.OrdinalIgnoreCase))
            .Select(ln => ln.Trim()[4..]));
        if (hex.Length > 0)
        {
            return new FsSay(hi, type8, ParseHexBytes(hex));
        }

        var markup = string.Join('\n', body.Where(ln =>
            !ln.TrimStart().StartsWith("hex=", StringComparison.OrdinalIgnoreCase) &&
            !ln.TrimStart().StartsWith('#')));
        var tokens = DialogueMarkup.Apply([], markup, type8, mapStem);
        return new FsSay(hi, type8, DialogueTokens.Emit(tokens));
    }

    private static FsItem ParseOp(string head, string[] rest, Dictionary<string, string> kv)
    {
        head = head.ToLowerInvariant();
        var pos = rest.Where(p => !p.Contains('=')).ToArray();
        switch (head)
        {
            case "flag_ctx_begin":
                return new FsWord(kv.TryGetValue("word", out var w0) ? ParseInt(w0) : ModeBegin);
            case "flag_ctx_end":
                return new FsWord(kv.TryGetValue("word", out var w1) ? ParseInt(w1) : ModeEnd);
            case "set_flag":
                return new FsFlag(kv.TryGetValue("word", out var w2) ? ParseInt(w2) : FlagSet,
                    pos.Length > 0 ? ParseInt(pos[0]) : 0);
            case "clear_flag":
                return new FsFlag(kv.TryGetValue("word", out var w3) ? ParseInt(w3) : FlagClear,
                    pos.Length > 0 ? ParseInt(pos[0]) : 0);
            case "yield":
                return new FsWord(kv.TryGetValue("word", out var w4) ? ParseInt(w4) : YieldWord);
            case "jump":
                if (pos.Length == 0 || !LabelName.IsMatch(pos[0]))
                {
                    throw new ArgumentException("jump needs a label");
                }

                return new FsJump(kv.TryGetValue("word", out var w5) ? ParseInt(w5) : JumpHi, pos[0],
                    kv.TryGetValue("skip", out var sk) ? ParseInt(sk) : 0);
            case "reljump":
                if (pos.Length == 0 || !LabelName.IsMatch(pos[0]))
                {
                    throw new ArgumentException("reljump needs a label");
                }

                return new FsRelJump(kv.TryGetValue("word", out var w6) ? ParseInt(w6) : RelJumpWord, pos[0],
                    kv.TryGetValue("rel", out var rl) ? ParseInt(rl) : 0);
            case "skip_if_clear":
                return new FsBranch(kv.TryGetValue("word", out var w7) ? ParseInt(w7) : BranchClear,
                    pos.Length > 0 ? ParseInt(pos[0]) : 0,
                    kv.TryGetValue("extra", out var e7) ? ParseHexBytes(e7) : []);
            case "skip_if_set":
                return new FsBranch(kv.TryGetValue("word", out var w8) ? ParseInt(w8) : BranchSet,
                    pos.Length > 0 ? ParseInt(pos[0]) : 0,
                    kv.TryGetValue("extra", out var e8) ? ParseHexBytes(e8) : []);
            case "check_flag":
                return new FsBranch(kv.TryGetValue("word", out var w9) ? ParseInt(w9) : BranchCheck,
                    pos.Length > 0 ? ParseInt(pos[0]) : 0,
                    kv.TryGetValue("extra", out var e9) ? ParseHexBytes(e9) : []);
            case "branch":
                if (!kv.TryGetValue("word", out var bw) || !kv.TryGetValue("arg", out var ba))
                {
                    throw new ArgumentException("branch needs word= and arg=");
                }

                return new FsBranch(ParseInt(bw), ParseInt(ba),
                    kv.TryGetValue("extra", out var be) ? ParseHexBytes(be) : []);
            case "raw":
                if (!kv.TryGetValue("hex", out var hx))
                {
                    throw new ArgumentException("raw needs hex=");
                }

                return new FsRaw(ParseHexBytes(hx));
            case "call_hook":
            case "wait":
            case "arm_wait":
            case "wait_aux":
            case "camera":
            case "give_item":
            case "give_item_alt":
            case "walk":
            case "sfx":
            case "save_menu":
            case "restore":
            case "stash_menu":
            case "retrieve_menu":
            case "type6":
                return ParseType6(head, pos, kv);
            default:
                throw new ArgumentException($"unknown op {head}");
        }
    }

    private static FsType6 ParseType6(string kind, string[] rest, Dictionary<string, string> kv)
    {
        if (kv.ContainsKey("raw") ^ kv.ContainsKey("word"))
        {
            throw new ArgumentException($"{kind} needs both word= and raw=, or neither");
        }

        if (kv.TryGetValue("raw", out var rawHex))
        {
            var raw = ParseHexBytes(rawHex);
            if (raw.Length < 2)
            {
                throw new ArgumentException($"{kind} raw too short");
            }

            var word = ParseInt(kv["word"]);
            var sub = FieldScriptIr.U16(raw, 0) & 0x3FFF;
            return new FsType6(word, sub, raw);
        }

        return kind switch
        {
            "call_hook" => Hook(rest),
            "wait" => new FsType6(Type6Word, 0x0011, PackType6Arg(0x0011, rest.Length > 0 ? ParseInt(rest[0]) : 0)),
            "arm_wait" => new FsType6(Type6Word, 0x0021, PackType6Arg(0x0021, rest.Length > 0 ? ParseInt(rest[0]) : 0)),
            "wait_aux" => new FsType6(WaitAuxWord, 0x0020, PackWaitAux(rest.Length > 0 ? ParseInt(rest[0]) : 0)),
            "camera" => Camera(rest),
            "give_item" => Item(0x0004, rest),
            "give_item_alt" => Item(0x0005, rest),
            "walk" => new FsType6(Type6Word, 0x0015, PackType6Arg(0x0015, rest.Length > 0 ? ParseInt(rest[0]) : 0)),
            "sfx" => Sfx(rest),
            "save_menu" => new FsType6(Type6Word, 0x0010, PackType6(0x0010)),
            "restore" => new FsType6(Type6Word, 0x000E, PackType6(0x000E)),
            "stash_menu" => new FsType6(Type6Word, 0x001B, PackType6(0x001B)),
            "retrieve_menu" => new FsType6(Type6Word, 0x001C, PackType6(0x001C)),
            _ => throw new ArgumentException($"{kind} needs word= and raw="),
        };

        static FsType6 Hook(string[] rest)
        {
            var hid = rest.Length > 0 ? ParseInt(rest[0]) : 0;
            var alt = rest.Skip(1).Any(p => p.Equals("alt", StringComparison.OrdinalIgnoreCase));
            var arg = (hid & 0x7FFF) | (alt ? 0x8000 : 0);
            return new FsType6(Type6Word, 0x001A, PackType6Arg(0x001A, arg));
        }

        static FsType6 Camera(string[] rest)
        {
            var op = rest.Length > 0 ? rest[0] : "activate";
            var scene = rest.Length > 1 ? ParseInt(rest[1]) : 0;
            var packed = CameraArg(op, scene) ??
                         throw new ArgumentException($"camera needs activate/overlay/deactivate/deactivate_overlay, got {op}");
            return new FsType6(Type6Word, 0x0016, PackType6Arg(0x0016, packed));
        }

        static FsType6 Item(int sub, string[] rest) =>
            new(Type6Word, sub, PackType6Arg(sub, rest.Length > 0 ? ParseInt(rest[0]) : 0));

        static FsType6 Sfx(string[] rest)
        {
            var extra = 0x000C;
            if (rest.Length > 0 && !rest[0].Equals("recover", StringComparison.OrdinalIgnoreCase))
            {
                extra = ParseInt(rest[0]);
            }

            return new FsType6(Type6Word, 0x0015, PackType6Arg(0x0015, extra));
        }
    }

    private static byte[] PackType6(int sub) =>
        [(byte)(sub & 0xFF), (byte)((sub >> 8) & 0x3F)];

    private static byte[] PackType6Arg(int sub, int arg)
    {
        var w = (sub & 0x3FFF) | Type6Hi2;
        return [(byte)w, (byte)(w >> 8), (byte)arg, (byte)(arg >> 8)];
    }

    private static byte[] PackWaitAux(int ticks)
    {
        var w = 0x0020 | Type6Hi2;
        return [(byte)w, (byte)(w >> 8), (byte)ticks, (byte)(ticks >> 8), 0, 0];
    }

    private static int? CameraArg(string op, int scene) =>
        op switch
        {
            "activate" => scene & 0x3FFF,
            "overlay" => (scene & 0x3FFF) | (1 << 14),
            "deactivate" => (scene & 0x3FFF) | (2 << 14),
            "deactivate_overlay" => (scene & 0x3FFF) | (3 << 14),
            _ => null,
        };

    private static string WordClause(int word, int def) => word == def ? "" : $" word=0x{word:X4}";

    private static byte[] Even(byte[] data) => data.Length % 2 == 0 ? data : [.. data, 0];

    private static Dictionary<string, string> ParseKv(IEnumerable<string> parts)
    {
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts)
        {
            var m = Kv.Match(p);
            if (m.Success)
            {
                kv[m.Groups[1].Value] = m.Groups[2].Value;
            }
        }

        return kv;
    }

    private static int ParseInt(string raw)
    {
        raw = raw.Trim();
        return raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(raw, CultureInfo.InvariantCulture);
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

    private static int LineIndent(string line)
    {
        var n = 0;
        foreach (var ch in line)
        {
            if (ch == ' ')
            {
                n += 1;
            }
            else if (ch == '\t')
            {
                n += 2;
            }
            else
            {
                break;
            }
        }

        return n;
    }

    private static void SkipNoise(string[] raw, ref int i)
    {
        while (i < raw.Length)
        {
            var s = raw[i].Trim();
            if (s.Length == 0 || s.StartsWith('#'))
            {
                i += 1;
                continue;
            }

            break;
        }
    }

    private static List<string> CollectIndented(string[] raw, ref int i, int parentIndent)
    {
        var body = new List<string>();
        var cut = parentIndent + 2;
        while (i < raw.Length)
        {
            var nxt = raw[i];
            var nxts = nxt.Trim();
            var nxti = LineIndent(nxt);
            if (string.IsNullOrEmpty(nxts))
            {
                if (nxti == 0 && body.Count > 0)
                {
                    break;
                }

                i += 1;
                continue;
            }

            if (nxts.StartsWith('#') && nxti <= parentIndent)
            {
                break;
            }

            if (nxti <= parentIndent)
            {
                break;
            }

            body.Add(nxti >= cut ? nxt[cut..] : nxts);
            i += 1;
        }

        return body;
    }
}
