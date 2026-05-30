using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Xml.Linq;
using FreeX.App.Host;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class OptionsDialogSourceTests
{
    [Fact]
    public void OptionsDialog_ExposesPersistedViewOptions()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

        xaml.Should().Contain("<ListBoxItem Content=\"_View\"/>");
        xaml.Should().Contain("x:Name=\"PanelView\"");
        xaml.Should().Contain("x:Name=\"OptShowFormulaBar\"");
        xaml.Should().Contain("x:Name=\"OptFormulaBarExpanded\"");
        source.Should().Contain("OptShowFormulaBar.IsChecked = _opts.ShowFormulaBar");
        source.Should().Contain("OptFormulaBarExpanded.IsChecked = _opts.FormulaBarExpanded");
        source.Should().Contain("ShowFormulaBar     = OptShowFormulaBar.IsChecked == true");
        source.Should().Contain("FormulaBarExpanded = OptShowFormulaBar.IsChecked == true && OptFormulaBarExpanded.IsChecked == true");
    }

    [Fact]
    public void OptionsDialog_PreservesPersistedExportOptionsWhenSavingGeneralOptions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

        source.Should().Contain("PdfExportLanguage = ExportPlanner.NormalizePdfLanguage(_opts.PdfExportLanguage)");
    }

    [Fact]
    public void OptionsDialog_ExposesKeyboardAccessKeysForTabsFieldsAndButtons()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("OptionsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "ListBoxItem")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain([
                "_General",
                "_Formulas",
                "_Proofing",
                "_Save",
                "_Language",
                "_Ease of Access",
                "_Advanced",
                "_Customize Ribbon",
                "_Quick Access Toolbar",
                "_Add-ins",
                "_Trust Center"
            ]);

        AssertLabelTargets(document, presentation, "Default _font:", "OptDefaultFont");
        AssertLabelTargets(document, presentation, "Font _size:", "OptDefaultFontSize");
        AssertLabelTargets(document, presentation, "Include this many _sheets:", "OptSheetCount");
        AssertLabelTargets(document, presentation, "User _name:", "OptUserName");
        AssertLabelTargets(document, presentation, "Save files in this _format:", "OptDefaultFormat");
        AssertLabelTargets(document, presentation, "Recent files _location:", "OptRecentFilesPath");
        AssertLabelTargets(document, presentation, "App _language:", "OptAppLanguage");

        document.Descendants(presentation + "CheckBox")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain([
                "_Collapse the ribbon automatically",
                "Show feature descriptions in _ScreenTips",
                "Use _R1C1 reference style",
                "Enable _AutoComplete for cell values",
                "Show formula _bar",
                "Expand formula ba_r"
            ]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_OK", "_Cancel"]);

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element => element.Attribute("Content")?.Value == content);

            label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
        }
    }

    [Fact]
    public void OptionsDialogOpenedFromKeyboard_FocusesCategoryList()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

        source.Should().Contain("Loaded += (_, _) =>");
        source.Should().Contain("FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("TabList.Focus();");
        source.Should().Contain("Keyboard.Focus(TabList);");
    }

    [Fact]
    public void OptionsDialog_ExposesStableAutomationMetadataForCategoriesAndActions()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");

        xaml.Should().Contain("AutomationProperties.Name=\"Options categories\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"OptionsCategoryList\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Select a FreeX Options category.\"");
        xaml.Should().Contain("x:Name=\"OkBtn\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"OptionsOkButton\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Apply FreeX Options changes.\"");
        xaml.Should().Contain("x:Name=\"CancelBtn\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"OptionsCancelButton\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Close FreeX Options without applying changes.\"");
    }

    [Fact]
    public void OptionsDialog_ExposesPersistedAppLanguageSwitcher()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var appSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "App.xaml.cs"));

        xaml.Should().Contain("x:Name=\"PanelLanguage\"");
        xaml.Should().Contain("Choose display language");
        xaml.Should().Contain("x:Name=\"OptAppLanguage\"");
        xaml.Should().Contain("DisplayMemberPath=\"DisplayName\"");
        xaml.Should().Contain("SelectedValuePath=\"CultureName\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Select the display language FreeX uses for menus, dialogs, and messages.\"");
        xaml.Should().Contain("Some open windows may keep their current language until you restart FreeX.");

        source.Should().Contain("OptAppLanguage.ItemsSource = AppLanguageCatalog.GetAvailableLanguages()");
        source.Should().Contain("OptAppLanguage.SelectedValue = AppLanguageCatalog.NormalizeCultureName(_opts.AppLanguage)");
        source.Should().Contain("AppLanguage       = AppLanguageCatalog.NormalizeCultureName(OptAppLanguage.SelectedValue as string)");

        backstageSource.Should().Contain("AppLocalization.ApplyAppLanguage(_options.AppLanguage)");
        backstageSource.Should().Contain("UiText.Get(\"Options_AppLanguageRestartMessage\")");
        appSource.Should().Contain("AppLocalization.ApplyAppLanguage(options.AppLanguage);");
        appSource.Should().Contain("_startupOptions = options;");
        appSource.Should().Contain("ConfigureServices(serviceCollection);");
        appSource.Should().Contain("var options = _startupOptions ?? FreeXOptions.Load();");
        appSource.Should().NotContain("var options = Services.GetRequiredService<FreeXOptions>();");
    }

    [Fact]
    public void OptionsDialogInvalidGeneralInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

        source.Should().Contain("OptionsInputParser.TryParseDefaultFontSize(OptDefaultFontSize.Text, out var defaultFontSize)");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"Options_InvalidDefaultFontSizeMessage\"), OptDefaultFontSize);");
        source.Should().Contain("OptionsInputParser.TryParseDefaultSheetCount(OptSheetCount.Text, out var defaultSheetCount)");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"Options_InvalidSheetCountMessage\"), OptSheetCount);");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, Control target)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title);");
        source.Should().Contain("if (target is TextBox textBox)");
        source.Should().Contain("textBox.SelectAll();");
        source.Should().Contain("else if (target is ComboBox comboBox)");
        source.Should().Contain("comboBox.Focus();");
        source.Should().Contain("Keyboard.Focus(target);");
        source.Should().NotContain("ParseDefaultFontSizeOrFallback");
        source.Should().NotContain("ParseDefaultSheetCountOrFallback");
    }

    [Fact]
    public void OptionsDialog_ExposesExcelLikeAdvancedAndDisplayAffordances()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

        xaml.Should().Contain("x:Name=\"PanelAdvanced\"");
        xaml.Should().Contain("Editing options");
        xaml.Should().Contain("Display options for this workbook");
        xaml.Should().Contain("x:Name=\"OptAfterEnterDirection\"");
        xaml.Should().Contain("x:Name=\"OptMoveAfterEnter\"");
        xaml.Should().Contain("x:Name=\"OptShowGridlines\"");
        xaml.Should().Contain("x:Name=\"OptShowHeadings\"");
        xaml.Should().Contain("x:Name=\"OptObjectsDisplay\"");
        xaml.Should().Contain("x:Name=\"PanelTrustCenter\"");
        xaml.Should().Contain("Trust Center _Settings...");
        foreach (var clickHandler in new[]
        {
            "AutoCorrectOptionsButton_Click",
            "RibbonImportExportButton_Click",
            "QuickAccessResetButton_Click",
            "AddInsGoButton_Click",
            "TrustCenterSettingsButton_Click"
        })
            xaml.Should().Contain($"Click=\"{clickHandler}\"");

        source.Should().Contain("PanelAdvanced.Visibility");
        source.Should().Contain("OptAfterEnterDirection.ItemsSource");
        source.Should().Contain("OptMoveAfterEnter.IsChecked = _opts.MoveSelectionAfterEnter");
        source.Should().Contain("ShowGridlines = OptShowGridlines.IsChecked == true");
        source.Should().Contain("ShowHeadings = OptShowHeadings.IsChecked == true");
        source.Should().Contain("ObjectsDisplay = OptObjectsDisplay.SelectedIndex switch");
        source.Should().Contain("OptObjectsDisplay.ItemsSource");
        source.Should().Contain("ShowDeferredOptionsMessage");
        source.Should().Contain("DeferredCommandMessages.AutoCorrectOptions()");
        source.Should().Contain("DeferredCommandMessages.RibbonCustomizationImportExport()");
        source.Should().Contain("DeferredCommandMessages.QuickAccessToolbarReset()");
        source.Should().Contain("DeferredCommandMessages.OfficeAddIns()");
        source.Should().Contain("DeferredCommandMessages.TrustCenterSettings()");
    }

    [Fact]
    public void OptionsDialog_ExposesQuickAccessToolbarCustomizationAsDeferredAffordance()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));
        var deferredMessages = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DeferredCommandMessages.cs"));

        xaml.Should().Contain("<ListBoxItem Content=\"_Quick Access Toolbar\"/>");
        xaml.Should().Contain("x:Name=\"PanelQuickAccessToolbar\"");
        xaml.Should().Contain("Customize the Quick Access Toolbar");
        xaml.Should().Contain("Show Quick Access Toolbar _below the Ribbon");
        xaml.Should().Contain("x:Name=\"QuickAccessResetButton\"");
        xaml.Should().Contain("Click=\"QuickAccessResetButton_Click\"");

        source.Should().Contain("PanelQuickAccessToolbar.Visibility = selectedIndex == 8 ? Visibility.Visible : Visibility.Collapsed;");
        source.Should().Contain("DeferredCommandMessages.QuickAccessToolbarReset()");

        deferredMessages.Should().Contain("DeferredCommand_QuickAccessToolbar_Body");
        UiText.Get("DeferredCommand_QuickAccessToolbar_Body")
            .Should().Contain("Quick Access Toolbar customization is not persisted in FreeX yet");
        UiText.Get("DeferredCommand_QuickAccessToolbar_Body")
            .Should().Contain("so there is no custom toolbar state to reset.");
    }

    [Fact]
    public void OptionsDialog_MoveAfterEnterToggleControlsDirectionEnabledState()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

        xaml.Should().Contain("Checked=\"MoveAfterEnter_Changed\"");
        xaml.Should().Contain("Unchecked=\"MoveAfterEnter_Changed\"");
        source.Should().Contain("UpdateAfterEnterDirectionState();");
        source.Should().Contain("private void MoveAfterEnter_Changed(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void UpdateAfterEnterDirectionState()");
        source.Should().Contain("OptAfterEnterDirection.IsEnabled = OptMoveAfterEnter.IsChecked == true;");
    }

    [Fact]
    public void OptionsDialog_ShowFormulaBarToggleControlsExpandedState()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

        xaml.Should().Contain("Checked=\"ShowFormulaBar_Changed\"");
        xaml.Should().Contain("Unchecked=\"ShowFormulaBar_Changed\"");
        source.Should().Contain("UpdateFormulaBarExpandedState();");
        source.Should().Contain("private void ShowFormulaBar_Changed(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void UpdateFormulaBarExpandedState()");
        source.Should().Contain("OptFormulaBarExpanded.IsEnabled = OptShowFormulaBar.IsChecked == true;");
        source.Should().Contain("FormulaBarExpanded = OptShowFormulaBar.IsChecked == true && OptFormulaBarExpanded.IsChecked == true");
    }

    [Fact]
    public void OptionsDialog_RuntimeFormulaBarToggleControlsExpandedState()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(new FreeXOptions
            {
                ShowFormulaBar = false,
                FormulaBarExpanded = true
            });

            dialog.Show();
            try
            {
                var showFormulaBar = GetControl<CheckBox>(dialog, "OptShowFormulaBar");
                var expandedFormulaBar = GetControl<CheckBox>(dialog, "OptFormulaBarExpanded");

                expandedFormulaBar.IsChecked.Should().BeTrue();
                expandedFormulaBar.IsEnabled.Should().BeFalse();

                showFormulaBar.IsChecked = true;

                expandedFormulaBar.IsEnabled.Should().BeTrue();

                showFormulaBar.IsChecked = false;

                expandedFormulaBar.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Viewport_MapsObjectPlaceholderOptionToGridDisplayMode()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Viewport.cs"));

        source.Should().Contain("SheetGrid.ObjectDisplayMode = _options.ObjectsDisplay switch");
        source.Should().Contain("FreeXObjectDisplay.Placeholders => FreeX.App.UI.GridObjectDisplayMode.Placeholders");
        source.Should().Contain("FreeXObjectDisplay.Nothing => FreeX.App.UI.GridObjectDisplayMode.Nothing");
        source.Should().Contain("var keepObjectData = _options.ObjectsDisplay != FreeXObjectDisplay.Nothing");
    }

    [Fact]
    public void OptionsDialog_AppliesWorksheetViewOptionsThroughUndoableCommand()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var workbookUiSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.WorkbookUiState.cs"));

        backstageSource.Should().Contain("ApplyOptionsWorksheetViewSettings()");
        backstageSource.Should().Contain("new SetWorksheetViewOptionsCommand(");
        workbookUiSource.Should().NotContain("currentSheet.ShowGridlines = _options.ShowGridlines");
        workbookUiSource.Should().NotContain("currentSheet.ShowHeadings = _options.ShowHeadings");
    }

    private static T GetControl<T>(OptionsDialog dialog, string name)
        where T : class
    {
        var field = typeof(OptionsDialog).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }
}
