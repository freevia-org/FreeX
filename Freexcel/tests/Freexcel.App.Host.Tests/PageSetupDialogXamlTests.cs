using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class PageSetupDialogXamlTests
{
    [Fact]
    public void PageSetupDialog_ExposesKeyboardAccessKeysForTabsOptionsAndButtons()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));

        foreach (var header in new[]
        {
            "_Page",
            "_Margins",
            "_Header/Footer",
            "_Sheet"
        })
            xaml.Should().Contain($"Header=\"{header}\"");

        foreach (var content in new[]
        {
            "_Orientation:",
            "_Paper size:",
            "First _page number:",
            "Print _quality:",
            "_Left:",
            "_Right:",
            "_Top:",
            "_Bottom:",
            "_Header:",
            "_Footer:",
            "_Header preset:",
            "_Footer preset:",
            "Custom _Header...",
            "Custom _Footer...",
            "_Different first page",
            "Different _odd and even pages",
            "_Scale with document",
            "_Align with page margins",
            "Print _area:",
            "_Rows to repeat at top:",
            "_Columns to repeat at left:",
            "_Center horizontally",
            "Center _vertically",
            "_Print gridlines",
            "Print row and column _headings",
            "Pa_ge order:",
            "_Black and white",
            "_Draft quality",
            "Cell _errors as:",
            "Co_mments:",
            "_OK",
            "_Cancel"
        })
            xaml.Should().Contain(content);
    }

    [Fact]
    public void PageTab_UsesExcelLikeScalingChoices()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        document.Descendants(presentation + "GroupBox")
            .Single(element => element.Attribute("Header")?.Value == "Scaling")
            .Descendants(presentation + "RadioButton")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_Adjust to:", "_Fit to:"]);

        foreach (var name in new[] { "ScalePercentBox", "FitPagesWideBox", "FitPagesTallBox" })
        {
            document.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == name)
                .Should().BeTrue($"{name} should exist for Excel-style scaling input");
        }
    }

    [Fact]
    public void PageTab_DisablesInactiveScalingInputsByMode()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml.cs"));

        xaml.Should().Contain("Checked=\"ScalingMode_Changed\"");
        source.Should().Contain("UpdateScalingInputState");
        source.Should().Contain("ScalePercentBox.IsEnabled = adjustTo");
        source.Should().Contain("FitPagesWideBox.IsEnabled = fitTo");
        source.Should().Contain("FitPagesTallBox.IsEnabled = fitTo");
    }

    [Fact]
    public void HeaderFooterTab_ReusesSupportedPresetAndCustomDialogConcepts()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var tab = document.Descendants(presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "_Header/Footer");

        foreach (var name in new[]
        {
            "HeaderPresetBox",
            "FooterPresetBox",
            "CustomHeaderButton",
            "CustomFooterButton",
            "DifferentFirstPageBox",
            "DifferentOddEvenBox",
            "ScaleWithDocumentBox",
            "AlignWithMarginsBox"
        })
        {
            tab.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == name)
                .Should().BeTrue($"{name} should exist on the Page Setup Header/Footer tab");
        }
    }

    [Fact]
    public void SheetTab_ExposesHonestRangePickerButtonsForPrintRanges()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var (buttonName, targetName, automationName) in new[]
        {
            ("PrintAreaPickerButton", "PrintAreaBox", "Focus print area range"),
            ("RowsRepeatPickerButton", "RowsRepeatBox", "Focus rows to repeat range"),
            ("ColumnsRepeatPickerButton", "ColumnsRepeatBox", "Focus columns to repeat range")
        })
        {
            var button = document.Descendants(presentation + "Button")
                .SingleOrDefault(element => element.Attribute(x + "Name")?.Value == buttonName);

            button.Should().NotBeNull($"{buttonName} should expose Excel-like picker affordance");
            button!.Attribute("Content")?.Value.Should().Be("...");
            button.Attribute("Click")?.Value.Should().Be("RangePickerButton_Click");
            button.Attribute("ToolTip")?.Value.Should().Contain("Focuses and selects");
            button.Attribute("Tag")?.Value.Should().Be(targetName);
            button.Attribute(x + "Name")?.Value.Should().Be(buttonName);
            button.Attribute("AutomationProperties.Name")?.Value.Should().Be(automationName);
        }

        source.Should().Contain("RangePickerButton_Click");
        source.Should().Contain("target.Focus()");
        source.Should().Contain("target.SelectAll()");
    }

    [Fact]
    public void Footer_ExposesExcelPrintActionsAndHonestOptionsState()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml.cs"));
        var handlerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PageLayout.cs"));

        foreach (var content in new[] { "Print Pre_view", "_Print...", "_Options..." })
            xaml.Should().Contain($"Content=\"{content}\"");

        xaml.Should().Contain("ToolTip=\"Printer driver options are not available yet.\"");
        source.Should().Contain("PageSetupDialogAction.PrintPreview");
        source.Should().Contain("PageSetupDialogAction.Print");
        handlerSource.Should().Contain("dialog.RequestedAction is PageSetupDialogAction.Print or PageSetupDialogAction.PrintPreview");
        handlerSource.Should().Contain("PrintButton_Click(this, new RoutedEventArgs())");
    }

    [Fact]
    public void PageSetupHandler_AppliesHeaderFooterValuesReturnedByDialog()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PageLayout.cs"));

        source.Should().Contain("new CompositeWorkbookCommand(");
        source.Should().Contain("new SetHeaderFooterCommand(");
        source.Should().Contain("dialog.FirstPageHeader");
        source.Should().Contain("dialog.EvenPageFooter");
        source.Should().Contain("dialog.ScaleHeaderFooterWithDocument");
        source.Should().Contain("dialog.AlignHeaderFooterWithMargins");
    }
}
