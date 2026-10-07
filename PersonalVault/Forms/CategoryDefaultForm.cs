using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// Add/edit dialog for a single category's Repeats/Autopay/Institution default -
/// opened from CategoryDefaultsForm's "Add..."/"Edit..." buttons. Same "mutate a
/// passed-in object, caller decides whether to keep it" pattern as
/// BankSubAccountForm/AccountEditForm, except the category name itself is the
/// dictionary key in the caller (VaultData.CustomCategoryDefaults), not a field on
/// CategoryDefault - so it comes back out via the Category property instead, and the
/// caller handles a renamed category as a remove-old-key/add-new-key pair.
/// </summary>
public class CategoryDefaultForm : Form
{
    private readonly CategoryDefault _default;

    private readonly ComboBox _categoryBox = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
    private readonly ComboBox _recurrenceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly CheckBox _autopayBox = new() { Text = "This is paid automatically (autopay)", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly TextBox _institutionBox = new() { Dock = DockStyle.Fill };

    /// <summary>The category name typed/picked - the dictionary key the caller stores this default under.</summary>
    public string Category => _categoryBox.Text.Trim();

    public CategoryDefaultForm(string? existingCategory, CategoryDefault? existingDefault, IEnumerable<string> knownCategories)
    {
        _default = existingDefault ?? new CategoryDefault();

        Text = existingCategory == null ? "Add Category Default" : "Edit Category Default";
        Width = 440;
        Height = 280;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var categoryItems = knownCategories
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _categoryBox.Items.AddRange(categoryItems.Cast<object>().ToArray());
        _recurrenceBox.Items.AddRange(Enum.GetNames(typeof(RecurrenceType)));

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
        AddRow(layout, ref row, "Category:", _categoryBox);
        AddRow(layout, ref row, "Repeats:", _recurrenceBox);
        AddRow(layout, ref row, "", _autopayBox);
        AddRow(layout, ref row, "Institution:", _institutionBox);

        var hint = new Label
        {
            AutoSize = false, Width = 380, Height = 34, Dock = DockStyle.Top,
            Padding = new Padding(16, 0, 16, 0), ForeColor = Color.DimGray,
            Text = "Applied automatically only when adding a brand-new entry in this category - never changes an existing entry."
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
        Controls.Add(hint);
        Controls.Add(buttonPanel);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        _categoryBox.Text = existingCategory ?? string.Empty;
        _recurrenceBox.SelectedItem = _default.Recurrence.ToString();
        _autopayBox.Checked = _default.Autopay;
        _institutionBox.Text = _default.Institution ?? string.Empty;
    }

    private static void AddRow(TableLayoutPanel layout, ref int row, string label, Control control)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) }, 0, row);
        layout.Controls.Add(control, 1, row);
        row++;
    }

    /// <summary>The edited default values - read this (alongside Category) after ShowDialog returns OK.</summary>
    public CategoryDefault Result { get; private set; } = new();

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_categoryBox.Text))
        {
            MessageBox.Show(this, "Please enter or pick a category.", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var recurrenceText = _recurrenceBox.SelectedItem as string ?? nameof(RecurrenceType.None);
        Result = new CategoryDefault
        {
            Recurrence = Enum.Parse<RecurrenceType>(recurrenceText),
            Autopay = _autopayBox.Checked,
            Institution = string.IsNullOrWhiteSpace(_institutionBox.Text) ? null : _institutionBox.Text.Trim()
        };

        DialogResult = DialogResult.OK;
    }
}
