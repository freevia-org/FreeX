using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
            Width = 620,
            Height = 460,
            MinWidth = 540,
            MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "OptionsDialog");

        // ── General ─────────────────────────────────────────────────────────────
        var fontBox = new ComboBox { MinWidth = 200, ItemsSource = OptionsDialogPlanner.FontNames };
        fontBox.SelectedIndex = OptionsDialogPlanner.DefaultFontToIndex(current.DefaultFontName);
        AutomationProperties.SetAutomationId(fontBox, "OptionsDefaultFontComboBox");

        var fontSizeBox = new ComboBox { MinWidth = 100, ItemsSource = OptionsDialogPlanner.FontSizes };
        fontSizeBox.SelectedItem = current.DefaultFontSize.ToString();
        AutomationProperties.SetAutomationId(fontSizeBox, "OptionsDefaultFontSizeComboBox");

        var sheetCountBox = new TextBox { MinWidth = 100, Text = current.DefaultSheetCount.ToString() };
        AutomationProperties.SetAutomationId(sheetCountBox, "OptionsDefaultSheetCountBox");

        var userNameBox = new TextBox { MinWidth = 200, Text = current.UserName };
        AutomationProperties.SetAutomationId(userNameBox, "OptionsUserNameBox");

        var screenTipsBox = new CheckBox { Content = UiText.Get("Options_ShowScreenTips"), IsChecked = current.ShowScreenTips };
        AutomationProperties.SetAutomationId(screenTipsBox, "OptionsShowScreenTipsCheckBox");

        var generalPanel = OptionsCategoryPanel(
            new TextBlock { Text = UiText.Get("Options_NewWorkbooksHeader"), FontWeight = FontWeight.SemiBold },
            OptionsLabeled(UiText.Get("Options_DefaultFontLabel"), fontBox),
            OptionsLabeled(UiText.Get("Options_DefaultFontSizeLabel"), fontSizeBox),
            OptionsLabeled(UiText.Get("Options_DefaultSheetCountLabel"), sheetCountBox),
            new TextBlock { Text = UiText.Get("Options_PersonalizeHeader"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) },
            OptionsLabeled(UiText.Get("Options_UserNameLabel"), userNameBox),
            screenTipsBox);

        // ── Formulas ────────────────────────────────────────────────────────────
        var calcAutoButton = new RadioButton { Content = UiText.Get("Options_CalcAutomatic"), GroupName = "OptionsCalcMode", IsChecked = current.AutoCalculate };
        AutomationProperties.SetAutomationId(calcAutoButton, "OptionsCalcAutomaticButton");
        var calcManualButton = new RadioButton { Content = UiText.Get("Options_CalcManual"), GroupName = "OptionsCalcMode", IsChecked = !current.AutoCalculate };
        AutomationProperties.SetAutomationId(calcManualButton, "OptionsCalcManualButton");

        var r1c1Box = new CheckBox { Content = UiText.Get("Options_R1C1ReferenceStyle"), IsChecked = current.UseR1C1ReferenceStyle };
        AutomationProperties.SetAutomationId(r1c1Box, "OptionsR1C1ReferenceStyleCheckBox");

        var errorCheckingBox = new CheckBox { Content = UiText.Get("Options_EnableErrorChecking"), IsChecked = current.ErrorCheckingEnabled };
        AutomationProperties.SetAutomationId(errorCheckingBox, "OptionsEnableErrorCheckingCheckBox");

        var formulasPanel = OptionsCategoryPanel(
            new TextBlock { Text = UiText.Get("Options_CalculationHeader"), FontWeight = FontWeight.SemiBold },
            calcAutoButton,
            calcManualButton,
            new TextBlock { Text = UiText.Get("Options_FormulasWorkingHeader"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) },
            r1c1Box,
            new TextBlock { Text = UiText.Get("Options_ErrorCheckingHeader"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) },
            errorCheckingBox);

        // ── Proofing ────────────────────────────────────────────────────────────
        var ignoreUppercaseBox = new CheckBox { Content = UiText.Get("Options_IgnoreUppercase"), IsChecked = current.ProofingIgnoreUppercase };
        AutomationProperties.SetAutomationId(ignoreUppercaseBox, "OptionsIgnoreUppercaseCheckBox");
        var ignoreNumbersBox = new CheckBox { Content = UiText.Get("Options_IgnoreNumbers"), IsChecked = current.ProofingIgnoreNumbers };
        AutomationProperties.SetAutomationId(ignoreNumbersBox, "OptionsIgnoreNumbersCheckBox");

        var proofingPanel = OptionsCategoryPanel(
            new TextBlock { Text = UiText.Get("Options_ProofingHeader"), FontWeight = FontWeight.SemiBold },
            ignoreUppercaseBox,
            ignoreNumbersBox);

        // ── View ────────────────────────────────────────────────────────────────
        var showFormulaBarBox = new CheckBox { Content = UiText.Get("Options_ShowFormulaBar"), IsChecked = current.ShowFormulaBar };
        AutomationProperties.SetAutomationId(showFormulaBarBox, "OptionsShowFormulaBarCheckBox");
        var showGridlinesBox = new CheckBox { Content = UiText.Get("Options_ShowGridlines"), IsChecked = current.ShowGridlines };
        AutomationProperties.SetAutomationId(showGridlinesBox, "OptionsShowGridlinesCheckBox");
        var showHeadingsBox = new CheckBox { Content = UiText.Get("Options_ShowHeadings"), IsChecked = current.ShowHeadings };
        AutomationProperties.SetAutomationId(showHeadingsBox, "OptionsShowHeadingsCheckBox");

        var viewPanel = OptionsCategoryPanel(
            new TextBlock { Text = UiText.Get("Options_ViewDisplayHeader"), FontWeight = FontWeight.SemiBold },
            showFormulaBarBox,
            showGridlinesBox,
            showHeadingsBox);

        // ── Save ────────────────────────────────────────────────────────────────
        var defaultFormatBox = new ComboBox
        {
            MinWidth = 220,
            ItemsSource = new[] { UiText.Get("Options_DefaultFormatXlsx"), UiText.Get("Options_DefaultFormatNative") },
            SelectedIndex = OptionsDialogPlanner.DefaultFormatToIndex(current.DefaultFormat),
        };
        AutomationProperties.SetAutomationId(defaultFormatBox, "OptionsDefaultFormatComboBox");

        var savePanel = OptionsCategoryPanel(
            new TextBlock { Text = UiText.Get("Options_SaveHeader"), FontWeight = FontWeight.SemiBold },
            OptionsLabeled(UiText.Get("Options_DefaultFormatLabel"), defaultFormatBox));

        // ── Category list + content host ──────────────────────────────────────────
        var panels = new[] { generalPanel, formulasPanel, proofingPanel, viewPanel, savePanel };
        var contentHost = new ContentControl { Content = generalPanel };
        AutomationProperties.SetAutomationId(contentHost, "OptionsContentHost");

        var categoryList = new ListBox
        {
            Width = 160,
            ItemsSource = new[]
            {
                UiText.Get("Options_CategoryGeneral"),
                UiText.Get("Options_CategoryFormulas"),
                UiText.Get("Options_CategoryProofing"),
                UiText.Get("Options_CategoryView"),
                UiText.Get("Options_CategorySave"),
            },
            SelectedIndex = 0,
        };
        AutomationProperties.SetAutomationId(categoryList, "OptionsCategoryList");
        categoryList.SelectionChanged += (_, _) =>
        {
            var index = categoryList.SelectedIndex;
            if (index >= 0 && index < panels.Length)
                contentHost.Content = panels[index];
        };

        // ── Warning + buttons ─────────────────────────────────────────────────────
        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "OptionsWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(okButton, "OptionsOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(cancelButton, "OptionsCancelButton");
        var applyButton = new Button { Content = UiText.Get("Common_Apply"), MinWidth = 84 };
        AutomationProperties.SetAutomationId(applyButton, "OptionsApplyButton");

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

        applyButton.Click += (_, _) => TryCommit();
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
            Margin = new Thickness(0, 10, 0, 0),
            Children = { warningText, cancelButton, applyButton, okButton },
        };
        warningText.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        warningText.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        Grid.SetColumn(categoryList, 0);
        var scrollHost = new ScrollViewer { Content = contentHost, Margin = new Thickness(16, 0, 0, 0) };
        Grid.SetColumn(scrollHost, 1);
        body.Children.Add(categoryList);
        body.Children.Add(scrollHost);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
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

    private static StackPanel OptionsCategoryPanel(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    private static StackPanel OptionsLabeled(string label, Control field) =>
        new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = AvaloniaVerticalAlignment.Center, MinWidth = 180 },
                field,
            },
        };
}
