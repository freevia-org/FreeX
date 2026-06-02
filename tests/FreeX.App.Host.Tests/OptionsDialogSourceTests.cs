using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        xaml.Should().Contain("<ListBoxItem Content=\"View\"/>");
        xaml.Should().Contain("x:Name=\"PanelView\"");
        xaml.Should().Contain("x:Name=\"OptShowFormulaBar\"");
        xaml.Should().Contain("x:Name=\"OptFormulaBarExpanded\"");
        source.Should().Contain("OptShowFormulaBar.IsChecked = _opts.ShowFormulaBar");
        source.Should().Contain("OptFormulaBarExpanded.IsChecked = _opts.FormulaBarExpanded");
        source.Should().Contain("ShowFormulaBar     = OptShowFormulaBar.IsChecked == true");
        source.Should().Contain("FormulaBarExpanded = OptShowFormulaBar.IsChecked == true && OptFormulaBarExpanded.IsChecked == true");
    }

    [Fact]
    public void OptionsDialog_RoundTripsPersistedGeneralUiOptions()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "FreeXOptionsDialogTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(tempDirectory, "options.json");
        var previousPath = Environment.GetEnvironmentVariable(FreeXOptions.OptionsPathEnvironmentVariable);
        Environment.SetEnvironmentVariable(FreeXOptions.OptionsPathEnvironmentVariable, path);

        try
        {
            StaTestRunner.Run(() =>
            {
                var dialog = new OptionsDialog(new FreeXOptions
                {
                    CollapseRibbonAutomatically = true,
                    ShowScreenTips = false
                });
                dialog.Show();
                try
                {
                    var collapseRibbon = GetControl<CheckBox>(dialog, "OptCollapseRibbon");
                    var showScreenTips = GetControl<CheckBox>(dialog, "OptShowScreenTips");

                    collapseRibbon.IsChecked.Should().BeTrue();
                    showScreenTips.IsChecked.Should().BeFalse();

                    collapseRibbon.IsChecked = false;
                    showScreenTips.IsChecked = true;

                    ClickOkAllowingNonModalDialogResult(dialog);

                    dialog.Result.CollapseRibbonAutomatically.Should().BeFalse();
                    dialog.Result.ShowScreenTips.Should().BeTrue();
                }
                finally
                {
                    dialog.Close();
                }
            });

            var reloaded = FreeXOptions.LoadFromPath(path);
            reloaded.CollapseRibbonAutomatically.Should().BeFalse();
            reloaded.ShowScreenTips.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(FreeXOptions.OptionsPathEnvironmentVariable, previousPath);
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void OptionsDialog_PreservesPersistedExportOptionsWhenSavingGeneralOptions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

        source.Should().Contain("PdfExportLanguage = ExportPlanner.NormalizePdfLanguage(_opts.PdfExportLanguage)");
    }

    [Fact]
    public void OptionsDialog_ExposesPlainCategoryLabelsAndKeyboardAccessKeysForFieldsAndButtons()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("OptionsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "ListBoxItem")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain([
                "General",
                "Formulas",
                "Proofing",
                "Save",
                "Language",
                "Ease of Access",
                "Advanced",
                "Customize Ribbon",
                "Quick Access Toolbar",
                "Add-ins",
                "Trust Center",
                "View"
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
            "QuickAccessAddButton_Click",
            "QuickAccessRemoveButton_Click",
            "QuickAccessMoveUpButton_Click",
            "QuickAccessMoveDownButton_Click",
            "QuickAccessImportExportButton_Click",
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
        source.Should().Contain("DeferredCommandMessages.OfficeAddIns()");
        source.Should().Contain("DeferredCommandMessages.TrustCenterSettings()");
    }

    [Fact]
    public void OptionsDialog_ExposesPersistedQuickAccessToolbarCustomization()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

        xaml.Should().Contain("<ListBoxItem Content=\"Quick Access Toolbar\"/>");
        xaml.Should().Contain("x:Name=\"PanelQuickAccessToolbar\"");
        xaml.Should().Contain("Customize the Quick Access Toolbar");
        xaml.Should().Contain("Show Quick Access Toolbar _below the Ribbon");
        xaml.Should().Contain("x:Name=\"QuickAccessSearchBox\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"QuickAccessToolbarCommandSearchBox\"");
        xaml.Should().Contain("TextChanged=\"QuickAccessSearchBox_TextChanged\"");
        xaml.Should().Contain("x:Name=\"QuickAccessAvailableCommandsList\"");
        xaml.Should().Contain("x:Name=\"QuickAccessSelectedCommandsList\"");
        xaml.Should().Contain("x:Name=\"QuickAccessAddButton\"");
        xaml.Should().Contain("x:Name=\"QuickAccessRemoveButton\"");
        xaml.Should().Contain("x:Name=\"QuickAccessMoveUpButton\"");
        xaml.Should().Contain("x:Name=\"QuickAccessMoveDownButton\"");
        xaml.Should().Contain("x:Name=\"QuickAccessResetButton\"");
        xaml.Should().Contain("x:Name=\"QuickAccessImportExportButton\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"QuickAccessToolbarImportExportButton\"");
        xaml.Should().Contain("Click=\"QuickAccessResetButton_Click\"");
        xaml.Should().Contain("Click=\"QuickAccessImportExportButton_Click\"");
        xaml.Should().Contain("MouseDoubleClick=\"QuickAccessAvailableCommandsList_MouseDoubleClick\"");
        xaml.Should().Contain("MouseDoubleClick=\"QuickAccessSelectedCommandsList_MouseDoubleClick\"");

        source.Should().Contain("PanelQuickAccessToolbar.Visibility = selectedIndex == 8 ? Visibility.Visible : Visibility.Collapsed;");
        source.Should().Contain("QuickAccessToolbarCatalog.NormalizeCommandIds(_opts.QuickAccessToolbarCommands)");
        source.Should().Contain("QuickAccessToolbarBelowRibbon = QuickAccessBelowRibbonCheckBox.IsChecked == true");
        source.Should().Contain("QuickAccessToolbarCommands = QuickAccessToolbarCatalog.NormalizeCommandIds(_quickAccessCommandIds).ToList()");
        source.Should().Contain("QuickAccessToolbarCatalog.DefaultCommandIds");
        source.Should().Contain("QuickAccessCommandMatchesFilter(command, filterText)");
        source.Should().Contain("QuickAccessSearchBox_TextChanged");
        source.Should().Contain("private void QuickAccessAvailableCommandsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)");
        source.Should().Contain("private void QuickAccessSelectedCommandsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)");
        source.Should().Contain("QuickAccessAddButton_Click(sender, e);");
        source.Should().Contain("QuickAccessRemoveButton_Click(sender, e);");
        source.Should().Contain("e.Handled = true;");
        source.Should().Contain("QuickAccessToolbarCustomizationFile.TryLoad(dialog.FileName)");
        source.Should().Contain("QuickAccessToolbarCustomizationFile.TrySave(");
        source.Should().Contain("new OpenFileDialog");
        source.Should().Contain("new SaveFileDialog");
        source.Should().Contain("QuickAccessToolbarImportCustomizationMenuItem");
        source.Should().Contain("QuickAccessToolbarExportCustomizationMenuItem");
        source.Should().NotContain("DeferredCommandMessages.QuickAccessToolbarReset()");
    }

    [Fact]
    public void OptionsDialog_RuntimeQuickAccessToolbarSearchFiltersAvailableCommandsAndKeepsAddFlow()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(new FreeXOptions());
            dialog.Show();
            try
            {
                var tabList = GetControl<ListBox>(dialog, "TabList");
                var searchBox = GetControl<TextBox>(dialog, "QuickAccessSearchBox");
                var availableList = GetControl<ListBox>(dialog, "QuickAccessAvailableCommandsList");
                var selectedList = GetControl<ListBox>(dialog, "QuickAccessSelectedCommandsList");
                var addButton = GetControl<Button>(dialog, "QuickAccessAddButton");

                tabList.SelectedIndex = 8;
                availableList.Items.Count.Should().BeGreaterThan(1);

                searchBox.Text = "bold";

                GetListDisplayNames(availableList).Should().Equal("Bold");
                addButton.IsEnabled.Should().BeFalse();

                availableList.SelectedIndex = 0;
                addButton.IsEnabled.Should().BeTrue();

                addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                GetListDisplayNames(availableList).Should().BeEmpty();
                GetListDisplayNames(selectedList).Should().Contain("Bold");
                addButton.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_RuntimeQuickAccessToolbarDoubleClickAddsAndRemovesSelectedCommand()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(new FreeXOptions());
            dialog.Show();
            try
            {
                var tabList = GetControl<ListBox>(dialog, "TabList");
                var searchBox = GetControl<TextBox>(dialog, "QuickAccessSearchBox");
                var availableList = GetControl<ListBox>(dialog, "QuickAccessAvailableCommandsList");
                var selectedList = GetControl<ListBox>(dialog, "QuickAccessSelectedCommandsList");

                tabList.SelectedIndex = 8;
                searchBox.Text = "bold";
                availableList.SelectedIndex = 0;

                var addDoubleClick = CreateMouseDoubleClickEvent();
                availableList.RaiseEvent(addDoubleClick);

                addDoubleClick.Handled.Should().BeTrue();
                GetListDisplayNames(availableList).Should().BeEmpty();
                GetListDisplayNames(selectedList).Should().Contain("Bold");

                selectedList.SelectedItem = selectedList.Items
                    .Cast<object>()
                    .First(item => string.Equals(GetListDisplayName(item), "Bold", StringComparison.Ordinal));

                var removeDoubleClick = CreateMouseDoubleClickEvent();
                selectedList.RaiseEvent(removeDoubleClick);

                removeDoubleClick.Handled.Should().BeTrue();
                GetListDisplayNames(availableList).Should().Equal("Bold");
                GetListDisplayNames(selectedList).Should().NotContain("Bold");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_SurfacePersistenceFailuresInsteadOfClosingSilently()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

        source.Should().Contain("if (!opts.Save())");
        source.Should().Contain("DialogMessageHelper.ShowError(this, opts.LastPersistenceError, Title);");
        source.Should().Contain("return;");
        source.Should().Contain("DialogResult = true;");
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

    private static IReadOnlyList<string> GetListDisplayNames(ListBox listBox) =>
        listBox.Items
            .Cast<object>()
            .Select(GetListDisplayName)
            .ToArray();

    private static string GetListDisplayName(object item) =>
        item.GetType().GetProperty("DisplayName")?.GetValue(item) as string ?? string.Empty;

    private static MouseButtonEventArgs CreateMouseDoubleClickEvent() =>
        new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Control.MouseDoubleClickEvent
        };

    private static void ClickOkAllowingNonModalDialogResult(OptionsDialog dialog)
    {
        var okButton = GetControl<Button>(dialog, "OkBtn");
        try
        {
            okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        catch (InvalidOperationException invalidOperation)
            when (invalidOperation.Message.Contains("DialogResult", StringComparison.Ordinal))
        {
            // The handler commits Result before setting DialogResult. Direct modeless invocation in
            // tests reaches that WPF guard after exercising the same save path as the dialog button.
        }
    }
}
