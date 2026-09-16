using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using PersonalVault.Storage;

namespace PersonalVault.Forms;

/// <summary>
/// Edits the non-secret preferences in AppSettings (auto-lock timeout, due-date
/// reminder windows, start-with-Windows) that previously required hand-editing
/// settings.json. Mutates the passed-in AppSettings in place on Save, same pattern as
/// the other edit forms; the caller is responsible for calling AppSettings.Save().
///
/// Also shows - read-only, "no hidden stuff" - exactly where your data actually lives
/// on disk and whether Google Drive is connected, since none of that is visible
/// anywhere else in the UI besides the tray menu.
/// </summary>
public class SettingsForm : Form
{
    private readonly AppSettings _settings;

    private readonly NumericUpDown _autoLockBox = new() { Minimum = 0, Maximum = 240, Dock = DockStyle.Fill };
    private readonly TextBox _reminderDaysBox = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _startWithWindowsBox = new() { Text = "Start with Windows", AutoSize = true };

    public SettingsForm(AppSettings settings, string vaultFilePath, string credentialsFilePath, bool isDriveConnected)
    {
        _settings = settings;

        Text = "Settings";
        Width = 500;
        Height = 485;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        AddRow(layout, ref row, "Auto-lock after (minutes, 0 = off):", _autoLockBox);
        AddRow(layout, ref row, "Remind before due date\n(comma-separated days):", _reminderDaysBox);
        AddRow(layout, ref row, "", _startWithWindowsBox);

        var note = new Label
        {
            Text = "Example: \"7,3,1,0\" reminds a week before, 3 days before, the day before, " +
                   "and on the due date itself.",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(16, 0, 16, 0),
            ForeColor = Color.DimGray
        };

        // "Where your data lives" - read-only, informational, no hidden stuff.
        var infoGroup = new GroupBox
        {
            Text = "Where your data lives",
            Dock = DockStyle.Top,
            Height = 175,
            Padding = new Padding(12, 4, 12, 8)
        };

        var infoLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true
        };
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var vaultPathBox = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = SystemColors.Control,
            Text = vaultFilePath
        };
        var openFolderButton = new Button { Text = "Open Folder", AutoSize = true };
        openFolderButton.Click += (_, _) => OpenContainingFolder(vaultFilePath);
        var vaultPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        vaultPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        vaultPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        vaultPanel.Controls.Add(vaultPathBox, 0, 0);
        vaultPanel.Controls.Add(openFolderButton, 1, 0);

        int infoRow = 0;
        AddRow(infoLayout, ref infoRow, "Vault file:", vaultPanel);
        AddRow(infoLayout, ref infoRow, "Google Drive:", new Label
        {
            AutoSize = true,
            Text = isDriveConnected ? "Connected ✓" : "Not connected",
            ForeColor = isDriveConnected ? Color.SeaGreen : Color.Firebrick
        });
        AddRow(infoLayout, ref infoRow, "credentials.json:", new Label
        {
            AutoSize = true,
            Text = File.Exists(credentialsFilePath)
                ? "Found"
                : "Not found - this is the Google API token credential file needed to connect to Drive (see README for setup)",
            ForeColor = File.Exists(credentialsFilePath) ? Color.SeaGreen : Color.Firebrick,
            MaximumSize = new Size(280, 0)
        });

        infoGroup.Controls.Add(infoLayout);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var saveButton = new Button { Text = "Save", AutoSize = true };
        saveButton.Click += SaveButton_Click;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);

        // Added bottom-to-top since these are stacked in reverse Dock=Top order below
        // the fixed Dock=Bottom button panel - each Controls.Add(x) with Dock=Top here
        // places x above whatever was added right before it.
        Controls.Add(infoGroup);
        Controls.Add(note);
        Controls.Add(layout);
        Controls.Add(buttonPanel);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        _autoLockBox.Value = Math.Clamp(_settings.AutoLockMinutes, 0, 240);
        _reminderDaysBox.Text = string.Join(",", _settings.ReminderDaysBefore);
        _startWithWindowsBox.Checked = _settings.StartWithWindows;
    }

    private static void OpenContainingFolder(string filePath)
    {
        try
        {
            // /select, highlights the file itself in Explorer rather than just opening
            // the folder, so it's obvious at a glance which file is the vault.
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open that folder: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void AddRow(TableLayoutPanel layout, ref int row, string label, Control control)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) }, 0, row);
        layout.Controls.Add(control, 1, row);
        row++;
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        var days = _reminderDaysBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
            .Where(n => n.HasValue && n.Value >= 0)
            .Select(n => n!.Value)
            .Distinct()
            .OrderByDescending(n => n)
            .ToArray();

        if (days.Length == 0)
        {
            MessageBox.Show(this, "Enter at least one valid reminder day (e.g. \"7,3,1,0\").", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.AutoLockMinutes = (int)_autoLockBox.Value;
        _settings.ReminderDaysBefore = days;
        _settings.StartWithWindows = _startWithWindowsBox.Checked;

        DialogResult = DialogResult.OK;
    }
}
