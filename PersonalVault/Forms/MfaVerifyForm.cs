using System.Drawing;
using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// The second-factor prompt shown at unlock time when Profile has Two-Factor
/// Authentication enabled - after the master secret has already decrypted the vault
/// successfully (see TrayApplicationContext.VerifyMfaAsync, which owns the actual
/// TOTP/backup-code/email-code checking; this form only collects what the user typed,
/// same "form collects input, caller verifies" split as UnlockForm/ChangeSecretForm).
/// </summary>
public class MfaVerifyForm : Form
{
    private readonly TextBox _codeBox = new() { Location = new Point(20, 90), Width = 200, Font = new Font("Consolas", 14) };
    private readonly LinkLabel _backupCodeLink = new() { Text = "Use a backup code instead", AutoSize = true, Location = new Point(20, 130) };
    private readonly Label _errorLabel = new() { ForeColor = Color.Firebrick, AutoSize = true, Location = new Point(20, 160), MaximumSize = new Size(340, 0) };
    private readonly Button _okButton = new() { Text = "Verify", AutoSize = true };
    private readonly Button _resendButton = new() { Text = "Resend Code", AutoSize = true, Visible = false };

    public string EnteredCode => _codeBox.Text.Trim();
    public bool UsingBackupCode { get; private set; }

    /// <summary>Set by the caller if a resend link should be offered (Email method only) - clicking it just closes this dialog with a special result the caller checks for.</summary>
    public bool ResendRequested { get; private set; }

    public MfaVerifyForm(MfaMethod method)
    {
        Text = "Verification Required";
        Width = 400;
        Height = method == MfaMethod.Totp ? 260 : 230;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;

        var prompt = new Label
        {
            AutoSize = false,
            Width = 340,
            Height = 50,
            Location = new Point(20, 12),
            Text = method == MfaMethod.Totp
                ? "Enter the 6-digit code from your authenticator app."
                : "Enter the 6-digit code just emailed to you. It expires in 10 minutes."
        };
        Controls.Add(prompt);
        Controls.Add(_codeBox);

        if (method == MfaMethod.Totp)
        {
            _backupCodeLink.LinkClicked += (_, _) =>
            {
                UsingBackupCode = !UsingBackupCode;
                _backupCodeLink.Text = UsingBackupCode ? "Use an authenticator code instead" : "Use a backup code instead";
                _codeBox.Font = UsingBackupCode ? new Font("Consolas", 11) : new Font("Consolas", 14);
                _codeBox.Text = string.Empty;
            };
            Controls.Add(_backupCodeLink);
        }
        else
        {
            _resendButton.Visible = true;
            _resendButton.Location = new Point(20, 130);
            _resendButton.Click += (_, _) =>
            {
                ResendRequested = true;
                DialogResult = DialogResult.Retry;
            };
            Controls.Add(_resendButton);
        }

        Controls.Add(_errorLabel);

        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, Location = new Point(200, 12) };
        _okButton.Location = new Point(285, 12);
        _okButton.Click += OkButton_Click;
        Controls.Add(cancelButton);
        Controls.Add(_okButton);

        AcceptButton = _okButton;
        CancelButton = cancelButton;

        _codeBox.Focus();
    }

    /// <summary>Called by the caller after a wrong code, to show an error and let the user try again without closing the dialog.</summary>
    public void ShowError(string message)
    {
        _errorLabel.Text = message;
        _codeBox.Text = string.Empty;
        _codeBox.Focus();
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_codeBox.Text))
        {
            _errorLabel.Text = "Enter a code.";
            return;
        }
        DialogResult = DialogResult.OK;
    }
}
