using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class FieldSfxTests
{
    [Fact]
    public void List_isEmptyWhenHostUnbound()
    {
        Assert.Null(Game.Native);
        Assert.Equal(0, Game.FieldSfx.Count);
        Assert.Equal(0, Game.FieldSfx.Range);
        Assert.Empty(Game.FieldSfx.List());
        Assert.Null(Game.FieldSfx[0]);
        Assert.False(Game.FieldSfx.Mute(0));
        Assert.False(Game.FieldSfx.Move(0, 1, 2, 3));
        Assert.Equal(0, Game.FieldSfx.MuteAll());
        Assert.Equal(0, Game.FieldSfx.MuteSfx(16));
        Assert.Null(Game.FieldSfx.Add(16, 0, 0, 0));
        Assert.False(Game.FieldSfx.Remove(0));
        Assert.False(Game.FieldSfx.SetFlags(0, 0x60));
    }
}
