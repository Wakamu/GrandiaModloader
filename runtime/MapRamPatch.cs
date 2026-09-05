using System.Runtime.InteropServices;

namespace Grandia.Runtime;

internal sealed class RedirectBlob
{
    public required int Id { get; init; }
    public required byte[] Bytes { get; init; }
    private GCHandle _pin;

    public void Pin()
    {
        if (!_pin.IsAllocated && Bytes.Length > 0)
        {
            _pin = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
        }
    }

    public nint Ptr => _pin.IsAllocated ? _pin.AddrOfPinnedObject() : 0;
}

internal sealed class MapRamPatch
{
    public byte[] Sec7 { get; init; } = [];
    public byte[] Scn { get; init; } = [];
    public byte[] Ofs { get; init; } = [];
    public int StockScnLen { get; init; }
    public int StockOfsLen { get; init; }
    public Dictionary<int, RedirectBlob> Scripts { get; } = [];
    public Dictionary<int, RedirectBlob> Hooks { get; } = [];

    private GCHandle _sec7;
    private GCHandle _scn;
    private GCHandle _ofs;
    private bool _pinned;

    public void Pin()
    {
        if (_pinned)
        {
            return;
        }

        if (Sec7.Length > 0)
        {
            _sec7 = GCHandle.Alloc(Sec7, GCHandleType.Pinned);
        }

        if (Scn.Length > 0)
        {
            _scn = GCHandle.Alloc(Scn, GCHandleType.Pinned);
        }

        if (Ofs.Length > 0)
        {
            _ofs = GCHandle.Alloc(Ofs, GCHandleType.Pinned);
        }

        foreach (var blob in Scripts.Values)
        {
            blob.Pin();
        }

        foreach (var blob in Hooks.Values)
        {
            blob.Pin();
        }

        _pinned = true;
    }

    public nint Sec7Ptr => _sec7.IsAllocated ? _sec7.AddrOfPinnedObject() : 0;
    public nint ScnPtr => _scn.IsAllocated ? _scn.AddrOfPinnedObject() : 0;
    public nint OfsPtr => _ofs.IsAllocated ? _ofs.AddrOfPinnedObject() : 0;

    public void SetScript(int id, byte[] bytes)
    {
        var blob = new RedirectBlob { Id = id, Bytes = bytes };
        blob.Pin();
        Scripts[id] = blob;
    }

    public void SetHook(int id, byte[] bytes)
    {
        var blob = new RedirectBlob { Id = id, Bytes = bytes };
        blob.Pin();
        Hooks[id] = blob;
    }
}

/// <summary>
/// One-shot script arms from <c>OnScriptExecute</c>. Kept pinned for the VM
/// but never reused as the next event's default bytecode.
/// </summary>
internal static class ScriptArmStore
{
    private static readonly Dictionary<(string Stem, int Id), RedirectBlob> Pins = new();

    public static nint Pin(string stem, int id, byte[] bytes)
    {
        var blob = new RedirectBlob { Id = id, Bytes = bytes };
        blob.Pin();
        Pins[(stem, id)] = blob;
        return blob.Ptr;
    }
}

internal static class MapRamStore
{
    internal const int Sec7Budget = 0x4000;
    private static readonly Dictionary<string, MapRamPatch?> Patches = new(StringComparer.OrdinalIgnoreCase);

    public static bool Has(string stem) => Patches.ContainsKey(stem);

    public static bool IsDirty(string stem) =>
        Patches.TryGetValue(stem, out var patch) && patch != null;

    public static void MarkClean(string stem) => Patches[stem] = null;

    public static MapRamPatch? Get(string stem) =>
        Patches.TryGetValue(stem, out var patch) ? patch : null;

    public static void LoadLive(string stem, MapRamPatch patch)
    {
        patch.Pin();
        Patches[stem] = patch;
    }

    public static void LoadEmbedded(string stem, byte[]? mdp, byte[]? scn, byte[]? ofs)
    {
        var sec7 = Array.Empty<byte>();
        if (mdp is { Length: > 0 } && MdpHookIds.TrySection(mdp, 7, out var off, out var len) &&
            len > 0)
        {
            sec7 = mdp.AsSpan(off, Math.Min(len, mdp.Length - off)).ToArray();
            if (sec7.Length > Sec7Budget)
            {
                sec7 = sec7.AsSpan(0, Sec7Budget).ToArray();
            }
        }

        var patch = new MapRamPatch
        {
            Sec7 = sec7,
            Scn = scn ?? [],
            Ofs = ofs ?? [],
            StockScnLen = 0,
            StockOfsLen = 0,
        };
        ExtractScripts(patch, OfsIds(patch.Ofs));
        ExtractHooks(patch, Sec7HookIds(patch.Sec7));
        LoadLive(stem, patch);
    }

    private static List<int> OfsIds(byte[] ofs)
    {
        var ids = new List<int>();
        for (var i = 0; i + 4 <= ofs.Length; i += 4)
        {
            var id = BitConverter.ToUInt16(ofs, i);
            if (id == 0xFFFF)
            {
                break;
            }

            ids.Add(id);
        }

        return ids;
    }

