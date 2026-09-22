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
    /// Map enter (first fopen of this visit). MDP/SCN/OFS of the same enter
    /// share one call; leaving and coming back fires again. Mutate
    /// <see cref="MapLoadEvent.Map"/>; do not return a new map. Assembled
    /// scripts and hooks are delivered by redirecting <see cref="OnScriptExecute"/>
    /// / <see cref="OnCallHook"/> — the host does not remap fopen. Scripts,
    /// hooks, alt hooks (<see cref="Map.AltHooks"/>, <c>call_hook N alt</c>),
    /// and zones assemble in-process (no <c>field_tools</c>). Do not
    /// rewrite dest-cam / sec[32]. <see cref="Map.Sfx"/> is sec[29]
    /// positional beds (same lazy hydrate as <see cref="Map.Zones"/>);
    /// mutate / <see cref="Map.AddSfx"/> / <see cref="MapSfx.Remove"/> write
    /// back onto the heap copy before mixer bind.
    /// <see cref="Map.Npcs"/> is sec[8] town talkers (kind 0 or 4);
    /// mutate / <see cref="Map.AddNpc"/> / <see cref="MapNpc.Remove"/>
    /// recopies the 4 KiB instance heap after the field-setup word-copy.
    /// Kind-2 wanderers stay on <see cref="Map.Encounters"/> — do not treat
    /// them as NPCs. Add clones an existing talker's CLUT / flags; it does
    /// not graft a new hdr+4 body.
    /// <see cref="Map.Anims"/> is this map's sec[21] clip directory
    /// (id + raw streams; <see cref="MapAnim.Header"/> /
    /// <see cref="MapAnim.Frames"/> / <see cref="MapAnim.Cues"/>).
    /// Play with <c>anim {id} talk={TalkId} mode=2</c>.
    /// Ids missing there are the shared bank.
    /// <see cref="MapAnim.SetFrames"/> / <see cref="MapAnim.SetCues"/> /
    /// <see cref="Map.AddAnim"/> emit and swap <c>[0x71CAE0]</c> after bind.
    /// <see cref="Map.CameraPaths"/> is this map's sec[15] camera-path
    /// directory (1-based id; <c>camera_path {id}</c> / hook <c>+5</c>).
    /// <see cref="MapCameraPath.Replace"/> / <see cref="MapCameraPath.ToAsm"/>
    /// / <see cref="Map.AddCameraPath(string)"/> use the sec[15] dump
    /// mnemonics (<c>set_pos</c>, <c>wait_b</c>, …). Emit swaps
    /// <c>[0x71A644]</c> after bind. Removed ids stay as
    /// <c>0xFF</c> stubs. <see cref="Map.Camera"/> is this map's
    /// sec[10] camera params (mode 0–3, pitch reset, Select pan AABB,
    /// Select height <see cref="MapSelectPan.Distance"/> (p28, stock 0.25),
    /// minimap clip at +0xE4/+0xE8, follow-cam / proj words). Dirty fields write the live
    /// field-params heap at <c>[0x63FA9C]</c> after the field-setup
    /// word-copy; Select pan also pokes <c>713F44/3E/40/42</c>; Distance
    /// substitutes the Select-enter p28 write (script 0 +0x20).
    /// <see cref="Map.SpriteClips"/> /
    /// <see cref="Map.Poses"/> are this map's sec[23] character sprite
    /// bank (clip id + timed poses; parts carry channel + sprite cookie).
    /// Play with <c>unit_bind {clipId} {talkId}</c> (field_talk).
    /// Read-only — no emit.
    /// <see cref="Map.Textures"/> is the original PS1 TIM (sec[1]/[27]).
    /// Crop a pose part with <c>e.Map.Textures.TryCrop(part, out var tim)</c>
    /// or an HD row with <c>e.Map.TryCrop(e.Map.Sprites.Anim[7], out tim)</c>.
    /// Dump PNGs with <see cref="MapTextureExtract.Write"/>.
    /// <see cref="Map.Sprites"/> is the SoftHD spriteinfo catalog plus
    /// sec[32] UV cells (HD atlas + key). Read-only — no emit.
    /// <see cref="Game.RunScript(int)"/> / <see cref="Game.RunHook(int, int)"/>
    /// queue a stock or custom arm for the next idle field tick.
    /// <see cref="Map.Encounters"/> lists this
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
    /// Call <see cref="CallHookEvent.Replace"/> with a table-2 / table-3
    /// assembler line (e.g. <c>hook 888 setup dest=0xCC15 spawn=1</c>), set a 20-byte
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
    /// After the vanilla 0xE80 slot body is written. Prefer
    /// <see cref="Game.SaveData"/> (shared key → object bag; the host
    /// always writes it, even with no mods). <see cref="SaveEvent.Trailer"/>
    /// is the JSON snapshot at hook start.
    /// </summary>
    void OnSave(SaveEvent e)
    {
    }

    /// <summary>
    /// Slot load. <see cref="LoadPhase.ConfirmPeek"/> is confirm-Yes (set
    /// <see cref="LoadEvent.Allow"/> to veto before Loading). <see cref="LoadPhase.Applied"/>
    /// is after the vanilla body is in RAM — <see cref="Game.SaveData"/> is
    /// already replaced from this slot (<see cref="LoadEvent.Data"/>).
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
    /// Last field enemy just paid out at +0x138790. Once per fight.
    /// <see cref="VictoryEvent.Exp"/> / <see cref="VictoryEvent.Gold"/> /
    /// <see cref="VictoryEvent.Drops"/> write the result pots before the
    /// victory screen. <see cref="VictoryEvent.EncounterRow"/> is the field
    /// wanderer group. Does not fire on flee.
    /// </summary>
    void OnVictory(VictoryEvent e)
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
    /// (up to 16 each) and <see cref="ShopOpenEvent.SetPrice"/> (session
    /// buy-gold). Items added to stock without SetPrice keep catalog
    /// <see cref="ItemEvent.Cost"/>. <see cref="ShopKind.Sell"/> is the sell screen — use
    /// <see cref="ShopOpenEvent.SetSellPrice"/>; inventory is not writable.
    /// Writes the live field-params copy of MDP sec[10] on Buy.
    /// </summary>
    void OnShopOpen(ShopOpenEvent e)
    {
    }

    /// <summary>
    /// About 60 Hz. Poll <see cref="TickEvent.Pad"/> / <see cref="Game.Input"/>
    /// and drive <see cref="Game.Turbo"/>, <see cref="Game.Encounters"/>,
    /// <see cref="Game.Debug"/>, <see cref="Game.Compass"/>, <see cref="Game.Menu"/>, and <see cref="Game.Ui"/>. Set <see cref="TickEvent.BlockGameInput"/> to
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
    /// icon / paras / leftover record bytes write every live sec3 alias.
    /// <see cref="ItemEvent.Name"/> / ShortName / Description patch
    /// <c>TEXT1.BIN</c> in place at the title screen (same file size; a
    /// string that does not fit is skipped). Unused vanilla slots included.
    /// <see cref="ItemEvent.Effect"/> is the skill id used in combat
    /// (Herbs → Heal). <see cref="ItemEvent.WeaponKind"/> is record+8.
    /// <see cref="ItemEvent.Stats"/> / <see cref="ItemEvent.Auto"/> /
    /// <see cref="ItemEvent.AttackRange"/> are the typed para and on-hit
    /// bytes (raw <c>Para*</c> / <c>Unknown8</c> still work). Re-apply on
    /// every open — the game recopies vanilla WINDT each time. SellPrice
    /// defaults to Cost/2 (vanilla shop rule).
    /// </summary>
    void OnItem(ItemEvent e)
    {
    }

    /// <summary>
    /// Party skill catalog (WINDT sec7/sec8 on field menus; STAT/BBG in
    /// battle). Magic and weapon moves. Mutate Power (signed), Cost (MP/SP),
    /// Speed (casting time), Exp (XP per hit), Radius / Distance,
    /// CancelChance, Requirements, CharacterMask / Allow, Effect / Mode
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

    /// <summary>
    /// SoftHD is about to open <c>*__atlas.png</c>,
    /// <c>*__atlas_tables.png</c>, or <c>*__spriteinfo.bin</c>
    /// (fopen / SDL). <see cref="HdTextureEvent.Kind"/>
    /// is the filename token (maps / tenants / anim / mapeff / faces /
    /// party / areamap / logo / title / …). <see cref="HdTextureEvent.Pixels"/> decodes the
    /// PNG only if read. <see cref="HdTextureEvent.Replace"/> /
    /// <see cref="HdTextureEvent.ReplacePixels"/> virt-file the replacement
    /// so SoftHD decodes it. Not MDP Huffman tpages.
    /// </summary>
    void OnHdTexture(HdTextureEvent e)
    {
    }

    /// <summary>
    /// SoftHD inserted one SPRIV row into its live table
    /// (parse of <c>*__spriteinfo.bin</c>). <see cref="HdSpriteMatchEvent.Index"/>
    /// is the row; <see cref="HdSpriteMatchEvent.Rect"/> is atlas xywh when
    /// packed that way. <see cref="HdSpriteMatchEvent.Record"/> is the raw
    /// 8- or 32-byte insert (tenants / maps v4 keep the PS1 match key here).
    /// Observe-only. Not <c>Map.Poses[].SpriteIndex</c>.
    /// </summary>
    void OnHdSpriteMatch(HdSpriteMatchEvent e)
    {
    }

    /// <summary>
    /// SoftHD resolved a live blit to an HD atlas crop
    /// (<c>+0x1B67A</c>). <see cref="HdSpriteDrawEvent.Index"/> is that
    /// row; <see cref="HdSpriteDrawEvent.Rect"/> is the atlas crop;
    /// <see cref="HdSpriteDrawEvent.Live"/> is the UV box; the catalog
    /// key is FNV of the PS1 VRAM texels. 32-byte
    /// tables only. Observe-only; per blit — do not subscribe unless
    /// you need it.
    /// </summary>
    void OnHdSpriteDraw(HdSpriteDrawEvent e)
    {
    }
}
