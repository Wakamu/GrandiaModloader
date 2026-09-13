using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class GameRunFieldTests
{
    [Fact]
    public void RunScript_without_host_returns_false()
    {
        Assert.False(Game.RunScript(15));
        Assert.False(Game.RunScript("wait 1\nyield"));
        Assert.False(Game.RunHook(888));
        Assert.False(Game.Quit());
    }

    [Fact]
    public void AssembleScript_wraps_header_and_appends_yield()
    {
        var bytes = FieldScriptAsm.Assemble("script 0xFFFE\nwait 1\nyield", 0xFFFE);
        Assert.True(bytes.Length >= 4);
        Assert.Equal(0xF0, bytes[^1] & 0xF0);

        var hook = FieldHookAsm.AssembleHook("hook 888 setup dest=0xCC15 spawn=1");
        Assert.Equal(FieldHookAsm.HookRowSize, hook.Length);
        Assert.False(Game.RunHook(hook));
        Assert.False(Game.RunHook(new byte[3]));
    }
}
