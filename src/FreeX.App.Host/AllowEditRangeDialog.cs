using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record AllowEditRangeSelectionRequest(
    string CurrentText,
    bool CollapseDialog = true);

public enum AllowEditRangeDialogAction
{
    Add,
    Modify,
    Remove,
    Clear
}

public sealed record AllowEditRangeDialogResult(AllowEditRangeDialogAction Action, GridRange? Range, GridRange? PreviousRange = null);

public sealed class AllowEditRangeDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _rangeBox = new();
    private readonly ListBox _existingRangesBox = new();
    private readonly Button _newRangeButton = new() { Content = UiText.Get("AllowEditRange_NewButton"), Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _modifyRangeButton = new() { Content = UiText.Get("AllowEditRange_ModifyButton"), Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _deleteRangeButton = new() { Content = UiText.Get("AllowEditRange_DeleteButton"), Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _permissionsButton = new() { Content = UiText.Get("AllowEditRange_PermissionsButton"), Width = 104 };
    private readonly Action<AllowEditRangeSelectionRequest>? _requestRangeSelection;
    private GridRange? _rangeBeingModified;

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
        Title = UiText.Get("AllowEditRange_Title");
        Width = 430;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        // Layout mirrors the Avalonia/Linux shell (the agreed parity target): a single vertical flow of
        // intro text, a "Ranges" group whose New/Modify/Delete/Permissions buttons sit UNDER the list,
        // then the range label + box stacked vertically, the example hint, and an [OK][Cancel] row.
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("AllowEditRange_Intro"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _existingRangesBox.ItemsSource = AllowEditRangeDialogPlanner.BuildExistingRangeItems(existingRanges);
        AutomationProperties.SetName(_existingRangesBox, UiText.Get("AllowEditRange_ExistingRangesAutomationName"));
        AutomationProperties.SetAutomationId(_existingRangesBox, "AllowEditRangeExistingRangesList");
        AutomationProperties.SetHelpText(_existingRangesBox, UiText.Get("AllowEditRange_ExistingRangesHelpText"));
        _existingRangesBox.MinHeight = 80;
        _existingRangesBox.SelectionMode = SelectionMode.Single;
        _existingRangesBox.SelectionChanged += ExistingRangesBox_SelectionChanged;
        _existingRangesBox.MouseDoubleClick += ExistingRangesBox_MouseDoubleClick;

        _newRangeButton.Click += NewRange_Click;
        _modifyRangeButton.Click += ModifySelectedRange_Click;
        _deleteRangeButton.Click += DeleteSelectedRange_Click;
        _permissionsButton.IsEnabled = false;
        AutomationProperties.SetName(_newRangeButton, UiText.Get("AllowEditRange_NewAutomationName"));
        AutomationProperties.SetAutomationId(_newRangeButton, "AllowEditRangeNewButton");
        AutomationProperties.SetHelpText(_newRangeButton, UiText.Get("AllowEditRange_NewHelpText"));
        AutomationProperties.SetName(_modifyRangeButton, UiText.Get("AllowEditRange_ModifyAutomationName"));
        AutomationProperties.SetAutomationId(_modifyRangeButton, "AllowEditRangeModifyButton");
        AutomationProperties.SetHelpText(_modifyRangeButton, UiText.Get("AllowEditRange_ModifyHelpText"));
        AutomationProperties.SetName(_deleteRangeButton, UiText.Get("AllowEditRange_DeleteAutomationName"));
        AutomationProperties.SetAutomationId(_deleteRangeButton, "AllowEditRangeDeleteButton");
        AutomationProperties.SetHelpText(_deleteRangeButton, UiText.Get("AllowEditRange_DeleteHelpText"));
        AutomationProperties.SetName(_permissionsButton, UiText.Get("AllowEditRange_PermissionsAutomationName"));
        AutomationProperties.SetAutomationId(_permissionsButton, "AllowEditRangePermissionsButton");
        AutomationProperties.SetHelpText(_permissionsButton, UiText.Get("AllowEditRange_PermissionsHelpText"));

        // New/Modify/Delete/Permissions sit in a left-aligned row directly under the list.
        var rangeButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        rangeButtons.Children.Add(_newRangeButton);
        rangeButtons.Children.Add(_modifyRangeButton);
        rangeButtons.Children.Add(_deleteRangeButton);
        rangeButtons.Children.Add(_permissionsButton);

        var existingPanel = new StackPanel { Margin = new Thickness(8) };
        existingPanel.Children.Add(_existingRangesBox);
        existingPanel.Children.Add(rangeButtons);
        var existingGroup = new GroupBox
        {
            Header = UiText.Get("AllowEditRange_ExistingRangesLabel"),
            Content = existingPanel,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(existingGroup);

        // Range label + box stacked vertically (no nested "Range" group box, no inline "..." picker),
        // matching the Linux layout.
        _rangeBox.Text = defaultRange;
        AutomationProperties.SetName(_rangeBox, UiText.Get("AllowEditRange_RangeAutomationName"));
        AutomationProperties.SetAutomationId(_rangeBox, "AllowEditRangeBox");
        AutomationProperties.SetHelpText(_rangeBox, UiText.Get("AllowEditRange_RangeHelpText"));
        root.Children.Add(new Label
        {
            Content = UiText.Get("AllowEditRange_RangeLabel"),
            Target = _rangeBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(_rangeBox);
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("AllowEditRange_Example"),
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 6, 0, 10)
        });
        root.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 84));

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

    public static AllowEditRangeDialogResult CreateModifyResult(GridRange originalRange, GridRange updatedRange) =>
        AllowEditRangeDialogPlanner.CreateModifyResult(originalRange, updatedRange);

    public static AllowEditRangeDialogResult CreateRemoveResult(GridRange range) =>
        AllowEditRangeDialogPlanner.CreateRemoveResult(range);

    public static AllowEditRangeDialogResult CreateClearResult() =>
        AllowEditRangeDialogPlanner.CreateClearResult();

    private void Accept()
    {
        if (!ProtectionDialogPlanner.TryParseAllowEditRange(_rangeBox.Text, _sheetId, out var range))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("AllowEditRange_InvalidRangeMessage"), Title);
            FocusRangeInput();
            return;
        }

        Range = range;
        Result = _rangeBeingModified is { } originalRange
            ? CreateModifyResult(originalRange, range)
            : CreateAddResult(range);
        DialogResult = true;
    }

    private void NewRange_Click(object sender, RoutedEventArgs e)
    {
        _rangeBeingModified = null;
        _existingRangesBox.SelectedItem = null;
        FocusRangeInput();
    }

    private void ModifySelectedRange_Click(object sender, RoutedEventArgs e)
        => TryLoadSelectedRangeForModification();

    private void DeleteSelectedRange_Click(object sender, RoutedEventArgs e)
        => TryDeleteSelectedRange();

    private bool TryLoadSelectedRangeForModification()
    {
        if (_existingRangesBox.SelectedItem is not string selected ||
            !ProtectionDialogPlanner.TryParseAllowEditRange(selected, _sheetId, out var range))
            return false;

        _rangeBeingModified = range;
        _rangeBox.Text = selected;
        FocusRangeInput();
        return true;
    }

    private bool TryDeleteSelectedRange()
    {
        if (_existingRangesBox.SelectedItem is not string selected ||
            !ProtectionDialogPlanner.TryParseAllowEditRange(selected, _sheetId, out var range))
            return false;

        Range = range;
        Result = CreateRemoveResult(range);
        DialogResult = true;
        return true;
    }

    private void ExistingRangesBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryLoadSelectedRangeForModification())
            e.Handled = true;
    }

    private void ExistingRangesBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rangeBeingModified is not null &&
            (_existingRangesBox.SelectedItem is not string selected ||
             !ProtectionDialogPlanner.TryParseAllowEditRange(selected, _sheetId, out var selectedRange) ||
             selectedRange != _rangeBeingModified))
        {
            _rangeBeingModified = null;
        }

        UpdateRangeButtons();
    }

    private void UpdateRangeButtons()
    {
        var state = AllowEditRangeDialogPlanner.BuildButtonState(
            _existingRangesBox.Items.Count,
            _existingRangesBox.SelectedItem is not null);
        _modifyRangeButton.IsEnabled = state.CanModifySelectedRange;
        _deleteRangeButton.IsEnabled = state.CanDeleteSelectedRange;
        _permissionsButton.IsEnabled = state.CanUsePermissions;
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
