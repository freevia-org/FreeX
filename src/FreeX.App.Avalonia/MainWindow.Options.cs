using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Localization;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// File ▸ Options — the Avalonia/macOS shell's Options (Settings) dialog. The Windows host already has
/// a multi-tab <c>OptionsDialog</c> editing the shared <see cref="AppOptions"/> model; this is the
/// portable shell's counterpart. It edits the same <see cref="AppOptions"/> (loaded/saved through
/// <see cref="AppOptionsStore"/>), so settings persist across launches and are shared with the host.
///
/// <para>
/// The dialog is a left category list (General / Formulas / Proofing / View / Save) plus a right panel,
/// matching the host's tabbed layout in a compact form. All parsing, validation and projection onto
/// <see cref="AppOptions"/> live in the portable <see cref="OptionsDialogPlanner"/>; this view only
/// collects input and renders the planner's verdicts. Relevant view settings (gridlines, headings,
/// formula-bar visibility, calculation mode) are applied live to the current session on OK/Apply; the
/// rest persist for the next launch. User-facing strings route through <see cref="UiText"/>.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle OptionsDialogChromeStyle => new(FormulaBarFontFamily);

    // ── File ▸ Options entry point ──────────────────────────────────────────────
    private void ShowOptions() => _ = ShowOptionsDialogAsync();

    private async Task ShowOptionsDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        const double optionsDialogWidth = OptionsDialogPlanner.CaptureWidth;
        const double optionsDialogHeight = OptionsDialogPlanner.CaptureHeight;
        const double optionsFormulasDialogWidth = OptionsDialogPlanner.CaptureWidth;
        const double optionsFormulasDialogHeight = OptionsDialogPlanner.FormulasCaptureHeight;

        // Edit a snapshot loaded from the shared store during normal use. The capture route swaps
        // in a deterministic shared fixture so paired screenshots do not inherit user-local state.
        var current = App.ParityCaptureOptions is null
            ? AppOptionsStore.Load()
            : OptionsDialogParityFixture.Create();
        var quickAccessCommandIds = QuickAccessToolbarCatalog.NormalizeCommandIds(current.QuickAccessToolbarCommands).ToList();
        var customDictionaryWords = AppOptions.NormalizeSpellCheckCustomDictionaryWords(current.SpellCheckCustomDictionaryWords);
        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "OptionsWarningText");

        void ShowOptionsWarning(string message)
        {
            warningText.Text = message;
            warningText.IsVisible = true;
        }

        var dialog = new Window
        {
            Title = UiText.Get("Options_Title"),
            Width = optionsDialogWidth,
            Height = optionsDialogHeight,
            MinWidth = optionsDialogWidth,
            MinHeight = optionsDialogHeight,
            SizeToContent = SizeToContent.Manual,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "OptionsDialog");
        // Use the shared Windows-style dialog chrome so inherited labels and the page background
        // do not fall back to a wider Linux desktop font.
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, OptionsDialogChromeStyle);

        // ── General ─────────────────────────────────────────────────────────────
        var fontBox = new ComboBox { MinWidth = OptionsDialogPlanner.GeneralFontFieldWidth, ItemsSource = OptionsDialogPlanner.FontNames };
        fontBox.SelectedIndex = OptionsDialogPlanner.DefaultFontToIndex(current.DefaultFontName);
        ApplyOptionsComboBoxChrome(fontBox);
        AutomationProperties.SetAutomationId(fontBox, "OptionsDefaultFontComboBox");

        var fontSizeBox = new ComboBox { MinWidth = OptionsDialogPlanner.GeneralSmallFieldWidth, IsEditable = true, ItemsSource = OptionsDialogPlanner.FontSizes };
        fontSizeBox.SelectedItem = current.DefaultFontSize.ToString();
        ApplyOptionsComboBoxChrome(fontSizeBox);
        // The editable Avalonia template reserves the arrow gutter in addition to the shared
        // combo padding. Keep the WPF 80 px field while leaving the two-digit value fully visible.
        fontSizeBox.Text = current.DefaultFontSize.ToString();
        fontSizeBox.Padding = new Thickness(2, 0, 0, 0);
        fontSizeBox.HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(fontSizeBox, "OptionsDefaultFontSizeComboBox");

        var sheetCountBox = new TextBox { MinWidth = OptionsDialogPlanner.GeneralSmallFieldWidth, Text = current.DefaultSheetCount.ToString() };
        ApplyOptionsTextBoxChrome(sheetCountBox);
        AutomationProperties.SetAutomationId(sheetCountBox, "OptionsDefaultSheetCountBox");

        var userNameBox = new TextBox { Text = current.UserName };
        ApplyOptionsTextBoxChrome(userNameBox);
        AutomationProperties.SetAutomationId(userNameBox, "OptionsUserNameBox");

        var screenTipsBox = new CheckBox { Content = OptionsText("Options_ShowFeatureDescriptionsInScreenTips"), IsChecked = current.ShowScreenTips };
        ApplyOptionsCheckBoxChrome(screenTipsBox);
        screenTipsBox.Height = OptionsDialogPlanner.GeneralCheckBoxHeight;
        screenTipsBox.MinHeight = OptionsDialogPlanner.GeneralCheckBoxHeight;
        screenTipsBox.MaxHeight = OptionsDialogPlanner.GeneralCheckBoxHeight;
        screenTipsBox.Margin = new Thickness(0, 0, 0, 4);
        AutomationProperties.SetAutomationId(screenTipsBox, "OptionsShowScreenTipsCheckBox");
        var collapseRibbonBox = new CheckBox
        {
            Content = OptionsText("Options_CollapseTheRibbonAutomatically"),
            IsChecked = current.CollapseRibbonAutomatically,
        };
        ApplyOptionsCheckBoxChrome(collapseRibbonBox);
        collapseRibbonBox.Height = OptionsDialogPlanner.GeneralCheckBoxHeight;
        collapseRibbonBox.MinHeight = OptionsDialogPlanner.GeneralCheckBoxHeight;
        collapseRibbonBox.MaxHeight = OptionsDialogPlanner.GeneralCheckBoxHeight;
        collapseRibbonBox.Margin = new Thickness(0, 0, 0, 6);
        AutomationProperties.SetAutomationId(collapseRibbonBox, "OptionsCollapseRibbonAutomaticallyCheckBox");

        var generalPanel = OptionsCategoryPanel(
            OptionsDescription(OptionsText("Options_GeneralOptionsForWorkingWithFreeX"), bottomMargin: OptionsDialogPlanner.GeneralDescriptionBottomMargin),
            OptionsSectionHeader(OptionsText("Options_UserInterfaceOptions"), topMargin: 0, bottomMargin: OptionsDialogPlanner.GeneralSectionBottomMargin),
            collapseRibbonBox,
            screenTipsBox,
            OptionsSectionHeader(OptionsText("Options_WhenCreatingNewWorkbooks"), topMargin: OptionsDialogPlanner.GeneralSectionTopMargin, bottomMargin: OptionsDialogPlanner.GeneralSectionBottomMargin),
            OptionsLabeled(OptionsText("Options_DefaultFont"), fontBox, labelWidth: OptionsDialogPlanner.GeneralLabelWidth, fieldWidth: OptionsDialogPlanner.GeneralFontFieldWidth, spacing: OptionsDialogPlanner.GeneralFieldSpacing, margin: new Thickness(0, 0, 0, OptionsDialogPlanner.GeneralFieldBottomMargin)),
            OptionsLabeled(OptionsText("Options_FontSize"), fontSizeBox, labelWidth: OptionsDialogPlanner.GeneralLabelWidth, fieldWidth: OptionsDialogPlanner.GeneralSmallFieldWidth, spacing: OptionsDialogPlanner.GeneralFieldSpacing, margin: new Thickness(0, 0, 0, OptionsDialogPlanner.GeneralFieldBottomMargin)),
            OptionsLabeled(OptionsText("Options_IncludeThisManySheets"), sheetCountBox, labelWidth: OptionsDialogPlanner.GeneralLabelWidth, fieldWidth: OptionsDialogPlanner.GeneralSmallFieldWidth, spacing: OptionsDialogPlanner.GeneralFieldSpacing, margin: new Thickness(0, 0, 0, OptionsDialogPlanner.GeneralFieldBottomMargin)),
            OptionsSectionHeader(OptionsText("Options_PersonalizeYourCopyOfFreeX"), topMargin: OptionsDialogPlanner.GeneralSectionTopMargin, bottomMargin: OptionsDialogPlanner.GeneralSectionBottomMargin),
            OptionsLabeled(OptionsText("Options_UserName"), userNameBox, labelWidth: OptionsDialogPlanner.GeneralLabelWidth, stretchField: true, spacing: OptionsDialogPlanner.GeneralFieldSpacing, margin: new Thickness(0, 0, 0, OptionsDialogPlanner.GeneralUserNameBottomMargin)));
        generalPanel.MinWidth = OptionsDialogPlanner.GeneralContentWidth;
        generalPanel.Spacing = 0;

        // ── Formulas ────────────────────────────────────────────────────────────
        // Calc mode and iterative-calculation settings are workbook-level state (Workbook.
        // CalculationMode / IterativeCalculation / MaxCalculationIterations / MaxCalculationChange),
        // not a persisted app-wide default — seed them from the live session's workbook so the
        // dialog reflects whatever the ribbon's Calculation Options last set on this workbook
        // (matching Excel), not the stale on-disk AppOptions.AutoCalculate.
        var workbookAutoCalculate = !CalculationModeIsManual;
        var calcAutoButton = new RadioButton { Content = UiText.Get("Options_CalcAutomatic"), GroupName = "OptionsCalcMode", IsChecked = workbookAutoCalculate };
        ApplyOptionsRadioButtonChrome(calcAutoButton);
        AutomationProperties.SetAutomationId(calcAutoButton, "OptionsCalcAutomaticButton");
        var calcManualButton = new RadioButton { Content = UiText.Get("Options_CalcManual"), GroupName = "OptionsCalcMode", IsChecked = !workbookAutoCalculate };
        ApplyOptionsRadioButtonChrome(calcManualButton);
        AutomationProperties.SetAutomationId(calcManualButton, "OptionsCalcManualButton");

        var workbook = _session.Workbook;
        var iterativeBox = new CheckBox { Content = OptionsText("Options_EnableIterativeCalculation"), IsChecked = workbook.IterativeCalculation };
        ApplyOptionsCheckBoxChrome(iterativeBox);
        AutomationProperties.SetAutomationId(iterativeBox, "OptionsIterativeCalculationCheckBox");

        var maxIterationsBox = new TextBox
        {
            MinWidth = 80,
            Text = (workbook.MaxCalculationIterations ?? DefaultMaxCalculationIterations).ToString(),
            IsEnabled = workbook.IterativeCalculation,
        };
        ApplyOptionsTextBoxChrome(maxIterationsBox);
        AutomationProperties.SetAutomationId(maxIterationsBox, "OptionsMaxIterationsBox");

        var maxChangeBox = new TextBox
        {
            MinWidth = 90,
            Text = (workbook.MaxCalculationChange ?? DefaultMaxCalculationChange).ToString(System.Globalization.CultureInfo.InvariantCulture),
            IsEnabled = workbook.IterativeCalculation,
        };
        ApplyOptionsTextBoxChrome(maxChangeBox);
        AutomationProperties.SetAutomationId(maxChangeBox, "OptionsMaxChangeBox");

        iterativeBox.IsCheckedChanged += (_, _) =>
        {
            var enabled = iterativeBox.IsChecked == true;
            maxIterationsBox.IsEnabled = enabled;
            maxChangeBox.IsEnabled = enabled;
        };

        var r1c1Box = new CheckBox { Content = UiText.Get("Options_R1C1ReferenceStyle"), IsChecked = current.UseR1C1ReferenceStyle };
        ApplyOptionsCheckBoxChrome(r1c1Box);
        AutomationProperties.SetAutomationId(r1c1Box, "OptionsR1C1ReferenceStyleCheckBox");

        var errorCheckingBox = new CheckBox { Content = UiText.Get("Options_EnableErrorChecking"), IsChecked = current.ErrorCheckingEnabled };
        ApplyOptionsCheckBoxChrome(errorCheckingBox);
        AutomationProperties.SetAutomationId(errorCheckingBox, "OptionsEnableErrorCheckingCheckBox");
        var errorRuleBoxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        var errorRulesPanel = new StackPanel { Spacing = 0 };
        foreach (var rule in FormulaErrorCheckingRuleCatalog.SupportedRules)
        {
            var ruleBox = OptionsCheckBox(
                rule.Label,
                isChecked: !workbook.DisabledFormulaErrorCodes.Contains(rule.ErrorCode));
            ToolTip.SetTip(ruleBox, rule.Description);
            AutomationProperties.SetAutomationId(
                ruleBox,
                "OptionsFormulaErrorRule" + rule.ErrorCode.Replace("#", "", StringComparison.OrdinalIgnoreCase).Replace("/", "", StringComparison.OrdinalIgnoreCase).Replace("!", "", StringComparison.OrdinalIgnoreCase).Replace("?", "", StringComparison.OrdinalIgnoreCase));
            errorRuleBoxes[rule.ErrorCode] = ruleBox;
            errorRulesPanel.Children.Add(ruleBox);
        }

        var formulasPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_CalculationOptions")),
            new TextBlock { Text = OptionsText("Options_WorkbookCalculation"), FontWeight = FontWeight.SemiBold, FontSize = 12 },
            calcAutoButton,
            calcManualButton,
            OptionsDescription(OptionsText("Options_InManualModePressF9ToRecalculateTheWorkbook"), leftMargin: 18),
            iterativeBox,
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(18, 0, 0, 0),
                Children =
                {
                    new TextBlock { Text = OptionsText("Options_MaximumIterations"), VerticalAlignment = AvaloniaVerticalAlignment.Center, FontSize = 12 },
                    maxIterationsBox,
                    new TextBlock { Text = OptionsText("Options_MaximumChange"), VerticalAlignment = AvaloniaVerticalAlignment.Center, FontSize = 12 },
                    maxChangeBox,
                },
            },
            OptionsSectionHeader(OptionsText("Options_WorkingWithFormulas")),
            r1c1Box,
            OptionsCheckBox(OptionsText("Options_EnableAutoCompleteForCellValues"), isChecked: true, isEnabled: false),
            OptionsSectionHeader(OptionsText("Options_ErrorCheckingRules")),
            OptionsDescription(OptionsText("Options_EnableBackgroundErrorChecksFor")),
            errorCheckingBox,
            errorRulesPanel);

        // ── Proofing ────────────────────────────────────────────────────────────
        var proofingWordsList = new ListBox
        {
            Width = OptionsDialogPlanner.ProofingContentWidth,
            Height = OptionsDialogPlanner.ProofingWordsListHeight,
            ItemsSource = customDictionaryWords.ToList(),
        };
        ApplyOptionsListBoxChrome(proofingWordsList);
        AutomationProperties.SetName(proofingWordsList, OptionsText("Options_CustomDictionaryWordsAutomationName"));
        AutomationProperties.SetAutomationId(proofingWordsList, "ProofingCustomDictionaryWordsList");
        AutomationProperties.SetHelpText(proofingWordsList, OptionsText("Options_CustomDictionaryWordsHelpText"));

        var proofingWordBox = new TextBox { Height = 24 };
        ApplyOptionsTextBoxChrome(proofingWordBox);
        AutomationProperties.SetName(proofingWordBox, OptionsText("Options_CustomDictionaryWordAutomationName"));
        AutomationProperties.SetAutomationId(proofingWordBox, "ProofingCustomDictionaryWordBox");
        AutomationProperties.SetHelpText(proofingWordBox, OptionsText("Options_CustomDictionaryWordHelpText"));

        var proofingAddButton = new Button { Content = OptionsText("Options_CustomDictionaryAddWordButton"), Width = OptionsDialogPlanner.ProofingAddWordButtonWidth, Height = OptionsDialogPlanner.ButtonHeight, IsEnabled = false };
        ApplyOptionsButtonChrome(proofingAddButton, OptionsDialogPlanner.ProofingAddWordButtonWidth);
        AutomationProperties.SetName(proofingAddButton, OptionsText("Options_CustomDictionaryAddWordButtonAutomationName"));
        AutomationProperties.SetAutomationId(proofingAddButton, "ProofingCustomDictionaryAddWordButton");
        AutomationProperties.SetHelpText(proofingAddButton, OptionsText("Options_CustomDictionaryAddWordHelpText"));

        var proofingRemoveButton = new Button { Content = OptionsText("Options_CustomDictionaryRemoveWordButton"), Width = OptionsDialogPlanner.ProofingRemoveWordButtonWidth, Height = OptionsDialogPlanner.ButtonHeight, IsEnabled = false };
        ApplyOptionsButtonChrome(proofingRemoveButton, OptionsDialogPlanner.ProofingRemoveWordButtonWidth);
        AutomationProperties.SetName(proofingRemoveButton, OptionsText("Options_CustomDictionaryRemoveWordButtonAutomationName"));
        AutomationProperties.SetAutomationId(proofingRemoveButton, "ProofingCustomDictionaryRemoveWordButton");
        AutomationProperties.SetHelpText(proofingRemoveButton, OptionsText("Options_CustomDictionaryRemoveWordHelpText"));

        var proofingClearButton = new Button { Content = OptionsText("Options_CustomDictionaryClearAllButton"), Width = OptionsDialogPlanner.ProofingClearWordsButtonWidth, Height = OptionsDialogPlanner.ButtonHeight, IsEnabled = customDictionaryWords.Count > 0 };
        ApplyOptionsButtonChrome(proofingClearButton, OptionsDialogPlanner.ProofingClearWordsButtonWidth);
        AutomationProperties.SetName(proofingClearButton, OptionsText("Options_CustomDictionaryClearAllButtonAutomationName"));
        AutomationProperties.SetAutomationId(proofingClearButton, "ProofingCustomDictionaryClearWordsButton");
        AutomationProperties.SetHelpText(proofingClearButton, OptionsText("Options_CustomDictionaryClearAllHelpText"));

        void RefreshProofingWords(string? selectedWord = null)
        {
            var previous = selectedWord ?? proofingWordsList.SelectedItem as string;
            proofingWordsList.ItemsSource = customDictionaryWords.ToList();
            if (!string.IsNullOrWhiteSpace(previous))
            {
                proofingWordsList.SelectedItem = customDictionaryWords.FirstOrDefault(
                    word => string.Equals(word, previous, StringComparison.OrdinalIgnoreCase));
            }

            proofingRemoveButton.IsEnabled = proofingWordsList.SelectedItem is string;
            proofingClearButton.IsEnabled = customDictionaryWords.Count > 0;
        }

        void UpdateProofingButtons()
        {
            proofingAddButton.IsEnabled = AppOptions.NormalizeSpellCheckCustomDictionaryWord(proofingWordBox.Text) is not null;
            proofingRemoveButton.IsEnabled = proofingWordsList.SelectedItem is string;
            proofingClearButton.IsEnabled = customDictionaryWords.Count > 0;
        }

        proofingWordBox.TextChanged += (_, _) => UpdateProofingButtons();
        proofingWordsList.SelectionChanged += (_, _) => UpdateProofingButtons();
        proofingWordBox.KeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Enter or Key.Return) || !proofingAddButton.IsEnabled)
                return;

            proofingAddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            args.Handled = true;
        };
        proofingAddButton.Click += (_, _) =>
        {
            if (SpellCheckWorkflowPlanner.AddCustomDictionaryWord(customDictionaryWords, new HashSet<string>(customDictionaryWords, StringComparer.OrdinalIgnoreCase), proofingWordBox.Text ?? string.Empty))
            {
                var added = AppOptions.NormalizeSpellCheckCustomDictionaryWord(proofingWordBox.Text);
                proofingWordBox.Clear();
                RefreshProofingWords(added);
            }
            else
            {
                customDictionaryWords = AppOptions.NormalizeSpellCheckCustomDictionaryWords(customDictionaryWords);
                proofingWordBox.Clear();
                RefreshProofingWords();
            }
        };
        proofingRemoveButton.Click += (_, _) =>
        {
            if (proofingWordsList.SelectedItem is not string selected)
                return;

            var nextWord = SpellCheckWorkflowPlanner.RemoveCustomDictionaryWordAndSelectNext(
                customDictionaryWords,
                selected);
            RefreshProofingWords(nextWord);
        };
        proofingClearButton.Click += (_, _) =>
        {
            SpellCheckWorkflowPlanner.ClearCustomDictionaryWords(customDictionaryWords);
            RefreshProofingWords();
            proofingWordBox.Focus();
        };

        var proofingAddRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{OptionsDialogPlanner.ProofingAddWordLabelWidth},*,86"),
            Margin = new Thickness(0, 8, 0, 7),
        };
        var proofingAddLabel = new TextBlock
        {
            Text = OptionsText("Options_CustomDictionaryAddWord"),
            FontSize = 12,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        Grid.SetColumn(proofingAddLabel, 0);
        Grid.SetColumn(proofingWordBox, 1);
        Grid.SetColumn(proofingAddButton, 2);
        proofingAddButton.Margin = new Thickness(8, 0, 0, 0);
        proofingAddRow.Children.Add(proofingAddLabel);
        proofingAddRow.Children.Add(proofingWordBox);
        proofingAddRow.Children.Add(proofingAddButton);

        var proofingActionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 15),
            Children = { proofingRemoveButton, proofingClearButton },
        };
        var proofingWordsSection = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = OptionsText("Options_CustomDictionaryWords"), FontSize = 12 },
                proofingWordsList,
            },
        };
        var autoCorrectButton = OptionsButton(OptionsText("Options_AutoCorrectOptions2"), OptionsDialogPlanner.ProofingAutoCorrectButtonWidth);
        autoCorrectButton.Click += (_, _) => ShowOptionsWarning(UiText.Get("DeferredCommand_AutoCorrectOptions_Body"));

        var proofingChecks = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                OptionsCheckBox(OptionsText("Options_CheckSpellingAsYouType"), isChecked: true, isEnabled: false),
                OptionsCheckBox(OptionsText("Options_IgnoreWordsInUPPERCASE"), isChecked: current.ProofingIgnoreUppercase, isEnabled: false),
                OptionsCheckBox(OptionsText("Options_FlagRepeatedWords"), isEnabled: false),
            },
        };
        var proofingPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_AutoCorrectOptions"), topMargin: 0),
            proofingChecks,
            OptionsSectionHeader(OptionsText("Options_CustomDictionary"), topMargin: 30, bottomMargin: 8),
            OptionsDescription(OptionsText("Options_CustomDictionaryDescription"), bottomMargin: 8),
            proofingWordsSection,
            proofingAddRow,
            proofingActionRow,
            autoCorrectButton);
        // The WPF page uses control margins rather than a uniform StackPanel gap. Keeping this
        // page at zero spacing preserves the measured list/add/remove/footer rhythm exactly.
        proofingPanel.Spacing = 0;

        // ── View ────────────────────────────────────────────────────────────────
        var showFormulaBarBox = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("Options_ShowFormulaBar")), IsChecked = current.ShowFormulaBar };
        ApplyOptionsCheckBoxChrome(showFormulaBarBox);
        AutomationProperties.SetAutomationId(showFormulaBarBox, "OptionsShowFormulaBarCheckBox");
        var showGridlinesBox = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("Options_ShowGridlines")), IsChecked = current.ShowGridlines };
        ApplyOptionsCheckBoxChrome(showGridlinesBox);
        AutomationProperties.SetAutomationId(showGridlinesBox, "OptionsShowGridlinesCheckBox");
        var showHeadingsBox = new CheckBox { Content = UiText.Get("Options_ShowHeadings"), IsChecked = current.ShowHeadings };
        ApplyOptionsCheckBoxChrome(showHeadingsBox);
        AutomationProperties.SetAutomationId(showHeadingsBox, "OptionsShowHeadingsCheckBox");

        var viewPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_WorkbookViewOptions"), topMargin: 0, bottomMargin: 12),
            showFormulaBarBox,
            OptionsCheckBox(OptionsText("Options_ExpandFormulaBar"), isEnabled: showFormulaBarBox.IsChecked == true));
        viewPanel.Spacing = 0;

        // ── Save ────────────────────────────────────────────────────────────────
        var defaultFormatBox = new ComboBox
        {
            MinWidth = 220,
            ItemsSource = new[] { UiText.Get("Options_DefaultFormatXlsx"), UiText.Get("Options_DefaultFormatNative") },
            SelectedIndex = OptionsDialogPlanner.DefaultFormatToIndex(current.DefaultFormat),
        };
        ApplyOptionsComboBoxChrome(defaultFormatBox);
        AutomationProperties.SetAutomationId(defaultFormatBox, "OptionsDefaultFormatComboBox");

        var savePanel = OptionsCategoryPanel(
            OptionsSectionHeader(
                OptionsText("Options_SaveWorkbooks"),
                topMargin: 0,
                bottomMargin: OptionsDialogPlanner.GeneralSectionBottomMargin),
            OptionsLabeled(
                OptionsText("Options_SaveFilesInThisFormat"),
                defaultFormatBox,
                labelWidth: OptionsDialogPlanner.GeneralLabelWidth,
                fieldWidth: OptionsDialogPlanner.GeneralFontFieldWidth,
                spacing: OptionsDialogPlanner.GeneralFieldSpacing,
                margin: new Thickness(0, 0, 0, OptionsDialogPlanner.GeneralFieldBottomMargin)),
            OptionsSectionHeader(
                OptionsText("Options_FileLocations"),
                topMargin: OptionsDialogPlanner.GeneralSectionTopMargin,
                bottomMargin: OptionsDialogPlanner.GeneralSectionBottomMargin),
            OptionsLabeled(OptionsText("Options_RecentFilesLocation"), OptionsReadOnlyTextBox(
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FreeX",
                    "recent.json"),
                minWidth: 0),
                labelWidth: OptionsDialogPlanner.GeneralLabelWidth,
                stretchField: true,
                margin: new Thickness(0, 0, 0, OptionsDialogPlanner.GeneralFieldBottomMargin)));

        savePanel.Spacing = 0;

        var languageOptions = AppLanguageCatalog.GetAvailableLanguages().ToList();
        var normalizedLanguage = AppLanguageCatalog.NormalizeCultureName(current.AppLanguage);
        var languageBox = new ComboBox
        {
            ItemsSource = languageOptions.Select(option => option.DisplayName).ToList(),
            SelectedIndex = Math.Max(
                0,
                languageOptions.FindIndex(option =>
                    string.Equals(option.CultureName, normalizedLanguage, StringComparison.OrdinalIgnoreCase))),
            Width = OptionsDialogPlanner.LanguageFieldWidth,
        };
        ApplyOptionsComboBoxChrome(languageBox);
        AutomationProperties.SetAutomationId(languageBox, "OptionsAppLanguageComboBox");
        AutomationProperties.SetName(languageBox, OptionsText("Options_AppLanguage"));
        AutomationProperties.SetHelpText(languageBox, OptionsText("Options_AppLanguageHelpText"));

        var languagePanel = OptionsCategoryPanel(
            OptionsSectionHeader(
                OptionsText("Options_ChooseDisplayLanguage"),
                topMargin: OptionsDialogPlanner.LanguageSectionTopMargin,
                bottomMargin: OptionsDialogPlanner.LanguageSectionBottomMargin),
            OptionsLabeled(
                OptionsText("Options_AppLanguage"),
                languageBox,
                labelWidth: OptionsDialogPlanner.GeneralLabelWidth,
                fieldWidth: OptionsDialogPlanner.LanguageFieldWidth,
                spacing: OptionsDialogPlanner.GeneralFieldSpacing,
                margin: new Thickness(0, 0, 0, OptionsDialogPlanner.LanguageFieldBottomMargin)),
            OptionsDescription(
                OptionsText("Options_AppLanguageRestartNotice"),
                topMargin: OptionsDialogPlanner.LanguageDescriptionTopMargin));
        languagePanel.Spacing = 0;

        var easePanel = OptionsCategoryPanel(
            OptionsSectionHeader(
                OptionsText("Options_EaseOfAccessOptions"),
                topMargin: OptionsDialogPlanner.EaseSectionHeaderTopMargin,
                bottomMargin: OptionsDialogPlanner.EaseSectionHeaderBottomMargin,
                ruleTopMargin: OptionsDialogPlanner.EaseSectionRuleTopMargin,
                ruleBottomMargin: OptionsDialogPlanner.EaseSectionRuleBottomMargin),
            WithMargin(
                OptionsCheckBox(OptionsText("Options_ProvideFeedbackWithSound"), isEnabled: false, height: OptionsDialogPlanner.EaseCheckBoxHeight, preserveDisabledContrast: true),
                new Thickness(0, 0, 0, OptionsDialogPlanner.EaseCheckBoxBottomMargin)),
            WithMargin(
                OptionsCheckBox(OptionsText("Options_ShowQuickAnalysisOptionsOnSelection"), isChecked: true, isEnabled: false, height: OptionsDialogPlanner.EaseCheckBoxHeight, preserveDisabledContrast: true),
                new Thickness(0, 0, 0, OptionsDialogPlanner.EaseCheckBoxBottomMargin)),
            WithMargin(
                OptionsCheckBox(OptionsText("Options_OptimizeDisplayForAccessibility"), isEnabled: false, height: OptionsDialogPlanner.EaseCheckBoxHeight, preserveDisabledContrast: true),
                new Thickness(0, 0, 0, OptionsDialogPlanner.EaseCheckBoxBottomMargin)));
        // WPF's Ease page uses explicit checkbox margins, not the generic category gap.
        easePanel.Spacing = 0;

        // ── Advanced (editing options) ────────────────────────────────────────────
        // The "After pressing Enter, move selection" toggle + its direction picker edit the persisted
        // AppOptions.MoveSelectionAfterEnter / AfterEnterDirection, which the shell forwards into
        // ExcelEditKeyPlanner.GetIntent so Enter moves in the chosen direction (or not at all). Mirrors
        // the WPF host's OptionsDialog (OptMoveAfterEnter / OptAfterEnterDirection).
        var moveAfterEnterBox = OptionsCheckBox(
            OptionsText("Options_AfterPressingEnterMoveSelection"),
            isChecked: current.MoveSelectionAfterEnter);
        AutomationProperties.SetAutomationId(moveAfterEnterBox, "OptionsMoveSelectionAfterEnterCheckBox");

        var afterEnterDirectionBox = OptionsComboBox(
            new[]
            {
                OptionsText("Options_AfterEnterDirectionDown"),
                OptionsText("Options_AfterEnterDirectionRight"),
                OptionsText("Options_AfterEnterDirectionUp"),
                OptionsText("Options_AfterEnterDirectionLeft"),
            },
            selectedIndex: OptionsDialogPlanner.AfterEnterDirectionToIndex(current.AfterEnterDirection),
            isEnabled: current.MoveSelectionAfterEnter,
            minWidth: 140);
        AutomationProperties.SetAutomationId(afterEnterDirectionBox, "OptionsAfterEnterDirectionComboBox");

        // Match the WPF host's UpdateAfterEnterDirectionState: the direction only applies when the
        // move-after-Enter toggle is on, so gray the picker out whenever the checkbox is cleared.
        moveAfterEnterBox.IsCheckedChanged += (_, _) =>
            afterEnterDirectionBox.IsEnabled = moveAfterEnterBox.IsChecked == true;

        var advancedFillHandleBox = OptionsCheckBox(
            OptionsText("Options_EnableFillHandleAndCellDragAndDrop"),
            isChecked: current.EnableFillHandleAndCellDragAndDrop);
        AutomationProperties.SetAutomationId(advancedFillHandleBox, "OptionsEnableFillHandleAndCellDragAndDropCheckBox");
        var advancedAutoCompleteBox = OptionsCheckBox(
            OptionsText("Options_EnableAutoCompleteForCellValues"),
            isChecked: current.EnableAutoCompleteForCellValues);
        var objectsDisplayBox = OptionsComboBox(
            new[]
            {
                OptionsText("Options_ObjectsDisplayAll"),
                OptionsText("Options_ObjectsDisplayPlaceholders"),
                OptionsText("Options_ObjectsDisplayNothing"),
            },
            selectedIndex: current.ObjectsDisplay switch
            {
                AppOptionsObjectDisplay.Placeholders => 1,
                AppOptionsObjectDisplay.Nothing => 2,
                _ => 0,
            },
            isEnabled: true,
            minWidth: OptionsDialogPlanner.AdvancedObjectsControlWidth);
        AutomationProperties.SetAutomationId(objectsDisplayBox, "OptionsObjectsDisplayComboBox");

        var advancedDirectionRow = OptionsLabeled(
            OptionsText("Options_Direction"),
            afterEnterDirectionBox,
            labelWidth: OptionsDialogPlanner.AdvancedDirectionLabelWidth,
            fieldWidth: OptionsDialogPlanner.AdvancedDirectionControlWidth,
            spacing: 0,
            margin: new Thickness(
                OptionsDialogPlanner.AdvancedDirectionLeftMargin,
                0,
                0,
                OptionsDialogPlanner.AdvancedDirectionBottomMargin));
        var advancedObjectsRow = OptionsLabeled(
            OptionsText("Options_ForObjectsShow"),
            objectsDisplayBox,
            labelWidth: OptionsDialogPlanner.AdvancedObjectsLabelWidth,
            fieldWidth: OptionsDialogPlanner.AdvancedObjectsControlWidth,
            spacing: 0,
            margin: new Thickness(0, 0, 0, OptionsDialogPlanner.AdvancedObjectsBottomMargin));

        var advancedPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_EditingOptions"), topMargin: 0),
            WithMargin(moveAfterEnterBox, new Thickness(0, 0, 0, OptionsDialogPlanner.AdvancedMoveAfterEnterBottomMargin)),
            advancedDirectionRow,
            WithMargin(advancedFillHandleBox, new Thickness(0, 0, 0, OptionsDialogPlanner.AdvancedDisabledFillHandleBottomMargin)),
            WithMargin(advancedAutoCompleteBox, new Thickness(0, 0, 0, OptionsDialogPlanner.AdvancedAutoCompleteBottomMargin)),
            OptionsSectionHeader(OptionsText("Options_DisplayOptionsForThisWorkbook"), topMargin: OptionsDialogPlanner.AdvancedDisplaySectionTopMargin),
            WithMargin(showGridlinesBox, new Thickness(0, 0, 0, OptionsDialogPlanner.AdvancedGridlinesBottomMargin)),
            WithMargin(showHeadingsBox, new Thickness(0, 0, 0, OptionsDialogPlanner.AdvancedHeadingsBottomMargin)),
            advancedObjectsRow);
        advancedPanel.Spacing = 0;

        var customizeRibbonImportExportButton = OptionsButton(OptionsText("Options_ImportExport"), width: 130);
        AutomationProperties.SetAutomationId(customizeRibbonImportExportButton, "RibbonImportExportButton");
        customizeRibbonImportExportButton.Click += async (_, _) =>
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                dialog,
                UiText.Get("DeferredCommand_RibbonCustomization_Body"),
                UiText.Get("DeferredCommand_RibbonCustomization_Title"));

        var customizeRibbonPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_CustomizeTheRibbon")),
            OptionsDescription(OptionsText("Options_ChooseCommandsFromPopularCommands")),
            customizeRibbonImportExportButton);

        var quickAccessBelowRibbonBox = new CheckBox
        {
            Content = OptionsText("Options_ShowQuickAccessToolbarBelowTheRibbon"),
            IsChecked = current.QuickAccessToolbarBelowRibbon,
        };
        ApplyOptionsCheckBoxChrome(quickAccessBelowRibbonBox);
        AutomationProperties.SetAutomationId(quickAccessBelowRibbonBox, "QuickAccessToolbarBelowRibbonCheckBox");
        AutomationProperties.SetHelpText(quickAccessBelowRibbonBox, OptionsText("Options_QuickAccessBelowRibbonHelpText"));

        var quickAccessSearchBox = new TextBox { Height = 24 };
        ApplyOptionsTextBoxChrome(quickAccessSearchBox);
        AutomationProperties.SetName(quickAccessSearchBox, OptionsText("AutoFilter_Search3"));
        AutomationProperties.SetAutomationId(quickAccessSearchBox, "QuickAccessToolbarCommandSearchBox");
        AutomationProperties.SetHelpText(quickAccessSearchBox, OptionsText("Options_QuickAccessSearchHelpText"));

        var quickAccessAvailableList = new ListBox { Height = 180 };
        ApplyOptionsListBoxChrome(quickAccessAvailableList);
        AutomationProperties.SetName(quickAccessAvailableList, OptionsText("Options_AvailableCommandsAutomationName"));
        AutomationProperties.SetAutomationId(quickAccessAvailableList, "QuickAccessToolbarAvailableCommandsList");
        AutomationProperties.SetHelpText(quickAccessAvailableList, OptionsText("Options_QuickAccessAvailableCommandsHelpText"));

        var quickAccessSelectedList = new ListBox { Height = 180 };
        ApplyOptionsListBoxChrome(quickAccessSelectedList);
        AutomationProperties.SetName(quickAccessSelectedList, OptionsText("Options_QuickAccessToolbarCommandsAutomationName"));
        AutomationProperties.SetAutomationId(quickAccessSelectedList, "QuickAccessToolbarSelectedCommandsList");
        AutomationProperties.SetHelpText(quickAccessSelectedList, OptionsText("Options_QuickAccessSelectedCommandsHelpText"));

        Button MakeQuickAccessButton(string text, string automationId, double width = 92)
        {
            var button = new Button { Content = text, Width = width, Height = 26 };
            ApplyOptionsButtonChrome(button, width);
            AutomationProperties.SetAutomationId(button, automationId);
            var helpKey = automationId switch
            {
                "QuickAccessToolbarAddCommandButton" => "Options_QuickAccessAddCommandHelpText",
                "QuickAccessToolbarRemoveCommandButton" => "Options_QuickAccessRemoveCommandHelpText",
                "QuickAccessToolbarMoveUpButton" => "Options_QuickAccessMoveUpHelpText",
                "QuickAccessToolbarMoveDownButton" => "Options_QuickAccessMoveDownHelpText",
                "QuickAccessToolbarResetButton" => "Options_QuickAccessResetHelpText",
                "QuickAccessToolbarImportExportButton" => "Options_QuickAccessImportExportHelpText",
                _ => null,
            };
            if (helpKey is not null)
                AutomationProperties.SetHelpText(button, OptionsText(helpKey));
            return button;
        }

        var quickAccessAddButton = MakeQuickAccessButton(OptionsText("Options_Add"), "QuickAccessToolbarAddCommandButton");
        var quickAccessRemoveButton = MakeQuickAccessButton(OptionsText("Options_Remove"), "QuickAccessToolbarRemoveCommandButton");
        var quickAccessMoveUpButton = MakeQuickAccessButton(OptionsText("Options_MoveUp"), "QuickAccessToolbarMoveUpButton");
        var quickAccessMoveDownButton = MakeQuickAccessButton(OptionsText("Options_MoveDown"), "QuickAccessToolbarMoveDownButton");
        var quickAccessResetButton = MakeQuickAccessButton(OptionsText("Options_Reset"), "QuickAccessToolbarResetButton");
        var quickAccessImportExportButton = MakeQuickAccessButton(OptionsText("Options_ImportExport"), "QuickAccessToolbarImportExportButton", 130);
        quickAccessImportExportButton.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;

        void UpdateQuickAccessButtons()
        {
            quickAccessAddButton.IsEnabled = quickAccessAvailableList.SelectedItem is OptionsQuickAccessCommandChoice;
            quickAccessRemoveButton.IsEnabled = quickAccessSelectedList.SelectedItem is OptionsQuickAccessCommandChoice && quickAccessCommandIds.Count > 1;
            quickAccessMoveUpButton.IsEnabled = quickAccessSelectedList.SelectedIndex > 0;
            quickAccessMoveDownButton.IsEnabled = quickAccessSelectedList.SelectedIndex >= 0 && quickAccessSelectedList.SelectedIndex < quickAccessCommandIds.Count - 1;
        }

        void RefreshQuickAccessLists(string? selectedAvailableId = null, string? selectedCommandId = null)
        {
            var filter = quickAccessSearchBox.Text?.Trim() ?? string.Empty;
            var available = QuickAccessToolbarCustomizationPlanner.FilterAvailable(
                    quickAccessCommandIds,
                    filter,
                    command => [UiText.Get(command.TitleResourceKey), UiText.Get(command.DescriptionResourceKey)])
                .Select(command => new OptionsQuickAccessCommandChoice(command.Id, UiText.Get(command.TitleResourceKey)))
                .ToList();
            var selected = quickAccessCommandIds
                .Select(id => QuickAccessToolbarCatalog.TryGet(id, out var command) ? command : null)
                .Where(command => command is not null)
                .Select(command => new OptionsQuickAccessCommandChoice(command!.Id, UiText.Get(command.TitleResourceKey)))
                .ToList();
            quickAccessAvailableList.ItemsSource = available;
            quickAccessSelectedList.ItemsSource = selected;
            if (!string.IsNullOrWhiteSpace(selectedAvailableId))
                quickAccessAvailableList.SelectedItem = available.FirstOrDefault(item => string.Equals(item.Id, selectedAvailableId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(selectedCommandId))
                quickAccessSelectedList.SelectedItem = selected.FirstOrDefault(item => string.Equals(item.Id, selectedCommandId, StringComparison.OrdinalIgnoreCase));
            UpdateQuickAccessButtons();
        }

        void AddQuickAccessCommand()
        {
            if (quickAccessAvailableList.SelectedItem is not OptionsQuickAccessCommandChoice choice)
                return;
            quickAccessCommandIds = QuickAccessToolbarCustomizationPlanner.Apply(
                quickAccessCommandIds,
                choice.Id,
                QuickAccessToolbarCustomizationAction.Add).ToList();
            RefreshQuickAccessLists(selectedCommandId: choice.Id);
        }

        void RemoveQuickAccessCommand()
        {
            if (quickAccessSelectedList.SelectedItem is not OptionsQuickAccessCommandChoice choice || quickAccessCommandIds.Count <= 1)
                return;
            var index = quickAccessCommandIds.FindIndex(id => string.Equals(id, choice.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;
            quickAccessCommandIds = QuickAccessToolbarCustomizationPlanner.Apply(
                quickAccessCommandIds,
                choice.Id,
                QuickAccessToolbarCustomizationAction.Remove).ToList();
            var nextIndex = Math.Clamp(index, 0, quickAccessCommandIds.Count - 1);
            RefreshQuickAccessLists(selectedCommandId: quickAccessCommandIds[nextIndex]);
        }

        void MoveQuickAccessCommand(int delta)
        {
            if (quickAccessSelectedList.SelectedItem is not OptionsQuickAccessCommandChoice choice)
                return;
            var index = quickAccessCommandIds.FindIndex(id => string.Equals(id, choice.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;
            quickAccessCommandIds = QuickAccessToolbarCustomizationPlanner.Move(
                quickAccessCommandIds,
                choice.Id,
                delta).ToList();
            RefreshQuickAccessLists(selectedCommandId: choice.Id);
        }

        quickAccessSearchBox.TextChanged += (_, _) =>
        {
            var selectedAvailableId = (quickAccessAvailableList.SelectedItem as OptionsQuickAccessCommandChoice)?.Id;
            var selectedCommandId = (quickAccessSelectedList.SelectedItem as OptionsQuickAccessCommandChoice)?.Id;
            RefreshQuickAccessLists(selectedAvailableId, selectedCommandId);
        };
        quickAccessAvailableList.SelectionChanged += (_, _) => UpdateQuickAccessButtons();
        quickAccessSelectedList.SelectionChanged += (_, _) => UpdateQuickAccessButtons();
        quickAccessAvailableList.DoubleTapped += (_, _) => AddQuickAccessCommand();
        quickAccessSelectedList.DoubleTapped += (_, _) => RemoveQuickAccessCommand();
        quickAccessAvailableList.KeyDown += (_, args) =>
        {
            if (args.Key is Key.Enter or Key.Return)
            {
                AddQuickAccessCommand();
                args.Handled = true;
            }
        };
        quickAccessSelectedList.KeyDown += (_, args) =>
        {
            if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key == Key.Up)
            {
                MoveQuickAccessCommand(-1);
                args.Handled = true;
            }
            else if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key == Key.Down)
            {
                MoveQuickAccessCommand(1);
                args.Handled = true;
            }
            else if (args.Key is Key.Delete or Key.Back)
            {
                RemoveQuickAccessCommand();
                args.Handled = true;
            }
        };
        quickAccessAddButton.Click += (_, _) => AddQuickAccessCommand();
        quickAccessRemoveButton.Click += (_, _) => RemoveQuickAccessCommand();
        quickAccessMoveUpButton.Click += (_, _) => MoveQuickAccessCommand(-1);
        quickAccessMoveDownButton.Click += (_, _) => MoveQuickAccessCommand(1);
        quickAccessResetButton.Click += (_, _) =>
        {
            quickAccessCommandIds = QuickAccessToolbarCustomizationPlanner.Reset().ToList();
            RefreshQuickAccessLists(selectedCommandId: quickAccessCommandIds[0]);
        };

        async Task ImportQuickAccessCustomizationAsync()
        {
            try
            {
                var picker = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
                    StorageProvider,
                    AvaloniaFilePickerOpenRequest.FromFileTypes(
                        OptionsText("Options_QuickAccessToolbar"),
                        [new FilePickerFileType("FreeX Quick Access Toolbar")
                        {
                            Patterns = QuickAccessToolbarCustomizationFile.FilePickerPatterns,
                        }]));
                if (picker is null)
                    return;
                using (picker)
                {
                    if (string.IsNullOrWhiteSpace(picker.LocalPath))
                    {
                        ShowOptionsWarning("Quick Access Toolbar import requires a local file path.");
                        return;
                    }
                    var result = QuickAccessToolbarCustomizationFile.TryLoad(picker.LocalPath);
                    if (!result.Success || result.Customization is null)
                    {
                        ShowOptionsWarning(result.ErrorMessage ?? "Could not import Quick Access Toolbar customization.");
                        return;
                    }
                    quickAccessCommandIds = result.Customization.CommandIds.ToList();
                    quickAccessBelowRibbonBox.IsChecked = result.Customization.QuickAccessToolbarBelowRibbon;
                    RefreshQuickAccessLists(selectedCommandId: quickAccessCommandIds[0]);
                }
            }
            catch (Exception ex)
            {
                ShowOptionsWarning(ex.Message);
            }
        }

        async Task ExportQuickAccessCustomizationAsync()
        {
            try
            {
                var picker = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
                    StorageProvider,
                    AvaloniaFilePickerSaveRequest.FromFileTypes(
                        OptionsText("Options_QuickAccessToolbar"),
                        [new FilePickerFileType("FreeX Quick Access Toolbar")
                        {
                            Patterns = QuickAccessToolbarCustomizationFile.FilePickerPatterns,
                        }],
                        QuickAccessToolbarCustomizationFile.DefaultFileName,
                        "freex-qat.json",
                        showOverwritePrompt: true,
                        suggestFirstFileType: true));
                if (picker is null)
                    return;
                using (picker)
                {
                    string? errorMessage = null;
                    if (string.IsNullOrWhiteSpace(picker.LocalPath) ||
                        !QuickAccessToolbarCustomizationFile.TrySave(
                            picker.LocalPath,
                            quickAccessCommandIds,
                            quickAccessBelowRibbonBox.IsChecked == true,
                            out errorMessage))
                    {
                        ShowOptionsWarning(errorMessage ?? "Quick Access Toolbar export requires a local file path.");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowOptionsWarning(ex.Message);
            }
        }

        var importItem = new MenuItem { Header = QuickAccessToolbarCustomizationFile.ImportMenuHeader.TrimStart('_') };
        AutomationProperties.SetAutomationId(importItem, "QuickAccessToolbarImportCustomizationMenuItem");
        importItem.Click += async (_, _) => await ImportQuickAccessCustomizationAsync();
        var exportItem = new MenuItem { Header = QuickAccessToolbarCustomizationFile.ExportMenuHeader.TrimStart('_') };
        AutomationProperties.SetAutomationId(exportItem, "QuickAccessToolbarExportCustomizationMenuItem");
        exportItem.Click += async (_, _) => await ExportQuickAccessCustomizationAsync();
        var quickAccessImportExportMenu = new ContextMenu { Items = { importItem, exportItem } };
        quickAccessImportExportButton.Click += (_, _) => quickAccessImportExportMenu.Open(quickAccessImportExportButton);

        var quickAccessGrid = new Grid
        {
            Width = 469,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            ColumnDefinitions = new ColumnDefinitions("128,10,92,10,127,10,92"),
            RowDefinitions = new RowDefinitions("Auto,Auto,180"),
        };
        var availableLabel = new TextBlock { Text = OptionsText("Options_AvailableCommands"), FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetColumn(availableLabel, 0);
        Grid.SetRow(availableLabel, 0);
        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 8) };
        var searchLabel = new TextBlock { Text = OptionsText("AutoFilter_Search2"), FontSize = 12, VerticalAlignment = AvaloniaVerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(searchLabel, 0);
        Grid.SetColumn(quickAccessSearchBox, 1);
        searchRow.Children.Add(searchLabel);
        searchRow.Children.Add(quickAccessSearchBox);
        Grid.SetColumn(searchRow, 0);
        Grid.SetRow(searchRow, 1);
        Grid.SetColumn(quickAccessAvailableList, 0);
        Grid.SetRow(quickAccessAvailableList, 2);
        var addRemovePanel = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(10, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
            Children = { quickAccessAddButton, quickAccessRemoveButton },
        };
        Grid.SetColumn(addRemovePanel, 2);
        Grid.SetRow(addRemovePanel, 2);
        var selectedLabel = new TextBlock { Text = OptionsText("Options_QuickAccessToolbarCommands"), FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetColumn(selectedLabel, 4);
        Grid.SetRow(selectedLabel, 0);
        Grid.SetColumn(quickAccessSelectedList, 4);
        Grid.SetRow(quickAccessSelectedList, 2);
        var reorderPanel = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
            Children = { quickAccessMoveUpButton, quickAccessMoveDownButton, quickAccessResetButton },
        };
        Grid.SetColumn(reorderPanel, 6);
        Grid.SetRow(reorderPanel, 2);
        quickAccessGrid.Children.Add(availableLabel);
        quickAccessGrid.Children.Add(searchRow);
        quickAccessGrid.Children.Add(quickAccessAvailableList);
        quickAccessGrid.Children.Add(addRemovePanel);
        quickAccessGrid.Children.Add(selectedLabel);
        quickAccessGrid.Children.Add(quickAccessSelectedList);
        quickAccessGrid.Children.Add(reorderPanel);
        RefreshQuickAccessLists();

        var quickAccessPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_CustomizeTheQuickAccessToolbar"), topMargin: 0),
            quickAccessBelowRibbonBox,
            quickAccessGrid,
            quickAccessImportExportButton);

        var addInsPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_ViewAndManageAddIns")),
            OptionsDescription(OptionsText("Options_ActiveApplicationAddIns")),
            OptionsButton(OptionsText("Options_Go"), width: 72, isEnabled: false));

        var crashAnalyticsBox = new CheckBox
        {
            Content = OptionsText("Options_SendOptInCrashReports"),
            IsChecked = current.CrashAnalyticsEnabled,
        };
        ApplyOptionsCheckBoxChrome(crashAnalyticsBox);
        AutomationProperties.SetAutomationId(crashAnalyticsBox, "OptionsCrashAnalyticsCheckBox");
        AutomationProperties.SetHelpText(
            crashAnalyticsBox,
            OptionsText("Options_CrashReportsIncludeAppVersionRuntimeOperatingSystemSessi"));

        var trustCenterSettingsButton = OptionsButton(OptionsText("Options_TrustCenterSettings"), width: 170);
        trustCenterSettingsButton.Click += async (_, _) =>
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                dialog,
                UiText.Get("DeferredCommand_TrustCenter_Body"),
                UiText.Get("DeferredCommand_TrustCenter_Title"));

        var trustCenterPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_TrustCenter2")),
            OptionsDescription(OptionsText("Options_SecurityAndPrivacySettingsForFreeX")),
            crashAnalyticsBox,
            OptionsDescription(OptionsText("Options_CrashReportsAreSentOnlyWhenThisOptionIsEnabledAndTheTest")),
            OptionsSectionHeader(OptionsText("Options_LocalTesterDiagnostics")),
            OptionsDescription(OptionsText("Options_FreeXWritesLocalUsageEventsAndCrashFilesToLOCALAPPDATAFr")),
            trustCenterSettingsButton);
        trustCenterPanel.Width = OptionsDialogPlanner.GeneralContentWidth;

        // ── Category list + content host ──────────────────────────────────────────
        var panels = new[]
        {
            generalPanel,
            formulasPanel,
            proofingPanel,
            savePanel,
            languagePanel,
            easePanel,
            advancedPanel,
            customizeRibbonPanel,
            quickAccessPanel,
            addInsPanel,
            trustCenterPanel,
            viewPanel,
        };
        var contentHost = new ContentControl { Content = generalPanel };
        AutomationProperties.SetAutomationId(contentHost, "OptionsContentHost");

        var categoryNames = new[]
        {
            UiText.Get("Options_CategoryGeneral"),
            UiText.Get("Options_CategoryFormulas"),
            UiText.Get("Options_CategoryProofing"),
            UiText.Get("Options_CategorySave"),
            OptionsText("Options_CategoryLanguage"),
            OptionsText("Options_CategoryEaseOfAccess"),
            OptionsText("Options_CategoryAdvanced"),
            OptionsText("Options_CategoryCustomizeRibbon"),
            OptionsText("Options_CategoryQuickAccessToolbar"),
            OptionsText("Options_CategoryAddIns"),
            OptionsText("Options_CategoryTrustCenter"),
            UiText.Get("Options_CategoryView"),
        };
        var categoryRows = new Border[categoryNames.Length];
        var selectedCategoryIndex = 0;
        var categoryList = new StackPanel
        {
            Background = Brush(245, 245, 245),
            Margin = new Thickness(0, OptionsDialogPlanner.CategoryTopMargin, 0, 0),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(categoryList, "OptionsCategoryList");
        AutomationProperties.SetName(categoryList, UiText.Get("Options_OptionsCategories"));
        AutomationProperties.SetHelpText(categoryList, UiText.Get("Options_SelectAFreeXOptionsCategory"));

        for (var i = 0; i < categoryNames.Length; i++)
        {
            var index = i;
            // Avalonia rounds each row independently. Match WPF's 37.36 px logical row at
            // 96 DPI by carrying the first five rows at 38 px and the remaining rows at 37 px.
            var categoryItemHeight = index < 5
                ? Math.Ceiling(OptionsDialogPlanner.CategoryItemHeight)
                : Math.Floor(OptionsDialogPlanner.CategoryItemHeight);
            var row = new Border
            {
                Padding = new Thickness(
                    OptionsDialogPlanner.CategoryItemHorizontalPadding,
                    OptionsDialogPlanner.CategoryItemVerticalPadding),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Margin = new Thickness(1, 0, 1, 0),
                Height = categoryItemHeight,
                MinHeight = categoryItemHeight,
                MaxHeight = categoryItemHeight,
                Child = new TextBlock
                {
                    Text = categoryNames[i],
                    FontSize = 13,
                    Foreground = Brushes.Black,
                    FontFamily = OptionsDialogChromeStyle.FontFamily,
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                },
                Focusable = true,
                IsTabStop = i == 0,
            };
            row.PointerPressed += (_, args) =>
            {
                row.Focus();
                SelectCategory(index);
                args.Handled = true;
            };
            row.KeyDown += (_, args) =>
            {
                var nextIndex = args.Key switch
                {
                    Key.Up or Key.Left => index - 1,
                    Key.Down or Key.Right => index + 1,
                    Key.Home => 0,
                    Key.End => categoryRows.Length - 1,
                    Key.Enter or Key.Space => index,
                    _ => -1,
                };
                if (nextIndex < 0 || nextIndex >= categoryNames.Length)
                    return;

                SelectCategory(nextIndex);
                categoryRows[nextIndex].Focus();
                args.Handled = true;
            };
            row.PointerEntered += (_, _) =>
            {
                if (selectedCategoryIndex != index)
                    row.Background = Brush(232, 232, 232);
            };
            row.PointerExited += (_, _) =>
            {
                if (selectedCategoryIndex != index)
                    row.Background = Brushes.Transparent;
            };
            categoryRows[i] = row;
            categoryList.Children.Add(row);
        }
        SelectCategory(0);
        dialog.Opened += (_, _) => categoryRows[0].Focus();
        // Expose the category selector so the parity capture can switch left-list categories (which are
        // Border rows in this StackPanel, not a TabControl) to render one PNG per category.
        categoryList.Tag = (Action<int>)SelectCategory;

        void SelectCategory(int index)
        {
            if (index < 0 || index >= panels.Length)
                return;

            selectedCategoryIndex = index;
            ApplyOptionsDialogFrameForCategory(index);
            contentHost.Content = panels[index];
            for (var i = 0; i < categoryRows.Length; i++)
            {
                var selected = i == selectedCategoryIndex;
                categoryRows[i].Background = selected ? Brushes.White : Brushes.Transparent;
                categoryRows[i].BorderBrush = selected ? Brush(160, 160, 160) : Brushes.Transparent;
            }
        }

        void ApplyOptionsDialogFrameForCategory(int index)
        {
            if (index == 1)
            {
                dialog.Width = optionsFormulasDialogWidth;
                dialog.Height = optionsFormulasDialogHeight;
                dialog.MinWidth = optionsFormulasDialogWidth;
                dialog.MinHeight = optionsFormulasDialogHeight;
                return;
            }

            dialog.Width = optionsDialogWidth;
            dialog.Height = optionsDialogHeight;
            dialog.MinWidth = optionsDialogWidth;
            dialog.MinHeight = optionsDialogHeight;
        }

        // ── Warning + buttons ─────────────────────────────────────────────────────
        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, Width = OptionsDialogPlanner.FooterButtonWidth };
        ApplyOptionsButtonChrome(okButton, OptionsDialogPlanner.FooterButtonWidth, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "OptionsOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, Width = OptionsDialogPlanner.FooterButtonWidth };
        ApplyOptionsButtonChrome(cancelButton, OptionsDialogPlanner.FooterButtonWidth);
        AutomationProperties.SetAutomationId(cancelButton, "OptionsCancelButton");

        bool ApplyFormulaErrorCheckingOptions()
        {
            foreach (var rule in FormulaErrorCheckingRuleCatalog.SupportedRules)
            {
                if (!errorRuleBoxes.TryGetValue(rule.ErrorCode, out var box))
                    continue;

                var shouldDisable = box.IsChecked != true;
                var isDisabled = workbook.DisabledFormulaErrorCodes.Contains(rule.ErrorCode);
                if (shouldDisable == isDisabled)
                    continue;

                var result = _session.ExecuteReviewCommand(
                    new SetFormulaErrorCheckingRuleCommand(rule.ErrorCode, enabled: !shouldDisable));
                if (result.Success)
                    continue;

                warningText.Text = result.ErrorMessage ?? UiText.Get("Options_SaveFailed");
                warningText.IsVisible = true;
                return false;
            }

            return true;
        }

        bool TryCommit()
        {
            warningText.IsVisible = false;

            if (!OptionsDialogPlanner.TryBuildInput(
                    fontBox.SelectedItem as string,
                    fontSizeBox.SelectedItem as string ?? fontSizeBox.Text,
                    sheetCountBox.Text,
                    userNameBox.Text,
                    calcAutoButton.IsChecked == true,
                    r1c1Box.IsChecked == true,
                    errorCheckingBox.IsChecked == true,
                    current.ProofingIgnoreUppercase,
                    current.ProofingIgnoreNumbers,
                    showFormulaBarBox.IsChecked == true,
                    showGridlinesBox.IsChecked == true,
                    showHeadingsBox.IsChecked == true,
                    OptionsDialogPlanner.IndexToDefaultFormat(defaultFormatBox.SelectedIndex),
                    screenTipsBox.IsChecked == true,
                    moveAfterEnterBox.IsChecked == true,
                     OptionsDialogPlanner.IndexToAfterEnterDirection(afterEnterDirectionBox.SelectedIndex),
                     out var input,
                     out var inputError,
                     objectsDisplay: objectsDisplayBox.SelectedIndex switch
                     {
                         1 => AppOptionsObjectDisplay.Placeholders,
                         2 => AppOptionsObjectDisplay.Nothing,
                         _ => AppOptionsObjectDisplay.All,
                     },
                    collapseRibbonAutomatically: collapseRibbonBox.IsChecked == true,
                    appLanguage: languageOptions.Count > 0 && languageBox.SelectedIndex >= 0 && languageBox.SelectedIndex < languageOptions.Count
                        ? AppLanguageCatalog.NormalizeCultureName(languageOptions[languageBox.SelectedIndex].CultureName)
                        : current.AppLanguage,
                    crashAnalyticsEnabled: crashAnalyticsBox.IsChecked == true))
            {
                warningText.Text = inputError == OptionsDialogPlanner.OptionsInputError.InvalidFontSize
                    ? UiText.Get("Options_InvalidFontSizeMessage")
                    : UiText.Get("Options_InvalidSheetCountMessage");
                warningText.IsVisible = true;
                return false;
            }

            var iterativeEnabled = iterativeBox.IsChecked == true;
            if (!CalculationOptionsInputParser.TryParseBounds(
                    iterativeEnabled,
                    maxIterationsBox.Text,
                    maxChangeBox.Text,
                    workbook.MaxCalculationIterations ?? DefaultMaxCalculationIterations,
                    workbook.MaxCalculationChange ?? DefaultMaxCalculationChange,
                    out var maxIterations,
                    out var maxChange,
                    out var calculationInputError))
            {
                warningText.Text = UiText.Get(
                    calculationInputError == CalculationOptionsInputError.InvalidMaxIterations
                        ? "Options_InvalidMaxIterationsMessage"
                        : "Options_InvalidMaxChangeMessage");
                warningText.IsVisible = true;
                return false;
            }

            var projected = OptionsDialogPlanner.Project(current, input);
            projected.EnableFillHandleAndCellDragAndDrop = advancedFillHandleBox.IsChecked == true;
            projected.QuickAccessToolbarBelowRibbon = quickAccessBelowRibbonBox.IsChecked == true;
            projected.QuickAccessToolbarCommands = QuickAccessToolbarCatalog.NormalizeCommandIds(quickAccessCommandIds).ToList();
            projected.SpellCheckCustomDictionaryWords = AppOptions.NormalizeSpellCheckCustomDictionaryWords(customDictionaryWords);
            projected.NormalizePersistedCollections();
            if (!AppOptionsStore.Save(projected))
            {
                warningText.Text = projected.LastPersistenceError ?? UiText.Get("Options_SaveFailed");
                warningText.IsVisible = true;
                return false;
            }

            current = projected;
            _avaloniaQuickAccessOptions = AppOptionsStore.Load();
            RebuildAvaloniaQuickAccessToolbar();
            if (!ApplyFormulaErrorCheckingOptions())
                return false;

            ApplyLiveOptions(input);
            ApplyLiveIterativeCalculationOptions(iterativeEnabled, maxIterations, maxChange);
            return true;
        }

        okButton.Click += (_, _) =>
        {
            if (TryCommit())
                dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var footerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children = { warningText, okButton, cancelButton },
        };
        warningText.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        warningText.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
        var buttonRow = new Border
        {
            Background = Brush(245, 245, 245),
            BorderBrush = Brush(200, 200, 200),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(
                OptionsDialogPlanner.FooterPaddingHorizontal,
                OptionsDialogPlanner.FooterPaddingVertical),
            Height = OptionsDialogPlanner.FooterHeight,
            MinHeight = OptionsDialogPlanner.FooterHeight,
            MaxHeight = OptionsDialogPlanner.FooterHeight,
            Child = footerActions,
        };
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{OptionsDialogPlanner.CategoryColumnWidth},*"),
        };
        var categoryFrame = new Border
        {
            Background = Brush(245, 245, 245),
            BorderBrush = Brush(200, 200, 200),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = categoryList,
        };
        Grid.SetColumn(categoryFrame, 0);
        var scrollHost = new ScrollViewer
        {
            Content = contentHost,
            Padding = new Thickness(
                OptionsDialogPlanner.ContentPaddingHorizontal,
                OptionsDialogPlanner.ContentPaddingVertical,
                OptionsDialogPlanner.ContentPaddingHorizontal,
                OptionsDialogPlanner.ContentPaddingVertical),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Stretch,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Top,
        };
        Grid.SetColumn(scrollHost, 1);
        body.Children.Add(categoryFrame);
        body.Children.Add(scrollHost);

        dialog.Content = new Grid
        {
            Background = Brushes.White,
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { body, buttonRow },
        };
        Grid.SetRow(body, 0);
        Grid.SetRow(buttonRow, 1);

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Applies the cheap, immediately-visible options to the live session: gridlines, headings,
    /// formula-bar visibility and calculation mode. The persisted fields (default font/size/sheet-count,
    /// default format, proofing rules) take effect on the next workbook/launch.
    /// </summary>
    private void ApplyLiveOptions(OptionsDialogPlanner.OptionsDialogInput input)
    {
        if (_session.IsShowingGridlines != input.ShowGridlines)
            _session.SetShowGridlines(input.ShowGridlines);

        if (_session.IsShowingHeadings != input.ShowHeadings)
        {
            _session.SetShowHeadings(input.ShowHeadings);
            RefreshViewportSizeForZoom();
        }

        var formulaBarVisible = !_isFormulaBarHidden;
        if (formulaBarVisible != input.ShowFormulaBar)
        {
            _isFormulaBarHidden = !input.ShowFormulaBar;
            _formulaBox.IsVisible = input.ShowFormulaBar;
            _cellAddressText.IsVisible = input.ShowFormulaBar;
        }

        var wantManual = !input.AutoCalculate;
        if (CalculationModeIsManual != wantManual)
            SetCalculationMode(wantManual ? WorkbookCalculationMode.Manual : WorkbookCalculationMode.Automatic);

        RefreshShell(UiText.Get("Options_Saved"));
    }

    private const int DefaultMaxCalculationIterations = 100;
    private const double DefaultMaxCalculationChange = 0.001;

    /// <summary>
    /// Applies the dialog's iterative-calculation fields to the live workbook via the undoable
    /// <see cref="SetIterativeCalculationOptionsCommand"/>, but only when the values actually
    /// differ from the workbook's current settings — the fields were seeded from the live
    /// workbook, so this only fires on a genuine user edit.
    /// </summary>
    private void ApplyLiveIterativeCalculationOptions(bool enabled, int maxIterations, double maxChange)
    {
        var workbook = _session.Workbook;
        if (workbook.IterativeCalculation == enabled &&
            (workbook.MaxCalculationIterations ?? DefaultMaxCalculationIterations) == maxIterations &&
            (workbook.MaxCalculationChange ?? DefaultMaxCalculationChange) == maxChange)
        {
            return;
        }

        var result = _session.ExecuteReviewCommand(new SetIterativeCalculationOptionsCommand(enabled, maxIterations, maxChange));
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("ShellLoc_CouldNotChangeCalcMode"));
            return;
        }

        // Toggling iterative calculation changes whether circular-reference cells resolve at all
        // (Excel re-evaluates them the moment the setting changes), so any existing #CIRCULAR!
        // cells would otherwise stay stale until an unrelated edit forces a recalc.
        _session.RecalculateWorkbook();
    }

    private static void ApplyOptionsButtonChrome(Button button, double minWidth, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, OptionsDialogChromeStyle, minWidth, isDefault);

    private static void ApplyOptionsTextBoxChrome(TextBox textBox)
        => AvaloniaCompactDialogChrome.ApplyTextBox(textBox, OptionsDialogChromeStyle);

    private static void ApplyOptionsComboBoxChrome(ComboBox comboBox)
        => AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, OptionsDialogChromeStyle);

    private static void ApplyOptionsListBoxChrome(ListBox listBox)
    {
        AvaloniaCompactDialogChrome.ApplyListBox(listBox, OptionsDialogChromeStyle);
        // ApplyListBox supplies the shared item template; keep the list text on the same
        // Windows-style font as the surrounding Options labels as well.
        listBox.FontFamily = OptionsDialogChromeStyle.FontFamily;
    }

    private static void ApplyOptionsCheckBoxChrome(CheckBox checkBox)
    {
        StripContentMnemonic(checkBox);
        checkBox.MinHeight = 20;
        checkBox.MaxHeight = 20;
        AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, OptionsDialogChromeStyle);
    }

    private static void ApplyOptionsRadioButtonChrome(RadioButton radioButton)
    {
        StripContentMnemonic(radioButton);
        radioButton.MinHeight = 20;
        AvaloniaCompactDialogChrome.ApplyRadioButton(radioButton, OptionsDialogChromeStyle);
    }

    private static CheckBox OptionsCheckBox(
        string text,
        bool isChecked = false,
        bool isEnabled = true,
        double? height = null,
        bool preserveDisabledContrast = false)
    {
        var cb = new CheckBox { Content = text, IsChecked = isChecked, IsEnabled = isEnabled };
        ApplyOptionsCheckBoxChrome(cb);
        if (preserveDisabledContrast)
            cb.Classes.Add("free-options-ease-checkbox");
        if (height is { } fixedHeight)
        {
            cb.Height = fixedHeight;
            cb.MinHeight = fixedHeight;
            cb.MaxHeight = fixedHeight;
        }
        return cb;
    }

    private static ComboBox OptionsComboBox(string[] items, int selectedIndex = 0, bool isEnabled = true, double minWidth = 140)
    {
        var cb = new ComboBox { ItemsSource = items, SelectedIndex = selectedIndex, IsEnabled = isEnabled, MinWidth = minWidth };
        ApplyOptionsComboBoxChrome(cb);
        return cb;
    }

    private static Button OptionsButton(string text, double width, bool isEnabled = true)
    {
        var btn = new Button { Content = text, IsEnabled = isEnabled, HorizontalAlignment = AvaloniaHorizontalAlignment.Left };
        ApplyOptionsButtonChrome(btn, width);
        return btn;
    }

    private static TextBox OptionsReadOnlyTextBox(string text, double minWidth = 200)
    {
        var tb = new TextBox { Text = text, IsReadOnly = true, MinWidth = minWidth };
        ApplyOptionsTextBoxChrome(tb);
        return tb;
    }

    private static StackPanel OptionsCategoryPanel(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    private static string OptionsText(string resourceKey) =>
        NormalizeOptionAccessText(UiText.Get(resourceKey)) is { } localized && !LooksLikeMissingResource(localized)
            ? localized
            : resourceKey switch
            {
                "Options_CategoryLanguage" => "Language",
                "Options_CategoryEaseOfAccess" => "Ease of Access",
                "Options_CategoryAdvanced" => "Advanced",
                "Options_CategoryCustomizeRibbon" => "Customize Ribbon",
                "Options_CategoryQuickAccessToolbar" => "Quick Access Toolbar",
                "Options_CategoryAddIns" => "Add-ins",
                "Options_CategoryTrustCenter" => "Trust Center",
                "Options_GeneralOptionsForWorkingWithFreeX" => "General options for working with FreeX.",
                "Options_UserInterfaceOptions" => "User Interface options",
                "Options_CollapseTheRibbonAutomatically" => "Collapse the ribbon automatically",
                "Options_WhenCreatingNewWorkbooks" => "When creating new workbooks",
                "Options_DefaultFont" => "Default font:",
                "Options_FontSize" => "Font size:",
                "Options_IncludeThisManySheets" => "Include this many sheets:",
                "Options_PersonalizeYourCopyOfFreeX" => "Personalize your copy of FreeX",
                "Options_UserName" => "User name:",
                "Options_CalculationOptions" => "Calculation options",
                "Options_WorkbookCalculation" => "Workbook Calculation",
                "Options_InManualModePressF9ToRecalculateTheWorkbook" => "In Manual mode, press F9 to recalculate the workbook.",
                "Options_EnableIterativeCalculation" => "Enable iterative calculation",
                "Options_MaximumIterations" => "Maximum Iterations",
                "Options_MaximumChange" => "Maximum Change",
                "Options_WorkingWithFormulas" => "Working with formulas",
                "Options_EnableAutoCompleteForCellValues" => "Enable AutoComplete for cell values",
                "Options_EnableFillHandleAndCellDragAndDrop" => "Enable fill handle and cell drag-and-drop",
                "Options_ErrorCheckingRules" => "Error Checking Rules",
                "Options_EnableBackgroundErrorChecksFor" => "Enable background error checks for:",
                "Options_AutoCorrectOptions" => "AutoCorrect options",
                "Options_CheckSpellingAsYouType" => "Check spelling as you type",
                "Options_FlagRepeatedWords" => "Flag repeated words",
                "Options_CustomDictionary" => "Custom dictionary",
                "Options_CustomDictionaryDescription" => "FreeX spell check treats words in this list as correct.",
                "Options_WorkbookViewOptions" => "Workbook view options",
                "Options_ExpandFormulaBar" => "Expand formula bar",
                "Options_SaveWorkbooks" => "Save workbooks",
                "Options_SaveFilesInThisFormat" => "Save files in this format:",
                "Options_FileLocations" => "File locations",
                "Options_RecentFilesLocation" => "Recent files location:",
                "Options_ChooseDisplayLanguage" => "Choose display language",
                "Options_AppLanguage" => "App language:",
                "Options_AppLanguageSystemDefault" => "Use system default",
                "Options_AppLanguageEnglishUnitedStates" => "English (United States)",
                "Options_AppLanguageRestartNotice" => "Some open windows may keep their current language until you restart FreeX.",
                "Options_EaseOfAccessOptions" => "Ease of Access options",
                "Options_ProvideFeedbackWithSound" => "Provide feedback with sound",
                "Options_ShowQuickAnalysisOptionsOnSelection" => "Show Quick Analysis options on selection",
                "Options_OptimizeDisplayForAccessibility" => "Optimize display for accessibility",
                "Options_EditingOptions" => "Editing options",
                "Options_AfterPressingEnterMoveSelection" => "After pressing Enter, move selection",
                "Options_Direction" => "Direction:",
                "Options_AfterEnterDirectionDown" => "Down",
                "Options_AfterEnterDirectionRight" => "Right",
                "Options_AfterEnterDirectionUp" => "Up",
                "Options_AfterEnterDirectionLeft" => "Left",
                "Options_DisplayOptionsForThisWorkbook" => "Display options for this workbook",
                "Options_ForObjectsShow" => "For objects, show:",
                "Options_ObjectsDisplayAll" => "All",
                "Options_ObjectsDisplayPlaceholders" => "Placeholders",
                "Options_ObjectsDisplayNothing" => "Nothing",
                "Options_CustomizeTheRibbon" => "Customize the Ribbon",
                "Options_ChooseCommandsFromPopularCommands" => "Choose commands from: Popular Commands",
                "Options_ImportExport" => "Import/Export...",
                "Options_CustomizeTheQuickAccessToolbar" => "Customize the Quick Access Toolbar",
                "Options_ShowQuickAccessToolbarBelowTheRibbon" => "Show Quick Access Toolbar below the Ribbon",
                "Options_QuickAccessToolbarCommands" => "Quick Access Toolbar commands:",
                "Options_ViewAndManageAddIns" => "View and manage Add-ins",
                "Options_ActiveApplicationAddIns" => "Active Application Add-ins",
                "Options_Go" => "Go...",
                "Options_TrustCenter2" => "Trust Center",
                "Options_SecurityAndPrivacySettingsForFreeX" => "Security and privacy settings for FreeX.",
                "Options_SendOptInCrashReports" => "Send opt-in crash reports",
                "Options_CrashReportsAreSentOnlyWhenThisOptionIsEnabledAndTheTest" => "Crash reports are sent only when this option is enabled and the tester build is configured with a crash analytics endpoint.",
                "Options_LocalTesterDiagnostics" => "Local tester diagnostics",
                "Options_FreeXWritesLocalUsageEventsAndCrashFilesToLOCALAPPDATAFr" => "FreeX writes local usage events and crash files to %LOCALAPPDATA%\\FreeX\\Diagnostics. These files stay on this computer unless you attach them to an issue.",
                "Options_TrustCenterSettings" => "Trust Center Settings...",
                _ => resourceKey,
            };

    private static string NormalizeOptionAccessText(string text) =>
        text.Replace("_", string.Empty, StringComparison.Ordinal);

    private static bool LooksLikeMissingResource(string text) =>
        text.StartsWith("[[", StringComparison.Ordinal) && text.EndsWith("]]", StringComparison.Ordinal);

    private sealed record OptionsQuickAccessCommandChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private static Control OptionsSectionHeader(
        string text,
        double topMargin = 10,
        double bottomMargin = 4,
        double ruleTopMargin = 5,
        double ruleBottomMargin = 0) =>
        new StackPanel
        {
            Margin = new Thickness(0, topMargin, 0, bottomMargin),
            Children =
            {
                new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 13 },
                new Border { Height = 1, Background = Brush(212, 212, 212), Margin = new Thickness(0, ruleTopMargin, 0, ruleBottomMargin) },
            },
        };

    private static TextBlock OptionsDescription(string text, double leftMargin = 0, double topMargin = 0, double bottomMargin = 4) =>
        new()
        {
            Text = text,
            FontSize = 12,
            Foreground = Brush(85, 85, 85),
            Margin = new Thickness(leftMargin, topMargin, 0, bottomMargin),
            TextWrapping = TextWrapping.Wrap,
        };

    private static Control OptionsLabeled(
        string label,
        Control field,
        double labelWidth = 230,
        double? fieldWidth = null,
        bool stretchField = false,
        double spacing = 8,
        Thickness? margin = null)
    {
        if (fieldWidth.HasValue)
            field.Width = fieldWidth.Value;
        if (stretchField)
            field.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;

        if (stretchField)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*"),
                Margin = margin ?? new Thickness(0),
            };
            var labelControl = new TextBlock
            {
                Text = label,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            };
            Grid.SetColumn(labelControl, 0);
            Grid.SetColumn(field, 1);
            grid.Children.Add(labelControl);
            grid.Children.Add(field);
            return grid;
        }

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = spacing,
            Margin = margin ?? new Thickness(0),
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = AvaloniaVerticalAlignment.Center, Width = labelWidth, FontSize = 12, FontFamily = FormulaBarFontFamily },
                field,
            },
        };
    }

    private static T WithMargin<T>(T control, Thickness margin)
        where T : Control
    {
        control.Margin = margin;
        return control;
    }

}
