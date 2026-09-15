using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// The main account list window. This is a normal Form, but TrayApplicationContext
/// intercepts its close button and hides it instead of closing it - the app itself
/// only ever exits via the tray icon's "Exit" menu item.
/// </summary>
public class MainForm : Form
{
    private VaultData _vault;
    private readonly Action _save;
    private readonly ListView _listView;

    public MainForm(VaultData vault, Action save)
    {
        _vault = vault;
        _save = save;

        Text = "Personal Vault";
        Width = 920;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true
        };
        _listView.Columns.Add("Category", 140);
        _listView.Columns.Add("Name", 190);
        _listView.Columns.Add("Institution", 160);
        _listView.Columns.Add("Username", 160);
        _listView.Columns.Add("Due Date", 110);
        _listView.DoubleClick += (_, _) => EditSelected();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8)
        };

        var addBtn = new Button { Text = "Add Account", AutoSize = true };
        var editBtn = new Button { Text = "Edit", AutoSize = true };
        var deleteBtn = new Button { Text = "Delete", AutoSize = true };
        var copyUserBtn = new Button { Text = "Copy Username", AutoSize = true };
        var copyPassBtn = new Button { Text = "Copy Password", AutoSize = true };

        addBtn.Click += (_, _) => AddNew();
        editBtn.Click += (_, _) => EditSelected();
        deleteBtn.Click += (_, _) => DeleteSelected();
        copyUserBtn.Click += (_, _) => CopyField(a => a.UserName, "Username");
        copyPassBtn.Click += (_, _) => CopyField(a => a.Password, "Password");

        buttonPanel.Controls.AddRange(new Control[] { addBtn, editBtn, deleteBtn, copyUserBtn, copyPassBtn });

        Controls.Add(_listView);
        Controls.Add(buttonPanel);

        RefreshData(_vault);
    }

    /// <summary>Repoints this window at (possibly new) vault data - e.g. after pulling a newer copy from Drive.</summary>
    public void RefreshData(VaultData vault)
    {
        _vault = vault;
        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var account in _vault.Accounts.OrderBy(a => a.DueDate ?? DateTime.MaxValue))
        {
            var item = new ListViewItem(account.Category.ToString());
            item.SubItems.Add(account.Name);
            item.SubItems.Add(account.Institution);
            item.SubItems.Add(account.UserName);
            item.SubItems.Add(account.DueDate?.ToString("MMM d, yyyy") ?? "");
            item.Tag = account;
            _listView.Items.Add(item);
        }
        _listView.EndUpdate();
    }

    private AccountEntry? SelectedAccount() =>
        _listView.SelectedItems.Count > 0 ? _listView.SelectedItems[0].Tag as AccountEntry : null;

    private void AddNew()
    {
        var entry = new AccountEntry();
        using var form = new AccountEditForm(entry);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _vault.Accounts.Add(entry);
            _save();
            RefreshData(_vault);
        }
    }

    private void EditSelected()
    {
        var account = SelectedAccount();
        if (account == null) return;

        using var form = new AccountEditForm(account);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            account.ModifiedUtc = DateTime.UtcNow;
            _save();
            RefreshData(_vault);
        }
    }

    private void DeleteSelected()
    {
        var account = SelectedAccount();
        if (account == null) return;

        var result = MessageBox.Show(this, $"Delete '{account.Name}'? This cannot be undone.",
            "Personal Vault", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        _vault.Accounts.Remove(account);
        _save();
        RefreshData(_vault);
    }

    private void CopyField(Func<AccountEntry, string> selector, string label)
    {
        var account = SelectedAccount();
        if (account == null) return;

        var value = selector(account);
        if (string.IsNullOrEmpty(value)) return;

        Clipboard.SetText(value);
        MessageBox.Show(this, $"{label} copied to clipboard. It will stay there until you copy something else.",
            "Personal Vault", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
