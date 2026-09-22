using System.Text.Json;
using PersonalVault.Models;

namespace PersonalVault.Storage;

/// <summary>
/// Load/save for the local shares.json bookkeeping file - same "tolerant of a missing
/// or corrupt file" shape as AppSettings.Load/Save, just for a list instead of a single
/// POCO. See AppPaths.SharesLocalPath for why this is plain unencrypted JSON.
/// </summary>
public static class SharesStorage
{
    public static List<SharedLink> Load()
    {
        try
        {
            if (File.Exists(AppPaths.SharesLocalPath))
            {
                var json = File.ReadAllText(AppPaths.SharesLocalPath);
                var shares = JsonSerializer.Deserialize<List<SharedLink>>(json);
                if (shares != null) return shares;
            }
        }
        catch
        {
            // Corrupt or unreadable shares file - start fresh rather than crash. Worst
            // case, any Drive files behind previously-tracked shares just age out on
            // their own once their embedded expiry passes (see docs/share/index.html).
        }

        return new List<SharedLink>();
    }

    public static void Save(List<SharedLink> shares)
    {
        AppPaths.EnsureFoldersExist();
        var json = JsonSerializer.Serialize(shares, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.SharesLocalPath, json);
    }
}
