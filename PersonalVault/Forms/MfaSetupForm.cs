using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using PersonalVault.Models;
using PersonalVault.Security;

namespace PersonalVault.Forms;

/// <summary>
/// Profile's "Two-Factor Authentication..." dialog. Operates directly on the passed-in
/// VaultProfile (same "mutate in place, caller decides whether to persist" pattern as
/// ProfileForm itself), except for the two side effects that don't belong on
/// VaultProfile: saving/deleting the local, DPAPI-protected TOTP secret
/// (Storage/MfaSecretStorage.cs, deliberately outside the vault - see its doc comment)
/// and sending a test email code (needs an authorized Drive/Gmail credential, owned by
/// TrayApplicationContext) - both come in as delegates, matching how ChangeDataFolder
/// is threaded into ProfileForm.
///
/// A newly generated TOTP secret or a newly entered email address must be confirmed
/// with a real code THIS session before Save will accept it - this is what stops
/// someone from enabling MFA with a typo'd email or a secret they never actually
/// finished adding to their authenticator app, which would otherwise lock them out
/// immediately on the next unlock.
/// </summary>
public class MfaSetupForm : Form
{
    private readonly VaultProfile _profile;
    private readonly string _profileName;
    private readonly Action<byte[]> _saveTotpSecretLocally;
    private readonly Action _deleteTotpSecretLocally;
    private readonly Func<string, Task<string>> _sendTestEmailCode;

    private readonly RadioButton _noneRadio = new() { Text = "None", AutoSize = true };
    private readonly RadioButton _totpRadio = new() { Text = "Authenticator app (TOTP)", AutoSize = true };
    private readonly RadioButton _emailRadio = new() { Text = "Email code", AutoSize = true };

