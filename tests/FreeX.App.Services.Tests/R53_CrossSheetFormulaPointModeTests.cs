using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class R53_CrossSheetFormulaPointModeTests
{
    [Fact]
    public void CrossSheetPointing_PreservesSourceForSelectionCommitAndCancel()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);
        var sourceSheet = session.ActiveSheet;
        var targetSheet = session.Workbook.AddSheet("Revenue Data");
        var source = new CellAddress(sourceSheet.Id, 1, 1);
        var pointed = new GridRange(
            new CellAddress(targetSheet.Id, 2, 2),
            new CellAddress(targetSheet.Id, 4, 3));

        session.SelectCell(source);
        session.BeginFormulaEdit(source);
        session.SelectSheetForFormulaEdit(targetSheet.Id).Should().BeTrue();
        session.SelectRangeForFormulaEdit(pointed, source);

        session.ActiveSheet.Should().BeSameAs(targetSheet);
        session.SelectedRange.Should().Be(pointed);
        session.ActiveCell.Should().Be(pointed.Start);
        session.FormulaEditAddress.Should().Be(source);

        session.CancelFormulaEdit();

        session.ActiveSheet.Should().BeSameAs(sourceSheet);
        session.ActiveCell.Should().Be(source);
        session.SelectedRange.Should().Be(new GridRange(source, source));
        session.FormulaEditAddress.Should().BeNull();
    }

    [Fact]
    public void CrossSheetCommit_WritesFormulaToSourceCellAndReturnsToSourceSheet()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);
        var sourceSheet = session.ActiveSheet;
        var targetSheet = session.Workbook.AddSheet("Revenue Data");
        var source = new CellAddress(sourceSheet.Id, 1, 1);
        var pointed = new GridRange(
            new CellAddress(targetSheet.Id, 2, 2),
            new CellAddress(targetSheet.Id, 4, 3));

        session.BeginFormulaEdit(source);
        session.SelectSheetForFormulaEdit(targetSheet.Id);
        session.SelectRangeForFormulaEdit(pointed, source);
        var result = session.CommitCellText("='Revenue Data'!B2:C4");

        result.Success.Should().BeTrue(result.ErrorMessage);
        var editedSourceCell = session.Workbook.GetSheet(source.Sheet)?.GetCell(source);
        editedSourceCell.Should().NotBeNull();
        editedSourceCell!.FormulaText.Should().Be("'Revenue Data'!B2:C4");
        session.ActiveSheet.Should().BeSameAs(sourceSheet);
        session.ActiveCell.Should().Be(source);
        session.FormulaEditAddress.Should().BeNull();
    }

    [Fact]
    public void CrossSheetPointing_RejectsMissingFormulaSourceSheet()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);
        var target = session.ActiveCell;
        var missingSource = new CellAddress(SheetId.New(), 1, 1);

        var action = () => session.SelectRangeForFormulaEdit(
            new GridRange(target, target),
            missingSource);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("formulaEditAddress");
    }

    [Fact]
    public void ModifierSheetTabPointing_PreservesFormulaSourceAndGroupedTabs()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);
        var sourceSheet = session.ActiveSheet;
        var targetSheet = session.Workbook.AddSheet("Revenue Data");
        var source = new CellAddress(sourceSheet.Id, 1, 1);

        session.BeginFormulaEdit(source);
        session.SelectSheetForFormulaEdit(targetSheet.Id, selectRange: false, toggle: true).Should().BeTrue();

        session.ActiveSheet.Should().BeSameAs(targetSheet);
        session.FormulaEditAddress.Should().Be(source);
        session.IsWorkbookGrouped.Should().BeTrue();
        session.IsSheetInActiveGroupSelection(sourceSheet.Id).Should().BeTrue();
        session.IsSheetInActiveGroupSelection(targetSheet.Id).Should().BeTrue();
    }
}
