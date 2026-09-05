using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Grandia.Sdk;

/// <summary>
/// In-process port of <c>field_script_dialog_markup.py</c>: tokens ↔ editor markup.
/// Unchanged markup is a no-op on the original token list.
/// </summary>
public static class DialogueMarkup
{
    public const int BoxMaxLines = 3;
    public const int BoxLineMaxChars = 38;

    private const int DefaultSlot = 0x03;
    private const int DefaultPre = 0x0F;
    private static readonly HashSet<int> StillSlots = [0x03, 0x12, 0x21];
    private static readonly HashSet<int> HeaderExtraSkip = [0x00, 0x02, 0x06];
    private static readonly HashSet<int> PrintableExtra = CreatePrintableExtra();
    private static readonly Dictionary<int, string> TextGlyphs = new() { [0xD7] = "♥" };
    private static readonly Dictionary<char, int> TextGlyphBytes = new() { ['♥'] = 0xD7 };

    private static readonly Regex TokenRe = new(
        @"\[\[|" +
        @"\[P:(off|keep|\d+)(?:\s+slot=(0x[0-9A-Fa-f]+|\d+))?(?:\s+pre=(?:0x[0-9A-Fa-f]+|\d+))?\]|" +
        @"\[box:(top|bottom)(?:=(0x[0-9A-Fa-f]+|\d+))?\]|" +
        @"\[0B:(0x[0-9A-Fa-f]+|\d+)\]|" +
        @"\[voice:(?:play|stop)(?:\s+ch=\d+)?(?:\s+id=(?:0x[0-9A-Fa-f]+|\d+))?\]|" +
        @"\[TS:(0x[0-9A-Fa-f]+|\d+)\]|" +
        @"\[K:(?:0x[0-9A-Fa-f]+|\d+)\]|" +
        @"\[D:(\d+)\]|" +
        @"\[clear\]|" +
        @"\[wait\]|" +
        @"\[swap\]|" +
        @"\[menu\]|" +
        @"\[overlay\]|" +
        @"\[/overlay\]|" +
        @"\[raw:[0-9a-fA-F]+\]|" +
        @"\[line\]|" +
        @"\[09:[0-9A-Fa-f]{1,2}(?::(?:0x[0-9A-Fa-f]+|\d+))?\]|" +
        @"\[end(?::[0-9a-fA-F]*)?\]|" +
        @"\[hdr:[0-9a-fA-F]+\]|" +
        @"\[",
        RegexOptions.CultureInvariant);

    private static readonly Regex VoiceRe = new(
        @"\[voice:(play|stop)(?:\s+ch=(\d+))?(?:\s+id=(0x[0-9A-Fa-f]+|\d+))?\]",
        RegexOptions.CultureInvariant);
    private static readonly Regex PRe = new(
        @"\[P:(off|keep|\d+)(?:\s+slot=(0x[0-9A-Fa-f]+|\d+))?(?:\s+pre=(0x[0-9A-Fa-f]+|\d+))?\]",
        RegexOptions.CultureInvariant);
    private static readonly Regex Ctrl09Re = new(
        @"\[09:([0-9A-Fa-f]{1,2})(?::(0x[0-9A-Fa-f]+|\d+))?\]",
        RegexOptions.CultureInvariant);
    private static readonly Regex EndRe = new(@"\[end(?::([0-9a-fA-F]*))?\]", RegexOptions.CultureInvariant);
    private static readonly Regex HdrRe = new(@"\[hdr:([0-9a-fA-F]+)\]", RegexOptions.CultureInvariant);
    private static readonly Regex RawRe = new(@"\[raw:([0-9a-fA-F]+)\]", RegexOptions.CultureInvariant);
    private static readonly Regex KeyLegacyRe = new(@"\[K:(0x[0-9A-Fa-f]+|\d+)\]", RegexOptions.CultureInvariant);

    public static string FromPayload(ReadOnlySpan<byte> payload, bool type8 = false, string? mapStem = null) =>
        FromTokens(DialogueTokens.Tokenize(payload), type8, mapStem);

    public static byte[] ToPayload(ReadOnlySpan<byte> original, string markup, bool type8 = false,
        string? mapStem = null)
    {
        var tokens = DialogueTokens.Tokenize(original);
        var next = Apply(tokens, markup, type8, mapStem);
        return DialogueTokens.Emit(next);
    }

