namespace Grandia.Sdk;

/// <summary>
/// Legacy single-class contract. Prefer <see cref="ModAttribute"/> +
/// <see cref="InitAttribute"/> and hook attributes (<see cref="OnTickAttribute"/>,
/// …) on separate classes registered from <c>Init</c>.
/// Assemblies with no <see cref="ModAttribute"/> still load every exported
/// <see cref="IMod"/>. Mutate event objects in place.
/// </summary>
public interface IMod
{
    /// <summary>
    /// Game is <c>fopen</c>ing an MDP/SCN/OFS (assemble signal). Mutate
    /// <see cref="MapLoadEvent.Map"/>; do not return a new map. Assembled
    /// scripts and hooks are delivered by redirecting <see cref="OnScriptExecute"/>
    /// / <see cref="OnCallHook"/> — the host does not remap fopen. Scripts,
    /// hooks, and zones assemble in-process (no <c>field_tools</c>). Do not
    /// rewrite dest-cam / sec[32]. <see cref="Map.Encounters"/> lists this
    /// map's scripted fights (handler 0x19 / <c>scripted_battle</c>) and
    /// field wanderers (sec[30] + sec[8] kind 2).
    /// </summary>
    void OnMapLoad(MapLoadEvent e);

    /// <summary>
    /// Script id → IP at <c>+0x6F0B0</c> (once per arm, not per opcode).
    /// Call <see cref="ScriptExecuteEvent.Replace"/> or mutate
    /// <see cref="ScriptExecuteEvent.Script"/> to stop vanilla and run a
    /// new assembler script. <see cref="Script.Lines"/> decodes on first
    /// read; assemble is in-process C# only after a write, and applies to
    /// this arm only. Prefills <see cref="ScriptExecuteEvent.Bytecode"/>
    /// from OnMapLoad (those stay until the map is assembled again).
    /// <see cref="ScriptExecuteEvent.Skip"/> drops the script.
    /// </summary>
    void OnScriptExecute(ScriptExecuteEvent e)
    {
    }

    /// <summary>
    /// <c>call_hook</c> lookup at <c>+0x53560</c>. Prefills
    /// <see cref="CallHookEvent.Row"/> from this map's OnMapLoad hook rows.
    /// Call <see cref="CallHookEvent.Replace"/> with a table-2 assembler line
    /// (e.g. <c>hook 888 setup dest=0xCC15 spawn=1</c>), set a 20-byte
    /// <see cref="CallHookEvent.Row"/>, or <see cref="CallHookEvent.Skip"/> to drop.
    /// Assemble is in-process C# (no <c>field_tools</c>).
    /// </summary>
    void OnCallHook(CallHookEvent e)
    {
    }

    /// <summary>
    /// Progress flag write (chest loot or story script). Map-init bulk callers
    /// are filtered in the host and never reach this method.
    /// </summary>
    void OnEventFlag(EventFlagEvent e)
    {
    }

    /// <summary>
    /// Field chest is about to show the assign-item UI. Set
    /// <see cref="ItemAssignUiEvent.SkipVanilla"/> to keep the item out of party bags.
    /// </summary>
    void OnItemAssignUi(ItemAssignUiEvent e)
    {
    }

    /// <summary>
    /// Field chest is adding gold. Set <see cref="FieldGoldAddEvent.Amount"/> to 0
    /// to suppress the vanilla payout.
    /// </summary>
    void OnFieldGoldAdd(FieldGoldAddEvent e)
    {
    }

    /// <summary>
    /// Area map open at +0x59320. <see cref="WorldMapLoadEvent.Destinations"/>
    /// is the icons on this set (up to 32). One icon can travel to more than
    /// one map (Lama North vs South) from <see cref="WorldMapLoadEvent.OriginContext"/>.
    /// Add / remove / set <see cref="WorldMapDestination.Revealed"/>,
    /// <see cref="WorldMapDestination.Accessible"/> (grey + no cursor when false),
    /// <see cref="WorldMapDestination.Picture"/> (stock icon art to reuse),
    /// or <see cref="WorldMapDestination.SetPicture"/> (PNG as its own
    /// HD nameplate; optional width/height in area-map screen units).
    /// An empty hook does not rewrite tables. Confirm is still
    /// <see cref="OnWorldMapConfirm"/>.
    /// </summary>
    void OnWorldMapLoad(WorldMapLoadEvent e)
    {
    }

