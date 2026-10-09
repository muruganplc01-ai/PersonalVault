using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// Profile's "Field Sets..." button - lets you define, per category, exactly which
/// fields appear in Account Details and in what order (see
/// Models/AccountEntry.cs -> CategoryFieldSetDefaults for the full picture: reserved
/// vs. custom captions, why hiding a field never deletes its value). Operates directly
/// on VaultData.CategoryFieldSets (passed in by reference) - switching the category
/// picker commits whatever was just edited for the previous category immediately, same
/// "mutate and persist right away" pattern as CategoryDefaultsForm.
/// </summary>
public class CategoryFieldSetsForm : Form
{
    private const string BalanceTypeUseDefault = "(use default)";

    private readonly Dictionary<string, List<FieldDefinition>> _vaultFieldSets;
    private readonly Dictionary<string, BalanceType> _vaultBalanceTypes;
    private readonly List<string> _captionSuggestions;
    private readonly ComboBox _categoryBox;
    private readonly ComboBox _balanceTypeBox;
    private readonly ListView _listView;
    private readonly Button _editButton;
    private readonly Button _deleteButton;
    private readonly Button _moveUpButton;
    private readonly Button _moveDownButton;

    private string? _currentCategory;
    private List<FieldDefinition> _workingFields = new();

    public CategoryFieldSetsForm(
        Dictionary<string, List<FieldDefinition>> vaultFieldSets,
        Dictionary<string, BalanceType> vaultBalanceTypes,
        IEnumerable<string> knownCategories)
    {
        _vaultFieldSets = vaultFieldSets;
        _vaultBalanceTypes = vaultBalanceTypes;

        var categories = new List<string> { CategoryFieldSetDefaults.DefaultsKey };
        categories.AddRange(knownCategories
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

        _captionSuggestions = CategoryFieldSetDefaults.ReservedCaptions
            .Concat(CategoryFieldSetDefaults.BuiltInDefaults.Values.SelectMany(l => l.Select(f => f.Caption)))
            .Concat(_vaultFieldSets.Values.SelectMany(l => l.Select(f => f.Caption)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Text = "Field Sets";
        Width = 640;
        Height = 510;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 76,
            Padding = new Padding(10, 8, 10, 0),
            ForeColor = Color.DimGray,
            Text = "Choose which fields appear in Account Details for each category, and in what " +
                   "order. \"[Defaults]\" applies to any category with no field set of its own here " +
                   "(including brand-new ones). Hiding a field never deletes its value - it just stops " +
                   "showing. Balance type controls whether this category's Current Balance counts as " +
                   "cash on hand (Asset) or a debt (Liability, e.g. a loan's remaining payoff) in the " +
                   "Overview tab."
        };

        var categoryRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 4, 10, 8) };
        categoryRow.Controls.Add(new Label { Text = "Category:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
        _categoryBox = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        _categoryBox.Items.AddRange(categories.Cast<object>().ToArray());
        categoryRow.Controls.Add(_categoryBox);

        categoryRow.Controls.Add(new Label { Text = "Balance type:", AutoSize = true, Padding = new Padding(16, 6, 8, 0) });
        _balanceTypeBox = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _balanceTypeBox.Items.AddRange(new object[] { BalanceTypeUseDefault, nameof(BalanceType.Asset), nameof(BalanceType.Liability) });
        categoryRow.Controls.Add(_balanceTypeBox);

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true,
            HideSelection = false
        };
        _listView.Columns.Add("Caption", 200);
        _listView.Columns.Add("Data Type", 120);
        _listView.Columns.Add("Size", 80);
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
        _moveDownButton = new Button { Text = "Move Down", AutoSize = true, Enabled = false };
        _moveUpButton = new Button { Text = "Move Up", AutoSize = true, Enabled = false };
        addButton.Click += (_, _) => AddNew();
        _editButton.Click += (_, _) => EditSelected();
        _deleteButton.Click += (_, _) => DeleteSelected();
        _moveUpButton.Click += (_, _) => MoveSelected(-1);
        _moveDownButton.Click += (_, _) => MoveSelected(1);
        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(_deleteButton);
        buttonPanel.Controls.Add(_editButton);
        buttonPanel.Controls.Add(addButton);
        buttonPanel.Controls.Add(_moveDownButton);
        buttonPanel.Controls.Add(_moveUpButton);

        Controls.Add(_listView);
        Controls.Add(categoryRow);
        Controls.Add(hint);
        Controls.Add(buttonPanel);

        CancelButton = closeButton;

        _categoryBox.SelectedIndexChanged += (_, _) => SwitchCategory();
        FormClosing += (_, _) => CommitWorkingFields();

        _categoryBox.SelectedIndex = 0; // triggers SwitchCategory for [Defaults]
    }

    private void SwitchCategory()
    {
        CommitWorkingFields();

        _currentCategory = _categoryBox.SelectedItem as string;
        _workingFields = CategoryFieldSetDefaults.Resolve(_currentCategory, _vaultFieldSets)
            .Select(f => new FieldDefinition { Caption = f.Caption, DataType = f.DataType, Size = f.Size })
            .ToList();
        RefreshList();

        BalanceType? existingOverride = null;
        if (_currentCategory != null)
        {
            var match = _vaultBalanceTypes.FirstOrDefault(kv => string.Equals(kv.Key, _currentCategory, StringComparison.OrdinalIgnoreCase));
            if (match.Key != null) existingOverride = match.Value;
        }
        _balanceTypeBox.SelectedItem = existingOverride?.ToString() ?? BalanceTypeUseDefault;
    }

    /// <summary>Writes the currently-edited category's field list and balance type into the real vault dictionaries - called on every category switch and when this form closes, so nothing typed is ever lost.</summary>
    private void CommitWorkingFields()
    {
        if (_currentCategory == null) return;
        _vaultFieldSets[_currentCategory] = _workingFields;

        var existingKey = _vaultBalanceTypes.Keys.FirstOrDefault(k => string.Equals(k, _currentCategory, StringComparison.OrdinalIgnoreCase));
        if (existingKey != null) _vaultBalanceTypes.Remove(existingKey);

        var selected = _balanceTypeBox.SelectedItem as string;
        if (selected != null && selected != BalanceTypeUseDefault)
            _vaultBalanceTypes[_currentCategory] = Enum.Parse<BalanceType>(selected);
    }

    private void RefreshList()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var field in _workingFields)
        {
            var item = new ListViewItem(field.Caption);
            item.SubItems.Add(field.DataType.ToString());
            item.SubItems.Add(field.Size.ToString());
            item.Tag = field;
            _listView.Items.Add(item);
        }
        _listView.EndUpdate();
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        int index = _listView.SelectedIndices.Count > 0 ? _listView.SelectedIndices[0] : -1;
        bool hasSelection = index >= 0;
        _editButton.Enabled = hasSelection;
        _deleteButton.Enabled = hasSelection;
        _moveUpButton.Enabled = hasSelection && index > 0;
        _moveDownButton.Enabled = hasSelection && index < _workingFields.Count - 1;
    }

    private FieldDefinition? SelectedField() =>
        _listView.SelectedItems.Count > 0 ? _listView.SelectedItems[0].Tag as FieldDefinition : null;

    private void AddNew()
    {
        using var form = new FieldDefinitionForm(null, _captionSuggestions);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        _workingFields.Add(form.Result);
        RefreshList();
    }

    private void EditSelected()
    {
        var field = SelectedField();
        if (field == null) return;

        using var form = new FieldDefinitionForm(field, _captionSuggestions);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        int index = _workingFields.IndexOf(field);
        _workingFields[index] = form.Result;
        RefreshList();
    }

    private void DeleteSelected()
    {
        var field = SelectedField();
        if (field == null) return;

        var result = MessageBox.Show(this, $"Remove \"{field.Caption}\" from this category's field set?", "Personal Vault",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        _workingFields.Remove(field);
        RefreshList();
    }

    private void MoveSelected(int delta)
    {
        var field = SelectedField();
        if (field == null) return;

        int index = _workingFields.IndexOf(field);
        int newIndex = index + delta;
        if (newIndex < 0 || newIndex >= _workingFields.Count) return;

        _workingFields.RemoveAt(index);
        _workingFields.Insert(newIndex, field);
        RefreshList();
        _listView.Items[newIndex].Selected = true;
        _listView.Items[newIndex].Focused = true;
    }
}
