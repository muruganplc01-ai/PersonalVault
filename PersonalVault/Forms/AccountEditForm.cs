using System.Security.Cryptography;
using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// Add/edit dialog for a single AccountEntry. Works on a live reference to the entry -
/// callers only get DialogResult.OK back if the fields were actually valid and copied in.
/// </summary>
public class AccountEditForm : Form
{
    private readonly AccountEntry _entry;

    private readonly ComboBox _categoryBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _nameBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _institutionBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _userNameBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _passwordBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly CheckBox _showPasswordBox = new() { Text = "Show", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _generateButton = new() { Text = "Generate", AutoSize = true };
    private readonly TextBox _accountNumberBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _websiteBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _phoneBox = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _hasDueDateBox = new() { Text = "Has a due date", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly DateTimePicker _dueDatePicker = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short, Enabled = false };
    private readonly ComboBox _recurrenceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _notesBox = new() { Dock = DockStyle.Fill, Multiline = true, Height = 55, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _extraFieldsBox = new() { Dock = DockStyle.Fill, Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical };

    public AccountEditForm(AccountEntry entry)
    {
        _entry = entry;

        Text = "Account Details";
        Width = 540;
        Height = 660;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        _categoryBox.Items.AddRange(Enum.GetNames(typeof(AccountCategory)));
        _recurrenceBox.Items.AddRange(Enum.GetNames(typeof(RecurrenceType)));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        AddRow(layout, ref row, "Category:", _categoryBox);
        AddRow(layout, ref row, "Name:", _nameBox);
        AddRow(layout, ref row, "Institution:", _institutionBox);
        AddRow(layout, ref row, "Username:", _userNameBox);

        var passwordPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        passwordPanel.Controls.Add(_passwordBox, 0, 0);
        passwordPanel.Controls.Add(_showPasswordBox, 1, 0);
        passwordPanel.Controls.Add(_generateButton, 2, 0);
        AddRow(layout, ref row, "Password:", passwordPanel);

        AddRow(layout, ref row, "Account #:", _accountNumberBox);
        AddRow(layout, ref row, "Website:", _websiteBox);
        AddRow(layout, ref row, "Phone:", _phoneBox);

        var duePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        duePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        duePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        duePanel.Controls.Add(_hasDueDateBox, 0, 0);
        duePanel.Controls.Add(_dueDatePicker, 1, 0);
        AddRow(layout, ref row, "Due date:", duePanel);

        AddRow(layout, ref row, "Repeats:", _recurrenceBox);
        AddRow(layout, ref row, "Notes:", _notesBox);
        AddRow(layout, ref row, "Extra info:\n(key=value,\none per line)", _extraFieldsBox);

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

        _hasDueDateBox.CheckedChanged += (_, _) => _dueDatePicker.Enabled = _hasDueDateBox.Checked;
        _showPasswordBox.CheckedChanged += (_, _) => _passwordBox.UseSystemPasswordChar = !_showPasswordBox.Checked;
        _generateButton.Click += (_, _) => _passwordBox.Text = GeneratePassword();

        var scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scrollPanel.Controls.Add(layout);

        Controls.Add(scrollPanel);
        Controls.Add(buttonPanel);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        LoadFromEntry();
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

    private void LoadFromEntry()
    {
        _categoryBox.SelectedItem = _entry.Category.ToString();
        _nameBox.Text = _entry.Name;
        _institutionBox.Text = _entry.Institution;
        _userNameBox.Text = _entry.UserName;
        _passwordBox.Text = _entry.Password;
        _accountNumberBox.Text = _entry.AccountNumber;
        _websiteBox.Text = _entry.Website;
        _phoneBox.Text = _entry.PhoneNumber;
        _recurrenceBox.SelectedItem = _entry.Recurrence.ToString();
        _notesBox.Text = _entry.Notes;
        _extraFieldsBox.Text = string.Join(Environment.NewLine, _entry.ExtraFields.Select(kv => $"{kv.Key}={kv.Value}"));

        if (_entry.DueDate.HasValue)
        {
            _hasDueDateBox.Checked = true;
            _dueDatePicker.Enabled = true;
            _dueDatePicker.Value = _entry.DueDate.Value;
        }
        else
        {
            _hasDueDateBox.Checked = false;
            _dueDatePicker.Enabled = false;
        }
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show(this, "Please enter a name for this account.", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var categoryText = _categoryBox.SelectedItem as string ?? nameof(AccountCategory.Other);
        var recurrenceText = _recurrenceBox.SelectedItem as string ?? nameof(RecurrenceType.None);

        var previousDueDate = _entry.DueDate;

        _entry.Category = Enum.Parse<AccountCategory>(categoryText);
        _entry.Name = _nameBox.Text.Trim();
        _entry.Institution = _institutionBox.Text.Trim();
        _entry.UserName = _userNameBox.Text.Trim();
        _entry.Password = _passwordBox.Text;
        _entry.AccountNumber = _accountNumberBox.Text.Trim();
        _entry.Website = _websiteBox.Text.Trim();
        _entry.PhoneNumber = _phoneBox.Text.Trim();
        _entry.Recurrence = Enum.Parse<RecurrenceType>(recurrenceText);
        _entry.Notes = _notesBox.Text;
        _entry.DueDate = _hasDueDateBox.Checked ? _dueDatePicker.Value.Date : null;

        // If the due date actually changed, start the reminder cycle over for it.
        if (_entry.DueDate != previousDueDate)
            _entry.LastNotifiedOn = null;

        _entry.ExtraFields = _extraFieldsBox.Lines
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

        DialogResult = DialogResult.OK;
    }

    private static string GeneratePassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*()-_=+";
        byte[] bytes = RandomNumberGenerator.GetBytes(20);
        var result = new char[20];
        for (int i = 0; i < result.Length; i++)
            result[i] = chars[bytes[i] % chars.Length];
        return new string(result);
    }
}
