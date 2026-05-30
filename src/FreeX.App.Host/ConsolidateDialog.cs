using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record ConsolidateDialogResult(
    IReadOnlyList<GridRange> SourceRanges,
    CellAddress DestinationCell,
    ConsolidateFunction Function,
    bool UseTopRowLabels = false,
    bool UseLeftColumnLabels = false,
    bool CreateLinksToSourceData = false);

public enum ConsolidateRangeSelectionTarget
{
    Reference,
    DestinationCell
}

public sealed record ConsolidateRangeSelectionRequest(
    ConsolidateRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

public sealed partial class ConsolidateDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly ComboBox _functionBox = new();
    private readonly TextBox _referenceBox = new();
    private readonly ListBox _referencesList = new() { Height = 72 };
    private readonly Button _deleteReferenceButton = new() { Content = UiText.Get("Consolidate_Delete"), Width = 76, IsEnabled = false };
    private readonly TextBox _destinationBox = new();
    private readonly CheckBox _topRowBox = new() { Content = UiText.Get("Consolidate_TopRow") };
    private readonly CheckBox _leftColumnBox = new() { Content = UiText.Get("Consolidate_LeftColumn") };
    private readonly CheckBox _createLinksBox = new() { Content = UiText.Get("Consolidate_CreateLinksToSourceData") };
    private readonly Action<ConsolidateRangeSelectionRequest>? _requestRangeSelection;

    public ConsolidateDialogResult? Result { get; private set; }
    public ConsolidateRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public ConsolidateDialog(
        SheetId sheetId,
        string defaultSource,
        string defaultDestination,
        Action<ConsolidateRangeSelectionRequest>? requestRangeSelection = null)
    {
        _sheetId = sheetId;
        _requestRangeSelection = requestRangeSelection;
        Title = UiText.Get("Consolidate_Consolidate");
        Width = 380;
        Height = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _referenceBox.Text = defaultSource;
        AutomationProperties.SetName(_referenceBox, UiText.Get("Consolidate_Reference2"));
        AutomationProperties.SetAutomationId(_referenceBox, "ConsolidateReferenceBox");
        AutomationProperties.SetHelpText(_referenceBox, UiText.Get("Consolidate_EnterASourceRangeToAddToTheAllReferencesList"));
        foreach (var sourceRange in SplitSourceRangeText(defaultSource))
            _referencesList.Items.Add(sourceRange);
        AutomationProperties.SetName(_referencesList, UiText.Get("Consolidate_AllReferences2"));
        AutomationProperties.SetAutomationId(_referencesList, "ConsolidateAllReferencesList");
        AutomationProperties.SetHelpText(_referencesList, UiText.Get("Consolidate_ListsTheSourceRangesThatWillBeConsolidated"));
        _referencesList.SelectionChanged += (_, _) => UpdateReferenceButtons();
        _referencesList.KeyDown += ReferencesList_KeyDown;

        _destinationBox.Text = defaultDestination;
        AutomationProperties.SetName(_destinationBox, UiText.Get("Consolidate_DestinationCell2"));
        AutomationProperties.SetAutomationId(_destinationBox, "ConsolidateDestinationCellBox");
        AutomationProperties.SetHelpText(_destinationBox, UiText.Get("Consolidate_EnterTheUpperLeftDestinationCellForTheConsolidatedResult"));
        ApplyAutomationMetadata();
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new Label { Content = UiText.Get("Consolidate_Function"), Target = _functionBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 2) });
        foreach (var function in Enum.GetValues<ConsolidateFunction>())
            _functionBox.Items.Add(new ComboBoxItem { Content = FunctionLabel(function), Tag = function });
        _functionBox.SelectedIndex = 0;
        _functionBox.Margin = new Thickness(0, 0, 0, 8);
        root.Children.Add(_functionBox);
        root.Children.Add(new Label { Content = UiText.Get("Consolidate_Reference"), Target = _referenceBox, Padding = new Thickness(0) });
        root.Children.Add(CreateReferenceEditor(_referenceBox, UiText.Get("Consolidate_SelectReferenceRange"), ConsolidateRangeSelectionTarget.Reference));
        var referenceButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 8)
        };
        var addReferenceButton = new Button { Content = UiText.Get("Consolidate_Add"), Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(addReferenceButton, UiText.Get("Consolidate_AddReferenceAutomationName"));
        AutomationProperties.SetAutomationId(addReferenceButton, "ConsolidateAddReferenceButton");
        AutomationProperties.SetHelpText(addReferenceButton, UiText.Get("Consolidate_AddTheReferenceRangeToTheAllReferencesList"));
        addReferenceButton.Click += AddReferenceButton_Click;
        AutomationProperties.SetName(_deleteReferenceButton, UiText.Get("Consolidate_DeleteReferenceAutomationName"));
        AutomationProperties.SetAutomationId(_deleteReferenceButton, "ConsolidateDeleteReferenceButton");
        AutomationProperties.SetHelpText(_deleteReferenceButton, UiText.Get("Consolidate_DeleteTheSelectedReferenceRange"));
        _deleteReferenceButton.Click += DeleteReferenceButton_Click;
        referenceButtons.Children.Add(addReferenceButton);
        referenceButtons.Children.Add(_deleteReferenceButton);
        root.Children.Add(referenceButtons);
        root.Children.Add(new Label { Content = UiText.Get("Consolidate_AllReferences"), Target = _referencesList, Padding = new Thickness(0) });
        root.Children.Add(_referencesList);
        root.Children.Add(new Label { Content = UiText.Get("Consolidate_DestinationCell"), Target = _destinationBox, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(CreateReferenceEditor(_destinationBox, UiText.Get("Consolidate_SelectDestinationCell"), ConsolidateRangeSelectionTarget.DestinationCell));
        root.Children.Add(new TextBlock { Text = UiText.Get("Consolidate_UseLabelsIn"), Margin = new Thickness(0, 8, 0, 2) });
        var labelOptions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _topRowBox.Margin = new Thickness(0, 0, 16, 0);
        labelOptions.Children.Add(_topRowBox);
        labelOptions.Children.Add(_leftColumnBox);
        root.Children.Add(labelOptions);
        _createLinksBox.Margin = new Thickness(0, 0, 0, 12);
        _createLinksBox.ToolTip = UiText.Get("Consolidate_WriteFormulasThatReferenceTheSourceCellsWhileKeepingTheConsolidatedResul");
        root.Children.Add(_createLinksBox);
        root.Children.Add(TextToColumnsDialog.CreateButtonRow(Accept));
        Content = root;
        UpdateReferenceButtons();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void ApplyAutomationMetadata()
    {
        AutomationProperties.SetName(_functionBox, UiText.Get("Consolidate_FunctionAutomationName"));
        AutomationProperties.SetAutomationId(_functionBox, "ConsolidateFunctionBox");
        AutomationProperties.SetHelpText(_functionBox, UiText.Get("Consolidate_ChooseTheFunctionUsedToCombineSourceRanges"));

        AutomationProperties.SetName(_topRowBox, UiText.Get("Consolidate_TopRowLabelsAutomationName"));
        AutomationProperties.SetAutomationId(_topRowBox, "ConsolidateTopRowLabelsBox");
        AutomationProperties.SetHelpText(_topRowBox, UiText.Get("Consolidate_UseLabelsFromTheTopRowOfEachSourceRange"));

        AutomationProperties.SetName(_leftColumnBox, UiText.Get("Consolidate_LeftColumnLabelsAutomationName"));
        AutomationProperties.SetAutomationId(_leftColumnBox, "ConsolidateLeftColumnLabelsBox");
        AutomationProperties.SetHelpText(_leftColumnBox, UiText.Get("Consolidate_UseLabelsFromTheLeftColumnOfEachSourceRange"));

        AutomationProperties.SetName(_createLinksBox, UiText.Get("Consolidate_CreateLinksToSourceDataAutomationName"));
        AutomationProperties.SetAutomationId(_createLinksBox, "ConsolidateCreateLinksBox");
        AutomationProperties.SetHelpText(_createLinksBox, UiText.Get("Consolidate_CreateFormulasThatLinkTheResultToTheSourceCells"));
    }

    private DockPanel CreateReferenceEditor(
        TextBox textBox,
        string automationName,
        ConsolidateRangeSelectionTarget target) =>
        DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            requestSelection: request => RequestRangeSelection(target, request));

    private void RequestRangeSelection(ConsolidateRangeSelectionTarget target, DialogReferencePickerRequest request)
    {
        RangeSelectionRequest = CreateRangeSelectionRequest(target, request.CurrentText);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        FocusRangeSelectionInput(request.Target);
    }

    public void ApplyRangeSelection(ConsolidateRangeSelectionTarget target, string rangeText)
    {
        var textBox = target == ConsolidateRangeSelectionTarget.DestinationCell
            ? _destinationBox
            : _referenceBox;
        textBox.Text = rangeText;
        FocusRangeSelectionInput(textBox);
    }

    private static void FocusRangeSelectionInput(TextBox target)
    {
        DialogFocus.FocusAndSelect(target);
    }

    private void FocusInitialKeyboardTarget()
    {
        _functionBox.Focus();
        Keyboard.Focus(_functionBox);
    }

    private void AddReferenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryAddReference(
                _sheetId,
                _referencesList.Items.Cast<string>(),
                _referenceBox.Text,
                out var references,
                out var error))
        {
            DialogMessageHelper.ShowWarning(this, error ?? UiText.Get("Consolidate_EnterAValidSourceRange"), Title);
            FocusReferenceInput();
            return;
        }

        _referencesList.Items.Clear();
        foreach (var reference in references)
            _referencesList.Items.Add(reference);
        _referenceBox.Clear();
    }

    private void DeleteReferenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_referencesList.SelectedItem is { } selected)
            _referencesList.Items.Remove(selected);
        UpdateReferenceButtons();
    }

    private void ReferencesList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            DeleteReferenceButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void UpdateReferenceButtons() =>
        _deleteReferenceButton.IsEnabled = _referencesList.SelectedItem is not null;

    private void Accept()
    {
        if (HasPendingReferenceText(_referencesList.Items.Cast<string>(), _referenceBox.Text))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("Consolidate_AddTheReferenceBeforeClickingOk"), Title);
            FocusPendingReferenceInput();
            return;
        }

        var sourceRangesText = JoinSourceRanges(_referencesList.Items.Cast<string>());
        if (!TryParse(
                _sheetId,
                sourceRangesText,
                _destinationBox.Text,
                SelectedFunction(),
                _topRowBox.IsChecked == true,
                _leftColumnBox.IsChecked == true,
                _createLinksBox.IsChecked == true,
                out var result,
                out var error))
        {
            DialogMessageHelper.ShowWarning(this, error ?? UiText.Get("Consolidate_EnterValidConsolidationRanges"), Title);
            FocusInvalidFinalValidation(error);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void FocusInvalidFinalValidation(string? error)
    {
        if (string.Equals(error, UiText.Get("Consolidate_EnterValidDestinationCell"), StringComparison.Ordinal))
        {
            FocusDestinationInput();
            return;
        }

        FocusReferenceInput();
    }

    private void FocusReferenceInput()
    {
        if (_referencesList.Items.Count > 0)
        {
            _referencesList.Focus();
            Keyboard.Focus(_referencesList);
            return;
        }

        DialogFocus.FocusAndSelect(_referenceBox);
    }

    private void FocusPendingReferenceInput()
    {
        DialogFocus.FocusAndSelect(_referenceBox);
    }

    private void FocusDestinationInput()
    {
        DialogFocus.FocusAndSelect(_destinationBox);
    }

    private ConsolidateFunction SelectedFunction() =>
        _functionBox.SelectedItem is ComboBoxItem { Tag: ConsolidateFunction function }
            ? function
            : ConsolidateFunction.Sum;

}
