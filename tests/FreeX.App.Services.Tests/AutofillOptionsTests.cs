using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>Excel's Auto Fill Options button follows a fill-handle drag until anything else is done.</summary>
public sealed class AutofillOptionsTests
{
    [Fact]
    public void AfterADrag_TheOptionsAreAvailable_AndFillSeriesRedoesTheSameDrag()
    {
        var (session, sheet) = CreateSession();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fill = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));
        session.AutofillOptions.Should().BeNull();

        session.AutofillDragRange(source, fill).Success.Should().BeTrue();
        sheet.GetValue(3, 1).Should().Be(new NumberValue(5), "a plain drag copies a lone number");
        session.AutofillOptions.Should().NotBeNull();
        var depth = session.UndoStackDepth;

        session.ApplyAutofillOption(AutofillMode.FillSeries).Success.Should().BeTrue();

        sheet.GetValue(2, 1).Should().Be(new NumberValue(6));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(7));
        session.UndoStackDepth.Should().Be(depth, "the option replaces the fill, it does not stack another");
        session.AutofillOptions!.Mode.Should().Be(AutofillMode.FillSeries);

        session.ApplyAutofillOption(AutofillMode.CopyCells).Success.Should().BeTrue();
        sheet.GetValue(3, 1).Should().Be(new NumberValue(5), "the choice can be switched again");
    }

    [Fact]
    public void AnyOtherEdit_RemovesTheOptions()
    {
        var (session, sheet) = CreateSession();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fill = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        session.AutofillDragRange(source, fill).Success.Should().BeTrue();
        session.AutofillOptions.Should().NotBeNull();

        session.SelectCell(new CellAddress(sheet.Id, 9, 9));
        session.CommitCellText("x").Success.Should().BeTrue();

        session.AutofillOptions.Should().BeNull();
        session.ApplyAutofillOption(AutofillMode.FillSeries).IsNoOp.Should().BeTrue("a stale button must not undo someone's later edit");
    }

    [Fact]
    public void DraggingBackInsideTheSource_OffersNoOptions()
    {
        var (session, sheet) = CreateSession();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var shrunk = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));

        session.AutofillDragRange(source, shrunk);

        session.AutofillOptions.Should().BeNull();
    }

    [Fact]
    public void Menu_FollowsExcelAndChecksTheCurrentChoice()
    {
        AutofillOptionsPlanner.Items(AutofillMode.FillSeries).Select(item => item.Header).Should().Equal(
            "Copy Cells", "Fill Series", "Fill Formatting Only", "Fill Without Formatting");
        AutofillOptionsPlanner.Items(AutofillMode.FillSeries).Single(item => item.IsChecked).Mode.Should().Be(AutofillMode.FillSeries);
        AutofillOptionsPlanner.Items(AutofillMode.Default).Should().OnlyContain(item => !item.IsChecked);
    }

    private static (WorkbookSession Session, Sheet Sheet) CreateSession()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var sheet = workbook.Sheets.Single();
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        session.SelectCell(new CellAddress(sheet.Id, 1, 1));
        return (session, sheet);
    }
}
