using System.Drawing;
using System.Windows.Forms;
using PersonalVault.Security;

namespace PersonalVault.Forms;

public enum UnlockMode
{
    UnlockExisting,
    CreateNew,

    /// <summary>Unlocking a vault just downloaded from Google Drive (disaster-recovery path) - same secret entry as UnlockExisting, different wording.</summary>
    RestoreFromBackup
}

/// <summary>
/// Prompts for the master secret. In CreateNew mode it also asks for confirmation,
/// since there is no password-reset flow - losing this secret means losing the vault.
/// </summary>
public class UnlockForm : Form
{
    private readonly TextBox _secretBox = new() { PasswordChar = '●' };
    private readonly TextBox _confirmBox = new() { PasswordChar = '●' };
    private readonly Label _errorLabel = new() { ForeColor = Color.Firebrick, AutoSize = true };
    private readonly Button _okButton = new() { Text = "OK", AutoSize = true };

    public string Secret => _secretBox.Text;
    public UnlockMode Mode { get; }

    public UnlockForm(UnlockMode mode)
    {
        Mode = mode;

        Text = mode switch
        {
            UnlockMode.CreateNew => "Create Your Master Secret",
            UnlockMode.RestoreFromBackup => "Restore Vault from Google Drive",
            _ => "Unlock Personal Vault"
        };
        Width = 420;
        Height = mode == UnlockMode.CreateNew ? 350 : 220;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;

        var prompt = new Label
        {
            AutoSize = false,
            Width = 380,
            Height = mode == UnlockMode.CreateNew ? 110 : 50,
            Location = new Point(15, 12),
            Text = mode switch
            {
                UnlockMode.CreateNew =>
                    "Choose a master secret. It encrypts everything in this vault and is never " +
                    "stored anywhere. If you lose it, your data cannot be recovered.\n\n" +
                    "Requirements: " + PasswordPolicy.RequirementsText + ".",
                UnlockMode.RestoreFromBackup =>
                    "Enter the master secret this backup was created with (the same one you used " +
                    "on the original PC).",
                _ => "Enter your master secret to unlock the vault."
            }
        };
        Controls.Add(prompt);

        int y = prompt.Bottom + 10;

        Controls.Add(new Label { Text = "Secret:", AutoSize = true, Location = new Point(15, y + 4) });
        _secretBox.Location = new Point(120, y);
        _secretBox.Width = 260;
        Controls.Add(_secretBox);
        y += 32;

        if (mode == UnlockMode.CreateNew)
        {
            Controls.Add(new Label { Text = "Confirm:", AutoSize = true, Location = new Point(15, y + 4) });
            _confirmBox.Location = new Point(120, y);
            _confirmBox.Width = 260;
            Controls.Add(_confirmBox);
            y += 32;
        }

        _errorLabel.Location = new Point(15, y + 4);
        _errorLabel.MaximumSize = new Size(380, 0);
        Controls.Add(_errorLabel);
        y += 40;

        _okButton.Location = new Point(305, y);
        _okButton.Click += OkButton_Click;
        Controls.Add(_okButton);

        AcceptButton = _okButton;
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        _errorLabel.Text = "";

        if (Mode == UnlockMode.CreateNew)
        {
            if (!PasswordPolicy.IsValid(_secretBox.Text))
            {
                _errorLabel.Text = "Secret must have " + PasswordPolicy.RequirementsText + ".";
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(_secretBox.Text) || _secretBox.Text.Length < 8)
        {
            _errorLabel.Text = "Secret must be at least 8 characters.";
            return;
        }

        if (Mode == UnlockMode.CreateNew && _secretBox.Text != _confirmBox.Text)
        {
            _errorLabel.Text = "Secrets do not match.";
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
