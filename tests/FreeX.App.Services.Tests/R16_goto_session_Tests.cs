using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R16-meta-2: ResolveGoToSpecialSearchRange expands a single-cell selection to the sheet's used
/// range for content kinds (Constants/Blanks/Formulas/etc.), but Precedents/Dependents (and
/// CurrentRegion) must trace relationships from the user's TRUE active cell/selection instead --
/// otherwise a single-cell selection whose cell references one precedent leaks every precedent
/// (or dependent) formula found anywhere in the used range. Mirrors the WPF host's
/// SelectGoToSpecialMatches guard in MainWindow.HomeEditing.cs.
/// </summary>
public sealed class R16_goto_session_Tests
{
    [Fact]
    public void GoToSpecial_Precedents_SingleCellSelection_SelectsOnlyActiveCellsPrecedent()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var d10 = new CellAddress(sheet.Id, 10, 4);
        var c10 = new CellAddress(sheet.Id, 10, 3);
        var z100 = new CellAddress(sheet.Id, 100, 26);

        // A1 is the true precedent of the selected cell B2.
        sheet.SetCell(a1, new NumberValue(1));
        // Decoy formula elsewhere in the used range: if Precedents wrongly traces the whole used
        // range (A1:Z100) instead of just the selected cell B2, D10 (its precedent) leaks in too.
        sheet.SetCell(d10, new NumberValue(2));
        sheet.SetFormula(c10, "D10+1");
        sheet.SetFormula(b2, "A1");
        // Forces the sheet's used range to span A1:Z100.
        sheet.SetCell(z100, new NumberValue(3));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        // Only B2 is selected (a single active cell), not the whole A1:Z100 used range.
        session.SelectRange(new GridRange(b2, b2));

        var result = session.GoToSpecial(GoToSpecialKind.Precedents);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(1);
        result.SelectedRange.Should().Be(new GridRange(a1, a1));
        session.SelectedRange.Should().Be(new GridRange(a1, a1));
    }

    [Fact]
    public void GoToSpecial_Dependents_SingleCellSelection_SelectsOnlyActiveCellsDependents()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b3 = new CellAddress(sheet.Id, 3, 2);
        var d10 = new CellAddress(sheet.Id, 10, 4);
        var c10 = new CellAddress(sheet.Id, 10, 3);
        var z100 = new CellAddress(sheet.Id, 100, 26);

        // B3 is the true dependent of the selected cell A1.
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b3, "A1+1");
        // Decoy formula elsewhere in the used range: if Dependents wrongly traces the whole used
        // range (A1:Z100) instead of just the selected cell A1, C10 (which depends on the
        // unrelated D10) leaks into the result too.
        sheet.SetCell(d10, new NumberValue(2));
        sheet.SetFormula(c10, "D10+1");
        // Forces the sheet's used range to span A1:Z100.
        sheet.SetCell(z100, new NumberValue(3));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        // Only A1 is selected (a single active cell), not the whole A1:Z100 used range.
        session.SelectRange(new GridRange(a1, a1));

        var result = session.GoToSpecial(GoToSpecialKind.Dependents);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(1);
        result.SelectedRange.Should().Be(new GridRange(b3, b3));
        session.SelectedRange.Should().Be(new GridRange(b3, b3));
    }

    [Fact]
    public void GoToSpecial_Constants_SingleCellSelection_StillSearchesWholeUsedRange()
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

        // Only A1 is selected (a single active cell), not the whole A1:D20 range -- Constants must
        // still expand the search to the whole used range (unlike Precedents/Dependents).
        var a1 = new CellAddress(sheet.Id, 1, 1);
        session.SelectRange(new GridRange(a1, a1));

        var result = session.GoToSpecial(GoToSpecialKind.Constants);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(expected.Count);
        result.MatchCount.Should().BeGreaterThanOrEqualTo(80);
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
