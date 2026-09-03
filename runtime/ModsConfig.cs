using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grandia.Runtime;

public sealed class ModsConfig
{
    /// <summary>Absolute path to field_tools.exe (or the folder that contains it).</summary>
    public string Tools { get; set; } = "";

    /// <summary>Legacy mods.json key; same as <see cref="Tools"/>.</summary>
    public string Grandipelago { get; set; } = "";

    public string Field { get; set; } = "";

    public string Text { get; set; } = "";

    public string Cache { get; set; } = "";

    public List<ModEntry> Mods { get; set; } = [];

    public static ModsConfig Load(string path)
    {
        var text = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<ModsConfig>(text, JsonOptions()) ?? new ModsConfig();
        cfg.Mods ??= [];
        return cfg;
    }

    public static JsonSerializerOptions JsonOptions() =>
        new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
}

public sealed class ModEntry
{
    public string Id { get; set; } = "";

    public string Assembly { get; set; } = "";
}
