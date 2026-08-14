using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class FreeXPracticalResidualOwnershipTests
{
    [Fact]
    public void ViewportAndSelectionPolicies_HavePortableOwners()
    {
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var viewportPlanner = Read("src", "FreeX.App.Services", "WorkbookViewportScrollPlanner.cs");
        var findPlanner = Read("src", "FreeX.App.Services", "FindReplaceDialogPlanner.cs");
        var wpfFind = Read("src", "FreeX.App.Host", "FindReplaceDialog.xaml.cs");

        // The scrollable row/column count now has a single neutral owner
        // (WorkbookViewportScrollPlanner.CountVisibleScrollableRows/Columns); neither renderer may
        // keep its own private copy of the Math.Max(1, ViewportService...) wrapper.
        viewportPlanner.Should().Contain("ViewportService.CountScrollableRows(viewport.RowMetrics");
        viewportPlanner.Should().Contain("ViewportService.CountScrollableColumns(viewport.ColMetrics");
        avalonia.Should().Contain("WorkbookViewportScrollPlanner.CountVisibleScrollableRows(");
        avalonia.Should().Contain("WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(");
        avalonia.Should().NotContain("ViewportService.CountScrollableRows(");
        avalonia.Should().NotContain("ViewportService.CountScrollableColumns(");
        var wpfViewport = Read("src", "FreeX.App.Host", "MainWindow.Viewport.cs");
        wpfViewport.Should().Contain("WorkbookViewportScrollPlanner.CountVisibleScrollableRows(");
        wpfViewport.Should().Contain("WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(");
        wpfViewport.Should().NotContain("ViewportService.CountScrollableRows(");
        wpfViewport.Should().NotContain("ViewportService.CountScrollableColumns(");
        findPlanner.Should().Contain("public static IReadOnlyList<GridRange>? ResolveSelectionScopeAtOpen");
        avalonia.Should().Contain("FindReplaceDialogPlanner.ResolveSelectionScopeAtOpen(");
        wpfFind.Should().Contain("FindReplaceDialogPlanner.ResolveSelectionScopeAtOpen(");
    }

    [Fact]
    public void CellCommentAndFillSemantics_HavePortableOwners()
    {
        var wpfGrid = Read("src", "FreeX.App.UI", "GridView.cs");
        var wpfComments = Read("src", "FreeX.App.UI", "GridView.CommentPreview.cs");
        var wpfFill = Read("src", "FreeX.App.Host", "MainWindow.HomeEditing.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var session = Read("src", "FreeX.App.Services", "WorkbookSession.cs");

        wpfGrid.Should().Contain("CellAnnouncementPlanner.BuildName(");
        avalonia.Should().Contain("CellAnnouncementPlanner.BuildName(");
        wpfGrid.Should().NotContain("record struct CellAnnouncementMetadata");
        avalonia.Should().NotContain("FormatCellAccessibleName");
        wpfComments.Should().Contain("ThreadedCommentDialogPlanner.DescribeReply(");
        wpfComments.Should().Contain("ThreadedCommentTimestampProfile.InlineRelativeLocal");
        wpfComments.Should().NotContain("private static string SummarizeReplyText");
        wpfFill.Should().Contain("WorksheetCommandPresentationCatalog.DescribeFill(direction).CommandTitle");
        session.Should().Contain("WorksheetCommandPresentationCatalog.DescribeFill(direction).CommandTitle");
        session.Should().NotContain("GetFillCellsTitle");
        avalonia.Should().Contain("WorksheetCommandPresentationCatalog.FormatFillStatus(direction, rangeReference)");
        avalonia.Should().NotContain("FormatFillCellsAction");
    }

    [Fact]
    public void RendererGeometryTimelineAndKeyTips_AreThinRealizers()
    {
        var wpfGlyph = Read("src", "FreeX.App.UI", "ConditionalIconGlyphRenderer.cs");
        var avaloniaGlyph = Read("src", "FreeX.App.Avalonia", "ConditionalFormatIconGlyphFactory.cs");
        var wpfTimeline = Read("src", "FreeX.App.UI", "GridView.DrawingObjects.cs");
        var avaloniaTimeline = Read("src", "FreeX.App.Avalonia", "MainWindow.SlicerTimeline.cs");
        var wpfKeyTips = Read("src", "FreeX.App.Host", "MenuKeyTipAssigner.cs");
        var avaloniaKeyTips = Read("src", "FreeX.App.Avalonia", "AvaloniaPivotChartContextMenus.cs");

        wpfGlyph.Should().Contain("ConditionalIconGlyphGeometry.PlanStarFill(op)");
        avaloniaGlyph.Should().Contain("ConditionalIconGlyphGeometry.PlanStarFill(op)");
        wpfGlyph.Should().NotContain("double.MaxValue");
        avaloniaGlyph.Should().NotContain("double.MaxValue");
        wpfTimeline.Should().Contain("layout.GranularityLabel");
        wpfTimeline.Should().Contain("layout.ClearFilterGlyph");
        avaloniaTimeline.Should().Contain("layout.GranularityLabel");
        avaloniaTimeline.Should().Contain("layout.ClearFilterGlyph");
        wpfKeyTips.Should().Contain("MenuKeyTipAssignmentPlanner.AssignUnique(");
        avaloniaKeyTips.Should().Contain("MenuKeyTipAssignmentPlanner.AssignUnique(");
        wpfKeyTips.Should().NotContain("PreserveExistingKeyTip");
        avaloniaKeyTips.Should().NotContain("RibbonKeyTipText.CreateUniqueKeyTip");
    }

    [Fact]
    public void SynchronousPromptsAndMergeWarnings_HavePortableOwners()
    {
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var wpfEditing = Read("src", "FreeX.App.Host", "MainWindow.Editing.cs");
        var wpfBackstage = Read("src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var wpfUpdate = Read("src", "FreeX.App.Host", "MainWindow.Update.cs");
        var wpfFormatting = Read("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs");
        var synchronousHost = Read(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaSynchronousDialogHost.cs");
        var pairedMergeRenderers = wpfFormatting + Environment.NewLine + avalonia;

        avalonia.Should().Contain("AvaloniaSynchronousUserMessageDialog.ShowMessage(");
        avalonia.Should().Contain("FreeXSynchronousPromptCatalog.ForDataValidation(");
        avalonia.Should().Contain("FreeXSynchronousPromptCatalog.ForReadOnlyRecommended(");
        avalonia.Should().Contain("FreeXSynchronousPromptCatalog.ForExternallyModifiedFile(");
        avalonia.Should().Contain("FreeXSynchronousPromptCatalog.ForLossyFormatFeatureLoss(");
        avalonia.Should().Contain("FreeXSynchronousPromptCatalog.ForUpdateReady(");
        avalonia.Should().Contain("AvaloniaSynchronousDialogHost.Show(this, dialog, () => done);");
        avalonia.Should().NotContain("Dispatcher.UIThread.RunJobs(DispatcherPriority.Input)");
        avalonia.Should().NotContain("private UserMessageResult ShowDataValidationPromptDialog");
        synchronousHost.Should().Contain("Dispatcher.UIThread.RunJobs(DispatcherPriority.Input)");

        wpfEditing.Should().Contain("FreeXSynchronousPromptCatalog.ForDataValidation(");
        wpfBackstage.Should().Contain("FreeXSynchronousPromptCatalog.ForReadOnlyRecommended(");
        wpfBackstage.Should().Contain("FreeXSynchronousPromptCatalog.ForExternallyModifiedFile(");
        wpfBackstage.Should().Contain("FreeXSynchronousPromptCatalog.ForLossyFormatFeatureLoss(");
        wpfUpdate.Should().Contain("FreeXSynchronousPromptCatalog.ForUpdateReady(");
        (avalonia + wpfUpdate).Should().NotContain("MainWindowMessage_UpdateReadyToInstallFormat");
        (avalonia + wpfUpdate).Should().NotContain("MainLoc_RestartingToInstall");

        wpfFormatting.Should().Contain("MergeCellsContentWarningPlanner.Create(");
        avalonia.Should().Contain("MergeCellsContentWarningPlanner.Create(");
        pairedMergeRenderers.Should().NotContain("\"Merging cells can discard cell contents.\"");
        pairedMergeRenderers.Should().NotContain("\"Keep only first cell\"");
        pairedMergeRenderers.Should().NotContain("FreeXAutomationIdCatalog.MergeCellsContentWarningDialog");
    }

    [Fact]
    public void AvaloniaBorderDrawing_UsesSharedModesAndSessionMutation()
    {
        var avaloniaCommands = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var avaloniaInteraction = Read("src", "FreeX.App.Avalonia", "MainWindow.DrawBorder.cs");
        var avaloniaPicker = Read("src", "FreeX.App.Avalonia", "MainWindow.HomeBorders.cs");
        var wpfInteraction = Read("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs");
        var workbookSession = Read("src", "FreeX.App.Services", "WorkbookSession.cs");
        var pickerSession = Read("src", "FreeX.App.Services", "BorderPickerSession.cs");

        avaloniaCommands.Should().Contain("BeginBorderDrawMode(BorderDrawMode.Draw)");
        avaloniaCommands.Should().Contain("BeginBorderDrawMode(BorderDrawMode.DrawGrid)");
        avaloniaCommands.Should().Contain("BeginBorderDrawMode(BorderDrawMode.Erase)");
        avaloniaCommands.Should().NotContain(
            "[\"Draw Border Grid\"] = () => ApplySelectedRangeBorderPreset");
        avaloniaCommands.Should().NotContain(
            "[\"Erase Border\"] = () => ApplySelectedRangeBorderPreset");
        avaloniaPicker.Should().Contain("_borderPickerSession.SetColor(_session.Workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent1))");
        avaloniaPicker.Should().Contain("_borderPickerSession.SetStyle(BorderStyle.Double)");

        avaloniaInteraction.Should().Contain("_borderPickerSession.TryConsumeDrawPlan(out var plan)");
        avaloniaInteraction.Should().Contain(
            "_session.SetSelectedRangeDrawBorder(plan.Mode, plan.Style, plan.Color)");
        wpfInteraction.Should().Contain("_borderPickerSession.TryConsumeDrawPlan(out var plan)");
        workbookSession.Should().Contain("BorderDrawPlanner.CreateCellDiff(mode, range, address, borderStyle, color)");
        pickerSession.Should().Contain("public sealed class BorderPickerSession");
        pickerSession.Should().Contain("plan = new BorderDrawExecutionPlan(DrawMode, Style, Color)");
    }

    [Fact]
    public void GroupedSheetStructurePolicy_IsOwnedByWorkbookSession()
    {
        var session = Read("src", "FreeX.App.Services", "WorkbookSession.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var avaloniaMoveCopy = Read("src", "FreeX.App.Avalonia", "MainWindow.MoveCopySheet.cs");
        var wpf = Read("src", "FreeX.App.Host", "MainWindow.SheetTabs.cs");

        session.Should().Contain("public WorkbookCellEditResult DuplicateSelectedSheets()");
        session.Should().Contain("public WorkbookCellEditResult MoveOrCopySelectedSheets(");
        session.Should().Contain("public WorkbookCellEditResult DeleteSelectedSheets()");
        session.Should().Contain("public WorkbookCellEditResult HideSelectedSheets()");
        session.Should().Contain("public WorkbookCellEditResult SetSelectedSheetTabColor(");

        avalonia.Should().Contain("_session.DuplicateSelectedSheets()");
        avaloniaMoveCopy.Should().Contain("_session.MoveOrCopySelectedSheets(");
        avalonia.Should().Contain("_session.DeleteActiveSheet()");
        avalonia.Should().Contain("_session.HideActiveSheet()");
        avalonia.Should().Contain("_session.SetActiveSheetTabColor(");
        wpf.Should().Contain("_session.DuplicateSelectedSheets(tab.Id)");
        wpf.Should().Contain("_session.MoveOrCopySelectedSheets(");
        wpf.Should().Contain("_session.DeleteSelectedSheets()");
        wpf.Should().Contain("_session.HideSelectedSheets()");
        wpf.Should().Contain("_session.SetSelectedSheetTabColor(");

        wpf.Should().NotContain("new DuplicateSheetCommand(tab.Id)");
        wpf.Should().NotContain("new CompositeWorkbookCommand(\"Delete Sheet\"");
        wpf.Should().NotContain("new CompositeWorkbookCommand(\"Hide Sheet\"");
        wpf.Should().NotContain("new CompositeWorkbookCommand(\"Tab Color\"");
    }

    private static string Read(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
