using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class OptionsDialogSourceTests
{
    [Fact]
    public void OptionsDialog_ExposesPersistedViewOptions()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml.cs"));

        xaml.Should().Contain("<ListBoxItem Content=\"_View\"/>");
        xaml.Should().Contain("x:Name=\"PanelView\"");
        xaml.Should().Contain("x:Name=\"OptShowFormulaBar\"");
        xaml.Should().Contain("x:Name=\"OptFormulaBarExpanded\"");
        source.Should().Contain("OptShowFormulaBar.IsChecked = _opts.ShowFormulaBar");
        source.Should().Contain("OptFormulaBarExpanded.IsChecked = _opts.FormulaBarExpanded");
        source.Should().Contain("ShowFormulaBar     = OptShowFormulaBar.IsChecked == true");
        source.Should().Contain("FormulaBarExpanded = OptFormulaBarExpanded.IsChecked == true");
    }

    [Fact]
    public void OptionsDialog_PreservesPersistedExportOptionsWhenSavingGeneralOptions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml.cs"));

        source.Should().Contain("PdfExportLanguage = ExportPlanner.NormalizePdfLanguage(_opts.PdfExportLanguage)");
    }

    [Fact]
    public void OptionsDialog_ExposesKeyboardAccessKeysForTabsFieldsAndButtons()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml"));
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml.cs"));

        source.Should().Contain("Loaded += (_, _) =>");
        source.Should().Contain("FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("TabList.Focus();");
        source.Should().Contain("Keyboard.Focus(TabList);");
    }

    [Fact]
    public void OptionsDialogInvalidGeneralInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml.cs"));

        source.Should().Contain("OptionsInputParser.TryParseDefaultFontSize(OptDefaultFontSize.Text, out var defaultFontSize)");
        source.Should().Contain("ShowInvalidInputWarning(\"Enter a positive default font size.\", OptDefaultFontSize);");
        source.Should().Contain("OptionsInputParser.TryParseDefaultSheetCount(OptSheetCount.Text, out var defaultSheetCount)");
        source.Should().Contain("ShowInvalidInputWarning(\"Enter the number of sheets to include from 1 to 255.\", OptSheetCount);");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, Control target)");
        source.Should().Contain("MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);");
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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml.cs"));

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
            "AddLanguageButton_Click",
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
        source.Should().Contain("DeferredCommandMessages.EditingLanguages()");
        source.Should().Contain("DeferredCommandMessages.RibbonCustomizationImportExport()");
        source.Should().Contain("DeferredCommandMessages.QuickAccessToolbarReset()");
        source.Should().Contain("DeferredCommandMessages.OfficeAddIns()");
        source.Should().Contain("DeferredCommandMessages.TrustCenterSettings()");
    }

    [Fact]
    public void OptionsDialog_ExposesQuickAccessToolbarCustomizationAsDeferredAffordance()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml.cs"));
        var deferredMessages = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DeferredCommandMessages.cs"));

        xaml.Should().Contain("<ListBoxItem Content=\"_Quick Access Toolbar\"/>");
        xaml.Should().Contain("x:Name=\"PanelQuickAccessToolbar\"");
        xaml.Should().Contain("Customize the Quick Access Toolbar");
        xaml.Should().Contain("Show Quick Access Toolbar _below the Ribbon");
        xaml.Should().Contain("x:Name=\"QuickAccessResetButton\"");
        xaml.Should().Contain("Click=\"QuickAccessResetButton_Click\"");

        source.Should().Contain("PanelQuickAccessToolbar.Visibility = selected == \"_Quick Access Toolbar\" ? Visibility.Visible : Visibility.Collapsed;");
        source.Should().Contain("DeferredCommandMessages.QuickAccessToolbarReset()");

        deferredMessages.Should().Contain("Quick Access Toolbar customization is not persisted in Freexcel yet");
        deferredMessages.Should().Contain("so there is no custom toolbar state to reset.");
    }

    [Fact]
    public void OptionsDialog_MoveAfterEnterToggleControlsDirectionEnabledState()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "OptionsDialog.xaml.cs"));

        xaml.Should().Contain("Checked=\"MoveAfterEnter_Changed\"");
        xaml.Should().Contain("Unchecked=\"MoveAfterEnter_Changed\"");
        source.Should().Contain("UpdateAfterEnterDirectionState();");
        source.Should().Contain("private void MoveAfterEnter_Changed(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void UpdateAfterEnterDirectionState()");
        source.Should().Contain("OptAfterEnterDirection.IsEnabled = OptMoveAfterEnter.IsChecked == true;");
    }

    [Fact]
    public void Viewport_MapsObjectPlaceholderOptionToGridDisplayMode()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Viewport.cs"));

        source.Should().Contain("SheetGrid.ObjectDisplayMode = _options.ObjectsDisplay switch");
        source.Should().Contain("FreexcelObjectDisplay.Placeholders => Freexcel.App.UI.GridObjectDisplayMode.Placeholders");
        source.Should().Contain("FreexcelObjectDisplay.Nothing => Freexcel.App.UI.GridObjectDisplayMode.Nothing");
        source.Should().Contain("var keepObjectData = _options.ObjectsDisplay != FreexcelObjectDisplay.Nothing");
    }

    [Fact]
    public void OptionsDialog_AppliesWorksheetViewOptionsThroughUndoableCommand()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));
        var workbookUiSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorkbookUiState.cs"));

        backstageSource.Should().Contain("ApplyOptionsWorksheetViewSettings()");
        backstageSource.Should().Contain("new SetWorksheetViewOptionsCommand(");
        workbookUiSource.Should().NotContain("currentSheet.ShowGridlines = _options.ShowGridlines");
        workbookUiSource.Should().NotContain("currentSheet.ShowHeadings = _options.ShowHeadings");
    }
}
