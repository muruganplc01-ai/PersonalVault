using PersonalVault.Storage;

namespace PersonalVault.Utils;

/// <summary>
/// Implements Profile's "Change Data Folder..." - copies every file PersonalVault
/// currently has (vault, payments, settings, shares bookkeeping, credentials.json, the
/// Drive OAuth token cache, anything else sitting in the folder) to a new location,
/// then repoints AppPaths and remembers the choice for next launch (DataFolderLocation).
/// The OLD folder's files are left in place untouched - this is purely additive/
/// non-destructive, so a problem partway through never loses anything; delete the old
/// folder by hand once you've confirmed the new one works.
/// </summary>
public static class DataFolderMover
{
    public static void MoveTo(string newRoot)
    {
        string oldRoot = Path.GetFullPath(AppPaths.RootFolder);
        string resolvedNewRoot = Path.GetFullPath(newRoot);

        if (string.Equals(oldRoot, resolvedNewRoot, StringComparison.OrdinalIgnoreCase))
            return; // Already there - nothing to do.

        if (IsInsideOf(resolvedNewRoot, oldRoot))
            throw new InvalidOperationException("The new folder can't be inside the current data folder.");

        Directory.CreateDirectory(resolvedNewRoot);
        CopyDirectoryContents(oldRoot, resolvedNewRoot);

        AppPaths.SetRootFolder(resolvedNewRoot);
        DataFolderLocation.SetOverride(resolvedNewRoot);
    }

    /// <summary>True if path is potentialAncestor itself or nested somewhere under it - used to refuse copying a folder into its own descendant.</summary>
    private static bool IsInsideOf(string path, string potentialAncestor)
    {
        var ancestor = potentialAncestor.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectoryContents(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;

        foreach (var filePath in Directory.GetFiles(sourceDir))
            File.Copy(filePath, Path.Combine(destDir, Path.GetFileName(filePath)), overwrite: true);

        foreach (var dirPath in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dirPath));
            Directory.CreateDirectory(destSubDir);
            CopyDirectoryContents(dirPath, destSubDir);
        }
    }
}
