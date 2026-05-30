using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record ConditionalFormatAppliesToRangeSelectionRequest(
    Guid RuleId,
    string CurrentText,
    bool CollapseDialog = true);

/// <summary>
/// "Manage Conditional Formatting Rules" dialog - lists all rules on a sheet,
/// allows add / edit / delete / reorder, and returns the final ordered rule list.
/// </summary>
public sealed partial class ManageConditionalFormatsDialog : Window
{
    private enum ConditionalFormatScope
    {
        Sheet,
        Selection,
        Table
    }

    /// <summary>Set after OK or Apply is clicked. Priorities are re-assigned 1...N in list order.</summary>
    public IReadOnlyList<ConditionalFormat>? ResultRules { get; private set; }

    private readonly Sheet _sheet;
    private readonly GridRange? _selection;
    private readonly Action<ConditionalFormatAppliesToRangeSelectionRequest>? _requestAppliesToRangeSelection;
    private readonly Action<IReadOnlyList<ConditionalFormat>>? _applyRules;

    // Working copy bound to the ListView.
    private readonly ObservableCollection<ConditionalFormat> _rules = [];

    private readonly ComboBox _scopeBox;
    private readonly ListView _listView;
    private readonly Button _editBtn;
    private readonly Button _duplicateBtn;
    private readonly Button _deleteBtn;
    private readonly Button _moveUpBtn;
    private readonly Button _moveDownBtn;
    private readonly Button _applyBtn;

    private static string DefaultNewRuleType => UiText.Get("ManageConditionalFormats_DefaultNewRuleType");

    public ConditionalFormatAppliesToRangeSelectionRequest? AppliesToRangeSelectionRequest { get; private set; }

