using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.IO;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum OptionsDialogInitialSection
{
    General,
    FormulaErrorChecking
}

/// <summary>
/// Snapshot of the live workbook's calculation settings (calc mode + iterative-calculation
/// enable/max-iterations/max-change), captured just before the Options dialog opens so the
/// Formulas panel seeds from the workbook actually being edited, not the persisted app-wide
/// <see cref="AppOptions.AutoCalculate"/> default. Excel's Options dialog reflects the active
/// workbook's calculation state, not a saved app preference.
/// </summary>
/// <param name="AutoCalculate">
/// True when the workbook is NOT in Manual mode. The dialog only has two calc-mode radio
/// buttons (Automatic/Manual), so <see cref="WorkbookCalculationMode.AutomaticExceptDataTables"/>
/// must map here to "Automatic" (checked, not Manual) — never collapse it into Manual.
/// </param>
/// <param name="CalculationMode">
/// The workbook's real tri-state calculation mode when known (set by <see cref="FromWorkbook"/>).
/// Null for settings built from a dialog edit or the persisted app-wide default, which can only
/// ever express Automatic/Manual. Callers applying an edited settings snapshot back to the
/// workbook must compare against <see cref="AutoCalculate"/> (not this mode) so that leaving the
/// calc-mode radios untouched never overwrites an <see cref="WorkbookCalculationMode.AutomaticExceptDataTables"/>
/// workbook with plain Automatic or Manual as a side effect of an unrelated settings change.
/// </param>
public sealed record OptionsDialogCalculationSettings(
    bool AutoCalculate,
    bool IterativeCalculation,
    int? MaxCalculationIterations,
    double? MaxCalculationChange,
    WorkbookCalculationMode? CalculationMode = null)
{
    public static OptionsDialogCalculationSettings FromWorkbook(Workbook workbook) => new(
        workbook.CalculationMode != WorkbookCalculationMode.Manual,
        workbook.IterativeCalculation,
        workbook.MaxCalculationIterations,
        workbook.MaxCalculationChange,
        workbook.CalculationMode);
}

