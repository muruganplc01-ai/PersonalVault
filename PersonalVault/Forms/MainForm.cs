using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using PersonalVault.Models;
using PersonalVault.Utils;

namespace PersonalVault.Forms;

/// <summary>
/// The main account list window. This is a normal Form, but TrayApplicationContext
/// intercepts its close button and hides it instead of closing it - the app itself
/// only ever exits via the tray icon's "Exit" menu item.
/// </summary>
public class MainForm : Form
{
    private VaultData _vault;
    private readonly Action _save;
    private readonly Func<Task> _signInToDrive;
    private readonly Func<IEnumerable<PaymentRecord>> _getPayments;
    private readonly Action<AccountEntry, decimal, DateTime> _markPaid;
    private readonly Action<AccountEntry, decimal, DateTime, int, RecurrenceType> _backfillPayments;
    private readonly Func<string?> _getDefaultBrowserPath;
    private readonly Action<string?> _setDefaultBrowserPath;
    private readonly Func<string?> _getGitHubUsername;
    private readonly Action<string?> _setGitHubUsername;
    private readonly Action _changeMasterSecret;
    private readonly Func<string> _getDataFolder;
    private readonly Action<string> _changeDataFolder;
    private readonly Func<AccountEntry, TimeSpan, Task<string>> _shareAccount;
    private readonly ListView _listView;
    private readonly TextBox _searchBox;
    private readonly ComboBox _categoryFilter;
    private readonly ComboBox _ownerFilter;
    private readonly Label _driveStatusLabel;
    private readonly Button _driveSignInButton;

    // --- Dues tab ---
    private readonly ListView _duesListView;
    private readonly RadioButton _pastDueRadio;
    private readonly RadioButton _dueTodayRadio;
    private readonly RadioButton _dueInWeekRadio;
    private readonly Button _markPaidButton;
    private readonly Label _duesTotalLabel;

    // --- Overview tab ---
    private readonly ListView _balancesListView;
    private readonly Label _totalOnHandLabel;
    private readonly Label _totalDuesLabel;
    private readonly Label _netLabel;

    private const string AllCategoriesLabel = "All Categories";
    private const string AllOwnersLabel = "All Owners";

    // Grid coloring - header stands out from the white body, alternating rows make
    // long lists easier to scan, and the selected row uses its own color rather than
    // relying on the OS default (which fades to gray whenever the window loses focus).
    private static readonly Color GridHeaderBackColor = Color.FromArgb(51, 65, 85);
    private static readonly Color GridHeaderForeColor = Color.White;
    private static readonly Color GridRowBackColor = Color.White;
    private static readonly Color GridAltRowBackColor = Color.FromArgb(235, 241, 247);
    private static readonly Color GridSelectedBackColor = Color.FromArgb(255, 202, 58);
    private static readonly Color GridSelectedForeColor = Color.Black;

    // Guards against ApplyFilter re-entering itself while it's rebuilding the Owner
    // dropdown's item list (which raises SelectedIndexChanged on its own).
    private bool _updatingFilters;

    // Set after a Copy Username/Password click and cleared once the timer fires - lets
    // the clear check "is the clipboard still holding what I put there" before wiping
    // it, so we never stomp on something else the user copied in the meantime.
    private System.Windows.Forms.Timer? _clipboardClearTimer;

