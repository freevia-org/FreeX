using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.IO;
using FreeX.App.Localization;
using FreeX.App.Presentation.Calculation;
using FreeX.App.Presentation.Shell;
using FreeX.App.Presentation.Options;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum OptionsDialogInitialSection
{
    General,
    FormulaErrorChecking
}

/// <summary>Native WPF Options surface backed by portable options and calculation planners.</summary>
public partial class OptionsDialog : Window
{
    private readonly AppOptions _opts;
    private readonly CalculationOptionsDialogState _calculationState;
    private readonly HashSet<string> _disabledFormulaErrorCodes;
    private readonly Dictionary<string, CheckBox> _errorRuleBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly FreeXOptionsDialogSession _dialogSession;
    private readonly QuickAccessToolbarOptionsSession _quickAccessSession;
    private readonly CustomDictionaryEditorSession _customDictionaryEditor;
    private readonly OptionsDialogInitialSection _initialSection;
    public AppOptions Result { get; private set; }
    public IReadOnlySet<string> DisabledFormulaErrorCodesResult { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The portable calculation submission produced by the shared planner. Null when the user
    /// left the workbook calculation settings unchanged.
    /// </summary>
    public CalculationOptionsSubmission? CalculationSubmission { get; private set; }

    private sealed record QuickAccessCommandChoice(string Id, string DisplayName);

    private static readonly string[] Fonts =
        ["Calibri", "Arial", "Times New Roman", "Courier New", "Segoe UI", "Verdana", "Georgia"];

    private static readonly string[] Sizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36"];

    public OptionsDialog(
        AppOptions opts,
        IEnumerable<string>? disabledFormulaErrorCodes = null,
        OptionsDialogInitialSection initialSection = OptionsDialogInitialSection.General,
        CalculationOptionsDialogState? calcSettings = null,
        FreeXOptionsRuntimeSession? runtimeSession = null)
    {
        _dialogSession = (runtimeSession ?? new FreeXOptionsRuntimeSession(opts)).BeginDialog(opts);
        _opts = _dialogSession.OpenSnapshot;
        _quickAccessSession = _dialogSession.QuickAccessToolbar;
        _customDictionaryEditor = _dialogSession.CustomDictionary;
        // Falls back to the persisted app default only for callers that don't have a live workbook
        // handy (parity-capture surfaces, source-pinning unit tests). The real host call site always
        // passes the live workbook's calculation settings so the Formulas panel reflects the workbook
        // actually open, matching Excel.
        _calculationState = calcSettings ?? CalculationOptionsDialogState.FromAppDefault(opts.AutoCalculate);
        _disabledFormulaErrorCodes = new HashSet<string>(disabledFormulaErrorCodes ?? [], StringComparer.OrdinalIgnoreCase);
        DisabledFormulaErrorCodesResult = new HashSet<string>(_disabledFormulaErrorCodes, StringComparer.OrdinalIgnoreCase);
        _initialSection = initialSection;
        Result = opts;
        InitializeComponent();
        Width = OptionsDialogPlanner.WindowWidth;
        Height = OptionsDialogPlanner.WindowHeight;
        ApplySharedOptionsLayoutMetrics();
        TabList.SelectedIndex = _initialSection == OptionsDialogInitialSection.FormulaErrorChecking ? 1 : 0;
        Loaded += (_, _) =>
        {
            Populate();
            FocusInitialKeyboardTarget();
        };
    }

    private void ApplySharedOptionsLayoutMetrics()
    {
        OptionsCategoryColumn.Width = new GridLength(OptionsDialogPlanner.CategoryColumnWidth);
        TabList.Margin = new Thickness(0, OptionsDialogPlanner.CategoryTopMargin, 0, 0);
        OptionsContentScrollViewer.Padding = new Thickness(
            OptionsDialogPlanner.ContentPaddingHorizontal,
            OptionsDialogPlanner.ContentPaddingVertical,
            OptionsDialogPlanner.ContentPaddingHorizontal,
            OptionsDialogPlanner.ContentPaddingVertical);
        OptionsFooterBorder.Padding = new Thickness(
            OptionsDialogPlanner.FooterPaddingHorizontal,
            OptionsDialogPlanner.FooterPaddingVertical,
            OptionsDialogPlanner.FooterPaddingHorizontal,
            OptionsDialogPlanner.FooterPaddingVertical);
        OptionsFooterBorder.MinHeight = OptionsDialogPlanner.FooterHeight;
        OptionsFooterBorder.MaxHeight = OptionsDialogPlanner.FooterHeight;
        OkBtn.Height = OptionsDialogPlanner.ButtonHeight;
        CancelBtn.Height = OptionsDialogPlanner.ButtonHeight;

        AdvancedDirectionGrid.Margin = new Thickness(
            OptionsDialogPlanner.AdvancedDirectionLeftMargin,
            0,
            0,
            OptionsDialogPlanner.AdvancedDirectionBottomMargin);
        AdvancedDirectionLabelColumn.Width = new GridLength(OptionsDialogPlanner.AdvancedDirectionLabelWidth);
        AdvancedDirectionControlColumn.Width = new GridLength(OptionsDialogPlanner.AdvancedDirectionControlWidth);
        AdvancedObjectsGrid.Margin = new Thickness(0, 0, 0, OptionsDialogPlanner.AdvancedObjectsBottomMargin);
        AdvancedObjectsLabelColumn.Width = new GridLength(OptionsDialogPlanner.AdvancedObjectsLabelWidth);
        AdvancedObjectsControlColumn.Width = new GridLength(OptionsDialogPlanner.AdvancedObjectsControlWidth);
        OptAfterEnterDirection.Height = OptionsDialogPlanner.ControlHeight;
        OptObjectsDisplay.Height = OptionsDialogPlanner.ControlHeight;
    }

    private void Populate()
    {
        // General
        OptDefaultFont.ItemsSource = Fonts;
        var defaultFontName = AppOptions.NormalizeDefaultFontName(_opts.DefaultFontName);
        OptDefaultFont.SelectedItem = Fonts.Contains(defaultFontName)
            ? defaultFontName : AppOptions.DefaultFontNameFallback;

        OptDefaultFontSize.ItemsSource = Sizes;
        OptDefaultFontSize.Text = AppOptions.NormalizeDefaultFontSize(_opts.DefaultFontSize).ToString();

        OptSheetCount.Text = _opts.DefaultSheetCount.ToString();
        OptUserName.Text   = _opts.UserName;
        OptCollapseRibbon.IsChecked = _opts.CollapseRibbonAutomatically;
        OptShowScreenTips.IsChecked = _opts.ShowScreenTips;

        // Formulas — seeded from the live workbook's calculation settings, not the persisted
        // app-wide default, so the dialog reflects whatever the ribbon's Calculation Options
        // last set on this workbook (matching Excel).
        OptCalcAuto.IsChecked   =  _calculationState.AutoCalculate;
        OptCalcManual.IsChecked = !_calculationState.AutoCalculate;
        OptIterativeEnabled.IsChecked = _calculationState.IterativeCalculation;
        OptMaxIterations.Text = (_calculationState.MaxCalculationIterations ?? DefaultMaxCalculationIterations).ToString();
        OptMaxChange.Text = (_calculationState.MaxCalculationChange ?? DefaultMaxCalculationChange).ToString(System.Globalization.CultureInfo.InvariantCulture);
        UpdateIterativeCalculationFieldsState();
        OptR1C1.IsChecked = _opts.UseR1C1ReferenceStyle;
        OptFormulasAutocomplete.IsChecked = true;
        OptProofingIgnoreUppercase.IsChecked = _opts.ProofingIgnoreUppercase;
        PopulateErrorCheckingRules();
        PopulateProofingCustomDictionaryWords();

        // Advanced
        OptMoveAfterEnter.IsChecked = _opts.MoveSelectionAfterEnter;
        OptAfterEnterDirection.ItemsSource = new[]
        {
            UiText.Get("Options_AfterEnterDirectionDown"),
            UiText.Get("Options_AfterEnterDirectionRight"),
            UiText.Get("Options_AfterEnterDirectionUp"),
            UiText.Get("Options_AfterEnterDirectionLeft")
        };
        OptAfterEnterDirection.SelectedIndex = OptionsDialogPlanner.AfterEnterDirectionToIndex(_opts.AfterEnterDirection);
        UpdateAfterEnterDirectionState();
        OptAdvancedFillHandle.IsChecked = _opts.EnableFillHandleAndCellDragAndDrop;
        OptAdvancedAutoComplete.IsChecked = _opts.EnableAutoCompleteForCellValues;
        OptShowGridlines.IsChecked = _opts.ShowGridlines;
        OptShowHeadings.IsChecked = _opts.ShowHeadings;
        OptObjectsDisplay.ItemsSource = new[]
        {
            UiText.Get("Options_ObjectsDisplayAll"),
            UiText.Get("Options_ObjectsDisplayPlaceholders"),
            UiText.Get("Options_ObjectsDisplayNothing")
        };
        OptObjectsDisplay.SelectedIndex = OptionsDialogPlanner.ObjectDisplayToIndex(_opts.ObjectsDisplay);

        // View
        OptShowFormulaBar.IsChecked = _opts.ShowFormulaBar;
        OptFormulaBarExpanded.IsChecked = _opts.FormulaBarExpanded;
        UpdateFormulaBarExpandedState();

        // Save
        OptDefaultFormat.ItemsSource = new[]
        {
            UiText.Get("Options_DefaultFormatXlsx"),
            UiText.Get("Options_DefaultFormatJson")
        };
        OptDefaultFormat.SelectedIndex = OptionsDialogPlanner.DefaultFormatToIndex(_opts.DefaultFormat);
        OptCrashAnalytics.IsChecked = _opts.CrashAnalyticsEnabled;

        OptRecentFilesPath.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FreeX", "recent.json");

        // Language
        OptAppLanguage.ItemsSource = AppLanguageCatalog.GetAvailableLanguages();
        OptAppLanguage.SelectedValue = AppLanguageCatalog.NormalizeCultureName(_opts.AppLanguage);
        if (OptAppLanguage.SelectedIndex < 0)
            OptAppLanguage.SelectedIndex = 0;

        PopulateQuickAccessToolbarOptions();
    }

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabList.SelectedIndex < 0) return;
        var selectedIndex = TabList.SelectedIndex;
        PanelGeneral.Visibility = selectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelFormulas.Visibility = selectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelProofing.Visibility = selectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelSave.Visibility = selectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelLanguage.Visibility = selectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        PanelEaseOfAccess.Visibility = selectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
        PanelAdvanced.Visibility = selectedIndex == 6 ? Visibility.Visible : Visibility.Collapsed;
        PanelCustomizeRibbon.Visibility = selectedIndex == 7 ? Visibility.Visible : Visibility.Collapsed;
        PanelQuickAccessToolbar.Visibility = selectedIndex == 8 ? Visibility.Visible : Visibility.Collapsed;
        PanelAddIns.Visibility = selectedIndex == 9 ? Visibility.Visible : Visibility.Collapsed;
        PanelTrustCenter.Visibility = selectedIndex == 10 ? Visibility.Visible : Visibility.Collapsed;
        PanelView.Visibility = selectedIndex == 11 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FocusInitialKeyboardTarget()
    {
        if (_initialSection == OptionsDialogInitialSection.FormulaErrorChecking)
        {
            TabList.SelectedIndex = 1;
            if (_errorRuleBoxes.Values.FirstOrDefault() is { } firstRule)
            {
                firstRule.Focus();
                Keyboard.Focus(firstRule);
                return;
            }
        }

        TabList.Focus();
        Keyboard.Focus(TabList);
    }

