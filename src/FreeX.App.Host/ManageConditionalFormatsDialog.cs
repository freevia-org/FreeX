using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;
using ManageConditionalFormatsPlanner = FreeX.App.Presentation.ConditionalFormatting.ManageConditionalFormatsPlanner;

namespace FreeX.App.Host;

/// <summary>
/// "Manage Conditional Formatting Rules" dialog - lists all rules on a sheet,
/// allows add / edit / delete / reorder, and returns the final ordered rule list.
/// </summary>
public sealed partial class ManageConditionalFormatsDialog : Window
{
    /// <summary>Set after OK or Apply is clicked. Priorities are re-assigned 1...N in list order.</summary>
    public IReadOnlyList<ConditionalFormat>? ResultRules { get; private set; }

    private readonly Sheet _sheet;
    private readonly ManageConditionalFormatsDialogPlan _dialogPlan;
    private readonly ManageConditionalFormatsSession _manageSession;
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
        _dialogPlan = ManageConditionalFormatsPlanner.CreateDialogPlan(sheet, selection);
        _manageSession = new ManageConditionalFormatsSession(
            sheet.ConditionalFormats,
            _dialogPlan.DefaultScopeOption.Range,
            ManageConditionalFormatsWorkingCopyPolicy.CurrentScope);
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

        ComboBoxItem? defaultScopeItem = null;
        foreach (var option in _dialogPlan.ScopeOptions)
        {
            var item = CreateScopeItem(option);
            _scopeBox.Items.Add(item);
            if (option.Scope == _dialogPlan.DefaultScope)
                defaultScopeItem = item;
        }

        _scopeBox.SelectedItem = defaultScopeItem;
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
        _listView.MouseDoubleClick += ListView_MouseDoubleClick;
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
        FocusTarget(ManageConditionalFormatsPlanner.InitialFocusTarget);
    }

    private void FocusRulesList()
    {
        FocusTarget(ManageConditionalFormatsPlanner.MissingSelectionFocusTarget);
    }

    private void FocusTarget(ManageConditionalFormatsFocusTarget target)
    {
        if (target == ManageConditionalFormatsFocusTarget.ScopeSelector)
        {
            _scopeBox.Focus();
            Keyboard.Focus(_scopeBox);
            return;
        }

        _listView.Focus();
        Keyboard.Focus(_listView);
    }

    private void ScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateRules();
    }

    private void PopulateRules()
    {
        _manageSession.SetScope(CurrentScopeRange(), _sheet.ConditionalFormats);
        ReloadWorkingRules();
    }

    // Toolbar button handlers

    private void NewRule_Click(object sender, RoutedEventArgs e)
    {
        var defaultRange = GetDefaultNewRuleRange();

        var dlg = new NewConditionalFormatRuleDialog(DefaultNewRuleType, defaultRange);
        dlg.Owner = this;
        if (dlg.ShowDialog() == true && dlg.ResultRule is { } newRule)
        {
            _manageSession.Add(newRule);
            ReloadWorkingRules(newRule.Id);
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
            if (_manageSession.Replace(edited))
                ReloadWorkingRules(edited.Id);
        }
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        EditRule_Click(sender, e);
        e.Handled = true;
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (_listView.SelectedItem is not ConditionalFormat selected)
        {
            FocusRulesList();
            return;
        }

        var selectedIndex = _rules.IndexOf(selected);
        if (!_manageSession.Delete(selected.Id))
            return;

        ReloadWorkingRules();
        if (_rules.Count > 0)
            _listView.SelectedIndex = Math.Min(selectedIndex, _rules.Count - 1);
    }

    private void DuplicateRule_Click(object sender, RoutedEventArgs e)
    {
        if (_listView.SelectedItem is not ConditionalFormat selected)
        {
            FocusRulesList();
            return;
        }

        var duplicateId = Guid.NewGuid();
        if (_manageSession.Duplicate(selected.Id, duplicateId))
            ReloadWorkingRules(duplicateId);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (_listView.SelectedItem is not ConditionalFormat selected)
            return;

        if (_manageSession.Move(selected.Id, ConditionalFormatRuleMoveDirection.Up))
            ReloadWorkingRules(selected.Id);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (_listView.SelectedItem is not ConditionalFormat selected)
            return;

        if (_manageSession.Move(selected.Id, ConditionalFormatRuleMoveDirection.Down))
            ReloadWorkingRules(selected.Id);
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
        ResultRules = _manageSession.BuildResultRules(_sheet.ConditionalFormats);
    }

    // Helpers

    private void ReloadWorkingRules(Guid? selectedRuleId = null)
    {
        _rules.Clear();
        foreach (var rule in _manageSession.VisibleRules)
            _rules.Add(rule);

        if (selectedRuleId is { } id)
            _listView.SelectedItem = FindWorkingRuleById(id);
    }

    private GridRange? CurrentScopeRange() =>
        _scopeBox.SelectedItem is ComboBoxItem { Tag: ManageConditionalFormatScopeOption selectedScope }
            ? selectedScope.Range
            : null;

    private static ComboBoxItem CreateScopeItem(ManageConditionalFormatScopeOption option) =>
        new() { Content = UiText.Get(option.LabelKey), Tag = option };

    private void RangePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject current)
            return;
        var rule = (sender as FrameworkElement)?.DataContext as ConditionalFormat;

        while (current is not null)
        {
            if (current is DockPanel panel)
            {
                var rangeBox = FindFirstTextBox(panel);
                if (rangeBox is not null)
                {
                    rangeBox.Focus();
                    rangeBox.SelectAll();
                    if (rule is not null)
                    {
                        AppliesToRangeSelectionRequest = ManageConditionalFormatsPlanner
                            .CreateAppliesToRangeSelectionRequest(rule.Id, rangeBox.Text);
                        _requestAppliesToRangeSelection?.Invoke(AppliesToRangeSelectionRequest);
                    }
                }
                return;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                ?? LogicalTreeHelper.GetParent(current);
        }
    }

    private GridRange GetDefaultNewRuleRange() => _dialogPlan.DefaultNewRuleRange;

    private ConditionalFormat? FindWorkingRuleById(Guid ruleId)
    {
        foreach (var rule in _rules)
            if (rule.Id == ruleId)
                return rule;

        return null;
    }

    private static TextBox? FindFirstTextBox(Panel panel)
    {
        foreach (var child in panel.Children)
            if (child is TextBox textBox)
                return textBox;

        return null;
    }

    public void ApplyAppliesToRangeSelection(Guid ruleId, GridRange range)
    {
        if (!_manageSession.ApplyRange(ruleId, range))
            return;

        ReloadWorkingRules(ruleId);
        FocusRulesList();
    }
}
