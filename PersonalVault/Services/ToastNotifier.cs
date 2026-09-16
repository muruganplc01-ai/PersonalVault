using System.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using PersonalVault.Utils;

namespace PersonalVault.Services;

/// <summary>
/// Thin, defensive wrapper around Microsoft.Toolkit.Uwp.Notifications for rich Windows
/// action-center toasts (with an "Open Vault" button), used instead of the plain tray
/// balloon tip when possible.
///
/// AUMID/COM-activator registration for an unpackaged (non-MSIX) WinForms app is known
/// to be finicky - it depends on registry write access and Windows version quirks that
/// can't be verified from this sandbox. So every entry point here is wrapped in
/// try/catch and reports success/failure back to the caller; TrayApplicationContext
/// always falls back to the existing NotifyIcon.ShowBalloonTip(...) call whenever this
/// returns false, so a problem here degrades gracefully instead of breaking
/// notifications altogether.
///
/// Known limitation: this only handles a toast click while the app is already running
/// (the normal case for an always-on tray app). If Windows relaunches the app fresh
/// from a toast click after it was closed, OnActivated may fire before TrayApplicationContext
/// has subscribed to OpenVaultRequested and that first click would be missed - not
/// handled here, since this app is designed to always be running via "Start with
/// Windows".
/// </summary>
public static class ToastNotifier
{
    // Fixed app id used to register with Windows' notification system. Do not change
    // this after release - it's part of how Windows associates cached toast
    // registrations with this app.
    private const string AppId = "PersonalVault.App";

    private static bool _registered;
    private static bool _registrationFailed;

    /// <summary>Captured on the UI thread during Initialize() so ToastActivator can marshal clicks back onto it.</summary>
    internal static SynchronizationContext? UiContext { get; private set; }

    /// <summary>Raised (on the UI thread) when the user clicks the "Open Vault" button on a toast.</summary>
    public static event Action? OpenVaultRequested;

    /// <summary>
    /// Registers this app with Windows' notification system. Call once, early, on the
    /// UI thread (TrayApplicationContext's constructor). Safe to call more than once;
    /// safe to skip calling - TryShow will call it lazily if needed.
    /// </summary>
    public static void Initialize()
    {
        if (_registered || _registrationFailed) return;
        DebugLog.Write("ToastNotifier.Initialize: attempting AUMID/COM-activator registration...");

        try
        {
            if (SynchronizationContext.Current == null)
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Forms.WindowsFormsSynchronizationContext());
            UiContext = SynchronizationContext.Current;

            DesktopNotificationManagerCompat.RegisterAumidAndComServer<ToastActivator>(AppId);
            DesktopNotificationManagerCompat.RegisterActivator<ToastActivator>();

            _registered = true;
            DebugLog.Write("ToastNotifier.Initialize: registration succeeded.");
        }
        catch (Exception ex)
        {
            // Most likely cause: couldn't write the registry keys this needs, or this
            // Windows version's toast plumbing isn't happy with an unpackaged app.
            // Either way, every future call just uses the balloon-tip fallback instead.
            _registrationFailed = true;
            DebugLog.WriteException("ToastNotifier.Initialize (falling back to balloon tips from now on)", ex);
        }
    }

    /// <summary>
    /// Shows a rich toast with an "Open Vault" button. Returns true if the toast was
    /// (as far as we can tell) successfully raised; returns false on any failure, in
    /// which case the caller should fall back to ShowBalloonTip.
    /// </summary>
    public static bool TryShow(string title, string message)
    {
        if (_registrationFailed)
        {
            DebugLog.Write($"ToastNotifier.TryShow: registration previously failed - skipping toast for '{title}', caller will use balloon tip.");
            return false;
        }
        if (!_registered) Initialize();
        if (_registrationFailed) return false;

        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .AddButton(new ToastButton()
                    .SetContent("Open Vault")
                    .AddArgument("action", "open")
                    .SetBackgroundActivation())
                .Show();
            DebugLog.Write($"ToastNotifier.TryShow: toast raised OK for '{title}' - '{message}'.");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.WriteException($"ToastNotifier.TryShow ('{title}' - falling back to balloon tip)", ex);
            return false;
        }
    }

    /// <summary>Called by ToastActivator (already marshaled onto the UI thread) when any toast from this app is clicked.</summary>
    internal static void HandleActivation(string arguments)
    {
        try
        {
            var args = ToastArguments.Parse(arguments);
            if (args.Contains("action") && args["action"] == "open")
                OpenVaultRequested?.Invoke();
        }
        catch
        {
            // Malformed/unknown argument string - nothing sensible to do but ignore it.
        }
    }

    /// <summary>Call on app exit so Windows doesn't keep stale toasts around after the app is closed.</summary>
    public static void ClearAll()
    {
        try { ToastNotificationManagerCompat.History.Clear(); }
        catch { /* best-effort cleanup only */ }
    }
}
