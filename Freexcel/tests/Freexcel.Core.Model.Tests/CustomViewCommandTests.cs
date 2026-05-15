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
        sheet.FrozenRows = 1;
        sheet.SplitColumn = 3;
        var ctx = new SimpleCtx(workbook);

        var command = new SaveCustomViewCommand("Audit View");

        command.Apply(ctx).Success.Should().BeTrue();

        var view = workbook.CustomViews.Should().ContainSingle().Subject;
        view.Name.Should().Be("Audit View");
        view.Sheets.Should().ContainSingle().Which.SplitColumn.Should().Be(3);

        command.Revert(ctx);

        workbook.CustomViews.Should().BeEmpty();
    }

    [Fact]
    public void ApplyCustomViewCommand_AppliesSavedViewAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Saved",
            [new WorksheetCustomViewState("Sheet1", WorksheetViewMode.PageLayout, 2, 0, null, 4)]));
        var ctx = new SimpleCtx(workbook);
        sheet.ViewMode = WorksheetViewMode.Normal;
        sheet.FrozenRows = 0;
        sheet.SplitColumn = null;

        var command = new ApplyCustomViewCommand("Saved");

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        sheet.FrozenRows.Should().Be(2);
        sheet.SplitColumn.Should().Be(4);

        command.Revert(ctx);

        sheet.ViewMode.Should().Be(WorksheetViewMode.Normal);
        sheet.FrozenRows.Should().Be(0);
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
