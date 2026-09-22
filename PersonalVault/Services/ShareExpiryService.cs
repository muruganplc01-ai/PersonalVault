using PersonalVault.Models;

namespace PersonalVault.Services;

/// <summary>
/// Periodically scans active "Share Account" links and revokes (deletes the Drive file
/// for) anything past its expiration - the closest thing this feature has to "one-time
/// view" without a real backend to mark a link as consumed atomically (see
/// docs/share/index.html and README.md for the full explanation of that trade-off).
/// Same Timer/Start/CheckNow/Dispose shape as DueDateNotifier, just with an async tick
/// since revoking involves a Drive API call.
/// </summary>
public class ShareExpiryService : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Func<List<SharedLink>> _getShares;
    private readonly Action<List<SharedLink>> _persistShares;
    private readonly Func<SharedLink, Task> _revokeOnDrive;

    public ShareExpiryService(
        Func<List<SharedLink>> getShares,
        Action<List<SharedLink>> persistShares,
        Func<SharedLink, Task> revokeOnDrive)
    {
        _getShares = getShares;
        _persistShares = persistShares;
        _revokeOnDrive = revokeOnDrive;

        // Shares are short-lived (hours to a couple weeks) - 15 minutes is prompt enough
        // to matter without hammering the Drive API while the app just sits in the tray.
        _timer = new System.Windows.Forms.Timer { Interval = (int)TimeSpan.FromMinutes(15).TotalMilliseconds };
        _timer.Tick += async (_, _) => await CheckNowAsync();
    }

    public void Start()
    {
        _ = CheckNowAsync();
        _timer.Start();
    }

    /// <summary>Public so callers (e.g. right after Drive sign-in, or opening "Shared Links...") can force an immediate pass instead of waiting for the next tick.</summary>
    public async Task CheckNowAsync()
    {
        var shares = _getShares();
        var due = shares.Where(s => !s.Revoked && s.ExpiresUtc <= DateTime.UtcNow).ToList();
        if (due.Count == 0) return;

        bool anyChanged = false;
        foreach (var share in due)
        {
            try
            {
                await _revokeOnDrive(share);
                share.Revoked = true;
                anyChanged = true;
            }
            catch
            {
                // Leave it unrevoked and try again on the next tick - most likely cause
                // is not being signed in to Drive right now, not a permanent failure.
            }
        }

        if (anyChanged)
            _persistShares(shares);
    }

    public void Dispose() => _timer.Dispose();
}
