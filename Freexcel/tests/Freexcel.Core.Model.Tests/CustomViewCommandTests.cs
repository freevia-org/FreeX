using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

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
        sheet.FrozenRows = 1;
        sheet.SplitColumn = 3;
        var ctx = new SimpleCtx(workbook);

        var command = new SaveCustomViewCommand("Audit View");

        command.Apply(ctx).Success.Should().BeTrue();

        var view = workbook.CustomViews.Should().ContainSingle().Subject;
        view.Name.Should().Be("Audit View");
        var state = view.Sheets.Should().ContainSingle().Subject;
        state.SplitColumn.Should().BeNull();
        state.ShowGridlines.Should().BeFalse();
        state.ShowHeadings.Should().BeFalse();
        state.ShowRulers.Should().BeFalse();
        state.ZoomPercent.Should().Be(125);
        state.ShowFormulas.Should().BeTrue();
        state.SplitRow.Should().BeNull();
        state.SplitColumn.Should().BeNull();

        command.Revert(ctx);

        workbook.CustomViews.Should().BeEmpty();
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

        command.Apply(new SimpleCtx(workbook)).Success.Should().BeTrue();

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
        var ctx = new SimpleCtx(workbook);

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
        var ctx = new SimpleCtx(workbook);
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
        var ctx = new SimpleCtx(workbook);

        var command = new ApplyCustomViewCommand("Saved");

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FrozenRows.Should().Be(2);
        sheet.FrozenCols.Should().Be(1);
        sheet.SplitRow.Should().BeNull();
        sheet.SplitColumn.Should().BeNull();
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
        var ctx = new SimpleCtx(workbook);

        var command = new DeleteCustomViewCommand("Review");

        command.Apply(ctx).Success.Should().BeTrue();
        workbook.CustomViews.Should().BeEmpty();

        command.Revert(ctx);

        workbook.CustomViews.Should().ContainSingle().Which.Should().Be(view);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
