using System.Reflection;
using Google.Apis.Drive.v3;
using PersonalVault.Storage;

// Usage: whoami.exe [credentialsFolder] [driveFolderNameToCheck]
//   credentialsFolder      - a folder containing credentials.json + a drive-token/
//                            subfolder (e.g. C:\Personal\PersonalVault, or
//                            C:\Personal\dev-credentials for an older/other account).
//                            Defaults to C:\Personal\PersonalVault.
//   driveFolderNameToCheck - optional: also checks whether this credential set can see
//                            a Drive folder by that exact name, and lists its contents.
//
// This project ended up with multiple Google Drive identities in play (the app's own
// account vs. a separate account used for uploading documents/shares - see this repo's
// project memory on that split). Before assuming "the wrong Drive folder can't be
// reached" is a permissions problem, use this to directly answer: which account does a
// given credentials folder actually sign in as, and what can it actually see? The
// drive.file OAuth scope only reveals files/folders a given OAuth client created (or
// that were explicitly opened with it) - that's a scope limitation independent of which
// Google account is behind the token, and this is the fastest way to tell the two apart
// instead of guessing.
//
// Read-only: never writes, deletes, or uploads anything.

Console.WriteLine("=== Personal Vault - check which Google account a credentials folder signs in as ===");
Console.WriteLine();

string dataFolder = args.Length > 0 ? args[0] : @"C:\Personal\PersonalVault";
Console.WriteLine($"Using credentials folder: {dataFolder}");
AppPaths.SetRootFolder(dataFolder);

var drive = new GoogleDriveSync();
bool signedIn = await drive.SignInAsync(allowInteractive: false);
if (!signedIn)
{
    Console.WriteLine("Could not sign in silently (no cached token, or credentials.json missing in that folder). Press Enter to close.");
    Console.ReadLine();
    return;
}

var serviceField = typeof(GoogleDriveSync).GetField("_service", BindingFlags.NonPublic | BindingFlags.Instance)
    ?? throw new InvalidOperationException("GoogleDriveSync._service field not found.");
var service = (DriveService)serviceField.GetValue(drive)!;

var aboutRequest = service.About.Get();
aboutRequest.Fields = "user";
var about = await aboutRequest.ExecuteAsync();

Console.WriteLine();
Console.WriteLine($"Signed in as: {about.User?.EmailAddress ?? "(unknown)"}");
Console.WriteLine($"Display name: {about.User?.DisplayName ?? "(unknown)"}");

if (args.Length > 1)
{
    string folderName = args[1];
    Console.WriteLine();

    var folderSearch = service.Files.List();
    folderSearch.Q = $"name = '{folderName.Replace("'", "\\'")}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
    folderSearch.Fields = "files(id,name)";
    var folderResult = await folderSearch.ExecuteAsync();

    Console.WriteLine($"'{folderName}' folder visible to this client: {folderResult.Files?.Count ?? 0} match(es).");
    if (folderResult.Files != null)
    {
        foreach (var folder in folderResult.Files)
        {
            Console.WriteLine($"  id={folder.Id}  name={folder.Name}");

            var contentsRequest = service.Files.List();
            contentsRequest.Q = $"'{folder.Id}' in parents and trashed = false";
            contentsRequest.Fields = "files(id,name,size,modifiedTime)";
            contentsRequest.PageSize = 200;
            var contents = await contentsRequest.ExecuteAsync();

            Console.WriteLine($"    {contents.Files?.Count ?? 0} file(s) inside:");
            if (contents.Files != null)
                foreach (var f in contents.Files.OrderBy(f => f.Name))
                    Console.WriteLine($"      {f.Name}  ({f.Size} bytes, modified {f.ModifiedTimeRaw})");
        }
    }
}

Console.WriteLine();
Console.WriteLine("Press Enter to close.");
Console.ReadLine();
