using System.Drawing;
using System.Windows.Forms;

namespace PersonalVault.Forms;

/// <summary>
/// Blocking, hard-to-miss confirmation shown before the local vault is ever replaced by a
/// copy pulled from Google Drive - see TrayApplicationContext.SyncNowAsync. Added after a
/// real incident: a multi-day Google sign-in outage (expired/revoked token) meant local
/// saves kept succeeding locally while Drive silently fell behind; by the time Drive was
/// reconnected, an unattended automatic sync could have treated the stale Drive copy as
/// irrelevant or, worse, the user had no chance to say no before local was overwritten.
///
/// Every local-overwritten-by-remote code path must go through this - including a "silent"
/// background sync - and only proceeds on an explicit Yes. There is no cadence, duplicate
/// call, suppressible flag, or startup check that skips it: replacing the live local vault
/// is always a destructive action that deserves an explicit answer, every single time.
/// </summary>
public class DriveOverwriteWarningForm : Form
{
    public DriveOverwriteWarningForm(string backupPath, string? localSummary, string? remoteSummary)
    {
        Text = "Replace local vault with Google Drive's copy?";
        Width = 580;
        Height = 400;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;

        var warningLabel = new Label
        {
            Text = "⚠ REPLACE YOUR LOCAL VAULT WITH GOOGLE DRIVE'S COPY?",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = Color.Red,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 76,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 10, 16, 0)
        };

        var details = "Google Drive has a copy of your vault that's currently treated as newer " +
            "than what's on this PC. If you click Yes, everything on THIS PC will be REPLACED " +
            "with the Drive version right now.";
        if (!string.IsNullOrEmpty(localSummary)) details += "\n\n" + localSummary;
        if (!string.IsNullOrEmpty(remoteSummary)) details += "\n" + remoteSummary;
        details += "\n\nA safety copy of your CURRENT local vault will be saved first, here:\n" + backupPath;
        details += "\n\nIf you're not sure, click No and use \"Sync Now\" later once you've checked.";

        var bodyLabel = new Label
        {
            Text = details,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 10, 16, 10)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12)
        };
        var noButton = new Button { Text = "No - keep my local vault", AutoSize = true, DialogResult = DialogResult.No };
        var yesButton = new Button
        {
            Text = "Yes - replace with Drive's copy",
            AutoSize = true,
            DialogResult = DialogResult.Yes,
            ForeColor = Color.DarkRed,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        buttonPanel.Controls.Add(noButton);
        buttonPanel.Controls.Add(yesButton);

        Controls.Add(bodyLabel);
        Controls.Add(buttonPanel);
        Controls.Add(warningLabel);

        // Enter/Escape both default to the SAFE choice - the destructive action requires an
        // explicit mouse click on the red button, never a reflexive keypress.
        AcceptButton = noButton;
        CancelButton = noButton;
        ActiveControl = noButton;
    }
}
