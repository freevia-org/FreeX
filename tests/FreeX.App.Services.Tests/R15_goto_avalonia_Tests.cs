using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R15-name-manager-goto-1: a single-cell selection must expand the Go To Special SEARCH range to
/// the sheet's used range (mirroring the WPF host's ResolveGoToSpecialSearchRange), rather than
/// only searching the 1x1 selected cell.
/// </summary>
public sealed class R15_goto_avalonia_Tests
{
    [Fact]
    public void GoToSpecial_Constants_SingleCellSelectionSearchesWholeUsedRange()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();

        // Populate constants across A1:D20.
        var expected = new List<CellAddress>();
        for (uint row = 1; row <= 20; row++)
        {
            for (uint col = 1; col <= 4; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                sheet.SetCell(address, new NumberValue(row * 10 + col));
                expected.Add(address);
            }
        }

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        // Only A1 is selected (a single active cell), not the whole A1:D20 range.
        var a1 = new CellAddress(sheet.Id, 1, 1);
        session.SelectRange(new GridRange(a1, a1));

        var result = session.GoToSpecial(GoToSpecialKind.Constants);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(expected.Count);
        result.MatchCount.Should().BeGreaterThanOrEqualTo(80);
    }

    [Fact]
    public void GoToSpecial_Blanks_SingleCellSelectionSearchesUsedRangeInsteadOfReportingNoCellsFound()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);

        // A1 has a value (so the selected 1x1 cell itself is not blank); B1 is left blank inside
        // the used range and C1 has a value so the used range spans A1:C1.
        sheet.SetCell(a1, new TextValue("filled"));
        sheet.SetCell(c1, new NumberValue(10));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        // Select only the non-blank single cell A1 -- the search must still expand to the used
        // range and find B1, rather than immediately reporting "No cells found."
        session.SelectRange(new GridRange(a1, a1));

        var result = session.GoToSpecial(GoToSpecialKind.Blanks);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.MatchCount.Should().Be(1);
        result.SelectedRange.Should().Be(new GridRange(b1, b1));
        session.SelectedRange.Should().Be(new GridRange(b1, b1));
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
