using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
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
    private const string ApplicationName = "Personal Vault";
    public const string RemoteFileName = "PersonalVaultData.pvlt";

    private DriveService? _service;

    public bool IsAuthenticated => _service != null;
    public bool HasStoredCredentialsFile => File.Exists(AppPaths.CredentialsJsonPath);

    /// <summary>
    /// Signs in to Google Drive. If a valid token is already cached on disk from a
    /// previous sign-in, this completes silently with no UI. Otherwise, when
    /// allowInteractive is true, it opens a browser window for the user to grant access.
    /// Returns false (without throwing) if credentials.json is missing, or if
    /// allowInteractive is false and there's no cached token to use yet.
    /// </summary>
    public async Task<bool> SignInAsync(bool allowInteractive)
    {
        if (!HasStoredCredentialsFile)
            return false;

        if (!allowInteractive && !HasCachedToken())
            return false;

        AppPaths.EnsureFoldersExist();

        using var stream = new FileStream(AppPaths.CredentialsJsonPath, FileMode.Open, FileAccess.Read);
        var clientSecrets = (await GoogleClientSecrets.FromStreamAsync(stream)).Secrets;

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets,
            Scopes,
            "personal-vault-user",
            CancellationToken.None,
            new FileDataStore(AppPaths.TokenStoreFolder, true));

        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName
        });

        return true;
    }

    private static bool HasCachedToken()
    {
        // FileDataStore writes one file per (type, key) pair. We only ever use a single
        // fixed user key ("personal-vault-user"), so "any file present" reliably means
        // "we've signed in before and have something to refresh".
        return Directory.Exists(AppPaths.TokenStoreFolder)
            && Directory.EnumerateFiles(AppPaths.TokenStoreFolder).Any();
    }

    public async Task<string?> FindVaultFileIdAsync()
    {
        RequireAuthenticated();
        var request = _service!.Files.List();
        request.Q = $"name = '{RemoteFileName}' and trashed = false";
        request.Spaces = "drive";
        request.Fields = "files(id, name, modifiedTime)";
        var result = await request.ExecuteAsync();
        return result.Files.Count > 0 ? result.Files[0].Id : null;
    }

    public async Task<DateTime?> GetRemoteModifiedTimeAsync(string fileId)
    {
        RequireAuthenticated();
        var request = _service!.Files.Get(fileId);
        request.Fields = "modifiedTime";
        var file = await request.ExecuteAsync();

        // Read the raw ISO-8601 string rather than a typed DateTime/DateTimeOffset property:
        // the Drive client library has renamed that typed property across versions
        // (ModifiedTime -> ModifiedTimeDateTimeOffset), but the raw string field has stayed
        // stable, so parsing it ourselves avoids a version-specific compile break.
        if (string.IsNullOrEmpty(file.ModifiedTimeRaw))
            return null;

        return DateTime.Parse(
            file.ModifiedTimeRaw,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
    }

    public async Task<byte[]> DownloadAsync(string fileId)
    {
        RequireAuthenticated();
        using var ms = new MemoryStream();
        await _service!.Files.Get(fileId).DownloadAsync(ms);
        return ms.ToArray();
    }

    /// <summary>Creates the remote file if existingFileId is null, otherwise updates it in place. Returns the file id.</summary>
    public async Task<string> UploadOrUpdateAsync(string localFilePath, string? existingFileId)
    {
        RequireAuthenticated();
        using var stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);

        if (!string.IsNullOrEmpty(existingFileId))
        {
            var updateRequest = _service!.Files.Update(new DriveFile(), existingFileId, stream, "application/octet-stream");
            var progress = await updateRequest.UploadAsync();
            if (progress.Status != UploadStatus.Completed)
                throw new IOException("Google Drive upload did not complete: " + progress.Exception?.Message);
            return existingFileId;
        }
        else
        {
            var metadata = new DriveFile { Name = RemoteFileName };
            var createRequest = _service!.Files.Create(metadata, stream, "application/octet-stream");
            createRequest.Fields = "id";
            var progress = await createRequest.UploadAsync();
            if (progress.Status != UploadStatus.Completed)
                throw new IOException("Google Drive upload did not complete: " + progress.Exception?.Message);
            return createRequest.ResponseBody.Id;
        }
    }

    private void RequireAuthenticated()
    {
        if (_service == null)
            throw new InvalidOperationException("Not signed in to Google Drive yet.");
    }
}
