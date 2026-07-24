using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class StatusBarRefreshPlannerTests
{
    private sealed class TestTextProvider : IStatusBarTextProvider
    {
        public string GetReadyText() => "Ready";

        public string GetReadoutFormat(StatusBarReadoutKind kind) => "{0}";

        public string GetReadoutLabel(StatusBarReadoutKind kind) => kind.ToString();
    }

    private static readonly TestTextProvider TextProvider = new();

    [Fact]
    public void Build_WhenProgressVisible_HidesReadoutsButCarriesViewMode()
    {
        var sheet = CreateSheet();
        sheet.ViewMode = WorksheetViewMode.PageLayout;

        var plan = StatusBarRefreshPlanner.Build(
            sheet,
            selectedRange: null,
            selectionStats: null,
            isFileOperationProgressVisible: true,
            zoomPercent: 125,
            TextProvider);

        plan.Action.Should().Be(StatusBarRefreshAction.HideReadouts);
        plan.ViewMode.Should().Be(StatusBarViewMode.PageLayout);
        plan.ZoomPercent.Should().Be(125);
    }

    [Fact]
    public void Build_WithoutSelection_UsesReadyText()
    {
        var plan = StatusBarRefreshPlanner.Build(
            sheet: null,
            selectedRange: null,
            selectionStats: null,
            isFileOperationProgressVisible: false,
            zoomPercent: 100,
            TextProvider);

        plan.Action.Should().Be(StatusBarRefreshAction.Ready);
        plan.ViewMode.Should().Be(StatusBarViewMode.Normal);
        plan.ReadyText.Should().Be("Ready");
    }

    [Fact]
    public void Build_EmptySelectionStats_UsesActiveCellInputPrompt()
    {
        var sheet = CreateSheet();
        var cell = new CellAddress(sheet.Id, 2, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(cell, cell),
            ShowInputMessage = true,
            PromptTitle = "Input",
            PromptMessage = "Use a whole number"
        });

        var plan = StatusBarRefreshPlanner.Build(
            sheet,
            new GridRange(cell, cell),
            new WorkbookSelectionStats(0, 0, 0, null, null, null),
            isFileOperationProgressVisible: false,
            zoomPercent: 100,
            TextProvider);

        plan.Action.Should().Be(StatusBarRefreshAction.Ready);
        plan.ReadyText.Should().Be("Input: Use a whole number");
    }

    [Fact]
    public void Build_NonEmptyStats_RequestsStatsModel()
    {
        var sheet = CreateSheet();
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;
        var cell = new CellAddress(sheet.Id, 1, 1);
        var stats = new WorkbookSelectionStats(12, 3, 3, 4, 2, 6);

        var plan = StatusBarRefreshPlanner.Build(
            sheet,
            new GridRange(cell, cell),
            stats,
            isFileOperationProgressVisible: false,
            zoomPercent: 90,
            TextProvider);

        plan.Action.Should().Be(StatusBarRefreshAction.Stats);
        plan.ViewMode.Should().Be(StatusBarViewMode.PageBreak);
        plan.ZoomPercent.Should().Be(90);
        plan.Stats.Should().Be(stats);
        plan.ReadyText.Should().BeEmpty();
    }

    [Fact]
    public void Build_ViewModeOverrideProvided_WinsOverSheetsViewMode()
    {
        // R83-app-view-modes-5-1: a window's OWN displayed view mode can differ from
        // sheet.ViewMode (Excel "New Window" keeps each window's view mode independent of a
        // sibling window's changes to the shared Sheet) -- the caller-supplied override must win.
        var sheet = CreateSheet();
        sheet.ViewMode = WorksheetViewMode.PageLayout;

        var plan = StatusBarRefreshPlanner.Build(
            sheet,
            selectedRange: null,
            selectionStats: null,
            isFileOperationProgressVisible: false,
            zoomPercent: 100,
            TextProvider,
            viewModeOverride: WorksheetViewMode.Normal);

        plan.ViewMode.Should().Be(StatusBarViewMode.Normal);
    }

    [Fact]
    public void Build_NoViewModeOverride_FallsBackToSheetsViewMode()
    {
        var sheet = CreateSheet();
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;

        var plan = StatusBarRefreshPlanner.Build(
            sheet,
            selectedRange: null,
            selectionStats: null,
            isFileOperationProgressVisible: false,
            zoomPercent: 100,
            TextProvider);

        plan.ViewMode.Should().Be(StatusBarViewMode.PageBreak);
    }

    private static Sheet CreateSheet()
    {
        var workbook = new Workbook("Book1");
        return workbook.AddSheet("Sheet1");
    }
}