    private void MoveAfterEnter_Changed(object sender, RoutedEventArgs e) =>
        UpdateAfterEnterDirectionState();

    private void ShowFormulaBar_Changed(object sender, RoutedEventArgs e) =>
        UpdateFormulaBarExpandedState();

    private void UpdateAfterEnterDirectionState()
    {
        if (OptAfterEnterDirection is null)
            return;

        OptAfterEnterDirection.IsEnabled = OptMoveAfterEnter.IsChecked == true;
    }

    private void UpdateFormulaBarExpandedState()
    {
        if (OptFormulaBarExpanded is null)
            return;

        OptFormulaBarExpanded.IsEnabled = OptShowFormulaBar.IsChecked == true;
    }

    private void IterativeEnabled_Changed(object sender, RoutedEventArgs e) =>
        UpdateIterativeCalculationFieldsState();

    private void UpdateIterativeCalculationFieldsState()
    {
        if (OptMaxIterations is null || OptMaxChange is null)
            return;

        var enabled = OptIterativeEnabled.IsChecked == true;
        OptMaxIterations.IsEnabled = enabled;
        OptMaxChange.IsEnabled = enabled;
    }

    private void PopulateQuickAccessToolbarOptions()
    {
        QuickAccessBelowRibbonCheckBox.IsChecked = _quickAccessSession.QuickAccessToolbarBelowRibbon;
        RefreshQuickAccessToolbarCommandLists();
    }

