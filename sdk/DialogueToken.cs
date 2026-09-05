using System.Text;

namespace Grandia.Sdk;

/// <summary>One lossless token from a type-1 / type-8 dialogue payload.</summary>
public abstract class DialogueToken
{
    public abstract void WriteTo(IList<byte> dest);
}

public sealed class HeaderToken : DialogueToken
{
    public HeaderToken(int pre, int b1, int b2, int b3, int expr)
    {
        Pre = pre & 0xFF;
        B1 = b1 & 0xFF;
        B2 = b2 & 0xFF;
        B3 = b3 & 0xFF;
        Expr = expr & 0xFF;
    }

    public int Pre { get; }
    public int B1 { get; }
    public int B2 { get; }
    public int B3 { get; }
    public int Expr { get; }

    public override void WriteTo(IList<byte> dest)
    {
        dest.Add((byte)Pre);
        dest.Add((byte)B1);
        dest.Add((byte)B2);
        dest.Add((byte)B3);
        dest.Add((byte)Expr);
    }
}

public sealed class LineStartToken : DialogueToken
{
    public override void WriteTo(IList<byte> dest)
    {
        dest.Add(0x09);
        dest.Add(0x01);
    }
}

public sealed class TextToken : DialogueToken
{
    public TextToken(string text) => Text = text ?? "";

    public string Text { get; }

    public override void WriteTo(IList<byte> dest)
    {
        foreach (var b in Encoding.ASCII.GetBytes(Text))
        {
            dest.Add(b);
        }
    }
}

public sealed class NewlineToken : DialogueToken
{
    public override void WriteTo(IList<byte> dest) => dest.Add(0x01);
}

public sealed class PageBreakToken : DialogueToken
{
    public override void WriteTo(IList<byte> dest) => dest.Add(0x02);
}

public sealed class HoldToken : DialogueToken
{
    public HoldToken(int hold) => Hold = hold & 0xFF;

    public int Hold { get; }

    public override void WriteTo(IList<byte> dest)
    {
        dest.Add(0x0E);
        dest.Add(0x00);
        dest.Add((byte)Hold);
    }
}

public sealed class FaceKeyToken : DialogueToken
{
    public FaceKeyToken(int faceKey) => FaceKey = faceKey & 0xFFFF;

    public int FaceKey { get; }

    public override void WriteTo(IList<byte> dest)
    {
        dest.Add(0x09);
        dest.Add(0x0F);
        dest.Add((byte)FaceKey);
        dest.Add((byte)(FaceKey >> 8));
    }
}

public sealed class ControlToken : DialogueToken
{
    public ControlToken(int sub, int? arg = null)
    {
        Sub = sub & 0xFF;
        Arg = arg;
    }

    public int Sub { get; }
    public int? Arg { get; }

    public override void WriteTo(IList<byte> dest)
    {
        dest.Add(0x09);
        dest.Add((byte)Sub);
        if (Arg is int arg)
        {
            dest.Add((byte)arg);
            dest.Add((byte)(arg >> 8));
        }
    }
}

public sealed class TerminatorToken : DialogueToken
{
    public TerminatorToken(byte[]? trailing = null) => Trailing = trailing ?? [];

    public byte[] Trailing { get; }

    public override void WriteTo(IList<byte> dest)
    {
        dest.Add(0x07);
        foreach (var b in Trailing)
        {
            dest.Add(b);
        }
    }
}

public sealed class RawByteToken : DialogueToken
{
    public RawByteToken(int value) => Value = value & 0xFF;

    public int Value { get; }

    public override void WriteTo(IList<byte> dest) => dest.Add((byte)Value);
}

/// <summary>Byte-identical tokenise / emit for type-1 and type-8 payloads.</summary>
public static class DialogueTokens
{
    private static readonly (int, int)[] HeaderPairs = [(0x0A, 0x0C), (0x1F, 0x01)];

