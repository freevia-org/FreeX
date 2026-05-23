using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

/// <summary>
/// "Manage Conditional Formatting Rules" dialog — lists all rules on a sheet,
/// allows add / edit / delete / reorder, and returns the final ordered rule list.
/// </summary>
public sealed partial class ManageConditionalFormatsDialog : Window
{
    /// <summary>Set after OK or Apply is clicked. Priorities are re-assigned 1…N in list order.</summary>
    public IReadOnlyList<ConditionalFormat>? ResultRules { get; private set; }

    private readonly Sheet _sheet;
    private readonly GridRange? _selection;

    // Working copy — bound to the ListView
    private readonly ObservableCollection<ConditionalFormat> _rules = [];

    private readonly ComboBox _scopeBox;
    private readonly ListView _listView;
    private readonly Button _editBtn;
    private readonly Button _duplicateBtn;
    private readonly Button _deleteBtn;
    private readonly Button _moveUpBtn;
    private readonly Button _moveDownBtn;

    private const string ScopeSheet     = "This Worksheet";
    private const string ScopeSelection = "Current Selection";

    public ManageConditionalFormatsDialog(Sheet sheet, GridRange? selection)
    {
        _sheet     = sheet;
        _selection = selection;

        Title  = "Conditional Formatting Rules Manager";
        Width  = 560;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        // ── Root layout ────────────────────────────────────────────────────────
        var root = new DockPanel { Margin = new Thickness(12) };

        // ── Top bar: scope selector ────────────────────────────────────────────
        var topBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(topBar, Dock.Top);

        _scopeBox = new ComboBox { MinWidth = 160, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        topBar.Children.Add(new Label
        {
            Content = "Show formatting _rules for:",
            Target = _scopeBox,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(0, 0, 6, 0)
        });

        _scopeBox.Items.Add(ScopeSheet);
        if (selection.HasValue) _scopeBox.Items.Add(ScopeSelection);
        _scopeBox.SelectedItem = selection.HasValue ? ScopeSelection : ScopeSheet;
        _scopeBox.SelectionChanged += ScopeBox_SelectionChanged;
        topBar.Children.Add(_scopeBox);

        root.Children.Add(topBar);

        // ── Bottom button row ──────────────────────────────────────────────────
        var bottomRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin              = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(bottomRow, Dock.Bottom);

        var okBtn     = new Button { Content = "_OK",     Width = 72, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        var cancelBtn = new Button { Content = "_Cancel", Width = 72, Margin = new Thickness(0, 0, 6, 0), IsCancel = true };
        var applyBtn  = new Button { Content = "_Apply",  Width = 72 };
        okBtn.Click    += OkBtn_Click;
        applyBtn.Click += ApplyBtn_Click;
        bottomRow.Children.Add(okBtn);
        bottomRow.Children.Add(cancelBtn);
        bottomRow.Children.Add(applyBtn);
        root.Children.Add(bottomRow);

        // ── Middle toolbar: New / Edit / Duplicate / Delete / reorder ──────────
        var toolBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(toolBar, Dock.Bottom);

        var newBtn   = new Button { Content = "_New Rule...", Width = 104, Margin = new Thickness(0, 0, 6, 0) };
        _editBtn     = new Button { Content = "_Edit Rule",   Width = 94, Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
        _duplicateBtn = new Button { Content = "D_uplicate Rule", Width = 118, Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
        _deleteBtn   = new Button { Content = "_Delete Rule", Width = 100, Margin = new Thickness(0, 0, 12, 0), IsEnabled = false };
        _moveUpBtn   = new Button { Content = "▲", Width = 32, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Move selected rule up", IsEnabled = false };
        _moveDownBtn = new Button { Content = "▼", Width = 32, ToolTip = "Move selected rule down", IsEnabled = false };
        System.Windows.Automation.AutomationProperties.SetName(_moveUpBtn, "Move Up");
        System.Windows.Automation.AutomationProperties.SetName(_moveDownBtn, "Move Down");

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

        // ── ListView ───────────────────────────────────────────────────────────
        _listView = new ListView
        {
            ItemsSource   = _rules,
            SelectionMode = SelectionMode.Single
        };
        _listView.SelectionChanged += ListView_SelectionChanged;

        var gridView = new GridView();

        // Column 1 — priority number (#)
        var priorityCol = new GridViewColumn
        {
            Header = "#",
            Width  = 30,
            DisplayMemberBinding = new Binding("Priority")
        };
        gridView.Columns.Add(priorityCol);

        // Column 2 — rule description (custom cell template)
        var descCol = new GridViewColumn { Header = "Rule (Type)", Width = 200 };
        var descTemplate = new DataTemplate();
        var descFactory  = new FrameworkElementFactory(typeof(TextBlock));
        descFactory.SetBinding(TextBlock.TextProperty, new Binding(".") { Converter = new RuleDescriptionConverter() });
        descFactory.SetValue(TextBlock.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        descTemplate.VisualTree = descFactory;
        descCol.CellTemplate = descTemplate;
        gridView.Columns.Add(descCol);

        // Column 3 — format preview (colored rectangle)
        var fmtCol = new GridViewColumn { Header = "Format", Width = 95 };
        var fmtTemplate = new DataTemplate();
        var rectFactory  = new FrameworkElementFactory(typeof(Rectangle));
        rectFactory.SetValue(Rectangle.WidthProperty, 78.0);
        rectFactory.SetValue(Rectangle.HeightProperty, 16.0);
        rectFactory.SetValue(Rectangle.MarginProperty, new Thickness(0, 2, 0, 2));
        rectFactory.SetValue(Rectangle.StrokeProperty, Brushes.DarkGray);
        rectFactory.SetValue(Rectangle.StrokeThicknessProperty, 0.5);
        rectFactory.SetBinding(Rectangle.FillProperty, new Binding(".") { Converter = new PreviewBrushConverter() });
        fmtTemplate.VisualTree = rectFactory;
        fmtCol.CellTemplate = fmtTemplate;
        gridView.Columns.Add(fmtCol);

        // Column 4 — AppliesTo range
        var appliesToCol = new GridViewColumn { Header = "Applies To", Width = 170 };
        var appliesToTemplate = new DataTemplate();
        var appliesToPanelFactory = new FrameworkElementFactory(typeof(DockPanel));
        appliesToPanelFactory.SetValue(DockPanel.LastChildFillProperty, true);
        var rangePickerFactory = new FrameworkElementFactory(typeof(Button));
        rangePickerFactory.SetValue(ContentControl.ContentProperty, "...");
        rangePickerFactory.SetValue(FrameworkElement.WidthProperty, 24.0);
        rangePickerFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 0, 0));
        rangePickerFactory.SetValue(FrameworkElement.ToolTipProperty, "Select Applies To range text");
        rangePickerFactory.SetValue(DockPanel.DockProperty, Dock.Right);
        rangePickerFactory.SetBinding(UIElement.IsEnabledProperty, new Binding("IsSelected")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListViewItem), 1)
        });
        rangePickerFactory.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(RangePickerButton_Click));
        var appliesToFactory = new FrameworkElementFactory(typeof(TextBox));
        appliesToFactory.SetValue(Control.PaddingProperty, new Thickness(2, 0, 2, 0));
        appliesToFactory.SetValue(Control.VerticalContentAlignmentProperty, System.Windows.VerticalAlignment.Center);
        appliesToFactory.SetBinding(UIElement.IsEnabledProperty, new Binding("IsSelected")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListViewItem), 1)
        });
        appliesToFactory.SetBinding(TextBox.TextProperty, new Binding(nameof(ConditionalFormat.AppliesTo))
        {
            Converter = new AppliesToRangeConverter(_sheet.Id),
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
        });
        appliesToPanelFactory.AppendChild(rangePickerFactory);
        appliesToPanelFactory.AppendChild(appliesToFactory);
        appliesToTemplate.VisualTree = appliesToPanelFactory;
        appliesToCol.CellTemplate = appliesToTemplate;
        gridView.Columns.Add(appliesToCol);

        // Column 5 - Stop If True
        var stopIfTrueCol = new GridViewColumn { Header = "Stop If True", Width = 85 };
        var stopIfTrueTemplate = new DataTemplate();
        var stopIfTrueFactory  = new FrameworkElementFactory(typeof(CheckBox));
        stopIfTrueFactory.SetValue(CheckBox.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        stopIfTrueFactory.SetValue(CheckBox.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        stopIfTrueFactory.SetBinding(
            System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding(nameof(ConditionalFormat.StopIfTrue))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        stopIfTrueTemplate.VisualTree = stopIfTrueFactory;
        stopIfTrueCol.CellTemplate = stopIfTrueTemplate;
        gridView.Columns.Add(stopIfTrueCol);

        _listView.View = gridView;
        root.Children.Add(_listView);

        Content = root;

        // ── Initial load ───────────────────────────────────────────────────────
        PopulateRules();
    }

    // ── Scope selector ─────────────────────────────────────────────────────────

    private void ScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateRules();
    }

    private void PopulateRules()
    {
        _rules.Clear();
        bool filterToSelection = _selection.HasValue
            && _scopeBox.SelectedItem is string s
            && s == ScopeSelection;

        var source = filterToSelection
            ? _sheet.ConditionalFormats.Where(r => RangesOverlap(r.AppliesTo, _selection!.Value))
            : (IEnumerable<ConditionalFormat>)_sheet.ConditionalFormats;

        var priority = 1;
        foreach (var rule in source)
        {
            // Work on a shallow clone so we don't mutate the live sheet until OK/Apply
            var copy = CloneWithPriority(rule, priority++);
            _rules.Add(copy);
        }
    }

    // ── Toolbar button handlers ────────────────────────────────────────────────

    private void NewRule_Click(object sender, RoutedEventArgs e)
    {
        var defaultRange = _selection ?? _sheet.ConditionalFormats
            .Select(r => (GridRange?)r.AppliesTo)
            .FirstOrDefault() ?? new GridRange(
                new CellAddress(_sheet.Id, 1, 1),
                new CellAddress(_sheet.Id, 1, 1));

        var dlg = new NewConditionalFormatRuleDialog("Greater Than", defaultRange);
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
        if (_listView.SelectedItem is not ConditionalFormat selected) return;

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
        if (_listView.SelectedItem is not ConditionalFormat selected) return;
        _rules.Remove(selected);
        ReassignPriorities();
    }

    private void DuplicateRule_Click(object sender, RoutedEventArgs e)
    {
        if (_listView.SelectedItem is not ConditionalFormat selected) return;

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

    // ── OK / Apply ─────────────────────────────────────────────────────────────

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitResult();
        DialogResult = true;
    }

    private void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitResult();
    }

    private void CommitResult()
    {
        ReassignPriorities();
        ResultRules = BuildResultRules(
            _sheet.ConditionalFormats,
            _selection,
            IsFilteringToSelection(),
            _rules);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void ReassignPriorities()
    {
        // ObservableCollection items are mutable objects — create new copies with updated priority
        for (var i = 0; i < _rules.Count; i++)
        {
            var r = _rules[i];
            if (r.Priority != i + 1)
                _rules[i] = CloneWithPriority(r, i + 1);
        }
    }

    public static IReadOnlyList<ConditionalFormat> BuildResultRules(
        IReadOnlyList<ConditionalFormat> sheetRules,
        GridRange? selection,
        bool filterToSelection,
        IReadOnlyList<ConditionalFormat> editedRules)
    {
        if (!filterToSelection || selection is null)
            return Reprioritize(editedRules);

        var result = new List<ConditionalFormat>();
        var insertedEditedRules = false;

        foreach (var rule in sheetRules)
        {
            if (!RangesOverlap(rule.AppliesTo, selection.Value))
            {
                result.Add(rule);
                continue;
            }

            if (insertedEditedRules)
                continue;

            result.AddRange(editedRules);
            insertedEditedRules = true;
        }

        if (!insertedEditedRules)
            result.AddRange(editedRules);

        return Reprioritize(result);
    }

    private bool IsFilteringToSelection() =>
        _selection.HasValue
        && _scopeBox.SelectedItem is string selectedScope
        && selectedScope == ScopeSelection;

    private static IReadOnlyList<ConditionalFormat> Reprioritize(IReadOnlyList<ConditionalFormat> rules) =>
        rules.Select((rule, index) => CloneWithPriority(rule, index + 1)).ToList();

    private static ConditionalFormat CloneWithPriority(ConditionalFormat src, int priority, Guid? id = null)
    {
        var cf = new ConditionalFormat
        {
            // preserve Id
            Id            = id ?? src.Id,
            AppliesTo     = src.AppliesTo,
            Priority      = priority,
            RuleType      = src.RuleType,
            Operator      = src.Operator,
            Value1        = src.Value1,
            Value2        = src.Value2,
            FormatIfTrue  = src.FormatIfTrue?.Clone(),
            MinColor      = src.MinColor,
            MidColor      = src.MidColor,
            MaxColor      = src.MaxColor,
            UseThreeColorScale = src.UseThreeColorScale,
            MinThresholdType = src.MinThresholdType,
            MinThresholdValue = src.MinThresholdValue,
            MidThresholdType = src.MidThresholdType,
            MidThresholdValue = src.MidThresholdValue,
            MaxThresholdType = src.MaxThresholdType,
            MaxThresholdValue = src.MaxThresholdValue,
            DataBarColor  = src.DataBarColor,
            DataBarMinThresholdType = src.DataBarMinThresholdType,
            DataBarMinThresholdValue = src.DataBarMinThresholdValue,
            DataBarMaxThresholdType = src.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = src.DataBarMaxThresholdValue,
            DataBarShowValue = src.DataBarShowValue,
            DataBarMinLength = src.DataBarMinLength,
            DataBarMaxLength = src.DataBarMaxLength,
            DataBarGradient  = src.DataBarGradient,
            AboveAverage  = src.AboveAverage,
            FormulaText   = src.FormulaText,
            IconSetStyle = src.IconSetStyle,
            IconSetShowValue = src.IconSetShowValue,
            IconSetReverse = src.IconSetReverse,
            TopBottomRank = src.TopBottomRank,
            TopBottomPercent = src.TopBottomPercent,
            TextRuleText = src.TextRuleText,
            DateOccurringPeriod = src.DateOccurringPeriod,
            StopIfTrue    = src.StopIfTrue,
        };
        cf.IconSetThresholds.AddRange(src.IconSetThresholds);
        return cf;
    }

    private static bool RangesOverlap(GridRange a, GridRange b)
    {
        if (a.Start.Sheet != b.Start.Sheet) return false;
        return a.Start.Row <= b.End.Row && a.End.Row >= b.Start.Row
            && a.Start.Col <= b.End.Col && a.End.Col >= b.Start.Col;
    }

    private static void RangePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject current)
            return;

        while (current is not null)
        {
            if (current is DockPanel panel)
            {
                var rangeBox = panel.Children.OfType<TextBox>().FirstOrDefault();
                if (rangeBox is not null)
                {
                    rangeBox.Focus();
                    rangeBox.SelectAll();
                }
                return;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                ?? LogicalTreeHelper.GetParent(current);
        }
    }
}
