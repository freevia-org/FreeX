using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeFormatAsTableCommandSourceTests
{
    [Fact]
    public void FormatAsTableRibbonButton_ExposesExpectedKeyTipAndGalleryMenu()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var button = ExtractButtonElementByClickHandler(xaml, "FormatTableBtn_Click");

        button.ShouldContainInvariantCommandName("Format as Table");
        button.Should().Contain("local:RibbonTooltip.KeyTip=\"T\"");
        button.Should().Contain("Click=\"FormatTableBtn_Click\"");
        button.Should().Contain("x:Name=\"FormatTableGalleryMenu\"");
    }

    [Fact]
    public void FormatAsTableHandlers_RouteThroughGalleryPlannerAndStructuredTableCommand()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("private void FormatTableBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("PopulateFormatTableGalleryMenu();");
        source.Should().Contain("TableStyleGalleryPlanner.GetOptions()");
        source.Should().Contain("RibbonTooltip.SetKeyTip(menuItem, $\"{family[0]}{option.Label[(family.Length + 1)..]}\");");
        source.Should().Contain("menuItem.Click += FormatTableGalleryMenuItem_Click;");
        source.Should().Contain("TableStyleGalleryPlanner.GetOption(variant)");
        source.Should().Contain("new CreateTableDialog(");
        source.Should().Contain("request => ApplyCreateTableRangeSelection(dialog, request)");
        source.Should().Contain("new CreateStyledStructuredTableCommand(");
        source.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(dialog.Result.Range, sheetId)");
        source.Should().Contain("tableStyle.Banding");
    }

    private static string ExtractButtonElementByClickHandler(string xaml, string clickHandler)
    {
        var clickIndex = xaml.IndexOf($"Click=\"{clickHandler}\"", StringComparison.Ordinal);
        clickIndex.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should be present");

        var start = xaml.LastIndexOf("<Button", clickIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should have a Button start tag");

        var end = xaml.IndexOf("</Button>", clickIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(clickIndex, $"the {clickHandler} button should have an end tag");
        return xaml.Substring(start, end - start + "</Button>".Length);
    }
}
