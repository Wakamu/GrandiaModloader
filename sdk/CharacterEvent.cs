namespace Grandia.Sdk;

/// <summary>
/// One playable character after the MapObj+0x10C block is live.
/// Fires for ids 1–8 after a slot load copies into MapObj, and once
/// on new game when Justin’s level is live. Mutate stats and <see cref="Learned"/> in
/// place. Do not write field MapObj+0x0A; do not call +0x54F10.
/// </summary>
public sealed class CharacterEvent
{
    public const int WeaponSlotCount = 4;

    public CharacterEvent(int id, int level, int hp, int maxHp, int sp, int maxSp,
        int str, int vit, int wit, int agi, int exp, int fire, int water, int wind, int earth,
        int mp1, int mp2, int mp3)
    {
        Id = (CharacterId)id;
        Level = level;
        Hp = hp;
        MaxHp = maxHp;
        Sp = sp;
        MaxSp = maxSp;
        Str = str;
        Vit = vit;
        Wit = wit;
        Agi = agi;
        Exp = exp;
        Fire = fire;
        Water = water;
        Wind = wind;
        Earth = earth;
        Mp1 = mp1;
        Mp2 = mp2;
        Mp3 = mp3;
        WeaponLevels = new int[WeaponSlotCount];
        WeaponTypes = new WeaponType[WeaponSlotCount];
        Learned = [];
    }

    public CharacterId Id { get; }

    /// <summary>Byte at block+0x03.</summary>
    public int Level { get; set; }

    /// <summary>Current HP at +0x0C.</summary>
    public int Hp { get; set; }

    /// <summary>Max HP at +0x0A.</summary>
    public int MaxHp { get; set; }

    /// <summary>Current SP at +0x18.</summary>
    public int Sp { get; set; }

    /// <summary>Max SP at +0x16.</summary>
    public int MaxSp { get; set; }

    /// <summary>+0x0E.</summary>
    public int Str { get; set; }

    /// <summary>+0x10.</summary>
    public int Vit { get; set; }

    /// <summary>+0x12.</summary>
    public int Wit { get; set; }

    /// <summary>+0x14.</summary>
    public int Agi { get; set; }

    /// <summary>Total EXP at +0x34.</summary>
    public int Exp { get; set; }

    /// <summary>Fire magic level at +0x2C.</summary>
    public int Fire { get; set; }

    /// <summary>Water magic level at +0x2D.</summary>
    public int Water { get; set; }

    /// <summary>Wind magic level at +0x2E.</summary>
    public int Wind { get; set; }

    /// <summary>Earth magic level at +0x2F.</summary>
    public int Earth { get; set; }

    /// <summary>MP1 cur/max at +0x3C/+0x3D (both written to this value).</summary>
    public int Mp1 { get; set; }

    /// <summary>MP2 cur/max at +0x3E/+0x3F.</summary>
    public int Mp2 { get; set; }

    /// <summary>MP3 cur/max at +0x40/+0x41.</summary>
    public int Mp3 { get; set; }

    /// <summary>Four weapon levels at +0x30..+0x33. Length 4; extra slots ignored.</summary>
    public int[] WeaponLevels { get; }

    /// <summary>Four weapon types at +0x74..+0x77. Length 4; extra slots ignored.</summary>
    public WeaponType[] WeaponTypes { get; }

    /// <summary>
    /// Learned skill ids (1..127) from MapObj+0x50C+id, bit (char_id-1).
    /// Use <see cref="Learn"/> / <see cref="Forget"/> for <see cref="Skill"/>.
    /// </summary>
    public HashSet<int> Learned { get; }

    public bool Knows(Skill skill) => Learned.Contains((int)skill);

    public void Learn(Skill skill) => Learned.Add((int)skill);

    public void Forget(Skill skill) => Learned.Remove((int)skill);
}
