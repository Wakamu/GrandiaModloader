using System.Drawing;
using System.Windows.Forms;

namespace GrandiaModloader;

internal static class UiTheme
{
    public static readonly Color Window = Color.FromArgb(22, 22, 24);
    public static readonly Color Panel = Color.FromArgb(32, 32, 36);
    public static readonly Color Surface = Color.FromArgb(16, 16, 18);
    public static readonly Color Border = Color.FromArgb(62, 62, 68);
    public static readonly Color Text = Color.FromArgb(226, 224, 218);
    public static readonly Color Muted = Color.FromArgb(148, 146, 140);
    public static readonly Color Accent = Color.FromArgb(204, 168, 92);
    public static readonly Color Button = Color.FromArgb(46, 46, 52);
    public static readonly Color ButtonHot = Color.FromArgb(62, 62, 70);
    public static readonly Color Launch = Color.FromArgb(48, 86, 72);

    public static void Apply(Form form)
    {
        form.BackColor = Window;
        form.ForeColor = Text;
        var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (icon is not null)
        {
            form.Icon = icon;
        }

        Paint(form);
    }

    public static void Paint(Control root)
    {
        Style(root);
        foreach (Control child in root.Controls)
        {
            Paint(child);
        }
    }

    public static void StyleListView(ListView list)
    {
        list.BackColor = Surface;
        list.ForeColor = Text;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.OwnerDraw = true;
        list.DrawColumnHeader -= DrawColumnHeader;
        list.DrawItem -= DrawItem;
        list.DrawSubItem -= DrawSubItem;
        list.DrawColumnHeader += DrawColumnHeader;
        list.DrawItem += DrawItem;
        list.DrawSubItem += DrawSubItem;
    }

    public static void StyleLog(RichTextBox log)
    {
        log.BackColor = Surface;
        log.ForeColor = Text;
        log.BorderStyle = BorderStyle.FixedSingle;
        log.SelectionColor = Text;
    }

    private static void Style(Control control)
    {
        switch (control)
        {
            case Button button:
                StyleButton(button, button.Text is "Launch Grandia" or "OK");
                break;
            case TextBox box:
                box.BackColor = Surface;
                box.ForeColor = Text;
                box.BorderStyle = BorderStyle.FixedSingle;
                break;
            case RichTextBox log:
                StyleLog(log);
                break;
            case ListView list:
                StyleListView(list);
                break;
            case ComboBox combo:
                StyleCombo(combo);
                break;
            case NumericUpDown numeric:
                numeric.BackColor = Surface;
                numeric.ForeColor = Text;
                numeric.BorderStyle = BorderStyle.FixedSingle;
                break;
            case LinkLabel link:
                link.LinkColor = Accent;
                link.ActiveLinkColor = Text;
                link.VisitedLinkColor = Accent;
                link.ForeColor = Accent;
                link.BackColor = Color.Transparent;
                break;
            case CheckBox box:
                box.ForeColor = Text;
                box.BackColor = Color.Transparent;
                break;
            case Label label:
                label.ForeColor = label.Font.Size >= 16f ? Accent : Text;
                label.BackColor = Color.Transparent;
                break;
            case TableLayoutPanel or FlowLayoutPanel:
                control.BackColor = Window;
                control.ForeColor = Text;
                break;
        }
    }

    private static void StyleButton(Button button, bool accent)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = ButtonHot;
        button.FlatAppearance.MouseDownBackColor = Panel;
        button.UseVisualStyleBackColor = false;
        button.BackColor = accent ? Launch : Button;
        button.ForeColor = Text;
        button.Cursor = Cursors.Hand;
        button.Padding = new Padding(10, 3, 10, 3);
    }

    private static void StyleCombo(ComboBox combo)
    {
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Surface;
        combo.ForeColor = Text;
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.DrawItem -= DrawComboItem;
        combo.DrawItem += DrawComboItem;
    }

    private static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(selected ? ButtonHot : Surface);
        e.Graphics.FillRectangle(bg, e.Bounds);
        if (e.Index >= 0 && e.Index < combo.Items.Count)
        {
            TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString() ?? "", combo.Font, e.Bounds,
                Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    private static void DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var bg = new SolidBrush(Panel);
        e.Graphics.FillRectangle(bg, e.Bounds);
        using var pen = new Pen(Border);
        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", e.Font, e.Bounds, Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private static void DrawItem(object? sender, DrawListViewItemEventArgs e) => e.DrawDefault = true;

    private static void DrawSubItem(object? sender, DrawListViewSubItemEventArgs e) => e.DrawDefault = true;
}
