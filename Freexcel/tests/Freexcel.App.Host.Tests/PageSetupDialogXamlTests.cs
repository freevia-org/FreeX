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
    public void SheetTab_ProvidesRangePickerAffordancesForPrintAreaAndTitles()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var name in new[]
        {
            "PrintAreaBox",
            "PrintAreaPickerButton",
            "RowsRepeatPickerButton",
            "ColumnsRepeatPickerButton"
        })
        {
            document.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == name)
                .Should().BeTrue($"{name} should exist for range selection affordance");
        }
    }

    [Fact]
    public void Footer_ExposesExcelLikePrintActions()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageSetupDialog.xaml"));

        foreach (var content in new[] { "Print Pre_view", "_Print...", "_Options..." })
            xaml.Should().Contain($"Content=\"{content}\"");
    }
}
