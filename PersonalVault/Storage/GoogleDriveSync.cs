using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using PersonalVault.Utils;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace PersonalVault.Storage;

/// <summary>
/// Talks to the Google Drive API using OAuth (installed-app / loopback flow). Uses the
/// narrow "drive.file" scope, which only lets this app see files *it* created - not your
/// whole Drive.
///
/// Requires a credentials.json (OAuth client, "Desktop app" type) from Google Cloud
/// Console to be present at AppPaths.CredentialsJsonPath - see README.md for the
/// one-time setup steps.
/// </summary>
public class GoogleDriveSync
{
    private static readonly string[] Scopes = { DriveService.Scope.DriveFile };

    // Requested only via SignInAsync's includeGmailSendScope, not for every sign-in -
    // someone who only wants Drive backup should never see a "send email on your
    // behalf" consent screen they didn't ask for. Only TrayApplicationContext's
    // Email-MFA code path passes includeGmailSendScope: true.
    private static readonly string[] ScopesWithGmailSend = { DriveService.Scope.DriveFile, "https://www.googleapis.com/auth/gmail.send" };
    private const string ApplicationName = "Personal Vault";
    public const string RemoteFileName = "PersonalVaultData.pvlt";

    /// <summary>Separate Drive file for payment history (see PaymentsStorage) - same account, same drive.file scope, just a different file name.</summary>
    public const string RemotePaymentsFileName = "PersonalVaultPayments.pvlt";

    /// <summary>Separate Drive file for local preferences (see AppSettings) - same account, same drive.file scope, just a different file name.</summary>
    public const string RemoteSettingsFileName = "PersonalVaultSettings.json";

    /// <summary>
    /// Every "Share Account" upload (see ShareCrypto/SharesStorage) gets its own Drive
    /// file, named "{ShareFilePrefix}{a fresh Guid}.bin" - one file per share, so
    /// revoking/expiring one never touches any other active share.
    /// </summary>
    public const string ShareFilePrefix = "PersonalVaultShare-";

    private DriveService? _service;
    private UserCredential? _credential;

    public bool IsAuthenticated => _service != null;

    /// <summary>Exposed so GmailSender can build a GmailService from the exact same authorized credential, without this class needing to reference the Gmail API package itself.</summary>
    public UserCredential? Credential => _credential;
    public bool HasStoredCredentialsFile => File.Exists(AppPaths.CredentialsJsonPath);

