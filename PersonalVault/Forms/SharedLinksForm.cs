using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// "Shared Links..." tray menu item's window - lists every share this PC has created
/// (active, expired-pending-cleanup, or already revoked) and lets you revoke one
/// immediately instead of waiting for ShareExpiryService's next sweep. revokeNow is
/// TrayApplicationContext's wrapper around GoogleDriveSync.RevokeShareAsync plus
/// persisting the updated list - this form never touches Drive or shares.json directly.
/// </summary>
public class SharedLinksForm : Form
{
    private readonly List<SharedLink> _shares;
    private readonly Func<SharedLink, Task> _revokeNow;
    private readonly ListView _listView;
    private readonly Button _revokeButton;

    public SharedLinksForm(List<SharedLink> shares, Func<SharedLink, Task> revokeNow)
    {
        _shares = shares;
        _revokeNow = revokeNow;

        Text = "Shared Links";
        Width = 620;
        Height = 420;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true,
            HideSelection = false
        };
        _listView.Columns.Add("Account", 200);
        _listView.Columns.Add("Created", 130);
        _listView.Columns.Add("Expires", 130);
        _listView.Columns.Add("Status", 130);
        _listView.SelectedIndexChanged += (_, _) => UpdateRevokeButtonState();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        var closeButton = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        _revokeButton = new Button { Text = "Revoke Now", AutoSize = true, Enabled = false };
        _revokeButton.Click += async (_, _) => await RevokeSelectedAsync();
        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(_revokeButton);

        Controls.Add(_listView);
        Controls.Add(buttonPanel);

        CancelButton = closeButton;

        RefreshList();
    }

    private void RefreshList()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var share in _shares.OrderByDescending(s => s.CreatedUtc))
        {
            var item = new ListViewItem(share.AccountName);
            item.SubItems.Add(share.CreatedUtc.ToLocalTime().ToString("MMM d, yyyy h:mm tt"));
            item.SubItems.Add(share.ExpiresUtc.ToLocalTime().ToString("MMM d, yyyy h:mm tt"));
            item.SubItems.Add(Status(share));
            item.Tag = share;
            _listView.Items.Add(item);
        }
        _listView.EndUpdate();
        UpdateRevokeButtonState();
    }

    private static string Status(SharedLink share)
    {
        if (share.Revoked) return "Revoked";
        return share.ExpiresUtc <= DateTime.UtcNow ? "Expired (pending cleanup)" : "Active";
    }

    private void UpdateRevokeButtonState()
    {
        var selected = SelectedShare();
        _revokeButton.Enabled = selected != null && !selected.Revoked;
    }

    private SharedLink? SelectedShare() =>
        _listView.SelectedItems.Count > 0 ? _listView.SelectedItems[0].Tag as SharedLink : null;

    private async Task RevokeSelectedAsync()
    {
        var share = SelectedShare();
        if (share == null) return;

        _revokeButton.Enabled = false;
        try
        {
            await _revokeNow(share);
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not revoke this link: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateRevokeButtonState();
        }
    }
}
