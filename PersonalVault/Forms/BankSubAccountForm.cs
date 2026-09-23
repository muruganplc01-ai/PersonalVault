using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// Add/edit dialog for a single BankSubAccount (Checking, Savings, Money Market, ...)
/// - opened from BankAccountsForm's "Add..."/"Edit..." buttons. Same "mutate a passed-in
/// object, caller decides whether to keep it" pattern as AccountEditForm/ProfileForm.
/// </summary>
public class BankSubAccountForm : Form
{
    private readonly BankSubAccount _subAccount;

    private readonly TextBox _labelBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _accountNumberBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _routingNumberBox = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _hasBalanceBox = new() { Text = "Track balance", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly NumericUpDown _balanceBox = new()
    {
        Dock = DockStyle.Fill, DecimalPlaces = 2, Minimum = -100_000_000, Maximum = 100_000_000, ThousandsSeparator = true, Enabled = false
    };
    private readonly DateTimePicker _balanceAsOfBox = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short, Enabled = false };
    private readonly TextBox _notesBox = new() { Dock = DockStyle.Fill };

    public BankSubAccountForm(BankSubAccount subAccount)
    {
        _subAccount = subAccount;

        Text = "Bank Account";
        Width = 440;
        Height = 340;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(16, 12, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        AddRow(layout, ref row, "Label:", _labelBox);
        AddRow(layout, ref row, "Account Number:", _accountNumberBox);
        AddRow(layout, ref row, "Routing Number:", _routingNumberBox);

        var balancePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        balancePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        balancePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        balancePanel.Controls.Add(_balanceBox, 0, 0);
        balancePanel.Controls.Add(_balanceAsOfBox, 1, 0);
        AddRow(layout, ref row, "", _hasBalanceBox);
        AddRow(layout, ref row, "Balance:", balancePanel);

        AddRow(layout, ref row, "Notes:", _notesBox);

        _hasBalanceBox.CheckedChanged += (_, _) =>
        {
            _balanceBox.Enabled = _hasBalanceBox.Checked;
            _balanceAsOfBox.Enabled = _hasBalanceBox.Checked;
        };

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

        Controls.Add(layout);
        Controls.Add(buttonPanel);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        LoadFromSubAccount();
    }

    private static void AddRow(TableLayoutPanel layout, ref int row, string label, Control control)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) }, 0, row);
        layout.Controls.Add(control, 1, row);
        row++;
    }

    private void LoadFromSubAccount()
    {
        _labelBox.Text = _subAccount.Label;
        _accountNumberBox.Text = _subAccount.AccountNumber;
        _routingNumberBox.Text = _subAccount.RoutingNumber;
        _notesBox.Text = _subAccount.Notes;

        if (_subAccount.Balance.HasValue)
        {
            _hasBalanceBox.Checked = true;
            _balanceBox.Enabled = true;
            _balanceBox.Value = Math.Min(_balanceBox.Maximum, Math.Max(_balanceBox.Minimum, _subAccount.Balance.Value));
            _balanceAsOfBox.Enabled = true;
            _balanceAsOfBox.Value = _subAccount.BalanceAsOf ?? DateTime.Now;
        }
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_labelBox.Text))
        {
            MessageBox.Show(this, "Please enter a label (e.g. \"Checking\").", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _subAccount.Label = _labelBox.Text.Trim();
        _subAccount.AccountNumber = _accountNumberBox.Text.Trim();
        _subAccount.RoutingNumber = _routingNumberBox.Text.Trim();
        _subAccount.Notes = _notesBox.Text.Trim();
        _subAccount.Balance = _hasBalanceBox.Checked ? _balanceBox.Value : null;
        _subAccount.BalanceAsOf = _hasBalanceBox.Checked ? _balanceAsOfBox.Value.Date : null;

        DialogResult = DialogResult.OK;
    }
}