    /// <summary>
    /// Signs in to Google Drive. If a valid token is already cached on disk from a
    /// previous sign-in, this completes silently with no UI. Otherwise, when
    /// allowInteractive is true, it opens a browser window for the user to grant access.
    /// Returns false (without throwing) if credentials.json is missing, or if
    /// allowInteractive is false and there's no cached token to use yet.
    ///
    /// includeGmailSendScope additionally requests Gmail's send-only scope, needed only
    /// for Email MFA - pass true only from that code path. GoogleWebAuthorizationBroker
    /// reuses a cached token silently when its stored scopes already cover what's
    /// requested, and only prompts again when they don't (e.g. the first time this is
    /// called with includeGmailSendScope: true for an account that only ever signed in
    /// with the base scope before) - so requesting the base scope elsewhere never shows
    /// a Gmail consent screen to someone who hasn't touched Email MFA.
    /// </summary>
    public async Task<bool> SignInAsync(bool allowInteractive, bool includeGmailSendScope = false)
    {
        DebugLog.Write($"SignInAsync: called with allowInteractive={allowInteractive}. Already authenticated (_service != null)? {IsAuthenticated}");

        if (!HasStoredCredentialsFile)
        {
            DebugLog.Write($"SignInAsync: credentials.json NOT found at '{AppPaths.CredentialsJsonPath}'. Returning false.");
            return false;
        }
        DebugLog.Write($"SignInAsync: credentials.json found at '{AppPaths.CredentialsJsonPath}'.");

        bool hasCachedToken = HasCachedToken();
        DebugLog.Write($"SignInAsync: HasCachedToken() = {hasCachedToken} (checked '{AppPaths.TokenStoreFolder}').");

        if (!allowInteractive && !hasCachedToken)
        {
            DebugLog.Write("SignInAsync: allowInteractive=false and no cached token - returning false without prompting (this is the silent startup attempt, not a real failure).");
            return false;
        }

        AppPaths.EnsureFoldersExist();

        try
        {
            using var stream = new FileStream(AppPaths.CredentialsJsonPath, FileMode.Open, FileAccess.Read);
            var clientSecrets = (await GoogleClientSecrets.FromStreamAsync(stream)).Secrets;
            DebugLog.Write($"SignInAsync: credentials.json parsed OK (ClientId ends with '...{Suffix(clientSecrets.ClientId)}'). Calling GoogleWebAuthorizationBroker.AuthorizeAsync (this is where a browser window should open if a fresh sign-in is needed)...");

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                includeGmailSendScope ? ScopesWithGmailSend : Scopes,
                "personal-vault-user",
                CancellationToken.None,
                new FileDataStore(AppPaths.TokenStoreFolder, true));

            DebugLog.Write("SignInAsync: AuthorizeAsync returned a credential without throwing - sign-in succeeded.");

            _credential = credential;
            _service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName
            });

            DebugLog.Write("SignInAsync: DriveService created. IsAuthenticated is now true. Returning true.");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("SignInAsync", ex);
            throw;
        }
    }

    /// <summary>Last handful of characters only, for logging - enough to tell one client apart from another without writing the whole id to a log file.</summary>
    private static string Suffix(string? value, int length = 6) =>
        string.IsNullOrEmpty(value) ? "(empty)" : value[Math.Max(0, value.Length - length)..];

    private static bool HasCachedToken()
    {
        // FileDataStore writes one file per (type, key) pair. We only ever use a single
        // fixed user key ("personal-vault-user"), so "any file present" reliably means
        // "we've signed in before and have something to refresh".
        return Directory.Exists(AppPaths.TokenStoreFolder)
            && Directory.EnumerateFiles(AppPaths.TokenStoreFolder).Any();
    }

    public Task<string?> FindVaultFileIdAsync() => FindFileIdAsync(RemoteFileName);

    /// <summary>
    /// Generalized version of FindVaultFileIdAsync that works for any remote file name -
    /// used for both the vault (RemoteFileName) and the payments file
    /// (RemotePaymentsFileName), which are separate files on Drive but share this same
    /// lookup-by-name logic.
    /// </summary>
    public async Task<string?> FindFileIdAsync(string remoteFileName)
    {
        RequireAuthenticated();
        DebugLog.Write($"FindFileIdAsync: searching Drive for a file named '{remoteFileName}'...");
        try
        {
            var request = _service!.Files.List();
            request.Q = $"name = '{remoteFileName}' and trashed = false";
            request.Spaces = "drive";
            request.Fields = "files(id, name, modifiedTime)";
            var result = await request.ExecuteAsync();

            DebugLog.Write($"FindFileIdAsync: query for '{remoteFileName}' returned {result.Files.Count} matching file(s).");
            if (result.Files.Count > 0)
            {
                var f = result.Files[0];
                DebugLog.Write($"FindFileIdAsync: using file id '...{Suffix(f.Id)}' (name='{f.Name}').");
                return f.Id;
            }
            DebugLog.Write($"FindFileIdAsync: no existing file named '{remoteFileName}' found on Drive - a fresh upload will create one.");
            return null;
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("FindFileIdAsync", ex);
            throw;
        }
    }

    public async Task<DateTime?> GetRemoteModifiedTimeAsync(string fileId)
    {
        RequireAuthenticated();
        try
        {
            var request = _service!.Files.Get(fileId);
            request.Fields = "modifiedTime";
            var file = await request.ExecuteAsync();

            // Read the raw ISO-8601 string rather than a typed DateTime/DateTimeOffset property:
            // the Drive client library has renamed that typed property across versions
            // (ModifiedTime -> ModifiedTimeDateTimeOffset), but the raw string field has stayed
            // stable, so parsing it ourselves avoids a version-specific compile break.
            if (string.IsNullOrEmpty(file.ModifiedTimeRaw))
            {
                DebugLog.Write("GetRemoteModifiedTimeAsync: Drive returned no modifiedTime for this file - returning null.");
                return null;
            }

            var parsed = DateTime.Parse(
                file.ModifiedTimeRaw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
            DebugLog.Write($"GetRemoteModifiedTimeAsync: remote modifiedTime (UTC) = {parsed:O}.");
            return parsed;
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("GetRemoteModifiedTimeAsync", ex);
            throw;
        }
    }

    public async Task<byte[]> DownloadAsync(string fileId)
    {
        RequireAuthenticated();
        DebugLog.Write($"DownloadAsync: downloading file id '...{Suffix(fileId)}'...");
        try
        {
            using var ms = new MemoryStream();
            await _service!.Files.Get(fileId).DownloadAsync(ms);
            DebugLog.Write($"DownloadAsync: downloaded {ms.Length} byte(s).");
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("DownloadAsync", ex);
            throw;
        }
    }

    /// <summary>
    /// Creates the remote file if existingFileId is null, otherwise updates it in place.
    /// Returns the file id. remoteFileName is only used when creating a brand-new file
    /// (an update keeps whatever name the existing file already has); defaults to
    /// RemoteFileName (the vault) when not specified, so existing callers don't need to
    /// change - pass RemotePaymentsFileName for the payments file instead.
    /// </summary>
    public async Task<string> UploadOrUpdateAsync(string localFilePath, string? existingFileId, string? remoteFileName = null)
    {
        RequireAuthenticated();

        var fileInfo = new FileInfo(localFilePath);
        DebugLog.Write($"UploadOrUpdateAsync: local file '{localFilePath}' exists={fileInfo.Exists}, size={(fileInfo.Exists ? fileInfo.Length : -1)} byte(s). existingFileId={(existingFileId == null ? "(null - will create new)" : "..." + Suffix(existingFileId))}.");

        try
        {
            // Read the whole file up-front into memory rather than holding a FileStream
            // open against it for the entire (possibly several-second) network upload -
            // a held read handle here used to collide with VaultStorage.SaveLocal's
            // File.Replace if a save happened to land while a previous save's
            // fire-and-forget upload was still in flight, crashing with an unhandled
            // IOException ("being used by another process") or UnauthorizedAccessException
            // ("Access to the path is denied") depending on the exact lock mode held.
            // Reading the bytes and closing the file immediately removes the overlap
            // window entirely instead of just narrowing it.
            byte[] fileBytes = await File.ReadAllBytesAsync(localFilePath);
            using var stream = new MemoryStream(fileBytes);

            if (!string.IsNullOrEmpty(existingFileId))
            {
                await BackupBeforeOverwriteAsync(existingFileId);

                DebugLog.Write("UploadOrUpdateAsync: updating existing Drive file...");
                var updateRequest = _service!.Files.Update(new DriveFile(), existingFileId, stream, "application/octet-stream");
                var progress = await updateRequest.UploadAsync();
                DebugLog.Write($"UploadOrUpdateAsync: update finished with status={progress.Status}, bytesSent={progress.BytesSent}.");
                if (progress.Status != UploadStatus.Completed)
                {
                    DebugLog.WriteException("UploadOrUpdateAsync (update)", progress.Exception
                        ?? new IOException($"Upload status was {progress.Status}, not Completed."));
                    throw new IOException("Google Drive upload did not complete: " + progress.Exception?.Message);
                }
                DebugLog.Write("UploadOrUpdateAsync: update succeeded.");
                return existingFileId;
            }
            else
            {
                var newFileName = remoteFileName ?? RemoteFileName;
                DebugLog.Write($"UploadOrUpdateAsync: creating new Drive file named '{newFileName}'...");
                var metadata = new DriveFile { Name = newFileName };
                var createRequest = _service!.Files.Create(metadata, stream, "application/octet-stream");
                createRequest.Fields = "id";
                var progress = await createRequest.UploadAsync();
                DebugLog.Write($"UploadOrUpdateAsync: create finished with status={progress.Status}, bytesSent={progress.BytesSent}.");
                if (progress.Status != UploadStatus.Completed)
                {
                    DebugLog.WriteException("UploadOrUpdateAsync (create)", progress.Exception
                        ?? new IOException($"Upload status was {progress.Status}, not Completed."));
                    throw new IOException("Google Drive upload did not complete: " + progress.Exception?.Message);
                }
                DebugLog.Write($"UploadOrUpdateAsync: create succeeded, new file id '...{Suffix(createRequest.ResponseBody.Id)}'.");
                return createRequest.ResponseBody.Id;
            }
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("UploadOrUpdateAsync", ex);
            throw;
        }
    }

    /// <summary>
    /// Copies the current Drive file to a dated, ordinary filename (e.g.
    /// "PersonalVaultData-backup-2026-10-07-171053.pvlt") before it gets overwritten by
    /// the update that follows. Unlike Drive's internal revision history
    /// (PruneOldRevisionsAsync, reachable only through "Manage versions" - which turned
    /// out hard to find in practice), this shows up as a plain file in Drive's own
    /// listing, nothing to dig for. Best-effort: a failure here must never block the
    /// actual upload it's protecting.
    /// </summary>
    private async Task BackupBeforeOverwriteAsync(string existingFileId)
    {
        try
        {
            var fileRequest = _service!.Files.Get(existingFileId);
            fileRequest.Fields = "name";
            var file = await fileRequest.ExecuteAsync();

            string baseName = string.IsNullOrEmpty(file.Name) ? RemoteFileName : file.Name;
            string nameNoExt = Path.GetFileNameWithoutExtension(baseName);
            string ext = Path.GetExtension(baseName);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            string backupName = $"{nameNoExt}-backup-{stamp}{ext}";

            var copyRequest = _service.Files.Copy(new DriveFile { Name = backupName }, existingFileId);
            await copyRequest.ExecuteAsync();
            DebugLog.Write($"BackupBeforeOverwriteAsync: copied '...{Suffix(existingFileId)}' to dated backup '{backupName}'.");

            await PruneOldDatedBackupsAsync(nameNoExt);
        }
        catch (Exception ex)
        {
            // See doc comment - this is a safety net on top of the real upload, never a
            // reason to block it.
            DebugLog.WriteException("BackupBeforeOverwriteAsync (non-fatal)", ex);
        }
    }

    /// <summary>
    /// Deletes dated backup copies (see BackupBeforeOverwriteAsync) older than 7 days
    /// for the given base file name - same rolling window as PruneOldRevisionsAsync and
    /// the local backup folder (TrayApplicationContext.BackupLocalVaultBeforeOverwrite),
    /// so local history, Drive's visible dated copies, and Drive's internal revision
    /// history all cover the same span.
    /// </summary>
    private async Task PruneOldDatedBackupsAsync(string nameNoExt, int retainDays = 7)
    {
        var listRequest = _service!.Files.List();
        listRequest.Q = $"name contains '{nameNoExt}-backup-' and trashed = false";
        // "createdTime" is the real Drive API v3 field name - CreatedTimeRaw below is
        // just the .NET client library's own property name for that same value
        // (the unparsed ISO 8601 string), not a field the server recognizes; asking the
        // API for "createdTimeRaw" by name made every prune attempt fail with a 400
        // Bad Request ("Invalid field selection"), silently (it's caught as non-fatal),
        // so dated Drive-side backups were never actually being cleaned up.
        listRequest.Fields = "files(id, name, createdTime)";
        var result = await listRequest.ExecuteAsync();

        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-retainDays);
        var toPrune = (result.Files ?? new List<DriveFile>())
            .Where(f => !string.IsNullOrEmpty(f.CreatedTimeRaw))
            .Where(f => DateTimeOffset.TryParse(f.CreatedTimeRaw, out var created) && created < cutoffUtc)
            .ToList();

        DebugLog.Write($"PruneOldDatedBackupsAsync: found {result.Files?.Count ?? 0} dated backup(s) for '{nameNoExt}', pruning {toPrune.Count} older than {retainDays} day(s).");

        foreach (var old in toPrune)
        {
            try { await _service.Files.Delete(old.Id).ExecuteAsync(); }
            catch (Exception ex)
            {
                // best-effort - a failed prune just costs a bit more Drive storage, nothing breaks
                DebugLog.WriteException($"PruneOldDatedBackupsAsync (delete '{old.Name}')", ex);
            }
        }
    }

    /// <summary>
    /// Uploads a brand-new Drive file for the "Share Account" feature - always a
    /// Files.Create (unlike UploadOrUpdateAsync, a share is never updated in place,
    /// only created once and later deleted). Unlike every other upload path in this
    /// class, callers do NOT grant "anyone with the link" access here: the file stays
    /// completely private. It's only ever read by the vault owner's own Google Apps
    /// Script Web App (docs/share/AppsScript/Code.gs, run "as me" regardless of who
    /// calls it), which serves it exactly once and deletes it in the same request -
    /// that's what makes a real one-time view possible without the recipient ever
    /// needing a Google account or any Drive access of their own. See README.md's
    /// "Sharing an account (one-time link)" section for the full setup.
    /// </summary>
    public async Task<string> UploadShareAsync(byte[] blob, string fileName)
    {
        RequireAuthenticated();
        DebugLog.Write($"UploadShareAsync: creating new share file '{fileName}', {blob.Length} byte(s)...");

        try
        {
            using var stream = new MemoryStream(blob);
            var metadata = new DriveFile { Name = fileName };
            var createRequest = _service!.Files.Create(metadata, stream, "application/octet-stream");
            createRequest.Fields = "id";
            var progress = await createRequest.UploadAsync();
            if (progress.Status != UploadStatus.Completed)
            {
                DebugLog.WriteException("UploadShareAsync (create)", progress.Exception
                    ?? new IOException($"Upload status was {progress.Status}, not Completed."));
                throw new IOException("Google Drive upload did not complete: " + progress.Exception?.Message);
            }

            string fileId = createRequest.ResponseBody.Id;
            DebugLog.Write($"UploadShareAsync: create succeeded, new file id '...{Suffix(fileId)}'. Left private - only the Apps Script Web App can read it.");
            return fileId;
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("UploadShareAsync", ex);
            throw;
        }
    }

    /// <summary>
    /// Deletes a share's Drive file outright (rather than just removing its "anyone"
    /// permission), so nothing about it lingers on Drive at all once revoked/expired.
    /// Best-effort: called from ShareExpiryService's background sweep, where a file
    /// that's already gone (e.g. revoked twice, or deleted by hand from Drive's own UI)
    /// should count as a harmless no-op, not an error worth surfacing.
    /// </summary>
    public async Task RevokeShareAsync(string fileId)
    {
        RequireAuthenticated();
        DebugLog.Write($"RevokeShareAsync: deleting share file id '...{Suffix(fileId)}'...");
        try
        {
            await _service!.Files.Delete(fileId).ExecuteAsync();
            DebugLog.Write("RevokeShareAsync: delete succeeded.");
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            DebugLog.Write("RevokeShareAsync: file was already gone (404) - treating as already revoked.");
        }
    }

    /// <summary>
    /// Keeps a rolling <paramref name="retainDays"/> days of the vault file's revisions
    /// on Drive instead of only the current one, so an accidental delete, a bad save, or
    /// a botched sync has something to recover from. Time-based rather than count-based
    /// on purpose - a burst of saves in one day (e.g. several app restarts) shouldn't
    /// crowd out the one revision from three days ago that might actually be the one you
    /// need, the way a fixed "keep the last N" count could. Uses Drive's own revision
    /// history (every update to the file already creates one) - this just pins the
    /// newest one so it isn't silently aged out by Drive's default retention, then prunes
    /// anything older than <paramref name="retainDays"/> days. Best-effort: any failure
    /// here is swallowed rather than treated as a sync failure, since losing a rotation
    /// cycle is harmless.
    /// </summary>
    public async Task PruneOldRevisionsAsync(string fileId, int retainDays = 7)
    {
        RequireAuthenticated();
        DebugLog.Write($"PruneOldRevisionsAsync: starting for file id '...{Suffix(fileId)}', retainDays={retainDays}.");
        try
        {
            var fileRequest = _service!.Files.Get(fileId);
            fileRequest.Fields = "headRevisionId";
            var file = await fileRequest.ExecuteAsync();

            if (!string.IsNullOrEmpty(file.HeadRevisionId))
            {
                try
                {
                    var pin = _service.Revisions.Update(
                        new Google.Apis.Drive.v3.Data.Revision { KeepForever = true },
                        fileId, file.HeadRevisionId);
                    await pin.ExecuteAsync();
                    DebugLog.Write($"PruneOldRevisionsAsync: pinned head revision '...{Suffix(file.HeadRevisionId)}' with KeepForever.");
                }
                catch (Exception ex)
                {
                    // Not fatal - worst case this revision ages out under Drive's own
                    // default retention instead of ours.
                    DebugLog.WriteException("PruneOldRevisionsAsync (pin head revision - non-fatal)", ex);
                }
            }

            var listRequest = _service.Revisions.List(fileId);
            listRequest.Fields = "revisions(id, modifiedTime)";
            var revisions = await listRequest.ExecuteAsync();
            DebugLog.Write($"PruneOldRevisionsAsync: Drive reports {revisions.Revisions?.Count ?? 0} total revision(s).");

            // Parsed from ModifiedTimeRaw (the API's raw RFC3339 string) rather than
            // trusting the client library's own typed date property - see
            // GetRemoteModifiedTimeAsync above for why we've been burned by those before.
            // Parsing the raw string ourselves with DateTimeOffset.Parse is safe; it's
            // the library's own pre-parsed property that's been the problem historically.
            var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-retainDays);
            var toPrune = revisions.Revisions
                .Where(r => !string.IsNullOrEmpty(r.ModifiedTimeRaw))
                .Where(r => DateTimeOffset.TryParse(r.ModifiedTimeRaw, out var modified) && modified < cutoffUtc)
                .ToList();
            DebugLog.Write($"PruneOldRevisionsAsync: pruning {toPrune.Count} revision(s) older than {retainDays} day(s) (cutoff {cutoffUtc:O}).");

            foreach (var old in toPrune)
            {
                try { await _service.Revisions.Delete(fileId, old.Id).ExecuteAsync(); }
                catch (Exception ex)
                {
                    // best-effort - a failed prune just costs a bit more Drive storage, nothing breaks
                    DebugLog.WriteException($"PruneOldRevisionsAsync (delete revision '...{Suffix(old.Id)}' - non-fatal)", ex);
                }
            }

            DebugLog.Write("PruneOldRevisionsAsync: finished.");
        }
        catch (Exception ex)
        {
            // Backup rotation is a nice-to-have on top of a successful sync, never a
            // reason to make the sync itself look like it failed.
            DebugLog.WriteException("PruneOldRevisionsAsync (non-fatal, sync itself already succeeded)", ex);
        }
    }

    private void RequireAuthenticated()
    {
        if (_service == null)
        {
            DebugLog.Write("RequireAuthenticated: _service is null - throwing InvalidOperationException. (This means IsAuthenticated is false - a Drive call was attempted before/without a successful SignInAsync.)");
            throw new InvalidOperationException("Not signed in to Google Drive yet.");
        }
    }
}
