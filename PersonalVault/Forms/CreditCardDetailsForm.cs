using System.Windows.Forms;

namespace PersonalVault.Forms;

/// <summary>
/// Structured "Card Details" popup for CreditCard-category accounts, reachable from
/// AccountEditForm's "Card Details..." button (shown only when Category is
/// "CreditCard"). Card Number feeds back into the account's existing Account # field on
/// Save (so it shows up in the same place as every other account's identifying number,
/// with no new field on AccountEntry needed); Expiration Month/Year, Security Code and
/// Cardholder Name are written into ExtraFields instead, the same free-form storage
/// this app already uses for category-specific data (see Models/AccountEntry.cs's
/// CategoryFieldSpec) - this popup just offers real dropdowns/formatting for the fields
/// that benefit from it instead of typing "key=value" lines by hand.
/// </summary>
public class CreditCardDetailsForm : Form
{
    private readonly TextBox _cardNumberBox = new() { Dock = DockStyle.Fill, MaxLength = 19 };
    private readonly ComboBox _monthBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _yearBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _securityCodeBox = new() { Dock = DockStyle.Fill, MaxLength = 4 };
    private readonly TextBox _cardholderNameBox = new() { Dock = DockStyle.Fill };

    // Guards CardNumberBox_TextChanged against re-entering itself when it rewrites
    // _cardNumberBox.Text below to insert/remove spacing.
    private bool _formattingCardNumber;

    /// <summary>Digits and spaces as shown in the box, e.g. "4111 1111 1111 1111" - AccountEditForm writes this straight into the Account # field.</summary>
    public string CardNumber => _cardNumberBox.Text.Trim();
    public string ExpirationMonth => _monthBox.SelectedItem as string ?? string.Empty;
    public string ExpirationYear => _yearBox.SelectedItem as string ?? string.Empty;
    public string SecurityCode => _securityCodeBox.Text.Trim();
    public string CardholderName => _cardholderNameBox.Text.Trim();

    public CreditCardDetailsForm(string cardNumber, string expirationMonth, string expirationYear, string securityCode, string cardholderName)
    {
        Text = "Card Details";
        Width = 420;
        Height = 330;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        for (int month = 1; month <= 12; month++)
            _monthBox.Items.Add(month.ToString("00"));

        int currentYear = DateTime.Now.Year;
        for (int year = currentYear; year <= currentYear + 15; year++)
            _yearBox.Items.Add(year.ToString());

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
        AddRow(layout, ref row, "Card Number:", _cardNumberBox);

        var expiryPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        expiryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        expiryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        expiryPanel.Controls.Add(_monthBox, 0, 0);
        expiryPanel.Controls.Add(_yearBox, 1, 0);
        AddRow(layout, ref row, "Expiration (MM/YYYY):", expiryPanel);

        AddRow(layout, ref row, "Security Code:", _securityCodeBox);
        AddRow(layout, ref row, "Cardholder Name:", _cardholderNameBox);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var saveButton = new Button { Text = "Save", AutoSize = true };
        saveButton.Click += (_, _) => DialogResult = DialogResult.OK;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);

        Controls.Add(layout);
        Controls.Add(buttonPanel);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        _cardNumberBox.TextChanged += CardNumberBox_TextChanged;
        _securityCodeBox.TextChanged += (_, _) => KeepDigitsOnly(_securityCodeBox);

        _cardNumberBox.Text = cardNumber;
        if (!string.IsNullOrEmpty(expirationMonth))
            _monthBox.SelectedItem = _monthBox.Items.Cast<string>().FirstOrDefault(m => m == expirationMonth);
        if (!string.IsNullOrEmpty(expirationYear))
            _yearBox.SelectedItem = _yearBox.Items.Cast<string>().FirstOrDefault(y => y == expirationYear);
        _securityCodeBox.Text = securityCode;
        _cardholderNameBox.Text = cardholderName;
    }

    private static void AddRow(TableLayoutPanel layout, ref int row, string label, Control control)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) }, 0, row);
        layout.Controls.Add(control, 1, row);
        row++;
    }

    /// <summary>Formats digits as "0000 0000 0000 0000" while typing (up to 16 digits), same masked-input feel as a real card-entry form.</summary>
    private void CardNumberBox_TextChanged(object? sender, EventArgs e)
    {
        if (_formattingCardNumber) return;

        var digits = new string(_cardNumberBox.Text.Where(char.IsDigit).ToArray());
        if (digits.Length > 16) digits = digits[..16];

        var groups = Enumerable.Range(0, (digits.Length + 3) / 4)
            .Select(i => digits.Substring(i * 4, Math.Min(4, digits.Length - i * 4)));
        var formatted = string.Join(" ", groups);

        if (formatted == _cardNumberBox.Text) return;

        _formattingCardNumber = true;
        _cardNumberBox.Text = formatted;
        _cardNumberBox.SelectionStart = _cardNumberBox.Text.Length;
        _formattingCardNumber = false;
    }

    private static void KeepDigitsOnly(TextBox box)
    {
        var digits = new string(box.Text.Where(char.IsDigit).ToArray());
        if (digits == box.Text) return;

        int caret = box.SelectionStart;
        box.Text = digits;
        box.SelectionStart = Math.Min(caret, box.Text.Length);
    }
}
