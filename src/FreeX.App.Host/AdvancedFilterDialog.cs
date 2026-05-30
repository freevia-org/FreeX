using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record AdvancedFilterDialogResult(
    GridRange ListRange,
    GridRange CriteriaRange,
    CellAddress? CopyToCell,
    bool UniqueRecordsOnly,
    GridRange? CopyToRange = null);

public enum AdvancedFilterRangeSelectionTarget
{
    ListRange,
    CriteriaRange,
    CopyTo
}

public sealed record AdvancedFilterRangeSelectionRequest(
    AdvancedFilterRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

public sealed partial class AdvancedFilterDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly Func<string, SheetId?> _resolveSheetId;
    private readonly Action<AdvancedFilterRangeSelectionRequest>? _requestRangeSelection;
    private readonly TextBox _listRangeBox = new();
    private readonly TextBox _criteriaRangeBox = new();
    private readonly TextBox _copyToBox = new();
    private readonly DockPanel _copyToEditor;
    private readonly Label _copyToLabel;
    private readonly RadioButton _filterInPlaceButton = new() { Content = UiText.Get("AdvancedFilter_FilterTheListInPlace"), IsChecked = true };
    private readonly RadioButton _copyToAnotherLocationButton = new() { Content = UiText.Get("AdvancedFilter_CopyToAnotherLocation") };
    private readonly CheckBox _uniqueBox = new() { Content = UiText.Get("AdvancedFilter_UniqueRecordsOnly") };
    private readonly TextBlock _copyToHint = new()
    {
        Text = UiText.Get("AdvancedFilter_CopyToIsAvailableWhenCopyToAnotherLocationIsSelected"),
        TextWrapping = TextWrapping.Wrap,
        Foreground = SystemColors.GrayTextBrush,
        Margin = new Thickness(0, 2, 0, 0)
    };

    public AdvancedFilterDialogResult? Result { get; private set; }
    public AdvancedFilterRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public AdvancedFilterDialog(
        SheetId sheetId,
        string defaultListRange,
        Func<string, SheetId?>? resolveSheetId = null,
        Action<AdvancedFilterRangeSelectionRequest>? requestRangeSelection = null)
    {
        _sheetId = sheetId;
        _resolveSheetId = resolveSheetId ?? (_ => null);
        _requestRangeSelection = requestRangeSelection;
        _copyToEditor = CreateReferenceEditor(_copyToBox, UiText.Get("AdvancedFilter_SelectCopyToCell"), AdvancedFilterRangeSelectionTarget.CopyTo);
        Title = UiText.Get("AdvancedFilter_AdvancedFilter");
        Width = 420;
        Height = 340;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _listRangeBox.Text = defaultListRange;
        AutomationProperties.SetName(_listRangeBox, UiText.Get("AdvancedFilter_ListRange"));
        AutomationProperties.SetAutomationId(_listRangeBox, "AdvancedFilterListRangeBox");
        AutomationProperties.SetHelpText(_listRangeBox, UiText.Get("AdvancedFilter_EnterTheListRangeToFilterIncludingColumnLabels"));
        AutomationProperties.SetName(_criteriaRangeBox, UiText.Get("AdvancedFilter_CriteriaRange"));
        AutomationProperties.SetAutomationId(_criteriaRangeBox, "AdvancedFilterCriteriaRangeBox");
        AutomationProperties.SetHelpText(_criteriaRangeBox, UiText.Get("AdvancedFilter_EnterTheCriteriaRangeIncludingCriteriaLabels"));
        AutomationProperties.SetName(_copyToBox, UiText.Get("AdvancedFilter_CopyTo"));
        AutomationProperties.SetAutomationId(_copyToBox, "AdvancedFilterCopyToBox");
        AutomationProperties.SetHelpText(_copyToBox, UiText.Get("AdvancedFilter_EnterTheDestinationCellOrOneRowHeaderRangeWhenCopyingFilteredRecords"));
        ApplyAutomationMetadata();
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(root, Dock.Top);

        var content = new StackPanel();
        root.Children.Add(content);

        var actionGroup = new GroupBox { Header = UiText.Get("AdvancedFilter_Action"), Margin = new Thickness(0, 0, 0, 10) };
        var actionPanel = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };
        _filterInPlaceButton.Margin = new Thickness(0, 0, 0, 4);
        _copyToAnotherLocationButton.Margin = new Thickness(0, 0, 0, 0);
        _filterInPlaceButton.Checked += (_, _) => UpdateCopyToState();
        _copyToAnotherLocationButton.Checked += (_, _) => UpdateCopyToState();
        actionPanel.Children.Add(_filterInPlaceButton);
        actionPanel.Children.Add(_copyToAnotherLocationButton);
        actionGroup.Content = actionPanel;
        content.Children.Add(actionGroup);

        var rangesGrid = new Grid();
        rangesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rangesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddReferenceRow(rangesGrid, 0, UiText.Get("AdvancedFilter_ListRange2"), _listRangeBox, UiText.Get("AdvancedFilter_SelectListRange"), AdvancedFilterRangeSelectionTarget.ListRange);
        AddReferenceRow(rangesGrid, 1, UiText.Get("AdvancedFilter_CriteriaRange2"), _criteriaRangeBox, UiText.Get("AdvancedFilter_SelectCriteriaRange"), AdvancedFilterRangeSelectionTarget.CriteriaRange);
        _copyToLabel = AddReferenceRow(rangesGrid, 2, UiText.Get("AdvancedFilter_CopyTo2"), _copyToBox, UiText.Get("AdvancedFilter_SelectCopyToCell"), AdvancedFilterRangeSelectionTarget.CopyTo, _copyToEditor);
        content.Children.Add(rangesGrid);
        content.Children.Add(_copyToHint);

        _uniqueBox.Margin = new Thickness(0, 10, 0, 0);
        content.Children.Add(_uniqueBox);
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("AdvancedFilter_CriteriaShouldIncludeColumnLabelsInTheFirstRowMatchingExcelAdvancedFilte"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 10, 0, 0)
        });
        content.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 76, rowMargin: new Thickness(0, 14, 0, 0)));
        Content = root;
        UpdateCopyToState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void ApplyAutomationMetadata()
    {
        AutomationProperties.SetName(_filterInPlaceButton, UiText.Get("AdvancedFilter_FilterTheListInPlaceAutomationName"));
        AutomationProperties.SetAutomationId(_filterInPlaceButton, "AdvancedFilterInPlaceButton");
        AutomationProperties.SetHelpText(_filterInPlaceButton, UiText.Get("AdvancedFilter_FilterTheListInItsCurrentLocation"));

        AutomationProperties.SetName(_copyToAnotherLocationButton, UiText.Get("AdvancedFilter_CopyToAnotherLocationAutomationName"));
        AutomationProperties.SetAutomationId(_copyToAnotherLocationButton, "AdvancedFilterCopyToAnotherLocationButton");
        AutomationProperties.SetHelpText(_copyToAnotherLocationButton, UiText.Get("AdvancedFilter_CopyFilteredRecordsToTheCopyToDestination"));

        AutomationProperties.SetName(_uniqueBox, UiText.Get("AdvancedFilter_UniqueRecordsOnlyAutomationName"));
        AutomationProperties.SetAutomationId(_uniqueBox, "AdvancedFilterUniqueRecordsOnlyBox");
        AutomationProperties.SetHelpText(_uniqueBox, UiText.Get("AdvancedFilter_ShowOrCopyOnlyUniqueRecords"));
    }

    private DockPanel CreateReferenceEditor(
        TextBox textBox,
        string automationName,
        AdvancedFilterRangeSelectionTarget target) =>
        DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            requestSelection: request => RequestRangeSelection(target, request));

    private Label AddReferenceRow(
        Grid grid,
        int row,
        string label,
        TextBox textBox,
        string automationName,
        AdvancedFilterRangeSelectionTarget target,
        DockPanel? editor = null)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelBlock = new Label
        {
            Content = label,
            Target = textBox,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, row == 0 ? 0 : 8, 8, 0)
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        var rowEditor = editor ?? CreateReferenceEditor(textBox, automationName, target);
        rowEditor.Margin = new Thickness(0, row == 0 ? 0 : 8, 0, 0);
        Grid.SetRow(rowEditor, row);
        Grid.SetColumn(rowEditor, 1);
        grid.Children.Add(rowEditor);
        return labelBlock;
    }

    private void RequestRangeSelection(AdvancedFilterRangeSelectionTarget target, DialogReferencePickerRequest request)
    {
        RangeSelectionRequest = CreateRangeSelectionRequest(target, request.CurrentText);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        FocusRangeSelectionInput(request.Target);
    }

    public void ApplyRangeSelection(AdvancedFilterRangeSelectionTarget target, string rangeText)
    {
        var textBox = target switch
        {
            AdvancedFilterRangeSelectionTarget.CriteriaRange => _criteriaRangeBox,
            AdvancedFilterRangeSelectionTarget.CopyTo => _copyToBox,
            _ => _listRangeBox
        };

        textBox.Text = rangeText;
        if (target == AdvancedFilterRangeSelectionTarget.CopyTo)
        {
            _copyToAnotherLocationButton.IsChecked = true;
            UpdateCopyToState();
        }

        FocusRangeSelectionInput(textBox);
    }

    private static void FocusRangeSelectionInput(TextBox target)
    {
        DialogFocus.FocusAndSelect(target);
    }

    private void FocusInitialKeyboardTarget()
    {
        _filterInPlaceButton.Focus();
        Keyboard.Focus(_filterInPlaceButton);
    }

    private void UpdateCopyToState()
    {
        var isCopyToEnabled = _copyToAnotherLocationButton.IsChecked == true;
        _copyToLabel.IsEnabled = isCopyToEnabled;
        _copyToEditor.IsEnabled = isCopyToEnabled;
        _copyToBox.IsEnabled = isCopyToEnabled;
        _copyToHint.Visibility = _copyToAnotherLocationButton.IsChecked == true
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void FocusInvalidRangeInput(string? error)
    {
        TextBox target;
        if (string.Equals(error, UiText.Get("AdvancedFilter_EnterValidCriteriaRange"), StringComparison.Ordinal))
        {
            target = _criteriaRangeBox;
        }
        else if (string.Equals(error, UiText.Get("AdvancedFilter_CriteriaRangeMustIncludeHeaders"), StringComparison.Ordinal))
        {
            target = _criteriaRangeBox;
        }
        else if (string.Equals(error, UiText.Get("AdvancedFilter_EnterValidCopyToRange"), StringComparison.Ordinal))
        {
            _copyToAnotherLocationButton.IsChecked = true;
            UpdateCopyToState();
            target = _copyToBox;
        }
        else
        {
            target = _listRangeBox;
        }

        DialogFocus.FocusAndSelect(target);
    }

    private void Accept()
    {
        if (!TryParse(
                _sheetId,
                _listRangeBox.Text,
                _criteriaRangeBox.Text,
                _copyToBox.Text,
                _copyToAnotherLocationButton.IsChecked == true,
                _uniqueBox.IsChecked == true,
                _resolveSheetId,
                out var result,
                out var error))
        {
            DialogMessageHelper.ShowWarning(this, error ?? UiText.Get("AdvancedFilter_EnterValidFilterRanges"), Title);
            FocusInvalidRangeInput(error);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
