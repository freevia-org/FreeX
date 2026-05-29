using System.Windows;
using System.Windows.Controls;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record AllowEditRangeSelectionRequest(
    string CurrentText,
    bool CollapseDialog = true);

public enum AllowEditRangeDialogAction
{
    Add,
    Remove,
    Clear
}

public sealed record AllowEditRangeDialogResult(AllowEditRangeDialogAction Action, GridRange? Range);

public sealed class AllowEditRangeDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _rangeBox = new();
    private readonly ListBox _existingRangesBox = new();
    private readonly Button _deleteRangeButton = new() { Content = "_Delete", Width = 76, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _clearRangesButton = new() { Content = "Clear _All", Width = 76 };
    private readonly Action<AllowEditRangeSelectionRequest>? _requestRangeSelection;

    public GridRange Range { get; private set; }
    public AllowEditRangeDialogResult Result { get; private set; } = CreateClearResult();
    public AllowEditRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public AllowEditRangeDialog(
        SheetId sheetId,
        string defaultRange,
        Action<AllowEditRangeSelectionRequest>? requestRangeSelection)
        : this(sheetId, defaultRange, existingRanges: null, requestRangeSelection)
    {
    }

    public AllowEditRangeDialog(
        SheetId sheetId,
        string defaultRange,
        IReadOnlyList<GridRange>? existingRanges = null,
        Action<AllowEditRangeSelectionRequest>? requestRangeSelection = null)
    {
        _sheetId = sheetId;
        _requestRangeSelection = requestRangeSelection;
        Title = "Allow Users to Edit Ranges";
        Width = 430;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock
        {
            Text = "Specify a worksheet range that can be edited while the sheet is protected.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var existingGroup = new GroupBox { Margin = new Thickness(0, 0, 0, 10) };
        var existingPanel = new DockPanel { Margin = new Thickness(8) };
        _existingRangesBox.ItemsSource = AllowEditRangeDialogPlanner.BuildExistingRangeItems(existingRanges);
        System.Windows.Automation.AutomationProperties.SetName(_existingRangesBox, "Ranges unlocked by password");
        _existingRangesBox.MinHeight = 80;
        _existingRangesBox.SelectionMode = SelectionMode.Single;
        _existingRangesBox.SelectionChanged += (_, _) => UpdateRangeButtons();
        _existingRangesBox.MouseDoubleClick += DeleteSelectedRange_Click;
        var existingRangesLabel = new Label { Content = "_Ranges unlocked by password:", Target = _existingRangesBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(existingRangesLabel, Dock.Top);
        existingPanel.Children.Add(existingRangesLabel);
        existingPanel.Children.Add(_existingRangesBox);
        var rangeButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _deleteRangeButton.Click += DeleteSelectedRange_Click;
        _clearRangesButton.Click += ClearAllRanges_Click;
        rangeButtons.Children.Add(_deleteRangeButton);
        rangeButtons.Children.Add(_clearRangesButton);
        DockPanel.SetDock(rangeButtons, Dock.Bottom);
        existingPanel.Children.Add(rangeButtons);
        existingGroup.Content = existingPanel;
        root.Children.Add(existingGroup);

        var group = new GroupBox { Header = "Range", Margin = new Thickness(0, 0, 0, 10) };
        var rangePanel = new DockPanel { Margin = new Thickness(8) };
        rangePanel.Children.Add(new Label { Content = "_Range:", Target = _rangeBox, Margin = new Thickness(0, 0, 8, 0) });
        var rangePicker = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Collapse dialog and select editable range"
        };
        System.Windows.Automation.AutomationProperties.SetName(rangePicker, "Select editable range");
        System.Windows.Automation.AutomationProperties.SetHelpText(
            rangePicker,
            "Collapse dialog and select the editable range from the worksheet.");
        rangePicker.Click += RangePicker_Click;
        DockPanel.SetDock(rangePicker, Dock.Right);
        rangePanel.Children.Add(rangePicker);
        _rangeBox.Text = defaultRange;
        System.Windows.Automation.AutomationProperties.SetName(_rangeBox, "Editable range");
        rangePanel.Children.Add(_rangeBox);
        group.Content = rangePanel;
        root.Children.Add(group);
        root.Children.Add(new TextBlock
        {
            Text = "Use an A1-style range, for example A1:C10.",
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72));

        Content = root;
        UpdateRangeButtons();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void RangePicker_Click(object sender, RoutedEventArgs e)
    {
        RangeSelectionRequest = CreateRangeSelectionRequest(_rangeBox.Text);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        FocusRangeInput();
    }

    public static AllowEditRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        AllowEditRangeDialogPlanner.CreateRangeSelectionRequest(currentText);

    public void ApplyRangeSelection(string rangeText)
    {
        _rangeBox.Text = rangeText;
        FocusRangeInput();
    }

    public static AllowEditRangeDialogResult CreateAddResult(GridRange range) =>
        AllowEditRangeDialogPlanner.CreateAddResult(range);

    public static AllowEditRangeDialogResult CreateRemoveResult(GridRange range) =>
        AllowEditRangeDialogPlanner.CreateRemoveResult(range);

    public static AllowEditRangeDialogResult CreateClearResult() =>
        AllowEditRangeDialogPlanner.CreateClearResult();

    private void Accept()
    {
        if (!ProtectionDialogPlanner.TryParseAllowEditRange(_rangeBox.Text, _sheetId, out var range))
        {
            DialogMessageHelper.ShowWarning(this, "Enter a valid range.", Title);
            FocusRangeInput();
            return;
        }

        Range = range;
        Result = CreateAddResult(range);
        DialogResult = true;
    }

    private void DeleteSelectedRange_Click(object sender, RoutedEventArgs e)
    {
        if (_existingRangesBox.SelectedItem is not string selected ||
            !ProtectionDialogPlanner.TryParseAllowEditRange(selected, _sheetId, out var range))
            return;

        Range = range;
        Result = CreateRemoveResult(range);
        DialogResult = true;
    }

    private void ClearAllRanges_Click(object sender, RoutedEventArgs e)
    {
        Result = CreateClearResult();
        DialogResult = true;
    }

    private void UpdateRangeButtons()
    {
        var state = AllowEditRangeDialogPlanner.BuildButtonState(
            _existingRangesBox.Items.Count,
            _existingRangesBox.SelectedItem is not null);
        _deleteRangeButton.IsEnabled = state.CanDeleteSelectedRange;
        _clearRangesButton.IsEnabled = state.CanClearRanges;
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusRangeInput();
    }

    private void FocusRangeInput()
    {
        DialogFocus.FocusAndSelect(_rangeBox);
    }
}
