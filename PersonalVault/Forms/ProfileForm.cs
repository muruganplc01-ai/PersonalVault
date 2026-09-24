using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// Edits the vault owner's display name and picture (VaultData.Profile), plus two
/// per-machine AppSettings fields (default browser, GitHub username for Share Account
/// links) that live alongside the profile in this same window for convenience even
/// though they aren't part of VaultProfile itself. Name/picture are mutated directly on
/// the passed-in VaultProfile when the user clicks Save - same "mutate in place, caller
/// decides whether to persist" pattern as AccountEditForm; the AppSettings fields come
/// back out via SelectedBrowserPath/GitHubUsername for the caller to persist instead,
/// since this form has no direct access to AppSettings. "Change Master Password..." is
/// a shortcut to the same ChangeSecretForm flow as the tray menu's "Change Master
/// Secret..." - see the changeMasterSecret constructor parameter. "Change Data
/// Folder..." works the same way for Utils/DataFolderMover.MoveTo, letting the whole
/// %-app-folder%\PersonalVault\ (or wherever it's already been moved to) be relocated
/// without leaving this window.
///
/// The picture never leaves this app except inside the encrypted vault file itself -
/// there is deliberately no separate upload of it anywhere.
/// </summary>
public class ProfileForm : Form
{
    private const int PictureSize = 128;

    private readonly VaultProfile _profile;

    // null = picture unchanged from what was passed in; empty array = explicitly removed.
    private byte[]? _pendingPictureBytes;

