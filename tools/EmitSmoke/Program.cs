using Grandia.Runtime;
using Grandia.Sdk;
using HubBgm;

var hooks = LoadHub();
var map = new Map("CC15");
var ev = new MapLoadEvent(new MapId(0xE010), new MapId(0xCC15), 1, map);
hooks.Invoke("OnMapLoad", ev);

var flag = new EventFlagEvent(0x0A11, 0x53C45, EventFlagKind.Loot, 1, 0);
hooks.Invoke("OnEventFlag", flag);
if (flag.SuppressLootUi || flag.SuppressGold)
{
    Console.Error.WriteLine("HubBgmMod must not suppress chest loot");
    return 1;
}

_ = Game.Stash.Get(346);
_ = Game.Gold.Get();
_ = Game.Flags.IsSet(0x0A33);
var travel = new WorldMapConfirmEvent(new MapId(0xCC15));
hooks.Invoke("OnWorldMapConfirm", travel);
var wmLoad = new WorldMapLoadEvent(0, 0, 0,
    [new WorldMapDestination(new MapId(0x2000), 18, -124, -68, true, slot: 0)]);
hooks.Invoke("OnWorldMapLoad", wmLoad);
if (wmLoad.Destinations.Count < 2 || wmLoad.Destinations[0].Map.Value != 0x2000 ||
    !wmLoad.Destinations.Exists(d => d.Map.Value == 0xCC15))
{
    Console.Error.WriteLine("HubBgmMod OnWorldMapLoad should keep stock dests and Add CC15");
    return 1;
}
if (!travel.Allow)
{
    Console.Error.WriteLine("HubBgmMod must not deny world-map travel");
    return 1;
}

var save = new SaveEvent(1, []);
hooks.Invoke("OnSave", save);
if (save.Trailer.Length > 0)
{
    Console.Error.WriteLine("HubBgmMod must not write a save trailer");
    return 1;
}

var loadPeek = new LoadEvent(1, LoadPhase.ConfirmPeek, []);
hooks.Invoke("OnLoad", loadPeek);
if (!loadPeek.Allow)
{
    Console.Error.WriteLine("HubBgmMod must not deny slot load");
    return 1;
}

hooks.Invoke("OnLoad", new LoadEvent(1, LoadPhase.Applied, []));
hooks.Invoke("OnEnemyLoaded", new EnemyLoadedEvent(1, 1, (int)Species.GaiaDemon, 10, 80, 80, 12, 10, 8, 9, 20,
    15));
var shop = new ShopOpenEvent(new MapId(0x204C), ShopKind.Buy, [Item.CeramicSword],
    [Item.SportsWear], [Item.Herbs]);
hooks.Invoke("OnShopOpen", shop);
if (!shop.Weapons.Contains(Item.PoisonAntidote))
{
    Console.Error.WriteLine("HubBgmMod OnShopOpen should add PoisonAntidote");
    return 1;
}

if (shop.GetPrice(Item.PoisonAntidote) != 1)
{
    Console.Error.WriteLine("HubBgmMod OnShopOpen should price PoisonAntidote at 1");
    return 1;
}

var sell = new ShopOpenEvent(new MapId(0x204C), ShopKind.Sell);
hooks.Invoke("OnShopOpen", sell);
if (sell.GetSellPrice(Item.PoisonAntidote) != 990)
{
    Console.Error.WriteLine("HubBgmMod OnShopOpen Sell should price PoisonAntidote at 990");
    return 1;
}

_ = Game.Party.Get(0);
Game.Party.ClearOverride();
var battle = new BattleLoadEvent([1, 3, 0, 0]);
hooks.Invoke("OnBattleLoad", battle);
if (battle.Party[0] != 1 || battle.Party[1] != 0 || battle.Party[2] != 0 || battle.Party[3] != 0)
{
    Console.Error.WriteLine("HubBgmMod OnBattleLoad should set Justin only");
    return 1;
}