    // --- TOTP panel ---
    private readonly Panel _totpPanel = new() { Location = new Point(40, 110), Width = 420, Height = 300, Visible = false };
    private readonly Button _generateSecretButton = new() { Text = "Generate New Secret", AutoSize = true, Location = new Point(0, 0) };
    private readonly TextBox _secretBox = new() { Location = new Point(0, 35), Width = 300, ReadOnly = true, Visible = false };
    private readonly Button _copySecretButton = new() { Text = "Copy", AutoSize = true, Location = new Point(305, 34), Visible = false };
    private readonly Label _totpInstructions = new()
    {
        Text = "Add this secret to your authenticator app (Google Authenticator, Authy, etc. all accept typing a secret in manually), or paste the URI below if your app supports that instead.",
        AutoSize = false, Width = 420, Height = 40, Location = new Point(0, 65), Visible = false, ForeColor = Color.DimGray
    };
    private readonly TextBox _otpauthUriBox = new() { Location = new Point(0, 108), Width = 300, ReadOnly = true, Visible = false };
    private readonly Button _copyUriButton = new() { Text = "Copy", AutoSize = true, Location = new Point(305, 107), Visible = false };
    private readonly Label _confirmLabel = new() { Text = "Enter the current code to confirm:", AutoSize = true, Location = new Point(0, 140), Visible = false, ForeColor = Color.DimGray };
    private readonly TextBox _totpConfirmBox = new() { Location = new Point(0, 160), Width = 120, Font = new Font("Consolas", 12), Visible = false };
    private readonly Button _totpConfirmButton = new() { Text = "Confirm", AutoSize = true, Location = new Point(130, 158), Visible = false };
    private readonly TextBox _backupCodesBox = new()
    {
        Location = new Point(0, 195), Width = 420, Height = 90, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), Visible = false
    };

    // --- Email panel ---
    private readonly Panel _emailPanel = new() { Location = new Point(40, 110), Width = 420, Height = 150, Visible = false };
    private readonly Label _emailAddressLabel = new() { Text = "Email address:", AutoSize = true, Location = new Point(0, 4) };
    private readonly TextBox _emailBox = new() { Location = new Point(0, 22), Width = 300 };
    private readonly Button _sendTestButton = new() { Text = "Send Test Code", AutoSize = true, Location = new Point(0, 52) };
    private readonly Label _emailConfirmLabel = new() { Text = "Enter the emailed code to confirm:", AutoSize = true, Location = new Point(0, 88), ForeColor = Color.DimGray };
    private readonly TextBox _emailConfirmBox = new() { Location = new Point(0, 108), Width = 120, Font = new Font("Consolas", 12) };
    private readonly Button _emailConfirmButton = new() { Text = "Confirm", AutoSize = true, Location = new Point(130, 106) };
    private readonly Label _emailStatusLabel = new() { AutoSize = true, Location = new Point(240, 111) };

    private readonly Label _errorLabel = new() { ForeColor = Color.Firebrick, AutoSize = false, Width = 420, Height = 34, Location = new Point(40, 420) };

    private byte[]? _pendingTotpSecret;
    private List<string>? _pendingBackupCodes;
    private bool _totpConfirmedThisSession;
    private bool _emailConfirmedThisSession;
    private string? _emailConfirmedAddress;
    private readonly bool _missingLocalTotpSecret;

    public MfaSetupForm(
        VaultProfile profile,
        string profileName,
        bool totpSecretExistsLocally,
        Action<byte[]> saveTotpSecretLocally,
        Action deleteTotpSecretLocally,
        Func<string, Task<string>> sendTestEmailCode)
    {
        _profile = profile;
        _profileName = string.IsNullOrWhiteSpace(profileName) ? "Personal Vault" : profileName;
        _saveTotpSecretLocally = saveTotpSecretLocally;
        _deleteTotpSecretLocally = deleteTotpSecretLocally;
        _sendTestEmailCode = sendTestEmailCode;
        _missingLocalTotpSecret = profile.MfaMethod == MfaMethod.Totp && !totpSecretExistsLocally;

        Text = "Two-Factor Authentication";
        Width = 500;
        Height = 560;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        Controls.Add(new Label
        {
            Text = "Require a second step, in addition to your master password, to unlock the vault.",
            AutoSize = false, Width = 440, Height = 30, Location = new Point(20, 12), ForeColor = Color.DimGray
        });

        _noneRadio.Location = new Point(40, 50);
        _totpRadio.Location = new Point(40, 72);
        _emailRadio.Location = new Point(40, 94);
        Controls.Add(_noneRadio);
        Controls.Add(_totpRadio);
        Controls.Add(_emailRadio);

        BuildTotpPanel();
        BuildEmailPanel();
        Controls.Add(_totpPanel);
        Controls.Add(_emailPanel);
        Controls.Add(_errorLabel);

        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, Location = new Point(310, 460) };
        var saveButton = new Button { Text = "Save", AutoSize = true, Location = new Point(390, 460) };
        saveButton.Click += SaveButton_Click;
        Controls.Add(cancelButton);
        Controls.Add(saveButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        _noneRadio.CheckedChanged += (_, _) => UpdatePanelVisibility();
        _totpRadio.CheckedChanged += (_, _) => UpdatePanelVisibility();
        _emailRadio.CheckedChanged += (_, _) => UpdatePanelVisibility();

        LoadFromProfile();

        if (_missingLocalTotpSecret)
        {
            MessageBox.Show(this,
                "No authenticator secret was found on this PC - this is normal after restoring the " +
                "vault on a new machine, since it's deliberately never synced (see the Security Deep " +
                "Dive doc for why). Click \"Generate New Secret\" below to set one up here, or unlock " +
                "with a backup code in the meantime.",
                "Personal Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void BuildTotpPanel()
    {
        _generateSecretButton.Click += (_, _) => GenerateNewSecret();
        _copySecretButton.Click += (_, _) => { try { Clipboard.SetText(_secretBox.Text); } catch { } };
        _copyUriButton.Click += (_, _) => { try { Clipboard.SetText(_otpauthUriBox.Text); } catch { } };
        _totpConfirmButton.Click += (_, _) => ConfirmTotp();

        _totpPanel.Controls.Add(_generateSecretButton);
        _totpPanel.Controls.Add(_secretBox);
        _totpPanel.Controls.Add(_copySecretButton);
        _totpPanel.Controls.Add(_totpInstructions);
        _totpPanel.Controls.Add(_otpauthUriBox);
        _totpPanel.Controls.Add(_copyUriButton);
        _totpPanel.Controls.Add(_confirmLabel);
        _totpPanel.Controls.Add(_totpConfirmBox);
        _totpPanel.Controls.Add(_totpConfirmButton);
        _totpPanel.Controls.Add(_backupCodesBox);
    }

    private void BuildEmailPanel()
    {
        _emailPanel.Controls.Add(_emailAddressLabel);
        _emailPanel.Controls.Add(_emailBox);
        _emailBox.TextChanged += (_, _) =>
        {
            _emailConfirmedThisSession = _emailConfirmedThisSession && _emailBox.Text.Trim() == _emailConfirmedAddress;
            _emailStatusLabel.Text = _emailConfirmedThisSession ? "Verified" : "";
        };
        _sendTestButton.Click += async (_, _) => await SendTestEmailAsync();
        _emailPanel.Controls.Add(_sendTestButton);
        _emailPanel.Controls.Add(_emailConfirmLabel);
        _emailPanel.Controls.Add(_emailConfirmBox);
        _emailConfirmButton.Click += (_, _) => ConfirmEmail();
        _emailPanel.Controls.Add(_emailConfirmButton);
        _emailPanel.Controls.Add(_emailStatusLabel);
    }

    private void LoadFromProfile()
    {
        switch (_profile.MfaMethod)
        {
            case MfaMethod.Totp: _totpRadio.Checked = true; break;
            case MfaMethod.Email:
                _emailRadio.Checked = true;
                _emailBox.Text = _profile.MfaEmailAddress;
                break;
            default: _noneRadio.Checked = true; break;
        }
        UpdatePanelVisibility();
    }

    private void UpdatePanelVisibility()
    {
        _totpPanel.Visible = _totpRadio.Checked;
        _emailPanel.Visible = _emailRadio.Checked;
        _errorLabel.Text = string.Empty;
    }

    private void GenerateNewSecret()
    {
        _pendingTotpSecret = RandomNumberGenerator.GetBytes(20);
        _totpConfirmedThisSession = false;
        _pendingBackupCodes = null;

        string base32 = Base32.Encode(_pendingTotpSecret);
        string otpauthUri = "otpauth://totp/PersonalVault:" + Uri.EscapeDataString(_profileName)
            + "?secret=" + base32 + "&issuer=PersonalVault&digits=6&period=30";

        _secretBox.Text = base32;
        _secretBox.Visible = true;
        _copySecretButton.Visible = true;
        _totpInstructions.Visible = true;
        _otpauthUriBox.Text = otpauthUri;
        _otpauthUriBox.Visible = true;
        _copyUriButton.Visible = true;
        _confirmLabel.Visible = true;
        _totpConfirmBox.Visible = true;
        _totpConfirmBox.Text = string.Empty;
        _totpConfirmButton.Visible = true;
        _backupCodesBox.Visible = false;
        _errorLabel.Text = string.Empty;
    }

    private void ConfirmTotp()
    {
        if (_pendingTotpSecret == null) return;

        if (!TotpGenerator.Validate(_pendingTotpSecret, _totpConfirmBox.Text))
        {
            _errorLabel.Text = "That code doesn't match. Check your authenticator app and try again.";
            return;
        }

        _totpConfirmedThisSession = true;
        _pendingBackupCodes = GenerateBackupCodes();
        _backupCodesBox.Text = string.Join(Environment.NewLine, _pendingBackupCodes);
        _backupCodesBox.Visible = true;
        _errorLabel.Text = string.Empty;
        MessageBox.Show(this,
            "Save these backup codes somewhere safe - each works once, and they won't be shown again after you click Save. " +
            "They're your only way back in if you lose access to your authenticator app.",
            "Personal Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private async Task SendTestEmailAsync()
    {
        var address = _emailBox.Text.Trim();
        if (string.IsNullOrEmpty(address))
        {
            _errorLabel.Text = "Enter an email address first.";
            return;
        }

        _sendTestButton.Enabled = false;
        _errorLabel.Text = string.Empty;
        try
        {
            _expectedEmailCode = await _sendTestEmailCode(address);
            _emailStatusLabel.Text = "Code sent - check your inbox.";
        }
        catch (Exception ex)
        {
            _errorLabel.Text = "Could not send: " + ex.Message;
        }
        finally
        {
            _sendTestButton.Enabled = true;
        }
    }

    private string? _expectedEmailCode;

    private void ConfirmEmail()
    {
        if (_expectedEmailCode == null)
        {
            _errorLabel.Text = "Send a test code first.";
            return;
        }

        if (_emailConfirmBox.Text.Trim() != _expectedEmailCode)
        {
            _errorLabel.Text = "That code doesn't match.";
            return;
        }

        _emailConfirmedThisSession = true;
        _emailConfirmedAddress = _emailBox.Text.Trim();
        _emailStatusLabel.Text = "Verified";
        _errorLabel.Text = string.Empty;
    }

    private static List<string> GenerateBackupCodes()
    {
        const string chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no 0/O/1/I/L - avoids ambiguous characters when handwritten
        var codes = new List<string>(10);
        for (int i = 0; i < 10; i++)
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(10);
            var code = new char[10];
            for (int j = 0; j < 10; j++) code[j] = chars[bytes[j] % chars.Length];
            codes.Add(new string(code, 0, 5) + "-" + new string(code, 5, 5));
        }
        return codes;
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (_noneRadio.Checked)
        {
            if (_profile.MfaMethod == MfaMethod.Totp) _deleteTotpSecretLocally();
            _profile.MfaMethod = MfaMethod.None;
            _profile.MfaEmailAddress = string.Empty;
            _profile.MfaBackupCodeHashes.Clear();
            DialogResult = DialogResult.OK;
            return;
        }

        if (_totpRadio.Checked)
        {
            bool unchanged = _profile.MfaMethod == MfaMethod.Totp && _pendingTotpSecret == null;
            if (!unchanged)
            {
                if (_pendingTotpSecret == null || !_totpConfirmedThisSession)
                {
                    _errorLabel.Text = "Generate a secret and confirm a code from your authenticator app first.";
                    return;
                }
                _saveTotpSecretLocally(_pendingTotpSecret);
                _profile.MfaBackupCodeHashes = _pendingBackupCodes!.Select(HashBackupCode).ToList();
            }
            _profile.MfaMethod = MfaMethod.Totp;
            _profile.MfaEmailAddress = string.Empty;
            DialogResult = DialogResult.OK;
            return;
        }

        if (_emailRadio.Checked)
        {
            var address = _emailBox.Text.Trim();
            if (string.IsNullOrEmpty(address))
            {
                _errorLabel.Text = "Enter an email address.";
                return;
            }

            bool unchanged = _profile.MfaMethod == MfaMethod.Email && address == _profile.MfaEmailAddress;
            if (!unchanged)
            {
                if (!_emailConfirmedThisSession || _emailConfirmedAddress != address)
                {
                    _errorLabel.Text = "Send a test code to this address and confirm it first.";
                    return;
                }
                if (_profile.MfaMethod == MfaMethod.Totp) _deleteTotpSecretLocally();
                _profile.MfaBackupCodeHashes.Clear();
            }
            _profile.MfaMethod = MfaMethod.Email;
            _profile.MfaEmailAddress = address;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>Same hashing PersonalVault.Forms.MfaVerifyForm's caller (TrayApplicationContext.VerifyMfaAsync) uses to check a submitted backup code - keep the two in sync.</summary>
    public static string HashBackupCode(string code)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant()));
        return Convert.ToHexString(hash);
    }
}