    public static string FromTokens(IReadOnlyList<DialogueToken> tokens, bool type8 = false,
        string? mapStem = null)
    {
        var outb = new StringBuilder();
        int? lastB1 = null;
        var lastPre = DefaultPre;
        var pendingSpoken = false;
        var spokenEmitted = false;
        var stripTextExtra = false;
        var justOpenedOverlay = false;
        var i = 0;
        var n = tokens.Count;

        void EmitWait()
        {
            if (pendingSpoken && !type8)
            {
                outb.Append("[wait]");
                pendingSpoken = false;
            }
        }

        while (i < n)
        {
            var tok = tokens[i];
            if (tok is TerminatorToken term)
            {
                EmitWait();
                var extra = new List<byte>(term.Trailing);
                var j = i + 1;
                while (j < n && tokens[j] is RawByteToken raw)
                {
                    extra.Add((byte)raw.Value);
                    j += 1;
                }

                var content = DialogueTokens.Emit(tokens.Take(i).ToList());
                var inferred = InferredTermPad(content.Length);
                var actualTail = new byte[] { 0x07 }.Concat(extra).ToArray();
                var inferTail = new byte[] { 0x07 }.Concat(inferred).ToArray();
                if (outb.Length == 0 ||
                    !EvenPayload(content.Concat(actualTail).ToArray())
                        .SequenceEqual(EvenPayload(content.Concat(inferTail).ToArray())))
                {
                    outb.Append(extra.Count == 0 ? "[end]" : $"[end:{Convert.ToHexString(extra.ToArray()).ToLowerInvariant()}]");
                }

                break;
            }

            if (IsOverlayClose(tok))
            {
                outb.Append("[/overlay]");
                justOpenedOverlay = false;
                i += 1;
                continue;
            }

            if (tok is ControlToken ctrl)
            {
                outb.Append(ctrl.Sub == 0x0B && ctrl.Arg is int arg ? Format0B(arg) : Format09(ctrl));
                i += 1;
                continue;
            }

            if (tok is FaceKeyToken fk)
            {
                outb.Append($"[TS:{StoredToTimestamp(fk.FaceKey)}]");
                i += 1;
                continue;
            }

            if (tok is LineStartToken)
            {
                outb.Append("[line]");
                i += 1;
                continue;
            }

            if (IsHidden(tok))
            {
                i += 1;
                continue;
            }

            if (tok is HeaderToken hdr)
            {
                if (!IsStillHeader(hdr))
                {
                    outb.Append($"[hdr:{hdr.Pre:x2}{hdr.B1:x2}{hdr.B2:x2}{hdr.B3:x2}{hdr.Expr:x2}]");
                    lastB1 = hdr.B1;
                    lastPre = hdr.Pre;
                    justOpenedOverlay = false;
                    stripTextExtra = false;
                    i += 1;
                    continue;
                }

                pendingSpoken = false;
                var (extra, inText) = HeaderExtra(tokens, i);
                if (justOpenedOverlay && extra is null && !IsSlotClear(tokens, i) &&
                    lastB1 is int lb && hdr.B1 == lb)
                {
                    lastB1 = hdr.B1;
                    lastPre = hdr.Pre;
                    justOpenedOverlay = false;
                    stripTextExtra = false;
                    i += 1;
                    continue;
                }

                justOpenedOverlay = false;
                if (IsSlotClear(tokens, i))
                {
                    outb.Append(FormatP("off", hdr.B1, lastB1, hdr.Pre, lastPre));
                    lastB1 = hdr.B1;
                    lastPre = hdr.Pre;
                    stripTextExtra = false;
                    i += 1;
                    continue;
                }

                if (extra is null)
                {
                    var expr = spokenEmitted || hdr.B1 != DefaultSlot ? hdr.Expr : 1;
                    outb.Append(FormatP(expr.ToString(CultureInfo.InvariantCulture), hdr.B1, lastB1,
                        hdr.Pre, lastPre));
                    lastB1 = hdr.B1;
                    lastPre = hdr.Pre;
                    stripTextExtra = false;
                    i += 1;
                    continue;
                }

                var face = extra.Value >= 1 ? extra.Value - 1 : hdr.Expr;
                outb.Append(FormatP(face.ToString(CultureInfo.InvariantCulture), hdr.B1, lastB1,
                    hdr.Pre, lastPre));
                lastB1 = hdr.B1;
                lastPre = hdr.Pre;
                var skipHas06 = false;
                var k = i + 1;
                while (k < n && tokens[k] is RawByteToken sk && HeaderExtraSkip.Contains(sk.Value))
                {
                    if (sk.Value == 0x06)
                    {
                        skipHas06 = true;
                    }

                    k += 1;
                }

                stripTextExtra = inText && !skipHas06;
                i += 1;
                continue;
            }

            if (tok is TextToken text)
            {
                var chunk = text.Text;
                if (stripTextExtra && chunk.Length > 0 && PrintableExtra.Contains(chunk[0]))
                {
                    chunk = chunk[1..];
                }

                stripTextExtra = false;
                if (chunk.Length > 0)
                {
                    outb.Append(chunk.Replace("[", "[[", StringComparison.Ordinal));
                    pendingSpoken = true;
                    spokenEmitted = true;
                }

                i += 1;
                continue;
            }

            if (tok is NewlineToken)
            {
                outb.Append('\n');
                pendingSpoken = true;
                spokenEmitted = true;
                stripTextExtra = false;
                i += 1;
                continue;
            }

            if (tok is PageBreakToken)
            {
                var nxt = NextSignificant(tokens, i + 1);
                if (pendingSpoken && nxt is not HoldToken)
                {
                    EmitWait();
                }
                else
                {
                    pendingSpoken = false;
                }

                outb.Append("[clear]");
                stripTextExtra = false;
                i += 1;
                continue;
            }

            if (tok is HoldToken hold)
            {
                outb.Append($"[D:{hold.Hold}]");
                pendingSpoken = false;
                stripTextExtra = false;
                i += 1;
                continue;
            }

            if (tok is RawByteToken rawb)
            {
                if (TextGlyphs.TryGetValue(rawb.Value, out var glyph))
                {
                    outb.Append(glyph);
                    pendingSpoken = true;
                    spokenEmitted = true;
                    i += 1;
                    continue;
                }

                if (IsStreamMenu(tokens, i))
                {
                    outb.Append("[menu]");
                    justOpenedOverlay = false;
                    i += 1;
                    continue;
                }

                if (IsOverlayOpen(tokens, i))
                {
                    outb.Append("[overlay]");
                    justOpenedOverlay = true;
                    i += 1;
                    continue;
                }

                if (IsExclusiveSwap(tokens, i))
                {
                    outb.Append("[swap]");
                    justOpenedOverlay = false;
                    i += 1;
                    continue;
                }

                if (RawBelongsToHeader(tokens, i))
                {
                    i += 1;
                    continue;
                }

                var buf = new List<byte>();
                while (i < n && tokens[i] is RawByteToken rb && !IsNamedStreamRaw(tokens, i))
                {
                    buf.Add((byte)rb.Value);
                    i += 1;
                }

                if (buf.Count > 0)
                {
                    outb.Append($"[raw:{Convert.ToHexString(buf.ToArray()).ToLowerInvariant()}]");
                }

                justOpenedOverlay = false;
                continue;
            }

            i += 1;
        }

        EmitWait();
        return outb.ToString();
    }

