using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class DialogueMarkupGlyphTests
{
    [Fact]
    public void Music_note_d9_is_not_raw()
    {
        var tokens = new DialogueToken[]
        {
            new TextToken("The time for youth has come."),
            new RawByteToken(0xD9),
            new TerminatorToken(),
        };
        var text = DialogueMarkup.FromTokens(tokens);
        Assert.Contains("♪", text);
        Assert.DoesNotContain("[raw:d9]", text);
        Assert.Equal(DialogueTokens.Emit(tokens),
            DialogueTokens.Emit(DialogueMarkup.Apply(tokens, text)));
    }

    [Fact]
    public void Gaia_kana_and_heart_round_trip()
    {
        var text = "ァェッ♥";
        var tokens = DialogueMarkup.Apply([], text);
        var body = DialogueTokens.Emit(tokens.Where(t => t is not TerminatorToken).ToList());
        Assert.Equal(new byte[] { 0x80, 0x83, 0x88, 0xD7 }, body);
        Assert.StartsWith("ァェッ♥", DialogueMarkup.FromTokens(tokens));
    }

    [Fact]
    public void Legacy_raw_d9_still_applies()
    {
        var tokens = DialogueMarkup.Apply([], "come.[raw:d9]");
        Assert.Contains("♪", DialogueMarkup.FromTokens(tokens));
    }
}