    private void RefreshQuickAccessToolbarCommandLists(string? selectedAvailableId = null, string? selectedQatId = null)
    {
        var filterText = QuickAccessSearchBox.Text ?? string.Empty;
        QuickAccessAvailableCommandsList.ItemsSource = _quickAccessSession.FilterAvailable(
                filterText,
                command => [UiText.Get(command.TitleResourceKey), UiText.Get(command.DescriptionResourceKey)])
            .Select(CreateQuickAccessCommandChoice)
            .ToList();
        QuickAccessSelectedCommandsList.ItemsSource = _quickAccessSession.CommandIds
            .Select(id => QuickAccessToolbarCatalog.TryGet(id, out var command) ? command : null)
            .Where(command => command is not null)
            .Select(command => CreateQuickAccessCommandChoice(command!))
            .ToList();

        SelectQuickAccessCommand(QuickAccessAvailableCommandsList, selectedAvailableId);
        SelectQuickAccessCommand(QuickAccessSelectedCommandsList, selectedQatId);
        UpdateQuickAccessToolbarCustomizationButtons();
    }

    private static QuickAccessCommandChoice CreateQuickAccessCommandChoice(QuickAccessToolbarCommandDefinition command) =>
        new(command.Id, UiText.Get(command.TitleResourceKey));

