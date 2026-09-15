using System.Drawing;
using System.Windows.Forms;

namespace PersonalVault.Forms;

/// <summary>
/// Collects the current secret plus a new one (with confirmation). The actual
/// re-encryption happens in TrayApplicationContext.ChangeSecret, which verifies
/// OldSecret against the currently-unlocked vault before accepting NewSecret.
/// </summary>
public class ChangeSecretForm : Form
{
    private readonly TextBox _oldBox = new() { PasswordChar = '●' };
    private readonly TextBox _newBox = new() { PasswordChar = '●' };
    private readonly TextBox _confirmBox = new() { PasswordChar = '●' };
    private readonly Label _errorLabel = new() { ForeColor = Color.Firebrick, AutoSize = true };
    private readonly Button _okButton = new() { Text = "Change Secret", AutoSize = true };

    public string OldSecret => _oldBox.Text;
    public string NewSecret => _newBox.Text;

    public ChangeSecretForm()
    {
        Text = "Change Master Secret";
        Width = 420;
        Height = 300;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var note = new Label
        {
            AutoSize = false,
            Width = 380,
            Height = 40,
            Location = new Point(15, 12),
            Text = "This re-encrypts the entire vault file (and re-uploads it to Google Drive if connected)."
        };
        Controls.Add(note);

        int y = note.Bottom + 8;

        Controls.Add(new Label { Text = "Current secret:", AutoSize = true, Location = new Point(15, y + 4) });
        _oldBox.Location = new Point(150, y);
        _oldBox.Width = 230;
        Controls.Add(_oldBox);
        y += 32;

        Controls.Add(new Label { Text = "New secret:", AutoSize = true, Location = new Point(15, y + 4) });
        _newBox.Location = new Point(150, y);
        _newBox.Width = 230;
        Controls.Add(_newBox);
        y += 32;

        Controls.Add(new Label { Text = "Confirm new secret:", AutoSize = true, Location = new Point(15, y + 4) });
        _confirmBox.Location = new Point(150, y);
        _confirmBox.Width = 230;
        Controls.Add(_confirmBox);
        y += 32;

        _errorLabel.Location = new Point(15, y + 4);
        _errorLabel.MaximumSize = new Size(380, 0);
        Controls.Add(_errorLabel);
        y += 40;

        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        cancelButton.Location = new Point(210, y);
        Controls.Add(cancelButton);

        _okButton.Location = new Point(295, y);
        _okButton.Click += OkButton_Click;
        Controls.Add(_okButton);

        AcceptButton = _okButton;
        CancelButton = cancelButton;
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        _errorLabel.Text = "";

        if (string.IsNullOrWhiteSpace(_oldBox.Text))
        {
            _errorLabel.Text = "Enter your current secret.";
            return;
        }

        if (_newBox.Text.Length < 8)
        {
            _errorLabel.Text = "New secret must be at least 8 characters.";
            return;
        }

        if (_newBox.Text != _confirmBox.Text)
        {
            _errorLabel.Text = "New secrets do not match.";
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
