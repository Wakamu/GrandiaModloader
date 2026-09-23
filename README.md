# Grandia Modloader

Standalone **.NET mod SDK** for Grandia HD Remaster.

Mods are C# class libraries that reference `Grandia.Sdk.dll`. A `[Mod]` entry class has an `[Init]` method that registers hook / service classes. Hook methods can have any name; the host finds them by attributes (`[OnTick]`, `[OnBattleLoad]`, …). Launch injects `GrandiaMod.dll`, which hosts `Grandia.Runtime` (x86 .NET 8). When the game `fopen`s a `.MDP` / `.SCN` / `.OFS`, the runtime builds a `Map` and runs every enabled mod. Scripts, hooks, and zones assemble in-process (C# codecs — no `field_tools` at play time). Custom scripts and hooks are not written over the live SCN/sec[7] copies. The VM lookup at `+0x6F0B0` (`OnScriptExecute`) and `call_hook` at `+0x53560` (`OnCallHook`) redirect those ids to the assembled buffers. Dest-arrival still opens stock files.

## Build

Needs Visual Studio 2022 (Desktop C++ + .NET 8 x86), CMake, Win32, and Python 3 with PyInstaller (build-time only).

```powershell
cd C:\Users\User\Projects\GrandiaFieldPatch

python tools\pack_field_tools.py
python tools\build_field_tools.py

cmake -S . -B build -A Win32
cmake --build build --config Release

dotnet build modloader\GrandiaModloader.csproj -c Release
```

`GrandiaMod.dll`, `Grandia.Runtime.dll`, `Grandia.Sdk.dll`, and `field_tools\` are copied next to `modloader\bin\Release\net8.0-windows\GrandiaModloader.exe`.

The Visual Studio solution is `GrandiaFieldPatch.sln`. The WinForms app and installer live under `modloader\` (solution folder **modloader**). Right-click **Setup** → Build to produce `GrandiaModloaderSetup.msi` (needs the WiX toolset NuGet restore; .NET 8 Desktop x86 on the target PC).

Ship that folder as the install:

```
GrandiaModloader.exe
GrandiaMod.dll
field_tools\field_tools.exe
mods\
```

```
cmake --build build --config Release
dotnet build runtime\Grandia.Runtime.csproj -c Release
dotnet build sdk\Grandia.Sdk.csproj -c Release
dotnet build modloader\Setup\Setup.wixproj -c Release
```

That last command writes both the MSI and `dist\GrandiaModloader-win-x86.zip`. First-time install is still the MSI. In-app updates download the zip from the GitHub release (same file name), replace Program Files, and restart — `%AppData%\GrandiaModloader` mods and `config.json` stay put. Attach the zip to the release when you publish; bump `AppVersion.Current` so clients see it.

Each Setup build stamps a new MSI version (`1.{yy}.{day}{hour}` UTC). Re-running an older `1.0.0` MSI only Repairs and leaves Program Files unchanged. After install, launch the modloader once so `%AppData%\GrandiaModloader\runtime` picks up the new DLLs.

## Write a mod

The installer ships a **Grandia Mod** project template (Visual Studio, Rider, and `dotnet new`). After installing the modloader:

```powershell
& "C:\Program Files (x86)\Grandia Modloader\templates\install-template.cmd"
```

Then **File → New → Project / Solution**, search **Grandia Mod**. From the CLI: `dotnet new grandiamod -n MyMod -o MyMod`.

The generated project is `net8.0` / **x86**, references `Grandia.Sdk.dll` in the modloader install folder, and includes a `[Mod]` entry class. A Release/Debug build copies the DLL into `%AppData%\GrandiaModloader\mods\`.

To author a project by hand:

1. New C# class library: `net8.0`, `PlatformTarget` **x86**.
2. Add a reference to `Grandia.Sdk.dll` (from the install folder, `dist\`, or `sdk\bin\Release\net8.0\`). Do not add the SDK project to your mod solution.
3. One public `[Mod]` class with an `[Init]` method. Register hook classes and any services from there. Name, version, and description on `[Mod]` are what the modloader list shows.
4. Build and copy **only the DLL** into `%AppData%\GrandiaModloader\mods\` (or use **Add mod**). Maps, scripts, and PNGs are embedded in that DLL.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PlatformTarget>x86</PlatformTarget>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <Import Project="..\..\sdk\Grandia.Mod.targets" />
  <ItemGroup>
    <Reference Include="Grandia.Sdk">
      <HintPath>..\..\dist\Grandia.Sdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

```csharp
using Grandia.Sdk;

