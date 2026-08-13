using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class ViewCommandSourceTests
{

    [Fact]
    public void ViewZoomHandlers_RouteThroughZoomMapperDialogAndSelectionPlanner()
    {
        var source = ReadHostSourceFile("MainWindow.ViewCommands.cs");

        SourceMethodExtractor.ExtractMethodSource(source, "private void ZoomPickerBtn_Click(")
            .Should().Contain("ZoomCustomMenuItem_Click(sender, e);");
        source.Should().Contain("FreeX.App.Services.ZoomLevelMapper.TryParseZoomPercent(tag, out var zoomPercent)");
        source.Should().Contain("new ZoomDialog(current) { Owner = this }");
        source.Should().Contain("ZoomSelectionPlanner.CalculateZoomPercent(");
        source.Should().Contain("private void Zoom100Btn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("ZoomSlider.Value = StatusZoomSliderValueForPercent(ZoomLevelMapper.DefaultZoomPercent);");
        source.Should().Contain("ZoomSelectionPlanner.CalculateFitPercent(");
        source.Should().Contain("private void ConfigureStatusZoomSlider()");
        source.Should().Contain("private static double StatusZoomSliderValueForPercent(double zoomPercent)");
        source.Should().Contain("var inputPlan = StatusBarZoomSliderPlanner.BuildInput(e.NewValue);");
        source.Should().NotContain("Math.Abs(sliderVal - 100.0)");
        source.Should().NotContain("ZoomLevelMapper.SliderToZoomPercent(sliderVal)");
        source.Should().NotContain("ZoomLevelMapper.ZoomPercentToSlider(fitPct)");
    }

    // R79-render-namebar-statusbar-5-1 / -5-4: Zoom-to-Selection must (a) scroll the fitted
    // selection into view -- not just change the zoom % while leaving the scrollbars untouched --
    // and (b) fit the bounding box of the WHOLE multi-area selection (SelectedRanges), not just the
    // last-clicked active range.
    [Fact]
    public void ZoomSelectionBtn_Click_ResolvesMultiAreaFitRangeAndScrollsSelectionIntoView()
    {
        var source = ReadHostSourceFile("MainWindow.ViewCommands.cs");

        var method = SourceMethodExtractor.ExtractMethodSource(source, "private void ZoomSelectionBtn_Click(");
        method.Should().Contain("ZoomSelectionPlanner.ResolveFitRange(activeRange, SheetGrid.SelectedRanges)");
        method.Should().Contain("EnsureCellVisible(range.Start)");
    }

    [Fact]
    public void ViewWindowHandlers_RouteThroughExpectedPlannersAndCommands()
    {
        var source = ReadHostSourceFile("MainWindow.ViewCommands.cs");
        var sessionSource = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "WorkbookSession.cs");

        source.Should().Contain("ArrangeAllMenuPlanner.IsChecked(item.Tag, _workbook.WindowArrangement)");
        source.Should().Contain("ArrangeAllMenuPlanner.TryParseArrangement(");
        source.Should().Contain("new SetWorkbookWindowArrangementCommand(arrangement)");
        source.Should().Contain("_windowRegistry?.ArrangeVisibleWindows(arrangement, workArea.Width, workArea.Height)");
        source.Should().Contain("RefreshViewWindowCommandState()");
        source.Should().Contain("ApplyLiveWindowCommandState()");
        source.Should().Contain("var canSwitchWindows = (_windowRegistry?.VisibleCount ?? 1) > 1;");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableSwitchWindowsRequiresSecondVisibleWindow");
        source.Should().Contain("AutomationProperties.SetHelpText(control, description)");
        source.Should().NotContain("ViewWindowCommandPlanner");
        source.Should().NotContain("ViewWindowCommandBtn_Click");
        SourceMethodExtractor.ExtractMethodSource(source, "private void FreezePanesPickerBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        source.Should().Contain("ApplyFreezePanes(_session.FreezePanesAtActiveCell)");
        source.Should().Contain("ApplyFreezePanes(_session.UnfreezePanes)");
        source.Should().Contain("_session.SetFreezePanes(frozenRows, frozenCols)");
        source.Should().Contain("private void FreezeAtSelectionMenuItem_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void UnfreezeAllMenuItem_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("_session.SetSplitPanes(nextRow, nextColumn)");
        sessionSource.Should().Contain("new SetFreezePanesCommand(ActiveSheet.Id, frozenRows, frozenCols)");
        sessionSource.Should().Contain("new SetSplitPanesCommand(sheetId, splitRow, splitColumn)");
    }

    [Fact]
    public void ViewWindowLiveHandlers_RouteThroughRegistryAndWindowLayoutPlanners()
    {
        var source = ReadHostSourceFile("MainWindow.MultiWindow.cs");

        // Hide / Unhide are registry-driven, with owned IUserMessageService messages on refusal.
        source.Should().Contain("private void ViewHideWindowBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("_windowRegistry.Hide(this)");
        source.Should().Contain("private void ViewUnhideWindowBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("WorkbookWindowSelectionPlanner.BuildUnhideWindowTargets(");
        source.Should().Contain("BuildWorkbookWindowSelectionEntries(_windowRegistry, hidden)");
        source.Should().Contain("new UnhideWindowDialog(targets)");
        source.Should().Contain("dialog.Result?.Window");
        source.Should().Contain("_windowRegistry.Unhide(window)");
        source.Should().Contain("_messageService.ShowWarning(");
        source.Should().Contain("_messageService.ShowInfo(");

        // Reset Window Position (R90-app-window-arrange-freeze-ui-5-3) restores the active
        // side-by-side pair's tiled halves via the registry, instead of cascading/recentering
        // just the clicked window through the unrelated WindowResetPositionPlanner formula.
        source.Should().Contain("private void ViewResetWindowPositionBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("_windowRegistry?.ResetSideBySidePair(workArea.Width, workArea.Height)");
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
