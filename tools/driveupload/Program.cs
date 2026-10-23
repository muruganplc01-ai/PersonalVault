using System.Reflection;
using Google.Apis.Drive.v3;
using PersonalVault.Storage;

// Usage: driveupload.exe <localFilePath> <driveFileNameExactMatch> [dataFolder]
//
// Updates one existing Drive file in place with new content from a local file, matched
// by exact name. Refuses to run if zero or more than one file matches, rather than
// guessing. Built for syncing a single updated doc/file after editing it locally.
//
// IMPORTANT LIMITATION: this reuses the main app's own Drive sign-in, which only has
// the narrow "drive.file" OAuth scope - it can only see files the PersonalVault app
// itself created (the vault, payments, settings, dated backups, share files). It
// CANNOT see or update files uploaded through any other Drive connection or account,
// such as a manually-created "Sharing" folder of onboarding docs - those need to be
// updated by hand, or through whatever broader-access Drive connection created them.

Console.WriteLine("=== Personal Vault - update one Drive file in place (drive.file scope only - see limitation note in source) ===");
Console.WriteLine();

if (args.Length < 2)
{
    Console.WriteLine("Usage: driveupload.exe <localFilePath> <driveFileNameExactMatch> [dataFolder]");
    Console.WriteLine("Press Enter to close.");
    Console.ReadLine();
    return;
}

string localPath = args[0];
string driveFileName = args[1];
string dataFolder = args.Length > 2 ? args[2] : @"C:\Personal\PersonalVault";

if (!File.Exists(localPath))
{
    Console.WriteLine($"Local file not found: {localPath}");
    Console.ReadLine();
    return;
}

AppPaths.SetRootFolder(dataFolder);

var drive = new GoogleDriveSync();
bool signedIn = await drive.SignInAsync(allowInteractive: false);
if (!signedIn)
{
    Console.WriteLine("Could not sign in silently (no cached token, or credentials.json missing in that data folder). Press Enter to close.");
    Console.ReadLine();
    return;
}

var serviceField = typeof(GoogleDriveSync).GetField("_service", BindingFlags.NonPublic | BindingFlags.Instance)
    ?? throw new InvalidOperationException("GoogleDriveSync._service field not found.");
var service = (DriveService)serviceField.GetValue(drive)!;

var listRequest = service.Files.List();
listRequest.Q = $"name = '{driveFileName.Replace("'", "\\'")}' and trashed = false";
listRequest.Spaces = "drive";
listRequest.Fields = "files(id,name,modifiedTime)";
var result = await listRequest.ExecuteAsync();

Console.WriteLine($"Found {result.Files?.Count ?? 0} matching file(s) named '{driveFileName}' on Drive (within this app's drive.file access):");
if (result.Files != null)
    foreach (var f in result.Files)
        Console.WriteLine($"  id={f.Id}  modified={f.ModifiedTimeRaw}");

if (result.Files == null || result.Files.Count == 0)
{
    Console.WriteLine("No match - nothing to update. (Remember the drive.file scope limitation above if you expected to find it.)");
    Console.ReadLine();
    return;
}
if (result.Files.Count > 1)
{
    Console.WriteLine("More than one match - refusing to guess which one to update.");
    Console.ReadLine();
    return;
}

string newFileId = await drive.UploadOrUpdateAsync(localPath, result.Files[0].Id, result.Files[0].Name, createBackup: false);
Console.WriteLine($"Updated. File id: {newFileId}");
Console.WriteLine("Press Enter to close.");
Console.ReadLine();
