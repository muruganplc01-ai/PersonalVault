# Personal Vault

A Windows system-tray app that stores your account credentials (bank, credit card,
mortgage, insurance, utilities, memberships, car loan, home tax, rental, investments,
etc.) in a single AES-256 encrypted file, keeps that file synced to Google Drive, and
pops a notification when something is coming due.

This is a working first version, built to be extended - see "What's next" below.

## How it works

- **Encryption**: AES-256-GCM (authenticated encryption) with a key derived from your
  master secret via PBKDF2-HMAC-SHA256 (300,000 iterations). `.NET`'s crypto classes
  call into Windows' native CNG/BCrypt library, so this is "Windows AES" under the
  hood. The secret itself is **never stored anywhere** - only its derived key, in
  memory, for as long as the app is running. Lose the secret and the data cannot be
  recovered; there's no backdoor by design.
- **Storage**: everything (account type, institution, username, password, account
  number, due date, notes, arbitrary extra fields, ...) lives as one JSON blob inside
  that encrypted file: `%AppData%\PersonalVault\vault.pvlt`.
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
- **Changing the secret**: tray menu → "Change Master Secret..." re-encrypts the
  *entire* vault file under a brand-new key (and new random salt) in one step, then
  re-uploads it to Drive.

## Project layout

```
PersonalVault.sln
PersonalVault/
  Program.cs                 Entry point (single-instance mutex, starts the tray context)
  Models/AccountEntry.cs     Account record + category/recurrence enums
  Models/VaultData.cs        The full vault (list of accounts)
  Security/VaultCrypto.cs    AES-256-GCM encrypt/decrypt + PBKDF2 key derivation
  Storage/VaultStorage.cs    JSON <-> encrypted bytes <-> vault.pvlt on disk
  Storage/GoogleDriveSync.cs Google Drive OAuth + upload/download
  Storage/AppPaths.cs        All file/folder locations (under %AppData%\PersonalVault)
  Storage/AppSettings.cs     Small non-secret settings (reminder days, cached Drive file id)
  Services/DueDateNotifier.cs   Due-date scanning + tray notifications
  Forms/UnlockForm.cs         Enter/create the master secret
  Forms/MainForm.cs           Account list (add/edit/delete/copy)
  Forms/AccountEditForm.cs    Add/edit a single account
  Forms/ChangeSecretForm.cs   Change the master secret
  Forms/TrayApplicationContext.cs   Owns the tray icon and ties everything together
  Utils/StartupManager.cs     "Start with Windows" via the per-user Run registry key
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
5. Download the resulting JSON, rename it to `credentials.json`, and place it at:
   `%AppData%\PersonalVault\credentials.json`
   (create the `PersonalVault` folder if it doesn't exist yet; you can also just run
   the app once first so it creates the folder for you).
6. Run the app, unlock/create your vault, then use the tray menu → **Sign in to
   Google Drive**. A browser window opens for you to grant access once; after that,
   the app stays signed in (a refresh token is cached locally under
   `%AppData%\PersonalVault\drive-token\`).

If you'd rather not deal with Google Cloud Console at all yet, you can skip this
entirely - the app works fully offline against the local encrypted file, and you can
wire up Drive later.

## First run

1. Build and run the app. It creates `%AppData%\PersonalVault\` and asks you to choose
   a master secret (minimum 8 characters - use something long and memorable; this is
   the only thing standing between anyone and every password you store).
2. It opens the (initially empty) account list. Click **Add Account** to add your
   first bank/credit card/mortgage/etc. entry.
3. Optionally sign in to Google Drive (see above) so the encrypted file also lives
   there.
4. The app minimizes to the tray on close and keeps running; right-click the tray icon
   for Sync Now / Sign in / Change Secret / Start with Windows / Exit.

## Known limitations (v1 - flagged deliberately, not hidden)

- **Not yet build-verified on Windows** - see "Building" above.
- The master secret lives in memory only while the app runs; there's no auto-lock
  timer yet, so if you walk away while it's unlocked, anyone at your PC can open the
  vault window. (Locking Windows itself still protects you, same as any other app.)
- Clipboard copies of usernames/passwords are **not** auto-cleared after a delay yet.
- Drive sync is last-write-wins with no merge or conflict UI - if you edit the vault
  on two machines before syncing between them, the later save overwrites the earlier
  one's changes.
- The tray icon is a placeholder system icon (`SystemIcons.Shield`) - swap in a real
  `.ico` under `Resources\` and wire it into the `.csproj` / `TrayApplicationContext`.
- Notifications are basic tray balloons, not rich Windows 10/11 action-center toasts.
- No search/filter/sort beyond "sorted by due date" in the account list yet.

## What's next (ideas, not started)

- Auto-lock after N minutes idle; require re-entering the secret to unlock again.
- Auto-clear clipboard a few seconds after copying a password.
- Rich toast notifications (via `Microsoft.Toolkit.Uwp.Notifications` or the
  Windows App SDK) with an "Open Vault" action button.
- Keep the last few versions of the vault on Drive (simple rolling backup) instead of
  overwriting in place, in case of accidental deletes or bad syncs.
- Import/export (e.g. from a CSV or another password manager) and full-text search.
- A real custom tray/app icon.
- Category-specific fields (e.g. loan APR/term for CarLoan/Mortgage, premium/renewal
  for Insurance) instead of the generic key=value "extra fields" box.

This is meant as a solid, working starting point - tell me what to build on next
(any of the above, or something else) and we'll keep going.
