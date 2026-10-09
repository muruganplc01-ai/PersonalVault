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
    private readonly Func<string, CategoryDefault?>? _getCustomCategoryDefault;
    private readonly Action<string, CategoryDefault>? _saveCustomCategoryDefault;
    private readonly Func<string, IEnumerable<string>>? _getKnownSubCategories;
    private readonly IReadOnlyDictionary<string, List<FieldDefinition>> _vaultCategoryFieldSets;
    private readonly bool _isNewEntry;

    /// <summary>
    /// Every control belonging to a given reserved field caption (see
    /// Models/AccountEntry.cs -> CategoryFieldSetDefaults.ReservedCaptions), populated
    /// via Track() as each row is built below - lets ApplyFieldSetVisibility toggle an
    /// entire field (label + input, or every row in a multi-row group like "Due Date")
    /// on or off together, driven by the active category's field set.
    /// </summary>
    private readonly Dictionary<string, List<Control>> _fieldControls = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that the given controls all belong to one reserved field caption, appending across multiple calls (e.g. the 3 rows making up "Due Date").</summary>
    private void Track(string caption, params Control[] controls)
    {
        if (!_fieldControls.TryGetValue(caption, out var list))
            _fieldControls[caption] = list = new List<Control>();
        list.AddRange(controls);
    }

    private const string CreditCardCategory = "CreditCard";
    private const string BankAccountCategory = "BankAccount";

    private readonly ComboBox _categoryBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button _suggestFieldsButton = new() { Text = "+ Category Fields", AutoSize = true };
    private readonly Button _addCategoryButton = new() { Text = "+ New...", AutoSize = true };
    private readonly Label _cardDetailsLabel = new() { Text = "Card Details:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) };
    private readonly Button _cardDetailsButton = new() { Text = "Enter Card Details...", AutoSize = true };
    private readonly Label _bankAccountsLabel = new() { Text = "Bank Accounts:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) };
    private readonly Button _bankAccountsButton = new() { Text = "Checking, Savings, etc...", AutoSize = true };
    private List<BankSubAccount> _workingSubAccounts = new();
    private readonly TextBox _nameBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _institutionBox = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _ownerBox = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
    private readonly ComboBox _subCategoryBox = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
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
    private readonly CheckBox _hasAssetValueBox = new() { Text = "Track asset value", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly NumericUpDown _assetValueBox = new()
    {
        Dock = DockStyle.Fill, DecimalPlaces = 2, Minimum = 0, Maximum = 100_000_000, ThousandsSeparator = true, Enabled = false
    };
    private readonly DateTimePicker _assetValueAsOfBox = new()
    {
        Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short, Enabled = false
    };
    // AcceptsReturn = true is required here - without it, a multiline TextBox still
    // sends the Enter key up to the form's AcceptButton (Save) instead of inserting a
    // newline, so pressing Enter while typing Notes/Extra info would save-and-close
    // the dialog instead of starting a new line.
    private readonly TextBox _notesBox = new() { Dock = DockStyle.Fill, Multiline = true, Height = 55, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    private readonly TextBox _extraFieldsBox = new() { Dock = DockStyle.Fill, Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    private readonly Button _expandNotesButton = new() { Text = "⤢", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
    private readonly Button _expandExtraFieldsButton = new() { Text = "⤢", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };

    // --- Custom (non-reserved-caption) fields from the active category's field set -
    // rebuilt every category change by RebuildCustomFieldsPanel. Stored in
    // AccountEntry.ExtraFields keyed by caption, same as the reserved "Extra Info"
    // memo box - see SaveButton_Click for how the two are kept from clobbering each other.
    private readonly FlowLayoutPanel _customFieldsPanel = new() { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
    private readonly Dictionary<string, Control> _customFieldControls = new(StringComparer.OrdinalIgnoreCase);

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
        Action<string, string[]>? saveCustomCategoryFields = null,
        bool isNewEntry = false,
        Func<string, CategoryDefault?>? getCustomCategoryDefault = null,
        Action<string, CategoryDefault>? saveCustomCategoryDefault = null,
        IEnumerable<string>? knownOwners = null,
        Func<string, IEnumerable<string>>? getKnownSubCategories = null,
        IReadOnlyDictionary<string, List<FieldDefinition>>? categoryFieldSets = null)
    {
        _entry = entry;
        _defaultOwnerName = defaultOwnerName;
        _defaultBrowserPath = defaultBrowserPath;
        _getCustomCategoryFields = getCustomCategoryFields;
        _saveCustomCategoryFields = saveCustomCategoryFields;
        _isNewEntry = isNewEntry;
        _getCustomCategoryDefault = getCustomCategoryDefault;
        _saveCustomCategoryDefault = saveCustomCategoryDefault;
        _getKnownSubCategories = getKnownSubCategories;
        _vaultCategoryFieldSets = categoryFieldSets ?? new Dictionary<string, List<FieldDefinition>>();

        _ownerBox.Items.AddRange((knownOwners ?? Enumerable.Empty<string>()).Cast<object>().ToArray());

        Text = "Account Details";
        Width = 540;
        Height = 840;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        // F10 (or the ⤢ buttons next to Notes/Extra info) pops whichever one has focus
        // out into a full-size editor - see OpenExpandedEditor. KeyPreview so the form
        // sees F10 before any individual control would otherwise consume/ignore it.
        KeyPreview = true;

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
        AddRow(layout, ref row, "Category:", categoryPanel); // Category itself is never hidden - it's the selector driving everything else.
        Track("Name", AddRow(layout, ref row, "Name:", _nameBox), _nameBox);
        Track("Institution", AddRow(layout, ref row, "Institution:", _institutionBox), _institutionBox);
        Track("Owner", AddRow(layout, ref row, "Owner:", _ownerBox), _ownerBox);
        Track("Sub Category", AddRow(layout, ref row, "Sub Category:", _subCategoryBox), _subCategoryBox);
        Track("Username", AddRow(layout, ref row, "Username:", _userNameBox), _userNameBox);

        var passwordPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        passwordPanel.Controls.Add(_passwordBox, 0, 0);
        passwordPanel.Controls.Add(_showPasswordBox, 1, 0);
        passwordPanel.Controls.Add(_generateButton, 2, 0);
        Track("Password", AddRow(layout, ref row, "Password:", passwordPanel), passwordPanel);

        // Account # is hideable per category like everything else, but its Save is
        // deliberately NEVER gated by that visibility (see SaveButton_Click) - CreditCard's
        // Card Details popup writes the card number directly into this same textbox, and
        // that value must never be silently lost no matter how a category's field set
        // is configured later.
        Track("Account #", AddRow(layout, ref row, "Account #:", _accountNumberBox), _accountNumberBox);

        // Only shown for the CreditCard category - see UpdateCardDetailsVisibility (and, as of
        // the per-category field set feature, ApplyFieldSetVisibility - the two work together:
        // a category must both be CreditCard AND have "Card Details" in its field set).
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_cardDetailsLabel, 0, row);
        layout.Controls.Add(_cardDetailsButton, 1, row);
        row++;
        Track("Card Details", _cardDetailsLabel, _cardDetailsButton);

        // Only shown for the BankAccount category - see UpdateBankAccountsVisibility (same
        // both-must-agree relationship with ApplyFieldSetVisibility as Card Details above).
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_bankAccountsLabel, 0, row);
        layout.Controls.Add(_bankAccountsButton, 1, row);
        row++;
        Track("Bank Accounts", _bankAccountsLabel, _bankAccountsButton);

        var websitePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        websitePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        websitePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        websitePanel.Controls.Add(_websiteBox, 0, 0);
        websitePanel.Controls.Add(_openWebsiteButton, 1, 0);
        Track("Website", AddRow(layout, ref row, "Website:", websitePanel), websitePanel);

        Track("Phone", AddRow(layout, ref row, "Phone:", _phoneBox), _phoneBox);

        var duePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        duePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        duePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        duePanel.Controls.Add(_hasDueDateBox, 0, 0);
        duePanel.Controls.Add(_dueDatePicker, 1, 0);
        // "Due Date" as a reserved caption covers this whole group - the due date picker,
        // Repeats, and Autopay all show/hide together, since Repeats/Autopay are meaningless
        // without a due date to repeat.
        Track("Due Date", AddRow(layout, ref row, "Due date:", duePanel), duePanel);
        Track("Due Date", AddRow(layout, ref row, "Repeats:", _recurrenceBox), _recurrenceBox);
        Track("Due Date", AddRow(layout, ref row, "", _autoPaymentBox), _autoPaymentBox);

        // Amount due: a fixed balance owed (tuition/college fees, a remaining loan
        // payoff, etc.) - distinct from the recurring Due date/Repeats above, which are
        // about when the next bill is due rather than a running amount owed.
        var amountDuePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        amountDuePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        amountDuePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        amountDuePanel.Controls.Add(_hasAmountDueBox, 0, 0);
        amountDuePanel.Controls.Add(_amountDueBox, 1, 0);
        Track("Amount Due", AddRow(layout, ref row, "Amount due:", amountDuePanel), amountDuePanel);

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
        Track("Current Balance", AddRow(layout, ref row, "Current balance:", balancePanel), balancePanel);
        Track("Current Balance", AddRow(layout, ref row, "", _importBalanceButton), _importBalanceButton);

        // Asset value: what the underlying thing is worth (e.g. a home or car) -
        // distinct from Current Balance above, which for Mortgage/CarLoan is what you
        // still OWE. Totalled separately in the Overview tab, never summed together
        // with Current Balance - see MainForm.RefreshOverview.
        var assetValuePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        assetValuePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        assetValuePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        assetValuePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        assetValuePanel.Controls.Add(_hasAssetValueBox, 0, 0);
        assetValuePanel.Controls.Add(_assetValueBox, 1, 0);
        assetValuePanel.Controls.Add(_assetValueAsOfBox, 2, 0);
        Track("Asset Value", AddRow(layout, ref row, "Asset value:", assetValuePanel), assetValuePanel);

        var notesPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        notesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        notesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        notesPanel.Controls.Add(_notesBox, 0, 0);
        notesPanel.Controls.Add(_expandNotesButton, 1, 0);
        Track("Notes", AddRow(layout, ref row, "Notes:", notesPanel), notesPanel);

        var extraFieldsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        extraFieldsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        extraFieldsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        extraFieldsPanel.Controls.Add(_extraFieldsBox, 0, 0);
        extraFieldsPanel.Controls.Add(_expandExtraFieldsButton, 1, 0);
        Track("Extra Info", AddRow(layout, ref row, "Extra info:\n(key=value,\none per line)", extraFieldsPanel), extraFieldsPanel);

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
        _cardDetailsButton.Click += (_, _) => OpenCardDetails();
        _bankAccountsButton.Click += (_, _) => OpenBankAccounts();
        _categoryBox.SelectedIndexChanged += (_, _) =>
        {
            UpdateCardDetailsVisibility();
            UpdateBankAccountsVisibility();
            RefreshSubCategoryItems();
            ApplyFieldSetVisibility();
            if (_isNewEntry) ApplyCategoryDefaults();
        };
        _openWebsiteButton.Click += (_, _) => OpenWebsite();
        _expandNotesButton.Click += (_, _) => OpenExpandedEditor(_notesBox, "Notes");
        _expandExtraFieldsButton.Click += (_, _) => OpenExpandedEditor(_extraFieldsBox, "Extra Info");
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.F10) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            OpenExpandedEditor(_extraFieldsBox.Focused ? _extraFieldsBox : _notesBox,
                _extraFieldsBox.Focused ? "Extra Info" : "Notes");
        };
        _hasAmountDueBox.CheckedChanged += (_, _) => _amountDueBox.Enabled = _hasAmountDueBox.Checked;
        _hasBalanceBox.CheckedChanged += (_, _) =>
        {
            _balanceBox.Enabled = _hasBalanceBox.Checked;
            _balanceAsOfBox.Enabled = _hasBalanceBox.Checked;
        };
        _importBalanceButton.Click += (_, _) => ImportBalanceFromCsv();
        _hasAssetValueBox.CheckedChanged += (_, _) =>
        {
            _assetValueBox.Enabled = _hasAssetValueBox.Checked;
            _assetValueAsOfBox.Enabled = _hasAssetValueBox.Checked;
        };

        _customFieldsPanel.Padding = new Padding(12, 0, 12, 0);

        var scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scrollPanel.Controls.Add(layout);
        scrollPanel.Controls.Add(_customFieldsPanel);

        Controls.Add(scrollPanel);
        Controls.Add(buttonPanel);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        LoadFromEntry();
        if (_isNewEntry) ApplyCategoryDefaults();
    }

    /// <summary>
    /// Pre-fills Repeats/Autopay/Institution with sensible starting values for the
    /// selected category (Models/AccountEntry.cs -> CategoryEntryDefaults) - only
    /// called while adding a brand-new entry, never while editing an existing one, so
    /// nothing here ever overwrites a value someone already set deliberately.
    /// </summary>
    private void ApplyCategoryDefaults()
    {
        var categoryText = _categoryBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(categoryText)) return;

        // Vault-specific, user-configured defaults (Profile -> "Category Defaults...")
        // take priority over the hardcoded built-in ones for the same category.
        var custom = _getCustomCategoryDefault?.Invoke(categoryText);
        if (custom != null)
        {
            _recurrenceBox.SelectedItem = custom.Recurrence.ToString();
            _autoPaymentBox.Checked = custom.Autopay;
            if (!string.IsNullOrEmpty(custom.Institution))
                _institutionBox.Text = custom.Institution;
            return;
        }

        var defaults = CategoryEntryDefaults.For(categoryText);
        if (defaults == null) return;

        _recurrenceBox.SelectedItem = defaults.Value.Recurrence.ToString();
        _autoPaymentBox.Checked = defaults.Value.Autopay;
        if (defaults.Value.Institution != null)
            _institutionBox.Text = defaults.Value.Institution;
    }

    /// <summary>
    /// Repopulates Sub Category's suggestions for whichever category is currently
    /// selected - scoped per-category (via _getKnownSubCategories) so e.g. Email's
    /// Work/Personal values don't show up as suggestions under CreditCard. Preserves
    /// whatever's already typed, since this runs on every category change including
    /// ones that don't actually change Sub Category's own value.
    /// </summary>
    private void RefreshSubCategoryItems()
    {
        var categoryText = _categoryBox.SelectedItem as string;
        var currentText = _subCategoryBox.Text;

        _subCategoryBox.Items.Clear();
        if (categoryText != null && _getKnownSubCategories != null)
            _subCategoryBox.Items.AddRange(_getKnownSubCategories(categoryText).Cast<object>().ToArray());

        _subCategoryBox.Text = currentText;
    }

    /// <summary>
    /// Shows/hides every reserved field (see Track() calls in the constructor and
    /// Models/AccountEntry.cs -> CategoryFieldSetDefaults) based on the active
    /// category's field set - vault-level override if configured (Profile -> "Field
    /// Sets..."), else the built-in starting point, else [Defaults]. Card Details/Bank
    /// Accounts still separately depend on the category literally being
    /// CreditCard/BankAccount (UpdateCardDetailsVisibility/UpdateBankAccountsVisibility) -
    /// both conditions must agree for those two to show.
    /// </summary>
    private void ApplyFieldSetVisibility()
    {
        var categoryText = _categoryBox.SelectedItem as string;
        var fieldSet = CategoryFieldSetDefaults.Resolve(categoryText, _vaultCategoryFieldSets);
        var captionsToShow = fieldSet.Select(f => f.Caption).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (caption, controls) in _fieldControls)
        {
            bool show = captionsToShow.Contains(caption);
            foreach (var control in controls)
                control.Visible = show;
        }

        RebuildCustomFieldsPanel(fieldSet);
    }

    /// <summary>
    /// Rebuilds the dynamic panel of custom (non-reserved-caption) fields for the
    /// active category - a genuinely new field typed into Profile -> "Field Sets..."
    /// that isn't one of the built-in ones. Rendered per its DataType (String/DropDown/
    /// Memo) and read from/written to AccountEntry.ExtraFields keyed by caption - see
    /// SaveButton_Click for how this and the reserved "Extra Info" memo box avoid
    /// clobbering each other. Rebuilding on every category change means an unsaved edit
    /// to a custom field is lost if you switch categories away and back before clicking
    /// Save - a known, minor limitation (reserved fields don't have this problem, since
    /// their controls stay alive and are just hidden/shown rather than torn down).
    /// </summary>
    private void RebuildCustomFieldsPanel(List<FieldDefinition> fieldSet)
    {
        _customFieldsPanel.Controls.Clear();
        _customFieldControls.Clear();

        var customFields = fieldSet.Where(f => !CategoryFieldSetDefaults.ReservedCaptions.Contains(f.Caption, StringComparer.OrdinalIgnoreCase));

        foreach (var field in customFields)
        {
            _entry.ExtraFields.TryGetValue(field.Caption, out var existingValue);

            Control valueControl;
            Control inputControl;

            if (field.DataType == FieldDataType.Memo)
            {
                var memoBox = new TextBox { Multiline = true, Height = 55, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AcceptsReturn = true, Text = existingValue ?? "" };
                var expandBtn = new Button { Text = "⤢", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
                expandBtn.Click += (_, _) => OpenExpandedEditor(memoBox, field.Caption);
                var memoPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
                memoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                memoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                memoPanel.Controls.Add(memoBox, 0, 0);
                memoPanel.Controls.Add(expandBtn, 1, 0);
                valueControl = memoPanel;
                inputControl = memoBox;
            }
            else if (field.DataType == FieldDataType.DropDown)
            {
                // No cross-entry suggestion list for a custom dropdown (would need yet
                // another vault-wide lookup delegate) - still fully usable as free text.
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Text = existingValue ?? "" };
                valueControl = combo;
                inputControl = combo;
            }
            else
            {
                var textBox = new TextBox { Dock = DockStyle.Fill, MaxLength = Math.Max(1, field.Size), Text = existingValue ?? "" };
                valueControl = textBox;
                inputControl = textBox;
            }

            var rowPanel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
            rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rowPanel.Controls.Add(new Label { Text = field.Caption + ":", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) }, 0, 0);
            rowPanel.Controls.Add(valueControl, 1, 0);
            _customFieldsPanel.Controls.Add(rowPanel);

            _customFieldControls[field.Caption] = inputControl;
        }
    }

    /// <summary>
    /// Whether a reserved field is currently part of the active category's field set -
    /// SaveButton_Click uses this so a field hidden by the category's configuration
    /// never has its underlying AccountEntry value overwritten (and definitely never
    /// cleared) just because Save was clicked. This is what makes hiding a field purely
    /// cosmetic rather than destructive.
    /// </summary>
    private bool IsFieldVisible(string caption) =>
        _fieldControls.TryGetValue(caption, out var controls) && controls.Count > 0 && controls[0].Visible;

    /// <summary>
    /// Pops a single multiline field (Notes or Extra info) out into a bigger, resizable
    /// window - triggered by its ⤢ button or by pressing F10 while that field has
    /// focus. Edits only take effect on this popup's own OK; Cancel discards them, same
    /// as everything else in this form not taking effect until the outer Save.
    /// </summary>
    private void OpenExpandedEditor(TextBox sourceBox, string title)
    {
        using var dialog = new Form
        {
            Text = title,
            Width = 640,
            Height = 480,
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(360, 240)
        };

        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            AcceptsTab = true,
            Font = new Font("Consolas", 10),
            Text = sourceBox.Text
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var okButton = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        dialog.Controls.Add(textBox);
        dialog.Controls.Add(buttonPanel);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) == DialogResult.OK)
            sourceBox.Text = textBox.Text;
    }

    private static Label AddRow(TableLayoutPanel layout, ref int row, string label, Control control)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var labelControl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) };
        layout.Controls.Add(labelControl, 0, row);
        layout.Controls.Add(control, 1, row);
        row++;
        return labelControl;
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
        UpdateCardDetailsVisibility();
        UpdateBankAccountsVisibility();
        RefreshSubCategoryItems();
        ApplyFieldSetVisibility();
        _workingSubAccounts = _entry.SubAccounts.Select(CloneSubAccount).ToList();

        _nameBox.Text = _entry.Name;
        _institutionBox.Text = _entry.Institution;
        _ownerBox.Text = string.IsNullOrWhiteSpace(_entry.Owner) ? _defaultOwnerName : _entry.Owner;
        _subCategoryBox.Text = _entry.SubCategory;
        _userNameBox.Text = _entry.UserName;
        _passwordBox.Text = _entry.Password;
        _accountNumberBox.Text = _entry.AccountNumber;
        _websiteBox.Text = _entry.Website;
        _phoneBox.Text = _entry.PhoneNumber;
        _recurrenceBox.SelectedItem = _entry.Recurrence.ToString();
        _autoPaymentBox.Checked = _entry.IsAutomaticPayment;
        _notesBox.Text = _entry.Notes;
        // Excludes anything rendered as its own dedicated custom field (see
        // RebuildCustomFieldsPanel, already run via ApplyFieldSetVisibility above) -
        // those are edited through their own control, not duplicated here too.
        _extraFieldsBox.Text = string.Join(Environment.NewLine, _entry.ExtraFields
            .Where(kv => !_customFieldControls.ContainsKey(kv.Key))
            .Select(kv => $"{kv.Key}={kv.Value}"));

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

        if (_entry.AssetValue.HasValue)
        {
            _hasAssetValueBox.Checked = true;
            _assetValueBox.Enabled = true;
            _assetValueBox.Value = ClampToRange(_assetValueBox, _entry.AssetValue.Value);
            _assetValueAsOfBox.Enabled = true;
            _assetValueAsOfBox.Value = _entry.AssetValueAsOf ?? DateTime.Now;
        }
        else
        {
            _hasAssetValueBox.Checked = false;
            _assetValueBox.Enabled = false;
            _assetValueAsOfBox.Enabled = false;
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

        // Account # is always saved regardless of field-set visibility - see its Track()
        // call site for why. Category itself is never hidden either.
        _entry.Category = categoryText;
        _entry.AccountNumber = _accountNumberBox.Text.Trim();

        // Every other reserved field only gets written back if it's actually part of
        // the active category's field set - a field hidden by configuration keeps
        // whatever value it already had on the entry, untouched. This is the safety
        // rule that makes hiding a field purely cosmetic, never destructive.
        if (IsFieldVisible("Name")) _entry.Name = _nameBox.Text.Trim();
        if (IsFieldVisible("Institution")) _entry.Institution = _institutionBox.Text.Trim();
        if (IsFieldVisible("Owner")) _entry.Owner = _ownerBox.Text.Trim();
        if (IsFieldVisible("Sub Category")) _entry.SubCategory = _subCategoryBox.Text.Trim();
        if (IsFieldVisible("Username")) _entry.UserName = _userNameBox.Text.Trim();
        if (IsFieldVisible("Password")) _entry.Password = _passwordBox.Text;
        if (IsFieldVisible("Website")) _entry.Website = _websiteBox.Text.Trim();
        if (IsFieldVisible("Phone")) _entry.PhoneNumber = _phoneBox.Text.Trim();
        if (IsFieldVisible("Notes")) _entry.Notes = _notesBox.Text;

        if (IsFieldVisible("Extra Info"))
        {
            var fromMemo = _extraFieldsBox.Lines
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                // Custom fields are edited through their own dedicated control, not
                // this memo box - a line here that happens to match one is ignored
                // rather than fought over between the two.
                .Where(parts => !_customFieldControls.ContainsKey(parts[0].Trim()))
                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

            // Preserve existing custom-field entries this replace doesn't know about -
            // otherwise wholesale-replacing ExtraFields here would wipe them out before
            // the custom-fields loop below gets a chance to write them back.
            foreach (var kv in _entry.ExtraFields)
                if (_customFieldControls.ContainsKey(kv.Key) && !fromMemo.ContainsKey(kv.Key))
                    fromMemo[kv.Key] = kv.Value;

            _entry.ExtraFields = fromMemo;
        }

        // Custom (non-reserved-caption) fields from the active category's field set -
        // always saved regardless of Extra Info's own visibility, and always after the
        // block above so they're never clobbered by its wholesale dictionary replace.
        foreach (var (caption, control) in _customFieldControls)
        {
            var value = control.Text.Trim();
            if (string.IsNullOrEmpty(value))
                _entry.ExtraFields.Remove(caption);
            else
                _entry.ExtraFields[caption] = value;
        }

        if (IsFieldVisible("Due Date"))
        {
            _entry.Recurrence = Enum.Parse<RecurrenceType>(recurrenceText);
            _entry.IsAutomaticPayment = _autoPaymentBox.Checked;
            _entry.DueDate = _hasDueDateBox.Checked ? _dueDatePicker.Value.Date : null;

            // If the due date actually changed, start the reminder cycle over for it.
            if (_entry.DueDate != previousDueDate)
                _entry.LastNotifiedOn = null;
        }

        if (IsFieldVisible("Amount Due"))
            _entry.AmountDue = _hasAmountDueBox.Checked ? _amountDueBox.Value : null;

        if (IsFieldVisible("Current Balance"))
        {
            _entry.CurrentBalance = _hasBalanceBox.Checked ? _balanceBox.Value : null;
            _entry.CurrentBalanceAsOf = _hasBalanceBox.Checked ? _balanceAsOfBox.Value.Date : null;
        }

        if (IsFieldVisible("Asset Value"))
        {
            _entry.AssetValue = _hasAssetValueBox.Checked ? _assetValueBox.Value : null;
            _entry.AssetValueAsOf = _hasAssetValueBox.Checked ? _assetValueAsOfBox.Value.Date : null;
        }

        _entry.SubAccounts = _workingSubAccounts;

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

        bool isGenuinelyNew = existing == null;
        if (isGenuinelyNew)
        {
            _categoryBox.Items.Add(name);
            existing = name;
        }

        _categoryBox.SelectedItem = existing;

        // Offer to set Repeats/Autopay/Institution defaults for this category right
        // here, instead of saving this entry and then going to Profile -> "Category
        // Defaults..." separately to do the same thing - same "offer it inline the
        // first time it's needed" idea as OfferToDefineCategoryFields below, just for
        // defaults instead of suggested field names. Only offered for a category that
        // didn't already exist (one already in use may well already have a default set).
        if (isGenuinelyNew && _saveCustomCategoryDefault != null)
        {
            var offer = MessageBox.Show(this,
                $"Set default Repeats/Autopay/Institution for \"{name}\" now?\n\n" +
                "This is remembered for every future account in this category, not just this one. " +
                "You can also do this later from Profile -> \"Category Defaults...\".",
                "Personal Vault", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (offer != DialogResult.Yes) return;

            using var defaultForm = new CategoryDefaultForm(name, null, _categoryBox.Items.Cast<string>());
            if (defaultForm.ShowDialog(this) != DialogResult.OK) return;

            _saveCustomCategoryDefault(defaultForm.Category, defaultForm.Result);
            if (_isNewEntry) ApplyCategoryDefaults(); // so this entry benefits immediately too, not just future ones
        }
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

    /// <summary>Shows/hides the "Card Details..." row based on the currently selected category - only relevant for CreditCard.</summary>
    private void UpdateCardDetailsVisibility()
    {
        bool isCreditCard = string.Equals(_categoryBox.SelectedItem as string, CreditCardCategory, StringComparison.OrdinalIgnoreCase);
        _cardDetailsLabel.Visible = isCreditCard;
        _cardDetailsButton.Visible = isCreditCard;
    }

    /// <summary>Shows/hides the "Bank Accounts..." row based on the currently selected category - only relevant for BankAccount.</summary>
    private void UpdateBankAccountsVisibility()
    {
        bool isBankAccount = string.Equals(_categoryBox.SelectedItem as string, BankAccountCategory, StringComparison.OrdinalIgnoreCase);
        _bankAccountsLabel.Visible = isBankAccount;
        _bankAccountsButton.Visible = isBankAccount;
    }

    /// <summary>
    /// Opens the Checking/Savings/Money Market list (BankAccountsForm), operating on
    /// _workingSubAccounts - a working copy so Cancel on this whole dialog discards any
    /// sub-account edits too, same as every other field here. Only actually applied to
    /// _entry in SaveButton_Click.
    /// </summary>
    private void OpenBankAccounts()
    {
        using var form = new BankAccountsForm(_workingSubAccounts);
        form.ShowDialog(this);
    }

    private static BankSubAccount CloneSubAccount(BankSubAccount source) => new()
    {
        Id = source.Id,
        Label = source.Label,
        AccountNumber = source.AccountNumber,
        RoutingNumber = source.RoutingNumber,
        Balance = source.Balance,
        BalanceAsOf = source.BalanceAsOf,
        Notes = source.Notes
    };

    /// <summary>
    /// Opens the structured Card Details popup (see cc.png-style Card
    /// Number/Expiration/Security Code/Cardholder Name layout in CreditCardDetailsForm),
    /// pre-filled from the Account # field and whatever's already in Extra info. On
    /// Save, the card number overwrites Account # (so it shows up in the same place any
    /// other account's identifying number does) and the other four values are merged
    /// into Extra info - nothing here is a new AccountEntry field, so older vault data
    /// isn't affected either way.
    /// </summary>
    private void OpenCardDetails()
    {
        var extraFields = ParseExtraFieldsBox();
        using var dialog = new CreditCardDetailsForm(
            _accountNumberBox.Text,
            extraFields.GetValueOrDefault("Expiration Month", string.Empty),
            extraFields.GetValueOrDefault("Expiration Year", string.Empty),
            extraFields.GetValueOrDefault("Security Code", string.Empty),
            extraFields.GetValueOrDefault("Cardholder Name", string.Empty));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _accountNumberBox.Text = dialog.CardNumber;
        UpsertExtraField("Cardholder Name", dialog.CardholderName);
        UpsertExtraField("Expiration Month", dialog.ExpirationMonth);
        UpsertExtraField("Expiration Year", dialog.ExpirationYear);
        UpsertExtraField("Security Code", dialog.SecurityCode);
    }

    /// <summary>Read-only parse of the Extra info box, for pre-filling the Card Details popup - not used for the actual Save (see SaveButton_Click, which parses it fresh with its own comparer).</summary>
    private Dictionary<string, string> ParseExtraFieldsBox() =>
        _extraFieldsBox.Lines
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Replaces (or removes, if value is blank) a single "key=value" line in the Extra info box, leaving every other line untouched - used by OpenCardDetails so re-opening it doesn't duplicate fields.</summary>
    private void UpsertExtraField(string key, string value)
    {
        var lines = _extraFieldsBox.Lines
            .Where(line => !string.Equals(line.Split('=', 2)[0].Trim(), key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!string.IsNullOrEmpty(value))
            lines.Add($"{key}={value}");

        _extraFieldsBox.Lines = lines.ToArray();
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
