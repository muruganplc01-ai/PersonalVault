using System.Security.Cryptography;
using System.Windows.Forms;
using PersonalVault.Models;
using PersonalVault.Utils;

namespace PersonalVault.Forms;

/// <summary>
/// Add/edit dialog for a single AccountEntry. Works on a live reference to the entry -
/// callers only get DialogResult.OK back if the fields were actually valid and copied in.
/// </summary>
public class AccountEditForm : Form
{
    private readonly AccountEntry _entry;
    private readonly string _defaultOwnerName;
    private readonly string? _defaultBrowserPath;
    private readonly Func<string, string[]?>? _getCustomCategoryFields;
    private readonly Action<string, string[]>? _saveCustomCategoryFields;

    private readonly ComboBox _categoryBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button _suggestFieldsButton = new() { Text = "+ Category Fields", AutoSize = true };
    private readonly Button _addCategoryButton = new() { Text = "+ New...", AutoSize = true };
    private readonly TextBox _nameBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _institutionBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _ownerBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _userNameBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _passwordBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly CheckBox _showPasswordBox = new() { Text = "Show", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _generateButton = new() { Text = "Generate", AutoSize = true };
    private readonly TextBox _accountNumberBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _websiteBox = new() { Dock = DockStyle.Fill };
    private readonly Button _openWebsiteButton = new() { Text = "Open", AutoSize = true };
    private readonly TextBox _phoneBox = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _hasDueDateBox = new() { Text = "Has a due date", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly DateTimePicker _dueDatePicker = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short, Enabled = false };
    private readonly ComboBox _recurrenceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly CheckBox _autoPaymentBox = new() { Text = "This is paid automatically (autopay)", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox _hasAmountDueBox = new() { Text = "Track an amount due", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly NumericUpDown _amountDueBox = new()
    {
        Dock = DockStyle.Fill, DecimalPlaces = 2, Minimum = 0, Maximum = 100_000_000, ThousandsSeparator = true, Enabled = false
    };
    private readonly CheckBox _hasBalanceBox = new() { Text = "Track current balance", AutoSize = true, Anchor = AnchorStyles.Left };
    // Minimum is negative (not 0) so a margin/debt balance imported from a CSV isn't
    // silently floored at zero - see ImportBalanceFromCsv below.
    private readonly NumericUpDown _balanceBox = new()
    {
        Dock = DockStyle.Fill, DecimalPlaces = 2, Minimum = -100_000_000, Maximum = 100_000_000, ThousandsSeparator = true, Enabled = false
    };
    private readonly DateTimePicker _balanceAsOfBox = new()
    {
        Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short, Enabled = false
    };
    private readonly Button _importBalanceButton = new() { Text = "Import from CSV...", AutoSize = true, Anchor = AnchorStyles.Left };
    // AcceptsReturn = true is required here - without it, a multiline TextBox still
    // sends the Enter key up to the form's AcceptButton (Save) instead of inserting a
    // newline, so pressing Enter while typing Notes/Extra info would save-and-close
    // the dialog instead of starting a new line.
    private readonly TextBox _notesBox = new() { Dock = DockStyle.Fill, Multiline = true, Height = 55, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    private readonly TextBox _extraFieldsBox = new() { Dock = DockStyle.Fill, Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };

    /// <summary>
    /// knownCategories is every category currently in use across the vault (plus the
    /// built-in defaults) - passed in by MainForm so this dropdown always reflects any
    /// custom categories other accounts have already added, not just the fixed list.
    /// Falls back to AccountCategories.Defaults if null/empty (e.g. called from a test
    /// or some other context that doesn't have a vault to scan).
    /// </summary>
    public AccountEditForm(
        AccountEntry entry,
        string defaultOwnerName = "",
        string? defaultBrowserPath = null,
        IEnumerable<string>? knownCategories = null,
        Func<string, string[]?>? getCustomCategoryFields = null,
        Action<string, string[]>? saveCustomCategoryFields = null)
    {
        _entry = entry;
        _defaultOwnerName = defaultOwnerName;
        _defaultBrowserPath = defaultBrowserPath;
        _getCustomCategoryFields = getCustomCategoryFields;
        _saveCustomCategoryFields = saveCustomCategoryFields;

        Text = "Account Details";
        Width = 540;
        Height = 840;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var categoryItems = knownCategories?.Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (categoryItems == null || categoryItems.Length == 0)
            categoryItems = AccountCategories.Defaults;
        _categoryBox.Items.AddRange(categoryItems.Cast<object>().ToArray());

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

        var categoryPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        categoryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        categoryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        categoryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        categoryPanel.Controls.Add(_categoryBox, 0, 0);
        categoryPanel.Controls.Add(_suggestFieldsButton, 1, 0);
        categoryPanel.Controls.Add(_addCategoryButton, 2, 0);

        int row = 0;
        AddRow(layout, ref row, "Category:", categoryPanel);
        AddRow(layout, ref row, "Name:", _nameBox);
        AddRow(layout, ref row, "Institution:", _institutionBox);
        AddRow(layout, ref row, "Owner:", _ownerBox);
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

        var websitePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        websitePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        websitePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        websitePanel.Controls.Add(_websiteBox, 0, 0);
        websitePanel.Controls.Add(_openWebsiteButton, 1, 0);
        AddRow(layout, ref row, "Website:", websitePanel);

        AddRow(layout, ref row, "Phone:", _phoneBox);

        var duePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        duePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        duePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        duePanel.Controls.Add(_hasDueDateBox, 0, 0);
        duePanel.Controls.Add(_dueDatePicker, 1, 0);
        AddRow(layout, ref row, "Due date:", duePanel);

        AddRow(layout, ref row, "Repeats:", _recurrenceBox);
        AddRow(layout, ref row, "", _autoPaymentBox);

        // Amount due: a fixed balance owed (tuition/college fees, a remaining loan
        // payoff, etc.) - distinct from the recurring Due date/Repeats above, which are
        // about when the next bill is due rather than a running amount owed.
        var amountDuePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        amountDuePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        amountDuePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        amountDuePanel.Controls.Add(_hasAmountDueBox, 0, 0);
        amountDuePanel.Controls.Add(_amountDueBox, 1, 0);
        AddRow(layout, ref row, "Amount due:", amountDuePanel);

        // Current balance: a point-in-time snapshot (bank or investment account, etc.)
        // used by the Overview tab's "Total On Hand" figure - works for any category,
        // not just bank accounts, since investment accounts need the same thing.
        var balancePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        balancePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        balancePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        balancePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        balancePanel.Controls.Add(_hasBalanceBox, 0, 0);
        balancePanel.Controls.Add(_balanceBox, 1, 0);
        balancePanel.Controls.Add(_balanceAsOfBox, 2, 0);
        AddRow(layout, ref row, "Current balance:", balancePanel);
        AddRow(layout, ref row, "", _importBalanceButton);

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
        _suggestFieldsButton.Click += (_, _) => ApplySuggestedFields();
        _addCategoryButton.Click += (_, _) => AddNewCategory();
        _openWebsiteButton.Click += (_, _) => OpenWebsite();
        _hasAmountDueBox.CheckedChanged += (_, _) => _amountDueBox.Enabled = _hasAmountDueBox.Checked;
        _hasBalanceBox.CheckedChanged += (_, _) =>
        {
            _balanceBox.Enabled = _hasBalanceBox.Checked;
            _balanceAsOfBox.Enabled = _hasBalanceBox.Checked;
        };
        _importBalanceButton.Click += (_, _) => ImportBalanceFromCsv();

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
        // The category list is normally seeded from every category in use across the
        // vault (see MainForm.KnownCategories), so this entry's own category should
        // already be in there - but if it somehow isn't (e.g. a category was renamed
        // elsewhere, or this entry was created before that category existed), add it
        // rather than silently showing a blank/wrong selection.
        if (!_categoryBox.Items.Cast<string>().Any(c => string.Equals(c, _entry.Category, StringComparison.OrdinalIgnoreCase)))
            _categoryBox.Items.Add(string.IsNullOrWhiteSpace(_entry.Category) ? AccountCategories.Default : _entry.Category);
        _categoryBox.SelectedItem = _categoryBox.Items.Cast<string>()
            .FirstOrDefault(c => string.Equals(c, _entry.Category, StringComparison.OrdinalIgnoreCase))
            ?? _categoryBox.Items[0];

        _nameBox.Text = _entry.Name;
        _institutionBox.Text = _entry.Institution;
        _ownerBox.Text = string.IsNullOrWhiteSpace(_entry.Owner) ? _defaultOwnerName : _entry.Owner;
        _userNameBox.Text = _entry.UserName;
        _passwordBox.Text = _entry.Password;
        _accountNumberBox.Text = _entry.AccountNumber;
        _websiteBox.Text = _entry.Website;
        _phoneBox.Text = _entry.PhoneNumber;
        _recurrenceBox.SelectedItem = _entry.Recurrence.ToString();
        _autoPaymentBox.Checked = _entry.IsAutomaticPayment;
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

        if (_entry.AmountDue.HasValue)
        {
            _hasAmountDueBox.Checked = true;
            _amountDueBox.Enabled = true;
            _amountDueBox.Value = ClampToRange(_amountDueBox, _entry.AmountDue.Value);
        }
        else
        {
            _hasAmountDueBox.Checked = false;
            _amountDueBox.Enabled = false;
        }

        if (_entry.CurrentBalance.HasValue)
        {
            _hasBalanceBox.Checked = true;
            _balanceBox.Enabled = true;
            _balanceBox.Value = ClampToRange(_balanceBox, _entry.CurrentBalance.Value);
            _balanceAsOfBox.Enabled = true;
            _balanceAsOfBox.Value = _entry.CurrentBalanceAsOf ?? DateTime.Now;
        }
        else
        {
            _hasBalanceBox.Checked = false;
            _balanceBox.Enabled = false;
            _balanceAsOfBox.Enabled = false;
        }
    }

    private static decimal ClampToRange(NumericUpDown box, decimal value) =>
        Math.Min(box.Maximum, Math.Max(box.Minimum, value));

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show(this, "Please enter a name for this account.", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var categoryText = _categoryBox.SelectedItem as string ?? AccountCategories.Default;
        var recurrenceText = _recurrenceBox.SelectedItem as string ?? nameof(RecurrenceType.None);

        var previousDueDate = _entry.DueDate;

        _entry.Category = categoryText;
        _entry.Name = _nameBox.Text.Trim();
        _entry.Institution = _institutionBox.Text.Trim();
        _entry.Owner = _ownerBox.Text.Trim();
        _entry.UserName = _userNameBox.Text.Trim();
        _entry.Password = _passwordBox.Text;
        _entry.AccountNumber = _accountNumberBox.Text.Trim();
        _entry.Website = _websiteBox.Text.Trim();
        _entry.PhoneNumber = _phoneBox.Text.Trim();
        _entry.Recurrence = Enum.Parse<RecurrenceType>(recurrenceText);
        _entry.IsAutomaticPayment = _autoPaymentBox.Checked;
        _entry.Notes = _notesBox.Text;
        _entry.DueDate = _hasDueDateBox.Checked ? _dueDatePicker.Value.Date : null;
        _entry.AmountDue = _hasAmountDueBox.Checked ? _amountDueBox.Value : null;
        _entry.CurrentBalance = _hasBalanceBox.Checked ? _balanceBox.Value : null;
        _entry.CurrentBalanceAsOf = _hasBalanceBox.Checked ? _balanceAsOfBox.Value.Date : null;

        // If the due date actually changed, start the reminder cycle over for it.
        if (_entry.DueDate != previousDueDate)
            _entry.LastNotifiedOn = null;

        _entry.ExtraFields = _extraFieldsBox.Lines
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

        DialogResult = DialogResult.OK;
    }

    /// <summary>
    /// Lets the user type a brand-new category (e.g. "Tuition") right from this form
    /// instead of being limited to the built-in list. Adding it here only affects this
    /// dropdown's items and this entry's selection - it becomes a "known" category
    /// vault-wide (showing up in MainForm's filter and future Account Details dropdowns
    /// too) once this entry is actually saved, since categories are derived from
    /// whatever's currently in use rather than stored as a separate list.
    /// </summary>
    private void AddNewCategory()
    {
        using var dialog = new TextInputForm("New Category", "Category name:");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var name = dialog.Value;
        if (string.IsNullOrEmpty(name)) return;

        var existing = _categoryBox.Items.Cast<string>()
            .FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            _categoryBox.Items.Add(name);
            existing = name;
        }

        _categoryBox.SelectedItem = existing;
    }

    /// <summary>
    /// Opens the Website field - in the browser set on the Profile form
    /// (_defaultBrowserPath), or the system default browser if none was set. Accepts a
    /// bare domain (e.g. "statefarm.com") as well as a full URL via
    /// BrowserLauncher.TryParseUrl.
    /// </summary>
    private void OpenWebsite()
    {
        var uri = BrowserLauncher.TryParseUrl(_websiteBox.Text);
        if (uri == null)
        {
            MessageBox.Show(this, "That doesn't look like a valid website address.", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            BrowserLauncher.Open(uri, _defaultBrowserPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open that website: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Reads a balance/positions CSV downloaded from a brokerage (Schwab, Fidelity,
    /// Robinhood, M1, or similar) and offers to fill in Current Balance/As Of from it.
    /// None of these brokerages have a common export format (and several don't offer
    /// any official way to pull account data automatically at all), so this is a
    /// best-effort reader - see Utils/BalanceCsvImporter - that always shows its guess
    /// in a confirmation dialog (ImportBalanceForm) rather than applying it silently.
    /// Nothing is saved to the account until Save is clicked on this form, same as
    /// every other field here.
    /// </summary>
    private void ImportBalanceFromCsv()
    {
        using var fileDialog = new OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Select a balance/positions file downloaded from your brokerage"
        };
        if (fileDialog.ShowDialog(this) != DialogResult.OK) return;

        BalanceCsvImporter.Result result;
        try
        {
            result = BalanceCsvImporter.Analyze(fileDialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not read that file: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var confirmDialog = new ImportBalanceForm(Path.GetFileName(fileDialog.FileName), result);
        if (confirmDialog.ShowDialog(this) != DialogResult.OK) return;

        _hasBalanceBox.Checked = true;
        _balanceBox.Enabled = true;
        _balanceBox.Value = ClampToRange(_balanceBox, confirmDialog.Amount);
        _balanceAsOfBox.Enabled = true;
        _balanceAsOfBox.Value = confirmDialog.AsOfDate;
    }

    /// <summary>
    /// Inserts blank "Label=" placeholders for whatever fields are typical for the
    /// selected category (e.g. APR/term for a car loan, premium/policy for insurance)
    /// into the Extra info box, skipping any label already present. Purely a
    /// convenience over typing them by hand - the underlying ExtraFields dictionary is
    /// unchanged, so nothing here is a data-model change and any field can still be
    /// renamed or removed freely.
    ///
    /// For a custom (user-added) category with no built-in suggestions, this now
    /// offers to define one on the spot (see GetSuggestedFields/SaveCustomCategoryFields
    /// below) instead of just saying "no suggestions" - the definition is remembered
    /// vault-wide, so it's only ever typed once per category.
    /// </summary>
    private void ApplySuggestedFields()
    {
        if (_categoryBox.SelectedItem is not string categoryText || string.IsNullOrWhiteSpace(categoryText))
            return;

        var suggested = GetSuggestedFields(categoryText);
        if (suggested.Length == 0)
        {
            suggested = OfferToDefineCategoryFields(categoryText);
            if (suggested.Length == 0) return;
        }

        InsertFieldPlaceholders(suggested);
    }

    /// <summary>Built-in suggestions first (Models/AccountEntry.cs -> CategoryFieldSpec), then this vault's own custom-category definitions.</summary>
    private string[] GetSuggestedFields(string category)
    {
        var builtIn = CategoryFieldSpec.For(category);
        if (builtIn.Length > 0) return builtIn;

        return _getCustomCategoryFields?.Invoke(category) ?? Array.Empty<string>();
    }

    /// <summary>
    /// Asks whether to define suggested fields for a category that doesn't have any
    /// yet, and if so, saves the definition (via _saveCustomCategoryFields, wired up by
    /// MainForm to write into VaultData.CustomCategoryFields and save the vault) so
    /// every future account in this category offers the same fields. Returns the
    /// fields just defined, or an empty array if the user declined/entered nothing.
    /// </summary>
    private string[] OfferToDefineCategoryFields(string categoryText)
    {
        if (_saveCustomCategoryFields == null)
        {
            MessageBox.Show(this, "No suggested fields for this category.", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Array.Empty<string>();
        }

        var offer = MessageBox.Show(this,
            $"There are no suggested fields set up for \"{categoryText}\" yet.\n\n" +
            "Define some now? For example: Policy Number, Premium Amount\n\n" +
            "This is remembered for every account in this category from now on, not just this one.",
            "Personal Vault", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (offer != DialogResult.Yes) return Array.Empty<string>();

        using var dialog = new TextInputForm("Define Category Fields",
            $"Field names for \"{categoryText}\", separated by commas:");
        if (dialog.ShowDialog(this) != DialogResult.OK) return Array.Empty<string>();

        var fields = dialog.Value.Split(',')
            .Select(f => f.Trim())
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (fields.Length == 0) return Array.Empty<string>();

        _saveCustomCategoryFields(categoryText, fields);
        return fields;
    }

    private void InsertFieldPlaceholders(string[] suggested)
    {
        var existingKeys = _extraFieldsBox.Lines
            .Select(line => line.Split('=', 2)[0].Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = suggested.Where(f => !existingKeys.Contains(f)).ToArray();
        if (toAdd.Length == 0) return;

        _extraFieldsBox.Lines = _extraFieldsBox.Lines.Concat(toAdd.Select(f => f + "=")).ToArray();
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