public partial class OptionsDialog : Window
{
    private readonly AppOptions _opts;
    private readonly OptionsDialogCalculationSettings _calcSettings;
    private readonly HashSet<string> _disabledFormulaErrorCodes;
    private readonly Dictionary<string, CheckBox> _errorRuleBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _quickAccessCommandIds = [];
    private readonly CustomDictionaryEditorSession _customDictionaryEditor = new([]);
    private readonly OptionsDialogInitialSection _initialSection;
    public AppOptions Result { get; private set; }
    public IReadOnlySet<string> DisabledFormulaErrorCodesResult { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The workbook calculation settings as edited in the dialog. Null when the user did not
    /// change anything from <see cref="OptionsDialogCalculationSettings"/> passed in, so the
    /// caller can apply the workbook-level change only when something actually changed.
    /// </summary>
    public OptionsDialogCalculationSettings? CalculationSettingsResult { get; private set; }

    private sealed record QuickAccessCommandChoice(string Id, string DisplayName);

    private static readonly string[] Fonts =
        ["Calibri", "Arial", "Times New Roman", "Courier New", "Segoe UI", "Verdana", "Georgia"];

    private static readonly string[] Sizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36"];

    public OptionsDialog(
        AppOptions opts,
        IEnumerable<string>? disabledFormulaErrorCodes = null,
        OptionsDialogInitialSection initialSection = OptionsDialogInitialSection.General,
        OptionsDialogCalculationSettings? calcSettings = null)
    {
        _opts = opts;
        // Falls back to the persisted app default only for callers that don't have a live workbook
        // handy (parity-capture surfaces, source-pinning unit tests). The real host call site always
        // passes the live workbook's calculation settings so the Formulas panel reflects the workbook
        // actually open, matching Excel.
        _calcSettings = calcSettings ?? new OptionsDialogCalculationSettings(opts.AutoCalculate, false, null, null);
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
        OptCalcAuto.IsChecked   =  _calcSettings.AutoCalculate;
        OptCalcManual.IsChecked = !_calcSettings.AutoCalculate;
        OptIterativeEnabled.IsChecked = _calcSettings.IterativeCalculation;
        OptMaxIterations.Text = (_calcSettings.MaxCalculationIterations ?? DefaultMaxCalculationIterations).ToString();
        OptMaxChange.Text = (_calcSettings.MaxCalculationChange ?? DefaultMaxCalculationChange).ToString(System.Globalization.CultureInfo.InvariantCulture);
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
        QuickAccessBelowRibbonCheckBox.IsChecked = _opts.QuickAccessToolbarBelowRibbon;
        _quickAccessCommandIds.Clear();
        _quickAccessCommandIds.AddRange(QuickAccessToolbarCatalog.NormalizeCommandIds(_opts.QuickAccessToolbarCommands));
        RefreshQuickAccessToolbarCommandLists();
    }

    private void RefreshQuickAccessToolbarCommandLists(string? selectedAvailableId = null, string? selectedQatId = null)
    {
        var filterText = QuickAccessSearchBox.Text ?? string.Empty;
        QuickAccessAvailableCommandsList.ItemsSource = QuickAccessToolbarCustomizationPlanner.FilterAvailable(
                _quickAccessCommandIds,
                filterText,
                command => [UiText.Get(command.TitleResourceKey), UiText.Get(command.DescriptionResourceKey)])
            .Select(CreateQuickAccessCommandChoice)
            .ToList();
        QuickAccessSelectedCommandsList.ItemsSource = _quickAccessCommandIds
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

    private int IndexOfQuickAccessCommandId(string commandId)
    {
        for (var index = 0; index < _quickAccessCommandIds.Count; index++)
        {
            if (QuickAccessCommandIdsEqual(_quickAccessCommandIds[index], commandId))
                return index;
        }

        return -1;
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
            _quickAccessCommandIds.Count > 1;
        QuickAccessMoveUpButton.IsEnabled = QuickAccessSelectedCommandsList.SelectedIndex > 0;
        QuickAccessMoveDownButton.IsEnabled =
            QuickAccessSelectedCommandsList.SelectedIndex >= 0 &&
            QuickAccessSelectedCommandsList.SelectedIndex < _quickAccessCommandIds.Count - 1;
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

        var updated = QuickAccessToolbarCustomizationPlanner.Apply(
            _quickAccessCommandIds,
            choice.Id,
            QuickAccessToolbarCustomizationAction.Add);
        _quickAccessCommandIds.Clear();
        _quickAccessCommandIds.AddRange(updated);
        RefreshQuickAccessToolbarCommandLists(selectedQatId: choice.Id);
    }

    private void QuickAccessRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessSelectedCommandsList.SelectedItem is not QuickAccessCommandChoice choice ||
            _quickAccessCommandIds.Count <= 1)
        {
            return;
        }

        var removedIndex = IndexOfQuickAccessCommandId(choice.Id);
        if (removedIndex < 0)
            return;

        var updated = QuickAccessToolbarCustomizationPlanner.Apply(
            _quickAccessCommandIds,
            choice.Id,
            QuickAccessToolbarCustomizationAction.Remove);
        _quickAccessCommandIds.Clear();
        _quickAccessCommandIds.AddRange(updated);
        var nextIndex = Math.Clamp(removedIndex, 0, _quickAccessCommandIds.Count - 1);
        RefreshQuickAccessToolbarCommandLists(
            selectedAvailableId: choice.Id,
            selectedQatId: _quickAccessCommandIds.ElementAtOrDefault(nextIndex));
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

        var index = IndexOfQuickAccessCommandId(choice.Id);
        if (index <= 0)
            return;

        var updated = QuickAccessToolbarCustomizationPlanner.Move(_quickAccessCommandIds, choice.Id, -1);
        _quickAccessCommandIds.Clear();
        _quickAccessCommandIds.AddRange(updated);
        RefreshQuickAccessToolbarCommandLists(selectedQatId: choice.Id);
    }

