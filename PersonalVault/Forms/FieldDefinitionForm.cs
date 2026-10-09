using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// Add/edit dialog for a single field within a category's field set - opened from
/// CategoryFieldSetsForm's "Add..."/"Edit..." buttons. Same "mutate a passed-in
/// object, caller decides whether to keep it" pattern as BankSubAccountForm/
/// CategoryDefaultForm.
/// </summary>
public class FieldDefinitionForm : Form
{
    private readonly ComboBox _captionBox = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
    private readonly ComboBox _dataTypeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _sizeBox = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 10000, Value = 150 };
    private readonly Label _reservedNote = new()
    {
        AutoSize = false, Width = 380, Height = 48, Dock = DockStyle.Top,
        Padding = new Padding(16, 0, 16, 8), ForeColor = Color.DimGray,
        Text = "This is a built-in field - it already has its own control (password " +
               "masking, website open button, etc.). Data Type/Size are ignored for it; " +
               "only whether it's in this category's field set, and where, matters.",
        Visible = false
    };

    public string Caption => _captionBox.Text.Trim();
    public FieldDefinition Result { get; private set; } = new();

    public FieldDefinitionForm(FieldDefinition? existing, IEnumerable<string> captionSuggestions)
    {
        Text = existing == null ? "Add Field" : "Edit Field";
        Width = 440;
        Height = 280;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        _captionBox.Items.AddRange(captionSuggestions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Cast<object>().ToArray());
        _dataTypeBox.Items.AddRange(Enum.GetNames(typeof(FieldDataType)));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(16, 12, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        AddRow(layout, ref row, "Caption:", _captionBox);
        AddRow(layout, ref row, "Data Type:", _dataTypeBox);
        AddRow(layout, ref row, "Size:", _sizeBox);

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
        Controls.Add(_reservedNote);
        Controls.Add(buttonPanel);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        _captionBox.TextChanged += (_, _) => UpdateReservedNote();

        if (existing != null)
        {
            _captionBox.Text = existing.Caption;
            _dataTypeBox.SelectedItem = existing.DataType.ToString();
            _sizeBox.Value = Math.Min(_sizeBox.Maximum, Math.Max(_sizeBox.Minimum, existing.Size));
        }
        else
        {
            _dataTypeBox.SelectedItem = nameof(FieldDataType.String);
        }
        UpdateReservedNote();
    }

    private static void AddRow(TableLayoutPanel layout, ref int row, string label, Control control)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) }, 0, row);
        layout.Controls.Add(control, 1, row);
        row++;
    }

    private void UpdateReservedNote()
    {
        bool isReserved = CategoryFieldSetDefaults.ReservedCaptions
            .Any(c => string.Equals(c, _captionBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        _reservedNote.Visible = isReserved;
        _dataTypeBox.Enabled = !isReserved;
        _sizeBox.Enabled = !isReserved;
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_captionBox.Text))
        {
            MessageBox.Show(this, "Please enter or pick a field caption.", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var dataTypeText = _dataTypeBox.SelectedItem as string ?? nameof(FieldDataType.String);
        Result = new FieldDefinition
        {
            Caption = Caption,
            DataType = Enum.Parse<FieldDataType>(dataTypeText),
            Size = (int)_sizeBox.Value
        };

        DialogResult = DialogResult.OK;
    }
}
