using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class GameCameraTests
{
    [Fact]
    public void TryGetPosition_isFalseWhenHostUnbound()
    {
        Assert.Null(Game.Native);
        Assert.False(Game.Camera.TryGetPosition(out var pos));
        Assert.Equal(default, pos);
        Assert.Null(Game.Camera.GetPosition());
    }
}
