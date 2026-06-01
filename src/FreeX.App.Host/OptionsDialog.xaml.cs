using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public partial class OptionsDialog : Window
{
    private readonly FreeXOptions _opts;
    private readonly HashSet<string> _disabledFormulaErrorCodes;
    private readonly Dictionary<string, CheckBox> _errorRuleBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _quickAccessCommandIds = [];
    public FreeXOptions Result { get; private set; }
    public IReadOnlySet<string> DisabledFormulaErrorCodesResult { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private sealed record QuickAccessCommandChoice(string Id, string DisplayName);

    private static readonly string[] Fonts =
        ["Calibri", "Arial", "Times New Roman", "Courier New", "Segoe UI", "Verdana", "Georgia"];

    private static readonly string[] Sizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36"];

    public OptionsDialog(FreeXOptions opts, IEnumerable<string>? disabledFormulaErrorCodes = null)
    {
        _opts = opts;
        _disabledFormulaErrorCodes = new HashSet<string>(disabledFormulaErrorCodes ?? [], StringComparer.OrdinalIgnoreCase);
        DisabledFormulaErrorCodesResult = new HashSet<string>(_disabledFormulaErrorCodes, StringComparer.OrdinalIgnoreCase);
        Result = opts;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Populate();
            FocusInitialKeyboardTarget();
        };
        TabList.SelectedIndex = 0;
    }

    private void Populate()
    {
        // General
        OptDefaultFont.ItemsSource = Fonts;
        OptDefaultFont.SelectedItem = Fonts.Contains(_opts.DefaultFontName)
            ? _opts.DefaultFontName : "Calibri";

        OptDefaultFontSize.ItemsSource = Sizes;
        OptDefaultFontSize.Text = _opts.DefaultFontSize.ToString();

        OptSheetCount.Text = _opts.DefaultSheetCount.ToString();
        OptUserName.Text   = _opts.UserName;
        OptCollapseRibbon.IsChecked = _opts.CollapseRibbonAutomatically;
        OptShowScreenTips.IsChecked = _opts.ShowScreenTips;

        // Formulas
        OptCalcAuto.IsChecked   =  _opts.AutoCalculate;
        OptCalcManual.IsChecked = !_opts.AutoCalculate;
        OptR1C1.IsChecked = _opts.UseR1C1ReferenceStyle;
        OptFormulasAutocomplete.IsChecked = true;
        PopulateErrorCheckingRules();

        // Advanced
        OptMoveAfterEnter.IsChecked = _opts.MoveSelectionAfterEnter;
        OptAfterEnterDirection.ItemsSource = new[]
        {
            UiText.Get("Options_AfterEnterDirectionDown"),
            UiText.Get("Options_AfterEnterDirectionRight"),
            UiText.Get("Options_AfterEnterDirectionUp"),
            UiText.Get("Options_AfterEnterDirectionLeft")
        };
        OptAfterEnterDirection.SelectedIndex = _opts.AfterEnterDirection switch
        {
            FreeXEnterDirection.Right => 1,
            FreeXEnterDirection.Up => 2,
            FreeXEnterDirection.Left => 3,
            _ => 0
        };
        UpdateAfterEnterDirectionState();
        OptShowGridlines.IsChecked = _opts.ShowGridlines;
        OptShowHeadings.IsChecked = _opts.ShowHeadings;
        OptObjectsDisplay.ItemsSource = new[]
        {
            UiText.Get("Options_ObjectsDisplayAll"),
            UiText.Get("Options_ObjectsDisplayPlaceholders"),
            UiText.Get("Options_ObjectsDisplayNothing")
        };
        OptObjectsDisplay.SelectedIndex = _opts.ObjectsDisplay switch
        {
            FreeXObjectDisplay.Placeholders => 1,
            FreeXObjectDisplay.Nothing => 2,
            _ => 0
        };

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
        OptDefaultFormat.SelectedIndex = _opts.DefaultFormat == ".json" ? 1 : 0;
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

    private void PopulateQuickAccessToolbarOptions()
    {
        QuickAccessBelowRibbonCheckBox.IsChecked = _opts.QuickAccessToolbarBelowRibbon;
        _quickAccessCommandIds.Clear();
        _quickAccessCommandIds.AddRange(QuickAccessToolbarCatalog.NormalizeCommandIds(_opts.QuickAccessToolbarCommands));
        RefreshQuickAccessToolbarCommandLists();
    }

    private void RefreshQuickAccessToolbarCommandLists(string? selectedAvailableId = null, string? selectedQatId = null)
    {
        var selectedSet = new HashSet<string>(_quickAccessCommandIds, StringComparer.OrdinalIgnoreCase);
        var filterText = QuickAccessSearchBox.Text ?? string.Empty;
        QuickAccessAvailableCommandsList.ItemsSource = QuickAccessToolbarCatalog.Commands
            .Where(command => !selectedSet.Contains(command.Id))
            .Where(command => QuickAccessCommandMatchesFilter(command, filterText))
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

    private static bool QuickAccessCommandMatchesFilter(
        QuickAccessToolbarCommandDefinition command,
        string filterText)
    {
        var filter = filterText.Trim();
        if (string.IsNullOrEmpty(filter))
            return true;

        return QuickAccessCommandTextMatches(command.Id, filter) ||
            QuickAccessCommandTextMatches(command.CommandName, filter) ||
            QuickAccessCommandTextMatches(UiText.Get(command.TitleResourceKey), filter) ||
            QuickAccessCommandTextMatches(UiText.Get(command.DescriptionResourceKey), filter);
    }

    private static bool QuickAccessCommandTextMatches(string text, string filter) =>
        text.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    private static void SelectQuickAccessCommand(ListBox listBox, string? commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            return;

        listBox.SelectedItem = listBox.Items
            .OfType<QuickAccessCommandChoice>()
            .FirstOrDefault(choice => string.Equals(choice.Id, commandId, StringComparison.OrdinalIgnoreCase));
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

        _quickAccessCommandIds.Add(choice.Id);
        RefreshQuickAccessToolbarCommandLists(selectedQatId: choice.Id);
    }

    private void QuickAccessRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessSelectedCommandsList.SelectedItem is not QuickAccessCommandChoice choice ||
            _quickAccessCommandIds.Count <= 1)
        {
            return;
        }

        var removedIndex = _quickAccessCommandIds.FindIndex(id => string.Equals(id, choice.Id, StringComparison.OrdinalIgnoreCase));
        if (removedIndex < 0)
            return;

        _quickAccessCommandIds.RemoveAt(removedIndex);
        var nextIndex = Math.Clamp(removedIndex, 0, _quickAccessCommandIds.Count - 1);
        RefreshQuickAccessToolbarCommandLists(
            selectedAvailableId: choice.Id,
            selectedQatId: _quickAccessCommandIds.ElementAtOrDefault(nextIndex));
    }

    private void QuickAccessMoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessSelectedCommandsList.SelectedItem is not QuickAccessCommandChoice choice)
            return;

        var index = _quickAccessCommandIds.FindIndex(id => string.Equals(id, choice.Id, StringComparison.OrdinalIgnoreCase));
        if (index <= 0)
            return;

        (_quickAccessCommandIds[index - 1], _quickAccessCommandIds[index]) =
            (_quickAccessCommandIds[index], _quickAccessCommandIds[index - 1]);
        RefreshQuickAccessToolbarCommandLists(selectedQatId: choice.Id);
    }

    private void QuickAccessMoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessSelectedCommandsList.SelectedItem is not QuickAccessCommandChoice choice)
            return;

        var index = _quickAccessCommandIds.FindIndex(id => string.Equals(id, choice.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index >= _quickAccessCommandIds.Count - 1)
            return;

        (_quickAccessCommandIds[index + 1], _quickAccessCommandIds[index]) =
            (_quickAccessCommandIds[index], _quickAccessCommandIds[index + 1]);
        RefreshQuickAccessToolbarCommandLists(selectedQatId: choice.Id);
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!OptionsInputParser.TryParseDefaultFontSize(OptDefaultFontSize.Text, out var defaultFontSize))
        {
            ShowInvalidInputWarning(UiText.Get("Options_InvalidDefaultFontSizeMessage"), OptDefaultFontSize);
            return;
        }

        if (!OptionsInputParser.TryParseDefaultSheetCount(OptSheetCount.Text, out var defaultSheetCount))
        {
            ShowInvalidInputWarning(UiText.Get("Options_InvalidSheetCountMessage"), OptSheetCount);
            return;
        }

        var opts = new FreeXOptions
        {
            DefaultFontName   = OptDefaultFont.SelectedItem as string ?? _opts.DefaultFontName,
            DefaultFontSize   = defaultFontSize,
            DefaultSheetCount = defaultSheetCount,
            UserName          = string.IsNullOrWhiteSpace(OptUserName.Text) ? _opts.UserName : OptUserName.Text.Trim(),
            CollapseRibbonAutomatically = OptCollapseRibbon.IsChecked == true,
            ShowScreenTips = OptShowScreenTips.IsChecked == true,
            AutoCalculate     = OptCalcAuto.IsChecked == true,
            UseR1C1ReferenceStyle = OptR1C1.IsChecked == true,
            ShowFormulaBar     = OptShowFormulaBar.IsChecked == true,
            FormulaBarExpanded = OptShowFormulaBar.IsChecked == true && OptFormulaBarExpanded.IsChecked == true,
            MoveSelectionAfterEnter = OptMoveAfterEnter.IsChecked == true,
            AfterEnterDirection = OptAfterEnterDirection.SelectedIndex switch
            {
                1 => FreeXEnterDirection.Right,
                2 => FreeXEnterDirection.Up,
                3 => FreeXEnterDirection.Left,
                _ => FreeXEnterDirection.Down
            },
            ShowGridlines = OptShowGridlines.IsChecked == true,
            ShowHeadings = OptShowHeadings.IsChecked == true,
            ObjectsDisplay = OptObjectsDisplay.SelectedIndex switch
            {
                1 => FreeXObjectDisplay.Placeholders,
                2 => FreeXObjectDisplay.Nothing,
                _ => FreeXObjectDisplay.All
            },
            DefaultFormat     = OptDefaultFormat.SelectedIndex == 1 ? ".json" : ".xlsx",
            QuickAccessToolbarBelowRibbon = QuickAccessBelowRibbonCheckBox.IsChecked == true,
            QuickAccessToolbarCommands = QuickAccessToolbarCatalog.NormalizeCommandIds(_quickAccessCommandIds).ToList(),
            AppLanguage       = AppLanguageCatalog.NormalizeCultureName(OptAppLanguage.SelectedValue as string),
            CrashAnalyticsEnabled = OptCrashAnalytics.IsChecked == true,
            CrashAnalyticsPrompted = _opts.CrashAnalyticsPrompted || OptCrashAnalytics.IsChecked == true,
            PdfExportLanguage = ExportPlanner.NormalizePdfLanguage(_opts.PdfExportLanguage),
        };
        if (!opts.Save())
        {
            DialogMessageHelper.ShowError(this, opts.LastPersistenceError, Title);
            return;
        }

        Result = opts;
        DisabledFormulaErrorCodesResult = CollectDisabledFormulaErrorCodes();
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private bool ShowInvalidInputWarning(string message, Control target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        target.Focus();
        if (target is TextBox textBox)
            textBox.SelectAll();
        else if (target is ComboBox comboBox)
            comboBox.Focus();
        Keyboard.Focus(target);
        return true;
    }

    private void AutoCorrectOptionsButton_Click(object sender, RoutedEventArgs e) =>
        ShowDeferredOptionsMessage(DeferredCommandMessages.AutoCorrectOptions());

    private void RibbonImportExportButton_Click(object sender, RoutedEventArgs e) =>
        ShowDeferredOptionsMessage(DeferredCommandMessages.RibbonCustomizationImportExport());

    private void QuickAccessResetButton_Click(object sender, RoutedEventArgs e)
    {
        _quickAccessCommandIds.Clear();
        _quickAccessCommandIds.AddRange(QuickAccessToolbarCatalog.DefaultCommandIds);
        RefreshQuickAccessToolbarCommandLists(selectedQatId: _quickAccessCommandIds[0]);
    }

    private void AddInsGoButton_Click(object sender, RoutedEventArgs e) =>
        ShowDeferredOptionsMessage(DeferredCommandMessages.OfficeAddIns());

    private void TrustCenterSettingsButton_Click(object sender, RoutedEventArgs e) =>
        ShowDeferredOptionsMessage(DeferredCommandMessages.TrustCenterSettings());

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
