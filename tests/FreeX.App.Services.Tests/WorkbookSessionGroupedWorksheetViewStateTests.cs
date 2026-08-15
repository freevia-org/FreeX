using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionGroupedWorksheetViewStateTests
{
    [Fact]
    public void ViewModeAndZoom_ApplyToGroupedSheetsAsSingleUndoableOperations()
    {
        using var session = CreateGroupedSession(out var first, out var second);

        session.SetWorksheetViewMode(WorksheetViewMode.PageLayout).Success.Should().BeTrue();
        first.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        second.ViewMode.Should().Be(WorksheetViewMode.PageLayout);

        session.UndoLastEdit().Success.Should().BeTrue();
        first.ViewMode.Should().Be(WorksheetViewMode.Normal);
        second.ViewMode.Should().Be(WorksheetViewMode.Normal);

        session.RedoLastEdit().Success.Should().BeTrue();
        first.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        second.ViewMode.Should().Be(WorksheetViewMode.PageLayout);

        session.SetZoomPercent(175).Success.Should().BeTrue();
        first.ZoomPercent.Should().Be(175);
        second.ZoomPercent.Should().Be(175);

        session.UndoLastEdit().Success.Should().BeTrue();
        first.ZoomPercent.Should().Be(100);
        second.ZoomPercent.Should().Be(100);
    }

    [Fact]
    public void DisplayToggles_ApplyToGroupedSheetsAndPreserveEachSheetsOtherOptions()
    {
        using var session = CreateGroupedSession(out var first, out var second);
        first.ShowHeadings = false;
        first.ShowRulers = true;
        second.ShowHeadings = true;
        second.ShowRulers = false;
        session.SynchronizeWorksheetViewState(new Dictionary<SheetId, WorksheetViewStateSnapshot>
        {
            [first.Id] = Snapshot(first),
            [second.Id] = Snapshot(second),
        });

        session.SetShowGridlines(false).Success.Should().BeTrue();

        first.ShowGridlines.Should().BeFalse();
        second.ShowGridlines.Should().BeFalse();
        first.ShowHeadings.Should().BeFalse();
        second.ShowHeadings.Should().BeTrue();
        first.ShowRulers.Should().BeTrue();
        second.ShowRulers.Should().BeFalse();

        session.SetShowHeadings(false).Success.Should().BeTrue();
        first.ShowHeadings.Should().BeFalse();
        second.ShowHeadings.Should().BeFalse();
        first.ShowGridlines.Should().BeFalse();
        second.ShowGridlines.Should().BeFalse();
        first.ShowRulers.Should().BeTrue();
        second.ShowRulers.Should().BeFalse();

        session.SetShowRulers(true).Success.Should().BeTrue();
        first.ShowRulers.Should().BeTrue();
        second.ShowRulers.Should().BeTrue();
        first.ShowGridlines.Should().BeFalse();
        second.ShowGridlines.Should().BeFalse();
    }

    [Fact]
    public void ShowFormulas_AppliesToGroupedSheetsAndUndoInvalidatesEveryTargetCache()
    {
        using var session = CreateGroupedSession(out var first, out var second);

        session.SetShowFormulas(true).Success.Should().BeTrue();
        first.ShowFormulas.Should().BeTrue();
        second.ShowFormulas.Should().BeTrue();

        session.UndoLastEdit().Success.Should().BeTrue();
        first.ShowFormulas.Should().BeFalse();
        second.ShowFormulas.Should().BeFalse();

        session.SelectSheet(second.Id).Should().BeTrue();
        session.IsShowingFormulas.Should().BeFalse(
            "undo must invalidate the cached value for non-active grouped targets too");
    }

    [Fact]
    public void ActiveSheetAlreadyAtTarget_DoesNotHideAGroupedSiblingDifference()
    {
        using var session = CreateGroupedSession(out var first, out var second);
        second.ViewMode = WorksheetViewMode.PageBreakPreview;
        second.ZoomPercent = 150;
        second.ShowGridlines = false;
        second.ShowHeadings = false;
        second.ShowRulers = false;
        second.ShowFormulas = true;

        session.SetWorksheetViewMode(first.ViewMode).IsNoOp.Should().BeFalse();
        session.SetZoomPercent(first.ZoomPercent).IsNoOp.Should().BeFalse();
        session.SetShowGridlines(first.ShowGridlines).IsNoOp.Should().BeFalse();
        session.SetShowHeadings(first.ShowHeadings).IsNoOp.Should().BeFalse();
        session.SetShowRulers(first.ShowRulers).IsNoOp.Should().BeFalse();
        session.SetShowFormulas(first.ShowFormulas).IsNoOp.Should().BeFalse();

        second.ViewMode.Should().Be(first.ViewMode);
        second.ZoomPercent.Should().Be(first.ZoomPercent);
        second.ShowGridlines.Should().Be(first.ShowGridlines);
        second.ShowHeadings.Should().Be(first.ShowHeadings);
        second.ShowRulers.Should().Be(first.ShowRulers);
        second.ShowFormulas.Should().Be(first.ShowFormulas);
    }

    [Fact]
    public void GroupedViewStateChange_DoesNotLeakIntoSiblingWindowProjection()
    {
        using var session = CreateGroupedSession(out var first, out var second);
        using var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);
        sibling.SelectSheet(second.Id).Should().BeTrue();
        sibling.ViewMode.Should().Be(WorksheetViewMode.Normal);
        sibling.ZoomPercent.Should().Be(100);

        session.SetWorksheetViewMode(WorksheetViewMode.PageLayout).Success.Should().BeTrue();
        session.SetZoomPercent(175).Success.Should().BeTrue();

        first.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        second.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        sibling.ViewMode.Should().Be(WorksheetViewMode.Normal);
        sibling.ZoomPercent.Should().Be(100);
    }

    [Fact]
    public void RendererSnapshotSynchronization_PreservesPerWindowCompanionOptions()
    {
        using var session = CreateGroupedSession(out var first, out var second);
        first.ShowHeadings = true;
        first.ShowRulers = true;
        second.ShowHeadings = false;
        second.ShowRulers = false;
        session.SynchronizeWorksheetViewState(new Dictionary<SheetId, WorksheetViewStateSnapshot>
        {
            [first.Id] = new(WorksheetViewMode.Normal, 100, true, false, false),
            [second.Id] = new(WorksheetViewMode.Normal, 100, true, true, true),
        });

        session.SetShowGridlines(false).Success.Should().BeTrue();

        first.ShowGridlines.Should().BeFalse();
        second.ShowGridlines.Should().BeFalse();
        first.ShowHeadings.Should().BeFalse();
        first.ShowRulers.Should().BeFalse();
        second.ShowHeadings.Should().BeTrue();
        second.ShowRulers.Should().BeTrue();
    }

    [Fact]
    public void BothRenderersRouteWorksheetViewMutationsThroughWorkbookSession()
    {
        var wpfView = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));
        var wpfFormula = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.FormulaCommands.cs"));
        var avalonia = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var avaloniaView = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Avalonia", "MainWindow.ViewToggles.cs"));
        var avaloniaRibbonWires = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuWires.cs"));

        wpfView.Should().Contain("_session.SetShowGridlines(")
            .And.Contain("_session.SetShowHeadings(")
            .And.Contain("_session.SetShowRulers(")
            .And.Contain("_session.SetWorksheetViewMode(")
            .And.Contain("_session.SetZoomPercent(");
        wpfView.Should().NotContain("new SetWorksheetViewOptionsCommand(")
            .And.NotContain("new SetWorksheetViewModeCommand(")
            .And.NotContain("new SetWorksheetZoomCommand(");
        wpfFormula.Should().Contain("_session.SetShowFormulas(")
            .And.NotContain("new SetWorksheetShowFormulasCommand(");

        avalonia.Should().Contain("_session.SetShowGridlines(")
            .And.Contain("_session.SetShowHeadings(")
            .And.Contain("_session.SetZoomPercent(")
            .And.Contain("_session.SetShowFormulas(");
        avaloniaView.Should().Contain("_session.SetWorksheetViewMode(");
        avaloniaRibbonWires.Should().Contain("_session.SetShowRulers(");
    }

    private static WorkbookSession CreateGroupedSession(out Sheet first, out Sheet second)
    {
        var session = new WorkbookSessionFactory().CreateNew(30, 20);
        first = session.ActiveSheet;
        second = session.Workbook.AddSheet("Second");
        session.SelectSheet(first.Id);
        session.SelectAllVisibleSheets().Should().BeTrue();
        session.GetCurrentGroupedEditSheetIds().Should().Equal(first.Id, second.Id);
        return session;
    }

    private static WorksheetViewStateSnapshot Snapshot(Sheet sheet) =>
        new(
            sheet.ViewMode,
            sheet.ZoomPercent,
            sheet.ShowGridlines,
            sheet.ShowHeadings,
            sheet.ShowRulers,
            sheet.FrozenRows,
            sheet.FrozenCols,
            sheet.SplitRow,
            sheet.SplitColumn,
            sheet.ShowFormulas);
}
