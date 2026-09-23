using Microsoft.Win32;

namespace PersonalVault.Utils;

/// <summary>
/// Remembers a custom data folder (chosen from Profile -> "Change Data Folder...") in
/// the registry, mirroring StartupManager's registry usage - deliberately NOT inside
/// the data folder itself, since the app needs to know where to look before it knows
/// where the data folder is. No value present (the normal case) means "use the
/// default": AppPaths' own app-folder-relative formula.
/// </summary>
public static class DataFolderLocation
{
    private const string KeyPath = @"Software\PersonalVault";
    private const string ValueName = "DataFolder";

    public static string? GetOverride()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        return key?.GetValue(ValueName) as string;
    }

    public static void SetOverride(string folder)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, folder);
    }
}
