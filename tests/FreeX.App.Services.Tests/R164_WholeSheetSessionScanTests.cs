using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r164 remediation, second pass. The first pass fixed the commands a select-all reaches directly;
/// probing the session layer found five more paths that walked the whole 17,179,869,184-cell
/// selection and never returned: Ctrl+Enter (fill selection), Draw Border, the per-cell border
/// presets, and both Copy and Cut. Each was measured past a 15s budget before the fix and completes
/// in milliseconds after it.
///
/// Same split as the first pass. Copy/Cut narrow an UNBOUNDED selection to the data it covers but
/// leave a bounded one exactly as chosen -- a bounded copy's blank cells are meaningful, since
/// pasting them is what clears the destination. Ctrl+Enter instead takes a cell cap, because it
/// writes into every selected cell: the empty ones are precisely the point.
/// </summary>
public sealed class R164_WholeSheetSessionScanTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static WorkbookSession CreateSession()
    {
        var workbook = new Workbook("R164Session");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("b"));
        return new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
    }

    private static GridRange WholeSheet(SheetId id) =>
        new(new CellAddress(id, 1, 1), new CellAddress(id, CellAddress.MaxRow, CellAddress.MaxCol));

    private static T Within<T>(Func<T> run)
    {
        var task = Task.Run(run);
        task.Wait(Budget).Should().BeTrue("the whole-sheet scan must not hang the UI thread");
        return task.Result;
    }

    [Fact]
    public void FillSelection_WholeSheetSelection_IsRejectedWithACellLimit()
    {
        var session = CreateSession();
        session.SelectRange(WholeSheet(session.ActiveSheet.Id));

        var result = Within(() => session.CommitCellTextAcrossSelection("x"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("too large");
    }

    [Fact]
    public void FillSelection_AnOrdinarySelectionStillFillsEveryCell()
    {
        // Sibling/no-regression: Ctrl+Enter must still write into the EMPTY cells of a normal
        // selection -- that is the whole feature, and why this path takes a cap rather than the
        // populated-cells narrowing the clear/copy paths use.
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        session.SelectRange(new GridRange(new CellAddress(sheetId, 5, 5), new CellAddress(sheetId, 6, 6)));

        var result = session.CommitCellTextAcrossSelection("x");

        result.Success.Should().BeTrue(result.ErrorMessage);
        session.ActiveSheet.GetValue(new CellAddress(sheetId, 6, 6)).Should().Be(new TextValue("x"));
    }

    [Fact]
    public void DrawBorder_WholeSheetSelection_CompletesInsteadOfHanging()
    {
        var session = CreateSession();
        session.SelectRange(WholeSheet(session.ActiveSheet.Id));

        var result = Within(() => session.SetSelectedRangeDrawBorder(BorderDrawMode.Draw));

        result.Should().NotBeNull();
    }

    [Fact]
    public void BorderPreset_WholeSheetSelection_CompletesInsteadOfHanging()
    {
        var session = CreateSession();
        session.SelectRange(WholeSheet(session.ActiveSheet.Id));

        var result = Within(() => session.SetSelectedRangeBorderPreset(CellBorderPreset.All));

        result.Should().NotBeNull();
    }

    [Fact]
    public void Copy_WholeSheetSelection_CopiesTheDataInsteadOfHanging()
    {
        var session = CreateSession();
        session.SelectRange(WholeSheet(session.ActiveSheet.Id));

        var text = Within(session.CopySelectedRangeText);

        text.Should().Contain("1");
        text.Should().Contain("b");
    }

    [Fact]
    public void Cut_WholeSheetSelection_CompletesInsteadOfHanging()
    {
        var session = CreateSession();
        session.SelectRange(WholeSheet(session.ActiveSheet.Id));

        var text = Within(session.CutSelectedRangeText);

        text.Should().Contain("1");
    }

    [Fact]
    public void Copy_ABoundedSelection_StillCarriesItsBlankCells()
    {
        // Sibling/no-regression: only an UNBOUNDED selection is narrowed. A bounded copy must still
        // carry the trailing blank row/column, because pasting those blanks is what clears the
        // destination.
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        session.SelectRange(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)));

        var text = session.CopySelectedRangeText();

        // Three rows of three tab-separated columns: the empty third row/column survive as
        // separators rather than being trimmed away.
        text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Should().HaveCount(3);
        text.Replace("\r\n", "\n").Split('\n')[0].Split('\t').Should().HaveCount(3);
    }
}