    private static QuickAccessCommandChoice? FindQuickAccessCommandChoice(ListBox listBox, string commandId)
    {
        foreach (var item in listBox.Items)
        {
            if (item is QuickAccessCommandChoice choice && QuickAccessCommandIdsEqual(choice.Id, commandId))
                return choice;
        }

        return null;
    }

    private static bool QuickAccessCommandIdsEqual(string id, string otherId) =>
        string.Equals(id, otherId, StringComparison.OrdinalIgnoreCase);

    private static void SelectQuickAccessCommand(ListBox listBox, string? commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            return;

        listBox.SelectedItem = FindQuickAccessCommandChoice(listBox, commandId);
    }

    private void UpdateQuickAccessToolbarCustomizationButtons()
    {
        QuickAccessAddButton.IsEnabled = QuickAccessAvailableCommandsList.SelectedItem is QuickAccessCommandChoice;
        QuickAccessRemoveButton.IsEnabled =
            QuickAccessSelectedCommandsList.SelectedItem is QuickAccessCommandChoice &&
            _quickAccessSession.CommandIds.Count > 1;
        QuickAccessMoveUpButton.IsEnabled = QuickAccessSelectedCommandsList.SelectedIndex > 0;
        QuickAccessMoveDownButton.IsEnabled =
            QuickAccessSelectedCommandsList.SelectedIndex >= 0 &&
            QuickAccessSelectedCommandsList.SelectedIndex < _quickAccessSession.CommandIds.Count - 1;
    }

    private void QuickAccessCommandLists_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateQuickAccessToolbarCustomizationButtons();

    private void QuickAccessSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (QuickAccessAvailableCommandsList is null || QuickAccessSelectedCommandsList is null)
            return;

