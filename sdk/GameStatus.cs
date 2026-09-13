namespace Grandia.Sdk;

/// <summary>
/// Live scene from menu mode (<c>+0x31942C</c>) and battle mode (<c>+0x31CD4B</c>).
/// Battle (mode 2/3) wins; world map is menu mode 3; a field menu is mode 2
/// or an open <see cref="Game.Menu"/> / <see cref="Game.Ui"/> session.
/// </summary>
public enum GameStatus
{
    Field = 0,
    Menu = 1,
    WorldMap = 2,
    Battle = 3,
}
