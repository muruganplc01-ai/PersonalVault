using System.Reflection;
using Google.Apis.Drive.v3;
using PersonalVault.Storage;

// Usage: drivehistory.exe [dataFolder] [remoteFileName]
//   dataFolder     - defaults to C:\Personal\PersonalVault (where credentials.json and
//                    the cached drive-token live for a silent, non-interactive sign-in)
//   remoteFileName - defaults to GoogleDriveSync.RemoteFileName (the main vault file);
//                    pass "PersonalVaultPayments.pvlt" or "PersonalVaultSettings.json"
//                    to check one of the other synced files instead
//
// Lists every revision Drive has kept for one file, with its timestamp and byte size -
// useful for answering "was anything actually uploaded in this time window" without
// guessing from local file timestamps, which can lag behind Drive's own clock. Metadata
// only: no secret needed, and nothing here can decrypt or view account content.

Console.WriteLine("=== Personal Vault - Drive revision history (metadata only, no decryption, no secret needed) ===");
Console.WriteLine();

string dataFolder = args.Length > 0 ? args[0] : @"C:\Personal\PersonalVault";
AppPaths.SetRootFolder(dataFolder);

var drive = new GoogleDriveSync();
bool signedIn = await drive.SignInAsync(allowInteractive: false);
if (!signedIn)
{
    Console.WriteLine("Could not sign in silently (no cached token, or credentials.json missing in that data folder). Press Enter to close.");
    Console.ReadLine();
    return;
}

string remoteFileName = args.Length > 1 ? args[1] : GoogleDriveSync.RemoteFileName;
string? fileId = await drive.FindFileIdAsync(remoteFileName);
if (fileId == null)
{
    Console.WriteLine($"Could not find a file named '{remoteFileName}' on Drive. Press Enter to close.");
    Console.ReadLine();
    return;
}

// GoogleDriveSync doesn't expose its DriveService publicly (by design - everything
// callers need goes through its own methods), so this reads the private field via
// reflection rather than widening that class's public surface just for a diagnostic.
var serviceField = typeof(GoogleDriveSync).GetField("_service", BindingFlags.NonPublic | BindingFlags.Instance)
    ?? throw new InvalidOperationException("GoogleDriveSync._service field not found - internals must have changed.");
var service = (DriveService)serviceField.GetValue(drive)!;

var listRequest = service.Revisions.List(fileId);
listRequest.Fields = "revisions(id,modifiedTime,size)";
var revisions = await listRequest.ExecuteAsync();

var lines = new List<string> { $"Drive revision history for '{remoteFileName}' (id ...{fileId[^Math.Min(6, fileId.Length)..]}):", "" };
if (revisions.Revisions != null)
{
    foreach (var rev in revisions.Revisions.OrderBy(r => r.ModifiedTimeDateTimeOffset))
    {
        var localTime = rev.ModifiedTimeDateTimeOffset?.ToLocalTime();
        lines.Add($"  {localTime:yyyy-MM-dd HH:mm:ss} local  -  {rev.Size ?? 0} byte(s)  -  revision id ...{rev.Id?[^Math.Min(8, rev.Id.Length)..]}");
    }
}
lines.Add("");
lines.Add($"Total: {revisions.Revisions?.Count ?? 0} revision(s) currently retained (Drive only keeps the last 7 days by default - see PruneOldRevisionsAsync).");

string outPath = Path.Combine(AppContext.BaseDirectory, "drive-revision-history.txt");
File.WriteAllLines(outPath, lines);

Console.WriteLine();
foreach (var l in lines) Console.WriteLine(l);
Console.WriteLine();
Console.WriteLine($"Saved to: {outPath}");
Console.WriteLine("Press Enter to close.");
Console.ReadLine();