[Mod("My mod", "1.0.0", Description = "What this mod does.")]
public sealed class Plugin
{
    [Init]
    public void Init(ModContext ctx)
    {
        ctx.Register<HubHooks>();
    }
}

sealed class HubHooks
{
    [OnMapLoad]
    public void PatchHubEnter(MapLoadEvent e)
    {
        if (e.To.Value != 0xCC15) return;
        var enter = e.Map.GetScript(0x3000);
        enter.Clear();
        enter.CameraOverlay(0x01);
        enter.Wait(1);
        enter.CameraDeactivateOverlay(0x01);
        enter.Yield();
    }

    [OnScriptExecute]
    public void ReplaceArmedScript(ScriptExecuteEvent e)
    {
        if (e.Map.Value != 0xE010 || e.ScriptId != 0x3000) return;
        e.Replace("""
            camera overlay 0x01
            wait 1
            camera deactivate_overlay 0x01
            yield
            """);
    }

    [OnDialogue]
    public void Talk(DialogueEvent e)
    {
        e.Markup = e.Markup.Replace("Justin", "Hero");
    }
}
```

`[Init]` is `void Name()` or `void Name(ModContext ctx)`. Hook methods are `void Name(TheEvent e)` — the attribute picks the event, not the method name. `Game.Log.Info("...")` / `Game.Log.Warn("...")` append to `GrandiaMod.log`; `ctx.Log("...")` does the same from Init.

Hook attributes: `[OnMapLoad]`, `[OnScriptExecute]`, `[OnCallHook]`, `[OnEventFlag]`, `[OnItemAssignUi]`, `[OnFieldGoldAdd]`, `[OnWorldMapLoad]`, `[OnWorldMapConfirm]`, `[OnMapTravel]`, `[OnSave]`, `[OnLoad]`, `[OnBattleSetup]`, `[OnBattleLoad]`, `[OnVictory]`, `[OnMenuOpen]`, `[OnEnemyLoaded]`, `[OnShopOpen]`, `[OnTick]`, `[OnTitleScreen]`, `[OnCharacter]`, `[OnItem]`, `[OnMagic]`, `[OnDialogue]`, `[OnHdTexture]`, `[OnHdSpriteMatch]`, `[OnHdSpriteDraw]`.

`Game.Status` / `Game.GetGameStatus()` is `Field`, `Menu`, `WorldMap`, or `Battle`. `Game.WarpTo(map, spawn)` queues a vanilla `+0x614D0` MapTravel (same path as a door: `aux9=1` `auxA=30`; fires `[OnMapTravel]`). On the world map it commits through the confirm FSM so AMAP closes. `[OnVictory]` fires once when the last field enemy pays out — `Exp` / `Gold` / `Drops` are the result pots (writable), plus the field `Map` and wanderer `EncounterRow`. `e.Map.Sfx` on `[OnMapLoad]` is the authored sec[29] beds (same lazy hydrate as `e.Map.Zones`) — set `Sfx` / `X`/`Y`/`Z` / `Flags`, or `e.Map.AddSfx` / `Remove`, and the host recopies the table onto the heap before mixer bind (max 62 live). `e.Map.Npcs` is sec[8] town talkers (kind 0 stands, kind 4 random-walks, talk ≠ 0) — set `X`/`Y`/`Z` / `TalkId` / `TalkBox`, or `e.Map.AddNpc` / `Remove`; the host recopies the 4 KiB instance heap after the field-setup word-copy (max 85 live). Kind 4 also needs `WalkMode` 1–4 (high byte of +0x10; stock often 2) and a real `TalkBox` wander rectangle — `Kind = 4` alone does not walk. `npc.Walk(box)` sets that trio. Kind-2 wanderers stay on `e.Map.Encounters`. Add clones an existing talker's CLUT / flags; it does not graft a new hdr+4 body. `e.Map.Anims` is this map's sec[21] clip directory (`Id` + raw `StreamA`/`StreamB`, plus `Header` / `Turn` / `Frames` / `Cues`) — not NPC data. Stream A is a u16 header (low byte 0 skips facing) then `Flags` XYZ samples. Stream B cues are `cmd>>14` 0/1/2 = `call_hook` table 1/2/3, or 3 = delay. `SetFrames` / `SetCues` / `e.Map.AddAnim` emit and swap `[0x71CAE0]` after bind. Play a listed clip with `e.Map.AddHook($"anim {clip.Id} talk={npc.TalkId} mode=2")`. Ids missing from `Anims` are the shared bank. `e.Map.SpriteClips` / `e.Map.Poses` are this map's sec[23] character sprite bank (read-only): clip `Id` + timed pose frames, then 16-byte parts (channel + sprite cookie). `unit_bind {clipId} {talkId}` (field_talk) looks up `SpriteClips.OfId(clipId)`. Maps with no sec[23] hydrate empty. `Game.FieldSfx.List()` / `Mute` / `Move` / `Add` / `Remove` poke the live heap copy after field setup. `Game.Camera.TryGetPosition` is the field camera look-at in the same walk units as `Game.Party.TryGetPosition` (SFX volume uses this point). `Game.Overlay.Prompt(title, text => …)` opens a D3D text field that swallows keyboard and pad; the callback gets the string on Enter (optional `onCancel` on Escape). Or poll `TakeInput()` / `TakeInputCancel()`.

`[OnHdTexture]` fires when SoftHD opens `{stem}_{kind}__atlas.png`, `{stem}_{kind}__atlas_tables.png` (maps CLUT sheet), or `{stem}_{kind}__spriteinfo.bin` (fopen / `SDL_RWFromFile`). `Kind` is the filename token (`maps` / `tenants` / `anim` / `mapeff` / `faces` / `party` / `areamap` / `logo` / `title` / …) — not an MDP section. `File` is `Atlas`, `AtlasTables`, or `SpriteInfo`. `Bytes` is the stock file; `Rects` parses SPRIV; `Pixels` is an `HdPixels` (`Width` / `Height` / `Rgba`) decoded only if read. `Replace(pngOrSpriv)` / `ReplacePixels(w,h,rgba)` virt-file the replacement so SoftHD decodes it. `[OnHdSpriteMatch]` fires once per SPRIV row when SoftHD inserts it into the live table (`+0x29164` 32-byte v3/v4, `+0x29301` 8-byte v1/v2). `Index` is that row; `Rect` is atlas xywh when the record starts with it; `Record` is the raw insert (tenants / maps v4 keep the PS1 match key here). Observe-only — not `Map.Poses[].SpriteIndex`. `[OnHdSpriteDraw]` fires when SoftHD matches a live PS1 blit to that row (`+0x2EFA0`, 32-byte tables only): `Index` / `Rect` are the atlas sample, `Live` is the VRAM xywh key. Per blit — subscribe only if you need it. `[OnDialogue]` fires when a type-1 / type-8 textbox is about to run (`Map`, `ScriptId`, `OpIndex`, `Markup`). `Markup` decodes on first read and encodes only if you assign it (in-process C#, no `field_tools`). `e.Skip = true` drops that opcode and continues the script. `[OnCharacter]` fires for ids 1–8 after a slot load copies into MapObj, and once on new game — mutate stats and `e.Learn(Skill.Burn)`. `[OnItem]` / `[OnMagic]` run on every status, shop, and stash WINDT load (the game recopies vanilla tables each time). Item `Cost` is buy gold; `SellPrice` defaults to Cost/2. Item `Effect` is the skill id used when the item is used (Herbs → `Skill.Heal`); `EffectValue` is the magnitude (Herbs heal 15). `Name` / `ShortName` / `Description` patch `TEXT/EN/TEXT1.BIN` in place on fopen (same size; unused slots too — `Item.LifeJewel`, `Item.CampingTent`). A string that does not fit its section is skipped. Item `WeaponKind` is record+8 (same `WeaponType` ids as character slots). `Stats` / `Auto` / `AttackRange` are the typed para lines and Auto Effect (Strength 7, Cause Paralysis 33); raw `Para*` / `Unknown8` stay as aliases. `Unknown11` / `Unknown12` / `Unknown27` stay unlabeled. `Game.SaveData` is a shared key → JSON bag any mod can `Set` / `Remove` at any time (`"YourMod.progress"`). The host writes it on every slot save and replaces it on every slot load (also with no mods loaded); title and a vanilla slot clear it. Do not share keys across mods. `[OnMagic]` is WINDT sec7/sec8 (learn requirements, who can learn, power, MP/SP `Cost`, IP) plus STAT/BBG copies in battle. Magic `Cost` is the MP or SP number in menus; `IpCost` is the IP gauge. Magic / enemy-skill `Effect` is the combat class (`EffectType.Heal`, `Damage`, `Status`, …) and `Mode` is the subtype (`HealMode`, `DamageKind`, `StatusAilment`, `StatMod`, `ClearAilment`). `IpKnockback` is combat row +6 (Shockwave 3000, Lotus Cut 8500). `CriticalChance` is +17. Enemy damage-plus-ailment is `AddAilment` + `Chance` (header +0xF / +0x10), not a second EffectType.

### Assets (PNG, …)

Put files under `assets/` (imported `Grandia.Mod.targets` embeds them). World-map plates take the resource file name:

```csharp
e.Add(new MapId(0xCC15), picturePath: "hub.png", pictureWidth: 30, pictureHeight: 15);
```

The host extracts the embedded PNG to a temp file for the HD overlay.

### Custom maps (no game-folder copy)

Put compiled `STEM.mdp` / `STEM.scn` / `STEM.ofs` under `maps/` and embed them in the DLL. Register in `[Init]`. The host serves those bytes at fopen.

```xml
<ItemGroup>
  <EmbeddedResource Include="maps\**\*" />
