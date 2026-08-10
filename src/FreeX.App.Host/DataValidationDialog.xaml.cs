using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// Dialog for creating or editing a data validation rule.
/// </summary>
public partial class DataValidationDialog : Window
{
    /// <summary>Set to the resulting rule when the user clicks OK.</summary>
    public DataValidation? Result { get; private set; }
    public bool Accepted { get; private set; }
    public string? LastValidationError { get; private set; }
    public bool ClearRequested { get; private set; }
    public bool ApplyToSameSettings { get; private set; }
    public string? SelectionSource
    {
        get => _selectionSource;
        set
        {
            _selectionSource = value;
            UpdateVisibility();
        }
    }
    public DataValidationRangeSelectionRequest? RangeSelectionRequest { get; private set; }
    private readonly Guid _resultId = Guid.NewGuid();
    private readonly Action<DataValidationRangeSelectionRequest>? _requestRangeSelection;
    private string? _selectionSource;

    // Native/passthrough data captured from the rule being edited (e.g. imeMode and other
    // unmodeled attributes/child XML preserved on load, or the x14 extLst flag). The dialog's UI
    // controls have no editors for these, so they must be carried through untouched rather than
    // silently dropped when the user clicks OK. See DataValidationRuleEditorInput's doc comments.
    private bool _existingIsX14;
    private IReadOnlyDictionary<string, string>? _existingNativeAttributes;
    private IReadOnlyList<string>? _existingNativeChildXmls;
    private IReadOnlyDictionary<string, string>? _existingNativeContainerAttributes;
    private IReadOnlyList<string>? _existingNativeContainerChildXmls;

    public DataValidationDialog(Action<DataValidationRangeSelectionRequest>? requestRangeSelection = null)
    {
        _requestRangeSelection = requestRangeSelection;
        InitializeComponent();
        TypeCombo.ItemsSource = DataValidationDialogPlanner.CreateTypeChoices(UiText.Get)
            .Select(choice => new ComboBoxItem
            {
                Content = choice.Label,
                Tag = DataValidationDialogPlanner.TypeTag(choice.Type)
            });
        OperatorCombo.ItemsSource = DataValidationDialogPlanner.CreateOperatorChoices(UiText.Get)
            .Select(choice => new ComboBoxItem
            {
                Content = choice.Label,
                Tag = DataValidationDialogPlanner.OperatorTag(choice.Operator)
            });
        AlertStyleCombo.ItemsSource = DataValidationDialogPlanner.CreateAlertStyleChoices(UiText.Get)
            .Select(choice => new ComboBoxItem
            {
                Content = choice.Label,
                Tag = DataValidationDialogPlanner.AlertStyleTag(choice.AlertStyle)
            });
        ShowInputMessageBox.Checked += (_, _) => UpdateMessageEditorStates();
        ShowInputMessageBox.Unchecked += (_, _) => UpdateMessageEditorStates();
        ShowErrorMessageBox.Checked += (_, _) => UpdateMessageEditorStates();
        ShowErrorMessageBox.Unchecked += (_, _) => UpdateMessageEditorStates();
        ResetToDefaults(markClearRequested: false);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public DataValidationDialog(DataValidation? existing, Action<DataValidationRangeSelectionRequest>? requestRangeSelection = null)
        : this(requestRangeSelection)
    {
        if (existing is null)
            return;

        _resultId = existing.Id;
        _existingIsX14 = existing.IsX14;
        _existingNativeAttributes = existing.NativeAttributes;
        _existingNativeChildXmls = existing.NativeChildXmls;
        _existingNativeContainerAttributes = existing.NativeContainerAttributes;
        _existingNativeContainerChildXmls = existing.NativeContainerChildXmls;
        SelectComboItemByTag(TypeCombo, DataValidationDialogPlanner.TypeTag(existing.Type));
        SelectComboItemByTag(OperatorCombo, DataValidationDialogPlanner.OperatorTag(existing.Operator));
        SelectComboItemByTag(AlertStyleCombo, DataValidationDialogPlanner.AlertStyleTag(existing.AlertStyle));
        Formula1Box.Text = existing.Formula1 ?? "";
        Formula2Box.Text = existing.Formula2 ?? "";
        AllowBlankBox.IsChecked = existing.AllowBlank;
        ShowDropdownBox.IsChecked = existing.ShowDropdown;
        ShowInputMessageBox.IsChecked = existing.ShowInputMessage;
        ShowErrorMessageBox.IsChecked = existing.ShowErrorMessage;
        ErrorTitleBox.Text = existing.ErrorTitle ?? "";
        PromptTitleBox.Text = existing.PromptTitle ?? "";
        PromptMessageBox.Text = existing.PromptMessage ?? "";
        ErrorMessageBox.Text = existing.ErrorMessage ?? "";
        UpdateVisibility();
        UpdateMessageEditorStates();
    }

    private void ResetToDefaults(bool markClearRequested)
    {
        TypeCombo.SelectedIndex = 0;
        OperatorCombo.SelectedIndex = 0;
        AlertStyleCombo.SelectedIndex = 0;
        Formula1Box.Text = "";
        Formula2Box.Text = "";
        AllowBlankBox.IsChecked = true;
        ShowDropdownBox.IsChecked = true;
        SameSettingsBox.IsChecked = false;
        ShowInputMessageBox.IsChecked = true;
        ShowErrorMessageBox.IsChecked = true;
        ErrorTitleBox.Text = "";
        PromptTitleBox.Text = "";
        PromptMessageBox.Text = "";
        ErrorMessageBox.Text = "";
        ClearRequested = markClearRequested;
        Result = null;
        Accepted = false;
        ApplyToSameSettings = false;
        UpdateVisibility();
        UpdateMessageEditorStates();
    }

    private void FocusInitialKeyboardTarget()
    {
        TypeCombo.Focus();
        Keyboard.Focus(TypeCombo);
    }

    private void UpdateMessageEditorStates()
    {
        var plan = new DvMessageVisibility(
            ShowInputMessageBox.IsChecked == true,
            ShowErrorMessageBox.IsChecked == true,
            SelectedAlertStyle());

        PromptTitleBox.IsEnabled = plan.InputEditorsEnabled;
        PromptMessageBox.IsEnabled = plan.InputEditorsEnabled;
        AlertStyleCombo.IsEnabled = plan.AlertStyleEnabled;
        ErrorTitleBox.IsEnabled = plan.ErrorEditorsEnabled;
        ErrorMessageBox.IsEnabled = plan.ErrorEditorsEnabled;
    }

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateVisibility();
    }

