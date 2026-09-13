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
        // e.Map.Zones / e.Map.Sfx (sec[29] river, frogs) hydrate on first read.
        // e.Map.Sfx[0].Sfx = 16; e.Map.AddSfx(16, x, y, z); e.Map.Sfx.OfSfx(16)[0].Remove();
        // e.Map.Npcs is sec[8] kind 0 (stand) / kind 4 (walk). Not Marna wanderers.
        // e.Map.Npcs[0].Walk(new TalkBox(676, 820, 766, 514)); // kind 4 + WalkMode 2
        // e.Map.AddNpc(21, x, y, z); e.Map.Npcs.OfTalk(21)[0].Remove();
        // e.Map.Anims is sec[21] clips (not on the NPC row). Shared-bank ids are omitted.
        // clip.SetFrames([...], header: 8); clip.SetCues([new MapAnimCue(0, 2, 38)]);
        // var clip = e.Map.Anims.OfId(2); e.Map.AddHook($"anim {clip.Id} talk={npc.TalkId} mode=2");
        // e.Map.SpriteClips / e.Map.Poses are sec[23] (unit_bind x = clip id). Read-only.
        // var spr = e.Map.SpriteClips.OfId(15); var pose = e.Map.Poses[spr.Frames[0].Pose];
        // e.Map.Textures is the original PS1 TIM (sec[1]/[27]). SoftHD catalogs stay on Sprites.
        // e.Map.Textures.TryCrop(pose.Parts[0], out var tim); var px = tim.Rgba();
        // e.Map.TryCrop(e.Map.Sprites.Anim[7], out tim); // UV pack or FNV join via Sprites.Uv
        // Game.RunScript(15); Game.RunHook(888);
        // Game.RunScript("wait 30\ncall_hook 888"); // custom, queued for the next idle field tick
        // MapTextureExtract.Write(e.Map); // %AppData%\GrandiaExtract\{Stem}\Tim\
        // e.Map.Sprites is sec[32] UV cells + SoftHD anim/tenants/maps/mapeff spriteinfo.
        // var cell = e.Map.Sprites.Anim[0]; var uv = e.Map.Sprites.Uv[0];
    }

    [OnHdTexture]
    public void OnHdTexture(HdTextureEvent e)
    {
        // SoftHD atlas / spriteinfo fopen. Kind = maps/tenants/anim/mapeff/faces/party/areamap.
        // if (e.Kind != HdAssetKind.Anim || e.File != HdAssetFile.Atlas) return;
        // if (e.Pixels is not { } px) return;
        // e.ReplacePixels(px.Width, px.Height, px.Rgba);
    }

    [OnHdSpriteMatch]
    public void OnHdSpriteMatch(HdSpriteMatchEvent e)
    {
        // SoftHD inserted SPRIV row i (not Map.Poses SpriteIndex).
        // if (e.Kind != HdAssetKind.Faces) return;
        // Game.Log.Info($"{e.Stem} {e.Kind} [{e.Index}] {e.Rect}");
        // Draw-time: [OnHdSpriteDraw] — live VRAM blit → that same Index/Rect (per sprite; hot).
    }
}
