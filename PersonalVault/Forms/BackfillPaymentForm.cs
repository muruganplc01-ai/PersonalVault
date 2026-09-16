using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// Dues tab's "Backfill Past Payment..." dialog - for catching up payment history on
/// months that were never logged (most useful right after turning this feature on, or
/// for an autopay account that's been quietly paying itself for months with nothing
/// recorded here). Unlike MarkPaidForm/Mark as Paid, this can target ANY account (not
/// just one currently showing as due) and can add several months' worth of history at
/// once - but deliberately does NOT touch the account's live DueDate or recurrence
/// schedule, since it's only filling in the past, not affecting what happens next.
/// </summary>
public class BackfillPaymentForm : Form
{
    private readonly ComboBox _accountBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _amountBox = new()
    {
        Minimum = 0,
        Maximum = 1_000_000,
        DecimalPlaces = 2,
        ThousandsSeparator = true,
        Dock = DockStyle.Fill
    };
    private readonly DateTimePicker _dateBox = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short };
    private readonly NumericUpDown _countBox = new() { Minimum = 1, Maximum = 24, Value = 1, Dock = DockStyle.Fill };
    private readonly ComboBox _spacingBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };

    private readonly List<AccountEntry> _accounts;

    public AccountEntry SelectedAccount => _accounts[_accountBox.SelectedIndex];
    public decimal AmountPaid => _amountBox.Value;
    public DateTime MostRecentPaidDate => _dateBox.Value.Date;
    public int Count => (int)_countBox.Value;
    public RecurrenceType Spacing => Enum.Parse<RecurrenceType>((string)_spacingBox.SelectedItem!);

    public BackfillPaymentForm(IEnumerable<AccountEntry> accounts)
    {
        _accounts = accounts.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

        Text = "Backfill Past Payment";
        Width = 420;
        Height = 340;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var headerLabel = new Label
        {
            Text = "Catch up payment history for months that were never entered - this only " +
                   "records history, it won't change the account's upcoming due date.",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 55,
            Padding = new Padding(16, 12, 16, 0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(16, 4, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        foreach (var account in _accounts)
            _accountBox.Items.Add($"{account.Category} - {account.Name}");
        if (_accountBox.Items.Count > 0) _accountBox.SelectedIndex = 0;
        _accountBox.SelectedIndexChanged += (_, _) => SyncSpacingToSelectedAccount();

        _spacingBox.Items.AddRange(Enum.GetNames(typeof(RecurrenceType)).Where(n => n != nameof(RecurrenceType.None)).ToArray());

        int row = 0;
        AddRow(layout, ref row, "Account:", _accountBox);
        AddRow(layout, ref row, "Amount paid (each):", _amountBox);
        AddRow(layout, ref row, "Most recent date paid:", _dateBox);
        AddRow(layout, ref row, "Number of past payments:", _countBox);
        AddRow(layout, ref row, "Spacing between them:", _spacingBox);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var saveButton = new Button { Text = "Add Payment History", AutoSize = true };
        saveButton.Click += SaveButton_Click;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);

        // Same bottom-to-top Dock=Top ordering convention used elsewhere in this app:
        // each Controls.Add(x) with Dock=Top lands above whatever was added right before
        // it, so layout ends up below headerLabel.
        Controls.Add(layout);
        Controls.Add(headerLabel);
        Controls.Add(buttonPanel);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        _dateBox.Value = DateTime.Today;
        SyncSpacingToSelectedAccount();
    }

    /// <summary>Defaults the spacing dropdown to whatever the selected account's own Recurrence is (falling back to Monthly for a non-recurring account), since that's the right guess most of the time - still freely editable.</summary>
    private void SyncSpacingToSelectedAccount()
    {
        if (_accountBox.SelectedIndex < 0) return;

        var recurrence = _accounts[_accountBox.SelectedIndex].Recurrence;
        var spacingName = recurrence == RecurrenceType.None ? nameof(RecurrenceType.Monthly) : recurrence.ToString();
        _spacingBox.SelectedItem = spacingName;
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
        if (_accountBox.SelectedIndex < 0)
        {
            MessageBox.Show(this, "Choose an account.", "Personal Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
