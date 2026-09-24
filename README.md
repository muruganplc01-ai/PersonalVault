# Personal Vault

A Windows system-tray app that stores your account credentials (bank, credit card,
mortgage, insurance, utilities, memberships, car loan, home tax, rental, investments,
etc.) in a single AES-256 encrypted file, keeps that file synced to Google Drive, and
pops a notification when something is coming due.

This is a working v2 - see "What's next" below for what's still just an idea.

## How it works

- **Encryption**: AES-256-GCM (authenticated encryption) with a key derived from your
  master secret via PBKDF2-HMAC-SHA256 (300,000 iterations). `.NET`'s crypto classes
  call into Windows' native CNG/BCrypt library, so this is "Windows AES" under the
  hood. The secret itself is **never stored anywhere** - only its derived key, in
  memory, for as long as the app is running. Lose the secret and the data cannot be
  recovered; there's no backdoor by design.
- **Storage**: everything (account type, institution, username, password, account
  number, due date, notes, arbitrary extra fields, ...) lives as one JSON blob inside
  that encrypted file. By default it's a `PersonalVault` folder right next to the
  running `.exe` (a portable, no-installer layout) - `vault.pvlt` inside it. Move it
  anywhere via Profile -> **Change Data Folder...**, which copies everything over and
  remembers the choice (in the registry, not inside the folder itself, since the app
  needs to know where to look before it knows where the folder is) so it's picked up
  again on the next launch - see "Data folder location" below.
- **Sync**: the encrypted file (never the plaintext) is uploaded to Google Drive via
  the Drive API, using the narrow `drive.file` scope - the app can only see files it
  created, not your whole Drive. Sync uses simple last-write-wins: on startup, and
  whenever you click "Sync Now", it compares local vs. remote modified time and pulls
  or pushes accordingly.
- **Notifications**: a background timer checks due dates every 30 minutes and shows a
  tray balloon notification at 7, 3, and 1 day(s) before a due date, on the due date
  itself, and daily if something becomes overdue. Recurring accounts (monthly, yearly,
  etc.) automatically roll their due date forward once flagged as overdue.
- **Always running**: it's a tray app (no taskbar window) with an optional "Start with
  Windows" toggle in the tray menu, on by default for new installs.
- **Changing the secret**: tray menu → "Change Master Secret..." (or Profile → "Change
  Master Password...", same dialog) re-encrypts the *entire* vault file under a
  brand-new key (and new random salt) in one step, then re-uploads it to Drive. A new
  secret must meet the same 12-character/uppercase/lowercase/symbol requirement as
  creating a vault - see "First run" below.
- **Two-factor authentication**: Profile → **Two-Factor Authentication...** adds a
  second step after the master secret - either a standard TOTP authenticator app
  (Google Authenticator, Authy, etc.) or an emailed one-time code. The authenticator
  secret is deliberately stored outside the encrypted vault (Windows DPAPI, tied to this
  PC and Windows account) rather than inside it, specifically so someone who steals
  `vault.pvlt` and even correctly guesses the master secret still can't generate a valid
  code - see `Instructions/PersonalVault-Security-Deep-Dive.html` for the full reasoning.
  Ten one-time backup codes are generated when TOTP is turned on (shown once - save
  them) and travel with the vault for exactly this reason: the authenticator secret
  itself doesn't survive a move to a new PC, but a backup code does. Email codes instead
  require Drive to be connected with Gmail's send-only scope granted (existing
  Drive-connected accounts get prompted once to add it). **Prefer TOTP over Email** if
  the vault needs to stay usable offline: Email MFA needs an active internet connection
  and a signed-in Drive/Gmail session just to unlock the local vault, while TOTP codes
  generate locally with no connectivity at all.
- **Credit card details**: for a `CreditCard`-category entry, click **Card
  Details...** in the account editor for a structured popup (card number formatted as
  you type, expiration month/year dropdowns, security code, cardholder name) instead of
  typing them into Extra info by hand. The card number writes into the entry's own
  Account # field; the rest are stored as Extra info fields under the hood - no new
  vault schema, so older entries are unaffected.
- **Auto-lock**: after 10 minutes (by default) with no keyboard/mouse activity
  *anywhere on the system* (not just in this app - the same signal every mainstream
  password manager uses), the vault locks itself: the window hides if open, and the
  secret and every decrypted password are dropped from memory. Reopening the vault (or
  Sync Now / Change Secret) asks for the master secret again before doing anything. You
  can also lock it immediately yourself via tray menu → "Lock Now". Change the timeout
  (or disable it) from tray menu → **Settings...**.