    private static List<int> Sec7HookIds(byte[] sec7)
    {
        if (sec7.Length < 0x18)
        {
            return [];
        }

        var ids = new List<int>();
        var count2 = sec7[3];
        var rel2 = BitConverter.ToInt32(sec7, 16);
        const int rowSize = 20;
        for (var i = 0; i < count2; i++)
        {
            var at = rel2 + i * rowSize;
            if (at < 0 || at + rowSize > sec7.Length)
            {
                break;
            }

            var hid = sec7[at];
            if (hid != 0)
            {
                ids.Add(hid);
            }
        }

        return ids;
    }

    public static MapRamPatch LoadEmitted(string stem, string cacheDir, ModsConfig cfg,
        IReadOnlyCollection<int> dirtyScripts, IReadOnlyCollection<int> dirtyHooks)
    {
        var sec7 = ReadSec7(Path.Combine(cacheDir, "FIELD", stem + ".mdp"));
        if (sec7.Length == 0)
        {
            sec7 = ReadSec7(Path.Combine(cacheDir, "FIELD", stem + ".MDP"));
        }

        var scn = ReadAll(Path.Combine(cacheDir, "TEXT", "EN", stem + ".SCN"));
        var ofs = ReadAll(Path.Combine(cacheDir, "TEXT", "EN", stem + ".OFS"));
        var stockScn = StockLen(cfg.Text, stem + ".SCN", stem + ".scn");
        var stockOfs = StockLen(cfg.Text, stem + ".OFS", stem + ".ofs");
        var patch = new MapRamPatch
        {
            Sec7 = sec7.Length > Sec7Budget ? sec7.AsSpan(0, Sec7Budget).ToArray() : sec7,
            Scn = scn,
            Ofs = ofs,
            StockScnLen = stockScn,
            StockOfsLen = stockOfs,
        };
        ExtractScripts(patch, dirtyScripts);
        ExtractHooks(patch, dirtyHooks);
        patch.Pin();
        Patches[stem] = patch;
        return patch;
    }

    private static void ExtractScripts(MapRamPatch patch, IReadOnlyCollection<int> dirty)
    {
        if (dirty.Count == 0 || patch.Scn.Length == 0 || patch.Ofs.Length == 0)
        {
            return;
        }

        var entries = new List<(int Id, int Off)>();
        var ofs = patch.Ofs;
        for (var i = 0; i + 4 <= ofs.Length; i += 4)
        {
            var id = BitConverter.ToUInt16(ofs, i);
            var off = BitConverter.ToUInt16(ofs, i + 2);
            if (id == 0xFFFF)
            {
                break;
            }

            entries.Add((id, off));
        }

        var want = dirty.ToHashSet();
        for (var i = 0; i < entries.Count; i++)
        {
            var (id, off) = entries[i];
            if (!want.Contains(id) || off < 0 || off >= patch.Scn.Length)
            {
                continue;
            }

            var end = i + 1 < entries.Count ? entries[i + 1].Off : patch.Scn.Length;
            if (end < off)
            {
                end = patch.Scn.Length;
            }

            end = Math.Min(end, patch.Scn.Length);
            patch.Scripts[id] = new RedirectBlob
            {
                Id = id,
                Bytes = patch.Scn.AsSpan(off, end - off).ToArray(),
            };
        }
    }

    private static void ExtractHooks(MapRamPatch patch, IReadOnlyCollection<int> dirty)
    {
        if (dirty.Count == 0 || patch.Sec7.Length < 0x18)
        {
            return;
        }

        var want = dirty.ToHashSet();
        var sec7 = patch.Sec7;
        var count2 = sec7[3];
        var rel2 = BitConverter.ToInt32(sec7, 16);
        const int rowSize = 20;
        for (var i = 0; i < count2; i++)
        {
            var at = rel2 + i * rowSize;
            if (at < 0 || at + rowSize > sec7.Length)
            {
                break;
            }

            var hid = sec7[at];
            if (hid == 0 || !want.Contains(hid))
            {
                continue;
            }

            patch.Hooks[hid] = new RedirectBlob
            {
                Id = hid,
                Bytes = sec7.AsSpan(at, rowSize).ToArray(),
            };
        }
    }

    private static byte[] ReadAll(string path) => File.Exists(path) ? File.ReadAllBytes(path) : [];

    private static int StockLen(string textRoot, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(textRoot))
        {
            return 0;
        }

        foreach (var name in names)
        {
            var p = Path.Combine(textRoot, name);
            if (File.Exists(p))
            {
                return (int)new FileInfo(p).Length;
            }
        }

        return 0;
    }

    private static byte[] ReadSec7(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var mdp = File.ReadAllBytes(path);
        if (!MdpHookIds.TrySection(mdp, 7, out var off, out var len) || len <= 0)
        {
            return [];
        }

        return mdp.AsSpan(off, len).ToArray();
    }
}
