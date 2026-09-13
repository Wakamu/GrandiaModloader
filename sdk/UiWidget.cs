namespace Grandia.Sdk;

/// <summary>One node in a composed menu: box, row, column, label, or item.</summary>
public abstract class UiWidget
{
}

/// <summary>Gold-framed panel. Children are stacked top to bottom.</summary>
public sealed class UiBox : UiWidget
{
    public UiBox(params UiWidget[] children)
        : this(5, children)
    {
    }

    public UiBox(int style, params UiWidget[] children)
    {
        Style = style;
        Children = children ?? [];
    }

    public int Style { get; }

    public IReadOnlyList<UiWidget> Children { get; }
}

/// <summary>Children left to right. Adjacent <see cref="UiItem"/>s get a <c>/</c> between them.</summary>
public sealed class UiRow : UiWidget
{
    public UiRow(params UiWidget[] children)
    {
        Children = children ?? [];
    }

    public IReadOnlyList<UiWidget> Children { get; }
}

/// <summary>Children top to bottom.</summary>
public sealed class UiColumn : UiWidget
{
    public UiColumn(params UiWidget[] children)
    {
        Children = children ?? [];
    }

    public IReadOnlyList<UiWidget> Children { get; }
}

/// <summary>Static text. Not selectable.</summary>
public sealed class UiLabel : UiWidget
{
    public UiLabel(string text)
    {
        Text = text ?? "";
    }

    public string Text { get; }
}

/// <summary>
/// Selectable. A text item in a row is one value (Options: Standard / Reverse).
/// An item with children is one block (Save: name + time/party), whole row highlighted.
/// </summary>
public sealed class UiItem : UiWidget
{
    public UiItem(string text)
    {
        Text = text ?? "";
        Children = [];
    }

    public UiItem(params UiWidget[] children)
    {
        Text = "";
        Children = children ?? [];
    }

    public string Text { get; }

    public IReadOnlyList<UiWidget> Children { get; }
}