    public ManageConditionalFormatsDialog(
        Sheet sheet,
        GridRange? selection,
        Action<ConditionalFormatAppliesToRangeSelectionRequest>? requestAppliesToRangeSelection = null,
        Action<IReadOnlyList<ConditionalFormat>>? applyRules = null)
    {
        _sheet     = sheet;
        _selection = selection;
        _requestAppliesToRangeSelection = requestAppliesToRangeSelection;
        _applyRules = applyRules;

        Title = UiText.Get("ManageConditionalFormats_ConditionalFormattingRulesManager");
        Width  = 560;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        // Root layout
        var root = new DockPanel { Margin = new Thickness(12) };

        // Top bar: scope selector
        var topBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(topBar, Dock.Top);

        _scopeBox = new ComboBox { MinWidth = 160, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        topBar.Children.Add(new Label
        {
            Content = UiText.Get("ManageConditionalFormats_ShowFormattingRulesFor"),
            Target = _scopeBox,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(0, 0, 6, 0)
        });

        var sheetScope = CreateScopeItem(ConditionalFormatScope.Sheet, UiText.Get("ManageConditionalFormats_ScopeThisWorksheet"));
        var tableScope = CreateScopeItem(ConditionalFormatScope.Table, UiText.Get("ManageConditionalFormats_ScopeThisTable"));
        var selectionScope = CreateScopeItem(ConditionalFormatScope.Selection, UiText.Get("ManageConditionalFormats_ScopeCurrentSelection"));

        _scopeBox.Items.Add(sheetScope);
        if (FindSelectionTableRange() is not null)
            _scopeBox.Items.Add(tableScope);
        if (selection.HasValue) _scopeBox.Items.Add(selectionScope);
        _scopeBox.SelectedItem = selection.HasValue ? selectionScope : sheetScope;
        _scopeBox.SelectionChanged += ScopeBox_SelectionChanged;
        topBar.Children.Add(_scopeBox);

        root.Children.Add(topBar);

        // Bottom button row
        var bottomRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin              = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(bottomRow, Dock.Bottom);

        var okBtn     = new Button { Content = UiText.Ok,     Width = 72, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        var cancelBtn = new Button { Content = UiText.Cancel, Width = 72, Margin = new Thickness(0, 0, 6, 0), IsCancel = true };
        _applyBtn = new Button { Content = UiText.Get("ManageConditionalFormats_Apply"),  Width = 72 };
        okBtn.Click    += OkBtn_Click;
        _applyBtn.Click += ApplyBtn_Click;
        bottomRow.Children.Add(okBtn);
        bottomRow.Children.Add(cancelBtn);
        bottomRow.Children.Add(_applyBtn);
        root.Children.Add(bottomRow);

        // Middle toolbar: New / Edit / Duplicate / Delete / reorder
        var toolBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(toolBar, Dock.Bottom);

        var newBtn   = new Button { Content = UiText.Get("ManageConditionalFormats_NewRule"), Width = 104, Margin = new Thickness(0, 0, 6, 0) };
        _editBtn     = new Button { Content = UiText.Get("ManageConditionalFormats_EditRule"),   Width = 94, Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
        _duplicateBtn = new Button { Content = UiText.Get("ManageConditionalFormats_DuplicateRule"), Width = 118, Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
        _deleteBtn   = new Button { Content = UiText.Get("ManageConditionalFormats_DeleteRule"), Width = 100, Margin = new Thickness(0, 0, 12, 0), IsEnabled = false };
        _moveUpBtn   = new Button { Content = "\u25B2", Width = 32, Margin = new Thickness(0, 0, 4, 0), ToolTip = UiText.Get("ManageConditionalFormats_MoveSelectedRuleUp"), IsEnabled = false };
        _moveDownBtn = new Button { Content = "\u25BC", Width = 32, ToolTip = UiText.Get("ManageConditionalFormats_MoveSelectedRuleDown"), IsEnabled = false };
        System.Windows.Automation.AutomationProperties.SetName(_moveUpBtn, UiText.Get("ManageConditionalFormats_MoveUp"));
        System.Windows.Automation.AutomationProperties.SetName(_moveDownBtn, UiText.Get("ManageConditionalFormats_MoveDown"));

        newBtn.Click       += NewRule_Click;
        _editBtn.Click     += EditRule_Click;
        _duplicateBtn.Click += DuplicateRule_Click;
        _deleteBtn.Click   += DeleteRule_Click;
        _moveUpBtn.Click   += MoveUp_Click;
        _moveDownBtn.Click += MoveDown_Click;

        toolBar.Children.Add(newBtn);
        toolBar.Children.Add(_editBtn);
        toolBar.Children.Add(_duplicateBtn);
        toolBar.Children.Add(_deleteBtn);
        toolBar.Children.Add(_moveUpBtn);
        toolBar.Children.Add(_moveDownBtn);
        root.Children.Add(toolBar);

        // ListView
        _listView = new ListView
        {
            ItemsSource   = _rules,
            SelectionMode = SelectionMode.Single
        };
        _listView.SelectionChanged += ListView_SelectionChanged;
        _listView.MouseDoubleClick += EditRule_Click;
        _listView.KeyDown += ListView_KeyDown;
        AutomationProperties.SetName(_listView, UiText.Get("ManageConditionalFormats_ConditionalFormattingRules"));

        _listView.View = CreateRulesGridView();
        var rulesPanel = new DockPanel();
        var rulesLabel = new Label { Content = UiText.Get("ManageConditionalFormats_Rules"), Target = _listView, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(rulesLabel, Dock.Top);
        rulesPanel.Children.Add(rulesLabel);
        rulesPanel.Children.Add(_listView);
        root.Children.Add(rulesPanel);

        Content = root;

        // Initial load
        PopulateRules();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    // Scope selector

    private void FocusInitialKeyboardTarget()
    {
        _scopeBox.Focus();
        Keyboard.Focus(_scopeBox);
    }

    private void FocusRulesList()
    {
        _listView.Focus();
        Keyboard.Focus(_listView);
    }

    private void ScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateRules();
    }

    private void PopulateRules()
    {
        _rules.Clear();
        var scopeRange = CurrentScopeRange();

        var source = scopeRange is { } range
            ? _sheet.ConditionalFormats.Where(r => RangesOverlap(r.AppliesTo, range))
            : (IEnumerable<ConditionalFormat>)_sheet.ConditionalFormats;

        var priority = 1;
        foreach (var rule in source)
        {
            // Work on a shallow clone so we don't mutate the live sheet until OK/Apply
            var copy = CloneWithPriority(rule, priority++);
            _rules.Add(copy);
        }
    }

    // Toolbar button handlers

    private void NewRule_Click(object sender, RoutedEventArgs e)
    {
        var defaultRange = _selection ?? _sheet.ConditionalFormats
            .Select(r => (GridRange?)r.AppliesTo)
            .FirstOrDefault() ?? new GridRange(
                new CellAddress(_sheet.Id, 1, 1),
                new CellAddress(_sheet.Id, 1, 1));

        var dlg = new NewConditionalFormatRuleDialog(DefaultNewRuleType, defaultRange);
        dlg.Owner = this;
        if (dlg.ShowDialog() == true && dlg.ResultRule is { } newRule)
        {
            var copy = CloneWithPriority(newRule, _rules.Count + 1);
            _rules.Add(copy);
            _listView.SelectedItem = copy;
        }
    }

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (_listView.SelectedItem is not ConditionalFormat selected)
        {
            FocusRulesList();
            return;
        }

        var dlg = new ConditionalFormatDialog(selected) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.ResultRule is { } edited)
        {
            var idx  = _rules.IndexOf(selected);
            var copy = CloneWithPriority(edited, idx + 1);
            _rules[idx] = copy;
            _listView.SelectedItem = copy;
        }
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (_listView.SelectedItem is not ConditionalFormat selected)
        {
            FocusRulesList();
            return;
        }
        _rules.Remove(selected);
        ReassignPriorities();
    }

    private void DuplicateRule_Click(object sender, RoutedEventArgs e)
    {
        if (_listView.SelectedItem is not ConditionalFormat selected)
        {
            FocusRulesList();
            return;
        }

        var idx = _rules.IndexOf(selected);
        if (idx < 0) return;

        var duplicate = CloneWithPriority(selected, idx + 2, Guid.NewGuid());
        _rules.Insert(idx + 1, duplicate);
        _listView.SelectedItem = duplicate;
        ReassignPriorities();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var idx = _rules.IndexOf(_listView.SelectedItem as ConditionalFormat ?? default!);
        if (idx <= 0) return;
        (_rules[idx - 1], _rules[idx]) = (_rules[idx], _rules[idx - 1]);
        _listView.SelectedIndex = idx - 1;
        ReassignPriorities();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var idx = _rules.IndexOf(_listView.SelectedItem as ConditionalFormat ?? default!);
        if (idx < 0 || idx >= _rules.Count - 1) return;
        (_rules[idx + 1], _rules[idx]) = (_rules[idx], _rules[idx + 1]);
        _listView.SelectedIndex = idx + 1;
        ReassignPriorities();
    }

    private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = _listView.SelectedItem is not null;
        _editBtn.IsEnabled   = hasSelection;
        _duplicateBtn.IsEnabled = hasSelection;
        _deleteBtn.IsEnabled = hasSelection;

        var idx = _listView.SelectedIndex;
        _moveUpBtn.IsEnabled   = hasSelection && idx > 0;
        _moveDownBtn.IsEnabled = hasSelection && idx < _rules.Count - 1;
    }

    private void ListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            EditRule_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteRule_Click(sender, e);
            e.Handled = true;
        }
    }

