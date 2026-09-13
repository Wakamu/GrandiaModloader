namespace Grandia.Sdk;

/// <summary>
/// Weapon-class ids. Character block +0x74..+0x77, and item record +8
/// (<see cref="ItemEvent.WeaponKind"/>).
/// </summary>
public enum WeaponType
{
    None = 0,
    Dagger = 1,
    Sword = 2,
    Mace = 3,
    Ax = 4,
    Whip = 5,
    Bow = 6,
}