        var selectedAvailableId = (QuickAccessAvailableCommandsList.SelectedItem as QuickAccessCommandChoice)?.Id;
        var selectedQatId = (QuickAccessSelectedCommandsList.SelectedItem as QuickAccessCommandChoice)?.Id;
        RefreshQuickAccessToolbarCommandLists(selectedAvailableId, selectedQatId);
    }

    private void QuickAccessAddButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessAvailableCommandsList.SelectedItem is not QuickAccessCommandChoice choice)
            return;

        _quickAccessSession.Apply(choice.Id, QuickAccessToolbarCustomizationAction.Add);
        RefreshQuickAccessToolbarCommandLists(selectedQatId: choice.Id);
    }

    private void QuickAccessRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessSelectedCommandsList.SelectedItem is not QuickAccessCommandChoice choice ||
            _quickAccessSession.CommandIds.Count <= 1)
        {
            return;
        }

        var removedIndex = _quickAccessSession.IndexOf(choice.Id);
        if (removedIndex < 0)
            return;

        _quickAccessSession.Apply(choice.Id, QuickAccessToolbarCustomizationAction.Remove);
        var nextIndex = Math.Clamp(removedIndex, 0, _quickAccessSession.CommandIds.Count - 1);
        RefreshQuickAccessToolbarCommandLists(
            selectedAvailableId: choice.Id,
            selectedQatId: _quickAccessSession.CommandIds.ElementAtOrDefault(nextIndex));
    }

    private void QuickAccessAvailableCommandsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        QuickAccessAddButton_Click(sender, e);
        e.Handled = true;
    }

    private void QuickAccessSelectedCommandsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        QuickAccessRemoveButton_Click(sender, e);
        e.Handled = true;
    }

    private void QuickAccessAvailableCommandsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleQuickAccessAvailableCommandsListKey(e.Key))
            e.Handled = true;
    }

    private bool TryHandleQuickAccessAvailableCommandsListKey(Key key)
    {
        if (key is not (Key.Enter or Key.Return) ||
            QuickAccessAvailableCommandsList.SelectedItem is not QuickAccessCommandChoice)
        {
            return false;
        }

        QuickAccessAddButton_Click(
            QuickAccessAddButton,
            new RoutedEventArgs(ButtonBase.ClickEvent, QuickAccessAddButton));
        return true;
    }

    private void QuickAccessSelectedCommandsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleQuickAccessSelectedCommandsListKey(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    private bool TryHandleQuickAccessSelectedCommandsListKey(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (key == Key.Up && QuickAccessMoveUpButton.IsEnabled)
            {
                QuickAccessMoveUpButton_Click(
                    QuickAccessMoveUpButton,
                    new RoutedEventArgs(ButtonBase.ClickEvent, QuickAccessMoveUpButton));
                return true;
            }

            if (key == Key.Down && QuickAccessMoveDownButton.IsEnabled)
            {
                QuickAccessMoveDownButton_Click(
                    QuickAccessMoveDownButton,
                    new RoutedEventArgs(ButtonBase.ClickEvent, QuickAccessMoveDownButton));
                return true;
            }

            return false;
        }

        if (key is not (Key.Delete or Key.Back) || !QuickAccessRemoveButton.IsEnabled)
            return false;

        QuickAccessRemoveButton_Click(
            QuickAccessRemoveButton,
            new RoutedEventArgs(ButtonBase.ClickEvent, QuickAccessRemoveButton));
        return true;
    }

    private void QuickAccessMoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessSelectedCommandsList.SelectedItem is not QuickAccessCommandChoice choice)
            return;

        var index = _quickAccessSession.IndexOf(choice.Id);
        if (index <= 0)
            return;

        _quickAccessSession.Move(choice.Id, -1);
        RefreshQuickAccessToolbarCommandLists(selectedQatId: choice.Id);
    }

    private void QuickAccessMoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessSelectedCommandsList.SelectedItem is not QuickAccessCommandChoice choice)
            return;

        var index = _quickAccessSession.IndexOf(choice.Id);
        if (index < 0 || index >= _quickAccessSession.CommandIds.Count - 1)
            return;

        _quickAccessSession.Move(choice.Id, 1);
        RefreshQuickAccessToolbarCommandLists(selectedQatId: choice.Id);
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!OptionsDialogPlanner.TryBuildInput(
                OptDefaultFont.SelectedItem as string ?? _opts.DefaultFontName,
                OptDefaultFontSize.Text,
                OptSheetCount.Text,
                OptUserName.Text,
                OptCalcAuto.IsChecked == true,
                OptR1C1.IsChecked == true,
                _opts.ErrorCheckingEnabled,
                OptProofingIgnoreUppercase.IsChecked == true,
                _opts.ProofingIgnoreNumbers,
                OptShowFormulaBar.IsChecked == true,
                OptShowGridlines.IsChecked == true,
                OptShowHeadings.IsChecked == true,
                OptionsDialogPlanner.IndexToDefaultFormat(OptDefaultFormat.SelectedIndex),
                OptShowScreenTips.IsChecked == true,
                OptMoveAfterEnter.IsChecked == true,
                OptionsDialogPlanner.IndexToAfterEnterDirection(OptAfterEnterDirection.SelectedIndex),
                out var input,
                out var inputError,
                objectsDisplay: OptionsDialogPlanner.IndexToObjectDisplay(OptObjectsDisplay.SelectedIndex),
                collapseRibbonAutomatically: OptCollapseRibbon.IsChecked == true,
                appLanguage: AppLanguageCatalog.NormalizeCultureName(OptAppLanguage.SelectedValue as string),
                crashAnalyticsEnabled: OptCrashAnalytics.IsChecked == true))
        {
            var presentation = OptionsDialogPlanner.DescribeInputError(
                inputError,
                OptionsValidationTextProfile.Wpf);
            ShowInvalidInputWarning(
                presentation.Message.Resolve(UiText.Get, UiText.Format),
                presentation.FocusTarget == OptionsValidationFocusTarget.DefaultFontSize
                    ? OptDefaultFontSize
                    : OptSheetCount);
            return;
        }

        var iterativeEnabled = OptIterativeEnabled.IsChecked == true;
        if (!CalculationOptionsInputParser.TryParseBounds(
                iterativeEnabled,
                OptMaxIterations.Text,
                OptMaxChange.Text,
                _calculationState.MaxCalculationIterations ?? DefaultMaxCalculationIterations,
                _calculationState.MaxCalculationChange ?? DefaultMaxCalculationChange,
                out var maxIterations,
                out var maxChange,
                out var calculationInputError))
        {
            var presentation = OptionsValidationPresentationPlanner.DescribeCalculationInput(calculationInputError);
            ShowInvalidInputWarning(
                presentation.Message.Resolve(UiText.Get, UiText.Format),
                presentation.FocusTarget == OptionsValidationFocusTarget.MaxIterations
                    ? OptMaxIterations
                    : OptMaxChange);
            return;
        }

        var saveResult = _dialogSession.Commit(
            input,
            enableFillHandleAndCellDragAndDrop: OptAdvancedFillHandle.IsChecked == true,
            enableAutoCompleteForCellValues: OptAdvancedAutoComplete.IsChecked == true,
            quickAccessToolbarBelowRibbon: QuickAccessBelowRibbonCheckBox.IsChecked == true,
            formulaBarExpanded: OptShowFormulaBar.IsChecked == true && OptFormulaBarExpanded.IsChecked == true);
        if (!saveResult.IsPersisted)
        {
            DialogMessageHelper.ShowError(this, saveResult.PersistenceError, Title);
            return;
        }

        Result = saveResult.Options;
        DisabledFormulaErrorCodesResult = CollectDisabledFormulaErrorCodes();

        CalculationSubmission = CalculationOptionsSubmissionPlanner.Plan(
            _calculationState,
            OptCalcAuto.IsChecked == true,
            iterativeEnabled,
            maxIterations,
            maxChange);

        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private const int DefaultMaxCalculationIterations = CalculationCommandPolicy.DefaultMaxCalculationIterations;
    private const double DefaultMaxCalculationChange = CalculationCommandPolicy.DefaultMaxCalculationChange;

    private bool ShowInvalidInputWarning(string message, Control target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return true;
    }

    private void AutoCorrectOptionsButton_Click(object sender, RoutedEventArgs e) =>
        ShowDeferredOptionsMessage(WpfResourceKeyTextResolver.Resolve(DeferredCommandMessagePlanner.AutoCorrectOptions()));

    private void PopulateProofingCustomDictionaryWords()
    {
        _customDictionaryEditor.Reset(_opts.SpellCheckCustomDictionaryWords);
        RefreshProofingCustomDictionaryWordsList();
    }

    private void RefreshProofingCustomDictionaryWordsList()
    {
        var model = _customDictionaryEditor.Model;
        ProofingCustomDictionaryWordsList.ItemsSource = model.Words;
        ProofingCustomDictionaryWordsList.SelectedItem = model.SelectedWord;

        UpdateProofingCustomDictionaryButtons();
    }

    private void UpdateProofingCustomDictionaryButtons()
    {
        if (ProofingCustomDictionaryAddWordButton is null)
            return;

        var model = _customDictionaryEditor.Model;
        ProofingCustomDictionaryAddWordButton.IsEnabled = model.CanAdd;
        ProofingCustomDictionaryRemoveWordButton.IsEnabled = model.CanRemove;
        ProofingCustomDictionaryClearWordsButton.IsEnabled = model.CanClear;
    }

    private void ProofingCustomDictionaryWordBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _customDictionaryEditor.SetPendingWord(ProofingCustomDictionaryWordBox.Text);
        UpdateProofingCustomDictionaryButtons();
    }

    private void ProofingCustomDictionaryWordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || !ProofingCustomDictionaryAddWordButton.IsEnabled)
            return;

        ProofingCustomDictionaryAddWordButton_Click(
            ProofingCustomDictionaryAddWordButton,
            new RoutedEventArgs(ButtonBase.ClickEvent, ProofingCustomDictionaryAddWordButton));
        e.Handled = true;
    }

    private void ProofingCustomDictionaryWordsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _customDictionaryEditor.SelectWord(ProofingCustomDictionaryWordsList.SelectedItem as string);
        UpdateProofingCustomDictionaryButtons();
    }

    private void ProofingCustomDictionaryAddWordButton_Click(object sender, RoutedEventArgs e)
    {
        _customDictionaryEditor.SetPendingWord(ProofingCustomDictionaryWordBox.Text);
        _customDictionaryEditor.AddPendingWord();
        ProofingCustomDictionaryWordBox.Clear();
        RefreshProofingCustomDictionaryWordsList();
    }

    private void ProofingCustomDictionaryRemoveWordButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProofingCustomDictionaryWordsList.SelectedItem is not string selectedWord)
            return;

        _customDictionaryEditor.SelectWord(selectedWord);
        _customDictionaryEditor.RemoveSelectedWord();
        RefreshProofingCustomDictionaryWordsList();
    }

    private void ProofingCustomDictionaryClearWordsButton_Click(object sender, RoutedEventArgs e)
    {
        _customDictionaryEditor.Clear();
        RefreshProofingCustomDictionaryWordsList();
        ProofingCustomDictionaryWordBox.Focus();
        Keyboard.Focus(ProofingCustomDictionaryWordBox);
    }

    private void RibbonImportExportButton_Click(object sender, RoutedEventArgs e) =>
        ShowDeferredOptionsMessage(WpfResourceKeyTextResolver.Resolve(DeferredCommandMessagePlanner.RibbonCustomizationImportExport()));

    private void QuickAccessResetButton_Click(object sender, RoutedEventArgs e)
    {
        _quickAccessSession.Reset();
        RefreshQuickAccessToolbarCommandLists(selectedQatId: _quickAccessSession.CommandIds[0]);
    }

    private void QuickAccessImportExportButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = QuickAccessImportExportButton,
            Placement = PlacementMode.Bottom
        };

        var importItem = new MenuItem { Header = QuickAccessToolbarCustomizationFile.ImportMenuHeader };
        AutomationProperties.SetAutomationId(importItem, FreeXAutomationIdCatalog.QuickAccessToolbarImportCustomizationMenuItem);
        importItem.Click += QuickAccessImportCustomizationMenuItem_Click;

        var exportItem = new MenuItem { Header = QuickAccessToolbarCustomizationFile.ExportMenuHeader };
        AutomationProperties.SetAutomationId(exportItem, FreeXAutomationIdCatalog.QuickAccessToolbarExportCustomizationMenuItem);
        exportItem.Click += QuickAccessExportCustomizationMenuItem_Click;

        menu.Items.Add(importItem);
        menu.Items.Add(exportItem);
        QuickAccessImportExportButton.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void QuickAccessImportCustomizationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var pickerResult = WpfFileDialogService.ShowOpenDialog(
            this,
            QuickAccessToolbarCustomizationFile.DialogFilter,
            QuickAccessToolbarCustomizationFile.DefaultExtension,
            checkFileExists: true,
            title: "Import Quick Access Toolbar customization");
        if (!pickerResult.Chosen)
            return;

        var result = _quickAccessSession.TryImport(pickerResult.FileName!);
        if (!result.Success || result.Customization is null)
        {
            DialogMessageHelper.ShowWarning(this, result.ErrorMessage, UiText.Get("Options_QuickAccessToolbar"));
            return;
        }

        QuickAccessBelowRibbonCheckBox.IsChecked = _quickAccessSession.QuickAccessToolbarBelowRibbon;
        RefreshQuickAccessToolbarCommandLists(selectedQatId: _quickAccessSession.CommandIds[0]);
        DialogMessageHelper.ShowInfo(
            this,
            $"Imported Quick Access Toolbar customization from '{pickerResult.FileName!}'.",
            UiText.Get("Options_QuickAccessToolbar"));
    }

    private void QuickAccessExportCustomizationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var pickerResult = WpfFileDialogService.ShowSaveDialog(
            this,
            QuickAccessToolbarCustomizationFile.DialogFilter,
            QuickAccessToolbarCustomizationFile.DefaultFileName,
            QuickAccessToolbarCustomizationFile.DefaultExtension,
            filterIndex: 1,
            title: "Export Quick Access Toolbar customization");
        if (!pickerResult.Chosen)
            return;

        _quickAccessSession.SetPlacement(QuickAccessBelowRibbonCheckBox.IsChecked == true);
        if (!_quickAccessSession.TryExport(pickerResult.FileName!, out var errorMessage))
        {
            DialogMessageHelper.ShowError(this, errorMessage, UiText.Get("Options_QuickAccessToolbar"));
            return;
        }

        DialogMessageHelper.ShowInfo(
            this,
            $"Exported Quick Access Toolbar customization to '{pickerResult.FileName!}'.",
            UiText.Get("Options_QuickAccessToolbar"));
    }

    private void AddInsGoButton_Click(object sender, RoutedEventArgs e) =>
        ShowDeferredOptionsMessage(WpfResourceKeyTextResolver.Resolve(DeferredCommandMessagePlanner.OfficeAddIns()));

    private void TrustCenterSettingsButton_Click(object sender, RoutedEventArgs e) =>
        ShowDeferredOptionsMessage(WpfResourceKeyTextResolver.Resolve(DeferredCommandMessagePlanner.TrustCenterSettings()));

    private void ShowDeferredOptionsMessage(DeferredCommandMessage message) =>
        DialogMessageHelper.ShowInfo(this, message.Body, message.Title);

    private void PopulateErrorCheckingRules()
    {
        OptErrorCheckingRules.Children.Clear();
        _errorRuleBoxes.Clear();

        foreach (var rule in FormulaErrorCheckingRuleCatalog.SupportedRules)
        {
            var checkBox = new CheckBox
            {
                Content = rule.Label,
                ToolTip = rule.Description,
                IsChecked = !_disabledFormulaErrorCodes.Contains(rule.ErrorCode),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6),
                Tag = rule.ErrorCode
            };
            _errorRuleBoxes[rule.ErrorCode] = checkBox;
            OptErrorCheckingRules.Children.Add(checkBox);
        }
    }

    private IReadOnlySet<string> CollectDisabledFormulaErrorCodes()
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in FormulaErrorCheckingRuleCatalog.SupportedRules)
        {
            if (_errorRuleBoxes.TryGetValue(rule.ErrorCode, out var box) &&
                box.IsChecked != true)
            {
                disabled.Add(rule.ErrorCode);
            }
        }

        return disabled;
    }
}
