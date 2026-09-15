using System.Windows.Forms;
using Microsoft.Win32;

namespace PersonalVault.Utils;

/// <summary>
/// Registers/unregisters this app to launch automatically when the current Windows
/// user logs in, via the standard per-user Run registry key (no admin rights needed).
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PersonalVault";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