    /// <summary>
    /// World-map confirm at +0x58491, dest at +0x2C2990. Default is allow.
    /// Complements <see cref="OnMapLoad"/> (this is the gate; fopen only
    /// schedules assemble, and bind applies the live RAM patch).
    /// Do not hook dest-arrival / travel writers from here.
    /// </summary>
    void OnWorldMapConfirm(WorldMapConfirmEvent e)
    {
    }

    /// <summary>
    /// Field door / setup warp (before the auto-walk), field world-map
    /// exit (<see cref="MapTravelKind.WorldMapOpen"/>), or world-map
    /// confirm dest commit. <see cref="MapTravelEvent.From"/> is the
    /// current map; <see cref="MapTravelEvent.Destination"/> and
    /// <see cref="MapTravelEvent.Spawn"/> are writable. On
    /// <see cref="MapTravelKind.WorldMapOpen"/> dest starts at 0 (open
    /// the area map); set a map id to skip the AMAP and dest-load that
    /// field. Set <see cref="MapTravelEvent.Allow"/> to false to stay on
    /// this map without locking the party walk. Cancel a pin confirm with
    /// <see cref="OnWorldMapConfirm"/> instead.
    /// </summary>
    void OnMapTravel(MapTravelEvent e)
    {
    }

    /// <summary>
    /// After the vanilla 0xE80 slot body is written. Set <see cref="SaveEvent.Trailer"/>
    /// to persist extra bytes (GMOD envelope after the body). Start value is the last
    /// loaded trailer so later mods see earlier writes.
    /// </summary>
    void OnSave(SaveEvent e)
    {
    }

    /// <summary>
    /// Slot load. <see cref="LoadPhase.ConfirmPeek"/> is confirm-Yes (set
    /// <see cref="LoadEvent.Allow"/> to veto before Loading). <see cref="LoadPhase.Applied"/>
    /// is after the vanilla body is in RAM — restore from <see cref="LoadEvent.Trailer"/> here.
    /// Save-list preview does not raise this.
    /// </summary>
    void OnLoad(LoadEvent e)
    {
    }

    /// <summary>
    /// Battle setup at +0x12B070, before table dispatch and B00x stage/BGM.
    /// <see cref="BattleSetupEvent.EncounterTable"/> is stage setup, not monsters.
    /// Leave <see cref="BattleSetupEvent.EncounterRow"/> as the field group.
    /// Party staging is <see cref="OnBattleLoad"/>.
    /// </summary>
    void OnBattleSetup(BattleSetupEvent e)
    {
    }

    /// <summary>
    /// Battle context init at +0x12BDD0, before ally pack/spawn. Mutate
    /// <see cref="BattleLoadEvent.Party"/> to stage a roster for this fight only.
    /// The next fight starts from the field roster again (or <see cref="GameParty.SetIds"/>).
    /// Table dispatch already ran — use <see cref="OnBattleSetup"/> to change
    /// <see cref="BattleSetupEvent.EncounterTable"/>. Same-map slot swaps can still
    /// use <see cref="BattleLoadEvent.SetEnemies"/> (one group, or several
    /// <see cref="EnemyGroup"/>s for mixed species). Row is the field group.
    /// Background and battle BGM are already playing and are not writable here.
    /// The host snapshots field MapObj+0x0A and restores it after combat.
    /// Do not write field MapObj+0x0A yourself; do not call +0x54F10.
    /// </summary>
    void OnBattleLoad(BattleLoadEvent e)
    {
    }

    /// <summary>
    /// Status, stash, or item-assign is filling its party cache. Mutate
    /// <see cref="MenuOpenEvent.Party"/> for this menu only. Does not change
    /// field MapObj+0x0A or the battle roster. Do not call +0x54F10.
    /// </summary>
    void OnMenuOpen(MenuOpenEvent e)
    {
    }

