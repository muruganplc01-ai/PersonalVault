# Debug utilities

Small, throwaway-style console tools used to investigate real incidents against this
app's data (a reported data-loss, a stale sync, etc.) without touching the production
app itself. Kept here for future debugging rather than deleted after each use.

**Build order matters**: all four reference `PersonalVault.dll` (and, for the Drive
tools, `Google.Apis.*.dll`) via `HintPath` pointing at `C:\Personal\build\` - the main
app's own `OutputPath` (see `PersonalVault/PersonalVault.csproj`). Build the main solution
at least once first, then build whichever tool you need:

```
dotnet build PersonalVault.sln -c Release
dotnet build tools/comparevaults -c Release
```

## comparevaults

Decrypts and compares two or more vault snapshots (the live `vault.pvlt`, a dated local
backup from `backup/`, or a downloaded Drive revision all work) and reports accounts
added/removed/modified between each consecutive pair.

```
comparevaults.exe "Before=C:\Personal\PersonalVault\backup\vault-2026-10-22-080211.pvlt" "After=C:\Personal\PersonalVault\vault.pvlt"
```

Prompts for the master secret directly in its own console window - never pass it as an
argument or environment variable, and nothing else in the app or any automation sees it.
Writes a safe summary (names, categories, institutions, dates only - never passwords,
usernames, account numbers, or notes) to `comparison-result.txt` next to the exe.

## drivehistory

Lists every revision Google Drive has retained for one synced file, with its timestamp
and byte size - metadata only, no secret needed, can't decrypt anything. Useful for
answering "was anything actually uploaded in this window" without trusting local file
timestamps, which can lag behind Drive's own clock by a few seconds.

```
drivehistory.exe
drivehistory.exe "C:\Personal\PersonalVault" "PersonalVaultPayments.pvlt"
```

## driveupload

Updates one existing Drive file in place from a local file, matched by exact name.
Refuses to run if zero or more than one file matches.

```
driveupload.exe "F:\GitHub\PersonalVault\PersonalVault\Instructions\PersonalVault-Share-Setup-Guide.html" "PersonalVaultData-backup-2026-10-23.pvlt"
```

**Limitation**: reuses whichever credentials folder it's pointed at (the main app's own
by default). What that credential set can actually see on Drive depends on both the
Google account behind it and the OAuth client's own scope - don't assume either way;
use `whoami` below to check directly instead of guessing.

## whoami

Shows which Google account a given credentials folder (`credentials.json` + a
`drive-token/` subfolder) actually signs in as, and optionally checks whether that
credential set can see a named Drive folder and lists its contents. Read-only - never
writes, deletes, or uploads anything.

```
whoami.exe
whoami.exe "C:\Personal\dev-credentials"
whoami.exe "C:\Personal\dev-credentials" "Sharing"
```

This project ended up with more than one Google Drive identity in play (see this repo's
project memory on the account split, if one exists in your session). Before assuming a
Drive folder is unreachable, check directly which account a credentials folder is
actually signed in as and what it can see, rather than guessing from the OAuth scope
alone - `drive.file` is a scope limitation (what the OAuth *client* created), not simply
an account-identity one, and in practice an older/different credentials folder can
sometimes see more than expected.
