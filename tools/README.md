# Debug utilities

Small, throwaway-style console tools used to investigate real incidents against this
app's data (a reported data-loss, a stale sync, etc.) without touching the production
app itself. Kept here for future debugging rather than deleted after each use.

**Build order matters**: all three reference `PersonalVault.dll` (and, for the Drive
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

**Limitation**: reuses the main app's own Drive sign-in, which only has the narrow
`drive.file` OAuth scope - it can only see files the PersonalVault app itself created
(vault, payments, settings, dated backups, share files). It cannot see or update files
uploaded through a different Drive connection or account, such as a manually-maintained
onboarding-docs folder - those need a broader-access Drive connection or a manual
upload instead.
