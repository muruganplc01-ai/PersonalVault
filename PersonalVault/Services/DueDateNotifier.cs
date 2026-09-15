using PersonalVault.Models;

namespace PersonalVault.Services;

/// <summary>
/// Periodically scans the vault for upcoming/overdue due dates and raises a
/// notification callback once per account per calendar day. Runs on a WinForms Timer,
/// so all callbacks happen on the UI thread - no extra marshaling needed.
/// </summary>
public class DueDateNotifier : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Func<VaultData?> _getVault;
    private readonly Action<string, string> _notify; // (title, message)
    private readonly Action _persistNotifiedState;
    private readonly int[] _reminderDaysBefore;

    public DueDateNotifier(
        Func<VaultData?> getVault,
        Action<string, string> notify,
        Action persistNotifiedState,
        int[] reminderDaysBefore)
    {
        _getVault = getVault;
        _notify = notify;
        _persistNotifiedState = persistNotifiedState;
        _reminderDaysBefore = reminderDaysBefore;

        // Checking every 30 minutes is plenty for date-level (not time-level) reminders,
        // and cheap since this is just an in-memory scan.
        _timer = new System.Windows.Forms.Timer { Interval = (int)TimeSpan.FromMinutes(30).TotalMilliseconds };
        _timer.Tick += (_, _) => CheckNow();
    }

    public void Start()
    {
        CheckNow();
        _timer.Start();
    }

    public void CheckNow()
    {
        var vault = _getVault();
        if (vault == null) return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        bool anyChanged = false;

        foreach (var account in vault.Accounts)
        {
            if (account.DueDate == null) continue;

            var dueDate = DateOnly.FromDateTime(account.DueDate.Value);
            int daysUntilDue = dueDate.DayNumber - today.DayNumber;

            bool isReminderDay = daysUntilDue >= 0 && _reminderDaysBefore.Contains(daysUntilDue);
            bool isOverdue = daysUntilDue < 0;

            if (!(isReminderDay || isOverdue) || account.LastNotifiedOn == today)
                continue;

            string when = daysUntilDue switch
            {
                0 => "is due today",
                > 0 => $"is due in {daysUntilDue} day(s) ({dueDate:MMM d, yyyy})",
                _ => $"was due {-daysUntilDue} day(s) ago ({dueDate:MMM d, yyyy})"
            };

            _notify($"{account.Category}: {account.Name}", $"{account.Name} {when}.");
            account.LastNotifiedOn = today;
            anyChanged = true;

            // Once an overdue recurring bill has actually been flagged, roll its due date
            // forward so the next cycle's reminders start fresh instead of nagging forever
            // about the same past date.
            if (isOverdue && account.Recurrence != RecurrenceType.None)
            {
                account.DueDate = Advance(account.DueDate.Value, account.Recurrence);
                account.LastNotifiedOn = null;
            }
        }

        if (anyChanged)
            _persistNotifiedState();
    }

    private static DateTime Advance(DateTime date, RecurrenceType recurrence) => recurrence switch
    {
        RecurrenceType.Weekly => date.AddDays(7),
        RecurrenceType.Monthly => date.AddMonths(1),
        RecurrenceType.Quarterly => date.AddMonths(3),
        RecurrenceType.Yearly => date.AddYears(1),
        _ => date
    };

    public void Dispose() => _timer.Dispose();
}