- **Settings UI**: tray menu → "Settings..." edits auto-lock minutes, the due-date
  reminder windows, and "Start with Windows" - no more hand-editing `settings.json`
  yourself.
- **Profile**: tray menu → "Profile..." (or the "Profile..." button in the account
  list window) sets your display name and an optional picture. Both are stored only
  inside the encrypted vault file itself - the picture is never uploaded anywhere
  separately, so it gets the same protection and the same Drive sync as everything
  else. Your profile name becomes the default "Owner" on any new account you add
  (handy once more than one family member's accounts live in the same vault) - you can
  always override it per account.
- **Category-specific fields**: click "+ Category Fields" in the account editor to
  drop in blank labels typical for that category (e.g. APR/term for a car loan,
  policy/premium for insurance, lease dates for a rental) into the Extra Info box -
  just fill in the values. This is a convenience on top of the existing free-form
  key=value fields, not a new data format, so nothing about older entries changes.
- **Multiple bank accounts under one entry**: for a `BankAccount`-category entry, click
  **Bank Accounts...** in the account editor to add Checking, Savings, Money Market, or
  however many separate accounts that institution has - each with its own account
  number, routing number, and balance. These ride along with the entry (encrypted,
  synced, and included if you Share it). Once any sub-account has a balance set, the
  Overview tab's "Total On Hand" uses the *sum* of them for that entry instead of its
  own Current Balance field - so there's nothing to keep in sync by hand, just fill in
  the sub-accounts and leave Current Balance blank.
- **Search**: the account list has a live search box that matches across every field -
  name, institution, owner, username, notes, and any extra field - not just the ones
  shown as columns.
- **Import / export**: "Export CSV..." and "Import CSV..." in the account list window
  read/write a documented CSV format (see the header row `CsvIO.cs` writes) so you can
  back up outside the vault or bring in accounts from another password manager's CSV
  export (line up its columns to match, or use this app's own export as a template).
  **An exported CSV is plain, unencrypted text** - every password readable in the
  clear - so treat it as sensitive and delete it once you're done with it.
- **Clipboard auto-clear**: after copying a username or password, the clipboard clears
  itself automatically about 20 seconds later (only if you haven't copied something
  else in the meantime).
- **Rolling Drive backups**: each successful sync pins the revision it just uploaded
  and prunes anything older than the last 5, using Google Drive's own revision
  history - so an accidental delete, a bad edit, or a botched sync has a recent copy to
  recover from (via Drive's web UI → right-click the file → "Manage versions"). This is
  best-effort and never blocks or fails a sync.
- **Share an account (real one-time link)**: click **Share...** in the account list
  window to send one account to someone who doesn't use Personal Vault at all - no app,
  no account, no sign-in on their end. Pick how long the link stays live if nobody
  opens it (1 hour to 7 days), and the app encrypts just that one account under a
  brand-new random key and uploads it to your own Google Drive as a completely private
  file. The link itself is only ever readable through a small Google Apps Script Web
  App you deploy once (see "Sharing an account (one-time link)" below) - it deletes the
  Drive file the instant it's first opened, so this is a real one-time view (first
  device wins, dead everywhere after), not just an expiring link.
- **Rich notifications**: due-date alerts, lock notices, and sync status use real
  Windows action-center toasts (via `Microsoft.Toolkit.Uwp.Notifications`) with an
  "Open Vault" button, when Windows will let this unpackaged app register for them.
  AUMID/COM registration for a plain WinForms app (not an installed/MSIX app) is known
  to be finicky across Windows versions, so every toast call is wrapped in a fallback:
  if it doesn't work on your PC, you silently get the same tray balloon tips as before
  instead of anything breaking.

## Project layout

```
PersonalVault.sln
PersonalVault/
  Program.cs                 Entry point (single-instance mutex, starts the tray context)
  Models/AccountEntry.cs     Account record + category/recurrence enums + CategoryFieldSpec (suggested fields per category)
  Models/VaultData.cs        The full vault (accounts + profile)
  Models/VaultProfile.cs     The vault owner's display name + optional picture
  Security/VaultCrypto.cs    AES-256-GCM encrypt/decrypt + PBKDF2 key derivation
  Storage/VaultStorage.cs    JSON <-> encrypted bytes <-> vault.pvlt on disk
  Storage/GoogleDriveSync.cs Google Drive OAuth + upload/download + rolling revision backups
  Storage/AppPaths.cs        All file/folder locations (default: a PersonalVault folder next to the .exe; overridable, see Utils/DataFolderMover.cs)
  Storage/AppSettings.cs     Small non-secret settings (reminder days, cached Drive file id, auto-lock timeout)
  Services/DueDateNotifier.cs   Due-date scanning + notifications
  Services/ToastNotifier.cs     Rich toast notifications, with fallback to tray balloons
  Services/ToastActivator.cs    COM callback Windows uses when a toast is clicked
  Forms/UnlockForm.cs         Enter/create the master secret
  Forms/MainForm.cs           Account list (search, add/edit/delete/copy, export/import, profile)
  Forms/AccountEditForm.cs    Add/edit a single account, incl. category-specific suggested fields
  Forms/CreditCardDetailsForm.cs  "Card Details..." popup for the CreditCard category
  Forms/BankAccountsForm.cs   Checking/Savings/Money Market list for a BankAccount entry
  Forms/BankSubAccountForm.cs Add/edit one bank sub-account
  Forms/ProfileForm.cs        Edit the vault owner's name/picture
  Forms/SettingsForm.cs       Edit AutoLockMinutes / reminder days / Start with Windows
  Forms/ChangeSecretForm.cs   Change the master secret
  Security/PasswordPolicy.cs  Minimum-strength rule for a new/changed master secret
  Security/TotpGenerator.cs  RFC 6238 TOTP (matches Google Authenticator/Authy et al.)
  Security/Base32.cs         Base32 encode/decode - how a TOTP secret is shown/entered
  Storage/MfaSecretStorage.cs  DPAPI-protected local TOTP secret, deliberately outside the vault
  Storage/GmailSender.cs     Sends Email MFA's one-time code via the Drive-authorized Gmail scope
  Forms/MfaSetupForm.cs      Profile's "Two-Factor Authentication..." configuration dialog
  Forms/MfaVerifyForm.cs     The unlock-time second-factor prompt
  Forms/ShareAccountForm.cs   "Share Account" dialog - pick expiration, get a link
  Forms/SharedLinksForm.cs    "Shared Links..." - list/revoke active share links
  Forms/TrayApplicationContext.cs   Owns the tray icon and ties everything together
  Models/SharedLink.cs           Bookkeeping for one active share link (see shares.json)
  Models/SharedAccountPayload.cs What's actually encrypted and sent for a shared account
  Security/ShareCrypto.cs    AES-256-GCM for a shared account, random per-share key (no password)
  Storage/SharesStorage.cs   Load/save shares.json (the SharedLink list)
  Services/ShareExpiryService.cs Background sweep that revokes expired share links
  Utils/StartupManager.cs     "Start with Windows" via the per-user Run registry key
  Utils/CredentialBootstrap.cs   Auto-copies a bundled credentials.json into the data folder on first run
  Utils/DataFolderLocation.cs Remembers a custom data folder choice in the registry (read before AppPaths knows where to look)
  Utils/DataFolderMover.cs    Profile's "Change Data Folder..." - copies everything to a new location
  Utils/SystemIdleTime.cs     Reads system-wide idle time (Win32 GetLastInputInfo) for auto-lock
  Utils/CsvIO.cs              CSV export/import for accounts
  Resources/AppIcon.ico       App/tray icon (embedded into the .exe via <ApplicationIcon>)
docs/share/index.html          Static page (host via GitHub Pages) that decrypts/shows a shared account
docs/share/AppsScript/Code.gs   Deploy as a Web App - serves+deletes a share's Drive file exactly once
```

## Building

Requires **Windows** (WinForms + Windows Credential/CNG APIs) with the **.NET 8 SDK**.
This code was written and reviewed carefully, but it was developed in a Linux sandbox
that cannot run WinForms, so it has **not** been build-verified on a real Windows
machine yet - budget time for a first build/fix pass.

```
cd PersonalVault
dotnet restore
dotnet build
dotnet run --project PersonalVault
```

Or just open `PersonalVault.sln` in Visual Studio 2022+ and press F5.

If `Google.Apis.Drive.v3` fails to restore at the pinned version in the `.csproj`, run:

```
dotnet add PersonalVault/PersonalVault.csproj package Google.Apis.Drive.v3
```

to pick up whatever the current version is - the code only uses long-stable APIs from
that package.

## Data folder location

By default, Personal Vault keeps everything (`vault.pvlt`, `payments.pvlt`,
`settings.json`, `credentials.json`, the Drive sign-in token cache, `shares.json`) in a
`PersonalVault` folder created right next to `PersonalVault.exe` - a portable,
no-installer layout: copy the whole containing folder somewhere else (another drive, a
USB stick) and your data goes with it, no hunting through `%AppData%` required.

To use a different folder instead - a synced folder, a different drive, wherever you'd
rather keep it - open **Profile...** and use **Change Data Folder...** under "Data
folder": pick a folder, confirm, and everything is copied there immediately (the old
folder is left untouched as a backup - nothing is deleted). The choice is remembered in
the registry (`HKCU\Software\PersonalVault`, not inside the data folder itself, since
the app has to know where to look before it knows where the folder is) so it's picked
up again automatically on every future launch, including after moving the .exe itself.

If you skip this entirely, the default (next to the .exe) is created automatically the
first time the app runs and needs it - there's nothing to set up ahead of time.

## One-time setup: Google Drive

The app needs an OAuth **client** (not a service account) so it can ask *you* to grant
it access, the first time you click "Sign in to Google Drive" from the tray menu.

1. Go to the [Google Cloud Console](https://console.cloud.google.com/), create a
   project (or pick an existing one).
2. **APIs & Services → Library** → enable the **Google Drive API**.
3. **APIs & Services → OAuth consent screen** → set it up as "External" (or
   "Internal" if you're on a Workspace account), fill in the required fields. While
   it's in "Testing" mode, add your own Google account under "Test users".
4. **APIs & Services → Credentials → Create Credentials → OAuth client ID** → choose
   application type **Desktop app**.
5. Download the resulting JSON, rename it to `credentials.json`, and place it inside
   the app's data folder - by default a `PersonalVault` folder right next to
   `PersonalVault.exe` (run the app once first so it creates the folder for you; check
   Profile → **Data folder** if you're not sure where that is, e.g. after using
   **Change Data Folder...**).
6. Run the app, unlock/create your vault, then use the tray menu → **Sign in to
   Google Drive**. A browser window opens for you to grant access once; after that,
   the app stays signed in (a refresh token is cached locally in that same data
   folder's `drive-token\` subfolder).

If you'd rather not deal with Google Cloud Console at all yet, you can skip this
entirely - the app works fully offline against the local encrypted file, and you can
wire up Drive later.

**If you want Email two-factor authentication too:** also enable the **Gmail API** in
the same project (step 2 above, same Library page). Nothing extra happens at normal
sign-in either way - the app only asks Google for permission to send email at the
moment you actually turn on Email MFA in Profile, not before.

## Sharing this with family members

Each person should get their **own private vault** (own master secret, own accounts,
own Drive file) - nobody else's passwords are visible to them, even though everyone's
running the same app. Setup for this:

1. **One Google Cloud project covers the whole family** - you don't need to repeat the
   Google Cloud Console steps per person. In the same project you already created, go
   to **Google Auth Platform → Audience → Test users → Add users**, and add each family
   member's own Gmail address there (up to 100). That's the only step that truly can't
   be automated - Google requires each person's own account to be explicitly allowed.
2. **Build a portable, self-contained copy** so they don't need Visual Studio or the
   .NET SDK installed. From a command prompt in the `PersonalVault` project folder:
   ```
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
   ```
   This produces a `publish\` folder with `PersonalVault.exe` and everything it needs
   bundled in (it'll be ~100-150 MB - that's the .NET runtime included, which is why no
   separate install step is needed on their PC).
3. **Copy your `credentials.json` into that same `publish\` folder**, right next to
   `PersonalVault.exe`. This is safe to share - it only identifies the *app* to Google,
   it is not a credential to your data, and each person still authorizes with their own
   Google account and gets their own separate Drive file.
4. **Zip the `publish\` folder** and send it to them (email, USB drive, shared folder -
   however you'd share any file). They unzip it anywhere and double-click
   `PersonalVault.exe` - no install, no admin rights needed.
5. On first launch, the app automatically copies the bundled `credentials.json` into
   their own `PersonalVault` data folder (created right next to their copy of the
   .exe) the moment it sees one sitting next to it - they never have to find that
   folder or copy anything by hand. After they
   create their master secret, it directly asks "Connect Google Drive now?" - clicking
   Yes pops the same one-time Google sign-in browser window you saw, just for their own
   account.
6. From there their vault, their Drive file, and their secret are entirely their own -
   completely separate from yours, even though you both got the app from the same zip.

If you'd rather build a polished installer (Start Menu shortcut, uninstaller) instead
of a portable folder, that's possible with a tool like
[Inno Setup](https://jrsoftware.org/isinfo.php), but needs its own separate setup script
- ask if you want help writing one.

## Sharing an account (one-time link)

Personal Vault has no server of its own, so a "share this one account" link needs
somewhere to live: it works by encrypting a single account under a fresh random key
(never your master secret), uploading the ciphertext as a **completely private** Drive
file, and pointing the link at a small static page that decrypts it in the recipient's
own browser. The decryption key travels only in the URL's `#fragment`, which browsers
never send to any server - so neither Google, nor GitHub Pages, nor anyone but the
recipient ever sees it.

Reading a private Drive file on someone else's behalf, and deleting it the instant
that happens, needs *something* running with the vault owner's own authority - that's
what the small Google Apps Script Web App (`docs/share/AppsScript/Code.gs`) is for. It
runs "as you" no matter who calls it, is free, and lives in the same Google account
already used for Drive sync - no separate hosting or billing. This is what makes the
link a **real one-time view**: the first successful open deletes the Drive file, so
it's dead everywhere - including that same device again - immediately after, not just
once it eventually expires.

**One-time setup** (you do this once, not per-share):

1. **Publish the viewer page.** This repo already has it at `docs/share/index.html`. In
   this repo's GitHub settings: **Settings -> Pages -> Source -> Deploy from a branch
   -> Branch: `main`, Folder: `/docs`** -> Save. GitHub gives you a URL like
   `https://yourusername.github.io/PersonalVault/`.
2. **Deploy the Apps Script Web App.** Go to [script.google.com](https://script.google.com/)
   -> **New project** -> replace the default `Code.gs` contents with this repo's
   `docs/share/AppsScript/Code.gs` -> **Deploy -> New deployment**:
   - Select type **Web app**.
   - **Execute as: Me** (your Google account - this is what lets it read a private
     Drive file on the recipient's behalf).
   - **Who has access: Anyone** (not "Anyone with a Google account" - recipients must
     not need to sign in to anything).
   - Deploy, then copy the `.../exec` URL it gives you.
3. **Paste the Web App URL into the viewer page:** open `docs/share/index.html`, find
   `APPS_SCRIPT_URL = "https://script.google.com/macros/s/YOUR-DEPLOYMENT-ID/exec"`
   near the top of the `<script>`, and replace it with the URL from step 2. Commit and
   push - GitHub Pages redeploys automatically. (Unlike the old API-key approach, this
   URL isn't a secret to protect - the only thing anyone could do by knowing it is
   trigger the same "serve once, then delete" behavior the app already relies on.)
4. **Set your GitHub username in the app:** tray menu (or the account list window) ->
   **Profile...** -> **GitHub username (for Share links)** -> enter the same username
   from your Pages URL in step 1 -> **Save**. This is stored in your local settings and
   is what the app uses to build `https://<username>.github.io/PersonalVault/share/`
   links - no rebuild needed, and it follows the vault to a new PC during disaster
   recovery (see "Disaster recovery" below). It assumes the repo stays named
   `PersonalVault` with Pages served from `/docs`, matching step 1 - if you forked or
   renamed the repo, the link the app builds won't match your real Pages URL.

**Using it:** in the account list window, select an account -> **Share...** -> pick how
long the link should stay live *if nobody opens it* -> **Create Link**. The link is
copied to your clipboard automatically. The first person to open it sees that one
account's details in their browser and can copy any field - no Personal Vault install,
no Google account, no sign-in needed on their end - and the link is then dead for
everyone, including if they refresh or reopen it themselves. Manage or immediately kill
an unopened link anytime from the tray menu's **Shared Links...**. If your GitHub
username isn't set yet, **Share...** tells you to set it from Profile first instead of
failing silently.

**Known limitations of this feature specifically** (see also "Known limitations" below):

- **The management list ("Shared Links...") can't always tell the difference between
  "opened" and "still active."** The Apps Script Web App deletes the Drive file the
  moment someone opens the link, but has no way to report that back to the desktop
  app - so an already-viewed link still shows as "Active" there until its normal
  expiration time passes and the next background sweep discovers the file is already
  gone. The link itself is genuinely dead the instant it's opened either way; only the
  list's status label lags behind.
- **The app needs to be running for expired-but-never-opened links to actually get
  cleaned up** - the sweep is a timer inside Personal Vault (every 15 minutes while
  it's running in the tray), not something Google Drive does on its own. This only
  matters for a link nobody ever opened; an opened link is already deleted by Apps
  Script regardless of whether the desktop app is running.
- **The Apps Script deployment and the GitHub Pages viewer page are both under your own
  Google/GitHub accounts** - if you ever revoke the Apps Script deployment or take down
  the Pages site, existing links stop working (fails the same way as an already-viewed
  or expired one, from the recipient's point of view).

## First run

1. Build and run the app. If it finds no vault file yet and a `credentials.json` is
   already present, it first asks whether to sign in to Google Drive and check for an
   **existing backup** before creating anything new - see "Disaster recovery" below.
   Assuming there isn't one (the normal case for a truly first run), it then creates
   its data folder (a `PersonalVault` folder next to the .exe by default - see "Data
   folder location" below) and asks you to choose a master secret (minimum 12
   characters, with a mix of uppercase, lowercase, and at least one symbol - use
   something long and memorable; this is the only thing standing between anyone and
   every password you store). Changing it later (tray menu or Profile ->
   **Change Master Password...**) enforces the same requirement.
2. If a `credentials.json` is present but you weren't signed in yet, the app asks
   right away whether to connect Google Drive - one click, one browser sign-in, done.
3. It opens the (initially empty) account list. Click **Add Account** to add your
   first bank/credit card/mortgage/etc. entry, or **Profile...** first to set your name
   and picture (this becomes the default "Owner" on new accounts).
4. The app minimizes to the tray on close and keeps running; right-click the tray icon
   for Open Vault / Profile / Sync Now / Sign in / Lock Now / Change Secret / Settings /
   Start with Windows / Exit.

## Disaster recovery (new PC, reinstall, or a wiped/replaced drive)

If something happens to this PC, your encrypted vault file (`PersonalVaultData.pvlt`)
is still sitting in the root of your Google Drive's **My Drive** - it's uploaded there
every time you save (add/edit/delete an account, edit your profile, change the master
secret), as long as you were signed in to Google Drive at the time.

**Two things need to survive the PC dying for this to work** - the vault itself (that's
what Drive already takes care of automatically) and a copy of `credentials.json`,
since the app needs that just to sign in to Drive at all - it isn't backed up by the
app anywhere, and a brand-new PC won't have it yet. Unlike the vault, this one is easy
to get back regardless: since you created it yourself in Google Cloud Console, it's
still sitting in your Google account no matter what happens to any PC - go to
console.cloud.google.com → your project → **APIs & Services → Credentials** → click
the OAuth Client ID you made → **Download JSON** any time you need a fresh copy. (This
file only identifies the *app* to Google, not a credential to your data, so it's also
fine to just keep a spare copy off this PC too - e.g. emailed to yourself or on a USB
stick - if you'd rather not depend on remembering the Cloud Console steps under
pressure.) The master secret itself still has to come from your memory either way -
there's no recovery path for that by design.

To get back to business on a different (or freshly reinstalled) PC:

1. Install the app there (see "Sharing this with family members" above for the
   portable-copy option, or build it fresh) and put a `credentials.json` in place -
   re-download it from Google Cloud Console as above if you don't have a spare copy
   handy (either shipped alongside the .exe, or placed per "One-time setup" above).
2. Run it. Since there's no local vault yet, it asks to sign in to Google Drive and
   check for an existing backup **before** offering to create a new, empty one.
3. Sign in with the same Google account the original vault was synced to. If a backup
   is found, it asks to restore it - say yes, then enter the **same master secret**
   the original vault used.
4. That's it - the restored vault is saved locally on this PC too, and syncing
   continues from here exactly as before.
5. Preferences also come back automatically at this point (`LoadOrRestoreSettingsAsync`
   pulls `settings.json` from Drive the moment this PC has no local copy yet) - auto-lock
   minutes, due-date reminder windows, and your **GitHub username** (so Share Account
   links keep working immediately, with nothing to re-type). The one exception is
   "Default browser," which is deliberately left as this new PC's own value rather than
   copied from the old one, since a specific browser's install path only means something
   on the machine it's installed on.

If you say no at any of those prompts (or there's no backup found), the app falls back
to creating a brand-new, empty vault instead - so nothing forces you through recovery
if you genuinely want a fresh start.

## Known limitations (flagged deliberately, not hidden)

- **Builds cleanly on Windows as of the Share Account feature** (`dotnet build`
  succeeds with only pre-existing, unrelated warnings) - this pass also fixed two
  latent compile errors from before (a missing `GoogleDriveSync.RemoteSettingsFileName`
  constant and a missing `AppSettings.SettingsDriveFileId` property, both referenced by
  `TrayApplicationContext` but never defined). It has not yet been exercised
  interactively end-to-end (add an account, sync, share a link, etc.) on a real
  Windows desktop session - see the toast-notification note below for the one area
  most likely to need a build/fix pass of its own.
- Share Account links are a real one-time view once the Apps Script Web App is deployed
  (see "Sharing an account (one-time link)" above) - the desktop app's own "Shared
  Links..." list just can't always tell an already-opened link apart from a still-active
  one until its normal expiration passes.
- The default data folder is now next to the `.exe` rather than a fixed `%AppData%`
  location - moving/copying just the `.exe` without its sibling `PersonalVault` data
  folder leaves the data behind. Use Profile -> **Change Data Folder...** first if you
  need to relocate deliberately (it copies everything and remembers the new location in
  the registry either way), rather than moving the `.exe` and folder separately by hand.
- TOTP (authenticator app) two-factor is deliberately per-machine - the secret is stored
  outside the vault (Windows DPAPI, tied to this PC/Windows account) so it can't be read
  by simply decrypting a stolen vault file. It does **not** travel to a new PC or survive
  a disaster-recovery restore, by design - a backup code (which does travel with the
  vault) is the way back in, after which TOTP can be set up fresh on the new machine.
  Email two-factor requires Drive to stay connected with Gmail's send-only scope granted
  - there's no other zero-cost way to send the code.
- Auto-lock (10 min idle by default, see "How it works" above) covers walking away from
  an unlocked PC, but the secret is still a plain in-memory `string` while unlocked -
  .NET strings are immutable and the GC doesn't scrub freed memory, so this isn't
  hardened against a memory-dump attack. Good enough for a personal machine; not
  something to rely on if the PC itself isn't trusted.
- Drive sync is last-write-wins with no merge or conflict UI - if you edit the vault
  on two machines before syncing between them, the later save overwrites the earlier
  one's changes. (The rolling backup feature gives you a way to recover an overwritten
  version from Drive's revision history, but won't merge changes automatically.)
- Rich toast notifications depend on Windows letting an unpackaged app register an
  AUMID and COM activator - this is known to be finicky and could not be tested from
  this build environment. If it doesn't work on a given PC, notifications silently fall
  back to the same tray balloons used before; nothing breaks either way. One related
  gap: if Windows relaunches the app fresh from a toast click after it was fully
  closed, that specific click may be missed (not an issue for the normal "always
  running in the tray" use case).
- CSV import/export uses this app's own column format, not automatic detection of any
  specific third-party password manager's export - you may need to rename columns to
  match.
- Category-specific fields are suggested placeholders you insert with a button, not
  enforced required fields - nothing stops you from leaving them blank or renaming
  them.

## What's next (ideas, not started)

- Build/fix pass on a real Windows machine, especially around the toast notification
  registration (see "Known limitations").
- Automatic conflict handling for Drive sync (currently last-write-wins).
- Recognizing common export formats from other password managers directly (currently
  you line up columns to this app's own CSV schema).
- A "Restore from backup" UI that lists and restores prior Drive revisions from inside
  the app, instead of via Drive's own web UI.
- Packaging as an MSIX or a proper installer (Start Menu shortcut, uninstaller) instead
  of a portable folder, which would also make toast notification registration more
  reliable.

This is meant as a solid, working v2 - tell me what to build on next (any of the
above, or something else) and we'll keep going.