    private void QuickAccessMoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessSelectedCommandsList.SelectedItem is not QuickAccessCommandChoice choice)
            return;

        var index = IndexOfQuickAccessCommandId(choice.Id);
        if (index < 0 || index >= _quickAccessCommandIds.Count - 1)
            return;

        var updated = QuickAccessToolbarCustomizationPlanner.Move(_quickAccessCommandIds, choice.Id, 1);
        _quickAccessCommandIds.Clear();
        _quickAccessCommandIds.AddRange(updated);
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
            var invalidFontSize = inputError == OptionsDialogPlanner.OptionsInputError.InvalidFontSize;
            ShowInvalidInputWarning(
                UiText.Get(invalidFontSize
                    ? "Options_InvalidDefaultFontSizeMessage"
                    : "Options_InvalidSheetCountMessage"),
                invalidFontSize ? OptDefaultFontSize : OptSheetCount);
            return;
        }

        var iterativeEnabled = OptIterativeEnabled.IsChecked == true;
        if (!CalculationOptionsInputParser.TryParseBounds(
                iterativeEnabled,
                OptMaxIterations.Text,
                OptMaxChange.Text,
                _calcSettings.MaxCalculationIterations ?? DefaultMaxCalculationIterations,
                _calcSettings.MaxCalculationChange ?? DefaultMaxCalculationChange,
                out var maxIterations,
                out var maxChange,
                out var calculationInputError))
        {
            var invalidIterations = calculationInputError == CalculationOptionsInputError.InvalidMaxIterations;
            ShowInvalidInputWarning(
                UiText.Get(invalidIterations
                    ? "Options_InvalidMaxIterationsMessage"
                    : "Options_InvalidMaxChangeMessage"),
                invalidIterations ? OptMaxIterations : OptMaxChange);
            return;
        }

        var edited = OptionsDialogPlanner.Project(
            _opts,
            input,
            new OptionsDialogPlanner.OptionsDialogSupplementalInput(
                EnableFillHandleAndCellDragAndDrop: OptAdvancedFillHandle.IsChecked == true,
                EnableAutoCompleteForCellValues: OptAdvancedAutoComplete.IsChecked == true,
                QuickAccessToolbarBelowRibbon: QuickAccessBelowRibbonCheckBox.IsChecked == true,
                QuickAccessToolbarCommands: QuickAccessToolbarCatalog.NormalizeCommandIds(_quickAccessCommandIds).ToList(),
                SpellCheckCustomDictionaryWords: _customDictionaryEditor.Model.Words.ToList(),
                FormulaBarExpanded: OptFormulaBarExpanded.IsChecked == true));
        var opts = OptionsDialogPlanner.MergeOntoFreshLoad(
            AppOptionsStore.Load(),
            _opts,
            edited);
        if (!AppOptionsStore.Save(opts))
        {
            DialogMessageHelper.ShowError(this, opts.LastPersistenceError, Title);
            return;
        }

        Result = opts;
        DisabledFormulaErrorCodesResult = CollectDisabledFormulaErrorCodes();

        var editedCalcSettings = new OptionsDialogCalculationSettings(
            OptCalcAuto.IsChecked == true,
            iterativeEnabled,
            maxIterations,
            maxChange);
        // Only surface a workbook-level calculation change when the user actually changed
        // something in this panel — an unrelated Options edit (e.g. UserName) must never force
        // -apply stale/unseen calc settings back onto the live workbook. The max-iterations/
        // max-change text boxes always round-trip a concrete number (they're seeded from
        // DefaultMaxCalculationIterations/DefaultMaxCalculationChange when the workbook had no
        // explicit value yet), so comparing the raw records would spuriously treat every dialog
        // open+OK as an edit whenever the workbook's iterative-calc bounds are still null; compare
        // the *effective* (null-coalesced) values instead.
        var unchanged =
            editedCalcSettings.AutoCalculate == _calcSettings.AutoCalculate &&
            editedCalcSettings.IterativeCalculation == _calcSettings.IterativeCalculation &&
            editedCalcSettings.MaxCalculationIterations == (_calcSettings.MaxCalculationIterations ?? DefaultMaxCalculationIterations) &&
            editedCalcSettings.MaxCalculationChange == (_calcSettings.MaxCalculationChange ?? DefaultMaxCalculationChange);
        CalculationSettingsResult = unchanged ? null : editedCalcSettings;

        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private const int DefaultMaxCalculationIterations = 100;
    private const double DefaultMaxCalculationChange = 0.001;

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
        var reset = QuickAccessToolbarCustomizationPlanner.Reset();
        _quickAccessCommandIds.Clear();
        _quickAccessCommandIds.AddRange(reset);
        RefreshQuickAccessToolbarCommandLists(selectedQatId: _quickAccessCommandIds[0]);
    }

    private void QuickAccessImportExportButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = QuickAccessImportExportButton,
            Placement = PlacementMode.Bottom
        };

        var importItem = new MenuItem { Header = QuickAccessToolbarCustomizationFile.ImportMenuHeader };
        AutomationProperties.SetAutomationId(importItem, "QuickAccessToolbarImportCustomizationMenuItem");
        importItem.Click += QuickAccessImportCustomizationMenuItem_Click;

        var exportItem = new MenuItem { Header = QuickAccessToolbarCustomizationFile.ExportMenuHeader };
        AutomationProperties.SetAutomationId(exportItem, "QuickAccessToolbarExportCustomizationMenuItem");
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

        var result = QuickAccessToolbarCustomizationFile.TryLoad(pickerResult.FileName!);
        if (!result.Success || result.Customization is null)
        {
            DialogMessageHelper.ShowWarning(this, result.ErrorMessage, UiText.Get("Options_QuickAccessToolbar"));
            return;
        }

        QuickAccessBelowRibbonCheckBox.IsChecked = result.Customization.QuickAccessToolbarBelowRibbon;
        _quickAccessCommandIds.Clear();
        _quickAccessCommandIds.AddRange(result.Customization.CommandIds);
        RefreshQuickAccessToolbarCommandLists(selectedQatId: _quickAccessCommandIds[0]);
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

        if (!QuickAccessToolbarCustomizationFile.TrySave(
                pickerResult.FileName!,
                _quickAccessCommandIds,
                QuickAccessBelowRibbonCheckBox.IsChecked == true,
                out var errorMessage))
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
