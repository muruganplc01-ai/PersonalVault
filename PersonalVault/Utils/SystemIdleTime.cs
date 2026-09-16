using System.Runtime.InteropServices;

namespace PersonalVault.Utils;

/// <summary>
/// Reads how long it's been since the last keyboard/mouse input anywhere on the system -
/// not just in this app's own windows. This is deliberately system-wide: the risk
/// auto-lock defends against is someone walking away from an unlocked PC, which has
/// nothing to do with whether Personal Vault's window happens to have focus.
/// </summary>
public static class SystemIdleTime
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };

        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero; // If we can't read it, assume active rather than risk a false lock.

        // Both values come from the same 32-bit tick counter, which wraps roughly every
        // 49.7 days - subtracting as unsigned ints handles that wraparound correctly
        // (the standard idiom for this Win32 API).
        uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
