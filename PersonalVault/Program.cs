using System.Threading;
using System.Windows.Forms;
using PersonalVault.Forms;
using PersonalVault.Storage;
using PersonalVault.Utils;

namespace PersonalVault;

internal static class Program
{
    /// <summary>
    /// Application entry point. Keeps a single instance running via a named mutex,
    /// then hands off to the tray icon / application context for the rest of the
    /// app's lifetime (there is intentionally no "main window" - see TrayApplicationContext).
    /// </summary>
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "PersonalVault-SingleInstance-Mutex", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "Personal Vault is already running. Look for its icon in the system tray.",
                "Personal Vault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // Must happen before anything else touches AppPaths (including
        // TrayApplicationContext's constructor) - applies a data folder chosen earlier
        // via Profile -> "Change Data Folder...", if any. See DataFolderLocation/
        // DataFolderMover for how that choice is made and remembered.
        AppPaths.ApplyOverride(DataFolderLocation.GetOverride());

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