    public static List<DialogueToken> Tokenize(ReadOnlySpan<byte> payload)
    {
        var tokens = new List<DialogueToken>();
        var n = payload.Length;
        var pos = 0;
        while (pos < n)
        {
            var b = payload[pos];
            if ((b is 0x0F or 0x0B or 0x21) && pos + 5 <= n)
            {
                var b2 = payload[pos + 2];
                var b3 = payload[pos + 3];
                if (IsHeaderPair(b2, b3))
                {
                    tokens.Add(new HeaderToken(b, payload[pos + 1], b2, b3, payload[pos + 4]));
                    pos += 5;
                    continue;
                }
            }

            if (b == 0x09 && pos + 1 < n)
            {
                var sub = payload[pos + 1];
                if (tokens.Count > 0 && tokens[^1] is HeaderToken && (sub == 0x09 || sub >= 0x20))
                {
                    tokens.Add(new RawByteToken(0x09));
                    pos += 1;
                    continue;
                }

                if (sub == 0x01)
                {
                    tokens.Add(new LineStartToken());
                    pos += 2;
                    continue;
                }

                if (sub == 0x0F && pos + 4 <= n)
                {
                    tokens.Add(new FaceKeyToken(payload[pos + 2] | (payload[pos + 3] << 8)));
                    pos += 4;
                    continue;
                }

                if (sub is >= 0x0A and <= 0x0E && pos + 4 <= n)
                {
                    tokens.Add(new ControlToken(sub, payload[pos + 2] | (payload[pos + 3] << 8)));
                    pos += 4;
                    continue;
                }

                tokens.Add(new ControlToken(sub));
                pos += 2;
                continue;
            }

            if (b == 0x0E && pos + 2 < n && payload[pos + 1] == 0x00)
            {
                tokens.Add(new HoldToken(payload[pos + 2]));
                pos += 3;
                continue;
            }

            if (b == 0x07)
            {
                if (tokens.Count > 0 && tokens[^1] is HeaderToken)
                {
                    tokens.Add(new RawByteToken(0x07));
                    pos += 1;
                    continue;
                }

                byte[] trailing = [];
                if (pos + 1 < n && payload[pos + 1] == 0x00)
                {
                    trailing = [0x00];
                    pos += 2;
                }
                else
                {
                    pos += 1;
                }

                tokens.Add(new TerminatorToken(trailing));
                while (pos < n)
                {
                    tokens.Add(new RawByteToken(payload[pos]));
                    pos += 1;
                }

                break;
            }

            if (b is >= 0x20 and <= 0x7E)
            {
                var last = tokens.Count > 0 ? tokens[^1] : null;
                var nxtIsHeader = pos + 6 <= n &&
                    payload[pos + 1] is 0x0F or 0x0B or 0x21 &&
                    IsHeaderPair(payload[pos + 3], payload[pos + 4]);
                if (last is HeaderToken && pos + 1 < n && (payload[pos + 1] == 0x09 || nxtIsHeader))
                {
                    tokens.Add(new RawByteToken(b));
                    pos += 1;
                    continue;
                }

                var start = pos;
                while (pos < n && payload[pos] is >= 0x20 and <= 0x7E)
                {
                    pos += 1;
                }

                tokens.Add(new TextToken(Encoding.ASCII.GetString(payload[start..pos])));
                continue;
            }

            if (b == 0x01)
            {
                tokens.Add(new NewlineToken());
                pos += 1;
                continue;
            }

            if (b == 0x02)
            {
                tokens.Add(new PageBreakToken());
                pos += 1;
                continue;
            }

            tokens.Add(new RawByteToken(b));
            pos += 1;
        }

        return tokens;
    }

    public static byte[] Emit(IReadOnlyList<DialogueToken> tokens)
    {
        var dest = new List<byte>(64);
        foreach (var tok in tokens)
        {
            tok.WriteTo(dest);
        }

        return dest.ToArray();
    }

    private static bool IsHeaderPair(int b2, int b3)
    {
        foreach (var (a, b) in HeaderPairs)
        {
            if (b2 == a && b3 == b)
            {
                return true;
            }
        }

        return false;
    }
}
