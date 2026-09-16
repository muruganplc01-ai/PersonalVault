using System.Text.Json;

namespace PersonalVault.Storage;

/// <summary>
/// Small non-secret preferences, stored as plain (unencrypted) JSON since none of it
/// is sensitive. Do not put account data or the master secret in here.
/// </summary>
public class AppSettings
{
    public bool StartWithWindows { get; set; } = true;

    /// <summary>How many days before a due date to start reminding (0 = the due date itself).</summary>
    public int[] ReminderDaysBefore { get; set; } = { 7, 3, 1, 0 };

    /// <summary>Cached Google Drive file id for the vault, so we don't have to search every time.</summary>
    public string? DriveFileId { get; set; }

    /// <summary>Cached Google Drive file id for the payments file, so we don't have to search every time.</summary>
    public string? PaymentsDriveFileId { get; set; }

    /// <summary>
    /// Minutes of no keyboard/mouse activity anywhere on the system before the vault
    /// auto-locks and the secret has to be re-entered. 0 disables auto-lock.
    /// </summary>
    public int AutoLockMinutes { get; set; } = 10;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                var json = File.ReadAllText(AppPaths.SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // Corrupt or unreadable settings file - fall back to defaults rather than crash.
        }

        return new AppSettings();
    }

    public void Save()
    {
        AppPaths.EnsureFoldersExist();
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.SettingsPath, json);
    }
}
