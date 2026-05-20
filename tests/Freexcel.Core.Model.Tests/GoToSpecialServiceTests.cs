using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class GoToSpecialServiceTests
{
    [Fact]
    public void FindConstants_ReturnsNonFormulaNonBlankCellsInRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("constant"));
        sheet.SetCell(b1, Cell.FromFormula("1+1"));

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.Constants);

        result.Should().Equal(a1);
    }

    [Fact]
    public void FindBlanks_ReturnsBlankAddressesInRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.Blanks);

        result.Should().Equal(new CellAddress(sheet.Id, 1, 2));
    }

    [Fact]
    public void FindCommentsAndValidations_ReturnsMatchingCells()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var commentCell = new CellAddress(sheet.Id, 2, 1);
        var threadedCommentCell = new CellAddress(sheet.Id, 3, 1);
        var validationCell = new CellAddress(sheet.Id, 4, 1);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.Comments[commentCell] = "note";
        sheet.ThreadedComments[threadedCommentCell] = new ThreadedComment("discussion");
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(validationCell, validationCell),
            Type = DvType.WholeNumber
        });

        GoToSpecialService.Find(sheet, range, GoToSpecialKind.Comments).Should().Equal(commentCell, threadedCommentCell);
        GoToSpecialService.Find(sheet, range, GoToSpecialKind.DataValidation).Should().Equal(validationCell);
    }

    [Fact]
    public void FindVisibleCells_SkipsHiddenRowsAndColumns()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.HiddenRows.Add(2);
        sheet.HiddenCols.Add(2);

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.VisibleCellsOnly);

        result.Should().Equal(new CellAddress(sheet.Id, 1, 1));
    }
}
