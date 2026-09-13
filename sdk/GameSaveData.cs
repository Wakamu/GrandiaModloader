using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Grandia.Sdk;

/// <summary>
/// Shared extra-save bag (key → JSON value). Any mod can
/// <see cref="Set"/> / <see cref="Remove"/> at any time; the host writes
/// the whole bag on every slot save and replaces it on every slot load,
/// even when no user mods are loaded. Use a prefixed key
/// (<c>"YourMod.progress"</c>) so two mods do not share a name.
/// Cleared on the title screen and when a vanilla slot has no GMOD
/// trailer. Max payload <see cref="MaxBytes"/> bytes.
/// </summary>
public sealed class GameSaveData
{
    /// <summary>GMOD v2 payload cap (uint16 length on disk).</summary>
    public const int MaxBytes = 32768;

    public const int MaxKeyLength = 128;

    internal const string LegacyKey = "$legacy";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, JsonNode?> _map = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return VisibleCount();
            }
        }
    }

    /// <summary>User keys (reserved <c>$</c> names omitted).</summary>
    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (_gate)
            {
                return new ReadOnlyCollection<string>(VisibleKeys());
            }
        }
    }

    public bool Contains(string key)
    {
        if (!IsUserKey(key, out var k))
        {
            return false;
        }

        lock (_gate)
        {
            return _map.ContainsKey(k);
        }
    }

    /// <summary>
    /// Store <paramref name="value"/> (JSON-serializable: number, string,
    /// bool, object, array). Null removes the key. False if the key is
    /// invalid or the bag would exceed <see cref="MaxBytes"/>.
    /// </summary>
    public bool Set(string key, object? value)
    {
        if (!IsUserKey(key, out var k))
        {
            return false;
        }

        if (value is null)
        {
            return Remove(k);
        }

        JsonNode? node;
        try
        {
            var json = JsonSerializer.Serialize(value, value.GetType(), JsonOptions);
            node = JsonNode.Parse(json);
        }
        catch
        {
            return false;
        }

        lock (_gate)
        {
            _map.TryGetValue(k, out var previous);
            var had = _map.ContainsKey(k);
            _map[k] = node;
            if (ExportUtf8Unlocked().Length <= MaxBytes)
            {
                return true;
            }

            if (had)
            {
                _map[k] = previous;
            }
            else
            {
                _map.Remove(k);
            }

            return false;
        }
    }

    public bool Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_gate)
        {
            return _map.Remove(key.Trim());
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
        }
    }

    public bool TryGet<T>(string key, out T value)
    {
        value = default!;
        if (!IsUserKey(key, out var k))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_map.TryGetValue(k, out var node) || node is null)
            {
                return false;
            }

            try
            {
                var typed = node.Deserialize<T>(JsonOptions);
                if (typed is null && default(T) is not null)
                {
                    return false;
                }

                value = typed!;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public T Get<T>(string key, T fallback)
    {
        return TryGet(key, out T value) ? value : fallback;
    }

    public GameSaveData Clone()
    {
        var copy = new GameSaveData();
        lock (_gate)
        {
            foreach (var (k, v) in _map)
            {
                copy._map[k] = v?.DeepClone();
            }
        }

        return copy;
    }

    internal byte[] ExportUtf8()
    {
        lock (_gate)
        {
            return ExportUtf8Unlocked();
        }
    }

    internal void ReplaceFromBytes(byte[]? data)
    {
        lock (_gate)
        {
            _map.Clear();
            ImportUnlocked(data);
        }
    }

    internal void AbsorbSaveTrailer(byte[] original, byte[] afterHooks)
    {
        afterHooks ??= [];
        original ??= [];
        if (afterHooks.AsSpan().SequenceEqual(original))
        {
            return;
        }

        if (TryParseObject(afterHooks, out var obj) && obj is not null)
        {
            lock (_gate)
            {
                _map.Clear();
                ImportObjectUnlocked(obj);
            }

            return;
        }

        if (afterHooks.Length == 0)
        {
            lock (_gate)
            {
                _map.Remove(LegacyKey);
            }

            return;
        }

        lock (_gate)
        {
            _map[LegacyKey] = JsonValue.Create(Convert.ToBase64String(afterHooks));
            if (ExportUtf8Unlocked().Length > MaxBytes)
            {
                _map.Remove(LegacyKey);
            }
        }
    }

    internal static GameSaveData Parse(byte[]? data)
    {
        var bag = new GameSaveData();
        bag.ReplaceFromBytes(data);
        return bag;
    }

    private int VisibleCount()
    {
        var n = 0;
        foreach (var key in _map.Keys)
        {
            if (!key.StartsWith('$'))
            {
                n++;
            }
        }

        return n;
    }

    private List<string> VisibleKeys()
    {
        var keys = new List<string>(_map.Count);
        foreach (var key in _map.Keys)
        {
            if (!key.StartsWith('$'))
            {
                keys.Add(key);
            }
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    private byte[] ExportUtf8Unlocked()
    {
        var obj = new JsonObject();
        foreach (var key in _map.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            obj[key] = _map[key]?.DeepClone();
        }

        return JsonSerializer.SerializeToUtf8Bytes(obj, JsonOptions);
    }

    private void ImportUnlocked(byte[]? data)
    {
        if (data is not { Length: > 0 })
        {
            return;
        }

        if (TryParseObject(data, out var obj) && obj is not null)
        {
            ImportObjectUnlocked(obj);
            return;
        }

        _map[LegacyKey] = JsonValue.Create(Convert.ToBase64String(data));
    }

    private void ImportObjectUnlocked(JsonObject obj)
    {
        foreach (var kv in obj)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Key.Length > MaxKeyLength)
            {
                continue;
            }

            _map[kv.Key] = kv.Value?.DeepClone();
        }
    }

    private static bool TryParseObject(byte[] data, out JsonObject? obj)
    {
        obj = null;
        try
        {
            if (JsonNode.Parse(Encoding.UTF8.GetString(data)) is not JsonObject parsed)
            {
                return false;
            }

            obj = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUserKey(string? key, out string trimmed)
    {
        trimmed = "";
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        trimmed = key.Trim();
        if (trimmed.Length is 0 or > MaxKeyLength || trimmed.StartsWith('$'))
        {
            return false;
        }

        return true;
    }
}