    // OK / Apply

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitResult();
        DialogResult = true;
    }

    private void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitResult();
        if (ResultRules is not null)
            _applyRules?.Invoke(ResultRules);
    }

    private void CommitResult()
    {
        ReassignPriorities();
        ResultRules = BuildResultRules(
            _sheet.ConditionalFormats,
            CurrentScopeRange(),
            IsFilteringToRange(),
            _rules);
    }

    // Helpers

    private void ReassignPriorities()
    {
        // ObservableCollection items are mutable objects; create new copies with updated priority.
        for (var i = 0; i < _rules.Count; i++)
        {
            var r = _rules[i];
            if (r.Priority != i + 1)
                _rules[i] = CloneWithPriority(r, i + 1);
        }
    }

    private bool IsFilteringToRange() => CurrentScopeRange() is not null;

    private GridRange? CurrentScopeRange() =>
        _scopeBox.SelectedItem is ComboBoxItem { Tag: ConditionalFormatScope selectedScope }
            ? selectedScope switch
            {
                ConditionalFormatScope.Selection => _selection,
                ConditionalFormatScope.Table => FindSelectionTableRange(),
                _ => null
            }
            : null;

    private static ComboBoxItem CreateScopeItem(ConditionalFormatScope scope, string label) =>
        new() { Content = label, Tag = scope };

    private GridRange? FindSelectionTableRange()
    {
        if (_selection is not { } selection)
            return null;

        return _sheet.StructuredTables
            .FirstOrDefault(table => RangesOverlap(table.Range, selection))
            ?.Range;
    }

    private void RangePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject current)
            return;
        var rule = (sender as FrameworkElement)?.DataContext as ConditionalFormat;

        while (current is not null)
        {
            if (current is DockPanel panel)
            {
                var rangeBox = panel.Children.OfType<TextBox>().FirstOrDefault();
                if (rangeBox is not null)
                {
                    rangeBox.Focus();
                    rangeBox.SelectAll();
                    if (rule is not null)
                    {
                        AppliesToRangeSelectionRequest = CreateAppliesToRangeSelectionRequest(rule.Id, rangeBox.Text);
                        _requestAppliesToRangeSelection?.Invoke(AppliesToRangeSelectionRequest);
                    }
                }
                return;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                ?? LogicalTreeHelper.GetParent(current);
        }
    }

    public void ApplyAppliesToRangeSelection(Guid ruleId, GridRange range)
    {
        var index = _rules
            .Select((rule, ruleIndex) => new { rule, ruleIndex })
            .FirstOrDefault(item => item.rule.Id == ruleId)
            ?.ruleIndex;
        if (index is null)
            return;

        var updated = CloneWithPriority(_rules[index.Value], index.Value + 1);
        updated.AppliesTo = range;
        _rules[index.Value] = updated;
        _listView.SelectedItem = updated;
        FocusRulesList();
    }
}
