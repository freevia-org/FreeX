using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ViewCommandSourceTests
{
    [Theory]
    [InlineData("Zoom", "Q", "ZoomPickerBtn_Click")]
    [InlineData("100%", "Z1", "Zoom100Btn_Click")]
    [InlineData("Zoom to Selection", "ZS", "ZoomSelectionBtn_Click")]
    public void ViewZoomCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.ShouldContainLocalizedAttribute("Content", title);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("200%", "2", "200")]
    [InlineData("100%", "1", "100")]
    [InlineData("75%", "7", "75")]
    [InlineData("50%", "5", "50")]
    [InlineData("25%", "3", "25")]
    public void ViewZoomPresetMenuItems_ExposeExpectedKeyTipsTagsAndSharedHandler(
        string header,
        string keyTip,
        string tag)
    {
        var item = ExtractMenuItemElementByHeader(ReadMainWindowXaml(), header, "ZoomPresetMenuItem_Click");

        item.ShouldContainLocalizedAttribute("Header", header);
        item.Should().Contain($"Tag=\"{tag}\"");
        item.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        item.Should().Contain("Click=\"ZoomPresetMenuItem_Click\"");
    }

    [Fact]
    public void ViewZoomCustomMenuItem_OpensZoomDialog()
    {
        var item = ExtractMenuItemElementByHeader(ReadMainWindowXaml(), "Custom...");

        item.Should().Contain("local:RibbonTooltip.KeyTip=\"C\"");
        item.Should().Contain("Click=\"ZoomCustomMenuItem_Click\"");
    }

    [Fact]
    public void ViewZoomHandlers_RouteThroughZoomMapperDialogAndSelectionPlanner()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));

        source.Should().Contain("OpenRibbonContextMenu(btn, cm)");
        source.Should().Contain("FreeX.App.UI.ZoomLevelMapper.TryParseZoomPercent(tag, out var zoomPercent)");
        source.Should().Contain("new ZoomDialog(current) { Owner = this }");
        source.Should().Contain("ZoomSelectionPlanner.CalculateDialogZoomPercent(");
        source.Should().Contain("private void Zoom100Btn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("ZoomSlider.Value = 100;");
        source.Should().Contain("ZoomSelectionPlanner.CalculateFitPercent(");
        source.Should().Contain("FreeX.App.UI.ZoomLevelMapper.ZoomPercentToSlider(fitPct)");
    }

    [Theory]
    [InlineData("New Window", "NW", "ViewWindowDeferredBtn_Click")]
    [InlineData("Arrange All", "A", "ArrangeAllPickerBtn_Click")]
    [InlineData("Freeze Panes", "FP", "FreezePanesPickerBtn_Click")]
    [InlineData("Split", "SP", "SplitViewBtn_Click")]
    [InlineData("Hide", "H", "ViewWindowDeferredBtn_Click")]
    [InlineData("Unhide", "U", "ViewWindowDeferredBtn_Click")]
    [InlineData("View Side by Side", "B", "ViewWindowDeferredBtn_Click")]
    [InlineData("Synchronous Scrolling", "SS", "ViewWindowDeferredBtn_Click")]
    [InlineData("Reset Window Position", "RP", "ViewWindowDeferredBtn_Click")]
    [InlineData("Switch Windows", "W", "ViewWindowDeferredBtn_Click")]
    public void ViewWindowCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = ExtractCommandElementByTitle(ReadMainWindowXaml(), title);

        button.ShouldContainLocalizedAttribute("Content", title);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Tiled", "T", "Tiled")]
    [InlineData("Horizontal", "H", "Horizontal")]
    [InlineData("Vertical", "V", "Vertical")]
    [InlineData("Cascade", "C", "Cascade")]
    public void ViewArrangeAllMenuItems_ExposeExpectedKeyTipsTagsAndSharedHandler(
        string header,
        string keyTip,
        string tag)
    {
        var item = ExtractMenuItemElementByHeader(ReadMainWindowXaml(), header, "ArrangeAllMenuItem_Click");

        item.ShouldContainLocalizedAttribute("Header", header);
        item.Should().Contain($"Tag=\"{tag}\"");
        item.Should().Contain("IsCheckable=\"True\"");
        item.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        item.Should().Contain("Click=\"ArrangeAllMenuItem_Click\"");
    }

    [Theory]
    [InlineData("Freeze Panes", "F", "FreezeAtSelectionMenuItem_Click")]
    [InlineData("Freeze Top Row", "R", "FreezeTopRowMenuItem_Click")]
    [InlineData("Freeze First Column", "C", "FreezeFirstColMenuItem_Click")]
    [InlineData("Unfreeze Panes", "U", "UnfreezeAllMenuItem_Click")]
    public void ViewFreezePanesMenuItems_ExposeExpectedKeyTipsAndHandlers(
        string header,
        string keyTip,
        string handler)
    {
        var item = ExtractMenuItemElementByHeader(ReadMainWindowXaml(), header);

        item.ShouldContainLocalizedAttribute("Header", header);
        item.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        item.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void ViewWindowHandlers_RouteThroughExpectedPlannersAndCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));

        source.Should().Contain("ArrangeAllMenuPlanner.IsChecked(item.Tag, _workbook.WindowArrangement)");
        source.Should().Contain("ArrangeAllMenuPlanner.TryParseArrangement(");
        source.Should().Contain("new SetWorkbookWindowArrangementCommand(arrangement)");
        source.Should().Contain("DeferredCommandMessages.MultiWindow(commandName)");
        source.Should().Contain("new SetFreezePanesCommand(_currentSheetId, frozenRows, frozenCols)");
        source.Should().Contain("private void FreezeAtSelectionMenuItem_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void UnfreezeAllMenuItem_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new SetSplitPanesCommand(sheetId, splitRow, splitColumn)");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

    private static string ExtractButtonElementByTitle(string xaml, string title)
    {
        if (title == "Split")
            return xaml.ExtractElementByInvariantCommandName("ToggleButton", title);

        var button = xaml.ExtractElementByInvariantCommandName("Button", title);
        if (button.Contains($"local:RibbonMetadata.CommandName=\"{LocalizedXamlTestSupport.EscapeAttribute(title)}\"", StringComparison.Ordinal))
            return button;

        return xaml.ExtractElementByInvariantCommandName("ToggleButton", title);
    }

    private static string ExtractCommandElementByTitle(string xaml, string title) =>
        ExtractButtonElementByTitle(xaml, title);

    private static string ExtractMenuItemElementByHeader(string xaml, string header, string? clickHandler = null)
        => xaml.ExtractElementByLocalizedAttributeValue(
            "MenuItem",
            "Header",
            header,
            clickHandler is null ? null : $"Click=\"{clickHandler}\"");
}