    public static List<DialogueToken> Apply(IReadOnlyList<DialogueToken> original, string markup,
        bool type8 = false, string? mapStem = null)
    {
        var text = Normalize(markup);
        var current = FromTokens(original, type8, mapStem);
        var wantsConfirm = !type8 && text.Contains("[wait]", StringComparison.Ordinal);
        var leftoverLine = original.Any(t => t is LineStartToken);
        if (text == current && !(wantsConfirm && leftoverLine) && !MenuOpenerNeedsFix(original))
        {
            return original.ToList();
        }

        var events = Parse(text);
        if (events.Count == 0)
        {
            throw new ArgumentException("Dialog markup cannot be empty");
        }

        if (original.Count > 0)
        {
            var oldScore = OverflowScore(LayoutFromEvents(Parse(current), type8).violations);
            var newScore = OverflowScore(LayoutFromEvents(events, type8).violations);
            if (newScore > oldScore && LayoutFromEvents(events, type8).violations.Count > 0)
            {
                throw new ArgumentException(LayoutFromEvents(events, type8).violations[0]);
            }
        }

        var (prefix, _, term) = SplitPrefix(original);
        if (wantsConfirm)
        {
            prefix = prefix.Where(t => t is not LineStartToken).ToList();
        }

        var srcHeader = FirstStillHeader(original);
        int? lastB1 = null;
        var output = new List<DialogueToken>(prefix);
        var spokenOpen = false;
        var pendingOverlay = false;
        var synthOverlayHeader = false;
        var pendingSwap = false;
        var skipBefore = -1;
        var skipIds = new HashSet<int>();
        byte[]? endPad = null;

        int ResolveB1(int? evB1) => evB1 ?? lastB1 ?? DefaultSlot;

        int EmitHeader(int b1, int? pre = null)
        {
            var hdr = CloneHeader(srcHeader, b1);
            if (pre is int p && p != hdr.Pre)
            {
                hdr = new HeaderToken(p, hdr.B1, hdr.B2, hdr.B3, hdr.Expr);
            }

            output.Add(hdr);
            lastB1 = hdr.B1;
            srcHeader = hdr;
            return output.Count - 1;
        }

        void FlushType8Hold()
        {
            if (type8 && spokenOpen && original.Count > 0)
            {
                output.Add(new HoldToken(30));
                spokenOpen = false;
            }
        }

        void AttachExtra(int headerI, int expr)
        {
            WriteExtra(output, headerI, expr + 1);
        }

        void FlushSwap()
        {
            if (pendingSwap)
            {
                output.Add(new RawByteToken(0x08));
                pendingSwap = false;
            }
        }

        void FlushPendingOverlay()
        {
            if (pendingOverlay)
            {
                output.Add(new RawByteToken(0x06));
                pendingOverlay = false;
            }
        }

        bool InsertOverlayBeforeLastHeader()
        {
            for (var i = output.Count - 1; i >= 0; i--)
            {
                if (output[i] is HeaderToken)
                {
                    if (i > 0 && output[i - 1] is RawByteToken raw && raw.Value == 0x06)
                    {
                        return true;
                    }

                    output.Insert(i, new RawByteToken(0x06));
                    return true;
                }
            }

            return false;
        }

        bool LastHeaderHasSpoken()
        {
            for (var i = output.Count - 1; i >= 0; i--)
            {
                if (output[i] is HeaderToken)
                {
                    return output.Skip(i + 1).Any(t =>
                        t is TextToken or NewlineToken or HoldToken or PageBreakToken);
                }
            }

            return false;
        }

        void EnsureOverlayHeader()
        {
            if (!synthOverlayHeader)
            {
                return;
            }

            FlushSwap();
            FlushPendingOverlay();
            EmitHeader(lastB1 ?? DefaultSlot);
            synthOverlayHeader = false;
        }

        bool OverlayTargetsNextHeader(int start)
        {
            for (var i = start + 1; i < events.Count; i++)
            {
                if (events[i] is EvPortrait or EvSlotOff or EvKeep)
                {
                    return true;
                }

                if (events[i] is EvOverlayEnd)
                {
                    return false;
                }
            }

            return false;
        }

        void EmitStill(object e, bool faceExtra)
        {
            synthOverlayHeader = false;
            FlushType8Hold();
            FlushSwap();
            FlushPendingOverlay();
            if (e is EvSlotOff off)
            {
                var hi = EmitHeader(ResolveB1(off.B1), off.Pre);
                output.Insert(hi + 1, new RawByteToken(0x00));
                return;
            }

            if (e is EvKeep keep)
            {
                EmitHeader(ResolveB1(keep.B1), keep.Pre);
                return;
            }

            if (e is EvPortrait p)
            {
                var b1 = ResolveB1(p.B1);
                var hi = EmitHeader(b1, p.Pre);
                if (faceExtra)
                {
                    AttachExtra(hi, p.Expr);
                }
            }
        }

        for (var evI = 0; evI < events.Count; evI++)
        {
            if (evI < skipBefore || skipIds.Contains(evI))
            {
                continue;
            }

            var ev = events[evI];
            switch (ev)
            {
                case EvOverlay:
                    if (OverlayTargetsNextHeader(evI))
                    {
                        pendingOverlay = true;
                        synthOverlayHeader = false;
                    }
                    else if (LastHeaderHasSpoken())
                    {
                        pendingOverlay = true;
                        synthOverlayHeader = true;
                    }
                    else if (InsertOverlayBeforeLastHeader())
                    {
                        synthOverlayHeader = false;
                    }
                    else
                    {
                        pendingOverlay = true;
                        synthOverlayHeader = true;
                    }

                    break;
                case EvOverlayEnd:
                    EnsureOverlayHeader();
                    if (pendingOverlay)
                    {
                        InsertOverlayBeforeLastHeader();
                        pendingOverlay = false;
                    }

                    output.Add(new ControlToken(0x02));
                    synthOverlayHeader = false;
                    break;
                case EvSwap:
                    pendingSwap = true;
                    break;
                case EvMenu:
                {
                    var j = evI + 1;
                    var cluster = new List<object>();
                    while (j < events.Count && events[j] is EvPortrait or EvSlotOff or EvKeep or EvNewline)
                    {
                        cluster.Add(events[j]);
                        j += 1;
                    }

                    skipBefore = j;
                    var offs = cluster.OfType<EvSlotOff>().ToList();
                    var nls = cluster.OfType<EvNewline>().ToList();
                    var faces = cluster.Where(e => e is EvPortrait or EvKeep).ToList();
                    var prelude = faces.Count > 0 ? faces.Take(faces.Count - 1).ToList() : [];
                    var last = faces.Count > 0 ? faces[^1] : null;
                    var lastI = last is null ? -1 : cluster.IndexOf(last);
                    var offsBefore = offs.Where(e => lastI < 0 || cluster.IndexOf(e) < lastI).ToList();
                    var offsAfter = offs.Where(e => lastI >= 0 && cluster.IndexOf(e) > lastI).ToList();
                    foreach (var e in offsAfter.Cast<object>().Concat(prelude))
                    {
                        EmitStill(e, true);
                    }

                    if (output.Count > 0 && (output[^1] is HeaderToken ||
                        (output[^1] is RawByteToken rb && HeaderExtraSkip.Contains(rb.Value))))
                    {
                        output.Add(new NewlineToken());
                    }

                    output.Add(new RawByteToken(0x05));
                    foreach (var e in offsBefore)
                    {
                        EmitStill(e, true);
                    }

                    int? lastIdx = null;
                    for (var k = evI + 1; k < skipBefore; k++)
                    {
                        if (events[k] is EvPortrait)
                        {
                            lastIdx = k;
                        }
                    }

                    if (last is EvPortrait lp)
                    {
                        var b1 = ResolveB1(lp.B1);
                        var hi = EmitHeader(b1, lp.Pre);
                        var nj = skipBefore;
                        if (nj < events.Count && events[nj] is EvRaw raw && raw.Data.Length > 0 &&
                            !(raw.Data.Length == 1 && raw.Data[0] == 0x06) &&
                            raw.Data.All(b => HeaderExtraSkip.Contains(b)))
                        {
                            foreach (var b in raw.Data)
                            {
                                output.Add(new RawByteToken(b));
                            }

                            skipIds.Add(nj);
                        }

                        if (lastIdx is int li && StillWantsFaceExtra(events, li))
                        {
                            AttachExtra(hi, lp.Expr);
                        }
                    }
                    else if (last is not null)
                    {
                        EmitStill(last, false);
                    }

                    foreach (var _ in nls)
                    {
                        output.Add(new NewlineToken());
                    }

                    break;
                }
                case EvBox:
                    break;
                case EvVoice v:
                    FlushSwap();
                    FlushPendingOverlay();
                    output.Add(new ControlToken(0x0B, v.Arg & 0xFFFF));
                    break;
                case EvLine:
                    FlushSwap();
                    FlushPendingOverlay();
                    output.Add(new LineStartToken());
                    break;
                case EvCtrl09 c:
                    FlushSwap();
                    FlushPendingOverlay();
                    output.Add(new ControlToken(c.Sub, c.Arg));
                    break;
                case EvHdr h:
                    synthOverlayHeader = false;
                    FlushType8Hold();
                    FlushSwap();
                    FlushPendingOverlay();
                    var nh = new HeaderToken(h.Pre, h.B1, h.B2, h.B3, h.Expr);
                    output.Add(nh);
                    lastB1 = nh.B1;
                    srcHeader = nh;
                    break;
                case EvEnd end:
                    endPad = end.Pad;
                    break;
                case EvRaw raw:
                    FlushSwap();
                    FlushPendingOverlay();
                    foreach (var b in raw.Data)
                    {
                        output.Add(new RawByteToken(b));
                    }

                    break;
                case EvFaceKey fk:
                    FlushSwap();
                    FlushPendingOverlay();
                    output.Add(new FaceKeyToken(fk.Key & 0xFFFF));
                    break;
                case EvSlotOff off:
                    synthOverlayHeader = false;
                    FlushType8Hold();
                    FlushSwap();
                    FlushPendingOverlay();
                    var oi = EmitHeader(ResolveB1(off.B1), off.Pre);
                    output.Insert(oi + 1, new RawByteToken(0x00));
                    break;
                case EvKeep keep:
                    synthOverlayHeader = false;
                    FlushType8Hold();
                    FlushSwap();
                    FlushPendingOverlay();
                    EmitHeader(ResolveB1(keep.B1), keep.Pre);
                    break;
                case EvPortrait p:
                {
                    synthOverlayHeader = false;
                    FlushType8Hold();
                    FlushSwap();
                    FlushPendingOverlay();
                    var b1 = ResolveB1(p.B1);
                    var hi = EmitHeader(b1, p.Pre);
                    var nj = evI + 1;
                    if (nj < events.Count && events[nj] is EvRaw raw && raw.Data.Length > 0 &&
                        !(raw.Data.Length == 1 && raw.Data[0] == 0x06) &&
                        raw.Data.All(b => HeaderExtraSkip.Contains(b)))
                    {
                        foreach (var b in raw.Data)
                        {
                            output.Add(new RawByteToken(b));
                        }

                        skipIds.Add(nj);
                    }

                    if (StillWantsFaceExtra(events, evI))
                    {
                        AttachExtra(hi, p.Expr);
                    }

                    break;
                }
                case EvText te:
                    EnsureOverlayHeader();
                    FlushSwap();
                    if (te.Text.Length > 0)
                    {
                        AppendMarkupText(output, te.Text);
                        spokenOpen = true;
                    }

                    break;
                case EvNewline:
                    EnsureOverlayHeader();
                    FlushSwap();
                    output.Add(new NewlineToken());
                    spokenOpen = true;
                    break;
                case EvClear:
                    EnsureOverlayHeader();
                    FlushSwap();
                    output.Add(new PageBreakToken());
                    break;
                case EvDelay d:
                    EnsureOverlayHeader();
                    FlushSwap();
                    output.Add(new HoldToken(Math.Clamp(d.Frames, 0, 255)));
                    spokenOpen = false;
                    break;
                case EvWait:
                    spokenOpen = false;
                    break;
            }
        }

        FlushType8Hold();
        EnsureOverlayHeader();
        if (pendingOverlay)
        {
            if (!InsertOverlayBeforeLastHeader())
            {
                output.Add(new RawByteToken(0x06));
            }
        }

        FlushSwap();
        MergePrintableFaceExtras(output);
        if (endPad is not null)
        {
            var trailing = endPad.Length > 0 && endPad[0] == 0x00 ? new byte[] { 0x00 } : [];
            var rest = trailing.Length > 0 ? endPad.AsSpan(1).ToArray() : endPad;
            output.Add(new TerminatorToken(trailing));
            foreach (var b in rest)
            {
                output.Add(new RawByteToken(b));
            }
        }
        else if (term is not null)
        {
            output.Add(term);
        }
        else if (output.Count == 0 || output[^1] is not TerminatorToken)
        {
            var pad = new TerminatorToken();
            if (DialogueTokens.Emit([.. output, pad]).Length % 2 != 0)
            {
                pad = new TerminatorToken([0x00]);
            }

            output.Add(pad);
        }

        return output;
    }

