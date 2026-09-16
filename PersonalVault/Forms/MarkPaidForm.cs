using System.Windows.Forms;

namespace PersonalVault.Forms;

/// <summary>
/// Small modal used by the Dues tab's "Mark as Paid..." button - just the amount paid
/// and the date paid (defaults to today, but editable in case you're logging a payment
/// after the fact). Nothing here touches the vault or the payments file directly; the
/// caller (MainForm.MarkSelectedDuePaid) reads AmountPaid/PaidDate back out and hands
/// them to TrayApplicationContext.MarkAccountPaid to actually record and save.
/// </summary>
public class MarkPaidForm : Form
{
    private readonly NumericUpDown _amountBox = new()
    {
        Minimum = 0,
        Maximum = 1_000_000,
        DecimalPlaces = 2,
        ThousandsSeparator = true,
        Dock = DockStyle.Fill
    };
    private readonly DateTimePicker _dateBox = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short };

    public decimal AmountPaid => _amountBox.Value;
    public DateTime PaidDate => _dateBox.Value.Date;

    public MarkPaidForm(string accountName, DateTime? dueDate)
    {
        Text = "Mark as Paid";
        Width = 360;
        Height = 230;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var headerLabel = new Label
        {
            Text = dueDate.HasValue ? $"{accountName}  -  due {dueDate:MMM d, yyyy}" : accountName,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(16, 12, 16, 0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(16, 8, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        AddRow(layout, ref row, "Amount paid:", _amountBox);
        AddRow(layout, ref row, "Date paid:", _dateBox);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var saveButton = new Button { Text = "Mark as Paid", AutoSize = true };
        saveButton.Click += (_, _) => DialogResult = DialogResult.OK;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);

        // Same bottom-to-top Dock=Top ordering convention used elsewhere in this app
        // (e.g. SettingsForm): each Controls.Add(x) with Dock=Top lands above whatever
        // was added right before it, so layout ends up below headerLabel.
        Controls.Add(layout);
        Controls.Add(headerLabel);
        Controls.Add(buttonPanel);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        _dateBox.Value = DateTime.Today;
    }

    private static void AddRow(TableLayoutPanel layout, ref int row, string label, Control control)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) }, 0, row);
        layout.Controls.Add(control, 1, row);
        row++;
    }
}
