namespace Grandia.Sdk;

/// <summary>
/// One enemy <em>type</em> after the first combatant of that species is
/// bound (+0x142B00 / +0x142D40). Fires once per species per battle.
/// Mutations write the shared model, so later copies of the same enemy
/// inherit them without a second callback.
/// </summary>
public sealed class EnemyLoadedEvent
{
    public EnemyLoadedEvent(int actorId, int catalog, int formRow, int level, int hp, int maxHp,
        int str, int vit, int wit, int agi, int exp, int gold)
    {
        ActorId = actorId;
        Catalog = catalog;
        FormRow = formRow;
        Level = level;
        Hp = hp;
        MaxHp = maxHp;
        Str = str;
        Vit = vit;
        Wit = wit;
        Agi = agi;
        Exp = exp;
        Gold = gold;
        Drops = [new EnemyDrop(), new EnemyDrop()];
        Skills = [];
    }

    /// <summary>Combatant id (actor+4).</summary>
    public int ActorId { get; }

    /// <summary>Species-map slot at actor+0x189 (1-based catalog index).</summary>
    public int Catalog { get; }

    /// <summary>M_DAT form-row at actor+0x15A (same id as <see cref="Species"/>).</summary>
    public int FormRow { get; }

    public Species Species => (Species)FormRow;

    /// <summary>Actor+0x10E (model header byte 1). +0x10F is the species/id byte.</summary>
    public int Level { get; set; }

    /// <summary>Current HP at actor+0x100.</summary>
    public int Hp { get; set; }

    /// <summary>Max HP at actor+0x102 (copied onto current HP at +0x142B00).</summary>
    public int MaxHp { get; set; }

    /// <summary>Actor+0x106 (model header +4).</summary>
    public int Str { get; set; }

    /// <summary>Actor+0x108 (model header +6).</summary>
    public int Vit { get; set; }

    /// <summary>Actor+0x10A (model header +8).</summary>
    public int Wit { get; set; }

    /// <summary>Actor+0x10C (model header +0xA).</summary>
    public int Agi { get; set; }

    /// <summary>Actor+0x17E (model header +0xE). Summed into ctx+0x88 at +0x1387BC.</summary>
    public int Exp { get; set; }

    /// <summary>Actor+0x180 (model header +0x10). Summed into ctx+0x8C at +0x138A0D.</summary>
    public int Gold { get; set; }

    /// <summary>Basic-attack count at actor+0x18C (model +0x22).</summary>
    public int AttackCount { get; set; }

    /// <summary>Basic-attack range at actor+0x18D (model +0x23).</summary>
    public int AttackRange { get; set; }

    /// <summary>Two drop slots (item + %). Vanilla Marna is empty; Green Slime is Herbs @ 15.</summary>
    public EnemyDrop[] Drops { get; }

    /// <summary>Fire resist 0–15 (high nibble of actor+0x13E / model+0x2C).</summary>
    public int FireResist { get; set; }

    /// <summary>Water resist 0–15 (low nibble of actor+0x13E / model+0x2C).</summary>
    public int WaterResist { get; set; }

    /// <summary>Wind resist 0–15 (high nibble of actor+0x13F / model+0x2D).</summary>
    public int WindResist { get; set; }

    /// <summary>Earth resist 0–15 (low nibble of actor+0x13F / model+0x2D).</summary>
    public int EarthResist { get; set; }

    /// <summary>Skills parsed from the model after the name block. Edit in place; new slots are ignored.</summary>
    public List<EnemySkill> Skills { get; }
}
