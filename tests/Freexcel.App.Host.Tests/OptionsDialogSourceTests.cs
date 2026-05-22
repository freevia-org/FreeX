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

        source.Should().Contain("PanelAdvanced.Visibility");
        source.Should().Contain("OptAfterEnterDirection.ItemsSource");
        source.Should().Contain("OptMoveAfterEnter.IsChecked = _opts.MoveSelectionAfterEnter");
        source.Should().Contain("ShowGridlines = OptShowGridlines.IsChecked == true");
        source.Should().Contain("ShowHeadings = OptShowHeadings.IsChecked == true");
        source.Should().Contain("ObjectsDisplay = OptObjectsDisplay.SelectedIndex switch");
        source.Should().Contain("OptObjectsDisplay.ItemsSource");
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
