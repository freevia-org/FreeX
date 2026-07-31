using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class R52_FormulaPointModeSelectionTests
{
    [Fact]
    public void SelectRangeForFormulaEdit_PreservesSourceCellAndSelectsPointedRange()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);
        var sheet = session.ActiveSheet;
        var source = new CellAddress(sheet.Id, 1, 1);
        var pointed = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 3));

        session.SelectCell(source);
        session.BeginFormulaEdit(source);
        session.SelectRangeForFormulaEdit(pointed, source);

        session.SelectedRange.Should().Be(pointed);
        session.ActiveCell.Should().Be(pointed.Start);
        session.FormulaEditAddress.Should().Be(source);
    }

    [Fact]
    public void SelectRangeForFormulaEdit_PreservesDirectionalSelectionAnchor()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);
        var sheet = session.ActiveSheet;
        var source = new CellAddress(sheet.Id, 1, 1);
        var anchor = new CellAddress(sheet.Id, 5, 5);
        var cursor = new CellAddress(sheet.Id, 3, 3);
        var pointed = new GridRange(cursor, anchor);

        session.BeginFormulaEdit(source);
        session.SelectRangeForFormulaEdit(pointed, source, anchor);

        session.SelectedRange.Should().Be(pointed);
        session.ActiveCell.Should().Be(anchor,
            "formula point mode must retain the directional anchor, not normalize it to range.Start");
        session.FormulaEditAddress.Should().Be(source);
    }
}
