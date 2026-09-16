using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;

namespace PersonalVault.Services;

/// <summary>
/// COM activation callback Windows invokes when the user clicks a toast notification
/// (or a button on one) that this app raised. Required boilerplate for unpackaged
/// WinForms apps using Microsoft.Toolkit.Uwp.Notifications - see ToastNotifier for the
/// friendly wrapper around this.
///
/// The Guid below is fixed and must stay the same across builds/versions of this app:
/// Windows uses it (together with the AUMID in ToastNotifier) to route a clicked toast
/// back to this class, including relaunching the app if it's not currently running.
/// Do not regenerate it.
/// </summary>
[ClassInterface(ClassInterfaceType.None)]
[ComSourceInterfaces(typeof(NotificationActivator.INotificationActivationCallback))]
[Guid("52E6AA84-5C7B-4DEE-8ACB-1FCD55423AAB"), ComVisible(true)]
public class ToastActivator : NotificationActivator
{
    public override void OnActivated(string arguments, NotificationUserInput userInput, string appUserModelId)
    {
        // This can run on a background COM thread, possibly before the WinForms message
        // loop is even pumping (e.g. if Windows is relaunching the app from a toast
        // click). Hop back onto the UI thread via the context ToastNotifier captured at
        // startup; if that hasn't happened yet, just run inline rather than lose the
        // click entirely.
        var uiContext = ToastNotifier.UiContext;
        if (uiContext != null)
            uiContext.Post(_ => ToastNotifier.HandleActivation(arguments), null);
        else
            ToastNotifier.HandleActivation(arguments);
    }
}
