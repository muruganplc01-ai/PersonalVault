using System.Windows.Forms;

namespace PersonalVault.Forms;

/// <summary>
/// Small reusable "enter one line of text" dialog. First used for adding a new account
/// category from Account Details, but deliberately generic (title/prompt are passed in)
/// so anything else that just needs one text value back can reuse it too.
/// </summary>
public class TextInputForm : Form
{
    private readonly TextBox _inputBox = new() { Dock = DockStyle.Fill };

    /// <summary>The trimmed text the user entered. Only meaningful when the dialog returns DialogResult.OK.</summary>
    public string Value => _inputBox.Text.Trim();

    public TextInputForm(string title, string prompt, string initialValue = "")
    {
        Text = title;
        Width = 380;
        Height = 160;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var promptLabel = new Label
        {
            Text = prompt,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(12, 12, 12, 6)
        };

        _inputBox.Text = initialValue;
        var inputPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(12, 0, 12, 0) };
        inputPanel.Controls.Add(_inputBox);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var okButton = new Button { Text = "OK", AutoSize = true };
        okButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_inputBox.Text))
            {
                MessageBox.Show(this, "Please enter a value.", "Personal Vault",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        // Dock=Top siblings stack with the LAST Controls.Add() closest to the top edge,
        // so inputPanel is added before promptLabel to put the label above the box.
        Controls.Add(inputPanel);
        Controls.Add(promptLabel);
        Controls.Add(buttonPanel);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }
}