    private readonly PictureBox _pictureBox = new()
    {
        Width = PictureSize,
        Height = PictureSize,
        Location = new Point(20, 20),
        SizeMode = PictureBoxSizeMode.Zoom,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly TextBox _nameBox = new() { Location = new Point(20, 182), Width = 340 };
    private readonly TextBox _browserPathBox = new() { Location = new Point(20, 268), Width = 250, ReadOnly = true };
    private readonly TextBox _gitHubUsernameBox = new() { Location = new Point(20, 350), Width = 250 };
    private readonly TextBox _dataFolderBox = new() { Location = new Point(20, 656), Width = 250, ReadOnly = true };

    /// <summary>
    /// The chosen default-browser .exe path, or null to mean "use Windows' normal
    /// default browser" - set once the user clicks Save. AppSettings.DefaultBrowserPath
    /// isn't touched directly by this form; the caller (MainForm/TrayApplicationContext)
    /// reads this back out and persists it, same "mutate/return, caller decides whether
    /// to persist" pattern as everything else here.
    /// </summary>
    public string? SelectedBrowserPath { get; private set; }

    /// <summary>Same pattern as SelectedBrowserPath, for AppSettings.GitHubUsername.</summary>
    public string? GitHubUsername { get; private set; }

    private readonly Func<Task>? _changeMasterSecret;
    private readonly Action<string>? _changeDataFolder;
    private readonly Action? _openMfaSetup;
    private readonly Label _mfaStatusLabel = new() { AutoSize = true, Location = new Point(20, 596), ForeColor = Color.DimGray };

    /// <summary>
    /// changeMasterSecret is TrayApplicationContext.ChangeSecret, passed in so the
    /// button below can trigger the existing verify-old-secret / re-encrypt-the-vault
    /// flow (ChangeSecretForm) without this form needing to know anything about secrets
    /// or encryption itself - null (its default) hides the button entirely, for any
    /// future caller that doesn't have that capability wired up. currentDataFolder/
    /// changeDataFolder work the same way for Utils/DataFolderMover.MoveTo, and
    /// openMfaSetup for TrayApplicationContext.OpenMfaSetup (MfaSetupForm).
    /// </summary>
    public ProfileForm(
        VaultProfile profile,
        string? currentDefaultBrowserPath = null,
        string? currentGitHubUsername = null,
        Func<Task>? changeMasterSecret = null,
        string? currentDataFolder = null,
        Action<string>? changeDataFolder = null,
        Action? openMfaSetup = null)
    {
        _profile = profile;
        _changeMasterSecret = changeMasterSecret;
        _changeDataFolder = changeDataFolder;
        _openMfaSetup = openMfaSetup;

        Text = "Your Profile";
        Width = 400;
        Height = 880;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        Controls.Add(_pictureBox);

        var chooseBtn = new Button { Text = "Choose Picture...", AutoSize = true, Location = new Point(170, 20) };
        var removeBtn = new Button { Text = "Remove Picture", AutoSize = true, Location = new Point(170, 55) };
        chooseBtn.Click += ChooseBtn_Click;
        removeBtn.Click += (_, _) =>
        {
            _pendingPictureBytes = Array.Empty<byte>();
            _pictureBox.Image?.Dispose();
            _pictureBox.Image = null;
        };
        Controls.Add(chooseBtn);
        Controls.Add(removeBtn);

        Controls.Add(new Label { Text = "Name:", AutoSize = true, Location = new Point(20, 162) });
        Controls.Add(_nameBox);

        Controls.Add(new Label { Text = "Default browser:", AutoSize = true, Location = new Point(20, 220) });
        Controls.Add(new Label
        {
            Text = "Used when opening a website link from Personal Vault. Leave blank to use Windows' normal default browser.",
            AutoSize = false,
            Location = new Point(20, 240),
            Width = 350,
            Height = 30,
            ForeColor = Color.DimGray
        });

        var browseBrowserBtn = new Button { Text = "Browse...", AutoSize = true, Location = new Point(280, 267) };
        var useSystemDefaultBtn = new Button { Text = "Use System Default", AutoSize = true, Location = new Point(20, 296) };
        browseBrowserBtn.Click += BrowseBrowserBtn_Click;
        useSystemDefaultBtn.Click += (_, _) => _browserPathBox.Text = string.Empty;
        Controls.Add(_browserPathBox);
        Controls.Add(browseBrowserBtn);
        Controls.Add(useSystemDefaultBtn);

        Controls.Add(new Label { Text = "GitHub username (for Share links):", AutoSize = true, Location = new Point(20, 332) });
        Controls.Add(_gitHubUsernameBox);
        Controls.Add(new Label
        {
            Text = "Used to build \"Share Account\" links: https://<username>.github.io/PersonalVault/share/. " +
                   "Leave blank to disable the Share... button. See README for one-time GitHub Pages setup.",
            AutoSize = false,
            Location = new Point(20, 378),
            Width = 350,
            Height = 58,
            ForeColor = Color.DimGray
        });

        var masterPasswordLabel = new Label { Text = "Master password:", AutoSize = true, Location = new Point(20, 448), Visible = _changeMasterSecret != null };
        var changeSecretButton = new Button { Text = "Change Master Password...", AutoSize = true, Location = new Point(20, 468), Visible = _changeMasterSecret != null };
        changeSecretButton.Click += async (_, _) => { if (_changeMasterSecret != null) await _changeMasterSecret(); };
        var changeSecretNote = new Label
        {
            Text = "Re-encrypts your entire vault under a new secret. You'll be asked for the " +
                   "current one first.",
            AutoSize = false,
            Location = new Point(20, 498),
            Width = 350,
            Height = 32,
            ForeColor = Color.DimGray,
            Visible = _changeMasterSecret != null
        };
        Controls.Add(masterPasswordLabel);
        Controls.Add(changeSecretButton);
        Controls.Add(changeSecretNote);

        Controls.Add(new Label { Text = "Two-factor authentication:", AutoSize = true, Location = new Point(20, 546) });
        var mfaButton = new Button { Text = "Two-Factor Authentication...", AutoSize = true, Location = new Point(20, 566), Visible = _openMfaSetup != null };
        mfaButton.Click += (_, _) => { _openMfaSetup?.Invoke(); UpdateMfaStatusLabel(); };
        Controls.Add(mfaButton);
        Controls.Add(_mfaStatusLabel);

        Controls.Add(new Label { Text = "Data folder:", AutoSize = true, Location = new Point(20, 636) });
        var openFolderBtn = new Button { Text = "Open Folder", AutoSize = true, Location = new Point(280, 655) };
        openFolderBtn.Click += (_, _) => OpenDataFolder();
        Controls.Add(_dataFolderBox);
        Controls.Add(openFolderBtn);

        var changeFolderBtn = new Button
        {
            Text = "Change Data Folder...",
            AutoSize = true,
            Location = new Point(20, 686),
            Visible = _changeDataFolder != null
        };
        changeFolderBtn.Click += (_, _) => ChangeDataFolder();
        Controls.Add(changeFolderBtn);
        Controls.Add(new Label
        {
            Text = "Copies everything (vault, settings, Google Drive sign-in) to the new folder. " +
                   "The current folder is left in place as a backup - nothing is deleted.",
            AutoSize = false,
            Location = new Point(20, 716),
            Width = 350,
            Height = 40,
            ForeColor = Color.DimGray
        });

        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, Location = new Point(230, 775) };
        var saveButton = new Button { Text = "Save", AutoSize = true, Location = new Point(315, 775) };
        saveButton.Click += SaveButton_Click;
        Controls.Add(cancelButton);
        Controls.Add(saveButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        _nameBox.Text = _profile.Name;
        _browserPathBox.Text = currentDefaultBrowserPath ?? string.Empty;
        _gitHubUsernameBox.Text = currentGitHubUsername ?? string.Empty;
        _dataFolderBox.Text = currentDataFolder ?? string.Empty;
        UpdateMfaStatusLabel();
        if (_profile.HasPicture)
        {
            try
            {
                var bytes = Convert.FromBase64String(_profile.PictureBase64);
                using var ms = new MemoryStream(bytes);
                _pictureBox.Image = Image.FromStream(ms);
            }
            catch
            {
                // Stored picture is somehow corrupt/unreadable - just fall back to the empty placeholder.
            }
        }
    }

    private void ChooseBtn_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            Title = "Choose a profile picture"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var pngBytes = ResizeToSquarePng(dialog.FileName, PictureSize);
            _pendingPictureBytes = pngBytes;

            using var ms = new MemoryStream(pngBytes);
            _pictureBox.Image?.Dispose();
            _pictureBox.Image = Image.FromStream(ms);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not load that image: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BrowseBrowserBtn_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Applications (*.exe)|*.exe|All files|*.*",
            Title = "Choose a browser"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _browserPathBox.Text = dialog.FileName;
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        _profile.Name = _nameBox.Text.Trim();

        if (_pendingPictureBytes != null)
        {
            _profile.PictureBase64 = _pendingPictureBytes.Length == 0
                ? string.Empty
                : Convert.ToBase64String(_pendingPictureBytes);
        }

        var browserPath = _browserPathBox.Text.Trim();
        SelectedBrowserPath = string.IsNullOrEmpty(browserPath) ? null : browserPath;

        var gitHubUsername = _gitHubUsernameBox.Text.Trim();
        GitHubUsername = string.IsNullOrEmpty(gitHubUsername) ? null : gitHubUsername;

        DialogResult = DialogResult.OK;
    }

