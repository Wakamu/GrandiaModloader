namespace Grandia.Sdk;

/// <summary>
/// Passed to <see cref="InitAttribute"/> methods. Register hook classes,
/// embedded maps / scripts, and plain services from here.
/// </summary>
public sealed class ModContext
{
    private readonly Action<string>? _log;

    public ModContext(string id, ModHooks hooks, Action<string>? log = null)
        : this(id, hooks, new ModMapSet(), new ModScriptSet(), log)
    {
    }

    public ModContext(string id, ModHooks hooks, ModMapSet maps, Action<string>? log = null)
        : this(id, hooks, maps, new ModScriptSet(), log)
    {
    }

    public ModContext(string id, ModHooks hooks, ModMapSet maps, ModScriptSet scripts,
        Action<string>? log = null)
    {
        Id = id;
        Hooks = hooks;
        Maps = maps;
        Scripts = scripts;
        _log = log;
    }

    public string Id { get; }

    public ModHooks Hooks { get; }

    public ModMapSet Maps { get; }

    public ModScriptSet Scripts { get; }

    public void Log(string message)
    {
        if (_log is not null)
        {
            _log(message);
            return;
        }

        Game.Log.Info(message);
    }

    /// <summary>
    /// Scan <paramref name="instance"/> for hook attributes. Safe to call twice.
    /// </summary>
    public void Register(object instance)
    {
        Hooks.Register(instance);
    }

    /// <summary>Construct <typeparamref name="T"/> and <see cref="Register"/> it.</summary>
    public T Register<T>() where T : class, new()
    {
        var instance = new T();
        Register(instance);
        return instance;
    }
}
