using System.Drawing;
using System.Windows.Forms;
using PersonalVault.Utils;

namespace PersonalVault.Forms;

/// <summary>
/// Confirms (and lets the user correct) a balance figure read from a brokerage CSV
/// export before it's applied to Current Balance - see Utils/BalanceCsvImporter for
/// how the figure is guessed. This dialog never lets that figure through without the
/// user seeing and being able to edit it first, since a silently-wrong balance is
/// worse than not importing at all.
/// </summary>
public class ImportBalanceForm : Form
{
    private readonly NumericUpDown _amountBox = new()
    {
        Dock = DockStyle.Fill, DecimalPlaces = 2, Minimum = -100_000_000, Maximum = 100_000_000, ThousandsSeparator = true
    };
    private readonly DateTimePicker _asOfBox = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short };

    public decimal Amount => _amountBox.Value;
    public DateTime AsOfDate => _asOfBox.Value.Date;

    public ImportBalanceForm(string fileName, BalanceCsvImporter.Result result)
    {
        Text = "Import Balance from CSV";
        Width = 460;
        Height = 300;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var explanationLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12, 12, 12, 6),
            MaximumSize = new Size(420, 0),
            Text = $"From: {fileName}\n\n{result.Explanation}\n\n" +
                   "Double-check the amount below before using it - this is a best-effort reading of " +
                   "the file, not guaranteed to match its exact format."
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(12, 6, 12, 6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        AddRow(layout, ref row, "Amount:", _amountBox);
        AddRow(layout, ref row, "As of:", _asOfBox);

        _amountBox.Value = ClampToRange(_amountBox, result.Amount ?? 0);
        _asOfBox.Value = DateTime.Now;

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var okButton = new Button { Text = "Use This Amount", AutoSize = true, DialogResult = DialogResult.OK };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        // Dock=Top siblings stack with the LAST Controls.Add() closest to the top edge,
        // so explanationLabel is added after layout to keep the explanation above the
        // amount/date fields.
        Controls.Add(layout);
        Controls.Add(explanationLabel);
        Controls.Add(buttonPanel);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private static void AddRow(TableLayoutPanel layout, ref int row, string label, Control control)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(
            new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) },
            0, row);
        layout.Controls.Add(control, 1, row);
        row++;
    }

    private static decimal ClampToRange(NumericUpDown box, decimal value) =>
        Math.Min(box.Maximum, Math.Max(box.Minimum, value));
}