    /// <summary>Reflects _profile.MfaMethod, which MfaSetupForm updates directly (same live-object pattern as everything else on _profile here) - called on load and again right after the setup dialog closes.</summary>
    private void UpdateMfaStatusLabel()
    {
        _mfaStatusLabel.Text = _profile.MfaMethod switch
        {
            MfaMethod.Totp => "Currently: Authenticator app enabled",
            MfaMethod.Email => $"Currently: Email code to {_profile.MfaEmailAddress}",
            _ => "Currently: not enabled"
        };
    }

    private void OpenDataFolder()
    {
        if (string.IsNullOrEmpty(_dataFolderBox.Text)) return;
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", _dataFolderBox.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open that folder: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Lets the user pick a new folder, confirms, then delegates the actual copy to
    /// _changeDataFolder (TrayApplicationContext.ChangeDataFolder -> DataFolderMover) -
    /// this form just drives the picker/confirmation UI and reflects the result back
    /// into _dataFolderBox, since AppPaths takes effect immediately with no restart.
    /// </summary>
    private void ChangeDataFolder()
    {
        if (_changeDataFolder == null) return;

        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder for Personal Vault's data (vault, settings, Drive sign-in).",
            UseDescriptionForTitle = true,
            SelectedPath = _dataFolderBox.Text
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var confirm = MessageBox.Show(this,
            $"Copy everything from:\n{_dataFolderBox.Text}\n\nto:\n{dialog.SelectedPath}\n\n" +
            "The current folder is left in place - nothing is deleted. Continue?",
            "Personal Vault", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        try
        {
            _changeDataFolder(dialog.SelectedPath);
            _dataFolderBox.Text = dialog.SelectedPath;
            MessageBox.Show(this, "Data folder changed. Personal Vault will use the new location from now on.",
                "Personal Vault", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not change the data folder: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Loads an image file and crops/scales it to fill a size x size square - a standard avatar treatment.</summary>
    private static byte[] ResizeToSquarePng(string path, int size)
    {
        using var original = Image.FromFile(path);
        using var square = new Bitmap(size, size);
        using (var g = Graphics.FromImage(square))
        {
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;

            float scale = Math.Max((float)size / original.Width, (float)size / original.Height);
            int drawWidth = (int)Math.Round(original.Width * scale);
            int drawHeight = (int)Math.Round(original.Height * scale);
            int offsetX = (size - drawWidth) / 2;
            int offsetY = (size - drawHeight) / 2;

            g.DrawImage(original, offsetX, offsetY, drawWidth, drawHeight);
        }

        using var ms = new MemoryStream();
        square.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
