namespace Grandia.Sdk;

/// <summary>
/// Compose a menu from <see cref="UiBox"/>, <see cref="UiRow"/>, <see cref="UiColumn"/>,
/// <see cref="UiLabel"/>, and <see cref="UiItem"/>. Not a preset Options or Save screen.
/// </summary>
public sealed class GameUi
{
    /// <summary>
    /// Open a composed menu. <paramref name="body"/> is stacked as a column
    /// under <paramref name="title"/>. Poll <see cref="GameMenu.TakeChoice"/>
    /// (item/group index) and <see cref="GameMenu.Option"/> (value in a row of items).
    /// </summary>
    public bool Show(string title, params UiWidget[] body)
    {
        if (Game.Native is null || body is null || body.Length == 0)
        {
            return false;
        }

        var parts = new List<string>();
        foreach (var w in body)
        {
            Write(w, parts);
        }

        var n = CountFocus(body);
        if (n <= 0)
        {
            return false;
        }

        return Game.Native.MenuOpen(Field(title), "\u0003\n" + string.Join('\n', parts), n) > 0;
    }

    internal int FocusCount(params UiWidget[] body) => CountFocus(body);

    private static void Write(UiWidget? w, List<string> parts)
    {
        switch (w)
        {
            case UiColumn c:
                parts.Add("+C");
                foreach (var ch in c.Children)
                {
                    Write(ch, parts);
                }

                parts.Add("-");
                break;
            case UiRow r:
                parts.Add("+R");
                foreach (var ch in r.Children)
                {
                    Write(ch, parts);
                }

                parts.Add("-");
                break;
            case UiBox b:
                parts.Add($"+X\t{b.Style}");
                foreach (var ch in b.Children)
                {
                    Write(ch, parts);
                }

                parts.Add("-");
                break;
            case UiLabel l:
                parts.Add("L\t" + Field(l.Text, GameMenu.MaxMessageLength));
                break;
            case UiItem i when i.Children.Count > 0:
                parts.Add("+I");
                foreach (var ch in i.Children)
                {
                    Write(ch, parts);
                }

                parts.Add("-");
                break;
            case UiItem i:
                parts.Add("I\t" + Field(i.Text, GameMenu.MaxMessageLength));
                break;
        }
    }

    private static int CountFocus(IEnumerable<UiWidget> nodes)
    {
        var n = 0;
        foreach (var w in nodes)
        {
            n += CountFocusNode(w);
        }

        return n;
    }

    private static int CountFocusNode(UiWidget? w)
    {
        switch (w)
        {
            case UiItem i when i.Children.Count > 0:
                return 1;
            case UiItem:
                return 1;
            case UiBox b:
                return CountFocus(b.Children);
            case UiRow r:
            {
                var n = 0;
                var inRun = false;
                foreach (var ch in r.Children)
                {
                    if (ch is UiItem item && item.Children.Count == 0)
                    {
                        if (!inRun)
                        {
                            n++;
                            inRun = true;
                        }
                    }
                    else
                    {
                        inRun = false;
                        n += CountFocusNode(ch);
                    }
                }

                return n;
            }
            case UiColumn c:
                return CountFocus(c.Children);
            default:
                return 0;
        }
    }

    private static string Field(string? text, int max = GameMenu.MaxLabelLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var s = text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        return s.Length <= max ? s : s[..max];
    }
}
