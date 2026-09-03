namespace Grandia.Sdk;

/// <summary>
/// Marks the plugin entry class. The host constructs it once and calls every
/// <see cref="InitAttribute"/> method. Put hook methods on this class or
/// <see cref="ModContext.Register"/> other types from <c>Init</c>.
/// Name, version, and description are read by the modloader for the mod list
/// — drop the compiled DLL in <c>mods/</c>; there is no <c>mod.json</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModAttribute : Attribute
{
    public ModAttribute()
    {
    }

    public ModAttribute(string name)
    {
        Name = name;
    }

    public ModAttribute(string name, string version)
    {
        Name = name;
        Version = version;
    }

    /// <summary>Display name in the modloader list. Defaults to the DLL name.</summary>
    public string? Name { get; }

    /// <summary>Display version (for example <c>1.0.0</c>).</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>Short summary shown in the modloader list.</summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Optional id. The loader keys enable/order by the DLL file name, not this.
    /// </summary>
    public string? Id { get; set; }
}
