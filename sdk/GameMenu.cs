namespace Grandia.Sdk;

/// <summary>
/// One row on a custom Start→Options list: a label plus two or more values.
/// Left/Right moves the finger cursor onto a value; Confirm picks that value.
/// Yellow text is the vanilla saved setting and is not used here.
/// </summary>
public sealed class MenuRow
{
    public MenuRow(string label, params string[] options)
    {
        Label = label ?? "";
        if (options is { Length: > 0 })
        {
            Options = options;
        }
        else
        {
            Options = ["Yes", "No"];
        }
    }

    public string Label { get; }

    public IReadOnlyList<string> Options { get; }
}

/// <summary>
/// Convenience list (label + values) on the Options host. For a composed
/// screen use <see cref="Game.Ui"/>.<see cref="GameUi.Show"/>.
/// Open from a hook; poll <see cref="TakeChoice"/> / <see cref="TakeCancel"/>.
/// </summary>
public sealed class GameMenu
{
    public const int MaxItems = 12;
    public const int MaxVisible = 8;
    public const int MaxOptions = 8;
    public const int MaxLabelLength = 20;
    public const int MaxMessageLength = 40;

    public bool IsOpen => Game.Native?.MenuIsOpen() > 0;

    /// <summary>Current row 0..n-1 while open; 0 if closed.</summary>
    public int Cursor
    {
        get
        {
            var n = Game.Native?.MenuCursor() ?? 0;
            return n < 0 ? 0 : n;
        }
    }

    /// <summary>
    /// Option the finger cursor is on (0-based), or the option Confirm last picked.
    /// Independent of vanilla yellow / saved Options values.
    /// </summary>
    public int Option
    {
        get
        {
            var n = Game.Native?.MenuOption() ?? 0;
            return n < 0 ? 0 : n;
        }
    }

    /// <summary>
    /// Open a list whose rows are labeled <paramref name="items"/>, each with
    /// Yes / No. Extra rows past <see cref="MaxItems"/> are dropped.
    /// </summary>
    public bool Open(string title, IReadOnlyList<string> items)
    {
        if (items is null || items.Count == 0)
        {
            return false;
        }

        var n = Math.Min(items.Count, MaxItems);
        var rows = new MenuRow[n];
        for (var i = 0; i < n; i++)
        {
            rows[i] = new MenuRow(items[i], "Yes", "No");
        }

        return Open(title, rows);
    }

    public bool Open(string title, params string[] items) => Open(title, (IReadOnlyList<string>)items);

    /// <summary>
    /// Open a list with explicit per-row options. One option still draws;
    /// two use the stock Yes / No columns; more than two cycle with Left/Right.
    /// </summary>
    public bool Open(string title, IReadOnlyList<MenuRow> rows)
    {
        if (Game.Native is null || rows is null || rows.Count == 0)
        {
            return false;
        }

        var n = Math.Min(rows.Count, MaxItems);
        return Game.Native.MenuOpen(Field(title), EncodeRows(rows, n), n) > 0;
    }

    public bool Open(string title, params MenuRow[] rows) => Open(title, (IReadOnlyList<MenuRow>)rows);

    /// <summary>Close without a choice. Does not raise <see cref="TakeCancel"/>.</summary>
    public void Close()
    {
        Game.Native?.MenuClose();
    }

    /// <summary>0-based row if Confirm was pressed since the last take; otherwise null.</summary>
    public int? TakeChoice()
    {
        var n = Game.Native?.MenuTakeChoice() ?? -1;
        return n < 0 ? null : n;
    }

    /// <summary>True once if Back closed the menu since the last take.</summary>
    public bool TakeCancel() => Game.Native?.MenuTakeCancel() > 0;

    private static string EncodeRows(IReadOnlyList<MenuRow> rows, int n)
    {
        var lines = new string[n];
        for (var i = 0; i < n; i++)
        {
            var row = rows[i] ?? new MenuRow("");
            var opts = row.Options ?? Array.Empty<string>();
            var take = Math.Min(opts.Count, MaxOptions);
            if (take <= 0)
            {
                lines[i] = Field(row.Label) + "\tYes\tNo";
                continue;
            }

            var parts = new string[take + 1];
            parts[0] = Field(row.Label);
            for (var o = 0; o < take; o++)
            {
                parts[o + 1] = Field(opts[o]);
            }

            lines[i] = string.Join('\t', parts);
        }

        return string.Join('\n', lines);
    }

    private static string Field(string? text, int max = MaxLabelLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var s = text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        return s.Length <= max ? s : s[..max];
    }
}
