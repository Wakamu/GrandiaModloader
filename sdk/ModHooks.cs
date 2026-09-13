using System.Reflection;

namespace Grandia.Sdk;

/// <summary>
/// Cached hook table. <see cref="Register"/> scans a type for
/// <see cref="GameHookAttribute"/> methods (any name) and optional
/// <see cref="IMod"/> interface methods.
/// </summary>
public sealed class ModHooks
{
    private readonly Action<string>? _log;
    private readonly HashSet<object> _targets = new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Type, List<HookEntry>> _byEvent = [];

    public ModHooks(Action<string>? log = null)
    {
        _log = log;
    }

    public bool HasAny => _byEvent.Count > 0 && _byEvent.Values.Any(list => list.Count > 0);

    public bool Has<TEvent>() =>
        _byEvent.TryGetValue(typeof(TEvent), out var list) && list.Count > 0;

    public int TargetCount => _targets.Count;

    public void Register(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!_targets.Add(target))
        {
            return;
        }

        BindAttributed(target);
    }

    /// <summary>Legacy <see cref="IMod"/> assemblies with no <see cref="ModAttribute"/>.</summary>
    public void RegisterIMod(IMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (!_targets.Add(mod))
        {
            return;
        }

        BindIMod(mod);
    }

    public void Invoke<TEvent>(string name, TEvent ev)
    {
        if (!_byEvent.TryGetValue(typeof(TEvent), out var list) || list.Count == 0)
        {
            return;
        }

        foreach (var hook in list)
        {
            try
            {
                ((Action<TEvent>)hook.Invoke)(ev);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"{name} {hook.Owner}: {ex.Message}");
            }
        }
    }

    private void BindAttributed(object target)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var method in target.GetType().GetMethods(flags))
        {
            if (method.GetCustomAttribute<GameHookAttribute>(inherit: true) is not { } attr)
            {
                continue;
            }

            AddMethod(target, method, attr.EventType, attr.GetType().Name);
        }
    }

    private void BindIMod(IMod mod)
    {
        AddDelegate<MapLoadEvent>(mod, mod.OnMapLoad);
        AddDelegate<ScriptExecuteEvent>(mod, mod.OnScriptExecute);
        AddDelegate<CallHookEvent>(mod, mod.OnCallHook);
        AddDelegate<EventFlagEvent>(mod, mod.OnEventFlag);
        AddDelegate<ItemAssignUiEvent>(mod, mod.OnItemAssignUi);
        AddDelegate<FieldGoldAddEvent>(mod, mod.OnFieldGoldAdd);
        AddDelegate<WorldMapLoadEvent>(mod, mod.OnWorldMapLoad);
        AddDelegate<WorldMapConfirmEvent>(mod, mod.OnWorldMapConfirm);
        AddDelegate<MapTravelEvent>(mod, mod.OnMapTravel);
        AddDelegate<SaveEvent>(mod, mod.OnSave);
        AddDelegate<LoadEvent>(mod, mod.OnLoad);
        AddDelegate<BattleSetupEvent>(mod, mod.OnBattleSetup);
        AddDelegate<BattleLoadEvent>(mod, mod.OnBattleLoad);
        AddDelegate<VictoryEvent>(mod, mod.OnVictory);
        AddDelegate<MenuOpenEvent>(mod, mod.OnMenuOpen);
        AddDelegate<EnemyLoadedEvent>(mod, mod.OnEnemyLoaded);
        AddDelegate<ShopOpenEvent>(mod, mod.OnShopOpen);
        AddDelegate<TickEvent>(mod, mod.OnTick);
        AddDelegate<TitleScreenEvent>(mod, mod.OnTitleScreen);
        AddDelegate<CharacterEvent>(mod, mod.OnCharacter);
        AddDelegate<ItemEvent>(mod, mod.OnItem);
        AddDelegate<MagicEvent>(mod, mod.OnMagic);
        AddDelegate<DialogueEvent>(mod, mod.OnDialogue);
        const BindingFlags hdFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var hd = mod.GetType().GetMethod(nameof(IMod.OnHdTexture), hdFlags);
        if (hd != null && hd.DeclaringType != typeof(IMod))
        {
            AddDelegate<HdTextureEvent>(mod, mod.OnHdTexture);
        }

        var match = mod.GetType().GetMethod(nameof(IMod.OnHdSpriteMatch), hdFlags);
        if (match != null && match.DeclaringType != typeof(IMod))
        {
            AddDelegate<HdSpriteMatchEvent>(mod, mod.OnHdSpriteMatch);
        }

        var draw = mod.GetType().GetMethod(nameof(IMod.OnHdSpriteDraw), hdFlags);
        if (draw != null && draw.DeclaringType != typeof(IMod))
        {
            AddDelegate<HdSpriteDrawEvent>(mod, mod.OnHdSpriteDraw);
        }
    }

    private void AddMethod(object target, MethodInfo method, Type eventType, string attrName)
    {
        var args = method.GetParameters();
        if (method.ReturnType != typeof(void) || args.Length != 1 ||
            args[0].ParameterType != eventType)
        {
            _log?.Invoke(
                $"skip {target.GetType().Name}.{method.Name}: [{attrName.Replace("Attribute", "")}] needs void ({eventType.Name})");
            return;
        }

        var delType = typeof(Action<>).MakeGenericType(eventType);
        var del = method.IsStatic
            ? method.CreateDelegate(delType)
            : method.CreateDelegate(delType, target);
        Add(eventType, target, del);
    }

    private void AddDelegate<TEvent>(object target, Action<TEvent> invoke)
    {
        Add(typeof(TEvent), target, invoke);
    }

    private void Add(Type eventType, object target, Delegate invoke)
    {
        if (!_byEvent.TryGetValue(eventType, out var list))
        {
            list = [];
            _byEvent[eventType] = list;
        }

        list.Add(new HookEntry(target.GetType().Name, invoke));
    }

    private readonly struct HookEntry
    {
        public HookEntry(string owner, Delegate invoke)
        {
            Owner = owner;
            Invoke = invoke;
        }

        public string Owner { get; }
        public Delegate Invoke { get; }
    }
}