var menu = new MenuOpenEvent(MenuKind.Status, [1, 3, 0, 0]);
hooks.Invoke("OnMenuOpen", menu);
if (menu.Party[0] != 1 || menu.Party[1] != 3 || menu.Party[2] != 2 || menu.Party[3] != 4)
{
    Console.Error.WriteLine("HubBgmMod OnMenuOpen should set Justin, Sue, Feena, Gadwin");
    return 1;
}

if (!map.Dirty)
{
    Console.WriteLine("ok OnShopOpen + OnEventFlag default (no OnMapLoad emit)");
    return 0;
}

var patch = map.ToPatchText();
Console.WriteLine(patch);
if (!patch.Contains("dest=0xCC15", StringComparison.Ordinal))
{
    Console.Error.WriteLine("patch missing CC15 hook");
    return 1;
}

var repo = FindRepo();
var tools = FindFieldTools(repo);
var field = Path.Combine(
    @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster",
    "content", "FIELD");
var text = Path.Combine(
    @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster",
    "content", "TEXT", "EN");
if (!Directory.Exists(field) || tools is null)
{
    Console.WriteLine("skip field_patch (FIELD or field_tools.exe missing)");
    return 0;
}

var cache = Path.Combine(repo, "overlay", "_emit_smoke");
var baseline = Path.Combine(repo, "overlay", "_emit_smoke_base");
if (Directory.Exists(cache))
{
    Directory.Delete(cache, recursive: true);
}

if (Directory.Exists(baseline))
{
    Directory.Delete(baseline, recursive: true);
}

var resetCfg = new ModsConfig
{
    Tools = tools,
    Field = field,
    Text = text,
    Cache = baseline,
};
var resetMap = new Map("CC15");
resetMap.GetScript(0x3000).Clear();
resetMap.GetScript(0x3000).Yield();
PatchEmitter.Emit(resetMap, resetCfg, baseline, Console.WriteLine);
var baselineText = Path.Combine(baseline, "TEXT", "EN");
if (!File.Exists(Path.Combine(baselineText, "CC15.SCN")))
{
    Console.WriteLine("baseline reset was a no-op; compiling against Steam TEXT");
    baselineText = text;
}

var cfg = new ModsConfig
{
    Tools = tools,
    Field = field,
    Text = baselineText,
    Cache = cache,
};

PatchEmitter.Emit(map, cfg, cache, Console.WriteLine);
var mdp = Path.Combine(cache, "FIELD", "CC15.mdp");
var scn = Path.Combine(cache, "TEXT", "EN", "CC15.SCN");
var ofs = Path.Combine(cache, "TEXT", "EN", "CC15.OFS");
if (!File.Exists(mdp) && (!File.Exists(scn) || !File.Exists(ofs)))
{
    Console.Error.WriteLine("field_patch did not write CC15 overlay");
    return 1;
}

Console.WriteLine($"wrote {(File.Exists(mdp) ? mdp : scn)}");
Console.WriteLine("ok OnMapLoad emit + OnEventFlag default");
return 0;

static ModHooks LoadHub()
{
    var hooks = new ModHooks();
    new Plugin().Init(new ModContext("template", hooks));
    return hooks;
}

static string FindRepo()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var i = 0; i < 8 && dir is not null; i++)
    {
        if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt")) &&
            Directory.Exists(Path.Combine(dir.FullName, "sdk")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}

static string? FindFieldTools(string repo)
{
    string[] candidates =
    [
        Path.Combine(repo, "vendor", "field_tools", "field_tools.exe"),
        Path.Combine(AppContext.BaseDirectory, "field_tools", "field_tools.exe"),
        Path.Combine(AppContext.BaseDirectory, "field_tools.exe"),
    ];
    foreach (var c in candidates)
    {
        if (File.Exists(c))
        {
            return Path.GetFullPath(c);
        }
    }

    return null;
}
