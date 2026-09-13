using System.Drawing;
using System.Windows.Forms;

namespace GrandiaModloader;

public sealed class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly ComboBox _launch = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _appId = new() { Minimum = 1, Maximum = 99_999_999, Dock = DockStyle.Fill };
    private readonly TextBox _install = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _updates = new()
    {
        Text = "Check GitHub for updates on startup",
        AutoSize = true,
        Dock = DockStyle.Fill,
    };

    public SettingsForm(AppConfig config)
    {
        _config = config;
        Text = "Settings";
        Width = 640;
        Height = 280;
        MinimumSize = new Size(520, 260);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9f);

        _launch.Items.Add("Steam (steam://rungameid)");
        _launch.Items.Add("grandia.exe");
        _launch.SelectedIndex = string.Equals(config.LaunchMode, "exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _appId.Value = Math.Clamp(config.SteamAppId <= 0 ? AppConfig.DefaultSteamAppId : config.SteamAppId, 1, 99_999_999);
        _install.Text = config.InstallDir;
        _updates.Checked = config.CheckForUpdates;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            Padding = new Padding(12),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        AddRow(grid, 0, "Launch", _launch, null);
        AddRow(grid, 1, "Steam app id", _appId, null);
        AddRow(grid, 2, "Install folder", _install, BrowseInstall);
        AddRow(grid, 3, "Updates", _updates, null);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => Apply();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        grid.Controls.Add(buttons, 0, 4);
        grid.SetColumnSpan(buttons, 3);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(grid);
        UiTheme.Apply(this);
    }

    private static void AddRow(TableLayoutPanel grid, int row, string label, Control field, Action? browse)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        grid.Controls.Add(field, 1, row);
        if (browse is null)
        {
            grid.Controls.Add(new Label { Text = "" }, 2, row);
            return;
        }

        var btn = new Button { Text = "…", Dock = DockStyle.Fill };
        btn.Click += (_, _) => browse();
        grid.Controls.Add(btn, 2, row);
    }

    private void BrowseInstall()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Grandia HD folder (grandia.exe + content/)",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _install.Text = dlg.SelectedPath;
        }
    }

    private void Apply()
    {
        _config.LaunchMode = _launch.SelectedIndex == 1 ? "exe" : "steam";
        _config.SteamAppId = (int)_appId.Value;
        _config.InstallDir = _install.Text.Trim();
        _config.CheckForUpdates = _updates.Checked;
    }
}
