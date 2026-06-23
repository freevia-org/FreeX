using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Services;
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
    // ── File ▸ Options entry point ──────────────────────────────────────────────
    private void ShowOptions() => _ = ShowOptionsDialogAsync();

    private async Task ShowOptionsDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        // Edit a snapshot loaded from the shared store so unmanaged fields round-trip untouched.
        var current = AppOptionsStore.Load();

        var dialog = new Window
        {
            Title = UiText.Get("Options_Title"),
            Width = 760,
            Height = 560,
            MinWidth = 760,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "OptionsDialog");

        // ── General ─────────────────────────────────────────────────────────────
        var fontBox = new ComboBox { MinWidth = 200, ItemsSource = OptionsDialogPlanner.FontNames };
        fontBox.SelectedIndex = OptionsDialogPlanner.DefaultFontToIndex(current.DefaultFontName);
        ApplyOptionsComboBoxChrome(fontBox);
        AutomationProperties.SetAutomationId(fontBox, "OptionsDefaultFontComboBox");

        var fontSizeBox = new ComboBox { MinWidth = 100, ItemsSource = OptionsDialogPlanner.FontSizes };
        fontSizeBox.SelectedItem = current.DefaultFontSize.ToString();
        ApplyOptionsComboBoxChrome(fontSizeBox);
        AutomationProperties.SetAutomationId(fontSizeBox, "OptionsDefaultFontSizeComboBox");

        var sheetCountBox = new TextBox { MinWidth = 100, Text = current.DefaultSheetCount.ToString() };
        ApplyOptionsTextBoxChrome(sheetCountBox);
        AutomationProperties.SetAutomationId(sheetCountBox, "OptionsDefaultSheetCountBox");

        var userNameBox = new TextBox { MinWidth = 200, Text = current.UserName };
        ApplyOptionsTextBoxChrome(userNameBox);
        AutomationProperties.SetAutomationId(userNameBox, "OptionsUserNameBox");

        var screenTipsBox = new CheckBox { Content = UiText.Get("Options_ShowScreenTips"), IsChecked = current.ShowScreenTips };
        ApplyOptionsCheckBoxChrome(screenTipsBox);
        AutomationProperties.SetAutomationId(screenTipsBox, "OptionsShowScreenTipsCheckBox");
        var collapseRibbonBox = new CheckBox
        {
            Content = OptionsText("Options_CollapseTheRibbonAutomatically"),
            IsEnabled = false,
        };
        ApplyOptionsCheckBoxChrome(collapseRibbonBox);
        AutomationProperties.SetAutomationId(collapseRibbonBox, "OptionsCollapseRibbonAutomaticallyCheckBox");

        var generalPanel = OptionsCategoryPanel(
            OptionsDescription(OptionsText("Options_GeneralOptionsForWorkingWithFreeX")),
            OptionsSectionHeader(OptionsText("Options_UserInterfaceOptions")),
            collapseRibbonBox,
            screenTipsBox,
            OptionsSectionHeader(OptionsText("Options_WhenCreatingNewWorkbooks")),
            OptionsLabeled(OptionsText("Options_DefaultFont"), fontBox),
            OptionsLabeled(OptionsText("Options_FontSize"), fontSizeBox, fieldWidth: 80),
            OptionsLabeled(OptionsText("Options_IncludeThisManySheets"), sheetCountBox, fieldWidth: 80),
            OptionsSectionHeader(OptionsText("Options_PersonalizeYourCopyOfFreeX")),
            OptionsLabeled(OptionsText("Options_UserName"), userNameBox, stretchField: true));

        // ── Formulas ────────────────────────────────────────────────────────────
        var calcAutoButton = new RadioButton { Content = UiText.Get("Options_CalcAutomatic"), GroupName = "OptionsCalcMode", IsChecked = current.AutoCalculate };
        ApplyOptionsRadioButtonChrome(calcAutoButton);
        AutomationProperties.SetAutomationId(calcAutoButton, "OptionsCalcAutomaticButton");
        var calcManualButton = new RadioButton { Content = UiText.Get("Options_CalcManual"), GroupName = "OptionsCalcMode", IsChecked = !current.AutoCalculate };
        ApplyOptionsRadioButtonChrome(calcManualButton);
        AutomationProperties.SetAutomationId(calcManualButton, "OptionsCalcManualButton");

        var r1c1Box = new CheckBox { Content = UiText.Get("Options_R1C1ReferenceStyle"), IsChecked = current.UseR1C1ReferenceStyle };
        ApplyOptionsCheckBoxChrome(r1c1Box);
        AutomationProperties.SetAutomationId(r1c1Box, "OptionsR1C1ReferenceStyleCheckBox");

        var errorCheckingBox = new CheckBox { Content = UiText.Get("Options_EnableErrorChecking"), IsChecked = current.ErrorCheckingEnabled };
        ApplyOptionsCheckBoxChrome(errorCheckingBox);
        AutomationProperties.SetAutomationId(errorCheckingBox, "OptionsEnableErrorCheckingCheckBox");

        var formulasPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_CalculationOptions")),
            new TextBlock { Text = OptionsText("Options_WorkbookCalculation"), FontWeight = FontWeight.SemiBold, FontSize = 12 },
            calcAutoButton,
            calcManualButton,
            OptionsDescription(OptionsText("Options_InManualModePressF9ToRecalculateTheWorkbook"), leftMargin: 18),
            OptionsSectionHeader(OptionsText("Options_WorkingWithFormulas")),
            r1c1Box,
            OptionsCheckBox(OptionsText("Options_EnableAutoCompleteForCellValues"), isChecked: true, isEnabled: false),
            OptionsSectionHeader(OptionsText("Options_ErrorCheckingRules")),
            OptionsDescription(OptionsText("Options_EnableBackgroundErrorChecksFor")),
            errorCheckingBox);

        // ── Proofing ────────────────────────────────────────────────────────────
        var ignoreUppercaseBox = new CheckBox { Content = UiText.Get("Options_IgnoreUppercase"), IsChecked = current.ProofingIgnoreUppercase };
        ApplyOptionsCheckBoxChrome(ignoreUppercaseBox);
        AutomationProperties.SetAutomationId(ignoreUppercaseBox, "OptionsIgnoreUppercaseCheckBox");
        var ignoreNumbersBox = new CheckBox { Content = UiText.Get("Options_IgnoreNumbers"), IsChecked = current.ProofingIgnoreNumbers };
        ApplyOptionsCheckBoxChrome(ignoreNumbersBox);
        AutomationProperties.SetAutomationId(ignoreNumbersBox, "OptionsIgnoreNumbersCheckBox");

        var proofingPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_AutoCorrectOptions")),
            OptionsCheckBox(OptionsText("Options_CheckSpellingAsYouType"), isChecked: true, isEnabled: false),
            ignoreUppercaseBox,
            ignoreNumbersBox,
            OptionsCheckBox(OptionsText("Options_FlagRepeatedWords"), isEnabled: false),
            OptionsSectionHeader(OptionsText("Options_CustomDictionary")),
            OptionsDescription(OptionsText("Options_CustomDictionaryDescription")));

        // ── View ────────────────────────────────────────────────────────────────
        var showFormulaBarBox = new CheckBox { Content = UiText.Get("Options_ShowFormulaBar"), IsChecked = current.ShowFormulaBar };
        ApplyOptionsCheckBoxChrome(showFormulaBarBox);
        AutomationProperties.SetAutomationId(showFormulaBarBox, "OptionsShowFormulaBarCheckBox");
        var showGridlinesBox = new CheckBox { Content = UiText.Get("Options_ShowGridlines"), IsChecked = current.ShowGridlines };
        ApplyOptionsCheckBoxChrome(showGridlinesBox);
        AutomationProperties.SetAutomationId(showGridlinesBox, "OptionsShowGridlinesCheckBox");
        var showHeadingsBox = new CheckBox { Content = UiText.Get("Options_ShowHeadings"), IsChecked = current.ShowHeadings };
        ApplyOptionsCheckBoxChrome(showHeadingsBox);
        AutomationProperties.SetAutomationId(showHeadingsBox, "OptionsShowHeadingsCheckBox");

        var viewPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_WorkbookViewOptions")),
            showFormulaBarBox,
            OptionsCheckBox(OptionsText("Options_ExpandFormulaBar"), isEnabled: showFormulaBarBox.IsChecked == true));

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
            OptionsSectionHeader(OptionsText("Options_SaveWorkbooks")),
            OptionsLabeled(OptionsText("Options_SaveFilesInThisFormat"), defaultFormatBox),
            OptionsSectionHeader(OptionsText("Options_FileLocations")),
            OptionsLabeled(OptionsText("Options_RecentFilesLocation"), OptionsReadOnlyTextBox(
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FreeX",
                    "recent.json"),
                minWidth: 280), stretchField: true));

        var languagePanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_ChooseDisplayLanguage")),
            OptionsLabeled(OptionsText("Options_AppLanguage"), OptionsComboBox(
                new[] { UiText.Get("Options_AppLanguageSystemDefault"), UiText.Get("Options_AppLanguageEnglishUnitedStates") },
                selectedIndex: 0,
                isEnabled: false,
                minWidth: 240)),
            OptionsDescription(OptionsText("Options_AppLanguageRestartNotice")));

        var easePanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_EaseOfAccessOptions")),
            OptionsCheckBox(OptionsText("Options_ProvideFeedbackWithSound"), isEnabled: false),
            OptionsCheckBox(OptionsText("Options_ShowQuickAnalysisOptionsOnSelection"), isChecked: true, isEnabled: false),
            OptionsCheckBox(OptionsText("Options_OptimizeDisplayForAccessibility"), isEnabled: false));

        var advancedPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_EditingOptions")),
            OptionsCheckBox(OptionsText("Options_AfterPressingEnterMoveSelection"), isChecked: true, isEnabled: false),
            OptionsLabeled(OptionsText("Options_Direction"), OptionsComboBox(
                new[]
                {
                    OptionsText("Options_AfterEnterDirectionDown"),
                    OptionsText("Options_AfterEnterDirectionRight"),
                    OptionsText("Options_AfterEnterDirectionUp"),
                    OptionsText("Options_AfterEnterDirectionLeft"),
                },
                selectedIndex: 0,
                isEnabled: false,
                minWidth: 140), labelWidth: 160, fieldWidth: 140),
            OptionsCheckBox(OptionsText("Options_EnableFillHandleAndCellDragAndDrop"), isChecked: true, isEnabled: false),
            OptionsCheckBox(OptionsText("Options_EnableAutoCompleteForCellValues"), isChecked: true, isEnabled: false),
            OptionsSectionHeader(OptionsText("Options_DisplayOptionsForThisWorkbook")),
            showGridlinesBox,
            showHeadingsBox,
            OptionsLabeled(OptionsText("Options_ForObjectsShow"), OptionsComboBox(
                new[]
                {
                    OptionsText("Options_ObjectsDisplayAll"),
                    OptionsText("Options_ObjectsDisplayPlaceholders"),
                    OptionsText("Options_ObjectsDisplayNothing"),
                },
                selectedIndex: 0,
                isEnabled: false)));

        var customizeRibbonPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_CustomizeTheRibbon")),
            OptionsDescription(OptionsText("Options_ChooseCommandsFromPopularCommands")),
            OptionsButton(OptionsText("Options_ImportExport"), width: 130, isEnabled: false));

        var quickAccessPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_CustomizeTheQuickAccessToolbar")),
            OptionsCheckBox(OptionsText("Options_ShowQuickAccessToolbarBelowTheRibbon"), isEnabled: false),
            OptionsDescription(OptionsText("Options_QuickAccessToolbarCommands")));

        var addInsPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_ViewAndManageAddIns")),
            OptionsDescription(OptionsText("Options_ActiveApplicationAddIns")),
            OptionsButton(OptionsText("Options_Go"), width: 72, isEnabled: false));

        var trustCenterPanel = OptionsCategoryPanel(
            OptionsSectionHeader(OptionsText("Options_TrustCenter2")),
            OptionsDescription(OptionsText("Options_SecurityAndPrivacySettingsForFreeX")),
            OptionsCheckBox(OptionsText("Options_SendOptInCrashReports"), isEnabled: false),
            OptionsDescription(OptionsText("Options_CrashReportsAreSentOnlyWhenThisOptionIsEnabledAndTheTest")),
            OptionsSectionHeader(OptionsText("Options_LocalTesterDiagnostics")),
            OptionsDescription(OptionsText("Options_FreeXWritesLocalUsageEventsAndCrashFilesToLOCALAPPDATAFr")),
            OptionsButton(OptionsText("Options_TrustCenterSettings"), width: 150, isEnabled: false));

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
            Width = 220,
            Background = Brush(245, 245, 245),
            Margin = new Thickness(0, 8, 0, 0),
        };
        AutomationProperties.SetAutomationId(categoryList, "OptionsCategoryList");
        AutomationProperties.SetName(categoryList, UiText.Get("Options_OptionsCategories"));
        AutomationProperties.SetHelpText(categoryList, UiText.Get("Options_SelectAFreeXOptionsCategory"));

        for (var i = 0; i < categoryNames.Length; i++)
        {
            var index = i;
            var row = new Border
            {
                Height = 36,
                Padding = new Thickness(16, 0, 12, 0),
                BorderThickness = new Thickness(1, 0, 0, 1),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = categoryNames[i],
                    FontSize = 13,
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                },
            };
            row.PointerPressed += (_, _) => SelectCategory(index);
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

        void SelectCategory(int index)
        {
            if (index < 0 || index >= panels.Length)
                return;

            selectedCategoryIndex = index;
            contentHost.Content = panels[index];
            for (var i = 0; i < categoryRows.Length; i++)
            {
                var selected = i == selectedCategoryIndex;
                categoryRows[i].Background = selected ? Brushes.White : Brushes.Transparent;
                categoryRows[i].BorderBrush = selected ? Brush(205, 205, 205) : Brushes.Transparent;
            }
        }

        // ── Warning + buttons ─────────────────────────────────────────────────────
        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "OptionsWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        ApplyOptionsButtonChrome(okButton, 84, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "OptionsOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
        ApplyOptionsButtonChrome(cancelButton, 84);
        AutomationProperties.SetAutomationId(cancelButton, "OptionsCancelButton");

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
                    ignoreUppercaseBox.IsChecked == true,
                    ignoreNumbersBox.IsChecked == true,
                    showFormulaBarBox.IsChecked == true,
                    showGridlinesBox.IsChecked == true,
                    showHeadingsBox.IsChecked == true,
                    OptionsDialogPlanner.IndexToDefaultFormat(defaultFormatBox.SelectedIndex),
                    screenTipsBox.IsChecked == true,
                    out var input,
                    out var inputError))
            {
                warningText.Text = inputError == OptionsDialogPlanner.OptionsInputError.InvalidFontSize
                    ? UiText.Get("Options_InvalidFontSizeMessage")
                    : UiText.Get("Options_InvalidSheetCountMessage");
                warningText.IsVisible = true;
                return false;
            }

            var projected = OptionsDialogPlanner.Project(current, input);
            if (!AppOptionsStore.Save(projected))
            {
                warningText.Text = projected.LastPersistenceError ?? UiText.Get("Options_SaveFailed");
                warningText.IsVisible = true;
                return false;
            }

            current = projected;
            ApplyLiveOptions(input);
            return true;
        }

        okButton.Click += (_, _) =>
        {
            if (TryCommit())
                dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 16, 8),
            Children = { warningText, okButton, cancelButton },
        };
        warningText.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        warningText.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
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
            Padding = new Thickness(28, 20, 28, 20),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetColumn(scrollHost, 1);
        body.Children.Add(categoryFrame);
        body.Children.Add(scrollHost);

        dialog.Content = new DockPanel
        {
            Background = Brushes.White,
            Children = { buttonRow, body },
        };

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

    private static void ApplyOptionsButtonChrome(Button button, double minWidth, bool isDefault = false)
    {
        button.Height = 24;
        button.MinHeight = 24;
        button.MaxHeight = 24;
        button.MinWidth = minWidth;
        button.Padding = new Thickness(4, 1);
        button.Background = Brushes.White;
        button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);
        button.BorderThickness = new Thickness(1);
        button.FontSize = 12;
        button.FontFamily = FormulaBarFontFamily;
        button.HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center;
        button.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }

    private static void ApplyOptionsTextBoxChrome(TextBox textBox)
    {
        textBox.Height = 24;
        textBox.MinHeight = 24;
        textBox.MaxHeight = 24;
        textBox.Padding = new Thickness(4, 1);
        textBox.FontSize = 12;
        textBox.FontFamily = FormulaBarFontFamily;
        textBox.BorderBrush = Brush(130, 130, 130);
        textBox.BorderThickness = new Thickness(1);
        textBox.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }

    private static void ApplyOptionsComboBoxChrome(ComboBox comboBox)
    {
        comboBox.Height = 24;
        comboBox.MinHeight = 24;
        comboBox.MaxHeight = 24;
        comboBox.Padding = new Thickness(5, 0, 4, 0);
        comboBox.FontSize = 12;
        comboBox.FontFamily = FormulaBarFontFamily;
        comboBox.BorderBrush = Brush(130, 130, 130);
        comboBox.BorderThickness = new Thickness(1);
        comboBox.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }

    private static void ApplyOptionsCheckBoxChrome(CheckBox checkBox)
    {
        checkBox.MinHeight = 20;
        checkBox.MaxHeight = 20;
        checkBox.FontSize = 12;
        checkBox.FontFamily = FormulaBarFontFamily;
    }

    private static void ApplyOptionsRadioButtonChrome(RadioButton radioButton)
    {
        radioButton.MinHeight = 20;
        radioButton.FontSize = 12;
        radioButton.FontFamily = FormulaBarFontFamily;
    }

    private static CheckBox OptionsCheckBox(string text, bool isChecked = false, bool isEnabled = true)
    {
        var cb = new CheckBox { Content = text, IsChecked = isChecked, IsEnabled = isEnabled };
        ApplyOptionsCheckBoxChrome(cb);
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
                "Options_WorkingWithFormulas" => "Working with formulas",
                "Options_EnableAutoCompleteForCellValues" => "Enable AutoComplete for cell values",
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
                "Options_EnableFillHandleAndCellDragAndDrop" => "Enable fill handle and cell drag-and-drop",
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

    private static Control OptionsSectionHeader(string text) =>
        new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 4),
            Children =
            {
                new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 13 },
                new Border { Height = 1, Background = Brush(212, 212, 212), Margin = new Thickness(0, 5, 0, 0) },
            },
        };

    private static TextBlock OptionsDescription(string text, double leftMargin = 0) =>
        new()
        {
            Text = text,
            FontSize = 12,
            Foreground = Brush(85, 85, 85),
            Margin = new Thickness(leftMargin, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap,
        };

    private static StackPanel OptionsLabeled(
        string label,
        Control field,
        double labelWidth = 230,
        double? fieldWidth = null,
        bool stretchField = false)
    {
        if (fieldWidth.HasValue)
            field.Width = fieldWidth.Value;
        if (stretchField)
            field.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = AvaloniaVerticalAlignment.Center, Width = labelWidth, FontSize = 12, FontFamily = FormulaBarFontFamily },
                field,
            },
        };
    }

}
