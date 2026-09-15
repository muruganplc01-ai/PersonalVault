using System.Drawing;
using System.Security.Cryptography;
using System.Windows.Forms;
using PersonalVault.Models;
using PersonalVault.Services;
using PersonalVault.Storage;
using PersonalVault.Utils;

namespace PersonalVault.Forms;

/// <summary>
/// The app's real "root". There is no main window that, if closed, ends the app -
/// instead this ApplicationContext owns the tray icon, the in-memory vault + secret,
/// the Google Drive connection, and the due-date notifier, and lives for the whole
/// process lifetime. MainForm is just a view over the data this class owns.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly GoogleDriveSync _drive = new();
    private readonly AppSettings _settings = AppSettings.Load();

    private MainForm? _mainForm;
    private VaultData? _vault;
    private string? _secret;
    private DueDateNotifier? _notifier;
    private string? _driveFileId;

    public TrayApplicationContext()
    {
        AppPaths.EnsureFoldersExist();

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield, // Placeholder - swap for a custom .ico under Resources\ later.
            Text = "Personal Vault",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainForm();
        _trayIcon.ContextMenuStrip = BuildTrayMenu();

        _ = InitializeAsync();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Open Vault", null, (_, _) => ShowMainForm());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sync Now", null, async (_, _) => await SyncNowAsync());
        menu.Items.Add("Sign in to Google Drive", null, async (_, _) => await SignInToDriveAsync());
        menu.Items.Add("Change Master Secret...", null, (_, _) => ChangeSecret());

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled()
        };
        startupItem.Click += (_, _) =>
        {
            StartupManager.SetEnabled(startupItem.Checked);
            _settings.StartWithWindows = startupItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        return menu;
    }

    private async Task InitializeAsync()
    {
        // This runs "fire and forget" from the constructor (WinForms constructors can't
        // be async), so wrap the whole thing: an uncaught exception in here would
        // otherwise just vanish as an unobserved task exception instead of telling the
        // user anything went wrong.
        try
        {
            // First run: apply the default "start with Windows" preference once.
            if (!VaultStorage.LocalVaultExists())
                StartupManager.SetEnabled(_settings.StartWithWindows);

            // Try a silent sign-in using a cached token, if we've connected Drive before.
            // Never pops a browser window here - that only happens when the user explicitly
            // clicks "Sign in to Google Drive".
            try { await _drive.SignInAsync(allowInteractive: false); }
            catch { /* fine - user can sign in manually from the tray menu */ }

            if (!UnlockVault())
            {
                ExitApplication();
                return;
            }

            if (_drive.IsAuthenticated)
            {
                try { await SyncNowAsync(silent: true); }
                catch { /* keep working from the local copy; Sync Now can be retried later */ }
            }

            _notifier = new DueDateNotifier(
                () => _vault,
                (title, message) => _trayIcon.ShowBalloonTip(15000, title, message, ToolTipIcon.Info),
                SaveVault,
                _settings.ReminderDaysBefore);
            _notifier.Start();

            ShowMainForm();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Personal Vault failed to start: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitApplication();
        }
    }

    /// <summary>Prompts for the master secret (looping on wrong-secret errors) and loads/creates the vault.</summary>
    private bool UnlockVault()
    {
        bool creating = !VaultStorage.LocalVaultExists();
        using var form = new UnlockForm(creating ? UnlockMode.CreateNew : UnlockMode.UnlockExisting);

        while (true)
        {
            if (form.ShowDialog() != DialogResult.OK)
                return false;

            try
            {
                if (creating)
                {
                    _secret = form.Secret;
                    _vault = new VaultData();
                    SaveVault();
                }
                else
                {
                    _vault = VaultStorage.LoadLocal(form.Secret);
                    _secret = form.Secret;
                }
                return true;
            }
            catch (CryptographicException)
            {
                MessageBox.Show("Incorrect secret. Please try again.", "Personal Vault",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void ShowMainForm()
    {
        if (_vault == null || _secret == null) return;

        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(_vault, SaveVault);
            _mainForm.FormClosing += (_, e) =>
            {
                // Closing the window just hides it - the app keeps running in the tray
                // so due-date checks and Drive sync keep happening.
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    _mainForm.Hide();
                }
            };
        }

        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    private void SaveVault()
    {
        if (_vault == null || _secret == null) return;
        VaultStorage.SaveLocal(_vault, _secret);
        _ = UploadToDriveAsync();
    }

    private async Task UploadToDriveAsync()
    {
        if (!_drive.IsAuthenticated) return;

        try
        {
            _driveFileId ??= await _drive.FindVaultFileIdAsync();
            _driveFileId = await _drive.UploadOrUpdateAsync(AppPaths.VaultLocalPath, _driveFileId);
            _settings.DriveFileId = _driveFileId;
            _settings.Save();
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(10000, "Personal Vault",
                "Could not sync to Google Drive: " + ex.Message, ToolTipIcon.Warning);
        }
    }

    /// <summary>
    /// Pulls the newer copy (local vs. Drive, by modified time) and pushes ours up if
    /// ours is newer or nothing exists on Drive yet. This is a simple last-write-wins
    /// strategy - if you edit the vault on two machines between syncs, the later save wins
    /// and the earlier one's changes are lost, so sync often if you use more than one PC.
    /// </summary>
    private async Task SyncNowAsync(bool silent = false)
    {
        if (!_drive.IsAuthenticated)
        {
            if (!silent) await SignInToDriveAsync();
            if (!_drive.IsAuthenticated) return;
        }

        _driveFileId ??= _settings.DriveFileId ?? await _drive.FindVaultFileIdAsync();

        if (_driveFileId == null)
        {
            await UploadToDriveAsync();
            return;
        }

        var remoteModified = await _drive.GetRemoteModifiedTimeAsync(_driveFileId);
        var localModified = File.Exists(AppPaths.VaultLocalPath)
            ? File.GetLastWriteTimeUtc(AppPaths.VaultLocalPath)
            : DateTime.MinValue;

        if (remoteModified.HasValue && remoteModified.Value > localModified)
        {
            var bytes = await _drive.DownloadAsync(_driveFileId);
            var data = VaultStorage.LoadFromBytes(bytes, _secret!);
            _vault = data;
            File.WriteAllBytes(AppPaths.VaultLocalPath, bytes);
            _mainForm?.RefreshData(_vault);
            if (!silent) MessageBox.Show("Pulled the newer copy from Google Drive.", "Personal Vault");
        }
        else
        {
            await UploadToDriveAsync();
            if (!silent) MessageBox.Show("Google Drive is up to date.", "Personal Vault");
        }
    }

    private async Task SignInToDriveAsync()
    {
        try
        {
            bool ok = await _drive.SignInAsync(allowInteractive: true);
            if (!ok)
            {
                MessageBox.Show(
                    "Google Drive credentials were not found.\n\nExpected file:\n" + AppPaths.CredentialsJsonPath +
                    "\n\nSee README.md for how to create one in Google Cloud Console.",
                    "Personal Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _trayIcon.ShowBalloonTip(8000, "Personal Vault", "Signed in to Google Drive.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Google sign-in failed: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ChangeSecret()
    {
        if (_vault == null || _secret == null) return;

        using var form = new ChangeSecretForm();
        if (form.ShowDialog() != DialogResult.OK) return;

        if (form.OldSecret != _secret)
        {
            MessageBox.Show("Current secret is incorrect.", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _secret = form.NewSecret;
        SaveVault(); // Re-encrypts the whole vault file under the new secret (new salt/nonce too) and re-uploads it.
        MessageBox.Show("Master secret updated. The vault file has been re-encrypted.", "Personal Vault");
    }

    private void ExitApplication()
    {
        _notifier?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }
}
