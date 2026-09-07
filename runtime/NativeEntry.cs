using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Grandia.Sdk;

namespace Grandia.Runtime;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MapOpenNative
{
    public fixed byte Stem[16];
    public ushort From;
    public ushort To;
    public int Spawn;
    public fixed byte CacheDir[260];
    public int Dirty;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ScriptLookupNative
{
    public fixed byte Stem[16];
    public ushort ScriptId;
    public ushort Pad;
    public uint Ip;
    public int Redirect;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct CallHookNative
{
    public fixed byte Stem[16];
    public int Table;
    public ushort HookId;
    public ushort Pad;
    public uint Row;
    public int Redirect;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MapPatchInfoNative
{
    public fixed byte Stem[16];
    public uint Sec7;
    public int Sec7Len;
    public uint Scn;
    public int ScnLen;
    public uint Ofs;
    public int OfsLen;
    public int StockScnLen;
    public int StockOfsLen;
    public int Dirty;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct EventFlagNative
{
    public uint EventId;
    public uint FlagOffset;
    public uint FlagValue;
    public uint Mask;
    public uint EcxIndex;
    public uint CallerRva;
    public int Kind;
    public int SuppressLootUi;
    public int SuppressGold;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ItemAssignNative
{
    public uint EventId;
    public uint ReturnRva;
    public int SkipVanilla;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FieldGoldNative
{
    public uint EventId;
    public int Amount;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WorldMapConfirmNative
{
    public ushort MapId;
    public ushort Pad;
    public int Allow;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MapTravelNative
{
    public ushort From;
    public ushort Dest;
    public int Spawn;
    public int Allow;
    public int Kind;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TickNative
{
    public ushort Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public int Block;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct WorldMapLoadNative
{
    public int SetId;
    public int AmapIndex;
    public int OriginCtx;
    public int Count;
    public int Dirty;
    public fixed int Slot[32];
    public fixed ushort MapId[32];
    public fixed ushort Aux[32];
    public fixed short X[32];
    public fixed short Y[32];
    public fixed int Revealed[32];
    public fixed int Accessible[32];
    public fixed int Picture[32];
    public fixed ushort Extra[128];
    public fixed byte ExtraN[32];
    public fixed byte PicturePath[8320];
    public fixed short PictureW[32];
    public fixed short PictureH[32];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SaveEventNative
{
    public int Slot;
    public int TrailerLen;
    public fixed byte Trailer[1024];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct LoadEventNative
{
    public int Slot;
    public int Phase;
    public int Allow;
    public int TrailerLen;
    public fixed byte Trailer[1024];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct BattleLoadNative
{
    public fixed byte Party[4];
    public byte Formation;
    public byte BattleMode;
    public ushort Map;
    public ushort Dest;
    public int Spawn;
    public uint EncObj;
    public uint EncRow;
    public ushort PackW0;
    public ushort PackW2;
    public ushort PackW4;
    public ushort PackW6;
    public fixed byte Encounter[BattleLoadEvent.EncounterDumpSize];
    public fixed byte Species[16];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MenuOpenNative
{
    public int Which;
    public fixed byte Party[4];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ShopOpenNative
{
    public ushort Map;
    public int Kind;
    public fixed int Items[48];
    public fixed int Prices[48];
    public int SellCount;
    public fixed int SellItem[64];
    public fixed int SellGold[64];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct EnemyLoadedNative
{
    public int ActorId;
    public int Catalog;
    public int FormRow;
    public int Level;
    public int Hp;
    public int MaxHp;
    public int Str;
    public int Vit;
    public int Wit;
    public int Agi;
    public int Exp;
    public int Gold;
    public int AttackCount;
    public int AttackRange;
    public int DropItem0;
    public int DropItem1;
    public int DropRate0;
    public int DropRate1;
    public int FireResist;
    public int WaterResist;
    public int WindResist;
    public int EarthResist;
    public int SkillCount;
    public fixed int SkillPower[8];
    public fixed int SkillSpeed[8];
    public fixed int SkillElement[8];
    public fixed int SkillUsesStrength[8];
    public fixed int SkillEffect[8];
    public fixed int SkillMode[8];
    public fixed int SkillAdd[8];
    public fixed int SkillChance[8];
    public fixed int SkillAddLevel[8];
    public fixed byte SkillName[192];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct CharacterNative
{
    public int Id;
    public int Level;
    public int Hp;
    public int MaxHp;
    public int Sp;
    public int MaxSp;
    public int Str;
    public int Vit;
    public int Wit;
    public int Agi;
    public int Exp;
    public int Fire;
    public int Water;
    public int Wind;
    public int Earth;
    public int Mp1;
    public int Mp2;
    public int Mp3;
    public fixed int WeaponLevel[4];
    public fixed int WeaponType[4];
    public int LearnedCount;
    public fixed int Learned[128];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ItemNative
{
    public int Id;
    public int Cost;
    public int Icon;
    public int UseStatus;
    public int Unknown7;
    public int Para1Pre;
    public int Para2;
    public int Para3;
    public int Para4;
    public int Para1Post;
    public int Para2Post;
    public int Para3Post;
    public int Para4Post;
    public int SellPrice;
    public int Effect;
    public int EffectValue;
    public int Unknown8;
    public int Unknown11;
    public int Unknown12;
    public int Unknown13;
    public int Unknown14;
    public int Unknown27;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Text1Native
{
    public uint Src;
    public int SrcLen;
    public uint Dest;
    public int DestLen;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MagicNative
{
    public int Id;
    public int Element;
    public int CharacterMask;
    public int ReqCount;
    public fixed int ReqKind[4];
    public fixed int ReqLevel[4];
    public int Power;
    public int IpCost;
    public int Cost;
    public int Area;
    public int Range;
    public int ElementFlags;
    public int Effect;
    public int Mode;
    public int Crit;
    public fixed byte Name[32];
    public int Radius;
    public int Distance;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct DialogueNative
{
    public const int MaxPayload = 4096;
    public fixed byte Stem[16];
    public ushort ScriptId;
    public ushort Pad;
    public int OpIndex;
    public int Kind;
    public uint Src;
    public int SrcLen;
    public int DestLen;
    public fixed byte Dest[MaxPayload];
    public int Skip;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MapFileNative
{
    public fixed byte Stem[16];
    public int Kind;
    public uint Ptr;
    public int Len;
}

public static unsafe class NativeEntry
{
    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimePatchText1", CallConvs = [typeof(CallConvCdecl)])]
    public static int PatchText1(Text1Native* req)
    {
        if (req == null || req->Src == 0 || req->SrcLen <= 0)
        {
            return -1;
        }

        try
        {
            var src = CopyBytes((byte*)req->Src, req->SrcLen);
            var tables = ItemTextBin.Parse(src);
            for (var id = 1; id <= ItemTextBin.ItemCount; id++)
            {
                var ev = new ItemEvent(id, 0, 0, 0);
                ev.SeedText(tables.Names[id - 1], tables.ShortNames[id - 1], tables.Descriptions[id - 1]);
                ModHost.OnItem(ev, NativeLog);
                if (ev.NameSet)
                {
                    tables.Names[id - 1] = ev.Name;
                }

                if (ev.ShortNameSet)
                {
                    tables.ShortNames[id - 1] = ev.ShortName;
                }

                if (ev.DescriptionSet)
                {
                    tables.Descriptions[id - 1] = ev.Description;
                }
            }

            var result = ItemTextBin.PatchInPlace(src, tables);
            if (result.Data.Length != src.Length)
            {
                NativeLog($"PatchText1: in-place resized {src.Length} -> {result.Data.Length}");
                return -1;
            }

            NativeLog($"PatchText1: in-place {src.Length} applied={result.Applied} skipped={result.Skipped}");
            var pin = ModHost.PinText1(result.Data);
            req->Dest = (uint)pin;
            req->DestLen = result.Data.Length;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"PatchText1: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeGetMapFile", CallConvs = [typeof(CallConvCdecl)])]
    public static int GetMapFile(MapFileNative* req)
    {
        if (req == null)
        {
            return 0;
        }

        try
        {
            var stem = ReadFixed(req->Stem, 16);
            if (!ModHost.TryGetMapFile(stem, req->Kind, out var ptr, out var len))
            {
                req->Ptr = 0;
                req->Len = 0;
                return 0;
            }

            req->Ptr = unchecked((uint)ptr);
            req->Len = len;
            return 1;
        }
        catch (Exception ex)
        {
            NativeLog($"GetMapFile: {ex}");
            req->Ptr = 0;
            req->Len = 0;
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeInit", CallConvs = [typeof(CallConvCdecl)])]
    public static int Init(byte* modsJsonPathUtf8)
    {
        try
        {
            var path = ReadUtf8(modsJsonPathUtf8);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                NativeLog($"mods.json missing: {path}");
                return -1;
            }

            ModHost.Initialize(path, NativeLog);
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"Init: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnMapOpen", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnMapOpen(MapOpenNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var stem = ReadFixed(req->Stem, 16);
            var cache = ReadFixed(req->CacheDir, 260);
            var dirty = ModHost.OnMapOpen(stem, req->From, req->To, req->Spawn, cache, NativeLog);
            req->Dirty = dirty;
            return dirty >= 0 ? 0 : -1;
        }
        catch (Exception ex)
        {
            NativeLog($"OnMapOpen: {ex}");
            req->Dirty = 0;
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnMapPatchInfo", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnMapPatchInfo(MapPatchInfoNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var stem = ReadFixed(req->Stem, 16);
            if (!ModHost.TryFillPatchInfo(stem, out var patch) || patch == null)
            {
                req->Dirty = 0;
                return 0;
            }

            req->Sec7 = (uint)patch.Sec7Ptr;
            req->Sec7Len = patch.Sec7.Length;
            req->Scn = (uint)patch.ScnPtr;
            req->ScnLen = patch.Scn.Length;
            req->Ofs = (uint)patch.OfsPtr;
            req->OfsLen = patch.Ofs.Length;
            req->StockScnLen = patch.StockScnLen;
            req->StockOfsLen = patch.StockOfsLen;
            req->Dirty = 1;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnMapPatchInfo: {ex}");
            req->Dirty = 0;
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnScriptLookup", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnScriptLookup(ScriptLookupNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var stem = ReadFixed(req->Stem, 16);
            var rc = ModHost.OnScriptExecute(stem, req->ScriptId, out var ip, NativeLog);
            req->Ip = ip;
            req->Redirect = rc;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnScriptLookup: {ex}");
            req->Redirect = 0;
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnCallHook", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnCallHook(CallHookNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var stem = ReadFixed(req->Stem, 16);
            var rc = ModHost.OnCallHook(stem, req->Table, req->HookId, out var row, NativeLog);
            req->Row = row;
            req->Redirect = rc;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnCallHook: {ex}");
            req->Redirect = 0;
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnEventFlag", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnEventFlag(EventFlagNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var kind = req->Kind is >= 0 and <= 3 ? (EventFlagKind)req->Kind : EventFlagKind.Other;
            var ev = new EventFlagEvent(req->EventId, req->CallerRva, kind, req->Mask, req->FlagOffset)
            {
                SuppressLootUi = req->SuppressLootUi != 0,
                SuppressGold = req->SuppressGold != 0,
            };
            ModHost.OnEventFlag(ev, NativeLog);
            req->SuppressLootUi = ev.SuppressLootUi ? 1 : 0;
            req->SuppressGold = ev.SuppressGold ? 1 : 0;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnEventFlag: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnItemAssignUi", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnItemAssignUi(ItemAssignNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var ev = new ItemAssignUiEvent(req->EventId, req->ReturnRva, req->SkipVanilla != 0);
            ModHost.OnItemAssignUi(ev, NativeLog);
            req->SkipVanilla = ev.SkipVanilla ? 1 : 0;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnItemAssignUi: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnFieldGoldAdd", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnFieldGoldAdd(FieldGoldNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var ev = new FieldGoldAddEvent(req->EventId, req->Amount);
            ModHost.OnFieldGoldAdd(ev, NativeLog);
            req->Amount = ev.Amount;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnFieldGoldAdd: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeBindHost", CallConvs = [typeof(CallConvCdecl)])]
    public static int BindHost(HostApiNative* api)
    {
        if (api == null)
        {
            return -1;
        }

        try
        {
            Game.Native = new NativeGame(*api);
            NativeLog("Game.Stash / Gold / Flags / Party / Turbo / Encounters / Debug / Overlay bound");
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"BindHost: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnWorldMapLoad", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnWorldMapLoad(WorldMapLoadNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var dests = new List<WorldMapDestination>(WorldMapLoadEvent.SlotCount);
            var n = req->Count;
            if (n < 0)
            {
                n = 0;
            }

            if (n > WorldMapLoadEvent.SlotCount)
            {
                n = WorldMapLoadEvent.SlotCount;
            }

            for (var i = 0; i < n; i++)
            {
                var extras = new List<MapId>(4);
                var extraN = req->ExtraN[i];
                if (extraN > 4)
                {
                    extraN = 4;
                }

                for (var v = 0; v < extraN; v++)
                {
                    extras.Add(new MapId(req->Extra[i * 4 + v]));
                }

                dests.Add(new WorldMapDestination(new MapId(req->MapId[i]), req->Aux[i], req->X[i],
                    req->Y[i], req->Revealed[i] != 0, req->Accessible[i] != 0, req->Slot[i], extras,
                    req->Picture[i], ReadPicturePath(req, i), req->PictureW[i], req->PictureH[i]));
            }

            var ev = new WorldMapLoadEvent(req->SetId, req->AmapIndex, req->OriginCtx, dests);
            ModHost.OnWorldMapLoad(ev, NativeLog);
            var dirty = ev.Destinations.Count != dests.Count;
            if (!dirty)
            {
                for (var i = 0; i < dests.Count; i++)
                {
                    var a = dests[i];
                    var b = ev.Destinations[i];
                    if (a.Slot != b.Slot || a.Map != b.Map || a.Aux != b.Aux || a.X != b.X ||
                        a.Y != b.Y || a.Revealed != b.Revealed || a.Accessible != b.Accessible ||
                        a.Picture != b.Picture ||
                        a.PictureWidth != b.PictureWidth || a.PictureHeight != b.PictureHeight ||
                        !string.Equals(a.PicturePath, b.PicturePath, StringComparison.Ordinal))
                    {
                        dirty = true;
                        break;
                    }
                }
            }

            var outN = 0;
            foreach (var dest in ev.Destinations)
            {
                if (outN >= WorldMapLoadEvent.SlotCount)
                {
                    break;
                }

                if (dest.Map.Value == 0)
                {
                    dirty = true;
                    continue;
                }

                req->Slot[outN] = dest.Slot;
                req->MapId[outN] = dest.Map.Value;
                req->Aux[outN] = (ushort)dest.Aux;
                req->X[outN] = (short)dest.X;
                req->Y[outN] = (short)dest.Y;
                req->Revealed[outN] = dest.Revealed ? 1 : 0;
                req->Accessible[outN] = dest.Accessible ? 1 : 0;
                req->Picture[outN] = dest.Picture;
                req->PictureW[outN] = (short)dest.PictureWidth;
                req->PictureH[outN] = (short)dest.PictureHeight;
                WritePicturePath(req, outN, ResolvePicturePath(dest.PicturePath));
                outN++;
            }

            req->Count = outN;
            req->Dirty = dirty ? 1 : 0;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnWorldMapLoad: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnWorldMapConfirm", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnWorldMapConfirm(WorldMapConfirmNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var ev = new WorldMapConfirmEvent(new MapId(req->MapId), req->Allow != 0);
            ModHost.OnWorldMapConfirm(ev, NativeLog);
            req->Allow = ev.Allow ? 1 : 0;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnWorldMapConfirm: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnMapTravel", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnMapTravel(MapTravelNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var kind = req->Kind switch
            {
                1 => MapTravelKind.WorldMap,
                2 => MapTravelKind.Other,
                3 => MapTravelKind.WorldMapOpen,
                _ => MapTravelKind.Field,
            };
            var ev = new MapTravelEvent(new MapId(req->From), new MapId(req->Dest), req->Spawn, kind);
            ModHost.OnMapTravel(ev, NativeLog);
            req->Dest = ev.Destination.Value;
            req->Spawn = ev.Spawn;
            req->Allow = ev.Allow ? 1 : 0;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnMapTravel: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnTitleScreen", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnTitleScreen()
    {
        try
        {
            ModHost.OnTitleScreen(new TitleScreenEvent());
            ModHost.TryApplyItemText1(NativeLog);
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnTitleScreen: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnCharacter", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnCharacter(CharacterNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var ev = new CharacterEvent(req->Id, req->Level, req->Hp, req->MaxHp, req->Sp, req->MaxSp,
                req->Str, req->Vit, req->Wit, req->Agi, req->Exp, req->Fire, req->Water, req->Wind,
                req->Earth, req->Mp1, req->Mp2, req->Mp3);
            for (var i = 0; i < CharacterEvent.WeaponSlotCount; i++)
            {
                ev.WeaponLevels[i] = req->WeaponLevel[i];
                ev.WeaponTypes[i] = (WeaponType)req->WeaponType[i];
            }

            var n = req->LearnedCount < 0 ? 0 : req->LearnedCount > 128 ? 128 : req->LearnedCount;
            for (var i = 0; i < n; i++)
            {
                var id = req->Learned[i];
                if (id > 0)
                {
                    ev.Learned.Add(id);
                }
            }

            ModHost.OnCharacter(ev, NativeLog);
            req->Level = ev.Level;
            req->Hp = ev.Hp;
            req->MaxHp = ev.MaxHp;
            req->Sp = ev.Sp;
            req->MaxSp = ev.MaxSp;
            req->Str = ev.Str;
            req->Vit = ev.Vit;
            req->Wit = ev.Wit;
            req->Agi = ev.Agi;
            req->Exp = ev.Exp;
            req->Fire = ev.Fire;
            req->Water = ev.Water;
            req->Wind = ev.Wind;
            req->Earth = ev.Earth;
            req->Mp1 = ev.Mp1;
            req->Mp2 = ev.Mp2;
            req->Mp3 = ev.Mp3;
            for (var i = 0; i < CharacterEvent.WeaponSlotCount; i++)
            {
                req->WeaponLevel[i] = i < ev.WeaponLevels.Length ? ev.WeaponLevels[i] : 0;
                req->WeaponType[i] = i < ev.WeaponTypes.Length ? (int)ev.WeaponTypes[i] : 0;
            }

            var learnedN = 0;
            foreach (var id in ev.Learned)
            {
                if (id < 1 || id > 127 || learnedN >= 128)
                {
                    continue;
                }

                req->Learned[learnedN++] = id;
            }

            req->LearnedCount = learnedN;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnCharacter: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnItem", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnItem(ItemNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var ev = new ItemEvent(req->Id, req->Cost, req->Icon, req->UseStatus)
            {
                Unknown7 = req->Unknown7,
                Para1Pre = req->Para1Pre,
                Para2 = req->Para2,
                Para3 = req->Para3,
                Para4 = req->Para4,
                Para1Post = req->Para1Post,
                Para2Post = req->Para2Post,
                Para3Post = req->Para3Post,
                Para4Post = req->Para4Post,
                Effect = (Skill)req->Effect,
                EffectValue = req->EffectValue,
                Unknown8 = req->Unknown8,
                Unknown11 = req->Unknown11,
                Unknown12 = req->Unknown12,
                Unknown13 = req->Unknown13,
                Unknown14 = req->Unknown14,
                Unknown27 = req->Unknown27,
            };
            ModHost.OnItem(ev, NativeLog);
            req->Cost = ev.Cost;
            req->SellPrice = ev.SellPrice;
            req->Icon = ev.Icon;
            req->UseStatus = ev.UseStatus;
            req->Unknown7 = ev.Unknown7;
            req->Effect = (int)ev.Effect;
            req->EffectValue = ev.EffectValue;
            req->Para1Pre = ev.Para1Pre;
            req->Para2 = ev.Para2;
            req->Para3 = ev.Para3;
            req->Para4 = ev.Para4;
            req->Para1Post = ev.Para1Post;
            req->Para2Post = ev.Para2Post;
            req->Para3Post = ev.Para3Post;
            req->Para4Post = ev.Para4Post;
            req->Unknown8 = ev.Unknown8;
            req->Unknown11 = ev.Unknown11;
            req->Unknown12 = ev.Unknown12;
            req->Unknown13 = ev.Unknown13;
            req->Unknown14 = ev.Unknown14;
            req->Unknown27 = ev.Unknown27;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnItem: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnMagic", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnMagic(MagicNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var reqs = new List<LearnRequirement>();
            var n = req->ReqCount < 0 ? 0 : req->ReqCount > 4 ? 4 : req->ReqCount;
            for (var i = 0; i < n; i++)
            {
                var kind = req->ReqKind[i];
                if (kind == 0 || kind == ' ')
                {
                    continue;
                }

                reqs.Add(new LearnRequirement((LearnKind)kind, req->ReqLevel[i]));
            }

            var element = req->Element is >= 1 and <= 4 ? (MagicElement)req->Element : MagicElement.None;
            var name = ReadFixedUtf8(req->Name, 32);
            if (string.IsNullOrEmpty(name) && Enum.IsDefined(typeof(Skill), req->Id))
            {
                name = ((Skill)req->Id).ToString();
            }

            var ev = new MagicEvent(req->Id, name, element, req->CharacterMask, reqs)
            {
                Power = req->Power,
                Speed = req->IpCost,
                Cost = req->Cost,
                IpKnockback = req->Area,
                Exp = req->Range,
                Radius = req->Radius,
                Distance = req->Distance,
                ElementFlags = req->ElementFlags,
                Effect = (EffectType)req->Effect,
                Mode = req->Mode,
                CancelChance = req->Crit,
            };
            ModHost.OnMagic(ev, NativeLog);
            req->Element = (int)ev.Element;
            req->CharacterMask = ev.CharacterMask;
            req->Power = ev.Power;
            req->IpCost = ev.Speed;
            req->Cost = ev.Cost;
            req->Area = ev.IpKnockback;
            req->Range = ev.Exp;
            req->Radius = ev.Radius;
            req->Distance = ev.Distance;
            req->ElementFlags = ev.ElementFlags;
            req->Effect = (int)ev.Effect;
            req->Mode = ev.Mode;
            req->Crit = ev.CancelChance;
            var outN = 0;
            foreach (var r in ev.Requirements)
            {
                if (outN >= 4)
                {
                    break;
                }

                var kind = (int)r.Kind;
                if (kind == 0)
                {
                    continue;
                }

                req->ReqKind[outN] = kind;
                req->ReqLevel[outN] = r.Level < 0 ? 0 : r.Level > 255 ? 255 : r.Level;
                outN++;
            }

            req->ReqCount = outN;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnMagic: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnDialogue", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnDialogue(DialogueNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            if (req->Src == 0 || req->SrcLen <= 0 || req->SrcLen > DialogueNative.MaxPayload)
            {
                req->DestLen = 0;
                req->Skip = 0;
                return 0;
            }

            var src = new byte[req->SrcLen];
            Marshal.Copy((nint)req->Src, src, 0, req->SrcLen);
            var type8 = req->Kind == (int)DialogueKind.Type8;
            var stem = ReadFixed(req->Stem, 16);
            var ev = new DialogueEvent(MapId.Parse(stem), req->ScriptId, req->OpIndex,
                type8 ? DialogueKind.Type8 : DialogueKind.Type1, src, stem);
            ModHost.OnDialogue(ev);
            if (ev.Skip)
            {
                req->Skip = 1;
                req->DestLen = 0;
                return 0;
            }

            if (!ev.TryGetReplacement(out var dest) || dest.Length > 0x0FFF)
            {
                req->DestLen = 0;
                req->Skip = 0;
                return 0;
            }

            for (var i = 0; i < dest.Length; i++)
            {
                req->Dest[i] = dest[i];
            }

            req->DestLen = dest.Length;
            req->Skip = 0;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnDialogue: {ex}");
            req->DestLen = 0;
            req->Skip = 0;
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnTick", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnTick(TickNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var ev = new TickEvent(new PadState(req->Buttons, req->LeftTrigger, req->RightTrigger));
            ModHost.OnTick(ev);
            req->Block = ev.BlockGameInput ? 1 : 0;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnTick: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnSave", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnSave(SaveEventNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var ev = new SaveEvent(req->Slot, CopyBytes(req->Trailer, req->TrailerLen));
            ModHost.OnSave(ev, NativeLog);
            req->TrailerLen = WriteBytes(ev.Trailer, req->Trailer);
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnSave: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnLoad", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnLoad(LoadEventNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var phase = req->Phase == 1 ? LoadPhase.Applied : LoadPhase.ConfirmPeek;
            var ev = new LoadEvent(req->Slot, phase, CopyBytes(req->Trailer, req->TrailerLen),
                req->Allow != 0);
            ModHost.OnLoad(ev, NativeLog);
            req->Allow = ev.Allow ? 1 : 0;
            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnLoad: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnBattleSetup", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnBattleSetup(BattleLoadNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var encounter = CopyEncounter(req);
            var ev = new BattleSetupEvent(req->Formation, new MapId(req->Map), new MapId(req->Dest),
                req->Spawn, req->BattleMode, encounter);
            ModHost.OnBattleSetup(ev, NativeLog);
            WriteEncounter(req, ev.Encounter);

            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnBattleSetup: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnBattleLoad", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnBattleLoad(BattleLoadNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var party = new int[4];
            for (var i = 0; i < 4; i++)
            {
                party[i] = req->Party[i];
            }

            var encounter = CopyEncounter(req);
            var species = new byte[16];
            for (var i = 0; i < 16; i++)
            {
                species[i] = req->Species[i];
            }

            var ev = new BattleLoadEvent(party, req->Formation, new MapId(req->Map), new MapId(req->Dest),
                req->Spawn, req->BattleMode, encounter, req->PackW0, req->PackW2, req->PackW4,
                req->PackW6, species);
            ModHost.OnBattleLoad(ev, NativeLog);
            for (var i = 0; i < 4; i++)
            {
                var id = i < ev.Party.Length ? ev.Party[i] : 0;
                req->Party[i] = id is >= 0 and <= 255 ? (byte)id : (byte)0;
            }

            WriteEncounter(req, ev.Encounter);

            for (var i = 0; i < 16; i++)
            {
                req->Species[i] = i < ev.SpeciesMap.Length ? ev.SpeciesMap[i] : (byte)0;
            }

            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnBattleLoad: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnMenuOpen", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnMenuOpen(MenuOpenNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var party = new int[4];
            for (var i = 0; i < 4; i++)
            {
                party[i] = req->Party[i];
            }

            var which = req->Which is >= 0 and <= 2 ? (MenuKind)req->Which : MenuKind.Status;
            var ev = new MenuOpenEvent(which, party);
            ModHost.OnMenuOpen(ev, NativeLog);
            for (var i = 0; i < 4; i++)
            {
                var id = i < ev.Party.Length ? ev.Party[i] : 0;
                req->Party[i] = id is >= 0 and <= 255 ? (byte)id : (byte)0;
            }

            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnMenuOpen: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnEnemyLoaded", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnEnemyLoaded(EnemyLoadedNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var ev = new EnemyLoadedEvent(req->ActorId, req->Catalog, req->FormRow, req->Level,
                req->Hp, req->MaxHp, req->Str, req->Vit, req->Wit, req->Agi, req->Exp, req->Gold);
            ev.AttackCount = req->AttackCount;
            ev.AttackRange = req->AttackRange;
            ev.Drops[0].Item = (Item)req->DropItem0;
            ev.Drops[0].Rate = req->DropRate0;
            ev.Drops[1].Item = (Item)req->DropItem1;
            ev.Drops[1].Rate = req->DropRate1;
            ev.FireResist = req->FireResist;
            ev.WaterResist = req->WaterResist;
            ev.WindResist = req->WindResist;
            ev.EarthResist = req->EarthResist;
            var skillN = req->SkillCount < 0 ? 0 : req->SkillCount > 8 ? 8 : req->SkillCount;
            for (var i = 0; i < skillN; i++)
            {
                ev.Skills.Add(new EnemySkill(ReadFixedUtf8(&req->SkillName[i * 24], 24),
                    req->SkillPower[i], req->SkillUsesStrength[i] != 0, req->SkillSpeed[i],
                    req->SkillElement[i], (EffectType)req->SkillEffect[i], req->SkillMode[i],
                    (StatusAilment)req->SkillAdd[i], req->SkillChance[i], req->SkillAddLevel[i]));
            }

            ModHost.OnEnemyLoaded(ev, NativeLog);
            req->Level = ev.Level;
            req->Hp = ev.Hp;
            req->MaxHp = ev.MaxHp;
            req->Str = ev.Str;
            req->Vit = ev.Vit;
            req->Wit = ev.Wit;
            req->Agi = ev.Agi;
            req->Exp = ev.Exp;
            req->Gold = ev.Gold;
            req->AttackCount = ev.AttackCount;
            req->AttackRange = ev.AttackRange;
            req->DropItem0 = (int)ev.Drops[0].Item;
            req->DropRate0 = ev.Drops[0].Rate;
            req->DropItem1 = (int)ev.Drops[1].Item;
            req->DropRate1 = ev.Drops[1].Rate;
            req->FireResist = ev.FireResist;
            req->WaterResist = ev.WaterResist;
            req->WindResist = ev.WindResist;
            req->EarthResist = ev.EarthResist;
            skillN = ev.Skills.Count > 8 ? 8 : ev.Skills.Count;
            req->SkillCount = skillN;
            for (var i = 0; i < skillN; i++)
            {
                var sk = ev.Skills[i];
                req->SkillPower[i] = sk.Power;
                req->SkillSpeed[i] = sk.Speed;
                req->SkillElement[i] = sk.Element;
                req->SkillUsesStrength[i] = sk.Strength ? 1 : 0;
                req->SkillEffect[i] = (int)sk.Effect;
                req->SkillMode[i] = sk.Mode;
                req->SkillAdd[i] = (int)sk.AddAilment;
                req->SkillChance[i] = sk.Chance;
                req->SkillAddLevel[i] = sk.AddLevel;
            }

            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnEnemyLoaded: {ex}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "GrandiaRuntimeOnShopOpen", CallConvs = [typeof(CallConvCdecl)])]
    public static int OnShopOpen(ShopOpenNative* req)
    {
        if (req == null)
        {
            return -1;
        }

        try
        {
            var kind = req->Kind is >= 1 and <= 5 ? (ShopKind)req->Kind : ShopKind.Unknown;
            var pages = new List<Item>[ShopOpenEvent.PageCount];
            for (var p = 0; p < ShopOpenEvent.PageCount; p++)
            {
                pages[p] = [];
                for (var i = 0; i < ShopOpenEvent.SlotsPerPage; i++)
                {
                    var id = req->Items[p * ShopOpenEvent.SlotsPerPage + i];
                    if (id > 0)
                    {
                        pages[p].Add((Item)id);
                    }
                }
            }

            var ev = new ShopOpenEvent(new MapId(req->Map), kind, pages[0], pages[1], pages[2]);
            for (var i = 0; i < ShopOpenEvent.PageCount * ShopOpenEvent.SlotsPerPage; i++)
            {
                var id = req->Items[i];
                if (id > 0)
                {
                    ev.SeedPrice((Item)id, req->Prices[i]);
                }
            }

            ModHost.OnShopOpen(ev, NativeLog);
            ModHost.TryApplyItemText1(NativeLog);
            for (var p = 0; p < ShopOpenEvent.PageCount; p++)
            {
                var list = ev.Page(p);
                for (var i = 0; i < ShopOpenEvent.SlotsPerPage; i++)
                {
                    var id = i < list.Count ? (int)list[i] : 0;
                    if (id < 0 || id > 511)
                    {
                        id = 0;
                    }

                    var slot = p * ShopOpenEvent.SlotsPerPage + i;
                    req->Items[slot] = id;
                    req->Prices[slot] = id > 0 && ev.HasPriceOverride((Item)id)
                        ? ev.GetPrice((Item)id)
                        : -1;
                }
            }

            var sellN = 0;
            foreach (var kv in ev.SellPrices)
            {
                if (sellN >= ShopOpenEvent.SellOverrideCap)
                {
                    break;
                }

                var id = (int)kv.Key;
                if (id < 1 || id > 511)
                {
                    continue;
                }

                req->SellItem[sellN] = id;
                req->SellGold[sellN] = kv.Value < 0 ? 0 : kv.Value;
                sellN++;
            }

            req->SellCount = sellN;

            return 0;
        }
        catch (Exception ex)
        {
            NativeLog($"OnShopOpen: {ex}");
            return -1;
        }
    }

    private static byte[] CopyBytes(byte* ptr, int len)
    {
        if (ptr == null || len <= 0)
        {
            return [];
        }

        if (len > 1024)
        {
            len = 1024;
        }

        var buf = new byte[len];
        Marshal.Copy((nint)ptr, buf, 0, len);
        return buf;
    }

    private static int WriteBytes(byte[]? data, byte* dest)
    {
        if (dest == null || data == null || data.Length == 0)
        {
            return 0;
        }

        var n = Math.Min(data.Length, 1024);
        Marshal.Copy(data, 0, (nint)dest, n);
        return n;
    }

    private static string ReadUtf8(byte* ptr)
    {
        if (ptr == null)
        {
            return "";
        }

        var len = 0;
        while (ptr[len] != 0)
        {
            len++;
        }

        return Encoding.UTF8.GetString(ptr, len);
    }

    private static string ReadFixedUtf8(byte* ptr, int max)
    {
        if (ptr == null || max <= 0)
        {
            return "";
        }

        var len = 0;
        while (len < max && ptr[len] != 0)
        {
            len++;
        }

        return len == 0 ? "" : Encoding.UTF8.GetString(ptr, len);
    }

    private static void WriteFixedUtf8(byte* ptr, int max, string value)
    {
        if (ptr == null || max <= 0)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        var n = bytes.Length < max ? bytes.Length : max - 1;
        for (var i = 0; i < n; i++)
        {
            ptr[i] = bytes[i];
        }

        for (var i = n; i < max; i++)
        {
            ptr[i] = 0;
        }
    }

    private static byte[] CopyEncounter(BattleLoadNative* req)
    {
        var n = BattleLoadEvent.EncounterDumpSize;
        var encounter = new byte[n];
        for (var i = 0; i < n; i++)
        {
            encounter[i] = req->Encounter[i];
        }

        return encounter;
    }

    private static void WriteEncounter(BattleLoadNative* req, byte[] encounter)
    {
        var n = BattleLoadEvent.EncounterDumpSize;
        for (var i = 0; i < n; i++)
        {
            req->Encounter[i] = i < encounter.Length ? encounter[i] : (byte)0;
        }
    }

    private const int PicturePathSlot = 260;

    private static string ReadPicturePath(WorldMapLoadNative* req, int index)
    {
        if (req == null || index < 0 || index >= 32)
        {
            return "";
        }

        return ReadFixed(&req->PicturePath[index * PicturePathSlot], PicturePathSlot);
    }

    private static void WritePicturePath(WorldMapLoadNative* req, int index, string? path)
    {
        if (req == null || index < 0 || index >= 32)
        {
            return;
        }

        var dest = &req->PicturePath[index * PicturePathSlot];
        for (var i = 0; i < PicturePathSlot; i++)
        {
            dest[i] = 0;
        }

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(path);
        var n = Math.Min(bytes.Length, PicturePathSlot - 1);
        for (var i = 0; i < n; i++)
        {
            dest[i] = bytes[i];
        }
    }

    private static string? ResolvePicturePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        path = path.Trim();
        var resolved = ModHost.ResolveAsset(path);
        if (string.IsNullOrEmpty(resolved))
        {
            NativeLog($"world-map picture not in mod DLL: {path}");
            return path;
        }

        return resolved;
    }

    private static string ReadFixed(byte* ptr, int max)
    {
        var len = 0;
        while (len < max && ptr[len] != 0)
        {
            len++;
        }

        return len == 0 ? "" : Encoding.UTF8.GetString(ptr, len);
    }

    private static void NativeLog(string message)
    {
        GameLog.Write("INFO", message);
    }
}