    private void OperatorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (TypeCombo == null) return;

        var plan = DataValidationDialogPlanner.CreateVisibilityPlan(
            SelectedType(),
            SelectedOperator(),
            !string.IsNullOrWhiteSpace(SelectionSource));

        var operatorVisibility = ToVisibility(plan.ShowOperator);
        OperatorLabel.Visibility = operatorVisibility;
        OperatorCombo.Visibility = operatorVisibility;

        var formula1Descriptor = DataValidationDialogPlanner.GetFormula1FieldDescriptor(plan.Formula1Label);
        Formula1Label.Content = UiText.Get(formula1Descriptor.LabelResourceKey);
        AutomationProperties.SetName(Formula1Box, UiText.Get(formula1Descriptor.LabelResourceKey));
        AutomationProperties.SetHelpText(Formula1Box, formula1Descriptor.HelpText);
        Formula2Label.Content = UiText.Get(DataValidationDialogPlanner.Formula2FieldDescriptor.LabelResourceKey);
        AutomationProperties.SetName(Formula2Box, UiText.Get(DataValidationDialogPlanner.Formula2FieldDescriptor.LabelResourceKey));
        AutomationProperties.SetHelpText(Formula2Box, DataValidationDialogPlanner.Formula2FieldDescriptor.HelpText);
        Formula1Label.Visibility = ToVisibility(plan.ShowFormula1);
        Formula1Box.Visibility = ToVisibility(plan.ShowFormula1);
        SourcePickerButton.Visibility = ToVisibility(plan.ShowFormula1RangePicker);
        UseSelectionButton.Visibility = ToVisibility(plan.ShowFormula1UseSelection);

        Formula2Label.Visibility = ToVisibility(plan.ShowFormula2);
        Formula2Box.Visibility = ToVisibility(plan.ShowFormula2);
        SourcePicker2Button.Visibility = ToVisibility(plan.ShowFormula2RangePicker);
        UseSelection2Button.Visibility = ToVisibility(plan.ShowFormula2UseSelection);

