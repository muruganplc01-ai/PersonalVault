using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;
using PersonalVault.Models;
using PersonalVault.Security;
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
    private string? _settingsDriveFileId;
    private readonly List<SharedLink> _shares = SharesStorage.Load();
    private ShareExpiryService? _shareExpiry;
    private System.Windows.Forms.Timer? _idleTimer;
    private bool _isLocked;
    private ToolStripMenuItem? _startupMenuItem;
    private ToolStripMenuItem? _driveMenuItem;
    private bool _warnedNotSignedInThisSession;
    private bool _offeredEmptyVaultDriveRestoreThisSession;

    /// <summary>
    /// Exists purely so ShowMainForm() has a reliable way to hop onto the UI thread.
    /// TrayApplicationContext itself isn't a Control (ApplicationContext has no window
    /// of its own), and ShowMainForm() can be reached from a genuinely different thread
    /// - a toast notification's "Open Vault" button is activated by Windows via a
    /// background COM callback (see ToastActivator), which is finicky to marshal
    /// reliably for an unpackaged app. Rather than trust that hop, ShowMainForm() checks
    /// this control's InvokeRequired itself. Its handle is forced into existence in the
    /// constructor (see below) because InvokeRequired is unreliable before that.
    /// </summary>
    private readonly Control _uiThreadMarshal = new();

    public TrayApplicationContext()
    {
        AppPaths.EnsureFoldersExist();

        // Force the marshal control's handle to exist right away, on the UI thread,
        // so InvokeRequired in ShowMainForm() works correctly from the very first call
        // - without a created handle, InvokeRequired can incorrectly report false even
        // when called from a different thread.
        _ = _uiThreadMarshal.Handle;

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
        menu.Items.Add("Shared Links...", null, (_, _) => OpenSharedLinks());
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
            SaveSettings();
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

        using var form = new ProfileForm(_vault.Profile, _settings.DefaultBrowserPath, _settings.GitHubUsername, ChangeSecret, AppPaths.RootFolder, ChangeDataFolder);
        if (form.ShowDialog() == DialogResult.OK)
        {
            SetDefaultBrowserPath(form.SelectedBrowserPath);
            SetGitHubUsername(form.GitHubUsername);
            SaveVault();
            _mainForm?.RefreshData(_vault);
        }
    }

    private void OpenSharedLinks()
    {
        if (!EnsureUnlocked()) return;

        _shareExpiry?.CheckNowAsync(); // catch up on anything missed while this window was closed, before showing the list
        using var form = new SharedLinksForm(_shares, RevokeShareNowAsync);
        form.ShowDialog();
    }

    /// <summary>
    /// Encrypts a single account under a fresh random key, uploads it to Drive as a
    /// completely private file (never "anyone with the link" - only the owner's Apps
    /// Script Web App can ever read it, and only once, see
    /// GoogleDriveSync.UploadShareAsync), records a SharedLink for the background
    /// expiry sweep / "Shared Links..." management list, and returns the full share URL
    /// (built from AppSettings.GitHubUsername plus the Drive file id and key in the URL
    /// fragment, so the key itself never reaches any server - see
    /// docs/share/index.html). Called from MainForm's Share... button via
    /// ShareAccountForm.
    /// </summary>
    private async Task<string> ShareAccountAsync(AccountEntry account, TimeSpan lifetime)
    {
        if (!_drive.IsAuthenticated)
            throw new InvalidOperationException("Sign in to Google Drive first (tray menu -> Sign in to Google Drive) - sharing needs somewhere to host the encrypted link.");

        if (string.IsNullOrWhiteSpace(_settings.GitHubUsername))
            throw new InvalidOperationException("Set your GitHub username first (Profile...) - the share link points at your GitHub Pages viewer page, which needs to know whose Pages site to use.");

        var expiresUtc = DateTime.UtcNow.Add(lifetime);
        var payload = new SharedAccountPayload
        {
            Name = account.Name,
            Category = account.Category,
            Institution = account.Institution,
            UserName = account.UserName,
            Password = account.Password,
            AccountNumber = account.AccountNumber,
            Website = account.Website,
            PhoneNumber = account.PhoneNumber,
            Notes = account.Notes,
            ExtraFields = new Dictionary<string, string>(account.ExtraFields),
            ExpiresUtc = expiresUtc
        };

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        byte[] key;
        string driveFileId;
        try
        {
            var (blob, rawKey) = ShareCrypto.Encrypt(plaintext);
            key = rawKey;
            driveFileId = await _drive.UploadShareAsync(blob, $"{GoogleDriveSync.ShareFilePrefix}{Guid.NewGuid()}.bin");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        _shares.Add(new SharedLink
        {
            AccountEntryId = account.Id,
            AccountName = account.Name,
            DriveFileId = driveFileId,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = expiresUtc
        });
        SharesStorage.Save(_shares);

        string keyBase64Url = Convert.ToBase64String(key).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        long expUnixSeconds = ((DateTimeOffset)expiresUtc).ToUnixTimeSeconds();

        // Assumes the repo stays named "PersonalVault" with GitHub Pages served from
        // /docs at the default branch's root - see README.md's "Sharing an account"
        // section for the one-time Pages setup this depends on.
        string viewerBaseUrl = $"https://{_settings.GitHubUsername}.github.io/PersonalVault/share/";
        return $"{viewerBaseUrl}#id={Uri.EscapeDataString(driveFileId)}&key={keyBase64Url}&exp={expUnixSeconds}";
    }

    /// <summary>
    /// The Drive half of revoking a share, with no persistence side effect - this is
    /// exactly what ShareExpiryService's background sweep needs (it sets Revoked and
    /// saves shares.json itself, once per sweep, after possibly revoking several).
    /// Throws if not signed in to Drive, so the sweep leaves the share unrevoked and
    /// retries on its next tick rather than silently pretending it succeeded.
    /// </summary>
    private async Task RevokeShareOnDriveAsync(SharedLink share)
    {
        if (!_drive.IsAuthenticated)
            throw new InvalidOperationException("Not signed in to Google Drive.");
        await _drive.RevokeShareAsync(share.DriveFileId);
    }

    /// <summary>SharedLinksForm's "Revoke Now" button - revokes on Drive, then immediately marks/persists Revoked (unlike the background sweep, a single manual click doesn't need to batch anything).</summary>
    private async Task RevokeShareNowAsync(SharedLink share)
    {
        await RevokeShareOnDriveAsync(share);
        share.Revoked = true;
        SharesStorage.Save(_shares);
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(
            _settings,
            AppPaths.VaultLocalPath,
            AppPaths.CredentialsJsonPath,
            _drive.IsAuthenticated);
        if (form.ShowDialog() != DialogResult.OK) return;

        SaveSettings();
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

            // Settings restore runs before StartIdleMonitor so a restored AutoLockMinutes
            // value takes effect from the very first idle check, not just after a restart.
            await LoadOrRestoreSettingsAsync();
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

            // Whether just connected above, silently re-authenticated from a cached
            // token, or already connected from earlier this session, catch the specific
            // bug this was written to fix: a vault that's never actually been used
            // (zero accounts) while Drive already has a real one - happens whenever a
            // vault gets created locally before Drive was ever connected (e.g.
            // credentials.json wasn't in the data folder yet at this exact startup, so
            // the firstRun disaster-recovery check above had nothing to check against).
            // Doing this BEFORE the automatic SyncNowAsync call below matters: without
            // it, that silent sync could decide the empty local vault is "newer" than
            // Drive's real one purely by file timestamp and push it, overwriting the
            // Drive backup with nothing asked.
            await OfferDriveRestoreIfLocalVaultLooksEmptyAsync();

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

            _shareExpiry = new ShareExpiryService(
                () => _shares,
                SharesStorage.Save,
                RevokeShareOnDriveAsync);
            _shareExpiry.Start();

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
        // This can be reached from a thread other than the UI thread - most notably a
        // toast notification's "Open Vault" button, which Windows activates via a
        // background COM callback (see ToastActivator/ToastNotifier). Everything below
        // touches Controls created on the UI thread, so hop over there first rather
        // than trusting every current and future caller to already be on it.
        if (_uiThreadMarshal.InvokeRequired)
        {
            _uiThreadMarshal.BeginInvoke(new Action(ShowMainForm));
            return;
        }

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
                SetDefaultBrowserPath,
                () => _settings.GitHubUsername,
                SetGitHubUsername,
                ChangeSecret,
                () => AppPaths.RootFolder,
                ChangeDataFolder,
                ShareAccountAsync);
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
        SaveSettings();
    }

    /// <summary>
    /// Called from ProfileForm (Save) when the "GitHub username" field changed. Unlike
    /// SetDefaultBrowserPath, this goes through SaveSettings() the same way but the
    /// value it sets IS meant to follow the vault to a new PC - see
    /// AppSettings.GitHubUsername and LoadOrRestoreSettingsAsync below.
    /// </summary>
    private void SetGitHubUsername(string? username)
    {
        DebugLog.Write($"SetGitHubUsername: {(string.IsNullOrEmpty(username) ? "(cleared)" : username)}.");
        _settings.GitHubUsername = username;
        SaveSettings();
    }

    /// <summary>
    /// Call this (instead of _settings.Save() directly) whenever the USER actually
    /// changed a preference - auto-lock, reminder days, start-with-Windows, default
    /// browser - so it also gets backed up to Drive as part of disaster recovery.
    /// Internal bookkeeping saves elsewhere (caching a Drive file id right after a
    /// vault/payments upload) intentionally keep calling _settings.Save() directly
    /// instead, so a routine vault save doesn't also re-upload settings.json every time.
    /// </summary>
    private void SaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            // A settings-save failure (e.g. the file was momentarily locked by another
            // process and AppSettings.Save's own retries didn't outlast it) must never
            // crash the whole app - this used to propagate straight up through a Button
            // click handler and take down the entire WinForms message loop.
            DebugLog.WriteException("SaveSettings (non-fatal - the app keeps running with this change only in memory)", ex);
            Notify("Personal Vault", "Could not save settings: " + ex.Message, ToolTipIcon.Warning);
            return; // Don't try to upload a save that didn't actually happen locally.
        }

        _ = UploadSettingsToDriveAsync();
    }

    /// <summary>Same shape as UploadPaymentsToDriveAsync, for the local-preferences file.</summary>
    private async Task UploadSettingsToDriveAsync()
    {
        DebugLog.Write($"UploadSettingsToDriveAsync: called. _drive.IsAuthenticated={_drive.IsAuthenticated}.");
        if (!_drive.IsAuthenticated) return;

        try
        {
            _settingsDriveFileId ??= _settings.SettingsDriveFileId
                ?? await _drive.FindFileIdAsync(GoogleDriveSync.RemoteSettingsFileName);
            DebugLog.Write($"UploadSettingsToDriveAsync: resolved _settingsDriveFileId={(_settingsDriveFileId == null ? "(null - will create new file)" : "set")}.");

            _settingsDriveFileId = await _drive.UploadOrUpdateAsync(
                AppPaths.SettingsPath, _settingsDriveFileId, GoogleDriveSync.RemoteSettingsFileName);

            // Cache the id with a plain Save() (not SaveSettings()) so this doesn't
            // trigger another upload of itself.
            _settings.SettingsDriveFileId = _settingsDriveFileId;
            _settings.Save();
            DebugLog.Write("UploadSettingsToDriveAsync: upload call returned without throwing - upload succeeded.");
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("UploadSettingsToDriveAsync", ex);
            Notify("Personal Vault", "Could not sync settings to Google Drive: " + ex.Message, ToolTipIcon.Warning);
        }
    }

    /// <summary>
    /// Disaster-recovery counterpart for settings.json: only ever does anything when
    /// this PC has no local settings.json yet (a genuinely fresh %AppData%, same
    /// "firstRun" moment the vault/payments restores use) and Drive is already signed
    /// in. Deliberately does NOT restore DefaultBrowserPath - a browser's install path
    /// is a property of a specific PC, not something that should follow the vault to a
    /// different machine, so this PC keeps whatever it already has for that one field
    /// (empty/system-default on a truly fresh install) while adopting everything else.
    /// GitHubUsername is the opposite case: it identifies a GitHub account, not a PC, so
    /// (unlike DefaultBrowserPath) it IS restored here - a brand-new PC set up via
    /// disaster recovery gets Share Account links working immediately, with no need to
    /// re-type the username from Profile.
    /// </summary>
    private async Task LoadOrRestoreSettingsAsync()
    {
        if (File.Exists(AppPaths.SettingsPath) || !_drive.IsAuthenticated) return;

        try
        {
            var remoteId = await _drive.FindFileIdAsync(GoogleDriveSync.RemoteSettingsFileName);
            if (remoteId == null) return;

            var bytes = await _drive.DownloadAsync(remoteId);
            var remoteSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(bytes);
            if (remoteSettings == null) return;

            _settings.StartWithWindows = remoteSettings.StartWithWindows;
            _settings.ReminderDaysBefore = remoteSettings.ReminderDaysBefore;
            _settings.AutoLockMinutes = remoteSettings.AutoLockMinutes;
            _settings.DriveFileId ??= remoteSettings.DriveFileId;
            _settings.PaymentsDriveFileId ??= remoteSettings.PaymentsDriveFileId;
            _settings.GitHubUsername ??= remoteSettings.GitHubUsername;
            // DefaultBrowserPath intentionally left as this PC's own value - see doc comment above.

            _settingsDriveFileId = remoteId;
            _settings.SettingsDriveFileId = remoteId;
            _settings.Save();
            DebugLog.Write("LoadOrRestoreSettingsAsync: restored preferences from Google Drive.");
        }
        catch (Exception ex)
        {
            // Non-fatal either way: worst case, preferences just start at their defaults
            // on this PC, same as before this feature existed.
            DebugLog.WriteException("LoadOrRestoreSettingsAsync (non-fatal)", ex);
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

        // Defense-in-depth against the same bug OfferDriveRestoreIfLocalVaultLooksEmptyAsync
        // fixes at sign-in time: an empty local vault's file timestamp is often "now" (it
        // was just created), which would otherwise look "newer" than Drive's real copy
        // below and get pushed, overwriting it. Skipped if the user already went through
        // that check this session (accepted OR explicitly declined it) - an explicit "No,
        // keep this empty vault" is an informed decision this method should respect, not
        // silently override.
        if (_vault!.Accounts.Count == 0 && !_offeredEmptyVaultDriveRestoreThisSession)
        {
            try
            {
                var remoteBytesCheck = await _drive.DownloadAsync(_driveFileId);
                var remoteData = VaultStorage.LoadFromBytes(remoteBytesCheck, _secret!);
                if (remoteData.Accounts.Count > 0)
                {
                    DebugLog.Write("SyncNowAsync: local vault has zero accounts and Drive's copy has real data - pulling instead of comparing timestamps.");
                    _vault = remoteData;
                    File.WriteAllBytes(AppPaths.VaultLocalPath, remoteBytesCheck);
                    _mainForm?.RefreshData(_vault);
                    _offeredEmptyVaultDriveRestoreThisSession = true;
                    if (!silent) MessageBox.Show(
                        "Your local vault was empty - pulled the existing copy from Google Drive instead of overwriting it.",
                        "Personal Vault");
                    return;
                }
            }
            catch (CryptographicException)
            {
                // Drive's vault uses a different secret than the one currently unlocked -
                // can't reconcile automatically. Fall through to the normal timestamp
                // logic below (which will likely push the empty vault) - the manual
                // recovery path (delete the local vault file and restart) is the way to
                // actually restore a backup secured with a different secret.
                DebugLog.Write("SyncNowAsync: remote vault decrypt failed with the current secret - can't compare, falling through to normal sync logic.");
            }
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

            // This is the fix for the actual bug: connecting Drive manually, mid-session,
            // after a vault was already created locally (e.g. credentials.json wasn't in
            // place yet at startup) used to just authenticate and stop there - nothing
            // ever checked whether a real vault was already sitting on Drive. See
            // OfferDriveRestoreIfLocalVaultLooksEmptyAsync for the full explanation.
            await OfferDriveRestoreIfLocalVaultLooksEmptyAsync();
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

    /// <summary>
    /// Catches a vault that looks like it's never actually been used (zero accounts)
    /// while Drive already has a real one - the scenario that caused a real data-loss
    /// near-miss: a vault got created locally before Drive was connected (e.g.
    /// credentials.json wasn't in the data folder yet at that exact startup, so
    /// InitializeAsync's own firstRun disaster-recovery check had no credentials to act
    /// on and skipped straight to creating a new, empty vault), and by the time Drive
    /// was connected - either by a later silent re-authentication at startup or a manual
    /// "Sign in to Google Drive" click - there was no longer any "no local vault file
    /// yet" moment left to trigger the normal restore check.
    ///
    /// Deliberately does nothing if the current vault already has real accounts in it -
    /// this should never interrupt someone who's actually been using the app locally
    /// and is only now getting around to connecting Drive. The one-time-per-session
    /// guard means whichever caller runs first (InitializeAsync's startup check or a
    /// manual sign-in) "wins" and later callers this same session are silent no-ops, so
    /// the user is never asked about the same vault twice in one run - including not
    /// re-litigating an explicit "No, keep this empty vault" answer.
    /// </summary>
    private async Task OfferDriveRestoreIfLocalVaultLooksEmptyAsync()
    {
        if (_offeredEmptyVaultDriveRestoreThisSession) return;
        if (_vault == null || _secret == null) return;
        if (_vault.Accounts.Count > 0) return;
        if (!_drive.IsAuthenticated) return;

        _offeredEmptyVaultDriveRestoreThisSession = true; // set up front so this can never ask twice, even if something below throws

        try
        {
            var remoteFileId = _driveFileId ?? _settings.DriveFileId ?? await _drive.FindVaultFileIdAsync();
            if (remoteFileId == null) return; // Nothing on Drive yet either - this genuinely is a brand-new vault.

            var remoteBytes = await _drive.DownloadAsync(remoteFileId);

            var restore = MessageBox.Show(
                "A vault already exists on Google Drive, but the one open right now has no " +
                "accounts in it yet - this usually means it was created before Drive was " +
                "connected.\n\n" +
                "Restore the one from Google Drive instead? You'll need the master secret it " +
                "was created with. Choose \"No\" to keep using this empty vault (it will " +
                "overwrite the Drive copy the next time it syncs).",
                "Personal Vault", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (restore != DialogResult.Yes) return;

            if (RestoreVaultFromBytes(remoteBytes))
            {
                _driveFileId = remoteFileId;
                _settings.DriveFileId = _driveFileId;
                _settings.Save();
                _mainForm?.RefreshData(_vault!);
                Notify("Personal Vault", "Restored vault from Google Drive.");
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: worst case, this opportunistic check silently didn't happen -
            // the manual recovery path (delete the local vault file and restart, so
            // InitializeAsync's own firstRun check runs fresh) still works.
            DebugLog.WriteException("OfferDriveRestoreIfLocalVaultLooksEmptyAsync (non-fatal)", ex);
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

    /// <summary>
    /// Called from ProfileForm's "Change Data Folder..." (the confirmation prompt
    /// already happened there). DataFolderMover.MoveTo does the actual copy and
    /// repoints AppPaths/registers the choice for next launch - nothing here needs to
    /// touch _vault/_secret/_settings, since every future save already reads its target
    /// path from AppPaths fresh each time rather than caching it.
    /// </summary>
    private void ChangeDataFolder(string newFolder) => DataFolderMover.MoveTo(newFolder);

    private void ExitApplication()
    {
        _idleTimer?.Stop();
        _idleTimer?.Dispose();
        _notifier?.Dispose();
        _shareExpiry?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _uiThreadMarshal.Dispose();
        ToastNotifier.ClearAll();
        Application.Exit();
    }
}
