using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// Profile's "Category Defaults..." button - lets you configure your own
/// Repeats/Autopay/Institution starting values per category (e.g. "Work" always
/// defaults Institution to your employer), on top of the app's built-in ones
/// (Models/AccountEntry.cs -> CategoryEntryDefaults). Operates directly on
/// VaultData.CustomCategoryDefaults (passed in by reference, not a working copy) -
/// every Add/Edit/Delete here takes effect immediately, same as
/// MainForm.GetCustomCategoryFields/SaveCustomCategoryFields; the caller just needs to
/// persist the vault after this form closes.
/// </summary>
public class CategoryDefaultsForm : Form
{
    private readonly Dictionary<string, CategoryDefault> _defaults;
    private readonly List<string> _knownCategories;
    private readonly ListView _listView;
    private readonly Button _editButton;
    private readonly Button _deleteButton;

    public CategoryDefaultsForm(Dictionary<string, CategoryDefault> defaults, IEnumerable<string> knownCategories)
    {
        _defaults = defaults;
        _knownCategories = knownCategories.Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Text = "Category Defaults";
        Width = 640;
        Height = 440;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 40,
            Padding = new Padding(10, 8, 10, 0),
            ForeColor = Color.DimGray,
            Text = "These override the app's built-in starting values (CreditCard, Mortgage, etc.) " +
                   "for the categories listed here, and apply only when adding a brand-new entry."
        };

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true,
            HideSelection = false
        };
        _listView.Columns.Add("Category", 150);
        _listView.Columns.Add("Repeats", 110);
        _listView.Columns.Add("Autopay", 80);
        _listView.Columns.Add("Institution", 180);
        _listView.DoubleClick += (_, _) => EditSelected();
        _listView.SelectedIndexChanged += (_, _) => UpdateButtonStates();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        var closeButton = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        var addButton = new Button { Text = "Add...", AutoSize = true };
        _editButton = new Button { Text = "Edit...", AutoSize = true, Enabled = false };
        _deleteButton = new Button { Text = "Delete", AutoSize = true, Enabled = false };
        addButton.Click += (_, _) => AddNew();
        _editButton.Click += (_, _) => EditSelected();
        _deleteButton.Click += (_, _) => DeleteSelected();
        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(_deleteButton);
        buttonPanel.Controls.Add(_editButton);
        buttonPanel.Controls.Add(addButton);

        Controls.Add(_listView);
        Controls.Add(hint);
        Controls.Add(buttonPanel);

        CancelButton = closeButton;

        RefreshList();
    }

    private void RefreshList()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var kv in _defaults.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ListViewItem(kv.Key);
            item.SubItems.Add(kv.Value.Recurrence.ToString());
            item.SubItems.Add(kv.Value.Autopay ? "Yes" : "");
            item.SubItems.Add(kv.Value.Institution ?? "");
            item.Tag = kv.Key;
            _listView.Items.Add(item);
        }
        _listView.EndUpdate();
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        bool hasSelection = _listView.SelectedItems.Count > 0;
        _editButton.Enabled = hasSelection;
        _deleteButton.Enabled = hasSelection;
    }

    private string? SelectedKey() =>
        _listView.SelectedItems.Count > 0 ? _listView.SelectedItems[0].Tag as string : null;

    private void AddNew()
    {
        using var form = new CategoryDefaultForm(null, null, _knownCategories.Concat(_defaults.Keys));
        if (form.ShowDialog(this) != DialogResult.OK) return;

        _defaults[form.Category] = form.Result;
        RefreshList();
    }

    private void EditSelected()
    {
        var key = SelectedKey();
        if (key == null) return;

        using var form = new CategoryDefaultForm(key, _defaults[key], _knownCategories.Concat(_defaults.Keys));
        if (form.ShowDialog(this) != DialogResult.OK) return;

        // A renamed category moves to a new dictionary key - remove the old one first
        // so editing "Work" into "Job" doesn't leave both entries behind.
        if (!string.Equals(key, form.Category, StringComparison.OrdinalIgnoreCase))
            _defaults.Remove(key);

        _defaults[form.Category] = form.Result;
        RefreshList();
    }

    private void DeleteSelected()
    {
        var key = SelectedKey();
        if (key == null) return;

        var result = MessageBox.Show(this, $"Delete the default for \"{key}\"?", "Personal Vault",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        _defaults.Remove(key);
        RefreshList();
    }
}
