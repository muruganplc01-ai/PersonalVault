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
    private PaymentsData? _payments;
    private string? _paymentsDriveFileId;
    private System.Windows.Forms.Timer? _idleTimer;
    private bool _isLocked;
    private ToolStripMenuItem? _startupMenuItem;
    private ToolStripMenuItem? _driveMenuItem;
    private bool _warnedNotSignedInThisSession;

    public TrayApplicationContext()
    {
        AppPaths.EnsureFoldersExist();

        Icon trayIconImage = LoadTrayIcon();

        _trayIcon = new NotifyIcon
        {
            Icon = trayIconImage,
            Text = "Personal Vault",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainForm();
        _trayIcon.ContextMenuStrip = BuildTrayMenu();

        // Registers this app with Windows' toast system and wires up the "Open Vault"
        // button on any toast we raise. Wrapped internally in try/catch - if it fails,
        // ToastNotifier.TryShow just returns false forever after and every notification
        // site below falls back to the plain balloon tip.
        ToastNotifier.Initialize();
        ToastNotifier.OpenVaultRequested += () => ShowMainForm();

        _ = InitializeAsync();
    }

    /// <summary>
    /// Loads the app's real icon (Resources\AppIcon.ico, embedded into the .exe via the
    /// &lt;ApplicationIcon&gt; csproj setting) for the tray. Pulling it back out of the
    /// built .exe via ExtractAssociatedIcon - rather than expecting a loose .ico file
    /// next to the app at runtime - keeps this working the same way under `dotnet run`,
    /// a normal build, and the self-contained single-file publish used for family
    /// members, none of which are guaranteed to carry Resources\ along as a loose
    /// folder. Falls back to a loose file, then a built-in system icon, so a bad or
    /// missing icon never stops the app from starting.
    /// </summary>
    private static Icon LoadTrayIcon()
    {
        try
        {
            var extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (extracted != null) return extracted;
        }
        catch { /* fall through to the loose-file attempt below */ }

        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon.ico");
            if (File.Exists(icoPath)) return new Icon(icoPath);
        }
        catch { /* fall through to the system icon below */ }

        return SystemIcons.Shield;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Open Vault", null, (_, _) => ShowMainForm());
        menu.Items.Add("Profile...", null, (_, _) => OpenProfile());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sync Now", null, async (_, _) => await SyncNowAsync());

        _driveMenuItem = new ToolStripMenuItem("Sign in to Google Drive");
        _driveMenuItem.Click += async (_, _) => await SignInToDriveAsync();
        menu.Items.Add(_driveMenuItem);

        menu.Items.Add("Lock Now", null, (_, _) => Lock());
        menu.Items.Add("Change Master Secret...", null, (_, _) => ChangeSecret());
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());

        _startupMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled()
        };
        _startupMenuItem.Click += (_, _) =>
        {
            StartupManager.SetEnabled(_startupMenuItem.Checked);
            _settings.StartWithWindows = _startupMenuItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(_startupMenuItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        return menu;
    }

    /// <summary>Shows a rich toast if possible, otherwise the plain tray balloon - callers never need to know which happened.</summary>
    private void Notify(string title, string message, ToolTipIcon fallbackIcon = ToolTipIcon.Info)
    {
        DebugLog.Write($"Notify: '{title}' - '{message}'");
        bool toastShown = ToastNotifier.TryShow(title, message);
        if (!toastShown)
        {
            DebugLog.Write("Notify: falling back to _trayIcon.ShowBalloonTip. (Note: balloon tips can be silently suppressed by Windows Focus Assist / notification settings - the call happening here doesn't guarantee it was actually seen.)");
            _trayIcon.ShowBalloonTip(10000, title, message, fallbackIcon);
        }
    }

    /// <summary>Keeps the tray menu AND the main window honest about whether you're actually signed in right now - call this any time the answer might have changed.</summary>
    private void UpdateDriveMenuState()
    {
        if (_driveMenuItem != null)
        {
            _driveMenuItem.Text = _drive.IsAuthenticated
                ? "Google Drive: Connected ✓"
                : "Sign in to Google Drive...";
        }
        _mainForm?.UpdateDriveStatus(_drive.IsAuthenticated);
    }

    private void OpenProfile()
    {
        if (!EnsureUnlocked()) return;
        if (_vault == null) return;

        using var form = new ProfileForm(_vault.Profile);
        if (form.ShowDialog() == DialogResult.OK)
        {
            SaveVault();
            _mainForm?.RefreshData(_vault);
        }
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(
            _settings,
            AppPaths.VaultLocalPath,
            AppPaths.CredentialsJsonPath,
            _drive.IsAuthenticated);
        if (form.ShowDialog() != DialogResult.OK) return;

        _settings.Save();
        StartupManager.SetEnabled(_settings.StartWithWindows);
        if (_startupMenuItem != null) _startupMenuItem.Checked = _settings.StartWithWindows;

        // Auto-lock timeout may have just changed - restart the idle monitor so the new
        // value (including "0 = off") takes effect immediately instead of after a restart.
        _idleTimer?.Stop();
        _idleTimer?.Dispose();
        _idleTimer = null;
        StartIdleMonitor();

        // Reminder-day list may have changed too, but DueDateNotifier reads it once at
        // construction. Cheap enough to keep behaving correctly: just note it takes
        // effect after the next natural restart if changed. (Not restarting the notifier
        // here to avoid double-firing a reminder mid-cycle.)
    }

    private async Task InitializeAsync()
    {
        // This runs "fire and forget" from the constructor (WinForms constructors can't
        // be async), so wrap the whole thing: an uncaught exception in here would
        // otherwise just vanish as an unobserved task exception instead of telling the
        // user anything went wrong.
        try
        {
            DebugLog.Write($"InitializeAsync: starting. DebugLog.IsEnabled={DebugLog.IsEnabled} (you're reading this, so obviously true). VaultLocalPath='{AppPaths.VaultLocalPath}', CredentialsJsonPath='{AppPaths.CredentialsJsonPath}'.");

            bool firstRun = !VaultStorage.LocalVaultExists();
            DebugLog.Write($"InitializeAsync: firstRun (no local vault file yet) = {firstRun}.");

            // First run: apply the default "start with Windows" preference once.
            if (firstRun)
                StartupManager.SetEnabled(_settings.StartWithWindows);

            // If this is a portable copy handed to someone else (e.g. a family member)
            // with credentials.json shipped next to the .exe, pull it into %AppData% now -
            // removes the manual "go copy this file" step entirely for them.
            CredentialBootstrap.EnsureCredentialsCopiedFromAppFolder();
            DebugLog.Write($"InitializeAsync: after CredentialBootstrap, HasStoredCredentialsFile={_drive.HasStoredCredentialsFile}.");

            // Try a silent sign-in using a cached token, if we've connected Drive before.
            // Never pops a browser window here - that only happens when the user explicitly
            // clicks "Sign in to Google Drive" (or accepts one of the prompts below).
            try { await _drive.SignInAsync(allowInteractive: false); }
            catch (Exception ex) { DebugLog.WriteException("InitializeAsync silent SignInAsync (non-fatal - user can sign in manually)", ex); }
            DebugLog.Write($"InitializeAsync: after silent sign-in attempt, IsAuthenticated={_drive.IsAuthenticated}.");
            UpdateDriveMenuState();

            bool restored = false;

            if (firstRun)
            {
                // This is the actual disaster-recovery path: nothing local yet doesn't
                // necessarily mean "brand new user" - it just as easily means "reinstalled
                // after something happened to the old PC" or "setting up on a second PC".
                // Check Google Drive for an existing vault BEFORE ever offering to create an
                // empty one, so that case is never silently mishandled.
                if (!_drive.IsAuthenticated && _drive.HasStoredCredentialsFile)
                {
                    var checkDrive = MessageBox.Show(
                        "No vault found on this PC yet.\n\n" +
                        "Sign in to Google Drive now to check for an existing backup before " +
                        "creating a brand-new, empty vault?",
                        "Personal Vault", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (checkDrive == DialogResult.Yes)
                        await SignInToDriveAsync();
                }

                if (_drive.IsAuthenticated)
                {
                    try
                    {
                        var remoteFileId = await _drive.FindVaultFileIdAsync();
                        if (remoteFileId != null)
                        {
                            var remoteBytes = await _drive.DownloadAsync(remoteFileId);
                            var restore = MessageBox.Show(
                                "A Personal Vault backup was found on Google Drive.\n\n" +
                                "Restore it here? You'll need the master secret it was created " +
                                "with. Choose \"No\" to set this PC up with a brand-new, empty " +
                                "vault instead.",
                                "Personal Vault", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                            if (restore == DialogResult.Yes)
                            {
                                restored = RestoreVaultFromBytes(remoteBytes);
                                if (restored)
                                {
                                    _driveFileId = remoteFileId;
                                    _settings.DriveFileId = _driveFileId;
                                    _settings.Save();
                                }
                                else
                                {
                                    // User cancelled the restore prompt entirely - don't fall
                                    // through to silently creating an empty vault instead.
                                    ExitApplication();
                                    return;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Couldn't check/download from Drive (offline, transient error, etc.) -
                        // fall through to the normal create-new-vault flow below rather than
                        // blocking startup on it.
                    }
                }
            }

            if (!restored)
            {
                if (!UnlockVault(firstRun))
                {
                    ExitApplication();
                    return;
                }
            }

            StartIdleMonitor();
            await LoadOrRestorePaymentsAsync();

            if (firstRun && !restored && !_drive.IsAuthenticated && _drive.HasStoredCredentialsFile)
            {
                // credentials.json is already in place (bootstrapped above, or set up by
                // hand) but nobody's signed in yet - this is the one moment it's worth
                // interrupting a brand-new user to offer it, instead of leaving Drive sync
                // as a menu item they'd have to go discover themselves.
                var connect = MessageBox.Show(
                    "Vault created. Connect Google Drive now so this vault is backed up automatically?\n\n" +
                    "You can also do this anytime later from the tray menu.",
                    "Personal Vault", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (connect == DialogResult.Yes)
                    await SignInToDriveAsync();
            }

            if (_drive.IsAuthenticated)
            {
                try { await SyncNowAsync(silent: true); }
                catch { /* keep working from the local copy; Sync Now can be retried later */ }
            }

            _notifier = new DueDateNotifier(
                () => _vault,
                (title, message) => Notify(title, message),
                SaveVault,
                _settings.ReminderDaysBefore);
            _notifier.Start();

            DebugLog.Write("InitializeAsync: finished successfully.");
            ShowMainForm();
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("InitializeAsync (fatal - app is exiting)", ex);
            MessageBox.Show("Personal Vault failed to start: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitApplication();
        }
    }

    /// <summary>Prompts for the master secret (looping on wrong-secret errors) and loads/creates the vault.</summary>
    private bool UnlockVault(bool creating)
    {
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

    /// <summary>
    /// Disaster-recovery path: unlocks a vault just downloaded from Google Drive (rather
    /// than the local file) and, once unlocked, saves it locally so this PC has its own
    /// copy from now on. Loops on wrong-secret errors the same way UnlockVault does.
    /// Returns false only if the user cancels out of the dialog entirely.
    /// </summary>
    private bool RestoreVaultFromBytes(byte[] remoteBytes)
    {
        using var form = new UnlockForm(UnlockMode.RestoreFromBackup);

        while (true)
        {
            if (form.ShowDialog() != DialogResult.OK)
                return false;

            try
            {
                _vault = VaultStorage.LoadFromBytes(remoteBytes, form.Secret);
                _secret = form.Secret;
                VaultStorage.SaveLocal(_vault, _secret);
                return true;
            }
            catch (CryptographicException)
            {
                MessageBox.Show("Incorrect secret for this backup. Please try again.", "Personal Vault",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    /// <summary>
    /// Loads payment history for the now-unlocked vault: from the local payments file if
    /// one already exists on this PC, otherwise (best-effort, silent - no prompts) tries
    /// Google Drive in case this is a second PC or a reinstall, and only falls back to an
    /// empty history if neither has anything. Requires _secret to already be set, so this
    /// must run after the vault has been unlocked/created/restored. Genuinely async (not
    /// blocked on synchronously) because every await in this app resumes on the WinForms
    /// UI SynchronizationContext - calling .GetResult() on the UI thread here would
    /// deadlock against that same continuation.
    /// </summary>
    private async Task LoadOrRestorePaymentsAsync()
    {
        if (_secret == null) return;

        try
        {
            _payments = PaymentsStorage.LoadLocalOrEmpty(_secret);
            DebugLog.Write($"LoadOrRestorePaymentsAsync: loaded {_payments.Payments.Count} local payment record(s) from '{AppPaths.PaymentsLocalPath}'.");
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("LoadOrRestorePaymentsAsync (local) - starting with an empty payment history instead", ex);
            _payments = new PaymentsData();
        }

        if (PaymentsStorage.LocalPaymentsExist() || !_drive.IsAuthenticated)
            return;

        // No local payments file yet, but we're already signed in to Drive - this is the
        // same "don't silently start over on a second PC" reasoning as the vault's own
        // disaster-recovery check, just without an interactive prompt: payment history
        // isn't sensitive enough on its own to interrupt startup over, so this only ever
        // does something when Drive access is already available for free.
        try
        {
            var remotePaymentsId = await _drive.FindFileIdAsync(GoogleDriveSync.RemotePaymentsFileName);
            if (remotePaymentsId == null) return;

            var remoteBytes = await _drive.DownloadAsync(remotePaymentsId);
            _payments = PaymentsStorage.LoadFromBytes(remoteBytes, _secret);
            PaymentsStorage.SaveLocal(_payments, _secret);
            _paymentsDriveFileId = remotePaymentsId;
            _settings.PaymentsDriveFileId = _paymentsDriveFileId;
            _settings.Save();
            DebugLog.Write($"LoadOrRestorePaymentsAsync: restored {_payments.Payments.Count} payment record(s) from Google Drive.");
        }
        catch (Exception ex)
        {
            // Non-fatal either way: worst case, payment history starts empty here and a
            // fresh file is created next time something is marked paid.
            DebugLog.WriteException("LoadOrRestorePaymentsAsync (Drive restore, non-fatal)", ex);
        }
    }

    /// <summary>
    /// Polls system-wide idle time (keyboard/mouse anywhere, not just in this app) and
    /// locks the vault once it's been idle past the configured threshold. A 0 setting
    /// disables auto-lock entirely.
    /// </summary>
    private void StartIdleMonitor()
    {
        if (_settings.AutoLockMinutes <= 0) return;

        _idleTimer = new System.Windows.Forms.Timer { Interval = 15_000 }; // check every 15s
        _idleTimer.Tick += (_, _) =>
        {
            if (_isLocked || _secret == null) return;
            if (SystemIdleTime.GetIdleTime() >= TimeSpan.FromMinutes(_settings.AutoLockMinutes))
                Lock();
        };
        _idleTimer.Start();
    }

    /// <summary>
    /// Drops the vault from memory and requires the master secret again to resume.
    /// Safe to call anytime, including if nothing's unlocked yet or it's already locked.
    /// </summary>
    private void Lock()
    {
        if (_isLocked || _secret == null) return;

        _isLocked = true;
        _mainForm?.Hide();

        // Best-effort: drop the plaintext secret and every decrypted password from
        // memory. (.NET strings are immutable and the GC doesn't scrub freed memory, so
        // this isn't a hard guarantee against a memory-dump attack - but nothing here
        // stays reachable through normal use of the app once locked, and unlocking again
        // re-derives everything fresh from the encrypted file on disk.)
        _secret = null;
        _vault = null;
        _payments = null;

        _trayIcon.Text = "Personal Vault (Locked)";
        Notify("Personal Vault", "Locked due to inactivity.");
    }

    /// <summary>
    /// If locked, prompts for the secret and re-loads the vault before letting the
    /// caller proceed. Returns false (leaving the vault locked) if the user cancels.
    /// </summary>
    private bool EnsureUnlocked()
    {
        if (!_isLocked) return true;

        using var form = new UnlockForm(UnlockMode.UnlockExisting);
        while (true)
        {
            if (form.ShowDialog() != DialogResult.OK)
                return false;

            try
            {
                _vault = VaultStorage.LoadLocal(form.Secret);
                _secret = form.Secret;
                _payments = PaymentsStorage.LoadLocalOrEmpty(_secret);
                _isLocked = false;
                _trayIcon.Text = "Personal Vault";
                _notifier?.CheckNow(); // catch up on anything missed while locked
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
        if (!EnsureUnlocked()) return;
        if (_vault == null || _secret == null) return;

        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(
                _vault,
                SaveVault,
                _drive.IsAuthenticated,
                SignInToDriveAsync,
                AppPaths.VaultLocalPath,
                () => _payments?.Payments ?? Enumerable.Empty<PaymentRecord>(),
                MarkAccountPaid,
                BackfillPayments,
                () => _settings.DefaultBrowserPath,
                SetDefaultBrowserPath);
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
        DebugLog.Write($"SaveVault: called. _vault null? {_vault == null}. _secret null? {_secret == null}.");
        if (_vault == null || _secret == null) return;

        VaultStorage.SaveLocal(_vault, _secret);
        DebugLog.Write($"SaveVault: local file written to '{AppPaths.VaultLocalPath}'. Kicking off UploadToDriveAsync (fire-and-forget)...");
        _ = UploadToDriveAsync();
    }

    private async Task UploadToDriveAsync()
    {
        DebugLog.Write($"UploadToDriveAsync: called. _drive.IsAuthenticated={_drive.IsAuthenticated}, HasStoredCredentialsFile={_drive.HasStoredCredentialsFile}, cached _driveFileId={(_driveFileId == null ? "(null)" : "set")}.");

        if (!_drive.IsAuthenticated)
        {
            // Previously this just silently did nothing - which made "I saved something
            // but Drive stayed empty" indistinguishable from a real bug. credentials.json
            // being in place does NOT mean you're signed in - that only happens once the
            // interactive Google consent flow has actually been completed. Only speak up
            // when credentials.json exists (i.e. Drive sync was clearly intended) so
            // people who've deliberately chosen to stay fully offline aren't nagged.
            DebugLog.Write("UploadToDriveAsync: not authenticated - nothing will be uploaded. This is almost always because interactive sign-in was never completed (credentials.json existing is not enough by itself).");
            if (_drive.HasStoredCredentialsFile)
            {
                if (!_warnedNotSignedInThisSession)
                {
                    // First time in a session this happens: actually offer to fix it right
                    // here, rather than just warning and hoping you go find the menu item
                    // yourself. A plain balloon/toast is also too easy to miss entirely (can
                    // be suppressed by Focus Assist, or just disappears before you glance at
                    // the tray), so this uses a real modal - impossible to miss - and after
                    // this first prompt in the session falls back to the quieter
                    // notification so normal saves don't get interrupted every time.
                    _warnedNotSignedInThisSession = true;
                    DebugLog.Write("UploadToDriveAsync: showing first-of-session sign-in prompt.");
                    var signInNow = MessageBox.Show(
                        "This save is stored locally only - Personal Vault isn't signed in to Google " +
                        "Drive yet, so nothing has been backed up.\n\n" +
                        "Sign in now so this (and everything else) backs up automatically?",
                        "Personal Vault - Not Backed Up", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (signInNow == DialogResult.Yes)
                    {
                        DebugLog.Write("UploadToDriveAsync: user chose to sign in now - launching interactive sign-in, then retrying this upload.");
                        await SignInToDriveAsync();
                        if (_drive.IsAuthenticated)
                        {
                            await UploadToDriveAsync(); // single bounded retry now that we're actually signed in
                            return;
                        }
                        DebugLog.Write("UploadToDriveAsync: sign-in did not succeed - this save stays local only for now.");
                    }
                }
                else
                {
                    Notify("Personal Vault",
                        "Saved locally only - still not signed in to Google Drive. Use the tray menu's " +
                        "\"Sign in to Google Drive\" to turn on automatic backup.", ToolTipIcon.Warning);
                }
            }
            return;
        }

        // Visible "it's trying right now" signal, per your request - shows up on hover
        // over the tray icon for the duration of the upload, then reverts.
        string previousTrayText = _trayIcon.Text;
        _trayIcon.Text = "Personal Vault (Syncing to Drive...)";

        try
        {
            DebugLog.Write("UploadToDriveAsync: authenticated - starting upload sequence.");

            _driveFileId ??= await _drive.FindVaultFileIdAsync();
            DebugLog.Write($"UploadToDriveAsync: resolved _driveFileId={(_driveFileId == null ? "(null - will create new file)" : "set")}.");

            _driveFileId = await _drive.UploadOrUpdateAsync(AppPaths.VaultLocalPath, _driveFileId);
            _settings.DriveFileId = _driveFileId;
            _settings.Save();
            DebugLog.Write("UploadToDriveAsync: upload call returned without throwing - upload succeeded.");

            // Best-effort rolling backup: pins the revision we just uploaded and prunes
            // anything beyond the last few, so an accidental delete or bad sync has
            // something recent to recover from. Never throws (see GoogleDriveSync), so
            // this can't turn a successful upload into a reported sync failure.
            await _drive.PruneOldRevisionsAsync(_driveFileId);

            Notify("Personal Vault", "Saved to Google Drive.");
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("UploadToDriveAsync", ex);
            Notify("Personal Vault", "Could not sync to Google Drive: " + ex.Message, ToolTipIcon.Warning);
        }
        finally
        {
            _trayIcon.Text = previousTrayText;
        }
    }

    private void SavePayments()
    {
        DebugLog.Write($"SavePayments: called. _payments null? {_payments == null}. _secret null? {_secret == null}.");
        if (_payments == null || _secret == null) return;

        PaymentsStorage.SaveLocal(_payments, _secret);
        DebugLog.Write($"SavePayments: local file written to '{AppPaths.PaymentsLocalPath}'. Kicking off UploadPaymentsToDriveAsync (fire-and-forget)...");
        _ = UploadPaymentsToDriveAsync();
    }

    /// <summary>
    /// Same idea as UploadToDriveAsync but for the separate payments file. Deliberately
    /// stays quiet when not signed in to Drive rather than showing its own "not backed
    /// up" prompt - a Mark as Paid always calls SaveVault() too (the due date changed),
    /// and that call already surfaces the not-signed-in warning once per session, so a
    /// second prompt here for the same underlying cause would just be noise.
    /// </summary>
    private async Task UploadPaymentsToDriveAsync()
    {
        DebugLog.Write($"UploadPaymentsToDriveAsync: called. _drive.IsAuthenticated={_drive.IsAuthenticated}.");
        if (!_drive.IsAuthenticated) return;

        try
        {
            _paymentsDriveFileId ??= _settings.PaymentsDriveFileId
                ?? await _drive.FindFileIdAsync(GoogleDriveSync.RemotePaymentsFileName);
            DebugLog.Write($"UploadPaymentsToDriveAsync: resolved _paymentsDriveFileId={(_paymentsDriveFileId == null ? "(null - will create new file)" : "set")}.");

            _paymentsDriveFileId = await _drive.UploadOrUpdateAsync(
                AppPaths.PaymentsLocalPath, _paymentsDriveFileId, GoogleDriveSync.RemotePaymentsFileName);
            _settings.PaymentsDriveFileId = _paymentsDriveFileId;
            _settings.Save();
            DebugLog.Write("UploadPaymentsToDriveAsync: upload call returned without throwing - upload succeeded.");
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("UploadPaymentsToDriveAsync", ex);
            Notify("Personal Vault", "Could not sync payment history to Google Drive: " + ex.Message, ToolTipIcon.Warning);
        }
    }

    /// <summary>
    /// Called from MainForm's Dues tab ("Mark as Paid..."). Records the payment,
    /// advances a recurring bill's due date to its next cycle (or clears the due date
    /// entirely for a one-time bill that's now fully paid) using the same rule
    /// DueDateNotifier uses when an overdue reminder fires, and saves+uploads both the
    /// vault (the due date changed) and the payments file.
    /// </summary>
    private void MarkAccountPaid(AccountEntry account, decimal amountPaid, DateTime paidDate)
    {
        if (_vault == null || _payments == null) return;

        DebugLog.Write($"MarkAccountPaid: account='{account.Name}', amountPaid={amountPaid}, paidDate={paidDate:d}.");

        _payments.Payments.Add(new PaymentRecord
        {
            AccountId = account.Id,
            AccountName = account.Name,
            AmountPaid = amountPaid,
            PaidDate = paidDate,
            DueDateAtPayment = account.DueDate
        });

        if (account.DueDate != null)
        {
            account.DueDate = account.Recurrence != RecurrenceType.None
                ? DueDateNotifier.AdvanceDueDate(account.DueDate.Value, account.Recurrence)
                : null;
            account.LastNotifiedOn = null;
            account.ModifiedUtc = DateTime.UtcNow;
        }

        SaveVault();
        SavePayments();
        _mainForm?.RefreshData(_vault);
    }

    /// <summary>
    /// Called from MainForm's Dues tab ("Backfill Past Payment..."). Adds `count`
    /// PaymentRecords for `account`, spaced `spacing` apart and working backward from
    /// `mostRecentDate` (so the most recent one entered gets exactly that date, and each
    /// one before it is one cycle earlier). Unlike MarkAccountPaid, this never touches
    /// the account's DueDate/LastNotifiedOn or calls SaveVault() - it's purely filling in
    /// history, not affecting what's currently due, so only the payments file changes.
    /// </summary>
    private void BackfillPayments(AccountEntry account, decimal amountPaid, DateTime mostRecentDate, int count, RecurrenceType spacing)
    {
        if (_vault == null || _payments == null) return;

        DebugLog.Write($"BackfillPayments: account='{account.Name}', amountPaid={amountPaid}, mostRecentDate={mostRecentDate:d}, count={count}, spacing={spacing}.");

        var date = mostRecentDate;
        for (int i = 0; i < count; i++)
        {
            _payments.Payments.Add(new PaymentRecord
            {
                AccountId = account.Id,
                AccountName = account.Name,
                AmountPaid = amountPaid,
                PaidDate = date
                // DueDateAtPayment deliberately left null - these are historical catch-up
                // entries, not tied to a specific due-date cycle the way Mark as Paid is.
            });
            date = DueDateNotifier.RewindDueDate(date, spacing);
        }

        SavePayments();
        _mainForm?.RefreshData(_vault);
    }

    /// <summary>
    /// Called from MainForm's Profile form (Save) when the "Default browser" field
    /// changed. This is a per-machine, unencrypted preference (AppSettings), not
    /// something that rides along in the synced vault - see AppSettings.DefaultBrowserPath.
    /// </summary>
    private void SetDefaultBrowserPath(string? path)
    {
        DebugLog.Write($"SetDefaultBrowserPath: {(string.IsNullOrEmpty(path) ? "(cleared - back to system default)" : path)}.");
        _settings.DefaultBrowserPath = path;
        _settings.Save();
    }

    /// <summary>
    /// Pulls the newer copy (local vs. Drive, by modified time) and pushes ours up if
    /// ours is newer or nothing exists on Drive yet. This is a simple last-write-wins
    /// strategy - if you edit the vault on two machines between syncs, the later save wins
    /// and the earlier one's changes are lost, so sync often if you use more than one PC.
    /// </summary>
    private async Task SyncNowAsync(bool silent = false)
    {
        DebugLog.Write($"SyncNowAsync: called with silent={silent}.");
        if (!EnsureUnlocked())
        {
            DebugLog.Write("SyncNowAsync: EnsureUnlocked() returned false (still locked / user cancelled the unlock prompt) - aborting.");
            return;
        }

        if (!_drive.IsAuthenticated)
        {
            DebugLog.Write("SyncNowAsync: not authenticated yet.");
            if (!silent) await SignInToDriveAsync();
            if (!_drive.IsAuthenticated)
            {
                DebugLog.Write("SyncNowAsync: still not authenticated after sign-in attempt (or silent=true skipped it) - aborting.");
                return;
            }
        }

        _driveFileId ??= _settings.DriveFileId ?? await _drive.FindVaultFileIdAsync();
        DebugLog.Write($"SyncNowAsync: _driveFileId={(_driveFileId == null ? "(null)" : "set")}.");

        if (_driveFileId == null)
        {
            DebugLog.Write("SyncNowAsync: no remote file yet - delegating to UploadToDriveAsync to create one.");
            await UploadToDriveAsync();
            return;
        }

        var remoteModified = await _drive.GetRemoteModifiedTimeAsync(_driveFileId);
        var localModified = File.Exists(AppPaths.VaultLocalPath)
            ? File.GetLastWriteTimeUtc(AppPaths.VaultLocalPath)
            : DateTime.MinValue;
        DebugLog.Write($"SyncNowAsync: remoteModified={(remoteModified.HasValue ? remoteModified.Value.ToString("O") : "(null)")}, localModified={localModified:O}.");

        if (remoteModified.HasValue && remoteModified.Value > localModified)
        {
            DebugLog.Write("SyncNowAsync: remote is newer - pulling from Drive instead of pushing.");
            var bytes = await _drive.DownloadAsync(_driveFileId);
            var data = VaultStorage.LoadFromBytes(bytes, _secret!);
            _vault = data;
            File.WriteAllBytes(AppPaths.VaultLocalPath, bytes);
            _mainForm?.RefreshData(_vault);
            if (!silent) MessageBox.Show("Pulled the newer copy from Google Drive.", "Personal Vault");
        }
        else
        {
            DebugLog.Write("SyncNowAsync: local is newer (or equal) - pushing to Drive.");
            await UploadToDriveAsync();
            if (!silent) MessageBox.Show("Google Drive is up to date.", "Personal Vault");
        }
    }

    private async Task SignInToDriveAsync()
    {
        DebugLog.Write("SignInToDriveAsync: called (interactive).");
        try
        {
            bool ok = await _drive.SignInAsync(allowInteractive: true);
            DebugLog.Write($"SignInToDriveAsync: SignInAsync(allowInteractive: true) returned {ok}.");
            if (!ok)
            {
                MessageBox.Show(
                    "Google Drive credentials were not found.\n\nExpected file:\n" + AppPaths.CredentialsJsonPath +
                    "\n\nSee README.md for how to create one in Google Cloud Console.",
                    "Personal Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _warnedNotSignedInThisSession = false; // give the modal warning another chance if this connection later drops
            Notify("Personal Vault", "Signed in to Google Drive.");
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("SignInToDriveAsync", ex);
            MessageBox.Show("Google sign-in failed: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            DebugLog.Write($"SignInToDriveAsync: finished. _drive.IsAuthenticated is now {_drive.IsAuthenticated}.");
            // Whatever happened above (success, declined credentials, or an error), the
            // tray menu should always reflect the true current state afterward.
            UpdateDriveMenuState();
        }
    }

    private void ChangeSecret()
    {
        if (!EnsureUnlocked()) return;
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
        _idleTimer?.Stop();
        _idleTimer?.Dispose();
        _notifier?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        ToastNotifier.ClearAll();
        Application.Exit();
    }
}