    internal static List<object> Parse(string text)
    {
        var src = Normalize(text);
        var events = new List<object>();
        var i = 0;
        var n = src.Length;
        var textBuf = new StringBuilder();

        void FlushText()
        {
            if (textBuf.Length > 0)
            {
                events.Add(new EvText(textBuf.ToString()));
                textBuf.Clear();
            }
        }

        while (i < n)
        {
            if (src[i] == '\n')
            {
                FlushText();
                events.Add(new EvNewline());
                i += 1;
                continue;
            }

            if (src[i] != '[')
            {
                textBuf.Append(src[i]);
                i += 1;
                continue;
            }

            var m = TokenRe.Match(src, i);
            if (!m.Success || m.Value == "[")
            {
                var end = src.IndexOf(']', i);
                var snippet = src[i..(end >= 0 ? end + 1 : Math.Min(i + 16, n))];
                throw new ArgumentException($"Unknown markup token {snippet}");
            }

            var tok = m.Value;
            if (tok == "[[")
            {
                textBuf.Append('[');
                i = m.Index + m.Length;
                continue;
            }

            FlushText();
            if (tok == "[clear]")
            {
                events.Add(new EvClear());
            }
            else if (tok == "[wait]")
            {
                events.Add(new EvWait());
            }
            else if (tok == "[swap]")
            {
                events.Add(new EvSwap());
            }
            else if (tok == "[menu]")
            {
                events.Add(new EvMenu());
            }
            else if (tok == "[overlay]")
            {
                events.Add(new EvOverlay());
            }
            else if (tok == "[/overlay]")
            {
                events.Add(new EvOverlayEnd());
            }
            else if (tok.StartsWith("[box:", StringComparison.Ordinal))
            {
                events.Add(new EvBox());
            }
            else if (tok.StartsWith("[0B:", StringComparison.Ordinal))
            {
                events.Add(new EvVoice(ParseInt(m.Groups[5].Value)));
            }
            else if (tok.StartsWith("[voice:", StringComparison.Ordinal))
            {
                var vm = VoiceRe.Match(tok);
                if (!vm.Success)
                {
                    throw new ArgumentException($"Unknown markup token {tok}");
                }

                var play = vm.Groups[1].Value == "play";
                var channel = vm.Groups[2].Success ? int.Parse(vm.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                var voiceId = vm.Groups[3].Success ? ParseInt(vm.Groups[3].Value) : 0;
                events.Add(new EvVoice(EncodeVoiceArg(play, channel, voiceId)));
            }
            else if (tok.StartsWith("[TS:", StringComparison.Ordinal))
            {
                events.Add(new EvFaceKey(TimestampToStored(ParseInt(m.Groups[6].Value))));
            }
            else if (tok.StartsWith("[K:", StringComparison.Ordinal))
            {
                var km = KeyLegacyRe.Match(tok);
                events.Add(new EvFaceKey(ParseInt(km.Groups[1].Value)));
            }
            else if (tok.StartsWith("[D:", StringComparison.Ordinal))
            {
                events.Add(new EvDelay(int.Parse(m.Groups[7].Value, CultureInfo.InvariantCulture)));
            }
            else if (tok == "[line]")
            {
                events.Add(new EvLine());
            }
            else if (tok.StartsWith("[09:", StringComparison.Ordinal))
            {
                var cm = Ctrl09Re.Match(tok);
                var sub = int.Parse(cm.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                events.Add(cm.Groups[2].Success
                    ? new EvCtrl09(sub, ParseInt(cm.Groups[2].Value))
                    : new EvCtrl09(sub, null));
            }
            else if (tok.StartsWith("[end", StringComparison.Ordinal))
            {
                var em = EndRe.Match(tok);
                var hx = em.Groups[1].Success ? em.Groups[1].Value : "";
                events.Add(new EvEnd(hx.Length == 0 ? [] : Convert.FromHexString(hx)));
            }
            else if (tok.StartsWith("[hdr:", StringComparison.Ordinal))
            {
                var hm = HdrRe.Match(tok);
                var raw = Convert.FromHexString(hm.Groups[1].Value);
                events.Add(new EvHdr(raw[0], raw[1], raw[2], raw[3], raw[4]));
            }
            else if (tok.StartsWith("[raw:", StringComparison.Ordinal))
            {
                var rm = RawRe.Match(tok);
                events.Add(new EvRaw(Convert.FromHexString(rm.Groups[1].Value)));
            }
            else
            {
                var pm = PRe.Match(tok);
                var kind = pm.Groups[1].Value;
                int? b1 = pm.Groups[2].Success ? ParseInt(pm.Groups[2].Value) : null;
                int? pre = pm.Groups[3].Success ? ParseInt(pm.Groups[3].Value) : null;
                events.Add(kind switch
                {
                    "off" => new EvSlotOff(b1, pre),
                    "keep" => new EvKeep(b1, pre),
                    _ => new EvPortrait(int.Parse(kind, CultureInfo.InvariantCulture), b1, pre),
                });
            }

            i = m.Index + m.Length;
        }

        FlushText();
        return events;
    }

    private static HashSet<int> CreatePrintableExtra()
    {
        var set = new HashSet<int>();
        for (var c = 0x20; c <= 0x3C; c++)
        {
            set.Add(c);
        }

        return set;
    }

    private static string Normalize(string text) =>
        (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static int ParseInt(string raw)
    {
        raw = raw.Trim();
        return raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(raw, CultureInfo.InvariantCulture);
    }

    private static bool IsStillHeader(DialogueToken tok) =>
        tok is HeaderToken h && h.B2 == 0x0A && h.B3 == 0x0C && StillSlots.Contains(h.B1);

    private static bool IsOverlayClose(DialogueToken tok) =>
        tok is ControlToken c && c.Sub == 0x02 && c.Arg is null;

    private static bool IsHidden(DialogueToken tok) =>
        tok is ControlToken c && !IsOverlayClose(c) && !(c.Sub == 0x0B && c.Arg is not null);

    private static (int? extra, bool inText) HeaderExtra(IReadOnlyList<DialogueToken> tokens, int headerI)
    {
        var j = headerI + 1;
        var n = tokens.Count;
        while (j < n && tokens[j] is RawByteToken skip && HeaderExtraSkip.Contains(skip.Value))
        {
            j += 1;
        }

        if (j < n && tokens[j] is RawByteToken raw)
        {
            return raw.Value == 0x00 ? (null, false) : (raw.Value, false);
        }

        if (j < n && tokens[j] is TextToken t && t.Text.Length > 0 && PrintableExtra.Contains(t.Text[0]))
        {
            return (t.Text[0], true);
        }

        return (null, false);
    }

    private static bool IsSlotClear(IReadOnlyList<DialogueToken> tokens, int headerI)
    {
        var (extra, _) = HeaderExtra(tokens, headerI);
        if (extra is not null)
        {
            return false;
        }

        var j = headerI + 1;
        return j < tokens.Count && tokens[j] is RawByteToken raw && raw.Value == 0x00;
    }

    private static DialogueToken? NextSignificant(IReadOnlyList<DialogueToken> tokens, int i)
    {
        while (i < tokens.Count)
        {
            if (tokens[i] is HoldToken or HeaderToken or TextToken or NewlineToken or PageBreakToken
                or TerminatorToken)
            {
                return tokens[i];
            }

            i += 1;
        }

        return null;
    }

    private static bool PrecedingHeader(IReadOnlyList<DialogueToken> tokens, int i)
    {
        var prev = i - 1;
        while (prev >= 0 && IsHidden(tokens[prev]))
        {
            prev -= 1;
        }

        return prev >= 0 && tokens[prev] is HeaderToken;
    }

    private static int SkipHiddenAfterOverlay(IReadOnlyList<DialogueToken> tokens, int j)
    {
        while (j < tokens.Count)
        {
            var tok = tokens[j];
            if (tok is FaceKeyToken or LineStartToken)
            {
                j += 1;
                continue;
            }

            if (tok is ControlToken && !IsOverlayClose(tok))
            {
                j += 1;
                continue;
            }

            if (tok is RawByteToken raw && raw.Value is 0x00 or 0x02)
            {
                j += 1;
                continue;
            }

            break;
        }

        return j;
    }

    private static bool FollowsWithHeader(IReadOnlyList<DialogueToken> tokens, int i)
    {
        var j = SkipHiddenAfterOverlay(tokens, i + 1);
        return j < tokens.Count && tokens[j] is HeaderToken;
    }

    private static bool IsOverlayOpen(IReadOnlyList<DialogueToken> tokens, int i)
    {
        if (i >= tokens.Count || tokens[i] is not RawByteToken { Value: 0x06 })
        {
            return false;
        }

        if (FollowsWithHeader(tokens, i))
        {
            return true;
        }

        var j = SkipHiddenAfterOverlay(tokens, i + 1);
        return j < tokens.Count && IsOverlayClose(tokens[j]);
    }

    private static bool IsExclusiveSwap(IReadOnlyList<DialogueToken> tokens, int i)
    {
        if (i >= tokens.Count || tokens[i] is not RawByteToken { Value: 0x08 })
        {
            return false;
        }

        if (PrecedingHeader(tokens, i))
        {
            return false;
        }

        var j = SkipHiddenAfterOverlay(tokens, i + 1);
        if (j >= tokens.Count)
        {
            return false;
        }

        return tokens[j] is HeaderToken or TextToken or NewlineToken or HoldToken or PageBreakToken;
    }

    private static bool IsHeaderFaceExtra(IReadOnlyList<DialogueToken> tokens, int i)
    {
        if (i <= 0 || i >= tokens.Count || tokens[i] is not RawByteToken raw ||
            HeaderExtraSkip.Contains(raw.Value))
        {
            return false;
        }

        var prev = i - 1;
        int? headerI = null;
        while (prev >= 0)
        {
            if (tokens[prev] is HeaderToken)
            {
                headerI = prev;
                break;
            }

            if (tokens[prev] is RawByteToken sk && HeaderExtraSkip.Contains(sk.Value))
            {
                prev -= 1;
                continue;
            }

            if (IsHidden(tokens[prev]))
            {
                prev -= 1;
                continue;
            }

            return false;
        }

        if (headerI is null)
        {
            return false;
        }

        var (extra, inText) = HeaderExtra(tokens, headerI.Value);
        if (inText || extra is null)
        {
            return false;
        }

        var j = headerI.Value + 1;
        while (j < tokens.Count && tokens[j] is RawByteToken sk2 && HeaderExtraSkip.Contains(sk2.Value))
        {
            j += 1;
        }

        return j == i;
    }

    private static bool RawBelongsToHeader(IReadOnlyList<DialogueToken> tokens, int i)
    {
        if (IsHeaderFaceExtra(tokens, i))
        {
            return true;
        }

        if (i < 0 || i >= tokens.Count || tokens[i] is not RawByteToken tok)
        {
            return false;
        }

        var prev = i - 1;
        while (prev >= 0 && (IsHidden(tokens[prev]) ||
            (tokens[prev] is RawByteToken sk && HeaderExtraSkip.Contains(sk.Value))))
        {
            prev -= 1;
        }

        if (prev < 0 || tokens[prev] is not HeaderToken)
        {
            return false;
        }

        var (extra, _) = HeaderExtra(tokens, prev);
        return tok.Value == 0x00 && extra is null;
    }

    private static bool IsStreamMenu(IReadOnlyList<DialogueToken> tokens, int i) =>
        i >= 0 && i < tokens.Count && tokens[i] is RawByteToken { Value: 0x05 } &&
        !IsHeaderFaceExtra(tokens, i);

    private static bool IsNamedStreamRaw(IReadOnlyList<DialogueToken> tokens, int i)
    {
        if (i < 0 || i >= tokens.Count || tokens[i] is not RawByteToken raw)
        {
            return false;
        }

        if (TextGlyphs.ContainsKey(raw.Value))
        {
            return true;
        }

        return IsStreamMenu(tokens, i) || IsOverlayOpen(tokens, i) || IsExclusiveSwap(tokens, i) ||
               RawBelongsToHeader(tokens, i);
    }

    private static bool MenuOpenerNeedsFix(IReadOnlyList<DialogueToken> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!IsStreamMenu(tokens, i))
            {
                continue;
            }

            var headers = 0;
            for (var j = i + 1; j < tokens.Count && tokens[j] is not TextToken and not TerminatorToken; j++)
            {
                if (tokens[j] is HeaderToken)
                {
                    headers += 1;
                    var (extra, _) = HeaderExtra(tokens, j);
                    if (extra is not null || headers > 1)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static string FormatP(string kind, int b1, int? lastB1, int pre, int lastPre)
    {
        var bits = new List<string>();
        if (b1 != DefaultSlot || (lastB1 is int lb && b1 != lb))
        {
            bits.Add($"slot=0x{b1:x2}");
        }

        if (pre != lastPre)
        {
            bits.Add($"pre=0x{pre:x2}");
        }

        return bits.Count > 0 ? $"[P:{kind} {string.Join(" ", bits)}]" : $"[P:{kind}]";
    }

    private static string Format09(ControlToken tok) =>
        tok.Arg is int arg ? $"[09:{tok.Sub:X}:0x{arg:x}]" : $"[09:{tok.Sub:X2}]";

    private static (int play, int channel, int id) DecodeVoiceArg(int arg)
    {
        var engine = ((arg & 0xFF) << 8) | ((arg >> 8) & 0xFF);
        return ((engine & 0x8000) == 0 ? 1 : 0, (engine >> 13) & 3, engine & 0x1FFF);
    }

    private static int EncodeVoiceArg(bool play, int channel, int voiceId)
    {
        var engine = (play ? 0 : 0x8000) | ((channel & 3) << 13) | (voiceId & 0x1FFF);
        return ((engine & 0xFF) << 8) | ((engine >> 8) & 0xFF);
    }

    private static int StoredToTimestamp(int stored)
    {
        var word = stored & 0xFFFF;
        return ((word & 0xFF) << 8) | (word >> 8);
    }

    private static int TimestampToStored(int timestamp) => StoredToTimestamp(timestamp);

    private static string Format0B(int arg)
    {
        var (play, channel, id) = DecodeVoiceArg(arg);
        var parts = new List<string> { play != 0 ? "play" : "stop" };
        if (channel != 0)
        {
            parts.Add($"ch={channel}");
        }

        if (id != 0)
        {
            parts.Add($"id=0x{id:x}");
        }

        return "[voice:" + string.Join(" ", parts) + "]";
    }

    private static byte[] InferredTermPad(int contentLen) => contentLen % 2 == 0 ? [0x00] : [];

    private static byte[] EvenPayload(byte[] data) => data.Length % 2 == 0 ? data : [.. data, 0x00];

    private static HeaderToken CloneHeader(HeaderToken? src, int b1) =>
        src is null
            ? new HeaderToken(0x0F, b1, 0x0A, 0x0C, 0)
            : new HeaderToken(src.Pre, b1, src.B2, src.B3, src.Expr);

    private static void WriteExtra(List<DialogueToken> tokens, int headerI, int extra)
    {
        var j = headerI + 1;
        while (j < tokens.Count && tokens[j] is RawByteToken sk && HeaderExtraSkip.Contains(sk.Value))
        {
            j += 1;
        }

        if (j < tokens.Count && tokens[j] is TextToken t && t.Text.Length > 0)
        {
            var rest = t.Text;
            if (PrintableExtra.Contains(rest[0]))
            {
                rest = rest[1..];
            }

            if (PrintableExtra.Contains(extra))
            {
                tokens[j] = new TextToken((char)extra + rest);
            }
            else
            {
                tokens.Insert(j, new RawByteToken(extra));
                tokens[j + 1] = new TextToken(rest);
            }

            return;
        }

        if (PrintableExtra.Contains(extra) && j < tokens.Count && tokens[j] is TextToken t2)
        {
            tokens[j] = new TextToken((char)extra + t2.Text);
            return;
        }

        tokens.Insert(j, new RawByteToken(extra));
    }

    private static void MergePrintableFaceExtras(List<DialogueToken> tokens)
    {
        var i = 0;
        while (i < tokens.Count - 1)
        {
            if (tokens[i] is RawByteToken raw && PrintableExtra.Contains(raw.Value) &&
                tokens[i + 1] is TextToken t && i > 0)
            {
                var prev = i - 1;
                while (prev >= 0 && tokens[prev] is RawByteToken sk && HeaderExtraSkip.Contains(sk.Value))
                {
                    prev -= 1;
                }

                if (prev >= 0 && tokens[prev] is HeaderToken)
                {
                    tokens[i] = new TextToken((char)raw.Value + t.Text);
                    tokens.RemoveAt(i + 1);
                    i += 1;
                    continue;
                }
            }

            i += 1;
        }
    }

    private static (List<DialogueToken> prefix, List<DialogueToken> rest, TerminatorToken? term)
        SplitPrefix(IReadOnlyList<DialogueToken> tokens)
    {
        var prefix = new List<DialogueToken>();
        TerminatorToken? term = null;
        var rest = new List<DialogueToken>();
        var seen = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            if (tok is TerminatorToken t)
            {
                term = t;
                continue;
            }

            if (!seen && (IsOverlayClose(tok) || IsOverlayOpen(tokens, i) || tok is ControlToken ||
                IsExclusiveSwap(tokens, i) || IsStreamMenu(tokens, i)))
            {
                seen = true;
                rest.Add(tok);
                continue;
            }

            if (!seen && IsHidden(tok))
            {
                prefix.Add(tok);
                continue;
            }

            if (!seen && tok is HeaderToken && !IsStillHeader(tok))
            {
                prefix.Add(tok);
                continue;
            }

            if (!seen && tok is RawByteToken)
            {
                prefix.Add(tok);
                continue;
            }

            seen = true;
            rest.Add(tok);
        }

        return (prefix, rest, term);
    }

    private static HeaderToken? FirstStillHeader(IReadOnlyList<DialogueToken> tokens)
    {
        foreach (var tok in tokens)
        {
            if (IsStillHeader(tok) && tok is HeaderToken h)
            {
                return h;
            }
        }

        return null;
    }

    private static void AppendMarkupText(List<DialogueToken> output, string text)
    {
        var buf = new StringBuilder();
        foreach (var ch in text)
        {
            if (TextGlyphBytes.TryGetValue(ch, out var raw))
            {
                if (buf.Length > 0)
                {
                    output.Add(new TextToken(buf.ToString()));
                    buf.Clear();
                }

                output.Add(new RawByteToken(raw));
                continue;
            }

            buf.Append(ch);
        }

        if (buf.Length > 0)
        {
            output.Add(new TextToken(buf.ToString()));
        }
    }

    private static bool StillWantsFaceExtra(IReadOnlyList<object> events, int start)
    {
        if (events[start] is EvPortrait { Expr: 0 })
        {
            return false;
        }

        for (var i = start + 1; i < events.Count; i++)
        {
            var ev = events[i];
            if (ev is EvText te)
            {
                if (events[start] is EvPortrait { Expr: 1 } &&
                    (te.Text.Length == 0 || !PrintableExtra.Contains(te.Text[0])))
                {
                    return false;
                }

                return true;
            }

            if (ev is EvNewline)
            {
                return events[start] is not EvPortrait { Expr: 1 };
            }

            if (ev is EvLine or EvVoice or EvCtrl09 or EvFaceKey or EvDelay or EvWait or EvBox)
            {
                continue;
            }

            if (ev is EvClear)
            {
                return false;
            }

            if (ev is EvRaw raw && raw.Data.Length == 1 && raw.Data[0] == 0x06)
            {
                return false;
            }

            if (ev is EvPortrait or EvSlotOff or EvKeep or EvSwap or EvMenu or EvHdr or EvOverlayEnd
                or EvEnd)
            {
                return true;
            }
        }

        return true;
    }

    private static (List<string> boxes, List<string> violations) LayoutFromEvents(
        IReadOnlyList<object> events, bool type8)
    {
        var active = type8 ? "top" : "bottom";
        var overlay = false;
        var inMenu = false;
        var menuHasText = false;
        var lines = new List<string> { "" };
        var finished = new List<(string kind, List<string> lines)>();

        string Kind() => inMenu ? "menu" : overlay ? "overlay" : active;

        void Dump()
        {
            if (lines.Any(l => l.Length > 0))
            {
                finished.Add((Kind(), [.. lines]));
            }
        }

        for (var evI = 0; evI < events.Count; evI++)
        {
            var ev = events[evI];
            if (ev is EvMenu)
            {
                if (lines.Count > 0 && lines[^1].Length == 0)
                {
                    lines.RemoveAt(lines.Count - 1);
                }

                Dump();
                lines = [""];
                inMenu = true;
                menuHasText = false;
                continue;
            }

            if (ev is EvClear)
            {
                Dump();
                lines = [""];
                continue;
            }

            if (ev is EvSwap)
            {
                Dump();
                overlay = false;
                active = active == "top" ? "bottom" : "top";
                lines = [""];
                continue;
            }

            if (ev is EvOverlay)
            {
                if (active == "top")
                {
                    continue;
                }

                Dump();
                lines = [""];
                overlay = true;
                continue;
            }

            if (ev is EvOverlayEnd)
            {
                if (!overlay)
                {
                    continue;
                }

                Dump();
                overlay = false;
                lines = [""];
                continue;
            }

            if (ev is EvText te)
            {
                lines[^1] += te.Text;
                if (te.Text.Length > 0)
                {
                    menuHasText = true;
                }

                continue;
            }

            if (ev is EvNewline)
            {
                var nxt = evI + 1 < events.Count ? events[evI + 1] : null;
                if (nxt is EvMenu)
                {
                    continue;
                }

                if (inMenu && !menuHasText && lines[^1].Length == 0)
                {
                    continue;
                }

                lines.Add("");
            }
        }

        Dump();
        var violations = new List<string>();
        foreach (var box in finished)
        {
            if (box.kind != "menu" && box.lines.Count > BoxMaxLines)
            {
                violations.Add($"{box.kind} textbox has {box.lines.Count} lines (max {BoxMaxLines})");
            }

            for (var i = 0; i < box.lines.Count; i++)
            {
                if (box.lines[i].Length > BoxLineMaxChars)
                {
                    violations.Add(
                        $"{box.kind} textbox line {i + 1} is {box.lines[i].Length} characters (max {BoxLineMaxChars})");
                }
            }
        }

        return (finished.Select(b => b.kind).ToList(), violations);
    }

    private static int OverflowScore(IReadOnlyList<string> violations)
    {
        var score = 0;
        foreach (var v in violations)
        {
            if (v.Contains("lines", StringComparison.Ordinal))
            {
                score += 10000;
            }
            else
            {
                score += 1;
            }
        }

        return score;
    }

    private sealed record EvText(string Text);
    private sealed record EvNewline;
    private sealed record EvPortrait(int Expr, int? B1, int? Pre);
    private sealed record EvSlotOff(int? B1, int? Pre);
    private sealed record EvKeep(int? B1, int? Pre);
    private sealed record EvDelay(int Frames);
    private sealed record EvClear;
    private sealed record EvWait;
    private sealed record EvOverlay;
    private sealed record EvOverlayEnd;
    private sealed record EvBox;
    private sealed record EvVoice(int Arg);
    private sealed record EvSwap;
    private sealed record EvMenu;
    private sealed record EvFaceKey(int Key);
    private sealed record EvRaw(byte[] Data);
    private sealed record EvLine;
    private sealed record EvCtrl09(int Sub, int? Arg);
    private sealed record EvEnd(byte[] Pad);
    private sealed record EvHdr(int Pre, int B1, int B2, int B3, int Expr);
}