        ShowDropdownBox.Visibility = ToVisibility(plan.ShowDropdown);
    }

    private DvType SelectedType() =>
        DataValidationDialogPlanner.TypeFromTag((TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string);

    private DvOperator SelectedOperator() =>
        DataValidationDialogPlanner.OperatorFromTag((OperatorCombo.SelectedItem as ComboBoxItem)?.Tag as string);

    private DvAlertStyle SelectedAlertStyle() =>
        DataValidationDialogPlanner.AlertStyleFromTag((AlertStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string);

    private static Visibility ToVisibility(bool visible) =>
        visible ? Visibility.Visible : Visibility.Collapsed;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var type = SelectedType();
        var op = SelectedOperator();
        var alertStyle = SelectedAlertStyle();

        var validation = DataValidationDialogPlanner.ValidateCriteria(type, op, Formula1Box.Text, Formula2Box.Text);
        if (!validation.IsValid)
        {
            var criteriaError = LocalizeValidationError(validation.FirstError);
            LastValidationError = criteriaError;
            ValidationTabs.SelectedItem = SettingsTab;
            DialogFocus.ShowWarningAndFocus(this, criteriaError, Title, ResolveInvalidCriteriaInput(type, op));
            return;
        }

        var input = CreateRuleEditorInput(type, op, alertStyle);
        Result = DataValidationDialogPlanner.CreateRule(input);
        ApplyToSameSettings = SameSettingsBox.IsChecked == true;
        ClearRequested = ClearRequested && DataValidationDialogPlanner.IsClearAllState(input);
        LastValidationError = null;

        CompleteDialog(accepted: true);
    }

    private DataValidationRuleEditorInput CreateRuleEditorInput(
        DvType type,
        DvOperator op,
        DvAlertStyle alertStyle) =>
        new()
        {
            Id = _resultId,
            Type = type,
            Operator = op,
            AlertStyle = alertStyle,
            Formula1 = Formula1Box.Text,
            Formula2 = Formula2Box.Text,
            AllowBlank = AllowBlankBox.IsChecked == true,
            ShowDropdown = ShowDropdownBox.IsChecked == true,
            ApplyToSameSettings = SameSettingsBox.IsChecked == true,
            ShowInputMessage = ShowInputMessageBox.IsChecked == true,
            ShowErrorMessage = ShowErrorMessageBox.IsChecked == true,
            ErrorTitle = ErrorTitleBox.Text,
            PromptTitle = PromptTitleBox.Text,
            PromptMessage = PromptMessageBox.Text,
            ErrorMessage = ErrorMessageBox.Text,
            IsX14 = _existingIsX14,
            NativeAttributes = _existingNativeAttributes,
            NativeChildXmls = _existingNativeChildXmls,
            NativeContainerAttributes = _existingNativeContainerAttributes,
            NativeContainerChildXmls = _existingNativeContainerChildXmls
        };

    private TextBox ResolveInvalidCriteriaInput(DvType type, DvOperator op)
    {
        return ShouldFocusSecondCriteriaInput(type, op, Formula1Box.Text, Formula2Box.Text)
            ? Formula2Box
            : Formula1Box;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CompleteDialog(accepted: false);
    }

    private void CompleteDialog(bool accepted)
    {
        Accepted = accepted;
        try
        {
            DialogResult = accepted;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaults(markClearRequested: true);
    }

    private void UseSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectionSourceTo(Formula1Box);
    }

    private void SourcePickerButton_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectionSourceTo(Formula1Box);

        RequestRangeSelection(DataValidationRangeSelectionTarget.Formula1, Formula1Box);
    }

    private void SourcePicker2Button_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectionSourceTo(Formula2Box);

        RequestRangeSelection(DataValidationRangeSelectionTarget.Formula2, Formula2Box);
    }

    private void UseSelection2Button_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectionSourceTo(Formula2Box);
    }

    private void ApplySelectionSourceTo(TextBox textBox)
    {
        var selectionSource = SelectionSource?.Trim();
        if (string.IsNullOrWhiteSpace(selectionSource))
            return;

        textBox.Text = selectionSource;
        FocusRangeSelectionInput(textBox);
    }

    public void ApplyRangeSelection(DataValidationRangeSelectionTarget target, string formulaText)
    {
        SelectionSource = formulaText;
        var textBox = target == DataValidationRangeSelectionTarget.Formula2
            ? Formula2Box
            : Formula1Box;
        textBox.Text = formulaText;
        UpdateVisibility();
        FocusRangeSelectionInput(textBox);
    }

    private void RequestRangeSelection(DataValidationRangeSelectionTarget target, TextBox textBox)
    {
        FocusRangeSelectionInput(textBox);
        RangeSelectionRequest = CreateRangeSelectionRequest(target, textBox.Text);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        FocusRangeSelectionInput(textBox);
    }

    private static void FocusRangeSelectionInput(TextBox textBox)
    {
        DialogFocus.FocusAndSelect(textBox);
    }

    private static void SelectComboItemByTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && string.Equals(comboBoxItem.Tag as string, tag, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }

        comboBox.SelectedItem = comboBox.Items[0];
    }

}