    /// <summary>
    /// isDriveConnected/signInToDrive exist so this window can show, in plain sight
    /// rather than buried in the tray menu, whether anything you do here is actually
    /// getting backed up - "no hidden stuff" is the whole point. signInToDrive is
    /// TrayApplicationContext.SignInToDriveAsync, passed in so this form doesn't need
    /// its own copy of the Drive sign-in logic.
    /// </summary>
    public MainForm(
        VaultData vault,
        Action save,
        bool isDriveConnected,
        Func<Task> signInToDrive,
        string vaultFilePath,
        Func<IEnumerable<PaymentRecord>> getPayments,
        Action<AccountEntry, decimal, DateTime> markPaid,
        Action<AccountEntry, decimal, DateTime, int, RecurrenceType> backfillPayments,
        Func<string?> getDefaultBrowserPath,
        Action<string?> setDefaultBrowserPath,
        Func<string?> getGitHubUsername,
        Action<string?> setGitHubUsername,
        Action changeMasterSecret,
        Func<string> getDataFolder,
        Action<string> changeDataFolder,
        Func<AccountEntry, TimeSpan, Task<string>> shareAccount)
    {
        _vault = vault;
        _save = save;
        _signInToDrive = signInToDrive;
        _getPayments = getPayments;
        _markPaid = markPaid;
        _backfillPayments = backfillPayments;
        _getDefaultBrowserPath = getDefaultBrowserPath;
        _setDefaultBrowserPath = setDefaultBrowserPath;
        _getGitHubUsername = getGitHubUsername;
        _setGitHubUsername = setGitHubUsername;
        _changeMasterSecret = changeMasterSecret;
        _getDataFolder = getDataFolder;
        _changeDataFolder = changeDataFolder;
        _shareAccount = shareAccount;

        Text = "Personal Vault";
        Width = 960;
        Height = 645;
        StartPosition = FormStartPosition.CenterScreen;

        // Minimizing should behave the same as clicking the close button - drop to the
        // tray icon instead of sitting on the taskbar as a minimized window. Flip
        // WindowState back to Normal before hiding so that re-opening from the tray
        // (ShowMainForm) shows a normal-sized window rather than a minimized one.
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
                Hide();
            }
        };

        // One TableLayoutPanel for the whole top area (Drive status row, then search
        // row) instead of two separate Dock=Top siblings - WinForms' dock ordering
        // between same-dock-style siblings is easy to get backwards, so a single
        // container with its own top-to-bottom rows sidesteps that entirely.
        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true
        };
        topPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        topPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        topPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var driveRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 6, 8, 2)
        };
        _driveStatusLabel = new Label { AutoSize = true, Padding = new Padding(0, 6, 12, 0) };
        _driveSignInButton = new Button { Text = "Sign in to Google Drive", AutoSize = true };
        _driveSignInButton.Click += async (_, _) => await OnSignInClicked();
        driveRow.Controls.Add(_driveStatusLabel);
        driveRow.Controls.Add(_driveSignInButton);

        // Vault file location, right here on the main window instead of buried in
        // Settings - "no hidden stuff" means you shouldn't have to go looking for it.
        var vaultRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 0, 8, 4)
        };
        var vaultLabel = new Label
        {
            AutoSize = true,
            Padding = new Padding(0, 4, 12, 0),
            ForeColor = Color.DimGray,
            Text = "Vault file: " + vaultFilePath
        };
        var vaultOpenFolderButton = new Button { Text = "Open Folder", AutoSize = true };
        vaultOpenFolderButton.Click += (_, _) => OpenContainingFolder(vaultFilePath);
        vaultRow.Controls.Add(vaultLabel);
        vaultRow.Controls.Add(vaultOpenFolderButton);

        var searchRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 2, 8, 6)
        };
        var searchLabel = new Label { Text = "Search:", AutoSize = true, Padding = new Padding(0, 6, 6, 0) };
        _searchBox = new TextBox { Width = 260, Margin = new Padding(0, 3, 16, 0) };
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        searchRow.Controls.Add(searchLabel);
        searchRow.Controls.Add(_searchBox);

        // Category and Owner filters couple with the search box and with each other -
        // all three narrow the same list together (AND, not OR), so e.g. picking
        // "Insurance" + "Murugan R" + typing "state" only shows insurance accounts
        // owned by Murugan R with "state" somewhere in them.
        var categoryLabel = new Label { Text = "Category:", AutoSize = true, Padding = new Padding(0, 6, 6, 0) };
        _categoryFilter = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 16, 0) };
        _categoryFilter.Items.Add(AllCategoriesLabel);
        _categoryFilter.SelectedIndex = 0; // Populated for real by RefreshCategoryFilterItems, called from ApplyFilter below.
        _categoryFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        searchRow.Controls.Add(categoryLabel);
        searchRow.Controls.Add(_categoryFilter);

        var ownerLabel = new Label { Text = "Owner:", AutoSize = true, Padding = new Padding(0, 6, 6, 0) };
        _ownerFilter = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 0, 0) };
        _ownerFilter.Items.Add(AllOwnersLabel);
        _ownerFilter.SelectedIndex = 0;
        _ownerFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        searchRow.Controls.Add(ownerLabel);
        searchRow.Controls.Add(_ownerFilter);

        topPanel.Controls.Add(driveRow, 0, 0);
        topPanel.Controls.Add(vaultRow, 0, 1);
        topPanel.Controls.Add(searchRow, 0, 2);

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true,
            HideSelection = false,
            // Custom header color, zebra-striped rows, and a selection color that
            // stands out are all things the stock ListView won't do on its own - owner
            // draw hands us the header and every cell so we can paint them ourselves.
            OwnerDraw = true
        };
        _listView.Columns.Add("Category", 130);
        _listView.Columns.Add("Name", 170);
        _listView.Columns.Add("Institution", 140);
        _listView.Columns.Add("Owner", 110);
        _listView.Columns.Add("Username", 140);
        _listView.Columns.Add("Due Date", 100);
        _listView.Columns.Add("Autopay", 70);
        _listView.DoubleClick += (_, _) => EditSelected();
        _listView.DrawColumnHeader += ListView_DrawColumnHeader;
        _listView.DrawItem += (_, e) => e.DrawDefault = false; // rows are painted per-cell by DrawSubItem below
        _listView.DrawSubItem += ListView_DrawSubItem;

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 84,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(8)
        };

        var addBtn = new Button { Text = "Add Account", AutoSize = true };
        var editBtn = new Button { Text = "Edit", AutoSize = true };
        var deleteBtn = new Button { Text = "Delete", AutoSize = true };
        var openUrlBtn = new Button { Text = "Open URL", AutoSize = true };
        var copyUserBtn = new Button { Text = "Copy Username", AutoSize = true };
        var copyPassBtn = new Button { Text = "Copy Password", AutoSize = true };
        var shareBtn = new Button { Text = "Share...", AutoSize = true };
        var exportBtn = new Button { Text = "Export CSV...", AutoSize = true };
        var importBtn = new Button { Text = "Import CSV...", AutoSize = true };
        var profileBtn = new Button { Text = "Profile...", AutoSize = true };

        addBtn.Click += (_, _) => AddNew();
        editBtn.Click += (_, _) => EditSelected();
        deleteBtn.Click += (_, _) => DeleteSelected();
        openUrlBtn.Click += (_, _) => OpenSelectedUrl();
        copyUserBtn.Click += (_, _) => CopyField(a => a.UserName, "Username");
        copyPassBtn.Click += (_, _) => CopyField(a => a.Password, "Password");
        shareBtn.Click += (_, _) => ShareSelected();
        exportBtn.Click += (_, _) => ExportCsv();
        importBtn.Click += (_, _) => ImportCsv();
        profileBtn.Click += (_, _) => EditProfile();

        buttonPanel.Controls.AddRange(new Control[]
        {
            addBtn, editBtn, deleteBtn, openUrlBtn, copyUserBtn, copyPassBtn, shareBtn, exportBtn, importBtn, profileBtn
        });

        var accountsTab = new TabPage("Accounts");
        accountsTab.Controls.Add(_listView);
        accountsTab.Controls.Add(buttonPanel);
        accountsTab.Controls.Add(topPanel);

        // --- Dues tab: Past Due / Due Today / Due In One Week, with a Mark as Paid
        // button that records a payment (separate file, its own Drive sync - see
        // TrayApplicationContext.MarkAccountPaid) and rolls the account's due date
        // forward (or clears it, for a one-time bill) so it drops off this list.
        var duesFilterRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 8, 8, 4)
        };
        _pastDueRadio = new RadioButton { Text = "Past Due", AutoSize = true, Checked = true, Padding = new Padding(0, 0, 16, 0) };
        _dueTodayRadio = new RadioButton { Text = "Due Today", AutoSize = true, Padding = new Padding(0, 0, 16, 0) };
        _dueInWeekRadio = new RadioButton { Text = "Due In One Week", AutoSize = true };
        _pastDueRadio.CheckedChanged += (_, _) => RefreshDuesList();
        _dueTodayRadio.CheckedChanged += (_, _) => RefreshDuesList();
        _dueInWeekRadio.CheckedChanged += (_, _) => RefreshDuesList();
        duesFilterRow.Controls.Add(_pastDueRadio);
        duesFilterRow.Controls.Add(_dueTodayRadio);
        duesFilterRow.Controls.Add(_dueInWeekRadio);
        _duesTotalLabel = new Label { AutoSize = true, Padding = new Padding(24, 3, 0, 0), ForeColor = Color.DimGray };
        duesFilterRow.Controls.Add(_duesTotalLabel);

        _duesListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true,
            HideSelection = false,
            OwnerDraw = true
        };
        _duesListView.Columns.Add("Category", 120);
        _duesListView.Columns.Add("Name", 160);
        _duesListView.Columns.Add("Institution", 130);
        _duesListView.Columns.Add("Owner", 100);
        _duesListView.Columns.Add("Due Date", 100);
        _duesListView.Columns.Add("Amount Due", 90);
        _duesListView.Columns.Add("Autopay", 70);
        _duesListView.Columns.Add("Last Paid", 170);
        _duesListView.DrawColumnHeader += ListView_DrawColumnHeader;
        _duesListView.DrawItem += (_, e) => e.DrawDefault = false;
        _duesListView.DrawSubItem += ListView_DrawSubItem;
        _duesListView.SelectedIndexChanged += (_, _) =>
            _markPaidButton.Enabled = _duesListView.SelectedItems.Count > 0;

        var duesButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8)
        };
        _markPaidButton = new Button { Text = "Mark as Paid...", AutoSize = true, Enabled = false };
        _markPaidButton.Click += (_, _) => MarkSelectedDuePaid();
        var backfillButton = new Button { Text = "Backfill Past Payment...", AutoSize = true };
        backfillButton.Click += (_, _) => BackfillPastPayment();
        duesButtonPanel.Controls.Add(_markPaidButton);
        duesButtonPanel.Controls.Add(backfillButton);

        var duesTab = new TabPage("Dues");
        duesTab.Controls.Add(_duesListView);
        duesTab.Controls.Add(duesButtonPanel);
        duesTab.Controls.Add(duesFilterRow);

        // --- Overview tab: "how much do I have on hand" vs "how much do I owe",
        // rolled up from whichever accounts have a Current balance / Amount due
        // entered in Account Details (bank accounts, investment accounts, tuition/fees
        // accounts, etc. - not limited to any one category).
        var overviewTopPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 8, 8, 4)
        };
        _totalOnHandLabel = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 4, 24, 0) };
        _totalDuesLabel = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 4, 24, 0) };
        _netLabel = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 4, 0, 0) };
        overviewTopPanel.Controls.Add(_totalOnHandLabel);
        overviewTopPanel.Controls.Add(_totalDuesLabel);
        overviewTopPanel.Controls.Add(_netLabel);

        var overviewNote = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Padding = new Padding(8, 0, 8, 6),
            Text = "\"On Hand\" totals every account with a Current balance entered (bank, investment, etc.). " +
                   "\"Dues\" totals every account with an Amount due entered (tuition/fees, a loan payoff, etc.). " +
                   "Set these from Account Details."
        };

        _balancesListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true,
            HideSelection = false,
            OwnerDraw = true
        };
        _balancesListView.Columns.Add("Category", 130);
        _balancesListView.Columns.Add("Name", 170);
        _balancesListView.Columns.Add("Institution", 140);
        _balancesListView.Columns.Add("Owner", 110);
        _balancesListView.Columns.Add("Balance", 110);
        _balancesListView.Columns.Add("As Of", 100);
        _balancesListView.DoubleClick += (_, _) => EditFromBalancesTab();
        _balancesListView.DrawColumnHeader += ListView_DrawColumnHeader;
        _balancesListView.DrawItem += (_, e) => e.DrawDefault = false;
        _balancesListView.DrawSubItem += ListView_DrawSubItem;

        var overviewTab = new TabPage("Overview");
        overviewTab.Controls.Add(_balancesListView);
        overviewTab.Controls.Add(overviewNote);
        overviewTab.Controls.Add(overviewTopPanel);

        var tabControl = new TabControl { Dock = DockStyle.Fill };
        tabControl.TabPages.Add(accountsTab);
        tabControl.TabPages.Add(duesTab);
        tabControl.TabPages.Add(overviewTab);
        Controls.Add(tabControl);

        UpdateDriveStatus(isDriveConnected);
        RefreshData(_vault);
    }

    /// <summary>
    /// Reflects the true current Google Drive connection state - call this any time it
    /// might have changed (sign-in succeeded/failed, app just started, etc.) so this
    /// window never shows a stale "Connected" when it isn't, or vice versa.
    /// </summary>
    public void UpdateDriveStatus(bool isConnected)
    {
        if (isConnected)
        {
            _driveStatusLabel.Text = "✓ Connected to Google Drive - changes are backed up automatically.";
            _driveStatusLabel.ForeColor = Color.SeaGreen;
            _driveSignInButton.Visible = false;
        }
        else
        {
            _driveStatusLabel.Text = "⚠ Not connected to Google Drive - changes are stored on this PC only.";
            _driveStatusLabel.ForeColor = Color.Firebrick;
            _driveSignInButton.Visible = true;
        }

        Text = isConnected ? "Personal Vault  —  Google Drive: Connected" : "Personal Vault  —  Google Drive: Not Connected";
    }

    private static void OpenContainingFolder(string filePath)
    {
        try
        {
            // /select, highlights the file itself in Explorer rather than just opening
            // the folder, so it's obvious at a glance which file is the vault.
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open that folder: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OnSignInClicked()
    {
        _driveSignInButton.Enabled = false;
        try
        {
            // TrayApplicationContext.SignInToDriveAsync handles the whole flow itself
            // (including its own success/failure messages) and calls back into
            // UpdateDriveStatus once it knows the outcome, so there's nothing else to
            // do here afterward.
            await _signInToDrive();
        }
        finally
        {
            _driveSignInButton.Enabled = true;
        }
    }

    /// <summary>Repoints this window at (possibly new) vault data - e.g. after pulling a newer copy from Drive.</summary>
    public void RefreshData(VaultData vault)
    {
        _vault = vault;
        ApplyFilter();
        RefreshDuesList();
        RefreshOverview();
    }

    /// <summary>Rebuilds the Dues tab's list from whichever radio button is currently selected.</summary>
    private void RefreshDuesList()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var oneWeekOut = today.AddDays(7);

        IEnumerable<AccountEntry> accounts = _vault.Accounts.Where(a => a.DueDate.HasValue);

        if (_pastDueRadio.Checked)
            accounts = accounts.Where(a => DateOnly.FromDateTime(a.DueDate!.Value) < today);
        else if (_dueTodayRadio.Checked)
            accounts = accounts.Where(a => DateOnly.FromDateTime(a.DueDate!.Value) == today);
        else // Due In One Week - includes today through 7 days out
            accounts = accounts.Where(a =>
            {
                var due = DateOnly.FromDateTime(a.DueDate!.Value);
                return due >= today && due <= oneWeekOut;
            });

        // Most recent payment per account, so the list also answers "did I already pay
        // this?" without having to go dig through payment history separately.
        var lastPaymentByAccount = _getPayments()
            .GroupBy(p => p.AccountId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.PaidDate).First());

        _duesListView.BeginUpdate();
        _duesListView.Items.Clear();
        foreach (var account in accounts.OrderBy(a => a.DueDate))
        {
            var item = new ListViewItem(account.Category);
            item.SubItems.Add(account.Name);
            item.SubItems.Add(account.Institution);
            item.SubItems.Add(account.Owner);
            item.SubItems.Add(account.DueDate?.ToString("MMM d, yyyy") ?? "");
            item.SubItems.Add(account.AmountDue.HasValue ? account.AmountDue.Value.ToString("C") : "");
            item.SubItems.Add(account.IsAutomaticPayment ? "Yes" : "");
            item.SubItems.Add(lastPaymentByAccount.TryGetValue(account.Id, out var lastPaid)
                ? $"{lastPaid.AmountPaid:C} on {lastPaid.PaidDate:MMM d, yyyy}"
                : "");
            item.Tag = account;
            _duesListView.Items.Add(item);
        }
        _duesListView.EndUpdate();

        var totalDue = accounts.Where(a => a.AmountDue.HasValue).Sum(a => a.AmountDue!.Value);
        _duesTotalLabel.Text = $"Total amount due (this list): {totalDue:C}";

        _markPaidButton.Enabled = false;
    }

    /// <summary>
    /// Rebuilds the Overview tab: every account with a Current balance entered, plus
    /// the On Hand / Dues / Net totals. Called whenever accounts are added, edited,
    /// deleted or imported, and whenever the vault data is reloaded.
    /// </summary>
    private void RefreshOverview()
    {
        var withBalance = _vault.Accounts.Where(a => a.CurrentBalance.HasValue)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _balancesListView.BeginUpdate();
        _balancesListView.Items.Clear();
        foreach (var account in withBalance)
        {
            var item = new ListViewItem(account.Category);
            item.SubItems.Add(account.Name);
            item.SubItems.Add(account.Institution);
            item.SubItems.Add(account.Owner);
            item.SubItems.Add(account.CurrentBalance!.Value.ToString("C"));
            item.SubItems.Add(account.CurrentBalanceAsOf?.ToString("MMM d, yyyy") ?? "");
            item.Tag = account;
            _balancesListView.Items.Add(item);
        }
        _balancesListView.EndUpdate();

        var totalOnHand = withBalance.Sum(a => a.CurrentBalance!.Value);
        var totalDues = _vault.Accounts.Where(a => a.AmountDue.HasValue).Sum(a => a.AmountDue!.Value);
        var net = totalOnHand - totalDues;

        _totalOnHandLabel.Text = $"Total On Hand: {totalOnHand:C}";
        _totalDuesLabel.Text = $"Total Dues: {totalDues:C}";
        _netLabel.Text = $"Net: {net:C}";
        _netLabel.ForeColor = net < 0 ? Color.Firebrick : Color.SeaGreen;
    }

    /// <summary>Double-click on the Overview tab's grid opens the same Account Details dialog as the Accounts tab.</summary>
    private void EditFromBalancesTab()
    {
        if (_balancesListView.SelectedItems.Count == 0) return;
        var account = (AccountEntry)_balancesListView.SelectedItems[0].Tag!;

        using var form = new AccountEditForm(account, _vault.Profile.Name, _getDefaultBrowserPath(), KnownCategories(), GetCustomCategoryFields, SaveCustomCategoryFields);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            account.ModifiedUtc = DateTime.UtcNow;
            _save();
            ApplyFilter();
            RefreshDuesList();
            RefreshOverview();
        }
    }

    private void MarkSelectedDuePaid()
    {
        if (_duesListView.SelectedItems.Count == 0) return;
        var account = (AccountEntry)_duesListView.SelectedItems[0].Tag!;

        using var dialog = new MarkPaidForm(account.Name, account.DueDate);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // TrayApplicationContext.MarkAccountPaid does the actual work (records the
        // payment, advances/clears the due date, saves+uploads both files) and calls
        // RefreshData back on us once it's done - nothing else to do here.
        _markPaid(account, dialog.AmountPaid, dialog.PaidDate);
    }

    /// <summary>
    /// Catches up payment history for months that were never entered - works on any
    /// account (not just one currently showing as due), and can add several months at
    /// once. Deliberately separate from Mark as Paid: this only records history, it
    /// never touches the account's live due date.
    /// </summary>
    private void BackfillPastPayment()
    {
        if (_vault.Accounts.Count == 0)
        {
            MessageBox.Show(this, "Add an account first.", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new BackfillPaymentForm(_vault.Accounts);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _backfillPayments(dialog.SelectedAccount, dialog.AmountPaid, dialog.MostRecentPaidDate, dialog.Count, dialog.Spacing);
    }

    private static void ListView_DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var backBrush = new SolidBrush(GridHeaderBackColor);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        var textBounds = Rectangle.Inflate(e.Bounds, -6, 0);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? string.Empty, e.Font, textBounds, GridHeaderForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        e.DrawDefault = false;
    }

    private static void ListView_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var back = e.Item!.Selected
            ? GridSelectedBackColor
            : e.ItemIndex % 2 == 0 ? GridRowBackColor : GridAltRowBackColor;
        var fore = e.Item.Selected ? GridSelectedForeColor : Color.Black;

        using var backBrush = new SolidBrush(back);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        var textBounds = Rectangle.Inflate(e.Bounds, -4, 0);
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? string.Empty, e.Item.Font, textBounds, fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private void ApplyFilter()
    {
        if (_updatingFilters) return;

        RefreshOwnerFilterItems();
        RefreshCategoryFilterItems();

        var query = _searchBox.Text.Trim();
        var categorySelection = _categoryFilter.SelectedItem as string;
        var ownerSelection = _ownerFilter.SelectedItem as string;

        IEnumerable<AccountEntry> accounts = _vault.Accounts;
        if (!string.IsNullOrEmpty(query))
            accounts = accounts.Where(a => MatchesSearch(a, query));
        if (!string.IsNullOrEmpty(categorySelection) && categorySelection != AllCategoriesLabel)
            accounts = accounts.Where(a => string.Equals(a.Category, categorySelection, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(ownerSelection) && ownerSelection != AllOwnersLabel)
            accounts = accounts.Where(a => string.Equals(a.Owner, ownerSelection, StringComparison.OrdinalIgnoreCase));

        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var account in accounts.OrderBy(a => a.DueDate ?? DateTime.MaxValue))
        {
            var item = new ListViewItem(account.Category);
            item.SubItems.Add(account.Name);
            item.SubItems.Add(account.Institution);
            item.SubItems.Add(account.Owner);
            item.SubItems.Add(account.UserName);
            item.SubItems.Add(account.DueDate?.ToString("MMM d, yyyy") ?? "");
            item.SubItems.Add(account.IsAutomaticPayment ? "Yes" : "");
            item.Tag = account;
            _listView.Items.Add(item);
        }
        _listView.EndUpdate();
    }

    /// <summary>
    /// Rebuilds the Owner dropdown from whoever actually appears in the vault right now
    /// (new owners show up automatically as accounts are added/edited/imported), while
    /// keeping the current selection if it's still a valid owner - falls back to
    /// "All Owners" if the previously-selected owner no longer exists (e.g. their last
    /// account under that name was deleted or renamed).
    /// </summary>
    private void RefreshOwnerFilterItems()
    {
        var owners = _vault.Accounts
            .Select(a => a.Owner)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var desired = new List<string> { AllOwnersLabel };
        desired.AddRange(owners);

        var current = _ownerFilter.Items.Cast<string>().ToArray();
        if (current.SequenceEqual(desired)) return;

        var previousSelection = _ownerFilter.SelectedItem as string;

        _updatingFilters = true;
        try
        {
            _ownerFilter.Items.Clear();
            _ownerFilter.Items.AddRange(desired.Cast<object>().ToArray());
            var restoredIndex = previousSelection != null ? _ownerFilter.Items.IndexOf(previousSelection) : -1;
            _ownerFilter.SelectedIndex = restoredIndex >= 0 ? restoredIndex : 0;
        }
        finally
        {
            _updatingFilters = false;
        }
    }

    /// <summary>
    /// Rebuilds the Category dropdown from the built-in defaults plus whatever
    /// categories are actually in use across the vault right now - the same
    /// self-cleaning, always-in-sync approach as RefreshOwnerFilterItems, so a custom
    /// category added from Account Details ("Tuition", say) shows up here too without
    /// any separate list to maintain.
    /// </summary>
    private void RefreshCategoryFilterItems()
    {
        var inUse = _vault.Accounts
            .Select(a => a.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c));

        var categories = AccountCategories.Defaults
            .Union(inUse, StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var desired = new List<string> { AllCategoriesLabel };
        desired.AddRange(categories);

        var current = _categoryFilter.Items.Cast<string>().ToArray();
        if (current.SequenceEqual(desired)) return;

        var previousSelection = _categoryFilter.SelectedItem as string;

        _updatingFilters = true;
        try
        {
            _categoryFilter.Items.Clear();
            _categoryFilter.Items.AddRange(desired.Cast<object>().ToArray());
            var restoredIndex = previousSelection != null ? _categoryFilter.Items.IndexOf(previousSelection) : -1;
            _categoryFilter.SelectedIndex = restoredIndex >= 0 ? restoredIndex : 0;
        }
        finally
        {
            _updatingFilters = false;
        }
    }

    /// <summary>
    /// Every category currently known to the vault (built-in defaults plus any custom
    /// ones already in use), for seeding the Category dropdown in a new/edited
    /// account's Account Details dialog.
    /// </summary>
    private IEnumerable<string> KnownCategories() =>
        AccountCategories.Defaults
            .Union(_vault.Accounts.Select(a => a.Category).Where(c => !string.IsNullOrWhiteSpace(c)), StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Looks up a previously-defined "suggested fields" template for a custom category
    /// (see VaultData.CustomCategoryFields) - matched case-insensitively since the
    /// dictionary's own comparer doesn't survive JSON round-tripping.
    /// </summary>
    private string[]? GetCustomCategoryFields(string category) =>
        _vault.CustomCategoryFields
            .FirstOrDefault(kv => string.Equals(kv.Key, category, StringComparison.OrdinalIgnoreCase))
            .Value;

    /// <summary>
    /// Saves (or overwrites) the suggested-fields template for a custom category, from
    /// Account Details' "+ Category Fields" button, and persists the vault immediately
    /// so the definition isn't lost even if this particular account edit is cancelled.
    /// </summary>
    private void SaveCustomCategoryFields(string category, string[] fields)
    {
        var existingKey = _vault.CustomCategoryFields.Keys
            .FirstOrDefault(k => string.Equals(k, category, StringComparison.OrdinalIgnoreCase));
        if (existingKey != null)
            _vault.CustomCategoryFields.Remove(existingKey);

        _vault.CustomCategoryFields[category] = fields;
        _save();
    }

    /// <summary>Full-text-ish search across every field a person might actually remember about an account, including extra fields.</summary>
    private static bool MatchesSearch(AccountEntry a, string query)
    {
        bool Has(string? s) => !string.IsNullOrEmpty(s) && s.Contains(query, StringComparison.OrdinalIgnoreCase);

        return Has(a.Name) || Has(a.Institution) || Has(a.Owner) || Has(a.UserName) ||
               Has(a.AccountNumber) || Has(a.Website) || Has(a.PhoneNumber) || Has(a.Notes) ||
               Has(a.Category) ||
               a.ExtraFields.Any(kv => Has(kv.Key) || Has(kv.Value));
    }

    private AccountEntry? SelectedAccount() =>
        _listView.SelectedItems.Count > 0 ? _listView.SelectedItems[0].Tag as AccountEntry : null;

    private void AddNew()
    {
        var entry = new AccountEntry();
        using var form = new AccountEditForm(entry, _vault.Profile.Name, _getDefaultBrowserPath(), KnownCategories(), GetCustomCategoryFields, SaveCustomCategoryFields);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _vault.Accounts.Add(entry);
            _save();
            ApplyFilter();
            RefreshDuesList();
            RefreshOverview();
        }
    }

    private void EditSelected()
    {
        var account = SelectedAccount();
        if (account == null) return;

        using var form = new AccountEditForm(account, _vault.Profile.Name, _getDefaultBrowserPath(), KnownCategories(), GetCustomCategoryFields, SaveCustomCategoryFields);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            account.ModifiedUtc = DateTime.UtcNow;
            _save();
            ApplyFilter();
            RefreshDuesList();
            RefreshOverview();
        }
    }

    private void DeleteSelected()
    {
        var account = SelectedAccount();
        if (account == null) return;

        var result = MessageBox.Show(this, $"Delete '{account.Name}'? This cannot be undone.",
            "Personal Vault", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        _vault.Accounts.Remove(account);
        _save();
        ApplyFilter();
        RefreshDuesList();
        RefreshOverview();
    }

    /// <summary>
    /// Opens the selected account's Website in the browser set on the Profile form (or
    /// the system default browser if none was set there).
    /// </summary>
    private void OpenSelectedUrl()
    {
        var account = SelectedAccount();
        if (account == null) return;

        var uri = BrowserLauncher.TryParseUrl(account.Website);
        if (uri == null)
        {
            MessageBox.Show(this, "This account doesn't have a valid website URL set.", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            BrowserLauncher.Open(uri, _getDefaultBrowserPath());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open that website: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopyField(Func<AccountEntry, string> selector, string label)
    {
        var account = SelectedAccount();
        if (account == null) return;

        var value = selector(account);
        if (string.IsNullOrEmpty(value)) return;

        Clipboard.SetText(value);
        ScheduleClipboardClear(value);

        MessageBox.Show(this, $"{label} copied to clipboard. It will clear automatically in 20 seconds.",
            "Personal Vault", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Opens the Share Account dialog for the selected account - see ShareAccountForm / TrayApplicationContext.ShareAccountAsync.</summary>
    private void ShareSelected()
    {
        var account = SelectedAccount();
        if (account == null) return;

        using var form = new ShareAccountForm(account, _shareAccount);
        form.ShowDialog(this);
    }

    /// <summary>Clears the clipboard ~20s after a copy, but only if it still holds exactly what we put there.</summary>
    private void ScheduleClipboardClear(string copiedValue)
    {
        _clipboardClearTimer?.Stop();
        _clipboardClearTimer?.Dispose();

        _clipboardClearTimer = new System.Windows.Forms.Timer { Interval = 20_000 };
        _clipboardClearTimer.Tick += (_, _) =>
        {
            _clipboardClearTimer?.Stop();
            try
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == copiedValue)
                    Clipboard.Clear();
            }
            catch
            {
                // Clipboard can be momentarily locked by another app - not worth surfacing an error for.
            }
        };
        _clipboardClearTimer.Start();
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "PersonalVaultExport.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            CsvIO.Export(dialog.FileName, _vault.Accounts);
            MessageBox.Show(this,
                "Exported.\n\nImportant: this CSV file is plain, unencrypted text - every password is readable in it. " +
                "Treat it as sensitive and delete it once you're done with it.",
                "Personal Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export failed: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportCsv()
    {
        using var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var imported = CsvIO.Import(dialog.FileName);
            if (imported.Count == 0)
            {
                MessageBox.Show(this, "No rows found to import.", "Personal Vault",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(this,
                $"Import {imported.Count} account(s) from this file?\n\n" +
                "This adds them alongside your existing accounts - it will not overwrite or " +
                "de-duplicate anything, so importing the same file twice will create duplicates.",
                "Personal Vault", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            _vault.Accounts.AddRange(imported);
            _save();
            ApplyFilter();
            RefreshDuesList();
            RefreshOverview();

            MessageBox.Show(this, $"Imported {imported.Count} account(s).", "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Import failed: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void EditProfile()
    {
        using var form = new ProfileForm(_vault.Profile, _getDefaultBrowserPath(), _getGitHubUsername(), _changeMasterSecret, _getDataFolder(), _changeDataFolder);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _setDefaultBrowserPath(form.SelectedBrowserPath);
            _setGitHubUsername(form.GitHubUsername);
            _save();
            ApplyFilter(); // Owner column defaults may be worth re-checking after a name change, cheap to just refresh.
        }
    }
}
