namespace Grandia.Sdk;

/// <summary>Base for game-event hook attributes. Method name is free.</summary>
public abstract class GameHookAttribute : Attribute
{
    internal GameHookAttribute(Type eventType)
    {
        EventType = eventType;
    }

    public Type EventType { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnMapLoadAttribute : GameHookAttribute
{
    public OnMapLoadAttribute() : base(typeof(MapLoadEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnScriptExecuteAttribute : GameHookAttribute
{
    public OnScriptExecuteAttribute() : base(typeof(ScriptExecuteEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnCallHookAttribute : GameHookAttribute
{
    public OnCallHookAttribute() : base(typeof(CallHookEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnEventFlagAttribute : GameHookAttribute
{
    public OnEventFlagAttribute() : base(typeof(EventFlagEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnItemAssignUiAttribute : GameHookAttribute
{
    public OnItemAssignUiAttribute() : base(typeof(ItemAssignUiEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnFieldGoldAddAttribute : GameHookAttribute
{
    public OnFieldGoldAddAttribute() : base(typeof(FieldGoldAddEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnWorldMapLoadAttribute : GameHookAttribute
{
    public OnWorldMapLoadAttribute() : base(typeof(WorldMapLoadEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnWorldMapConfirmAttribute : GameHookAttribute
{
    public OnWorldMapConfirmAttribute() : base(typeof(WorldMapConfirmEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnMapTravelAttribute : GameHookAttribute
{
    public OnMapTravelAttribute() : base(typeof(MapTravelEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnSaveAttribute : GameHookAttribute
{
    public OnSaveAttribute() : base(typeof(SaveEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnLoadAttribute : GameHookAttribute
{
    public OnLoadAttribute() : base(typeof(LoadEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnBattleSetupAttribute : GameHookAttribute
{
    public OnBattleSetupAttribute() : base(typeof(BattleSetupEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnBattleLoadAttribute : GameHookAttribute
{
    public OnBattleLoadAttribute() : base(typeof(BattleLoadEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnMenuOpenAttribute : GameHookAttribute
{
    public OnMenuOpenAttribute() : base(typeof(MenuOpenEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnEnemyLoadedAttribute : GameHookAttribute
{
    public OnEnemyLoadedAttribute() : base(typeof(EnemyLoadedEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnShopOpenAttribute : GameHookAttribute
{
    public OnShopOpenAttribute() : base(typeof(ShopOpenEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnTickAttribute : GameHookAttribute
{
    public OnTickAttribute() : base(typeof(TickEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnTitleScreenAttribute : GameHookAttribute
{
    public OnTitleScreenAttribute() : base(typeof(TitleScreenEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnCharacterAttribute : GameHookAttribute
{
    public OnCharacterAttribute() : base(typeof(CharacterEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnItemAttribute : GameHookAttribute
{
    public OnItemAttribute() : base(typeof(ItemEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnMagicAttribute : GameHookAttribute
{
    public OnMagicAttribute() : base(typeof(MagicEvent))
    {
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnDialogueAttribute : GameHookAttribute
{
    public OnDialogueAttribute() : base(typeof(DialogueEvent))
    {
    }
}
