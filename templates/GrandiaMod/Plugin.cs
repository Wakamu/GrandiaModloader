using Grandia.Sdk;

[Mod("GrandiaMod1", "1.0.0", Description = "A Grandia HD remaster mod.")]
public sealed class Plugin
{
    [Init]
    public void Init(ModContext ctx)
    {
        ctx.Register<Hooks>();
    }
}

sealed class Hooks
{
    [OnMapLoad]
    public void OnMapLoad(MapLoadEvent e)
    {
        // Mutate e.Map for this fopen. Skip maps you do not care about:
        // if (e.To.Value != 0xCC15) return;
    }
}
