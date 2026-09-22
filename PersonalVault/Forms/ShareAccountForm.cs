using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// "Share Account" dialog opened from MainForm's Share... button. Two steps in one
/// window: pick how long the link should stay live, then (once TrayApplicationContext's
/// ShareAccountAsync has encrypted the account and uploaded it to Drive) show the
/// resulting link with a Copy button. shareAccount is that delegate, passed in the same
/// way MainForm receives its other TrayApplicationContext callbacks - this dialog never
/// touches Drive or crypto directly.
/// </summary>
public class ShareAccountForm : Form
{
    private readonly AccountEntry _account;
    private readonly Func<AccountEntry, TimeSpan, Task<string>> _shareAccount;

    private readonly Label _headerLabel;
    private readonly ComboBox _expiryBox;
    private readonly Button _createButton;
    private readonly Button _cancelButton;

    private readonly Label _linkExpiryNote;
    private readonly TextBox _linkBox;
    private readonly Button _copyButton;
    private readonly Button _doneButton;

    private static readonly (string Label, TimeSpan Duration)[] ExpiryOptions =
    {
        ("1 hour", TimeSpan.FromHours(1)),
        ("1 day", TimeSpan.FromDays(1)),
        ("3 days", TimeSpan.FromDays(3)),
        ("7 days", TimeSpan.FromDays(7)),
    };

    public ShareAccountForm(AccountEntry account, Func<AccountEntry, TimeSpan, Task<string>> shareAccount)
    {
        _account = account;
        _shareAccount = shareAccount;

        Text = "Share Account";
        Width = 460;
        Height = 260;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        _headerLabel = new Label
        {
            Text = $"Share \"{account.Name}\"",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 30,
            Padding = new Padding(16, 12, 16, 0)
        };

        // --- Step 1: pick expiration ---
        var pickPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(16, 8, 16, 8)
        };
        pickPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        pickPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pickPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pickPanel.Controls.Add(new Label { Text = "Link expires in:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) }, 0, 0);

        _expiryBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _expiryBox.Items.AddRange(ExpiryOptions.Select(o => o.Label).Cast<object>().ToArray());
        _expiryBox.SelectedIndex = 1; // default: 1 day
        pickPanel.Controls.Add(_expiryBox, 1, 0);

        var noteLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = System.Drawing.Color.DimGray,
            Padding = new Padding(16, 0, 16, 8),
            Text = "Anyone with the link can view this one account until it expires or you " +
                   "revoke it from the tray menu's \"Shared Links...\". This is not a true " +
                   "one-time view - see README for details."
        };

        // --- Step 2: show the resulting link (hidden until created) ---
        _linkExpiryNote = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(16, 8, 16, 4),
            Visible = false
        };
        var linkPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(16, 0, 16, 0), Visible = false };
        _linkBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
        linkPanel.Controls.Add(_linkBox);
        _copyButton = new Button { Text = "Copy Link", AutoSize = true, Visible = false };
        _copyButton.Click += (_, _) => CopyLink();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        _cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        _createButton = new Button { Text = "Create Link", AutoSize = true };
        _createButton.Click += async (_, _) => await CreateLinkAsync();
        _doneButton = new Button { Text = "Done", AutoSize = true, DialogResult = DialogResult.OK, Visible = false };
        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_createButton);
        buttonPanel.Controls.Add(_copyButton);
        buttonPanel.Controls.Add(_doneButton);

        // Dock=Top siblings stack with the LAST Controls.Add() closest to the top edge.
        Controls.Add(linkPanel);
        Controls.Add(_linkExpiryNote);
        Controls.Add(noteLabel);
        Controls.Add(pickPanel);
        Controls.Add(_headerLabel);
        Controls.Add(buttonPanel);

        AcceptButton = _createButton;
        CancelButton = _cancelButton;
    }

    private async Task CreateLinkAsync()
    {
        _createButton.Enabled = false;
        _expiryBox.Enabled = false;
        try
        {
            var duration = ExpiryOptions[_expiryBox.SelectedIndex].Duration;
            var link = await _shareAccount(_account, duration);

            _linkBox.Text = link;
            _linkExpiryNote.Text = $"Link created - expires {DateTime.Now.Add(duration):MMM d, yyyy h:mm tt}.";
            _linkExpiryNote.Visible = true;
            _linkBox.Parent!.Visible = true;
            _copyButton.Visible = true;
            _doneButton.Visible = true;
            _cancelButton.Visible = false;
            _createButton.Visible = false;
            _expiryBox.Enabled = false;

            CopyLink();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not create the share link: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _createButton.Enabled = true;
            _expiryBox.Enabled = true;
        }
    }

    private void CopyLink()
    {
        if (string.IsNullOrEmpty(_linkBox.Text)) return;
        try { Clipboard.SetText(_linkBox.Text); }
        catch { /* clipboard can be momentarily locked by another app - not worth surfacing an error for */ }
    }
}
