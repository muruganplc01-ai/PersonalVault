using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// Account Details' "Bank Accounts..." button - lets one BankAccount-category entry
/// (e.g. "Chase Bank") hold several sub-accounts (Checking, Savings, Money Market, ...),
/// each with its own account/routing number and balance. Operates directly on the
/// List&lt;BankSubAccount&gt; passed in (AccountEditForm's own working copy - see
/// _workingSubAccounts there), same "mutate what you're given, caller decides whether
/// to actually keep it" pattern as everything else in this app - nothing here touches
/// the vault or triggers a save on its own.
/// </summary>
public class BankAccountsForm : Form
{
    private readonly List<BankSubAccount> _subAccounts;
    private readonly ListView _listView;
    private readonly Button _editButton;
    private readonly Button _deleteButton;

    public BankAccountsForm(List<BankSubAccount> subAccounts)
    {
        _subAccounts = subAccounts;

        Text = "Bank Accounts";
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
        _listView.Columns.Add("Label", 130);
        _listView.Columns.Add("Account Number", 140);
        _listView.Columns.Add("Routing Number", 120);
        _listView.Columns.Add("Balance", 100);
        _listView.Columns.Add("As Of", 90);
        _listView.DoubleClick += (_, _) => EditSelected();
        _listView.SelectedIndexChanged += (_, _) => UpdateButtonStates();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        var closeButton = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        var addButton = new Button { Text = "Add...", AutoSize = true };
        _editButton = new Button { Text = "Edit...", AutoSize = true, Enabled = false };
        _deleteButton = new Button { Text = "Delete", AutoSize = true, Enabled = false };
        addButton.Click += (_, _) => AddNew();
        _editButton.Click += (_, _) => EditSelected();
        _deleteButton.Click += (_, _) => DeleteSelected();
        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(_deleteButton);
        buttonPanel.Controls.Add(_editButton);
        buttonPanel.Controls.Add(addButton);

        Controls.Add(_listView);
        Controls.Add(buttonPanel);

        CancelButton = closeButton;

        RefreshList();
    }

    private void RefreshList()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var sub in _subAccounts)
        {
            var item = new ListViewItem(sub.Label);
            item.SubItems.Add(sub.AccountNumber);
            item.SubItems.Add(sub.RoutingNumber);
            item.SubItems.Add(sub.Balance.HasValue ? sub.Balance.Value.ToString("C") : "");
            item.SubItems.Add(sub.BalanceAsOf?.ToString("MMM d, yyyy") ?? "");
            item.Tag = sub;
            _listView.Items.Add(item);
        }
        _listView.EndUpdate();
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        bool hasSelection = _listView.SelectedItems.Count > 0;
        _editButton.Enabled = hasSelection;
        _deleteButton.Enabled = hasSelection;
    }

    private BankSubAccount? SelectedSubAccount() =>
        _listView.SelectedItems.Count > 0 ? _listView.SelectedItems[0].Tag as BankSubAccount : null;

    private void AddNew()
    {
        var sub = new BankSubAccount();
        using var form = new BankSubAccountForm(sub);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _subAccounts.Add(sub);
            RefreshList();
        }
    }

    private void EditSelected()
    {
        var sub = SelectedSubAccount();
        if (sub == null) return;

        using var form = new BankSubAccountForm(sub);
        if (form.ShowDialog(this) == DialogResult.OK)
            RefreshList();
    }

    private void DeleteSelected()
    {
        var sub = SelectedSubAccount();
        if (sub == null) return;

        var result = MessageBox.Show(this, $"Delete \"{sub.Label}\"?", "Personal Vault",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        _subAccounts.Remove(sub);
        RefreshList();
    }
}
