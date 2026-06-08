using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

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
        var button = ReadMainWindowXaml().ExtractButtonElementByInvariantCommandName(title);

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
        var item = ReadMainWindowXaml()
            .ExtractElementByLocalizedAttributeValue("MenuItem", "Header", header, "Click=\"ZoomPresetMenuItem_Click\"");

        item.ShouldContainLocalizedAttribute("Header", header);
        item.Should().Contain($"Tag=\"{tag}\"");
        item.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        item.Should().Contain("Click=\"ZoomPresetMenuItem_Click\"");
    }

    [Fact]
    public void ViewZoomCustomMenuItem_OpensZoomDialog()
    {
        var item = ReadMainWindowXaml()
            .ExtractElementByLocalizedAttributeValue("MenuItem", "Header", "Custom...");

        item.Should().Contain("local:RibbonTooltip.KeyTip=\"C\"");
        item.Should().Contain("Click=\"ZoomCustomMenuItem_Click\"");
    }

    [Fact]
    public void ViewZoomHandlers_RouteThroughZoomMapperDialogAndSelectionPlanner()
    {
        var source = ReadHostSourceFile("MainWindow.ViewCommands.cs");

        SourceMethodExtractor.ExtractMethodSource(source, "private void ZoomPickerBtn_Click(")
            .Should().Contain("ZoomCustomMenuItem_Click(sender, e);");
        source.Should().Contain("FreeX.App.UI.ZoomLevelMapper.TryParseZoomPercent(tag, out var zoomPercent)");
        source.Should().Contain("new ZoomDialog(current) { Owner = this }");
        source.Should().Contain("ZoomSelectionPlanner.CalculateDialogZoomPercent(");
        source.Should().Contain("private void Zoom100Btn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("ZoomSlider.Value = 100;");
        source.Should().Contain("ZoomSelectionPlanner.CalculateFitPercent(");
        source.Should().Contain("FreeX.App.UI.ZoomLevelMapper.ZoomPercentToSlider(fitPct)");
    }

    [Theory]
    // Every View ▸ Window command is now live: each routes to a dedicated handler backed by the
    // WorkbookWindowRegistry / window-layout planners instead of a deferred-stub planner handler.
    [InlineData("New Window", "NW", "ViewNewWindowBtn_Click")]
    [InlineData("Arrange All", "A", "ArrangeAllPickerBtn_Click")]
    [InlineData("Freeze Panes", "FP", "FreezePanesPickerBtn_Click")]
    [InlineData("Split", "SP", "SplitViewBtn_Click")]
    [InlineData("Switch Windows", "W", "ViewSwitchWindowsBtn_Click")]
    [InlineData("Hide", "H", "ViewHideWindowBtn_Click")]
    [InlineData("Unhide", "U", "ViewUnhideWindowBtn_Click")]
    [InlineData("Reset Window Position", "RP", "ViewResetWindowPositionBtn_Click")]
    [InlineData("View Side by Side", "B", "ViewSideBySideBtn_Click")]
    [InlineData("Synchronous Scrolling", "SS", "ViewSynchronousScrollingBtn_Click")]
    public void ViewWindowCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var xaml = ReadMainWindowXaml();
        var button = title is "Split" or "View Side by Side" or "Synchronous Scrolling"
            ? xaml.ExtractElementByInvariantCommandName("ToggleButton", title)
            : xaml.ExtractButtonElementByInvariantCommandName(title);

        button.ShouldContainLocalizedAttribute("Content", title);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void ViewWindowCommands_AreAllLiveAndNeverDeferredRibbonPlaceholders()
    {
        var xaml = ReadMainWindowXaml()
            .ExtractElementByAttributeValue("Grid", "local:RibbonMetadata.CatalogId", "ViewWindowGroup");

        // Hide / Unhide / Reset Window Position / View Side by Side / Synchronous Scrolling are now
        // live commands with dedicated handlers — present in the ribbon, never deferred stubs.
        var expected = new (string CommandName, string Handler)[]
        {
            ("Hide", "ViewHideWindowBtn_Click"),
            ("Unhide", "ViewUnhideWindowBtn_Click"),
            ("Reset Window Position", "ViewResetWindowPositionBtn_Click"),
            ("View Side by Side", "ViewSideBySideBtn_Click"),
            ("Synchronous Scrolling", "ViewSynchronousScrollingBtn_Click"),
        };

        foreach (var (commandName, handler) in expected)
        {
            xaml.Should().Contain($"local:RibbonMetadata.CommandName=\"{commandName}\"");
            xaml.Should().Contain($"Click=\"{handler}\"");
        }

        xaml.Should().NotContain("Click=\"ViewWindowCommandBtn_Click\"");
        xaml.Should().NotContain("Deferred:");
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
        var item = ReadMainWindowXaml()
            .ExtractElementByLocalizedAttributeValue("MenuItem", "Header", header, "Click=\"ArrangeAllMenuItem_Click\"");

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
        var item = ReadMainWindowXaml()
            .ExtractElementByLocalizedAttributeValue("MenuItem", "Header", header);

        item.ShouldContainLocalizedAttribute("Header", header);
        item.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        item.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void ViewWindowHandlers_RouteThroughExpectedPlannersAndCommands()
    {
        var source = ReadHostSourceFile("MainWindow.ViewCommands.cs");

        source.Should().Contain("ArrangeAllMenuPlanner.IsChecked(item.Tag, _workbook.WindowArrangement)");
        source.Should().Contain("ArrangeAllMenuPlanner.TryParseArrangement(");
        source.Should().Contain("new SetWorkbookWindowArrangementCommand(arrangement)");
        source.Should().Contain("_windowRegistry?.ArrangeVisibleWindows(arrangement, workArea.Width, workArea.Height)");
        source.Should().Contain("RefreshViewWindowCommandState()");
        source.Should().Contain("ApplyLiveWindowCommandState()");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableSwitchWindowsRequiresSecondVisibleWindow");
        source.Should().Contain("AutomationProperties.SetHelpText(button, description)");
        source.Should().NotContain("ViewWindowCommandPlanner");
        source.Should().NotContain("ViewWindowCommandBtn_Click");
        SourceMethodExtractor.ExtractMethodSource(source, "private void FreezePanesPickerBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        source.Should().Contain("new SetFreezePanesCommand(_currentSheetId, frozenRows, frozenCols)");
        source.Should().Contain("private void FreezeAtSelectionMenuItem_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void UnfreezeAllMenuItem_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new SetSplitPanesCommand(sheetId, splitRow, splitColumn)");
    }

    [Fact]
    public void ViewWindowLiveHandlers_RouteThroughRegistryAndWindowLayoutPlanners()
    {
        var source = ReadHostSourceFile("MainWindow.MultiWindow.cs");

        // Hide / Unhide are registry-driven, with owned IUserMessageService messages on refusal.
        source.Should().Contain("private void ViewHideWindowBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("_windowRegistry.Hide(this)");
        source.Should().Contain("private void ViewUnhideWindowBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("WorkbookWindowSelectionPlanner.BuildUnhideWindowTargets(_windowRegistry, _workbook.Name)");
        source.Should().Contain("new UnhideWindowDialog(targets)");
        source.Should().Contain("dialog.Result?.Window");
        source.Should().Contain("_windowRegistry.Unhide(window)");
        source.Should().Contain("_messageService.ShowWarning(");
        source.Should().Contain("_messageService.ShowInfo(");

        // Reset Window Position runs through the pure WindowResetPositionPlanner.
        source.Should().Contain("private void ViewResetWindowPositionBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("WindowResetPositionPlanner.Compute(workArea.Width, workArea.Height, index)");
        source.Should().Contain("SystemParameters.WorkArea");

        // Arrange All stores the workbook choice, then applies the live visible-window layout.
        var viewCommandsSource = ReadHostSourceFile("MainWindow.ViewCommands.cs");
        viewCommandsSource.Should().Contain("new SetWorkbookWindowArrangementCommand(arrangement)");
        viewCommandsSource.Should().Contain("_windowRegistry?.ArrangeVisibleWindows(arrangement, workArea.Width, workArea.Height)");

        // View Side by Side toggles registry state and tiles via the registry/SideBySideLayoutPlanner.
        source.Should().Contain("private void ViewSideBySideBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("_windowRegistry.IsSideBySideActive");
        source.Should().Contain("_windowRegistry.EnableSideBySide(this, workArea.Width, workArea.Height)");
        source.Should().Contain("_windowRegistry.DisableSideBySide()");

        // Synchronous Scrolling toggles registry sync state and broadcasts offsets through the registry.
        source.Should().Contain("private void ViewSynchronousScrollingBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("_windowRegistry.SetSynchronousScroll(");
        source.Should().Contain("_windowRegistry?.BroadcastScrollOffset(this, GetScrollOffset())");
    }

}
