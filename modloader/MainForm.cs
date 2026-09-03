using System.Drawing;
using System.Windows.Forms;

namespace GrandiaModloader;

public sealed class MainForm : Form
{
    private readonly string _configPath;
    private readonly ModStore _store;
    private readonly LaunchService _launch;

    private readonly ListView _mods = new()
    {
        View = View.Details,
        FullRowSelect = true,
        CheckBoxes = true,
        HideSelection = false,
        Dock = DockStyle.Fill,
    };

    private readonly Button _add = new() { Text = "Add mod", AutoSize = true };
    private readonly Button _remove = new() { Text = "Remove", AutoSize = true };
    private readonly Button _open = new() { Text = "Open mods folder", AutoSize = true };
    private readonly Button _up = new() { Text = "Move up", AutoSize = true };
    private readonly Button _down = new() { Text = "Move down", AutoSize = true };
    private readonly Button _settings = new() { Text = "Settings", AutoSize = true };
    private readonly Button _launchBtn = new() { Text = "Launch Grandia", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly RichTextBox _log = new() { ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill };

    public MainForm()
    {
        _configPath = AppConfig.DefaultConfigPath();
        var config = AppConfig.Load(_configPath);
        _store = new ModStore(_configPath, config);
        _launch = new LaunchService(_store);
        _launch.Log += msg => BeginInvoke(() => AppendLog(msg));

        Text = "Grandia Modloader";
        Width = 860;
        Height = 640;
        MinimumSize = new Size(720, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        UiTheme.Apply(this);

        BuildLayout();
        UiTheme.Paint(this);
        RefreshMods();
        UpdateStatus();

        _add.Click += (_, _) => AddMod();
        _remove.Click += (_, _) => RemoveSelected();
        _open.Click += (_, _) => OpenSelected();
        _up.Click += (_, _) => MoveSelected(-1);
        _down.Click += (_, _) => MoveSelected(1);
        _settings.Click += (_, _) => OpenSettings();
        _launchBtn.Click += async (_, _) => await LaunchAsync();
        _cancel.Click += (_, _) => _launch.Cancel();
        _mods.ItemChecked += ModsOnItemChecked;
        FormClosing += (_, _) =>
        {
            if (_launch.IsBusy)
            {
                _launch.Cancel();
            }
        };
    }

    private void BuildLayout()
    {
        _mods.Columns.Add("Mod", 200);
        _mods.Columns.Add("Version", 80);
        _mods.Columns.Add("Description", 480);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        var title = new Label
        {
            Text = "Grandia Modloader",
            Font = new Font("Georgia", 18f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 8),
        };
        buttons.Controls.Add(_add);
        buttons.Controls.Add(_remove);
        buttons.Controls.Add(_open);
        buttons.Controls.Add(_up);
        buttons.Controls.Add(_down);
        buttons.Controls.Add(_settings);
        buttons.Controls.Add(_launchBtn);
        buttons.Controls.Add(_cancel);

        var header = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1 };
        header.Controls.Add(title);
        header.Controls.Add(buttons);
        header.Controls.Add(_status);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_mods, 0, 1);
        var logLabel = new Label { Text = "Log", AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
        root.Controls.Add(logLabel, 0, 2);
        root.Controls.Add(_log, 0, 3);
        Controls.Add(root);
    }

    private void RefreshMods()
    {
        _mods.ItemChecked -= ModsOnItemChecked;
        _mods.Items.Clear();
        foreach (var mod in _store.ListMods())
        {
            var item = new ListViewItem(mod.Name) { Tag = mod.Id, Checked = mod.Enabled };
            item.SubItems.Add(mod.Version);
            item.SubItems.Add(mod.Description);
            _mods.Items.Add(item);
        }

        _mods.ItemChecked += ModsOnItemChecked;
        UpdateStatus();
    }

    private void ModsOnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (e.Item.Tag is string id)
        {
            _store.SetEnabled(id, e.Item.Checked);
        }
    }

    private void AddMod()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Add a compiled mod DLL",
            Filter = "Mod assemblies (*.dll)|*.dll|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        var fileResult = dlg.ShowDialog(this);
        try
        {
            if (fileResult == DialogResult.OK)
            {
                _store.AddFromPath(dlg.FileName);
                RefreshMods();
                AppendLog($"Added {dlg.FileName}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Add mod", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string? SelectedId()
    {
        if (_mods.SelectedItems.Count == 0)
        {
            return null;
        }

        return _mods.SelectedItems[0].Tag as string;
    }

    private int SelectedIndex() => _mods.SelectedIndices.Count == 0 ? -1 : _mods.SelectedIndices[0];

    private void RemoveSelected()
    {
        var id = SelectedId();
        if (id is null)
        {
            return;
        }

        if (MessageBox.Show(this, $"Remove '{id}.dll' from the mods folder?", "Remove",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _store.Remove(id);
        RefreshMods();
        AppendLog($"Removed {id}");
    }

    private void OpenSelected()
    {
        Directory.CreateDirectory(Paths.ModsDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Paths.ModsDir,
            UseShellExecute = true,
        });
    }

    private void MoveSelected(int delta)
    {
        var index = SelectedIndex();
        if (index < 0)
        {
            return;
        }

        _store.Move(index, delta);
        RefreshMods();
        var next = Math.Clamp(index + delta, 0, Math.Max(0, _mods.Items.Count - 1));
        if (_mods.Items.Count > 0)
        {
            _mods.Items[next].Selected = true;
        }
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_store.Config);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _store.SaveConfig();
        UpdateStatus();
        AppendLog("Settings saved.");
    }

    private async Task LaunchAsync()
    {
        SetBusy(true);
        try
        {
            await _launch.LaunchAsync();
        }
        catch (OperationCanceledException)
        {
            AppendLog("Launch cancelled.");
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message);
            MessageBox.Show(this, ex.Message, "Launch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
            RefreshMods();
        }
    }

    private void SetBusy(bool busy)
    {
        _launchBtn.Enabled = !busy;
        _cancel.Enabled = busy;
        _add.Enabled = !busy;
        _remove.Enabled = !busy;
        _up.Enabled = !busy;
        _down.Enabled = !busy;
        _settings.Enabled = !busy;
    }

    private void UpdateStatus()
    {
        var cfg = _store.Config;
        var mode = string.Equals(cfg.LaunchMode, "exe", StringComparison.OrdinalIgnoreCase) ? "grandia.exe" : "Steam";
        var enabled = _store.ListMods().Count(m => m.Enabled);
        _status.ForeColor = UiTheme.Muted;
        _status.Text = $"{enabled} enabled · launch via {mode} · mods {Paths.ModsDir}";
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = UiTheme.Text;
        _log.AppendText(line);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}