</ItemGroup>
```

```csharp
[Init]
public void Init(ModContext ctx)
{
    ctx.Maps.AddFromEmbedded(typeof(Plugin).Assembly, "CC15");
    // or: ctx.Maps.Add("CC15", mdp, scn, ofs);
}
```

Assemble MDP/SCN/OFS when you **build the mod**, then embed the bins. Do not rewrite dest-cam / sec[32] from `OnMapLoad`. Later mods that `Add` the same stem replace earlier ones.

### Custom scripts (assemble at build)

Author `scripts/hub_save.asm` (field assembler text). Import `sdk/Grandia.Mod.targets` so `dotnet build` runs `field_tools embed` and embeds `hub_save.bin`. Register the catalog in `[Init]`. Nothing is replaced until you call `e.Use("hub_save")` from `[OnScriptExecute]` — the same blob can cover several script ids.

```xml
<Import Project="$(GrandiaFieldPatch)\sdk\Grandia.Mod.targets" />
```

```
MyMod/scripts/hub_save.asm
```

```csharp
[Init]
public void Init(ModContext ctx)
{
    ctx.Scripts.AddFromEmbedded(typeof(Plugin).Assembly);
    // or: ctx.Scripts.AddFromEmbedded(asm, "hub_save");
}

[OnScriptExecute]
public void UseCompiled(ScriptExecuteEvent e)
{
    if (e.Map.Value == 0xE010 && (e.ScriptId == 0xE000 || e.ScriptId == 0x3001))
        e.Use("hub_save");
}
```

`e.Replace(...)` assembles in-process (no `field_tools`) for that arm only — the next lookup is vanilla unless you write again (or `OnMapLoad` / `GetScript` patched it). `e.Script.Lines` decodes that id on first read. Last `Add` for the same name wins. A pre-built `scripts/hub_save.bin` embeds as-is.

Assemblies with no `[Mod]` still load exported `IMod` types.

The example mod is a separate project: `C:\Users\User\Projects\HubBgmMod` (open `HubBgmMod.sln`, reference `Grandia.Sdk.dll`). `dotnet build` copies `HubBgmMod.dll` into `GrandiaFieldPatch\mods\`. Script ops are the existing field assembler mnemonics — not a new language. Do not rewrite dest-cam / sec[32] from `OnMapLoad`.

## Use

1. Build your mod (`dotnet build -c Release -p:PlatformTarget=x86`) and drop the `.dll` in `mods\`, or **Add mod** and pick it. The list reads name / version / description from `[Mod]` on the DLL.
2. Check the ones you want. List order is hook order.
3. Settings: Grandia install folder, Steam or `grandia.exe`.
4. **Launch Grandia**. Writes `mods.json` next to GrandiaMod.dll (path to `field_tools.exe`), starts the game, injects. Does not compile mods.

If the game is already running, Launch still writes `mods.json` and injects. Quit and Launch again to load a rebuilt `GrandiaMod.dll`. Re-enter a map so fopen assembles; talking to an NPC / `call_hook` is when the redirect runs.

Log next to `grandia.exe`: `GrandiaMod.log` (truncated on each inject). Mods write with `Game.Log.Info("...")` / `Game.Log.Warn("...")`, or `ctx.Log("...")` from `[Init]`. Start skips MP4 cinematics (the vanilla skip path, ungated).

Enable flags and order are in `%AppData%\GrandiaModloader\config.json` (or `config.json` beside the exe).
