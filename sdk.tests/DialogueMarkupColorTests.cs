using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class DialogueMarkupColorTests
{
    [Fact]
    public void Signpost_yellow_round_trips()
    {
        var tokens = new DialogueToken[]
        {
            new TextToken("To "),
            new RawByteToken(0x0A),
            new RawByteToken(0x00),
            new RawByteToken(0xC0),
            new TextToken("Mysterious Vanishing Shrine"),
            new RawByteToken(0x0A),
            new RawByteToken(0x00),
            new RawByteToken(0xD0),
            new TerminatorToken(),
        };

        var text = DialogueMarkup.FromTokens(tokens);
        Assert.Contains("[color:yellow]", text);
        Assert.Contains("[color:grey]", text);
        Assert.DoesNotContain("[raw:0a00c0]", text);
        Assert.Contains("Mysterious Vanishing Shrine", text);

        var rebuilt = DialogueMarkup.Apply([], text);
        Assert.Equal(DialogueTokens.Emit(tokens[..^1].Concat([rebuilt[^1]]).ToList()),
            DialogueTokens.Emit(rebuilt));
        Assert.Equal(DialogueTokens.Emit(tokens), DialogueTokens.Emit(DialogueMarkup.Apply(tokens, text)));
    }

    [Fact]
    public void Color_close_restores_white()
    {
        var tokens = DialogueMarkup.Apply([], "[color:blue]Hi[color] how are you!");
        var bytes = TokensBytes(tokens);
        Assert.Equal(new byte[] { 0x0A, 0x00, 0xE0 }, bytes.Take(3));
        Assert.Contains("Hi", DialogueMarkup.FromTokens(tokens));
        Assert.Equal(new byte[] { 0x0A, 0x00, 0xF0 }, bytes.Skip(5).Take(3).ToArray());
    }

    [Fact]
    public void Color_named_palette_and_background()
    {
        Assert.Equal(new byte[] { 0x0A, 0x00, 0x20 }, ColorBody("[color:red]X"));
        Assert.Equal(new byte[] { 0x0A, 0x00, 0x90 }, ColorBody("[color:green]X"));
        Assert.Equal(new byte[] { 0x0A, 0x00, 0xC1 }, ColorBody("[color:yellow:black]X"));
        Assert.Equal(new byte[] { 0x0A, 0x00, 0xC1 }, ColorBody("[color:C1]X"));
        Assert.StartsWith("[color:yellow:black]", DialogueMarkup.FromTokens(
            DialogueMarkup.Apply([], "[color:yellow:black]X")));
    }

    [Fact]
    public void Color_white_and_grey_named()
    {
        var tokens = DialogueMarkup.Apply([], "[color:yellow]Hi[color:white] [color:grey]dim");
        var hex = Convert.ToHexString(TokensBytes(tokens)).ToLowerInvariant();
        Assert.Contains("0a00c0", hex);
        Assert.Contains("0a00f0", hex);
        Assert.Contains("0a00d0", hex);
    }

    [Fact]
    public void Legacy_raw_color_still_applies()
    {
        var tokens = DialogueMarkup.Apply([], "[raw:0a00c0]Gate[raw:0a00d0]");
        Assert.StartsWith("[color:yellow]Gate[color:grey]", DialogueMarkup.FromTokens(tokens));
    }

    private static byte[] TokensBytes(IReadOnlyList<DialogueToken> tokens) =>
        DialogueTokens.Emit(tokens.Where(t => t is not TerminatorToken).ToList());

    private static byte[] ColorBody(string markup) =>
        TokensBytes(DialogueMarkup.Apply([], markup)).Take(3).ToArray();
}