    /// <summary>
    /// First combatant of each enemy species after +0x142B00 / +0x142D40.
    /// Once per type per battle. Mutate stats / drops / resists / skills in
    /// place; later copies of the same species inherit the shared model.
    /// </summary>
    void OnEnemyLoaded(EnemyLoadedEvent e)
    {
    }

    /// <summary>
    /// Shop UI at +0x1E93A0, before the list is built.
    /// <see cref="ShopKind.Buy"/> is an item shop; <see cref="ShopKind.Magic"/>
    /// is the Mana Egg tutor. Mutate <see cref="ShopOpenEvent.Weapons"/> /
    /// <see cref="ShopOpenEvent.Armor"/> / <see cref="ShopOpenEvent.Goods"/>
    /// (up to 16 each) and <see cref="ShopOpenEvent.Prices"/> (WINDT catalog
    /// buy-gold). <see cref="ShopKind.Sell"/> is the sell screen — use
    /// <see cref="ShopOpenEvent.SetSellPrice"/>; inventory is not writable.
    /// Writes the live field-params copy of MDP sec[10] on Buy.
    /// </summary>
    void OnShopOpen(ShopOpenEvent e)
    {
    }

    /// <summary>
    /// About 60 Hz. Poll <see cref="TickEvent.Pad"/> / <see cref="Game.Input"/>
    /// and drive <see cref="Game.Turbo"/>, <see cref="Game.Encounters"/>, and
    /// <see cref="Game.Debug"/>. Set <see cref="TickEvent.BlockGameInput"/> to
    /// swallow this pad update so the game does not walk, open pause,
    /// or move the title New Game / Continue / Options cursor.
    /// </summary>
    void OnTick(TickEvent e)
    {
    }

    /// <summary>
    /// Press Start title at +0x7700 (TITLE.DAT). Once per appearance
    /// (boot and return-to-title). Not the BA38 cinematic title card.
    /// </summary>
    void OnTitleScreen(TitleScreenEvent e)
    {
    }

    /// <summary>
    /// Playable char block at MapObj+0x10C (ids 1–8). After a slot load
    /// copies into MapObj, and once on new game when Justin’s level is
    /// live. Mutate stats / <see cref="CharacterEvent.Learned"/>.
    /// Do not write MapObj+0x0A; do not call +0x54F10.
    /// </summary>
    void OnCharacter(CharacterEvent e)
    {
    }

    /// <summary>
    /// WINDT sec3 item record after each status / shop / stash load
    /// publishes sec3 (before the shop bakes prices). Cost / SellPrice /
    /// icon / paras write every live sec3 alias. <see cref="ItemEvent.Effect"/>
    /// is the skill id used in combat (Herbs → Heal). Re-apply on every open —
    /// the game recopies vanilla WINDT each time. SellPrice defaults to
    /// Cost/2 (vanilla shop rule).
    /// </summary>
    void OnItem(ItemEvent e)
    {
    }

    /// <summary>
    /// Party skill catalog (WINDT sec7/sec8 on field menus; STAT/BBG in
    /// battle). Magic and weapon moves. Mutate Power, Cost (MP/SP), IpCost
    /// (IP gauge), Requirements, CharacterMask / Allow, Effect / Mode
    /// (<see cref="HealMode"/>, <see cref="StatusAilment"/>, …).
    /// Re-applied on every menu WINDT load. Grant already-learned bits on
    /// <see cref="OnCharacter"/>.
    /// </summary>
    void OnMagic(MagicEvent e)
    {
    }

    /// <summary>
    /// Field VM is about to display a type-1 / type-8 dialogue op
    /// (dispatch at <c>+0x6F174</c>). <see cref="DialogueEvent.Markup"/> is
    /// the editor string (decoded on first read, encoded only if assigned).
    /// Assign a new value to replace this showing, or set
    /// <see cref="DialogueEvent.Skip"/> to drop the opcode and continue
    /// the script. In-process — no <c>field_tools</c>.
    /// </summary>
    void OnDialogue(DialogueEvent e)
    {
    }
}
