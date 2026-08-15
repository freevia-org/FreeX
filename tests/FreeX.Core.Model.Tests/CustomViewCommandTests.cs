using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class CustomViewCommandTests
{
    [Fact]
    public void SaveCustomViewCommand_CapturesWorkbookViewStateAndUndoRemoves()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;
        sheet.ShowGridlines = false;
        sheet.ShowHeadings = false;
        sheet.ShowRulers = false;
        sheet.ZoomPercent = 125;
        sheet.ShowFormulas = true;
        sheet.ActiveRow = 12;
        sheet.ActiveCol = 4;
        sheet.ViewTopRow = 10;
        sheet.ViewLeftCol = 3;
        sheet.FrozenRows = 1;
        sheet.SplitColumn = 3;
        workbook.ActiveSheetIndex = 0;
        var ctx = new TestCommandContext(workbook);

        var command = new SaveCustomViewCommand("Audit View");

        command.Apply(ctx).Success.Should().BeTrue();

        var view = workbook.CustomViews.Should().ContainSingle().Subject;
        view.Name.Should().Be("Audit View");
        view.ActiveSheetIndex.Should().Be(0);
        var state = view.Sheets.Should().ContainSingle().Subject;
        state.SplitColumn.Should().BeNull();
        state.ShowGridlines.Should().BeFalse();
        state.ShowHeadings.Should().BeFalse();
        state.ShowRulers.Should().BeFalse();
        state.ZoomPercent.Should().Be(125);
        state.ShowFormulas.Should().BeTrue();
        state.ActiveRow.Should().Be(12);
        state.ActiveCol.Should().Be(4);
        state.ViewTopRow.Should().Be(10);
        state.ViewLeftCol.Should().Be(3);
        state.SplitRow.Should().BeNull();
        state.SplitColumn.Should().BeNull();

        command.Revert(ctx);

        workbook.CustomViews.Should().BeEmpty();
    }

    [Fact]
    public void ApplyCustomViewCommand_RestoresActiveSheetSelectionScrollAndUndo()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        workbook.ActiveSheetIndex = 1;
        sheet2.ViewMode = WorksheetViewMode.PageLayout;
        sheet2.ActiveRow = 22;
        sheet2.ActiveCol = 6;
        sheet2.ViewTopRow = 18;
        sheet2.ViewLeftCol = 4;
        new SaveCustomViewCommand("Saved").Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        workbook.ActiveSheetIndex = 0;
        sheet1.ViewMode = WorksheetViewMode.PageBreakPreview;
        sheet2.ViewMode = WorksheetViewMode.Normal;
        sheet2.ActiveRow = 1;
        sheet2.ActiveCol = 1;
        sheet2.ViewTopRow = 1;
        sheet2.ViewLeftCol = 1;
        var command = new ApplyCustomViewCommand("Saved");

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        workbook.ActiveSheetIndex.Should().Be(1);
        sheet2.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        sheet2.ActiveRow.Should().Be(22);
        sheet2.ActiveCol.Should().Be(6);
        sheet2.ViewTopRow.Should().Be(18);
        sheet2.ViewLeftCol.Should().Be(4);

        command.Revert(new TestCommandContext(workbook));

        workbook.ActiveSheetIndex.Should().Be(0);
        sheet1.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
        sheet2.ViewMode.Should().Be(WorksheetViewMode.Normal);
        sheet2.ActiveRow.Should().Be(1);
        sheet2.ActiveCol.Should().Be(1);
        sheet2.ViewTopRow.Should().Be(1);
        sheet2.ViewLeftCol.Should().Be(1);
    }

    [Fact]
    public void SaveCustomViewCommand_CapturesExcelIncludeOptions()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var command = new SaveCustomViewCommand(
            "No Print",
            includePrintSettings: false,
            includeHiddenRowsColumnsAndFilterSettings: true);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var view = workbook.CustomViews.Should().ContainSingle().Subject;
        view.IncludePrintSettings.Should().BeFalse();
        view.IncludeHiddenRowsColumnsAndFilterSettings.Should().BeTrue();
    }

    [Fact]
    public void SaveCustomViewCommand_DropsSplitPaneStateWhenFrozenPanesArePresent()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 2;
        sheet.SplitRow = 4;
        sheet.SplitColumn = 5;
        var ctx = new TestCommandContext(workbook);

        var command = new SaveCustomViewCommand("Sanitized");

        command.Apply(ctx).Success.Should().BeTrue();

        var state = workbook.CustomViews.Should().ContainSingle().Subject.Sheets.Should().ContainSingle().Subject;
        state.FrozenRows.Should().Be(1);
        state.FrozenCols.Should().Be(2);
        state.SplitRow.Should().BeNull();
        state.SplitColumn.Should().BeNull();
    }

    [Fact]
    public void ApplyCustomViewCommand_AppliesSavedViewAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Saved",
            [new WorksheetCustomViewState(
                "Sheet1",
                WorksheetViewMode.PageLayout,
                2,
                0,
                null,
                4,
                ShowGridlines: false,
                ShowHeadings: false,
                ShowRulers: false,
                ZoomPercent: 150,
                ShowFormulas: true)]));
        var ctx = new TestCommandContext(workbook);
        sheet.ViewMode = WorksheetViewMode.Normal;
        sheet.ShowGridlines = true;
        sheet.ShowHeadings = true;
        sheet.ShowRulers = true;
        sheet.ZoomPercent = 100;
        sheet.ShowFormulas = false;
        sheet.FrozenRows = 0;
        sheet.SplitColumn = null;

        var command = new ApplyCustomViewCommand("Saved");

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        sheet.ShowGridlines.Should().BeFalse();
        sheet.ShowHeadings.Should().BeFalse();
        sheet.ShowRulers.Should().BeFalse();
        sheet.ZoomPercent.Should().Be(150);
        sheet.ShowFormulas.Should().BeTrue();
        sheet.FrozenRows.Should().Be(2);
        sheet.SplitColumn.Should().BeNull();

        command.Revert(ctx);

        sheet.ViewMode.Should().Be(WorksheetViewMode.Normal);
        sheet.ShowGridlines.Should().BeTrue();
        sheet.ShowHeadings.Should().BeTrue();
        sheet.ShowRulers.Should().BeTrue();
        sheet.ZoomPercent.Should().Be(100);
        sheet.ShowFormulas.Should().BeFalse();
        sheet.FrozenRows.Should().Be(0);
        sheet.SplitColumn.Should().BeNull();
    }

    [Fact]
    public void ApplyCustomViewCommand_CapturedNoFilterClearsLiveFilterAndUndoRestoresIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "No Filter",
            [new WorksheetCustomViewState(
                sheet.Name,
                WorksheetViewMode.Normal,
                0,
                0,
                null,
                null,
                HiddenRows: [],
                HiddenCols: [],
                FilterHiddenRows: [],
                AutoFilter: null)],
            IncludeHiddenRowsColumnsAndFilterSettings: true));
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B4", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["North"]));
        var command = new ApplyCustomViewCommand("No Filter");
        var context = new TestCommandContext(workbook);

        command.Apply(context).Success.Should().BeTrue();

        sheet.AutoFilter.Should().BeNull();

        command.Revert(context);

        sheet.AutoFilter.Should().NotBeNull();
        sheet.AutoFilter!.Reference.Should().Be("A1:B4");
        sheet.AutoFilter.FilterColumns.Should().ContainSingle().Which.Values.Should().Equal("North");
    }

    [Fact]
    public void ApplyCustomViewCommand_DropsSplitPaneStateWhenSavedViewHasFrozenPanes()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Saved",
            [new WorksheetCustomViewState(
                "Sheet1",
                WorksheetViewMode.Normal,
                FrozenRows: 2,
                FrozenCols: 1,
                SplitRow: 6,
                SplitColumn: 4)]));
        var ctx = new TestCommandContext(workbook);

        var command = new ApplyCustomViewCommand("Saved");

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FrozenRows.Should().Be(2);
        sheet.FrozenCols.Should().Be(1);
        sheet.SplitRow.Should().BeNull();
        sheet.SplitColumn.Should().BeNull();
    }

    [Fact]
    public void CustomViewStatePlanner_CapturesFindsSanitizesAndAppliesState()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 0;
        sheet.SplitRow = 7;
        sheet.SplitColumn = 8;
        sheet.ShowGridlines = false;
        sheet.ShowHeadings = false;
        sheet.ShowRulers = false;
        sheet.ZoomPercent = 140;
        sheet.ShowFormulas = true;
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Review",
            [new WorksheetCustomViewState("Sheet1", WorksheetViewMode.Normal, 0, 0, null, null)]));

        CustomViewStatePlanner.FindViewIndex(workbook, "review").Should().Be(0);
        CustomViewStatePlanner.FindViewIndex(workbook, "missing").Should().Be(-1);

        var state = CustomViewStatePlanner.CaptureSheetState(sheet);
        state.SplitRow.Should().BeNull();
        state.SplitColumn.Should().BeNull();
        state.ShowGridlines.Should().BeFalse();
        state.ShowHeadings.Should().BeFalse();
        state.ShowRulers.Should().BeFalse();
        state.ZoomPercent.Should().Be(140);
        state.ShowFormulas.Should().BeTrue();

        CustomViewStatePlanner.CaptureWorkbookState(workbook).Should().ContainSingle().Which.Should().Be(state);

        CustomViewStatePlanner.ApplyState(sheet, new WorksheetCustomViewState(
            "Sheet1",
            WorksheetViewMode.PageLayout,
            FrozenRows: 2,
            FrozenCols: 1,
            SplitRow: 3,
            SplitColumn: 4,
            ShowGridlines: true,
            ShowHeadings: true,
            ShowRulers: true,
            ZoomPercent: 90,
            ShowFormulas: false));

        sheet.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        sheet.FrozenRows.Should().Be(2);
        sheet.FrozenCols.Should().Be(1);
        sheet.SplitRow.Should().BeNull();
        sheet.SplitColumn.Should().BeNull();
        sheet.ShowGridlines.Should().BeTrue();
        sheet.ShowHeadings.Should().BeTrue();
        sheet.ShowRulers.Should().BeTrue();
        sheet.ZoomPercent.Should().Be(90);
        sheet.ShowFormulas.Should().BeFalse();
    }

    [Fact]
    public void CustomViewCommands_DelegateStatePlanning()
    {
        // N13/N14: CustomViewCommands.CaptureWorkbookState is no longer a bare pass-through to
        // CustomViewStatePlanner.CaptureWorkbookState(workbook) — it now takes the
        // includePrintSettings / includeHiddenRowsColumnsAndFilterSettings flags and augments each
        // sheet's base-captured state (via CustomViewStatePlanner.CaptureSheetState(sheet) per
        // sheet) with the extra hidden-rows/cols/filter and print-setting fields those flags gate.
        // The remaining delegation points to the planner are unchanged.
        var source = ModelSourceTestSupport.ReadCommandsSource("CustomViewCommands.cs");

        source.Should().Contain("CustomViewStatePlanner.FindViewIndex(workbook, name)");
        source.Should().Contain("CustomViewStatePlanner.CaptureSheetState(sheet)");
        source.Should().Contain("CustomViewStatePlanner.CaptureActiveSheetIndex(workbook)");
        source.Should().Contain("CustomViewStatePlanner.SanitizePaneState(state)");
        source.Should().Contain("CustomViewStatePlanner.ApplyState(sheet, state)");
    }

    [Fact]
    public void DeleteCustomViewCommand_RemovesViewAndUndoRestores()
    {
        var workbook = new Workbook("test");
        workbook.AddSheet("Sheet1");
        var view = new WorkbookCustomView(
            "Review",
            [new WorksheetCustomViewState("Sheet1", WorksheetViewMode.Normal, 0, 0, null, null)]);
        workbook.CustomViews.Add(view);
        var ctx = new TestCommandContext(workbook);

        var command = new DeleteCustomViewCommand("Review");

        command.Apply(ctx).Success.Should().BeTrue();
        workbook.CustomViews.Should().BeEmpty();

        command.Revert(ctx);

        workbook.CustomViews.Should().ContainSingle().Which.Should().Be(view);
    }
}
